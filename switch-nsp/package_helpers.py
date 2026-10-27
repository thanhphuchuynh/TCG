"""Local-only NSP staging helpers. Key material is never logged or persisted."""
from contextlib import contextmanager
from pathlib import Path
import hashlib,json,os,re,struct,subprocess,tempfile

TITLE_ID='05d76e4fc8d00000'
TITLE='TCG Card Shop Simulator'
AUTHOR='Community compatibility build'

def digest(path):
    h=hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda:stream.read(4*1024*1024),b''):h.update(block)
    return h.hexdigest()

def nro_nacp(path):
    with Path(path).open('rb') as stream:
        header=stream.read(0x80)
        if header[0x10:0x14]!=b'NRO0':raise ValueError('Runtime has no NRO header')
        size=struct.unpack_from('<I',header,0x18)[0];stream.seek(size);assets=stream.read(0x38)
        if assets[:4]!=b'ASET':raise ValueError('Runtime has no NRO asset header')
        offset,length=struct.unpack_from('<QQ',assets,0x18)
        if length!=0x4000:raise ValueError('Unexpected runtime NACP size')
        stream.seek(size+offset);nacp=bytearray(stream.read(length))
        if len(nacp)!=0x4000:raise ValueError('Truncated runtime NACP')
    return nacp

def control_nacp(template,title_id=TITLE_ID):
    data=bytearray(template)
    if len(data)!=0x4000:raise ValueError('NACP must be 16 KiB')
    title=int(title_id,16)
    for i in range(16):
        data[i*0x300:(i+1)*0x300]=bytes(0x300)
        data[i*0x300:i*0x300+len(TITLE.encode())]=TITLE.encode()
        data[i*0x300+0x200:i*0x300+0x200+len(AUTHOR.encode())]=AUTHOR.encode()
    data[0x3025:0x3028]=bytes([0,0,1])
    struct.pack_into('<I',data,0x302c,1) # English matches the provided icon; other console languages fall back.
    data[0x3034:0x3038]=bytes(4) # Screenshots allowed, automatic video capture disabled to preserve memory.
    data[0x3040:0x3060]=b'\xff'*32
    data[0x3060:0x3070]=b'0.1.0'+bytes(11)
    for offset,value in [(0x3038,title),(0x3070,title^0x1000),(0x3078,title),(0x30f8,title)]:struct.pack_into('<Q',data,offset,value)
    for offset in range(0x3080,0x30a8,8):struct.pack_into('<Q',data,offset,0)
    data[0x30a8:0x30b0]=b'tcg'+bytes(5)
    for offset in range(0x30b0,0x30f0,8):struct.pack_into('<Q',data,offset,title)
    data[0x30f0:0x30f2]=bytes([2,0]);data[0x3210:0x3214]=bytes(4)
    for offset in [0x3148,0x3150,0x3158,0x3160]:struct.pack_into('<Q',data,offset,0)
    return bytes(data)

@contextmanager
def private_keyset(source):
    values={}
    for line in Path(source).read_text().splitlines():
        if '=' not in line:continue
        name,value=(p.strip() for p in line.split('=',1));name=name.lower()
        if name in {'header_key','key_area_key_application_00','titlekek_00'}:
            length=64 if name=='header_key' else 32
            if not re.fullmatch('[0-9a-fA-F]{'+str(length)+'}',value):raise ValueError('Packaging key has an invalid format')
            values[name]=value
    if set(values)!={'header_key','key_area_key_application_00','titlekek_00'}:raise ValueError('Required packaging keys are missing')
    # TemporaryFile is unlinked immediately on macOS; the child only sees an inherited descriptor.
    with tempfile.TemporaryFile(mode='w+b') as stream:
        os.fchmod(stream.fileno(),0o600)
        stream.write(''.join(k+' = '+v+'\n' for k,v in values.items()).encode());stream.flush()
        yield stream

def run_packer(packer,key_stream,args,work,log):
    key_stream.seek(0)
    command=[str(packer),'-k','/dev/fd/'+str(key_stream.fileno()),*map(str,args)]
    result=subprocess.run(command,cwd=work,pass_fds=(key_stream.fileno(),),capture_output=True,text=True)
    # Redact all key-sized hex sequences, including any unexpected parser diagnostics.
    clean=re.sub(r'(?i)\b[0-9a-f]{32,}\b','[redacted-hex]',result.stdout+'\n'+result.stderr)
    Path(log).write_text(clean)
    if result.returncode:raise RuntimeError('NSP packer failed; see sanitized '+Path(log).name)

def safe_relative(path):
    parts=Path(path).parts
    if not parts or path.startswith('/') or '\\' in path or ':' in path or any(x in ('..','.') for x in parts) or '\t' in path or '\n' in path:raise ValueError('Unsafe payload path')
    if Path(path).name.lower() in {'prod.keys','title.keys'}:raise ValueError('Private keys cannot enter a game package')
    return path

def prepare_payload(handoff,romfs):
    handoff=Path(handoff);romfs=Path(romfs);payload=romfs/'payload'
    if payload.exists():raise ValueError('Refusing to replace an existing payload stage')
    entries=json.loads((handoff/'Verification/handoff-manifest.json').read_text())['files']
    selected=[]
    for item in entries:
        name=item['path']
        if name.startswith('Runtime/switch/wine/'):relative=name.removeprefix('Runtime/switch/wine/')
        elif name.startswith('Game/TCG_Autorun_Test/'):relative='drive_c/TCG_Autorun_Test/'+name.removeprefix('Game/TCG_Autorun_Test/')
        else:continue
        safe_relative(relative)
        source=handoff/name
        if source.is_symlink() or not source.is_file() or source.stat().st_size!=item['bytes'] or digest(source)!=item['sha256']:raise ValueError('Source handoff integrity failed: '+name)
        if item['bytes']>0xffffffff:raise ValueError('A source file exceeds FAT32 limits')
        selected.append((relative,source,item['bytes'],item['sha256']))
    if not selected:raise ValueError('No audited game/runtime files')
    payload.mkdir(parents=True)
    for relative,source,size,sha in selected:
        target=payload/relative;target.parent.mkdir(parents=True,exist_ok=True)
        # Independent clone on APFS; ordinary copy fallback stays local.
        clone=subprocess.run(['/bin/cp','-c',str(source),str(target)],capture_output=True)
        if clone.returncode:
            import shutil
            shutil.copy2(source,target)
    rows=''.join(sha+'\t'+str(size)+'\t'+name+'\n' for name,source,size,sha in sorted(selected))
    identifier=hashlib.sha256(rows.encode()).hexdigest();total=sum(x[2] for x in selected)
    manifest='TCG-COMPAT-1\t'+identifier+'\t'+str(total)+'\t'+str(len(selected))+'\n'+rows
    (romfs/'manifest.tsv').write_text(manifest)
    return {'id':identifier,'files':len(selected),'bytes':total,'largestFile':max(x[2] for x in selected),'nativeSwitchPort':False,'consoleTested':False,'modsEnabled':False}
