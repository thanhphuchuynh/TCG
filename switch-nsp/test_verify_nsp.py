import io, struct, unittest, hashlib
from verify_nsp import View, VerifyError, pfs0, crypt_header, nca, romfs

def packed(names, contents):
    strings, rows, offset = b'', b'', 0
    for name, data in zip(names, contents):
        rows += struct.pack('<QQII', offset, len(data), len(strings), 0)
        strings += name.encode() + b'\0'
        offset += len(data)
    return b'PFS0' + struct.pack('<III', len(names), len(strings), 0) + rows + strings + b''.join(contents)

def view(data):
    return View(io.BytesIO(data), 0, len(data))

class VerificationTests(unittest.TestCase):
    def test_valid_pfs0(self):
        self.assertEqual(pfs0(view(packed(['a', 'b'], [b'abc', b'def'])))['b'].read(0, 3), b'def')
    def test_truncated_header(self):
        with self.assertRaises(VerifyError): pfs0(view(b'PFS0' + b'\0' * 7))
    def test_traversal(self):
        for name in ['../x', '/x', 'a/b', 'a\\b', '.', '..']:
            with self.subTest(name=name), self.assertRaises(VerifyError): pfs0(view(packed([name], [b'x'])))
    def test_overlap(self):
        data = bytearray(packed(['a', 'b'], [b'abc', b'def']))
        struct.pack_into('<Q', data, 40, 1)
        with self.assertRaises(VerifyError): pfs0(view(data))
    def test_out_of_bounds(self):
        data = bytearray(packed(['a'], [b'x']))
        struct.pack_into('<Q', data, 24, 999)
        with self.assertRaises(VerifyError): pfs0(view(data))
    def test_header_roundtrip_and_tamper(self):
        key = bytes(range(32)); header = bytearray(0xc00)
        header[0x200:0x204] = b'NCA3'; struct.pack_into('<Q', header, 0x208, 0xc00)
        encrypted = crypt_header(bytes(header), key, encrypt=True)
        self.assertEqual(crypt_header(encrypted, key), header)
        self.assertEqual(nca(view(encrypted), key)['type'], 0)
        bad = bytearray(encrypted); bad[0x200] ^= 1
        with self.assertRaises(VerifyError): nca(view(bad), key)

def empty_romfs():
    header = struct.pack('<10Q', 0x50, 0x50, 4, 0x54, 24, 0x6c, 4, 0x70, 0, 0x70)
    return header + struct.pack('<I6II', 0, 0, 0xffffffff, 0xffffffff, 0xffffffff, 0xffffffff, 0, 0xffffffff)


def nca_for_section(section, fs):
    key = bytes(range(32))
    header = bytearray(0xc00)
    header[0x200:0x204] = b'NCA3'
    section += bytes((-len(section)) % 512)
    struct.pack_into('<Q', header, 0x208, 0xc00+len(section))
    struct.pack_into('<II', header, 0x240, 6, 6+len(section)//512)
    header[0x400:0x600] = fs
    header[0x280:0x2a0] = hashlib.sha256(fs).digest()
    return crypt_header(header, key, encrypt=True)+section, key


class IntegrityTests(unittest.TestCase):
    def test_pfs0_hierarchy_and_tampered_body(self):
        data = packed(['main'], [b'NSO0synthetic'])
        hashes = hashlib.sha256(data).digest()
        fs = bytearray(512)
        fs[:5] = b'\x02\x00\x01\x02\x01'
        fs[8:40] = hashlib.sha256(hashes).digest()
        struct.pack_into('<IIQQQQ', fs, 40, 4096, 2, 0, 32, 512, len(data))
        archive, key = nca_for_section(hashes+bytes(480)+data, fs)
        self.assertEqual(nca(view(archive), key)['sections'][0][1]['main'].read(0,4), b'NSO0')
        changed = bytearray(archive); changed[0xc00+512+len(data)-1] ^= 1
        with self.assertRaisesRegex(VerifyError, 'SHA256'): nca(view(changed), key)

    def test_romfs_bounds_and_hash_chain(self):
        data = empty_romfs()
        self.assertEqual(romfs(view(data)), {})
        changed = bytearray(data); struct.pack_into('<Q', changed, 24, 9999)
        with self.assertRaises(VerifyError): romfs(view(changed))
        changed = bytearray(data); struct.pack_into('<I', changed, 0x50, 1)
        with self.assertRaises(VerifyError): romfs(view(changed))

    def test_ivfc_all_levels_and_tamper(self):
        levels = [empty_romfs()]
        for i in range(5):
            child = levels[0]
            hashes = b''.join(hashlib.sha256(child[o:o+0x4000].ljust(0x4000,b'\0')).digest() for o in range(0,len(child),0x4000))
            levels.insert(0, hashes+bytes((-len(hashes)) % 0x4000))
        fs = bytearray(512); fs[:5] = b'\x02\x00\x00\x03\x01'
        fs[8:12] = b'IVFC'; struct.pack_into('<III',fs,12,0x20000,32,7)
        offset = 0
        for i, level in enumerate(levels):
            struct.pack_into('<QQII',fs,24+i*24,offset,len(level),14,0); offset += len(level)
        fs[0xc8:0xe8] = hashlib.sha256(levels[0]).digest()
        archive,key = nca_for_section(b''.join(levels),fs)
        self.assertEqual(nca(view(archive),key)['sections'], [('romfs',{})])
        changed=bytearray(archive); changed[0xc00+sum(map(len,levels[:-1]))+1] ^= 1
        with self.assertRaisesRegex(VerifyError,'SHA256'): nca(view(changed),key)


if __name__ == '__main__': unittest.main()
