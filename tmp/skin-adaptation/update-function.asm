21c020 mov byte ptr [rsp + 0x10], dl
21c024 push rbp
21c025 push rbx
21c026 push rsi
21c027 push rdi
21c028 push r12
21c02a push r13
21c02c push r14
21c02e push r15
21c030 lea rbp, [rsp - 0x1f]
21c035 sub rsp, 0x88
21c03c mov rsi, qword ptr [rcx]
21c03f lea rdx, [rcx + 0x18]
21c043 mov rdi, qword ptr [rcx + 8]
21c047 xor r12d, r12d
21c04a mov qword ptr [rbp - 0x29], r12
21c04e mov ebx, r12d
21c051 mov qword ptr [rbp - 0x21], r12
21c055 mov r13, rcx
21c058 mov qword ptr [rbp - 0x19], r12
21c05c mov qword ptr [rbp - 0x41], r12
21c060 mov qword ptr [rbp - 0x31], r12
21c064 mov qword ptr [rbp - 0x39], r12
21c068 mov qword ptr [rbp - 0x11], r12
21c06c mov qword ptr [rbp - 9], rsi
21c070 mov byte ptr [rbp + 0x67], 0xff
21c074 nop dword ptr [rax]
21c078 nop dword ptr [rax + rax]
21c080 cmp rdi, rsi
21c083 mov qword ptr [rbp - 1], rdi
21c087 lea rax, [rdi - 0xa0]
21c08e mov rcx, rdi
21c091 mov r15, rdx
21c094 mov r14, rdi
21c097 cmovne r15, rax
21c09b lea rdx, [rbp - 0x49]
21c09f cmp rcx, rsi
21c0a2 mov rdi, rax
21c0a5 mov r8, r15
21c0a8 cmove rdi, rcx
21c0ac lea rcx, [rip + 0x1a720ed]
21c0b3 call 0x3ab9d0
21c0b8 mov rcx, qword ptr [rbp - 0x49]
21c0bc test rcx, rcx
21c0bf je 0x21c453
21c0c5 cmp qword ptr [rbp - 0x41], 0
21c0ca jne 0x21c36c
21c0d0 mov rax, qword ptr [rip + 0x1a5fda9]
21c0d7 movzx edx, byte ptr [rip + 0x1aa38ca]
21c0de test rax, rax
21c0e1 setne r9b
21c0e5 test rax, rax
21c0e8 je 0x21c0f4
21c0ea movzx r8d, byte ptr [rax + 0x423]
21c0f2 jmp 0x21c0f8
21c0f4 movzx r8d, dl
21c0f8 cmp r8b, dl
21c0fb je 0x21c28a
21c101 test r9b, r9b
21c104 je 0x21c10d
21c106 movzx edx, byte ptr [rax + 0x423]
21c10d movzx eax, dl
21c110 lea rdx, [rax + rax*8]
21c114 shl rdx, 5
21c118 lea rax, [rip + 0x1aa4869]
21c11f mov rax, qword ptr [rdx + rax]
21c123 test rax, rax
21c126 je 0x21c28a
21c12c lea r14, [rax + 0x1a8]
21c133 mov rax, qword ptr [r14]
21c136 lea rdx, [rbp + 0x77]
21c13a mov rcx, r14
21c13d call qword ptr [rax + 0x20]
21c140 movzx ecx, byte ptr [rax]
21c143 mov rax, qword ptr [rip + 0x1a5fd0e]
21c14a mov esi, dword ptr [rax + rcx*4 + 0x20]
21c14e mov rcx, qword ptr [r13 + 0x130]
21c155 mov rax, qword ptr [rcx]
21c158 call qword ptr [rax + 0x50]
21c15b cmp esi, eax
21c15d jne 0x21c1e8
21c163 cmp dword ptr [r15 + 0x30], 0
21c168 lea r8, [r15 + 0x28]
21c16c je 0x21c1e8
21c16e lea rdx, [rbp + 7]
21c172 lea rcx, [rip + 0x1a72027]
21c179 call 0x3ab9d0
21c17e mov rax, qword ptr [rbp + 7]
21c182 test rax, rax
21c185 setne cl
21c188 test rax, rax
21c18b je 0x21c286
21c191 test cl, cl
21c193 je 0x21c19f
21c195 inc dword ptr [rax + 0x1c8]
21c19b mov rax, qword ptr [rbp + 7]
21c19f mov rcx, qword ptr [rbp - 0x41]
21c1a3 test rcx, rcx
21c1a6 je 0x21c1b1
21c1a8 call 0x1edf10
21c1ad mov rax, qword ptr [rbp + 7]
21c1b1 mov qword ptr [rbp - 0x41], rax
21c1b5 lea rdx, [r15 + 0x88]
21c1bc movzx esi, byte ptr [r15 + 0x84]
21c1c4 lea rcx, [rbp - 0x29]
21c1c8 mov byte ptr [rbp + 0x67], sil
21c1cc call 0x2152d0
21c1d1 mov rcx, qword ptr [rbp + 7]
21c1d5 test rcx, rcx
21c1d8 je 0x21c1df
21c1da call 0x1edf10
21c1df mov rcx, qword ptr [rbp - 0x49]
21c1e3 jmp 0x21c364
21c1e8 mov rax, qword ptr [r14]
21c1eb lea rdx, [rbp + 0x7f]
21c1ef mov rcx, r14
21c1f2 call qword ptr [rax + 0x20]
21c1f5 movzx ecx, byte ptr [rax]
21c1f8 mov rax, qword ptr [rip + 0x1a5fc59]
21c1ff mov esi, dword ptr [rax + rcx*4 + 0x20]
21c203 mov rcx, qword ptr [r13 + 0x130]
21c20a mov rax, qword ptr [rcx]
21c20d call qword ptr [rax + 0x50]
21c210 cmp esi, eax
21c212 je 0x21c286
21c214 cmp dword ptr [r15 + 0x58], 0
21c219 lea r8, [r15 + 0x50]
21c21d je 0x21c286
21c21f lea rdx, [rbp + 0xf]
21c223 lea rcx, [rip + 0x1a71f76]
21c22a call 0x3ab9d0
21c22f mov rax, qword ptr [rbp + 0xf]
21c233 test rax, rax
21c236 setne cl
21c239 test rax, rax
21c23c je 0x21c286
21c23e test cl, cl
21c240 je 0x21c24c
21c242 inc dword ptr [rax + 0x1c8]
21c248 mov rax, qword ptr [rbp + 0xf]
21c24c mov rcx, qword ptr [rbp - 0x41]
21c250 test rcx, rcx
21c253 je 0x21c25e
21c255 call 0x1edf10
21c25a mov rax, qword ptr [rbp + 0xf]
21c25e mov qword ptr [rbp - 0x41], rax
21c262 lea rdx, [r15 + 0x88]
21c269 movzx eax, byte ptr [r15 + 0x84]
21c271 lea rcx, [rbp - 0x29]
21c275 mov byte ptr [rbp + 0x67], al
21c278 call 0x2152d0
21c27d mov rcx, qword ptr [rbp + 0xf]
21c281 jmp 0x21c1d5
21c286 mov rcx, qword ptr [rbp - 0x49]
21c28a test rcx, rcx
21c28d je 0x21c299
21c28f inc dword ptr [rcx + 0x1c8]
21c295 mov rcx, qword ptr [rbp - 0x49]
21c299 mov rax, qword ptr [rbp - 0x41]
21c29d test rax, rax
21c2a0 je 0x21c2ae
21c2a2 mov rcx, rax
21c2a5 call 0x1edf10
21c2aa mov rcx, qword ptr [rbp - 0x49]
21c2ae mov rsi, qword ptr [rbp - 0x29]
21c2b2 mov r14d, dword ptr [rbp - 0x21]
21c2b6 shl r14, 4
21c2ba add r14, rsi
21c2bd mov qword ptr [rbp - 0x41], rcx
21c2c1 movzx eax, byte ptr [r15 + 0x84]
21c2c9 mov byte ptr [rbp + 0x67], al
21c2cc cmp rsi, r14
21c2cf jae 0x21c2e6
21c2d1 mov rcx, rsi
21c2d4 call 0xf5f6f0
21c2d9 add rsi, 0x10
21c2dd cmp rsi, r14
21c2e0 jb 0x21c2d1
21c2e2 mov rcx, qword ptr [rbp - 0x49]
21c2e6 mov edx, dword ptr [rbp - 0x1d]
21c2e9 mov eax, edx
21c2eb mov dword ptr [rbp - 0x21], r12d
21c2ef btr eax, 0x1f
21c2f3 mov esi, dword ptr [r15 + 0x90]
21c2fa cmp eax, esi
21c2fc jae 0x21c327
21c2fe test edx, edx
21c300 js 0x21c30b
21c302 mov rcx, qword ptr [rbp - 0x29]
21c306 call 0xf20d70
21c30b mov r14, rsi
21c30e shl r14, 4
21c312 mov rcx, r14
21c315 call 0xf26ad0
21c31a mov rcx, qword ptr [rbp - 0x49]
21c31e mov qword ptr [rbp - 0x29], rax
21c322 mov dword ptr [rbp - 0x1d], esi
21c325 jmp 0x21c32e
21c327 mov r14, rsi
21c32a shl r14, 4
21c32e mov dword ptr [rbp - 0x21], esi
21c331 mov rsi, qword ptr [rbp - 0x29]
21c335 lea r12, [r14 + rsi]
21c339 mov r14, qword ptr [r15 + 0x88]
21c340 cmp rsi, r12
21c343 jae 0x21c361
21c345 mov rdx, r14
21c348 mov rcx, rsi
21c34b call 0xf5d800
21c350 add rsi, 0x10
21c354 add r14, 0x10
21c358 cmp rsi, r12
21c35b jb 0x21c345
21c35d mov rcx, qword ptr [rbp - 0x49]
21c361 xor r12d, r12d
21c364 mov rsi, qword ptr [rbp - 9]
21c368 mov r14, qword ptr [rbp - 1]
21c36c cmp byte ptr [r15 + 0x79], 0
21c371 jne 0x21c41a
21c377 cmp qword ptr [rbp - 0x19], 0
21c37c jne 0x21c39f
21c37e test rcx, rcx
21c381 je 0x21c39b
21c383 inc dword ptr [rcx + 0x1c8]
21c389 mov rcx, qword ptr [rbp - 0x19]
21c38d test rcx, rcx
21c390 je 0x21c397
21c392 call 0x1edf10
21c397 mov rcx, qword ptr [rbp - 0x49]
21c39b mov qword ptr [rbp - 0x19], rcx
21c39f test rbx, rbx
21c3a2 jne 0x21c3bc
21c3a4 cmp byte ptr [r15 + 0x7a], bl
21c3a8 je 0x21c3bc
21c3aa test rcx, rcx
21c3ad je 0x21c3b9
21c3af inc dword ptr [rcx + 0x1c8]
21c3b5 mov rcx, qword ptr [rbp - 0x49]
21c3b9 mov rbx, rcx
21c3bc cmp qword ptr [rbp - 0x31], 0
21c3c1 jne 0x21c3eb
21c3c3 cmp byte ptr [r15 + 0x78], 0
21c3c8 je 0x21c3eb
21c3ca test rcx, rcx
21c3cd je 0x21c3e7
21c3cf inc dword ptr [rcx + 0x1c8]
21c3d5 mov rcx, qword ptr [rbp - 0x31]
21c3d9 test rcx, rcx
21c3dc je 0x21c3e3
21c3de call 0x1edf10
21c3e3 mov rcx, qword ptr [rbp - 0x49]
21c3e7 mov qword ptr [rbp - 0x31], rcx
21c3eb cmp qword ptr [rbp - 0x39], 0
21c3f0 jne 0x21c41a
21c3f2 cmp byte ptr [r15 + 0x7c], 0
21c3f7 je 0x21c41a
21c3f9 test rcx, rcx
21c3fc je 0x21c416
21c3fe inc dword ptr [rcx + 0x1c8]
21c404 mov rcx, qword ptr [rbp - 0x39]
21c408 test rcx, rcx
21c40b je 0x21c412
21c40d call 0x1edf10
21c412 mov rcx, qword ptr [rbp - 0x49]
21c416 mov qword ptr [rbp - 0x39], rcx
21c41a cmp qword ptr [rbp - 0x11], 0
21c41f jne 0x21c449
21c421 cmp byte ptr [r15 + 0x7b], 0
21c426 je 0x21c449
21c428 test rcx, rcx
21c42b je 0x21c445
21c42d inc dword ptr [rcx + 0x1c8]
21c433 mov rcx, qword ptr [rbp - 0x11]
21c437 test rcx, rcx
21c43a je 0x21c441
21c43c call 0x1edf10
21c441 mov rcx, qword ptr [rbp - 0x49]
21c445 mov qword ptr [rbp - 0x11], rcx
21c449 test rcx, rcx
21c44c je 0x21c453
21c44e call 0x1edf10
21c453 lea rdx, [r13 + 0x18]
21c457 cmp r14, rsi
21c45a jne 0x21c080
21c460 mov rcx, qword ptr [rbp - 0x11]
21c464 test rcx, rcx
21c467 je 0x21c4ae
21c469 cmp qword ptr [r13 + 0xd8], rcx
21c470 je 0x21c4ae
21c472 mov rcx, qword ptr [r13 + 0x130]
21c479 lea rdx, [rbp - 0x11]
21c47d mov rax, qword ptr [rcx]
21c480 call qword ptr [rax + 0x28]
21c483 mov rcx, qword ptr [rbp - 0x11]
21c487 test rcx, rcx
21c48a je 0x21c492
21c48c inc dword ptr [rcx + 0x1c8]
21c492 mov rcx, qword ptr [r13 + 0xd8]
21c499 test rcx, rcx
21c49c je 0x21c4a3
21c49e call 0x1edf10
21c4a3 mov rax, qword ptr [rbp - 0x11]
21c4a7 mov qword ptr [r13 + 0xd8], rax
21c4ae cmp qword ptr [rbp - 0x19], 0
21c4b3 je 0x21c540
21c4b9 mov qword ptr [rbp + 0x77], rbx
21c4bd mov rcx, rbx
21c4c0 test rbx, rbx
21c4c3 je 0x21c4cf
21c4c5 inc dword ptr [rbx + 0x1c8]
21c4cb mov rcx, qword ptr [rbp + 0x77]
21c4cf mov rax, qword ptr [rbp - 0x19]
21c4d3 cmp qword ptr [r13 + 0xb8], rax
21c4da je 0x21c536
21c4dc mov rcx, qword ptr [r13 + 0x130]
21c4e3 lea rdx, [rbp - 0x19]
21c4e7 mov rax, qword ptr [rcx]
21c4ea call qword ptr [rax + 0x10]
21c4ed mov rcx, qword ptr [rbp - 0x19]
21c4f1 test rcx, rcx
21c4f4 je 0x21c4fc
21c4f6 inc dword ptr [rcx + 0x1c8]
21c4fc mov rcx, qword ptr [r13 + 0xb8]
21c503 test rcx, rcx
21c506 je 0x21c50d
21c508 call 0x1edf10
21c50d mov rcx, qword ptr [rbp + 0x77]
21c511 mov rax, qword ptr [rbp - 0x19]
21c515 mov qword ptr [r13 + 0xb8], rax
21c51c test rcx, rcx
21c51f je 0x21c540
21c521 mov rcx, qword ptr [r13 + 0x130]
21c528 lea rdx, [rbp + 0x77]
21c52c mov rax, qword ptr [rcx]
21c52f call qword ptr [rax + 0x30]
21c532 mov rcx, qword ptr [rbp + 0x77]
21c536 test rcx, rcx
21c539 je 0x21c540
21c53b call 0x1edf10
21c540 mov rax, qword ptr [rbp - 0x41]
21c544 test rax, rax
21c547 je 0x21c596
21c549 cmp qword ptr [r13 + 0xc0], rax
21c550 je 0x21c596
21c552 mov rcx, qword ptr [r13 + 0x130]
21c559 lea rdx, [rbp - 0x41]
21c55d mov rax, qword ptr [rcx]
21c560 call qword ptr [rax + 8]
21c563 mov rax, qword ptr [rbp - 0x41]
21c567 test rax, rax
21c56a je 0x21c572
21c56c inc dword ptr [rax + 0x1c8]
21c572 mov rcx, qword ptr [r13 + 0xc0]
21c579 test rcx, rcx
21c57c je 0x21c583
21c57e call 0x1edf10
21c583 mov rax, qword ptr [rbp - 0x41]
21c587 mov qword ptr [r13 + 0xc0], rax
21c58e mov byte ptr [r13 + 0xf0], 1
21c596 cmp byte ptr [rbp + 0x6f], 0
21c59a mov rcx, qword ptr [rbp - 0x31]
21c59e je 0x21c5c9
21c5a0 test rcx, rcx
21c5a3 je 0x21c5ab
21c5a5 inc dword ptr [rcx + 0x1c8]
21c5ab mov rcx, qword ptr [r13 + 0xc8]
21c5b2 test rcx, rcx
21c5b5 je 0x21c5bc
21c5b7 call 0x1edf10
21c5bc mov rax, qword ptr [rbp - 0x31]
21c5c0 mov qword ptr [r13 + 0xc8], rax
21c5c7 jmp 0x21c636
21c5c9 test rcx, rcx
21c5cc je 0x21c613
21c5ce cmp qword ptr [r13 + 0xc8], rcx
21c5d5 je 0x21c613
21c5d7 mov rcx, qword ptr [r13 + 0x130]
21c5de lea rdx, [rbp - 0x31]
21c5e2 mov rax, qword ptr [rcx]
21c5e5 call qword ptr [rax + 0x18]
21c5e8 mov rcx, qword ptr [rbp - 0x31]
21c5ec test rcx, rcx
21c5ef je 0x21c5f7
21c5f1 inc dword ptr [rcx + 0x1c8]
21c5f7 mov rcx, qword ptr [r13 + 0xc8]
21c5fe test rcx, rcx
21c601 je 0x21c608
21c603 call 0x1edf10
21c608 mov rax, qword ptr [rbp - 0x31]
21c60c mov qword ptr [r13 + 0xc8], rax
21c613 mov rcx, qword ptr [rbp - 0x39]
21c617 test rcx, rcx
21c61a je 0x21c661
21c61c cmp qword ptr [r13 + 0xd0], rcx
21c623 je 0x21c661
21c625 mov rcx, qword ptr [r13 + 0x130]
21c62c lea rdx, [rbp - 0x39]
21c630 mov rax, qword ptr [rcx]
21c633 call qword ptr [rax + 0x20]
21c636 mov rcx, qword ptr [rbp - 0x39]
21c63a test rcx, rcx
21c63d je 0x21c645
21c63f inc dword ptr [rcx + 0x1c8]
21c645 mov rcx, qword ptr [r13 + 0xd0]
21c64c test rcx, rcx
21c64f je 0x21c656
21c651 call 0x1edf10
21c656 mov rax, qword ptr [rbp - 0x39]
21c65a mov qword ptr [r13 + 0xd0], rax
21c661 movzx esi, byte ptr [rbp + 0x67]
21c665 lea rdi, [r13 + 0x130]
21c66c cmp sil, 0xff
21c670 je 0x21c682
21c672 mov rcx, qword ptr [rdi]
21c675 xor r8d, r8d
21c678 movzx edx, sil
21c67c mov rax, qword ptr [rcx]
21c67f call qword ptr [rax + 0x38]
21c682 lea r14, [r13 + 0xf0]
21c689 cmp byte ptr [r14], 0
21c68d jne 0x21c6df
21c68f mov eax, dword ptr [r13 + 0xe8]
21c696 cmp eax, dword ptr [rbp - 0x21]
21c699 jne 0x21c6df
21c69b test eax, eax
21c69d je 0x21c700
21c69f mov rdi, r12
21c6a2 mov rdx, qword ptr [rbp - 0x29]
21c6a6 mov rcx, qword ptr [r13 + 0xe0]
21c6ad add rdx, rdi
21c6b0 add rcx, rdi
21c6b3 call 0xf5fe90
21c6b8 test al, al
21c6ba je 0x21c6d1
21c6bc mov eax, dword ptr [r13 + 0xe8]
21c6c3 inc r12
21c6c6 add rdi, 0x10
21c6ca cmp r12, rax
21c6cd jb 0x21c6a2
21c6cf jmp 0x21c700
21c6d1 lea rdi, [r13 + 0x130]
21c6d8 lea r14, [r13 + 0xf0]
21c6df mov rcx, qword ptr [rdi]
21c6e2 lea rdx, [rbp - 0x29]
21c6e6 mov rax, qword ptr [rcx]
21c6e9 call qword ptr [rax + 0x48]
21c6ec lea rdx, [rbp - 0x29]
21c6f0 lea rcx, [r13 + 0xe0]
21c6f7 call 0x2152d0
21c6fc mov byte ptr [r14], 0
21c700 mov rdi, qword ptr [rbp - 0x29]
21c704 mov esi, dword ptr [rbp - 0x21]
21c707 shl rsi, 4
21c70b add rsi, rdi
21c70e cmp rdi, rsi
21c711 jae 0x21c724
21c713 mov rcx, rdi
21c716 call 0xf5f6f0
21c71b add rdi, 0x10
21c71f cmp rdi, rsi
21c722 jb 0x21c713
21c724 test dword ptr [rbp - 0x1d], 0x80000000
21c72b jne 0x21c736
21c72d mov rcx, qword ptr [rbp - 0x29]
21c731 call 0xf20d70
21c736 mov rcx, qword ptr [rbp - 0x11]
21c73a test rcx, rcx
21c73d je 0x21c744
21c73f call 0x1edf10
21c744 mov rcx, qword ptr [rbp - 0x39]
21c748 test rcx, rcx
21c74b je 0x21c752
21c74d call 0x1edf10
21c752 mov rcx, qword ptr [rbp - 0x31]
21c756 test rcx, rcx
21c759 je 0x21c760
21c75b call 0x1edf10
21c760 mov rax, qword ptr [rbp - 0x41]
21c764 test rax, rax
21c767 je 0x21c771
21c769 mov rcx, rax
21c76c call 0x1edf10
21c771 test rbx, rbx
21c774 je 0x21c77e
21c776 mov rcx, rbx
21c779 call 0x1edf10
21c77e mov rcx, qword ptr [rbp - 0x19]
21c782 test rcx, rcx
21c785 je 0x21c78c
21c787 call 0x1edf10
21c78c add rsp, 0x88
21c793 pop r15
21c795 pop r14
21c797 pop r13
21c799 pop r12
21c79b pop rdi
21c79c pop rsi
21c79d pop rbx
21c79e pop rbp
21c79f ret 