"""Read-only external process probe. Never writes or injects into the target."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import json
from pathlib import Path
import struct
import time

k = c.WinDLL('kernel32', use_last_error=True)
k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
k.OpenProcess.restype = w.HANDLE
k.CloseHandle.argtypes = [w.HANDLE]
k.ReadProcessMemory.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_size_t, c.POINTER(c.c_size_t)]
k.ReadProcessMemory.restype = w.BOOL
k.CreateToolhelp32Snapshot.argtypes = [w.DWORD, w.DWORD]
k.CreateToolhelp32Snapshot.restype = w.HANDLE
class Module(c.Structure):
    _fields_ = [('dwSize', w.DWORD), ('th32ModuleID', w.DWORD), ('th32ProcessID', w.DWORD),
                ('GlblcntUsage', w.DWORD), ('ProccntUsage', w.DWORD), ('modBaseAddr', c.c_void_p),
                ('modBaseSize', w.DWORD), ('hModule', w.HMODULE), ('szModule', w.WCHAR * 256),
                ('szExePath', w.WCHAR * 260)]
k.Module32FirstW.argtypes = [w.HANDLE, c.POINTER(Module)]
k.Module32FirstW.restype = w.BOOL
k.Module32NextW.argtypes = [w.HANDLE, c.POINTER(Module)]
k.Module32NextW.restype = w.BOOL

class Reader:
    def __init__(self, pid):
        self.pid = pid
        self.handle = k.OpenProcess(0x410, False, pid)
        if not self.handle:
            raise OSError(c.get_last_error(), 'OpenProcess(read/query) failed')
    def read(self, address, size):
        buf = c.create_string_buffer(size)
        count = c.c_size_t()
        ok = k.ReadProcessMemory(self.handle, address, buf, size, c.byref(count))
        if not ok or count.value != size:
            raise OSError(c.get_last_error(), f'ReadProcessMemory({address:#x}, {size:#x}) read {count.value}')
        return buf.raw
    def pointer(self, address):
        return struct.unpack('<Q', self.read(address, 8))[0]
    def module(self):
        snap = k.CreateToolhelp32Snapshot(0x18, self.pid)
        if snap == c.c_void_p(-1).value:
            return self.module_from_peb()
        try:
            m = Module()
            m.dwSize = c.sizeof(m)
            more = k.Module32FirstW(snap, c.byref(m))
            if not more:
                raise OSError(c.get_last_error(), 'Module32First failed')
            while more:
                if m.szModule.lower() == 'league of legends.exe':
                    return m.modBaseAddr, m.modBaseSize, m.szExePath
                more = k.Module32NextW(snap, c.byref(m))
            raise RuntimeError('Game module not found')
        finally:
            k.CloseHandle(snap)
    def module_from_peb(self):
        class BasicInfo(c.Structure):
            _fields_ = [('reserved1', c.c_void_p), ('peb', c.c_void_p),
                        ('reserved2', c.c_void_p * 2), ('pid', c.c_size_t), ('reserved3', c.c_void_p)]
        nt = c.WinDLL('ntdll')
        nt.NtQueryInformationProcess.argtypes = [w.HANDLE, w.ULONG, c.c_void_p, w.ULONG, c.POINTER(w.ULONG)]
        nt.NtQueryInformationProcess.restype = c.c_long
        info, length = BasicInfo(), w.ULONG()
        status = nt.NtQueryInformationProcess(self.handle, 0, c.byref(info), c.sizeof(info), c.byref(length))
        if status < 0:
            raise OSError(f'Module enumeration denied; basic process query NTSTATUS={status & 0xffffffff:#x}')
        base = self.pointer(info.peb + 0x10)
        header = self.read(base, 4096)
        pe = struct.unpack_from('<I', header, 0x3c)[0]
        return base, struct.unpack_from('<I', header, pe + 24 + 56)[0], '(PEB read)'
    def close(self):
        k.CloseHandle(self.handle)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--pid', type=int, required=True)
    args = ap.parse_args()
    out = Path(__file__).parent
    result = {'pid': args.pid, 'read_only': True, 'observed_at_unix': time.time(), 'verified_cooldown': False}
    reader = None
    try:
        reader = Reader(args.pid)
        result['read_handle_opened'] = True
        base, size, path = reader.module()
        result.update(module_base=hex(base), module_size=size, executable_path=path)
        header = reader.read(base, 4096)
        pe = struct.unpack_from('<I', header, 0x3c)[0]
        timestamp = struct.unpack_from('<I', header, pe + 8)[0]
        image_size = struct.unpack_from('<I', header, pe + 24 + 56)[0]
        profile = json.loads((out.parents[1] / 'native/LeagueSkinCore/game-profile.json').read_text(encoding='utf-8-sig'))['profile']
        result.update(timestamp=timestamp, image_size=image_size)
        if timestamp != profile['timestamp'] or image_size != profile['image_size']:
            raise RuntimeError('Current game does not match existing inspected profile')
        player = reader.pointer(base + profile['player_slot'])
        result['player_address'] = hex(player)
        name = reader.pointer(player + profile['stack_offset'] + 0x18)
        result['player_model'] = reader.read(name, 64).split(b'\0')[0].decode('ascii')
        data = reader.read(player, 0x5000)
        (out / 'local-player.bin').write_bytes(data)
        result.update(player_snapshot_bytes=len(data), success=True)
    except Exception as e:
        result.update(success=False, error=str(e))
    finally:
        if reader:
            reader.close()
    (out / 'access-result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))

if __name__ == '__main__':
    main()
