import sys, re, json, bisect
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent / 'python-deps'))
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64

binary = Path(sys.argv[1])
pe = pefile.PE(str(binary))
data = binary.read_bytes()
text = next(s for s in pe.sections if s.Characteristics & 0x20000000)
code, text_rva = text.get_data(), text.VirtualAddress
functions = [(f.struct.BeginAddress, f.struct.EndAddress) for f in pe.DIRECTORY_ENTRY_EXCEPTION]
begins = [f[0] for f in functions]
strings = {}
for match in re.finditer(rb'[ -~]{5,180}', data):
    value = match.group().decode('ascii')
    if any(key in value.lower() for key in ['regalia', 'loadscreen', 'loadingscreen']):
        strings[pe.get_rva_from_offset(match.start())] = value
xrefs = []
for m in re.finditer(rb'[\x48-\x4f][\x8b\x8d]', code):
    offset = m.start()
    if offset + 7 > len(code) or code[offset + 2] & 0xc7 != 0x05:
        continue
    target = text_rva + offset + 7 + int.from_bytes(code[offset+3:offset+7], 'little', signed=True)
    if target in strings:
        instruction = text_rva + offset
        index = bisect.bisect_right(begins, instruction) - 1
        fn = functions[index] if index >= 0 and functions[index][1] > instruction else (max(text_rva, instruction-40), instruction+140)
        xrefs.append({'string': strings[target], 'string_rva': hex(target), 'xref': hex(instruction), 'function': [hex(v) for v in fn]})
result = {'image_base': hex(pe.OPTIONAL_HEADER.ImageBase), 'timestamp': hex(pe.FILE_HEADER.TimeDateStamp), 'references': xrefs}
out = Path(__file__).parent / 'loading-static.json'
out.write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
if len(sys.argv) > 2:
    start = int(sys.argv[2], 0)
    index = bisect.bisect_right(begins, start)-1
    lo, hi = functions[index]
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    for i in md.disasm(pe.get_data(lo, min(hi-lo, 12000)), lo):
        print(f'{i.address:x}: {i.mnemonic} {i.op_str}')
