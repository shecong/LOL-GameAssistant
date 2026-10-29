"""Diagnose normal Windows process access; no protection changes or drivers."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import json
from pathlib import Path
import runpy
import sys

p=argparse.ArgumentParser();p.add_argument('pid',type=int);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
k=c.WinDLL('kernel32',use_last_error=True);adv=c.WinDLL('advapi32',use_last_error=True)
class LUID(c.Structure): _fields_=[('low',w.DWORD),('high',w.LONG)]
class TP(c.Structure): _fields_=[('count',w.DWORD),('luid',LUID),('attributes',w.DWORD)]
k.GetCurrentProcess.restype=w.HANDLE
k.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD];k.OpenProcess.restype=w.HANDLE
k.CloseHandle.argtypes=[w.HANDLE]
adv.OpenProcessToken.argtypes=[w.HANDLE,w.DWORD,c.POINTER(w.HANDLE)]
adv.LookupPrivilegeValueW.argtypes=[w.LPCWSTR,w.LPCWSTR,c.POINTER(LUID)]
adv.AdjustTokenPrivileges.argtypes=[w.HANDLE,w.BOOL,c.POINTER(TP),w.DWORD,c.POINTER(TP),c.POINTER(w.DWORD)]
k.GetProcessInformation.argtypes=[w.HANDLE,c.c_int,c.c_void_p,w.DWORD]
nt=c.WinDLL('ntdll');nt.NtQueryObject.argtypes=[w.HANDLE,w.DWORD,c.c_void_p,w.DWORD,c.POINTER(w.DWORD)]
k.ReadProcessMemory.argtypes=[w.HANDLE,c.c_void_p,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)]
result={'pid':a.pid,'elevated':bool(c.windll.shell32.IsUserAnAdmin()),'access_checks':[],'capture_succeeded':False}
source=c.c_uint64(0x1122334455667788);destination=c.c_uint64();read_count=c.c_size_t()
result['read_api_self_control']=bool(k.ReadProcessMemory(k.GetCurrentProcess(),c.addressof(source),c.byref(destination),8,c.byref(read_count)) and read_count.value==8 and destination.value==source.value)
token=w.HANDLE();previous=TP();restore=False
try:
 if adv.OpenProcessToken(k.GetCurrentProcess(),0x28,c.byref(token)):
  desired=TP();desired.count=1;desired.attributes=2
  if adv.LookupPrivilegeValueW(None,'SeDebugPrivilege',c.byref(desired.luid)):
   returned=w.DWORD();c.set_last_error(0)
   changed=adv.AdjustTokenPrivileges(token,False,c.byref(desired),c.sizeof(previous),c.byref(previous),c.byref(returned))
   error=c.get_last_error();restore=bool(changed and error==0)
   result['debug_privilege']={'adjust_returned':bool(changed),'error':error,'enabled':restore,'previous_attributes':previous.attributes}
  else: result['debug_privilege']={'lookup_error':c.get_last_error()}
 else: result['token_error']=c.get_last_error()
 for mask in (0x1000,0x1010,0x410):
  h=k.OpenProcess(mask,False,a.pid)
  record={'mask':hex(mask),'opened':bool(h),'error':0 if h else c.get_last_error()}
  if h:
   try:
    basic=c.create_string_buffer(56);length=w.DWORD()
    status=nt.NtQueryObject(h,0,basic,c.sizeof(basic),c.byref(length))
    record['handle_query_status']=hex(status & 0xffffffff)
    if status==0:
     access=c.c_uint32.from_buffer(basic,4).value
     record['granted_access']=hex(access);record['vm_read_granted']=bool(access & 0x10)
    level=w.DWORD()
    ok=k.GetProcessInformation(h,7,c.byref(level),c.sizeof(level))
    record['protection_query_ok']=bool(ok)
    record['protection_level']=hex(level.value) if ok else None
    if not ok: record['protection_query_error']=c.get_last_error()
   finally:k.CloseHandle(h)
  result['access_checks'].append(record)
 # Retry the same read-only collector inside this token, without debug attach.
 sys.argv=['capture-skin-update-code.py',str(a.pid),'--output',str(a.output)]
 try:
  runpy.run_path(str(Path(__file__).with_name('capture-skin-update-code.py')),run_name='__main__')
  result['capture_succeeded']=True
 except Exception as e:result['capture_error']=str(e)
finally:
 if token:
  if restore:adv.AdjustTokenPrivileges(token,False,c.byref(previous),0,None,None)
  k.CloseHandle(token)
 a.output.mkdir(parents=True,exist_ok=True)
 (a.output/'access-diagnosis.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps(result,ensure_ascii=True))

