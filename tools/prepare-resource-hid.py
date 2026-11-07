"""Prepare the pinned resource loader from the user-supplied original (requires pefile).
Preserves the WAD backend, adds HID API forwarding, disables its optional ad worker.
No game files, security settings, or game offsets are modified by this build tool.
"""
from pathlib import Path
import struct
import pefile
import argparse
import hashlib
import json
from types import SimpleNamespace

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('source', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
source = args.source
original = source.read_bytes()
if hashlib.sha256(original).hexdigest() != 'e4fbbf9671303712a764d505d9e9a6c5fae7004873899255ab798f759d23e69b':
    raise ValueError('Unexpected original loader SHA256; no output written')
pe = pefile.PE(data=original)
assert not hasattr(pe, 'DIRECTORY_ENTRY_EXPORT')
records = json.loads(Path(__file__).with_name('resource-hid-exports.json').read_text(encoding='utf-8'))
symbols = sorted([SimpleNamespace(name=record['name'].encode('ascii'), ordinal=record['ordinal']) for record in records], key=lambda symbol: symbol.ordinal)
first = min(symbol.ordinal for symbol in symbols)
count = max(symbol.ordinal for symbol in symbols) - first + 1
names = sorted([symbol for symbol in symbols if symbol.name], key=lambda symbol: symbol.name)
align = lambda value, alignment: (value + alignment - 1) // alignment * alignment
section_alignment = pe.OPTIONAL_HEADER.SectionAlignment
file_alignment = pe.OPTIONAL_HEADER.FileAlignment
last = pe.sections[-1]
rva = align(last.VirtualAddress + max(last.Misc_VirtualSize, last.SizeOfRawData), section_alignment)
raw_offset = align(len(original), file_alignment)
new_header = pe.sections[-1].get_file_offset() + 40
assert new_header + 40 <= min(section.PointerToRawData for section in pe.sections)
blob = bytearray(40 + count * 4 + len(names) * 6)
def string(value):
    offset = len(blob)
    blob.extend(value + b'\0')
    return rva + offset
dll_name = string(b'HID.dll')
functions = 40
name_table = functions + count * 4
ordinals = name_table + len(names) * 4
for symbol in symbols:
    assert symbol.name
    target = string(b'sc_resource_hid_system.' + symbol.name)
    struct.pack_into('<I', blob, functions + (symbol.ordinal - first) * 4, target)
for index, symbol in enumerate(names):
    name_rva = string(symbol.name)
    struct.pack_into('<I', blob, name_table + index * 4, name_rva)
    struct.pack_into('<H', blob, ordinals + index * 2, symbol.ordinal - first)
struct.pack_into('<IIHHIIIIIII', blob, 0, 0, 0, 0, 0, dll_name, first, count, len(names), rva + functions, rva + name_table, rva + ordinals)
image = bytearray(original)
image.extend(bytes(raw_offset - len(image)))
image.extend(blob)
image.extend(bytes(align(len(blob), file_alignment) - len(blob)))
struct.pack_into('<8sIIIIIIHHI', image, new_header, b'.hidapi\0', len(blob), rva, align(len(blob), file_alignment), raw_offset, 0, 0, 0, 0, 0x40000040)
struct.pack_into('<H', image, pe.FILE_HEADER.get_field_absolute_offset('NumberOfSections'), len(pe.sections) + 1)
struct.pack_into('<I', image, pe.OPTIONAL_HEADER.get_field_absolute_offset('SizeOfImage'), align(rva + len(blob), section_alignment))
directory = pe.OPTIONAL_HEADER.DATA_DIRECTORY[0].get_file_offset()
struct.pack_into('<II', image, directory, rva, len(blob))
disable_intro = True
if disable_intro:
    # Only the optional post-spawn advertising/chat worker. Its body waits for
    # game time then calls a fixed game chat address with the author-group text.
    intro = pe.get_offset_from_rva(0x7730)
    assert original[intro:intro+5] == bytes.fromhex('40534883ec')
    image[intro:intro+3] = bytes.fromhex('33c0c3')  # DWORD worker(void*) { return 0; }
patched = pefile.PE(data=image)
struct.pack_into('<I', image, patched.OPTIONAL_HEADER.get_field_absolute_offset('CheckSum'), patched.generate_checksum())
text = next(section for section in pe.sections if section.Name.startswith(b'.text'))
comparison = bytearray(image[text.PointerToRawData:text.PointerToRawData + text.SizeOfRawData])
if disable_intro:
    relative = intro - text.PointerToRawData
    comparison[relative:relative+3] = original[intro:intro+3]
assert comparison == original[text.PointerToRawData:text.PointerToRawData + text.SizeOfRawData]
output = args.output
if hashlib.sha256(image).hexdigest() != 'da1b89fb914992d012aebf5557c41274497463a7a5d2cb8ecfacee51f872a3e4':
    raise ValueError('Compatible loader SHA256 differs from pinned build; no output written')
output.parent.mkdir(parents=True, exist_ok=True)
output.write_bytes(image)
check = pefile.PE(str(output))
assert len(check.DIRECTORY_ENTRY_EXPORT.symbols) == len(symbols)
assert all(symbol.forwarder for symbol in check.DIRECTORY_ENTRY_EXPORT.symbols)
print(f'Added {len(symbols)} system API forwarders; optional intro disabled={disable_intro}; mounting code unchanged: {output}')
