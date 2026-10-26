FUNCTION 0x413260
413260: push rbx
413262: push rsi
413263: push rdi
413264: push r12
413266: push r13
413268: push r14
41326a: push r15
41326c: sub rsp, 0x160
413273: mov rax, qword ptr [rip + 0x185be86]
41327a: xor rax, rsp
41327d: mov qword ptr [rsp + 0x150], rax
413285: mov r13d, r9d
413288: mov dword ptr [rsp + 0x2c], r8d
41328d: mov esi, r8d
413290: mov dword ptr [rsp + 0x20], r8d
413295: mov r12, rdx
413298: mov r15, rcx
41329b: call 0x495fc0
4132a0: mov r10, qword ptr [rip + 0x1880021]
4132a7: mov r8d, dword ptr [rax + 0x38]
4132ab: mov rbx, qword ptr [rax + 0x30]
4132af: mov r11d, r8d
4132b2: add r11, r11
4132b5: mov r9, rbx
4132b8: test r8, r8
4132bb: je 0x4132f6
4132bd: nop dword ptr [rax]
4132c0: mov rax, r8
4132c3: cqo 
4132c5: sub rax, rdx
4132c8: sar rax, 1
4132cb: mov rdx, rax
4132ce: add rdx, rdx
4132d1: cmp dword ptr [r9 + rdx*8], r10d
4132d5: jae 0x4132ee
4132d7: lea r9, [r9 + rdx*8]
4132db: mov rcx, 0xffffffffffffffff
4132e2: add r9, 0x10
4132e6: sub rcx, rax
4132e9: add r8, rcx
4132ec: jmp 0x4132f1
4132ee: mov r8, rax
4132f1: test r8, r8
4132f4: jg 0x4132c0
4132f6: lea rax, [rbx + r11*8]
4132fa: cmp r9, rax
4132fd: je 0x41330a
4132ff: cmp r10d, dword ptr [r9]
413302: jb 0x41330a
413304: mov rdi, qword ptr [r9 + 8]
413308: jmp 0x41330c
41330a: xor edi, edi
41330c: mov rbx, qword ptr [rip + 0x1871f35]
413313: lea rcx, [rip + 0x145e606]
41331a: call 0xf70ad0
41331f: mov rdx, rax
413322: lea rcx, [rip + 0x145e5f7]
413329: call 0xf69970
41332e: xor r8d, r8d
413331: mov dword ptr [rsp + 0x28], eax
413335: lea rdx, [rsp + 0x28]
41333a: mov rcx, rbx
41333d: call 0xf66970
413342: test rax, rax
413345: je 0x41335f
413347: mov rdx, qword ptr [rax]
41334a: lea r8, [rip + 0x188023f]
413351: mov rcx, qword ptr [rax + 8]
413355: call 0xf61f00
41335a: mov r14, rax
41335d: jmp 0x413362
41335f: xor r14d, r14d
413362: test rdi, rdi
413365: je 0x41337a
413367: lea rcx, [rdi + 8]
41336b: lea rdx, [rip + 0x188021e]
413372: call 0xf72e80
413377: mov r14, rax
41337a: test r14, r14
41337d: je 0x4135c1
413383: mov ebx, dword ptr [r14 + 8]
413387: mov rdi, qword ptr [r14]
41338a: test rbx, rbx
41338d: je 0x4133ef
FUNCTION 0x412b0d
412b0d: mov qword ptr [rsp + 0x38], rbx
412b12: lea rcx, [rip + 0x145ed17]
412b19: mov rbx, qword ptr [rip + 0x1872728]
412b20: mov qword ptr [rsp + 0x20], rdi
412b25: call 0xf70ad0
412b2a: mov rdx, rax
412b2d: lea rcx, [rip + 0x145ecfc]
412b34: call 0xf69970
412b39: xor r8d, r8d
412b3c: mov dword ptr [rsp + 0x30], eax
412b40: lea rdx, [rsp + 0x30]
412b45: mov rcx, rbx
412b48: call 0xf66970
412b4d: xor edi, edi
412b4f: test rax, rax
412b52: je 0x412b69
412b54: mov rdx, qword ptr [rax]
412b57: lea r8, [rip + 0x1880ac2]
412b5e: mov rcx, qword ptr [rax + 8]
412b62: call 0xf61f00
412b67: jmp 0x412b6c
412b69: mov rax, rdi
412b6c: mov rbx, qword ptr [rip + 0x18726d5]
412b73: lea rcx, [rip + 0x145ecee]
412b7a: mov qword ptr [rip + 0x188069f], rax
412b81: call 0xf70ad0
412b86: mov rdx, rax
412b89: lea rcx, [rip + 0x145ecd8]
412b90: call 0xf69970
412b95: xor r8d, r8d
412b98: mov dword ptr [rsp + 0x30], eax
412b9c: lea rdx, [rsp + 0x30]
412ba1: mov rcx, rbx
412ba4: call 0xf66970
412ba9: test rax, rax
412bac: je 0x412bc3
412bae: mov rdx, qword ptr [rax]
412bb1: lea r8, [rip + 0x1880af8]
412bb8: mov rcx, qword ptr [rax + 8]
412bbc: call 0xf61f00
412bc1: jmp 0x412bc6
412bc3: mov rax, rdi
412bc6: mov rbx, qword ptr [rip + 0x187267b]
412bcd: lea rcx, [rip + 0x145eccc]
412bd4: mov qword ptr [rip + 0x188064d], rax
412bdb: call 0xf70ad0
412be0: mov rdx, rax
412be3: lea rcx, [rip + 0x145ecb6]
412bea: call 0xf69970
412bef: xor r8d, r8d
412bf2: mov dword ptr [rsp + 0x30], eax
412bf6: lea rdx, [rsp + 0x30]
412bfb: mov rcx, rbx
412bfe: call 0xf66970
412c03: test rax, rax
412c06: je 0x412c1d
412c08: mov rdx, qword ptr [rax]
412c0b: lea r8, [rip + 0x1880a9e]
412c12: mov rcx, qword ptr [rax + 8]
412c16: call 0xf61f00
412c1b: jmp 0x412c20
412c1d: mov rax, rdi
412c20: mov rbx, qword ptr [rip + 0x1872621]
412c27: lea rcx, [rip + 0x145ecaa]
412c2e: mov qword ptr [rip + 0x18805fb], rax
412c35: call 0xf70ad0
412c3a: mov rdx, rax
412c3d: lea rcx, [rip + 0x145ec94]
412c44: call 0xf69970
412c49: xor r8d, r8d
412c4c: mov dword ptr [rsp + 0x30], eax
412c50: lea rdx, [rsp + 0x30]
412c55: mov rcx, rbx
412c58: call 0xf66970
412c5d: mov rbx, qword ptr [rsp + 0x38]
412c62: test rax, rax
412c65: je 0x412c7d
FUNCTION 0x4133ef
4133ef: mov eax, dword ptr [r14 + 8]
4133f3: imul rdx, rax, 0x38
4133f7: add rdx, qword ptr [r14]
4133fa: cmp rdi, rdx
4133fd: je 0x413563
413403: mov rdx, qword ptr [rdi]
413406: mov rcx, r12
413409: call 0xf620e0
41340e: shr eax, 0x1f
413411: test al, al
413413: jne 0x413563
413419: mov eax, dword ptr [r14 + 8]
41341d: imul rcx, rax, 0x38
413421: add rcx, qword ptr [r14]
413424: cmp rdi, rcx
413427: je 0x413563
41342d: mov rbx, qword ptr [rdi + 0x10]
413431: mov rax, qword ptr [rbx + 0x98]
413438: test rax, rax
41343b: je 0x413440
41343d: inc dword ptr [rax + 0x1c]
413440: mov rcx, qword ptr [r15 + 0x20]
413444: test rcx, rcx
413447: je 0x41344e
413449: call 0x11050c0
41344e: mov rax, qword ptr [rbx + 0x98]
413455: lea rdx, [rsp + 0x20]
41345a: lea rcx, [rdi + 0x18]
41345e: mov qword ptr [r15 + 0x20], rax
413462: call 0x3c17b0
413467: mov ecx, dword ptr [rdi + 0x20]
41346a: shl rcx, 4
41346e: add rcx, qword ptr [rdi + 0x18]
413472: cmp rax, rcx
413475: je 0x4134a3
413477: mov rbx, qword ptr [rax + 8]
41347b: mov rax, qword ptr [rbx + 0x98]
413482: test rax, rax
413485: je 0x41348a
413487: inc dword ptr [rax + 0x1c]
41348a: mov rcx, qword ptr [r15 + 0x28]
41348e: test rcx, rcx
413491: je 0x413498
413493: call 0x11050c0
413498: mov rax, qword ptr [rbx + 0x98]
41349f: mov qword ptr [r15 + 0x28], rax
4134a3: cmp r13d, 1
4134a7: jl 0x4134e3
4134a9: lea rdx, [rsp + 0x20]
4134ae: mov dword ptr [rsp + 0x20], 1
4134b6: lea rcx, [rdi + 0x28]
4134ba: call 0x3c17b0
4134bf: mov ecx, dword ptr [rdi + 0x30]
4134c2: shl rcx, 4
4134c6: add rcx, qword ptr [rdi + 0x28]
4134ca: cmp rax, rcx
4134cd: je 0x4134e3
4134cf: mov rdx, qword ptr [rax + 8]
4134d3: lea rcx, [r15 + 0x30]
4134d7: add rdx, 0x98
4134de: call 0x407b80
4134e3: cmp r13d, 2
4134e7: jl 0x413523
4134e9: lea rdx, [rsp + 0x20]
4134ee: mov dword ptr [rsp + 0x20], 2
4134f6: lea rcx, [rdi + 0x28]
4134fa: call 0x3c17b0
4134ff: mov ecx, dword ptr [rdi + 0x30]
413502: shl rcx, 4
413506: add rcx, qword ptr [rdi + 0x28]
41350a: cmp rax, rcx
41350d: je 0x413523
41350f: mov rdx, qword ptr [rax + 8]
413513: lea rcx, [r15 + 0x38]
413517: add rdx, 0x98
41351e: call 0x407b80
413523: cmp r13d, 3
413527: jl 0x413563
413529: lea rdx, [rsp + 0x20]
41352e: mov dword ptr [rsp + 0x20], 3
413536: lea rcx, [rdi + 0x28]
41353a: call 0x3c17b0
41353f: mov ecx, dword ptr [rdi + 0x30]
413542: shl rcx, 4
413546: add rcx, qword ptr [rdi + 0x28]
41354a: cmp rax, rcx
41354d: je 0x413563
41354f: mov rdx, qword ptr [rax + 8]
413553: lea rcx, [r15 + 0x40]
413557: add rdx, 0x98
41355e: call 0x407b80
413563: lea rax, [rsp + 0x40]
413568: mov dword ptr [rsp + 0x38], 0
413570: mov r8d, esi
413573: mov qword ptr [rsp + 0x30], rax
413578: lea rdx, [rip + 0x145e381]
41357f: mov dword ptr [rsp + 0x3c], 0x100
413587: lea rcx, [rsp + 0x30]
41358c: mov byte ptr [rsp + 0x40], 0
413591: call 0xf67050
413596: lea rdx, [rsp + 0x30]
41359b: lea rcx, [rsp + 0x30]
4135a0: call 0xf72690
4135a5: test al, al
4135a7: je 0x4135b7
4135a9: lea rcx, [r15 + 0x68]
4135ad: lea rdx, [rsp + 0x30]
4135b2: call 0xf61610
4135b7: lea rcx, [rsp + 0x30]
4135bc: call 0xf5f6f0
4135c1: mov rcx, qword ptr [rsp + 0x150]
4135c9: xor rcx, rsp
4135cc: call 0x1798850
4135d1: add rsp, 0x160
4135d8: pop r15
4135da: pop r14
4135dc: pop r13
4135de: pop r12
4135e0: pop rdi
4135e1: pop rsi
4135e2: pop rbx
4135e3: ret 
FUNCTION 0x5e8cf0
5e8cf0: mov qword ptr [rsp + 8], rbx
5e8cf5: mov qword ptr [rsp + 0x10], rbp
5e8cfa: mov qword ptr [rsp + 0x18], rsi
5e8cff: push rdi
5e8d00: sub rsp, 0x30
5e8d04: lea rax, [rip + 0x12b764d]
5e8d0b: movaps xmmword ptr [rsp + 0x20], xmm6
5e8d10: mov qword ptr [rcx], rax
5e8d13: mov rbx, rcx
5e8d16: mov qword ptr [rip + 0x16d2a83], rcx
5e8d1d: add rcx, 8
5e8d21: call 0x5e5c30
5e8d26: mov dword ptr [rbx + 0x378], 1
5e8d30: lea rax, [rip + 0x12b7629]
5e8d37: mov qword ptr [rbx], rax
5e8d3a: lea rcx, [rbx + 0x390]
5e8d41: lea rax, [rip + 0x12b7620]
5e8d48: xor ebp, ebp
5e8d4a: mov qword ptr [rbx + 8], rax
5e8d4e: mov qword ptr [rbx + 0x380], rbp
5e8d55: mov qword ptr [rbx + 0x388], rbp
5e8d5c: call 0x5be130
5e8d61: mov qword ptr [rbx + 0x3b8], rbp
5e8d68: mov qword ptr [rbx + 0x3c0], rbp
5e8d6f: mov dword ptr [rbx + 0x3c8], ebp
5e8d75: mov dword ptr [rbx + 0x3cc], 0xbf800000
5e8d7f: mov rax, qword ptr [rip + 0x1696692]
5e8d86: cmp dword ptr [rax + 8], 1
5e8d8a: jne 0x5e8dd2
5e8d8c: mov ecx, 0x28
5e8d91: call 0xf26ad0
5e8d96: test rax, rax
5e8d99: je 0x5e8da5
5e8d9b: mov rcx, rax
5e8d9e: call 0x676f10
5e8da3: jmp 0x5e8da8
5e8da5: mov rax, rbp
5e8da8: mov ecx, 0x38
5e8dad: mov qword ptr [rip + 0x16a7304], rax
5e8db4: call 0xf26ad0
5e8db9: test rax, rax
5e8dbc: je 0x5e8dc8
5e8dbe: mov rcx, rax
5e8dc1: call 0x390bc0
5e8dc6: jmp 0x5e8dcb
5e8dc8: mov rax, rbp
5e8dcb: mov qword ptr [rip + 0x16a72de], rax
5e8dd2: call 0x6133c0
5e8dd7: test rax, rax
5e8dda: je 0x5e8dff
5e8ddc: mov ecx, 0x28
5e8de1: call 0xf26ad0
5e8de6: test rax, rax
5e8de9: je 0x5e8df5
5e8deb: mov rcx, rax
5e8dee: call 0x60b5c0
5e8df3: jmp 0x5e8df8
5e8df5: mov rax, rbp
5e8df8: mov qword ptr [rip + 0x16d42c9], rax
5e8dff: call 0x4b9dd0
5e8e04: mov ecx, 0x18
5e8e09: call 0xf26ad0
5e8e0e: test rax, rax
5e8e11: je 0x5e8e1d
5e8e13: mov rcx, rax
5e8e16: call 0x113d1c0
5e8e1b: jmp 0x5e8e20
5e8e1d: mov rax, rbp
5e8e20: mov qword ptr [rip + 0x16d4321], rax
5e8e27: call 0x1124520
5e8e2c: call 0x1103a00
5e8e31: mov ecx, 0x40
5e8e36: call 0xf26ad0
5e8e3b: test rax, rax
5e8e3e: je 0x5e8e4a
5e8e40: mov rcx, rax
5e8e43: call 0x98a390
5e8e48: jmp 0x5e8e4d
5e8e4a: mov rax, rbp
5e8e4d: cmp qword ptr [rip + 0x16965cc], rbp
5e8e54: mov qword ptr [rip + 0x16ad68d], rax
5e8e5b: je 0x5e8e80
5e8e5d: mov ecx, 0x38
5e8e62: call 0xf26ad0
5e8e67: test rax, rax
5e8e6a: je 0x5e8e76
5e8e6c: mov rcx, rax
5e8e6f: call 0x831bf0
5e8e74: jmp 0x5e8e79
5e8e76: mov rax, rbp
5e8e79: mov qword ptr [rip + 0x16d42d0], rax
5e8e80: mov rcx, qword ptr [rip + 0x1692fe9]
5e8e87: call 0x368d90
5e8e8c: call 0x5cd630
5e8e91: mov r8b, 1
5e8e94: lea rdx, [rip + 0x12b618d]
5e8e9b: lea rcx, [rbx + 8]
5e8e9f: call 0x5f3310
5e8ea4: call 0x5d00b0
5e8ea9: mov ecx, dword ptr [rbx + 0x3c0]
5e8eaf: xorps xmm6, xmm6
5e8eb2: test ecx, ecx
5e8eb4: je 0x5e8ed8
5e8eb6: mov eax, dword ptr [rbx + 0x3c8]
5e8ebc: xorps xmm6, xmm6
5e8ebf: xorps xmm0, xmm0
5e8ec2: cvtsi2ss xmm0, rcx
5e8ec7: cvtsi2ss xmm6, rax
5e8ecc: divss xmm6, xmm0
5e8ed0: mulss xmm6, dword ptr [rip + 0x12635c4]
5e8ed8: ucomiss xmm6, dword ptr [rbx + 0x3cc]
5e8edf: jp 0x5e8ee3
5e8ee1: je 0x5e8efd
5e8ee3: xor r8d, r8d
5e8ee6: lea rcx, [rbx + 0x390]
5e8eed: movaps xmm1, xmm6
5e8ef0: call 0x5daec0
5e8ef5: movss dword ptr [rbx + 0x3cc], xmm6
5e8efd: mov rcx, qword ptr [rip + 0x16d2874]
5e8f04: xor edx, edx
5e8f06: call 0xf31160
5e8f0b: mov rsi, qword ptr [rsp + 0x50]
5e8f10: mov rax, rbx
5e8f13: movaps xmm6, xmmword ptr [rsp + 0x20]
5e8f18: mov dword ptr [rbx + 0x378], ebp
5e8f1e: mov rbx, qword ptr [rsp + 0x40]
5e8f23: mov rbp, qword ptr [rsp + 0x48]
5e8f28: add rsp, 0x30
5e8f2c: pop rdi
5e8f2d: ret 