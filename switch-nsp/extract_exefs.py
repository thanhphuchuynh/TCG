#!/usr/bin/env python3
"""Extract the two loader ExeFS files from devkitPro's build output."""
from pathlib import Path
import struct,sys
source=Path(sys.argv[1]);destination=Path(sys.argv[2]);data=source.read_bytes()
assert data[:4]==b'PFS0'
count,strings_size=struct.unpack_from('<II',data,4);strings_start=16+24*count;files_start=strings_start+strings_size
assert count<=16 and files_start<=len(data)
found={}
for i in range(count):
 offset,size,name_offset=struct.unpack_from('<QQI',data,16+24*i)
 end=data.index(b'\0',strings_start+name_offset,files_start)
 name=data[strings_start+name_offset:end].decode()
 assert name in {'main','main.npdm'} and name not in found
 assert files_start+offset+size<=len(data)
 found[name]=data[files_start+offset:files_start+offset+size]
assert set(found)=={'main','main.npdm'} and found['main'][:4]==b'NSO0' and found['main.npdm'][:4]==b'META'
destination.mkdir(parents=True,exist_ok=True)
for name,content in found.items():(destination/name).write_bytes(content)
print('Loader ExeFS validated:',{k:len(v) for k,v in found.items()})
