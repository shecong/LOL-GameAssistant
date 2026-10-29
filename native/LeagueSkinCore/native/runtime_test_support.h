#pragma once
static void runtimeTestHeaders(BYTE* image,DWORD size,DWORD timestamp=0x70001234) {
    auto dos=(IMAGE_DOS_HEADER*)image;dos->e_magic=IMAGE_DOS_SIGNATURE;dos->e_lfanew=0x100;
    auto nt=(IMAGE_NT_HEADERS64*)(image+0x100);nt->Signature=IMAGE_NT_SIGNATURE;
    nt->FileHeader.Machine=IMAGE_FILE_MACHINE_AMD64;nt->FileHeader.NumberOfSections=2;
    nt->FileHeader.TimeDateStamp=timestamp;nt->FileHeader.SizeOfOptionalHeader=sizeof(IMAGE_OPTIONAL_HEADER64);
    nt->OptionalHeader.Magic=IMAGE_NT_OPTIONAL_HDR64_MAGIC;nt->OptionalHeader.SizeOfImage=size;
    auto sections=IMAGE_FIRST_SECTION(nt);
    sections[0].VirtualAddress=0x1000;sections[0].Misc.VirtualSize=0x1900000-0x1000;
    sections[0].Characteristics=IMAGE_SCN_MEM_EXECUTE|IMAGE_SCN_MEM_READ;
    sections[1].VirtualAddress=0x1900000;sections[1].Misc.VirtualSize=size-0x1900000;
    sections[1].Characteristics=IMAGE_SCN_MEM_READ|IMAGE_SCN_MEM_WRITE;
}
