"""Read-only structural probe for a user-supplied Windows installation.

Does not extract code, images, registration information or user file history.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
from pathlib import Path


def pe_metadata(path: Path) -> dict:
    data = path.read_bytes()
    u16 = lambda p: struct.unpack_from('<H', data, p)[0]
    u32 = lambda p: struct.unpack_from('<I', data, p)[0]
    pe = u32(0x3c)
    assert data[pe:pe+4] == b'PE\0\0'
    optional = pe + 24
    section_start = optional + u16(pe+20)
    sections = []
    for i in range(u16(pe+6)):
        p = section_start + i*40
        sections.append({'name': data[p:p+8].split(b'\0')[0].decode('ascii'),
                         'virtual_size': u32(p+8), 'rva': u32(p+12),
                         'raw_size': u32(p+16), 'raw_offset': u32(p+20)})
    def offset(rva):
        for section in sections:
            if section['rva'] <= rva < section['rva'] + max(section['virtual_size'], section['raw_size']):
                return section['raw_offset'] + rva - section['rva']
        return rva
    def asciiz(p):
        return data[p:data.index(b'\0',p)].decode('ascii', errors='replace')
    plus = u16(optional) == 0x20b
    directory = optional + (112 if plus else 96)
    imports = {}
    import_rva = u32(directory+8)
    if import_rva:
        p = offset(import_rva)
        while data[p:p+20] != bytes(20):
            original, _, _, name_rva, first = struct.unpack_from('<IIIII', data, p)
            dll = asciiz(offset(name_rva))
            q = offset(original or first)
            names = []
            while True:
                value = struct.unpack_from('<Q' if plus else '<I', data, q)[0]
                if not value:
                    break
                if value & (1 << (63 if plus else 31)):
                    names.append(f'ordinal:{value & 65535}')
                else:
                    names.append(asciiz(offset(value)+2))
                q += 8 if plus else 4
            imports[dll] = names
            p += 20
    resource_names = []
    resource_rva = u32(directory+16)
    if resource_rva:
        base = offset(resource_rva)
        def walk(relative, labels):
            p = base+relative
            count = u16(p+12)+u16(p+14)
            for i in range(count):
                name, dest = struct.unpack_from('<II',data,p+16+i*8)
                if name & 0x80000000:
                    q = base+(name & 0x7fffffff)
                    label = data[q+2:q+2+2*u16(q)].decode('utf-16le')
                else:
                    label = str(name)
                if dest & 0x80000000:
                    walk(dest & 0x7fffffff, labels+[label])
                else:
                    resource_names.append('/'.join(labels+[label]))
        walk(0, [])
    indicators = sorted(set(x.decode('ascii') for x in re.findall(rb'T[A-Z][A-Za-z0-9]{5,60}',data)
                            if any(k in x for k in (b'Form',b'Window',b'Capture',b'Record',b'Paint',b'Settings',b'HotKey'))))
    return {'file':path.name, 'size':len(data), 'sha256':hashlib.sha256(data).hexdigest(),
            'machine':hex(u16(pe+4)), 'pe32plus':plus, 'sections':sections,
            'imports':imports, 'resource_names':resource_names, 'class_indicators':indicators}


class DfmReader:
    def __init__(self, data):
        self.data = data
        self.pos = 4
    def take(self, count):
        out = self.data[self.pos:self.pos+count]
        self.pos += count
        return out
    def number(self, fmt):
        return struct.unpack(fmt, self.take(struct.calcsize(fmt)))[0]
    def short(self):
        return self.take(self.number('<B')).decode('cp1252', errors='replace')
    def value(self):
        kind = self.number('<B')
        if kind == 0: return None
        if kind == 1:
            values=[]
            while self.data[self.pos] != 0: values.append(self.value())
            self.pos += 1
            return values
        if kind in (2,3,4): return self.number({2:'<b',3:'<h',4:'<i'}[kind])
        if kind == 5: return {'extended_hex':self.take(10).hex()}
        if kind in (6,7): return self.short()
        if kind in (8,9): return kind == 9
        if kind == 10:
            length = self.number('<I'); self.take(length); return {'binary_length':length}
        if kind == 11:
            items=[]
            while True:
                item=self.short()
                if not item: return items
                items.append(item)
        if kind in (12,20): return self.take(self.number('<I')).decode('utf-8' if kind == 20 else 'cp1252', errors='replace')
        if kind == 13: return None
        if kind == 14:
            result=[]
            while self.data[self.pos] != 0:
                assert self.number('<B') == 1
                result.append(self.properties())
            self.pos += 1
            return result
        if kind in (15,16,17,19): return self.number({15:'<f',16:'<q',17:'<d',19:'<q'}[kind])
        if kind in (18,21): return self.take(self.number('<I')*2).decode('utf-16le')
        raise ValueError(f'Unknown value type {kind} at {self.pos-1}')
    def properties(self):
        props={}
        while True:
            name=self.short()
            if not name: return props
            value=self.value()
            # Remove private data before any output or serialization.
            if not re.search(r'registr|license|user|last|recent|history|folder|path|directory|filename|watermark|email|cache|installdate|runcount|exprogram|title|computer|password|passwd|pwd|secret|token|credential|ftp|smtp|proxy|hostname',name,re.I):
                if not isinstance(value,str) or (re.search(r'color$|fontname|saveformat$|prefix$|suffix$|shadowvalue$',name,re.I)
                    and len(value) < 45 and not any(ch in value for ch in '\\/:\r\n')):
                    props[name]=value
    def read(self):
        assert self.data.startswith(b'TPF0')
        cls=self.short()
        name=self.short()
        return {'class':cls,'properties':self.properties()}


def language_forms(path):
    data=path.read_bytes()
    p=12
    forms={}
    def number(fmt='<I'):
        nonlocal p
        value=struct.unpack_from(fmt,data,p)[0]
        p+=struct.calcsize(fmt)
        return value
    def string(wide=False):
        nonlocal p
        size=number()*(2 if wide else 1)
        value=data[p:p+size].decode('utf-16le' if wide else 'cp1252')
        p+=size
        return value
    for _ in range(struct.unpack_from('<I',data,8)[0]):
        name=string()
        next_offset=number() or len(data)
        entries=[]
        # First list contains the component captions/hints. Later lists hold
        # auxiliary text and are deliberately not copied into the evidence.
        for _ in range(number()):
            component=string()
            kind=number('<H')
            source=string()
            translation=string(True)
            entries.append({'component':component,'kind':kind,'source':source,'translation':translation})
        forms[name]=entries
        p=next_offset
    return forms


def language_summary(path):
    forms=language_forms(path)
    keys=sorted(set(entry['source'] for entries in forms.values() for entry in entries
                    if any(k in entry['source'] for k in ('Ctrl+', 'Shift+', 'Alt+', 'Print Screen','PrintScreen'))))
    return {'file':path.name,'bytes':path.stat().st_size,'form_classes':list(forms),
            'component_count':sum(len(entries) for entries in forms.values()),'hotkey_text':keys}


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('installation',type=Path)
    parser.add_argument('--output',type=Path)
    args=parser.parse_args()
    result={'binaries':[pe_metadata(p) for p in sorted(args.installation.glob('*.exe'))],
            'settings':{},'languages':[language_summary(p) for p in sorted((args.installation/'Languages').glob('*.fslang'))]}
    for name in ['fsc.db','fsrec.db','TBSettings.db']:
        try: result['settings'][name]=DfmReader((args.installation/name).read_bytes()).read()
        except Exception as error: result['settings'][name]={'parse_error':str(error)}
    output=json.dumps(result,ensure_ascii=False,indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True,exist_ok=True)
        args.output.write_text(output,encoding='utf-8')
    else:
        print(output)


if __name__ == '__main__': main()
