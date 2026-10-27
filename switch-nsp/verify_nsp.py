#!/usr/bin/env python3
"""Independent bounded parser for hacPack plaintext homebrew NSPs.

Verifies structure and content integrity, never Nintendo signature authenticity.
Only header_key is read from the supplied keys file; no keys enter reports.
"""
import argparse
import hashlib
import json
import re
import struct
from pathlib import Path
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

class VerifyError(Exception):
    pass

def require(condition, message):
    if not condition:
        raise VerifyError(message)

class View:
    def __init__(self, file, offset, size):
        self.file, self.offset, self.size = file, offset, size
    def sub(self, offset, size):
        require(offset >= 0 and size >= 0 and offset <= self.size and size <= self.size-offset, 'Range exceeds container')
        return View(self.file, self.offset+offset, size)
    def read(self, offset, size):
        region = self.sub(offset, size)
        self.file.seek(region.offset)
        result = self.file.read(size)
        require(len(result) == size, 'Truncated file')
        return result
    def digest(self):
        h = hashlib.sha256()
        for offset in range(0, self.size, 1024*1024):
            h.update(self.read(offset, min(1024*1024, self.size-offset)))
        return h.digest()

def unpack(fmt, data, offset=0):
    require(offset >= 0 and offset+struct.calcsize(fmt) <= len(data), 'Truncated structure')
    return struct.unpack_from(fmt, data, offset)

def safe_name(name):
    require(name and name not in ('.', '..') and not any(x in name for x in ('/', '\\', '\0')) and not any(ord(x)<32 for x in name), 'Unsafe filesystem name')

def nonoverlap(regions):
    end = 0
    for start, size in sorted(regions):
        require(start >= end, 'Overlapping ranges')
        end = start+size

def pfs0(v):
    head = v.read(0, 16)
    require(head[:4] == b'PFS0', 'Invalid PFS0 magic')
    count, ssize, reserved = unpack('<III', head, 4)
    require(count <= 100000 and ssize <= 16*1024*1024 and reserved == 0, 'Invalid PFS0 header')
    table_end = 16+count*24
    v.sub(0, table_end+ssize)
    strings = v.read(table_end, ssize)
    result, regions = {}, []
    for i in range(count):
        offset, size, name_offset, reserved = unpack('<QQII', v.read(16+i*24, 24))
        require(reserved == 0 and name_offset < ssize, 'Invalid PFS0 entry')
        end = strings.find(b'\0', name_offset)
        require(end >= 0, 'Unterminated PFS0 name')
        name = strings[name_offset:end].decode('utf-8')
        safe_name(name)
        require(name not in result, 'Duplicate PFS0 filename')
        result[name] = v.sub(table_end+ssize+offset, size)
        regions.append((offset, size))
    nonoverlap(regions)
    return result

def crypt_header(data, key, encrypt=False):
    require(len(data) == 0xc00 and len(key) == 32, 'Invalid NCA header/key size')
    result = bytearray()
    for i in range(6):
        cipher = Cipher(algorithms.AES(key), modes.XTS(i.to_bytes(16, 'big')))
        operation = cipher.encryptor() if encrypt else cipher.decryptor()
        result += operation.update(data[i*512:(i+1)*512])+operation.finalize()
    return bytes(result)

def check_hashes(parent, child, block_size, pad_final=False):
    require(0 < block_size <= 16*1024*1024, 'Invalid hash block size')
    count = (child.size+block_size-1)//block_size
    require(parent.size >= count*32, 'Hash table too small')
    for i in range(count):
        block = child.sub(i*block_size, min(block_size, child.size-i*block_size))
        actual = hashlib.sha256(block.read(0, block.size).ljust(block_size, b'\0')).digest() if pad_final else block.digest()
        require(actual == parent.read(i*32, 32), 'Hierarchical SHA256 mismatch')

def romfs(v):
    fields = unpack('<10Q', v.read(0, 0x50))
    require(fields[0] == 0x50, 'Invalid RomFS header')
    dh, dhs, dt, dts, fh, fhs, ft, fts, data = fields[1:]
    require(data <= v.size, 'RomFS data outside container')
    regions = [(0, 0x50), (dh, dhs), (dt, dts), (fh, fhs), (ft, fts)]
    for o, s in regions:
        v.sub(o, s)
        
    nonoverlap(regions)
    dirs, files = v.sub(dt, dts), v.sub(ft, fts)
    result, seen_dirs, seen_files, used_dirs, used_files, payload_regions = {}, set(), set(), [], [], []
    def visit_dir(offset, path, expected_parent):
        require(offset not in seen_dirs, 'RomFS directory cycle')
        seen_dirs.add(offset)
        parent, sibling, child, file, hashed, length = unpack('<6I', dirs.read(offset, 24))
        require(parent == expected_parent, 'RomFS directory parent mismatch')
        name = dirs.read(offset+24, length).decode('utf-8')
        used_dirs.append((offset, (24+length+3)//4*4))
        if path is None:
            require(offset == 0 and not name and sibling == 0xffffffff, 'Invalid RomFS root')
            path = ''
        else:
            safe_name(name)
            path += '/' + name
        current = file
        while current != 0xffffffff:
            require(current not in seen_files, 'RomFS file cycle')
            seen_files.add(current)
            fp, fs, fo, size, hashed, length = unpack('<IIQQII', files.read(current, 32))
            require(fp == offset, 'RomFS file parent mismatch')
            name = files.read(current+32, length).decode('utf-8'); safe_name(name)
            full = path+'/'+name
            require(full not in result, 'Duplicate RomFS path')
            result[full] = v.sub(data+fo, size)
            used_files.append((current, (32+length+3)//4*4))
            payload_regions.append((fo, size))
            current = fs
        current = child
        while current != 0xffffffff:
            next_sibling = unpack('<I', dirs.read(current+4, 4))[0]
            visit_dir(current, path, offset)
            current = next_sibling
    visit_dir(0, None, 0)
    nonoverlap(used_dirs); nonoverlap(used_files); nonoverlap(payload_regions)
    nonoverlap(regions + [(data+o,s) for o,s in payload_regions])
    # Validate hash-chain pointers and entries, including otherwise-unreachable metadata.
    for table, entries, seen, minimum in ((v.sub(dh,dhs),dirs,seen_dirs,24), (v.sub(fh,fhs),files,seen_files,32)):
        require(table.size % 4 == 0, 'Invalid RomFS hash table')
        hashed_entries = set()
        for i in range(0, table.size, 4):
            offset = unpack('<I', table.read(i,4))[0]
            chain = set()
            while offset != 0xffffffff:
                require(offset in seen and offset not in chain and offset not in hashed_entries, 'Invalid RomFS hash chain')
                chain.add(offset); hashed_entries.add(offset)
                offset = unpack('<I', entries.read(offset+minimum-8,4))[0]
        require(hashed_entries == seen, 'RomFS metadata missing from hash table')
    return result

def nca(v, key):
    header = crypt_header(v.read(0,0xc00),key)
    require(header[0x200:0x204] == b'NCA3', 'NCA3 header decrypt/magic failed')
    kind = header[0x205]
    size, title = unpack('<QQ',header,0x208)
    require(size == v.size and kind <= 5, 'Invalid NCA declared size/type')
    require(not any(header[0x230:0x240]), 'Rights-managed NCA unsupported')
    sections, ranges = [], []
    for i in range(4):
        start, end = unpack('<II',header,0x240+16*i)
        if start == end == 0: continue
        require(start >= 6 and end > start, 'Invalid NCA section range')
        section = v.sub(start*512,(end-start)*512); ranges.append((start*512,section.size))
        fs = header[0x400+i*512:0x600+i*512]
        require(hashlib.sha256(fs).digest() == header[0x280+i*32:0x2a0+i*32], 'NCA filesystem header hash mismatch')
        require(fs[4] == 1, 'Only plaintext body NCAs supported')
        require(unpack('<H',fs)[0] == 2, 'Unsupported NCA filesystem version')
        if fs[2:4] == b'\x01\x02':
            block, levels, ho, hs, po, ps = unpack('<IIQQQQ',fs,0x28)
            require(levels == 2, 'Invalid PFS0 hash levels')
            hashes, data = section.sub(ho,hs), section.sub(po,ps)
            nonoverlap([(ho,hs),(po,ps)])
            require(hashes.digest() == fs[8:40], 'PFS0 master hash mismatch')
            require(hs == ((ps+block-1)//block)*32 if block else False, 'PFS0 hash table size mismatch')
            check_hashes(hashes,data,block)
            sections.append(('pfs0',pfs0(data)))
        elif fs[2:4] == b'\x00\x03':
            require(fs[8:12] == b'IVFC', 'Invalid IVFC magic')
            ident, master_size, levels = unpack('<III',fs,12)
            require(ident == 0x20000 and master_size == 32 and levels == 7, 'Unsupported IVFC geometry')
            views, blocks, level_ranges = [], [], []
            for j in range(6):
                offset, length, power, reserved = unpack('<QQII',fs,24+j*24)
                require(5 <= power <= 24 and reserved == 0, 'Invalid IVFC level')
                views.append(section.sub(offset,length)); blocks.append(1<<power); level_ranges.append((offset,length))
            nonoverlap(level_ranges)
            require(views[0].digest() == fs[0xc8:0xe8], 'IVFC master hash mismatch')
            for j in range(1,6): check_hashes(views[j-1],views[j],blocks[j],pad_final=True)
            sections.append(('romfs',romfs(views[-1])))
        else:
            raise VerifyError('Unsupported NCA filesystem/hash type')
    nonoverlap(ranges)
    return {'type':kind,'title_id':f'{title:016x}','sections':sections,'sha256':v.digest().hex(),'size':v.size}

def read_key(path):
    # Do not include lines or values in errors or reports.
    for line in Path(path).read_text().splitlines():
        match = re.fullmatch(r'\s*header_key\s*=\s*([0-9a-fA-F]{64})\s*(?:#.*)?',line)
        if match: return bytes.fromhex(match[1])
    raise VerifyError('No valid header_key found in supplied keys file')

def verify(nsp_path, key, manifest_path=None, expected_name='TCG Card Shop Simulator', expected_title_id=None):
    with open(nsp_path,'rb') as f:
        size = f.seek(0,2); root = View(f,0,size)
        entries = pfs0(root)
        require(len(entries) == 3 and all(re.fullmatch(r'[0-9a-f]{32}(?:\.cnmt)?\.nca',name) for name in entries), 'Expected exactly program, control, and meta NCAs')
        archives = {}
        for name, entry in entries.items():
            archive = nca(entry,key)
            require(name[:32] == archive['sha256'][:32], 'NCA content ID filename mismatch')
            require(archive['type'] not in archives,'Duplicate NCA content type')
            archives[archive['type']] = archive
        require(set(archives) == {0,1,2}, 'Missing program/control/meta NCA')
        titles = {a['title_id'] for a in archives.values()}
        require(len(titles)==1,'NCA title IDs differ')
        title = titles.pop()
        require(expected_title_id is None or title == expected_title_id.lower(), 'Unexpected application title ID')
        def fsfiles(kind, fs):
            result = {}
            for actual, files in archives[kind]['sections']:
                if actual == fs:
                    require(not set(result).intersection(files),'Duplicate files across sections'); result.update(files)
            return result
        exefs = fsfiles(0,'pfs0')
        require('main' in exefs and 'main.npdm' in exefs,'Program ExeFS missing main or NPDM')
        require(exefs['main'].read(0,4)==b'NSO0','Program main is not NSO0')
        nso = exefs['main']; nh = nso.read(0,0x100)
        flags = unpack('<I',nh,0xc)[0]
        file_regions, memory_regions = [], []
        for segment in range(3):
            offset, memory, unpacked = unpack('<III',nh,0x10+segment*0x10)
            stored = unpack('<I',nh,0x60+segment*4)[0] if flags & (1<<segment) else unpacked
            require(offset >= 0x100 and stored > 0 and unpacked > 0, 'Invalid NSO segment')
            segment_view = nso.sub(offset,stored)
            file_regions.append((offset,stored)); memory_regions.append((memory,unpacked))
            if not flags & (1<<segment) and flags & (1<<(segment+3)):
                require(segment_view.digest() == nh[0xa0+segment*32:0xc0+segment*32], 'NSO segment hash mismatch')
        nonoverlap(file_regions); nonoverlap(memory_regions)
        npdm = exefs['main.npdm']; require(npdm.read(0,4)==b'META','Invalid NPDM META')
        ao, az, acid, acid_size = unpack('<4I',npdm.read(0x70,16))
        aci=npdm.sub(ao,az); acd=npdm.sub(acid,acid_size)
        require(aci.read(0,4)==b'ACI0' and f'{unpack("<Q",aci.read(0x10,8))[0]:016x}'==title,'NPDM ACI0 title mismatch')
        require(acd.read(0x200,4)==b'ACID','Invalid NPDM ACID')
        low, high = unpack('<QQ',acd.read(0x210,16)); require(low <= int(title,16) <= high,'NPDM ACID title range mismatch')
        for label,region,field in [('ACI0',aci,0x30),('ACID',acd,0x230)]:
            ko,ks=unpack('<II',region.read(field,8))
            require(ks % 4 == 0,'Misaligned NPDM capabilities')
            caps=region.sub(ko,ks)
            types=[value>>14 & 7 for value in unpack('<'+'I'*(ks//4),caps.read(0,ks)) if value & 0x3fff == 0x1fff]
            require(types == [1],label+' must declare application program type, not applet')

        control=fsfiles(2,'romfs'); require('/control.nacp' in control,'Missing control.nacp')
        nacp=control['/control.nacp']; require(nacp.size>=0x4000,'Truncated control NACP')
        name=nacp.read(0,0x200).split(b'\0',1)[0].decode('utf-8')
        require(name==expected_name,'Unexpected control NACP name')
        author=nacp.read(0x200,0x100).split(b'\0',1)[0].decode('utf-8')
        require(author=='Community compatibility build','Unexpected control NACP author')
        meta=fsfiles(1,'pfs0'); require(len(meta)==1,'Expected one CNMT')
        cnmt=next(iter(meta.values())); head=cnmt.read(0,32)
        tid,version,ctype,unused,ext,count,metacount=unpack('<QIBBHHH',head)
        require(f'{tid:016x}'==title and ctype==0x80 and count==2 and metacount==0,'Invalid application CNMT header')
        require(cnmt.size>=32+ext+count*56+32,'Truncated CNMT records/digest')
        cnmt_digest=cnmt.read(cnmt.size-32,32)
        require(cnmt_digest==bytes(32) or cnmt.sub(0,cnmt.size-32).digest()==cnmt_digest,'CNMT digest mismatch')
        found=set()
        for i in range(count):
            row=cnmt.read(32+ext+i*56,56); kind=row[54]
            require(kind in (1,3) and kind not in found,'Invalid CNMT content type'); found.add(kind)
            a=archives[{1:0,3:2}[kind]]
            require(row[:32].hex()==a['sha256'] and row[32:48].hex()==a['sha256'][:32] and int.from_bytes(row[48:54],'little')==a['size'],'CNMT content hash/ID/size mismatch')
        payload=fsfiles(0,'romfs')
        manifest_result=None
        if manifest_path:
            raw=Path(manifest_path).read_bytes()
            require('/manifest.tsv' in payload and payload['/manifest.tsv'].size==len(raw) and payload['/manifest.tsv'].read(0,len(raw))==raw,'Embedded manifest differs')
            lines=raw.decode('utf-8').splitlines(); hdr=lines[0].split('\t')
            require(len(hdr)==4 and hdr[0]=='TCG-COMPAT-1' and re.fullmatch('[0-9a-f]{64}',hdr[1]),'Invalid manifest header')
            seen=set(); total=0
            for line in lines[1:]:
                row=line.split('\t'); require(len(row)==3 and re.fullmatch('[0-9a-f]{64}',row[0]),'Invalid manifest row')
                digest,length,path=row; require(not path.startswith('/') and all(part not in ('','.', '..') for part in path.split('/')) and '\\' not in path,'Unsafe manifest path')
                require(path not in seen,'Duplicate manifest path'); seen.add(path)
                embedded=payload.get('/payload/'+path); require(embedded is not None,'Manifest payload file missing')
                require(embedded.size==int(length) and embedded.digest().hex()==digest,'Payload manifest size/hash mismatch'); total+=embedded.size
            require(total==int(hdr[2]) and len(seen)==int(hdr[3]),'Manifest totals mismatch')
            require({p for p in payload if p.startswith('/payload/')}=={'/payload/'+p for p in seen},'Unlisted payload files')
            manifest_result={'id':hdr[1],'files':len(seen),'bytes':total,'all_payload_sha256_verified':True}
        return {'status':'passed','title_id':title,'nsp_bytes':size,'nsp_sha256':root.digest().hex(),'control_name':name,'control_author':author,'cnmt_digest':('omitted (hacPack default)' if cnmt_digest==bytes(32) else 'verified'),'ncas':[{k:v for k,v in a.items() if k!='sections'} for a in archives.values()],'payload_manifest':manifest_result,'signature_authenticity':'not verified; unsigned homebrew expected','console_execution':'not tested'}

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--nsp',required=True); parser.add_argument('--keys',required=True); parser.add_argument('--report',required=True); parser.add_argument('--manifest'); parser.add_argument('--expected-name',default='TCG Card Shop Simulator'); parser.add_argument('--expected-title-id')
    args=parser.parse_args()
    try:
        report=verify(args.nsp,read_key(args.keys),args.manifest,args.expected_name,args.expected_title_id); status=0
    except (VerifyError,ValueError,UnicodeError,OSError,RecursionError,struct.error) as error:
        # File errors deliberately exclude path/value details (in particular key paths).
        message=str(error) if isinstance(error,VerifyError) else 'Input could not be parsed/read'
        report={'status':'failed','error':message,'signature_authenticity':'not verified'}; status=1
    Path(args.report).write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report,indent=2))
    return status

if __name__=='__main__': raise SystemExit(main())
