from pathlib import Path
p=Path('tmp/skin-adaptation/native/standalone_probe.h')
s=p.read_text()
s=s.replace('uintptr_t at=(uintptr_t)base,limit=at+nt->OptionalHeader.SizeOfImage;', '''HANDLE dump=CreateFileW(L"E:\\\\code\\\\weather-app\\\\tmp\\\\skin-adaptation\\\\runtime-code.bin",GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    DWORD written=0;if(dump!=INVALID_HANDLE_VALUE)WriteFile(dump,base,4096,&written,nullptr);
    uintptr_t at=(uintptr_t)base,limit=at+nt->OptionalHeader.SizeOfImage;''')
s=s.replace('size_t length=end-at;', '''size_t length=end-at;
            if(dump!=INVALID_HANDLE_VALUE){SetFilePointer(dump,(LONG)(at-(uintptr_t)base),nullptr,FILE_BEGIN);WriteFile(dump,(void*)at,(DWORD)length,&written,nullptr);}''')
s=s.replace('Writer writer{request->result', 'if(dump!=INVALID_HANDLE_VALUE)CloseHandle(dump);\n    Writer writer{request->result')
p.write_text(s)
