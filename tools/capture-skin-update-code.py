"""Read only the main game's executable section; no heap, writes or debugger."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
from pathlib import Path
import struct

p = argparse.ArgumentParser()
p.add_argument('pid', type=int)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
k = c.WinDLL('kernel32', use_last_error=True)
ps = c.WinDLL('psapi', use_last_error=True)
k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]; k.OpenProcess.restype = w.HANDLE
k.CloseHandle.argtypes = [w.HANDLE]
k.ReadProcessMemory.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_size_t, c.POINTER(c.c_size_t)]
k.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, c.POINTER(w.DWORD)]
k.GetProcessTimes.argtypes = [w.HANDLE] + [c.POINTER(w.FILETIME)] * 4
k.GetExitCodeProcess.argtypes = [w.HANDLE, c.POINTER(w.DWORD)]
ps.EnumProcessModulesEx.argtypes = [w.HANDLE, c.c_void_p, w.DWORD, c.POINTER(w.DWORD), w.DWORD]
h = k.OpenProcess(0x410, False, a.pid)
if not h: raise c.WinError(c.get_last_error())
try:
    def identity():
        text = c.create_unicode_buffer(32768); size = w.DWORD(len(text))
        if not k.QueryFullProcessImageNameW(h, 0, text, c.byref(size)): raise c.WinError(c.get_last_error())
        times = [w.FILETIME() for _ in range(4)]
        if not k.GetProcessTimes(h, *[c.byref(t) for t in times]): raise c.WinError(c.get_last_error())
        exit_code = w.DWORD()
        if not k.GetExitCodeProcess(h, c.byref(exit_code)) or exit_code.value != 259: raise RuntimeError('process_exited')
        return {'path': text.value, 'creation_filetime': times[0].dwLowDateTime | times[0].dwHighDateTime << 32}
    before = identity()
    if Path(before['path']).name.lower() != 'league of legends.exe': raise RuntimeError('wrong_process')
    def read(address, size):
        buf = c.create_string_buffer(size); count = c.c_size_t()
        if not k.ReadProcessMemory(h, address, buf, size, c.byref(count)) or count.value != size: raise RuntimeError(f'incomplete_read:{address:x}:{count.value}:{c.get_last_error()}')
        return buf.raw
    modules = (c.c_void_p * 2048)(); needed = w.DWORD()
    if ps.EnumProcessModulesEx(h, modules, c.sizeof(modules), c.byref(needed), 3):
        base = modules[0]
    else:
        # Standard read-only process metadata fallback; retain the same handle
        # and access rights, and stop if either metadata or memory is denied.
        ntapi = c.WinDLL('ntdll')
        ntapi.NtQueryInformationProcess.argtypes = [w.HANDLE, w.DWORD, c.c_void_p, w.DWORD, c.POINTER(w.DWORD)]
        basic = (c.c_uint64 * 6)(); returned = w.DWORD()
        status = ntapi.NtQueryInformationProcess(h, 0, c.byref(basic), c.sizeof(basic), c.byref(returned))
        if status != 0: raise RuntimeError(f'process_metadata_denied:{status & 0xffffffff:08x}')
        base = struct.unpack('<Q', read(basic[1]+0x10, 8))[0]
    header = read(base, 4096)
    nt = struct.unpack_from('<I', header, 0x3c)[0]
    if header[:2] != b'MZ' or header[nt:nt+4] != b'PE\0\0': raise RuntimeError('invalid_image')
    machine, sections, timestamp = struct.unpack_from('<HHI', header, nt+4)
    if machine != 0x8664: raise RuntimeError('not_x64')
    optional_size = struct.unpack_from('<H', header, nt+20)[0]
    image_size = struct.unpack_from('<I', header, nt+24+56)[0]
    executable = []
    for n in range(sections):
        off = nt+24+optional_size+n*40
        virtual_size, rva = struct.unpack_from('<II', header, off+8)
        flags = struct.unpack_from('<I', header, off+36)[0]
        if flags & 0x20000000: executable.append((rva, virtual_size))
    rva, size = executable[0]
    if size <= 0 or size > 64*1024*1024 or rva+size > image_size: raise RuntimeError('invalid_section')
    data = bytearray()
    for offset in range(0, size, 65536): data.extend(read(base+rva+offset, min(65536, size-offset)))
    after = identity()
    if before != after: raise RuntimeError('identity_changed')
    a.output.mkdir(parents=True, exist_ok=True)
    (a.output/'game-code.bin').write_bytes(data)
    manifest = {**before, 'pid': a.pid, 'base': base, 'code_rva': rva, 'code_size': size, 'timestamp': timestamp,
        'image_size': image_size, 'code_sha256': hashlib.sha256(data).hexdigest(), 'complete': True, 'read_only': True,
        'game_file_sha256': hashlib.sha256(Path(before['path']).read_bytes()).hexdigest()}
    (a.output/'capture.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(manifest, ensure_ascii=True))
finally:
    k.CloseHandle(h)
