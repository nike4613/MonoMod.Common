﻿#if !NET35
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using CCallSite = Mono.Cecil.CallSite;

namespace MonoMod.RuntimeDetour.Platforms {
#if !MONOMOD_INTERNAL
    public
#endif
    class GenericDetourCoreCLRWinX64 : GenericDetourCoreCLR {

#if false
        // for all of the precall thunks, the initial detour jump will be a call instruction
        // immediately following that instruction will be the pointer to the correct handling method
        // immediately following that pointer will be a 32 bit index to determine the hook target (index into list?)
        // immediately following that index will be a 16 bit length of the call instruction (to determine how to backpatch)

        // for all of the call thunks, the initial detour jump will be a call instruction
        // immediately following that instruction pointer will be a pointer to the context conversion thunk
        // immediately following that pointer will be a pointer to the real method body
        // immediately following that pointer will be a 32 bit index which determines the hook target

        // the code used was generated using NASM and hexdump
        // each NASM file starts with this header:
        //* bits 64
        //* default rel
        //* section .text
        // they are assembled using this command:
        // $ nasm -f bin thunk.s
        // which is then exported with
        // $ hexdump -e '/1 "0x%02X, "' thunk
        // which can be pasted into the bye arrays

        // it seems our thunks *require* us to set up stack frames correctly, and allocate at least enough space for the parameters to call the handlers

#region Thunk definitions

#region call_p1
        /*
; after the call instruction is this:
; 8[context converter ptr]
; 8[real target]
; 4[index]

pop r10 ; r10 isn't used to pass arguments on Windows; it now contains the return position

; setup stack frame
push rbp
sub rsp, 40h ; 4 qword (20h) + 4 qword arguments (18h) ~~+ 4 128-bit vectors~~
lea rbp, [rsp + 40h]

; save register-passed arguments, specifically in the top of our allocated range
mov [rbp - 8h], rcx
mov [rbp - 10h], rdx
mov [rbp - 18h], r8
mov [rbp - 20h], r9
mov [rbp - 28h], r10
; if we assume we never do floating point math, this should be fine... i hope
;movdqa [rbp - 30h], xmm0
;movdqa [rbp - 40h], xmm1
;movdqa [rbp - 50h], xmm2
;movdqa [rbp - 60h], xmm3

; setup call
; the methods being called here have no strangeness, only user args passed in register

; arg 1 will be the given context ptr, which here, is in rcx

; arg 2 will be the index ptr
mov edx, [r10 + 10h]

; finally call handler
call toFillFixupPtr
toFillFixupPtr:

; rax now contains a corrected generic context

; now we'll set up the call to the context converter
; reload arguments
mov rcx, [rbp - 8h]
mov rdx, [rbp - 10h]
mov r8, [rbp - 18h]
mov r9, [rbp - 20h]
mov r10, [rbp - 28h]
;movdqa xmm0, [rbp - 30h]
;movdqa xmm1, [rbp - 40h]
;movdqa xmm2, [rbp - 50h]
;movdqa xmm3, [rbp - 60h]

; clean up our stack frame
lea rsp, [rbp]
pop rbp

mov r11, [r10 + 8h] ; load our real target into r11

; we're finally ready to call the context converter
jmp [r10]

; the context converter expects in rax the new generic context, and in r11 the real target
         */
        private const int call_p1_from_fix_fn_offs = 37;
        private static readonly byte[] call_p1_form = {
            0x41, 0x5A, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48,
            0x8D, 0x6C, 0x24, 0x40, 0x48, 0x89, 0x4D, 0xF8,
            0x48, 0x89, 0x55, 0xF0, 0x4C, 0x89, 0x45, 0xE8,
            0x4C, 0x89, 0x4D, 0xE0, 0x4C, 0x89, 0x55, 0xD8,
            0x41, 0x8B, 0x52, 0x10, 0xE8, 0x00, 0x00, 0x00, 0x00,
            0x48, 0x8B, 0x4D, 0xF8, 0x48, 0x8B, 0x55, 0xF0,
            0x4C, 0x8B, 0x45, 0xE8, 0x4C, 0x8B, 0x4D, 0xE0,
            0x4C, 0x8B, 0x55, 0xD8, 0x48, 0x8D, 0x65, 0x00,
            0x5D, 0x4D, 0x8B, 0x5A, 0x08, 0x41, 0xFF, 0x22,
        };
#endregion
#region call_p2
        /*
; after the call instruction is this:
; 8[context converter ptr]
; 8[real target]
; 4[index]

pop r10 ; r10 isn't used to pass arguments on Windows; it now contains the return position

; setup stack frame
push rbp
sub rsp, 40h ; 4 qword (20h) + 4 qword arguments (18h) ~~+ 4 128-bit vectors~~
lea rbp, [rsp + 40h]

; save register-passed arguments, specifically in the top of our allocated range
mov [rbp - 8h], rcx
mov [rbp - 10h], rdx
mov [rbp - 18h], r8
mov [rbp - 20h], r9
mov [rbp - 28h], r10
; if we assume we never do floating point math, this should be fine... i hope
;movdqa [rbp - 30h], xmm0
;movdqa [rbp - 40h], xmm1
;movdqa [rbp - 50h], xmm2
;movdqa [rbp - 60h], xmm3

; setup call
; the methods being called here have no strangeness, only user args passed in register

; arg 1 will be the given context ptr, which here, is in rdx
mov rcx, rdx

; arg 2 will be the index ptr
mov edx, [r10 + 10h]

; finally call handler
call toFillFixupPtr
toFillFixupPtr:

; rax now contains a corrected generic context

; now we'll set up the call to the context converter
; reload arguments
mov rcx, [rbp - 8h]
mov rdx, [rbp - 10h]
mov r8, [rbp - 18h]
mov r9, [rbp - 20h]
mov r10, [rbp - 28h]
;movdqa xmm0, [rbp - 30h]
;movdqa xmm1, [rbp - 40h]
;movdqa xmm2, [rbp - 50h]
;movdqa xmm3, [rbp - 60h]

; clean up our stack frame
lea rsp, [rbp]
pop rbp

mov r11, [r10 + 8h] ; load our real target into r11

; we're finally ready to call the context converter
jmp [r10]
         */
        private const int call_p2_from_fix_fn_offs = 40;
        private static readonly byte[] call_p2_form = {
            0x41, 0x5A, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48,
            0x8D, 0x6C, 0x24, 0x40, 0x48, 0x89, 0x4D, 0xF8,
            0x48, 0x89, 0x55, 0xF0, 0x4C, 0x89, 0x45, 0xE8,
            0x4C, 0x89, 0x4D, 0xE0, 0x4C, 0x89, 0x55, 0xD8,
            0x48, 0x89, 0xD1, 0x41, 0x8B, 0x52, 0x10, 0xE8,
            0x00, 0x00, 0x00, 0x00, 0x48, 0x8B, 0x4D, 0xF8,
            0x48, 0x8B, 0x55, 0xF0, 0x4C, 0x8B, 0x45, 0xE8,
            0x4C, 0x8B, 0x4D, 0xE0, 0x4C, 0x8B, 0x55, 0xD8,
            0x48, 0x8D, 0x65, 0x00, 0x5D, 0x4D, 0x8B, 0x5A,
            0x08, 0x41, 0xFF, 0x22,
        };
#endregion
#region call_p2t
        /*
; after the call instruction is this:
; 8[context converter ptr]
; 8[real target]
; 4[index]

pop r10 ; r10 isn't used to pass arguments on Windows; it now contains the return position

; setup stack frame
push rbp
sub rsp, 40h ; 4 qword (20h) + 4 qword arguments (18h) ~~+ 4 128-bit vectors~~
lea rbp, [rsp + 40h]

; save register-passed arguments, specifically in the top of our allocated range
mov [rbp - 8h], rcx
mov [rbp - 10h], rdx
mov [rbp - 18h], r8
mov [rbp - 20h], r9
mov [rbp - 28h], r10
; if we assume we never do floating point math, this should be fine... i hope
;movdqa [rbp - 30h], xmm0
;movdqa [rbp - 40h], xmm1
;movdqa [rbp - 50h], xmm2
;movdqa [rbp - 60h], xmm3

; setup call
; the methods being called here have no strangeness, only user args passed in register

; arg 3 will be the this ptr
mov r8, rcx

; arg 1 will be the given context ptr, which here, is in rdx
mov rcx, rdx

; arg 2 will be the index ptr
mov edx, [r10 + 10h]

; finally call handler
call toFillFixupPtr
toFillFixupPtr:

; rax now contains a corrected generic context

; now we'll set up the call to the context converter
; reload arguments
mov rcx, [rbp - 8h]
mov rdx, [rbp - 10h]
mov r8, [rbp - 18h]
mov r9, [rbp - 20h]
mov r10, [rbp - 28h]
;movdqa xmm0, [rbp - 30h]
;movdqa xmm1, [rbp - 40h]
;movdqa xmm2, [rbp - 50h]
;movdqa xmm3, [rbp - 60h]

; clean up our stack frame
lea rsp, [rbp]
pop rbp

mov r11, [r10 + 8h] ; load our real target into r11

; we're finally ready to call the context converter
jmp [r10]

; the context converter expects in rax the new generic context, and in r11 the real target
         */
        private const int call_p2t_from_fix_fn_offs = 43;
        private static readonly byte[] call_p2t_form = {
            0x41, 0x5A, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48,
            0x8D, 0x6C, 0x24, 0x40, 0x48, 0x89, 0x4D, 0xF8,
            0x48, 0x89, 0x55, 0xF0, 0x4C, 0x89, 0x45, 0xE8,
            0x4C, 0x89, 0x4D, 0xE0, 0x4C, 0x89, 0x55, 0xD8,
            0x49, 0x89, 0xC8, 0x48, 0x89, 0xD1, 0x41, 0x8B,
            0x52, 0x10, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x48,
            0x8B, 0x4D, 0xF8, 0x48, 0x8B, 0x55, 0xF0, 0x4C,
            0x8B, 0x45, 0xE8, 0x4C, 0x8B, 0x4D, 0xE0, 0x4C,
            0x8B, 0x55, 0xD8, 0x48, 0x8D, 0x65, 0x00, 0x5D,
            0x4D, 0x8B, 0x5A, 0x08, 0x41, 0xFF, 0x22,
        };
#endregion
#region call_p3
        /*
; after the call instruction is this:
; 8[context converter ptr]
; 8[real target]
; 4[index]

pop r10 ; r10 isn't used to pass arguments on Windows; it now contains the return position

; setup stack frame
push rbp
sub rsp, 40h ; 4 qword (20h) + 4 qword arguments (18h) ~~+ 4 128-bit vectors~~
lea rbp, [rsp + 40h]

; save register-passed arguments, specifically in the top of our allocated range
mov [rbp - 8h], rcx
mov [rbp - 10h], rdx
mov [rbp - 18h], r8
mov [rbp - 20h], r9
mov [rbp - 28h], r10
; if we assume we never do floating point math, this should be fine... i hope
;movdqa [rbp - 30h], xmm0
;movdqa [rbp - 40h], xmm1
;movdqa [rbp - 50h], xmm2
;movdqa [rbp - 60h], xmm3

; setup call
; the methods being called here have no strangeness, only user args passed in register

; arg 1 will be the given context ptr, which here, is in r8
; arg 3 will be the this argument, which here, is in rcx
xchg rcx, r8

; arg 2 will be the index ptr
mov edx, [r10 + 10h]

; finally call handler
call toFillFixupPtr
toFillFixupPtr:

; rax now contains a corrected generic context

; now we'll set up the call to the context converter
; reload arguments
mov rcx, [rbp - 8h]
mov rdx, [rbp - 10h]
mov r8, [rbp - 18h]
mov r9, [rbp - 20h]
mov r10, [rbp - 28h]
;movdqa xmm0, [rbp - 30h]
;movdqa xmm1, [rbp - 40h]
;movdqa xmm2, [rbp - 50h]
;movdqa xmm3, [rbp - 60h]

; clean up our stack frame
lea rsp, [rbp]
pop rbp

mov r11, [r10 + 8h] ; load our real target into r11

; we're finally ready to call the context converter
jmp [r10]
         */
        private const int call_p3_from_fix_fn_offs = 40;
        private static readonly byte[] call_p3_form = {
            0x41, 0x5A, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48,
            0x8D, 0x6C, 0x24, 0x40, 0x48, 0x89, 0x4D, 0xF8,
            0x48, 0x89, 0x55, 0xF0, 0x4C, 0x89, 0x45, 0xE8,
            0x4C, 0x89, 0x4D, 0xE0, 0x4C, 0x89, 0x55, 0xD8,
            0x49, 0x87, 0xC8, 0x41, 0x8B, 0x52, 0x10, 0xE8,
            0x00, 0x00, 0x00, 0x00, 0x48, 0x8B, 0x4D, 0xF8,
            0x48, 0x8B, 0x55, 0xF0, 0x4C, 0x8B, 0x45, 0xE8,
            0x4C, 0x8B, 0x4D, 0xE0, 0x4C, 0x8B, 0x55, 0xD8,
            0x48, 0x8D, 0x65, 0x00, 0x5D, 0x4D, 0x8B, 0x5A,
            0x08, 0x41, 0xFF, 0x22,
        };

#endregion
#region cc g g
        /*
; at this point we have int he argument registers, the appropriate arguments
; in rax, our new generic context
; in r11, our real invocation target

; our current arg layout is:
;   g a...
; we want it to be:
;   g a...
; but we want to replace the generic context, so...

mov rcx, rax

; now that the stuff is fixed, we can call our real target in r11
jmp r11
         */
        private static readonly byte[] cconv_g_g = {
            0x48, 0x89, 0xC1, 0x41, 0xFF, 0xE3,
        };
#endregion
#region cc rg rg
        /*
; at this point we have int he argument registers, the appropriate arguments
; in rax, our new generic context
; in r11, our real invocation target

; our current arg layout is:
;   r g a...
; we want it to be:
;   r g a...
; but we want to replace the generic context, so...

mov rdx, rax

; now that the stuff is fixed, we can call our real target in r11
jmp r11
         */
        private static readonly byte[] cconv_rg_rg = {
            0x48, 0x89, 0xC2, 0x41, 0xFF, 0xE3,
        };
#endregion
#region cc t g
        /*
; the thunk data must have the following data
; * a pointer to the context conversion
; * a pointer to the actual target code
; * a byte containing an index
; * a byte containing additional flags.
; * a word containing the amount to adjust the stack pointer after pushing the fourth arg as
;   a 64-bit integer (computed as 8 - sizeof(arg4))

; the flags byte has the following bits:
; * 0: set if the fourth argument is floating point
; * 1: set if the fourth argument is floating point and is a double

; we currently have
;   t a...
; we want
;   g t a...
; so we want to move the last argument down one

; r10 contains a pointer to the thunk data
; r11 contains the real invocation target
; rax contains the new generic context

pop rdi ; load return address into rdi

test byte [r10 + 14h], 1b ; check if the flag for floating point is set
jnz flop

; conveniently, our calling convention pushes arguments in reverse order
; do regular push
push r9 ; push the last register argument to the stack
xor rsi, rsi
mov si, word [r10 + 15h]
add rsp, rsi ; do final adjustment

jmp movRegs
flop:
; idfk what to do here????

test byte [r10 + 14h], 10b ; check if double flag is set
jnz double

; move single
sub rsp, 4 ; sizeof(single)
movss [rsp], xmm3

jmp movRegs
double:
; move double
sub rsp, 8 ; sizeof(double)
movsd [rsp], xmm3

movRegs:

push rdi ; make sure to push the return address back onto the stack

; integer args
mov r9, r8
mov r8, rdx
mov rdx, rcx

; floating point args
movsd xmm3, xmm2
movsd xmm2, xmm1
movsd xmm1, xmm0
; do I need movss or movsd???

mov rcx, rax ; the location of the new generic context into position 2

; now we're set up to just call the actual target
jmp r11 ; call the actual target
         */
        private static readonly byte[] cconv_t_g = {
            0x5F, 0x41, 0xF6, 0x42, 0x14, 0x01, 0x75, 0x0F,
            0x41, 0x51, 0x48, 0x31, 0xF6, 0x66, 0x41, 0x8B,
            0x72, 0x15, 0x48, 0x01, 0xF4, 0xEB, 0x1B, 0x41,
            0xF6, 0x42, 0x14, 0x02, 0x75, 0x0B, 0x48, 0x83,
            0xEC, 0x04, 0xF3, 0x0F, 0x11, 0x1C, 0x24, 0xEB,
            0x09, 0x48, 0x83, 0xEC, 0x08, 0xF2, 0x0F, 0x11,
            0x1C, 0x24, 0x57, 0x4D, 0x89, 0xC1, 0x49, 0x89,
            0xD0, 0x48, 0x89, 0xCA, 0xF2, 0x0F, 0x10, 0xDA,
            0xF2, 0x0F, 0x10, 0xD1, 0xF2, 0x0F, 0x10, 0xC8,
            0x48, 0x89, 0xC1, 0x41, 0xFF, 0xE3,
        };
#endregion
#region cc tg g
        /*
; at this point we have int he argument registers, the appropriate arguments
; in rax, our new generic context
; in r11, our real invocation target

; our current arg layout is:
;   t g a...
; we want it to be:
;   g t a...
; but we want to replace the generic context, so...

mov rdx, rcx
mov rcx, rax

; now that the stuff is fixed, we can call our real target in r11
jmp r11
         */
        private static readonly byte[] cconv_tg_g = {
            0x48, 0x89, 0xCA, 0x48, 0x89, 0xC1, 0x41, 0xFF, 0xE3,
        };
#endregion
#region cc tr rg
        /*
; calling this thunk means that you must have, immediately following the call instruction,
; * a pointer to the context conversion
; * a pointer to the actual target code
; * a byte containing an index
; * a byte containing additional flags.
; * a word containing the amount to adjust the stack pointer after pushing the fourth arg as
;   a 64-bit integer (computed as 8 - sizeof(arg4))

; the flags byte has the following bits:
; * 0: set if the fourth argument is floating point
; * 1: set if the fourth argument is floating point and is a double

; we currently have
;   t r a...
; we want
;   r g t a...
; so we want to move the last argument down one

; r10 contains a pointer to the thunk data
; r11 contains the real invocation target
; rax contains the new generic context

pop rdi ; load return address into rdi

test byte [r10 + 14h], 1b ; check if the flag for floating point is set
jnz flop

; conveniently, our calling convention pushes arguments in reverse order
; do regular push
push r9 ; push the last register argument to the stack
xor rsi, rsi
mov si, word [r10 + 15h]
add rsp, rsi ; do final adjustment

jmp movRegs
flop:
; idfk what to do here????

test byte [r10 + 14h], 10b ; check if double flag is set
jnz double

; move single
sub rsp, 4 ; sizeof(single)
movss [rsp], xmm3

jmp movRegs
double:
; move double
sub rsp, 8 ; sizeof(double)
movsd [rsp], xmm3

movRegs:

push rdi ; make sure to push the return address back onto the stack

; integer args
mov r9, r8
;mov r8, rdx

; floating point args
movsd xmm3, xmm2
;movsd xmm2, xmm1
; do I need movss or movsd???

mov r8, rcx
mov rcx, rdx
mov rdx, rax ; the location of the new generic context into position 2

; now we're set up to just call the actual target
jmp r11 ; call the actual target
         */
        private static readonly byte[] cconv_tr_rg = {
            0x5F, 0x41, 0xF6, 0x42, 0x14, 0x01, 0x75, 0x0F,
            0x41, 0x51, 0x48, 0x31, 0xF6, 0x66, 0x41, 0x8B,
            0x72, 0x15, 0x48, 0x01, 0xF4, 0xEB, 0x1B, 0x41,
            0xF6, 0x42, 0x14, 0x02, 0x75, 0x0B, 0x48, 0x83,
            0xEC, 0x04, 0xF3, 0x0F, 0x11, 0x1C, 0x24, 0xEB,
            0x09, 0x48, 0x83, 0xEC, 0x08, 0xF2, 0x0F, 0x11,
            0x1C, 0x24, 0x57, 0x4D, 0x89, 0xC1, 0xF2, 0x0F,
            0x10, 0xDA, 0x49, 0x89, 0xC8, 0x48, 0x89, 0xD1,
            0x48, 0x89, 0xC2, 0x41, 0xFF, 0xE3,
        };
#endregion
#region cc trg rg
        /*
; at this point we have int he argument registers, the appropriate arguments
; in rax, our new generic context
; in r11, our real invocation target

; our current arg layout is:
;   t r g a...
; we want it to be:
;   r g t a...
; we don't need to preserve the existing g, so...

mov r8, rcx  ; t r t a...
mov rcx, rdx ; r r t a...
mov rdx, rax ; r g t a...

; now that the stuff is fixed, we can call our real target in r11
jmp r11
         */
        private static readonly byte[] cconv_trg_rg = {
            0x49, 0x89, 0xC8, 0x48, 0x89, 0xD1, 0x48, 0x89,
            0xC2, 0x41, 0xFF, 0xE3,
        };
#endregion
#region pre1
        /*
pop r10 ; r10 isn't used to pass arguments on Windows; it now contains the return position

; setup stack frame
push rbp
sub rsp, 40h ; 4 qword (20h) + 3 qword arguments (18h) + some buffer ~~+ 4 128-bit vectors~~
lea rbp, [rsp + 40h]

; save register-passed arguments, specifically in the top of our allocated range
mov [rbp - 8h], rcx
mov [rbp - 10h], rdx
mov [rbp - 18h], r8
mov [rbp - 20h], r9
; if we assume we never do floating point math, this should be fine... i hope
;movdqa [rbp - 30h], xmm0
;movdqa [rbp - 40h], xmm1
;movdqa [rbp - 50h], xmm2
;movdqa [rbp - 60h], xmm3

; setup call
; the methods being called here have no strangeness, only user args passed in register

; first arg is the this ptr/generic context

; the third arg is the source start
mov r8, r10 ; r8 is the argument we want it in
xor rdx, rdx
mov dx, [r10 + 12] ; offset of the call insn length
sub r8, rdx

; the second arg is the index
;xor rdx, rdx
mov edx, [r10 + 8] ; offset of the index

; finally call handler
call [r10]

; rax now contains our target method, but we need to re-load our arguments
mov rcx, [rbp - 8h]
mov rdx, [rbp - 10h]
mov r8, [rbp - 18h]
mov r9, [rbp - 20h]
;movdqa xmm0, [rbp - 30h]
;movdqa xmm1, [rbp - 40h]
;movdqa xmm2, [rbp - 50h]
;movdqa xmm3, [rbp - 60h]

; clean up our stack frame
lea rsp, [rbp]
pop rbp

; we're finally ready to call our target
jmp rax
         */
        private static readonly byte[] precall_1 = {
            0x41, 0x5A, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48,
            0x8D, 0x6C, 0x24, 0x40, 0x48, 0x89, 0x4D, 0xF8,
            0x48, 0x89, 0x55, 0xF0, 0x4C, 0x89, 0x45, 0xE8,
            0x4C, 0x89, 0x4D, 0xE0, 0x4D, 0x89, 0xD0, 0x48,
            0x31, 0xD2, 0x66, 0x41, 0x8B, 0x52, 0x0C, 0x49,
            0x29, 0xD0, 0x41, 0x8B, 0x52, 0x08, 0x41, 0xFF,
            0x12, 0x48, 0x8B, 0x4D, 0xF8, 0x48, 0x8B, 0x55,
            0xF0, 0x4C, 0x8B, 0x45, 0xE8, 0x4C, 0x8B, 0x4D,
            0xE0, 0x48, 0x8D, 0x65, 0x00, 0x5D, 0xFF, 0xE0,
        };
#endregion
#region pre2
        /*
pop r10 ; r10 isn't used to pass arguments on Windows; it now contains the return position

; setup stack frame
push rbp
sub rsp, 40h ; 4 qword (20h) + 3 qword arguments (18h) + some buffer ~~+ 4 128-bit vectors~~
lea rbp, [rsp + 40h]

; save register-passed arguments, specifically in the top of our allocated range
mov [rbp - 8h], rcx
mov [rbp - 10h], rdx
mov [rbp - 18h], r8
mov [rbp - 20h], r9
; if we assume we never do floating point math, this should be fine... i hope
;movdqa [rbp - 30h], xmm0
;movdqa [rbp - 40h], xmm1
;movdqa [rbp - 50h], xmm2
;movdqa [rbp - 60h], xmm3

; setup call
; the methods being called here have no strangeness, only user args passed in register

; second arg is the generic context, in rdx
mov rcx, rdx

; the third arg is the source start
mov r8, r10 ; r8 is the argument we want it in
xor rdx, rdx
mov dx, [r10 + 12] ; offset of the call insn length
sub r8, rdx

; the second arg is the index
;xor rdx, rdx
mov edx, [r10 + 8] ; offset of the index

; finally call handler
call [r10]

; rax now contains our target method, but we need to re-load our arguments
mov rcx, [rbp - 8h]
mov rdx, [rbp - 10h]
mov r8, [rbp - 18h]
mov r9, [rbp - 20h]
;movdqa xmm0, [rbp - 30h]
;movdqa xmm1, [rbp - 40h]
;movdqa xmm2, [rbp - 50h]
;movdqa xmm3, [rbp - 60h]

; clean up our stack frame
lea rsp, [rbp]
pop rbp

; we're finally ready to call our target
jmp rax
         */
        private static readonly byte[] precall_2 = {
            0x41, 0x5A, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48,
            0x8D, 0x6C, 0x24, 0x40, 0x48, 0x89, 0x4D, 0xF8,
            0x48, 0x89, 0x55, 0xF0, 0x4C, 0x89, 0x45, 0xE8,
            0x4C, 0x89, 0x4D, 0xE0, 0x48, 0x89, 0xD1, 0x4D,
            0x89, 0xD0, 0x48, 0x31, 0xD2, 0x66, 0x41, 0x8B,
            0x52, 0x0C, 0x49, 0x29, 0xD0, 0x41, 0x8B, 0x52,
            0x08, 0x41, 0xFF, 0x12, 0x48, 0x8B, 0x4D, 0xF8,
            0x48, 0x8B, 0x55, 0xF0, 0x4C, 0x8B, 0x45, 0xE8,
            0x4C, 0x8B, 0x4D, 0xE0, 0x48, 0x8D, 0x65, 0x00,
            0x5D, 0xFF, 0xE0,
        };
#endregion
#region pre2t
        /*
pop r10 ; r10 isn't used to pass arguments on Windows; it now contains the return position

; setup stack frame
push rbp
sub rsp, 40h ; 4 qword (20h) + 3 qword arguments (18h) + some buffer ~~+ 4 128-bit vectors~~
lea rbp, [rsp + 40h]

; save register-passed arguments, specifically in the top of our allocated range
mov [rbp - 8h], rcx
mov [rbp - 10h], rdx
mov [rbp - 18h], r8
mov [rbp - 20h], r9
; if we assume we never do floating point math, this should be fine... i hope
;movdqa [rbp - 30h], xmm0
;movdqa [rbp - 40h], xmm1
;movdqa [rbp - 50h], xmm2
;movdqa [rbp - 60h], xmm3

; setup call
; the methods being called here have no strangeness, only user args passed in register

; pass the this arg to the thunk
mov r9, rcx

; second arg is the generic context, in rdx
mov rcx, rdx

; the third arg is the source start
mov r8, r10 ; r8 is the argument we want it in
xor rdx, rdx
mov dx, word [r10 + 12] ; offset of the call insn length
sub r8, rdx

; the second arg is the index
;xor rdx, rdx
mov edx, [r10 + 8] ; offset of the index

; finally call handler
call [r10]

; rax now contains our target method, but we need to re-load our arguments
mov rcx, [rbp - 8h]
mov rdx, [rbp - 10h]
mov r8, [rbp - 18h]
mov r9, [rbp - 20h]
;movdqa xmm0, [rbp - 30h]
;movdqa xmm1, [rbp - 40h]
;movdqa xmm2, [rbp - 50h]
;movdqa xmm3, [rbp - 60h]

; clean up our stack frame
lea rsp, [rbp]
pop rbp

; we're finally ready to call our target
jmp rax

         */
        private static readonly byte[] precall_2t = {
            0x41, 0x5A, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48,
            0x8D, 0x6C, 0x24, 0x40, 0x48, 0x89, 0x4D, 0xF8,
            0x48, 0x89, 0x55, 0xF0, 0x4C, 0x89, 0x45, 0xE8,
            0x4C, 0x89, 0x4D, 0xE0, 0x49, 0x89, 0xC9, 0x48,
            0x89, 0xD1, 0x4D, 0x89, 0xD0, 0x48, 0x31, 0xD2,
            0x66, 0x41, 0x8B, 0x52, 0x0C, 0x49, 0x29, 0xD0,
            0x41, 0x8B, 0x52, 0x08, 0x41, 0xFF, 0x12, 0x48,
            0x8B, 0x4D, 0xF8, 0x48, 0x8B, 0x55, 0xF0, 0x4C,
            0x8B, 0x45, 0xE8, 0x4C, 0x8B, 0x4D, 0xE0, 0x48,
            0x8D, 0x65, 0x00, 0x5D, 0xFF, 0xE0,
        };
#endregion
#region pre3
        /*
pop r10 ; r10 isn't used to pass arguments on Windows; it now contains the return position

; setup stack frame
push rbp
sub rsp, 40h ; 4 qword (20h) + 3 qword arguments (18h) + some buffer ~~+ 4 128-bit vectors~~
lea rbp, [rsp + 40h]

; save register-passed arguments, specifically in the top of our allocated range
mov [rbp - 8h], rcx
mov [rbp - 10h], rdx
mov [rbp - 18h], r8
mov [rbp - 20h], r9
; if we assume we never do floating point math, this should be fine... i hope
;movdqa [rbp - 30h], xmm0
;movdqa [rbp - 40h], xmm1
;movdqa [rbp - 50h], xmm2
;movdqa [rbp - 60h], xmm3

; setup call
; the methods being called here have no strangeness, only user args passed in register

; pass the this arg to the thunk
mov r9, rcx

; third arg is the generic context, in r8
mov rcx, r8

; the third arg is the source start
mov r8, r10 ; r8 is the argument we want it in
xor rdx, rdx
mov dx, [r10 + 12] ; offset of the call insn length
sub r8, rdx

; the second arg is the index
;xor rdx, rdx
mov edx, [r10 + 8] ; offset of the index

; finally call handler
call [r10]

; rax now contains our target method, but we need to re-load our arguments
mov rcx, [rbp - 8h]
mov rdx, [rbp - 10h]
mov r8, [rbp - 18h]
mov r9, [rbp - 20h]
;movdqa xmm0, [rbp - 30h]
;movdqa xmm1, [rbp - 40h]
;movdqa xmm2, [rbp - 50h]
;movdqa xmm3, [rbp - 60h]

; clean up our stack frame
lea rsp, [rbp]
pop rbp

; we're finally ready to call our target
jmp rax
         */
        private static readonly byte[] precall_3 = {
            0x41, 0x5A, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48,
            0x8D, 0x6C, 0x24, 0x40, 0x48, 0x89, 0x4D, 0xF8,
            0x48, 0x89, 0x55, 0xF0, 0x4C, 0x89, 0x45, 0xE8,
            0x4C, 0x89, 0x4D, 0xE0, 0x49, 0x89, 0xC9, 0x4C,
            0x89, 0xC1, 0x4D, 0x89, 0xD0, 0x48, 0x31, 0xD2,
            0x66, 0x41, 0x8B, 0x52, 0x0C, 0x49, 0x29, 0xD0,
            0x41, 0x8B, 0x52, 0x08, 0x41, 0xFF, 0x12, 0x48,
            0x8B, 0x4D, 0xF8, 0x48, 0x8B, 0x55, 0xF0, 0x4C,
            0x8B, 0x45, 0xE8, 0x4C, 0x8B, 0x4D, 0xE0, 0x48,
            0x8D, 0x65, 0x00, 0x5D, 0xFF, 0xE0,
        };
#endregion

#region jmp block
        private static readonly byte[] jmp_long = {
            0xFF, 0x25, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0x25, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0x25, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0x25, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0x25, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0x25, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0x25, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0x25, 0x00, 0x00, 0x00, 0x00,
        };
#endregion

#endregion

#region Thunk executable segment
        private struct ConstThunkMemory {
            public IntPtr MemStart;

            public IntPtr Pre1;
            public IntPtr Pre2;
            public IntPtr Pre2T;
            public IntPtr Pre3;

            public IntPtr CC_TRG_RG;
            public IntPtr CC_TR_RG;
            public IntPtr CC_RG_RG;
            public IntPtr CC_G_G;
            public IntPtr CC_TG_G;
            public IntPtr CC_T_G;

            public IntPtr C1_T_MT;
            public IntPtr C1_T_MD;

            public IntPtr C1_MT_MT;
            public IntPtr C1_MT_MD;
            public IntPtr C1_MD_MT;
            public IntPtr C1_MD_MD;

            public IntPtr C2_MT_MT;
            public IntPtr C2_MT_MD;
            public IntPtr C2_MD_MT;
            public IntPtr C2_MD_MD;
            public IntPtr C2_MDT_MT;
            public IntPtr C2_MDT_MD;

            public IntPtr C3_MDT_MT;
            public IntPtr C3_MDT_MD;
        }

        private static uint RoundLength(uint len, uint amt = 16) {
            return ((len / amt) + 1) * amt;
        }

        private static unsafe ConstThunkMemory BuildThunkMemory() {
            uint allocSize = 
                RoundLength((uint) precall_1.Length) + 
                RoundLength((uint) precall_2.Length) +
                RoundLength((uint) precall_2t.Length) +
                RoundLength((uint) precall_3.Length) +
                RoundLength((uint) cconv_g_g.Length) +
                RoundLength((uint) cconv_rg_rg.Length) +
                RoundLength((uint) cconv_tg_g.Length) +
                RoundLength((uint) cconv_trg_rg.Length) +
                RoundLength((uint) cconv_tr_rg.Length) +
                RoundLength((uint) cconv_t_g.Length) + 
                (RoundLength((uint) call_p1_form.Length) * 6) + // there are 6 variants of the C1 thunk
                (RoundLength((uint) call_p2_form.Length) * 4) + //           4 variants of the C2 thunk
                (RoundLength((uint) call_p2t_form.Length) * 2) + //          2 variants of the C2t thunk
                (RoundLength((uint) call_p3_form.Length) * 2) + //       and 2 variants of the C3 thunk
                RoundLength(6u * 8) + // there are 8 fixup targets, which requires 8 6-byte absolute jumps
                (8u * 8);             //       and 8 absolute jump targets

            IntPtr alloc = DetourHelper.Native.MemAlloc(allocSize);
            DetourHelper.Native.MakeWritable(alloc, allocSize);

            byte* data = (byte*) alloc;
            for (uint i = 0; i < allocSize / 4; i++)
                ((uint*)data)[i] = 0xCCCCCCCCu; // fill with 0xCC

            ConstThunkMemory mem = new() { MemStart = alloc };

            static unsafe byte* CopyToData(out IntPtr memTarget, byte* data, byte[] thunkSrc) {
                fixed (byte* thunk = thunkSrc) {
                    memTarget = (IntPtr) data;
                    Copy(thunk, data, (uint) thunkSrc.Length);
                }
                return data + RoundLength((uint) thunkSrc.Length);
            }

            data = CopyToData(out mem.Pre1, data, precall_1);
            data = CopyToData(out mem.Pre2, data, precall_2);
            data = CopyToData(out mem.Pre2T, data, precall_2t);
            data = CopyToData(out mem.Pre3, data, precall_3);
            data = CopyToData(out mem.CC_G_G, data, cconv_g_g);
            data = CopyToData(out mem.CC_RG_RG, data, cconv_rg_rg);
            data = CopyToData(out mem.CC_TG_G, data, cconv_tg_g);
            data = CopyToData(out mem.CC_TRG_RG, data, cconv_trg_rg);
            data = CopyToData(out mem.CC_TR_RG, data, cconv_tr_rg);
            data = CopyToData(out mem.CC_T_G, data, cconv_t_g);

            // copy in all variants of C1
            data = CopyToData(out mem.C1_T_MT, data, call_p1_form);
            data = CopyToData(out mem.C1_T_MD, data, call_p1_form);
            data = CopyToData(out mem.C1_MT_MT, data, call_p1_form);
            data = CopyToData(out mem.C1_MT_MD, data, call_p1_form);
            data = CopyToData(out mem.C1_MD_MT, data, call_p1_form);
            data = CopyToData(out mem.C1_MD_MD, data, call_p1_form);
            // all variants of C2
            data = CopyToData(out mem.C2_MT_MT, data, call_p2_form);
            data = CopyToData(out mem.C2_MT_MD, data, call_p2_form);
            data = CopyToData(out mem.C2_MD_MT, data, call_p2_form);
            data = CopyToData(out mem.C2_MD_MD, data, call_p2_form);
            // all variants of C2t
            data = CopyToData(out mem.C2_MDT_MT, data, call_p2t_form);
            data = CopyToData(out mem.C2_MDT_MD, data, call_p2t_form);
            // all variants of C3
            data = CopyToData(out mem.C3_MDT_MT, data, call_p3_form);
            data = CopyToData(out mem.C3_MDT_MD, data, call_p3_form);

            // calculate offsets to jump block offsets
            const int rel_t_mt = 2;
            const int rel_t_md = rel_t_mt + 6;
            const int rel_mt_mt = rel_t_md + 6;
            const int rel_mt_md = rel_mt_mt + 6;
            const int rel_md_mt = rel_mt_md + 6;
            const int rel_md_md = rel_md_mt + 6;
            const int rel_mdt_mt = rel_md_md + 6;
            const int rel_mdt_md = rel_mdt_mt + 6;
            // copy in jump block
            data = CopyToData(out IntPtr jumpBlock, data, jmp_long);

            {
                static void WriteOffset(IntPtr fbase, int foffs, IntPtr tbase, int toffs) {
                    long diff = (long) tbase - (long) fbase;
                    diff -= foffs;
                    diff += toffs;
                    diff -= 4; // sizeof the offset (since its relative to the end of the insn

                    fbase.Write(ref foffs, (uint) (int) diff);
                }

                // write offsets to jump block into C* variants
                WriteOffset(mem.C1_T_MT, call_p1_from_fix_fn_offs, jumpBlock, rel_t_mt - 2);
                WriteOffset(mem.C1_T_MD, call_p1_from_fix_fn_offs, jumpBlock, rel_t_md - 2);
                WriteOffset(mem.C1_MT_MT, call_p1_from_fix_fn_offs, jumpBlock, rel_mt_mt - 2);
                WriteOffset(mem.C1_MT_MD, call_p1_from_fix_fn_offs, jumpBlock, rel_mt_md - 2);
                WriteOffset(mem.C1_MD_MT, call_p1_from_fix_fn_offs, jumpBlock, rel_md_mt - 2);
                WriteOffset(mem.C1_MD_MD, call_p1_from_fix_fn_offs, jumpBlock, rel_md_md - 2);

                WriteOffset(mem.C2_MT_MT, call_p2_from_fix_fn_offs, jumpBlock, rel_mt_mt - 2);
                WriteOffset(mem.C2_MT_MD, call_p2_from_fix_fn_offs, jumpBlock, rel_mt_md - 2);
                WriteOffset(mem.C2_MD_MT, call_p2_from_fix_fn_offs, jumpBlock, rel_md_mt - 2);
                WriteOffset(mem.C2_MD_MD, call_p2_from_fix_fn_offs, jumpBlock, rel_md_md - 2);
                WriteOffset(mem.C2_MDT_MT, call_p2t_from_fix_fn_offs, jumpBlock, rel_mdt_mt - 2);
                WriteOffset(mem.C2_MDT_MD, call_p2t_from_fix_fn_offs, jumpBlock, rel_mdt_md - 2);

                WriteOffset(mem.C3_MDT_MT, call_p3_from_fix_fn_offs, jumpBlock, rel_mdt_mt - 2);
                WriteOffset(mem.C3_MDT_MD, call_p3_from_fix_fn_offs, jumpBlock, rel_mdt_md - 2);
            }

            {
                static void WriteOffset(IntPtr ptr, int offsLoc, int offs) {
                    ptr.Write(ref offsLoc, (uint) (offs - offsLoc - 4));
                }

                // write jump targets and offsets
                int offs = (int) (data - ((byte*) jumpBlock));
                WriteOffset(jumpBlock, rel_t_mt, offs);
                jumpBlock.Write(ref offs, (ulong) FixCtxThis2MTTarget);

                WriteOffset(jumpBlock, rel_t_md, offs);
                jumpBlock.Write(ref offs, (ulong) FixCtxThis2MDTarget);

                WriteOffset(jumpBlock, rel_mt_mt, offs);
                jumpBlock.Write(ref offs, (ulong) FixCtxMT2MTTarget);

                WriteOffset(jumpBlock, rel_mt_md, offs);
                jumpBlock.Write(ref offs, (ulong) FixCtxMT2MDTarget);

                WriteOffset(jumpBlock, rel_md_mt, offs);
                jumpBlock.Write(ref offs, (ulong) FixCtxMD2MTTarget);

                WriteOffset(jumpBlock, rel_md_md, offs);
                jumpBlock.Write(ref offs, (ulong) FixCtxMD2MDTarget);

                WriteOffset(jumpBlock, rel_mdt_mt, offs);
                jumpBlock.Write(ref offs, (ulong) FixCtxMDT2MTTarget);

                WriteOffset(jumpBlock, rel_mdt_md, offs);
                jumpBlock.Write(ref offs, (ulong) FixCtxMDT2MDTarget);
            }

            DetourHelper.Native.MakeExecutable(alloc, allocSize);
            DetourHelper.Native.FlushICache(alloc, allocSize);

            return mem;
        }

        private static void FreeThunkMemory(ConstThunkMemory mem) {
            DetourHelper.Native.MemFree(mem.MemStart);
        }

        private readonly ConstThunkMemory thunkMemory = BuildThunkMemory();

        ~GenericDetourCoreCLRWinX64() {
            FreeThunkMemory(thunkMemory);
        }
#endregion
#endif

        private static unsafe void Copy(byte* from, byte* to, uint length) {
            for (uint i = 0; i < length / 4; i++) {
                *(uint*) to = *(uint*) from;
                from += 4;
                to += 4;
            }
            if (length % 4 >= 2) {
                *(ushort*) to = *(ushort*) from;
                from += 2;
                to += 2;
            }
            if (length % 2 >= 1) {
                *to = *from;
            }
        }

        private static unsafe void Copy(IntPtr from, IntPtr to, uint length)
            => Copy((byte*) from, (byte*) to, length);

        private enum CallType : byte {
            Rel32,
            Abs64
        }

        private static bool Is32Bit(long to)
            // JMP rel32 is "sign extended to 64-bits"
            => (((ulong) to) & 0x000000007FFFFFFFUL) == ((ulong) to);

        private static CallType FindCallType(IntPtr from, IntPtr to) {
            return Is32Bit((long) to - (long) from) || Is32Bit((long) from - (long) to)
                ? CallType.Rel32
                : CallType.Abs64;
        }

        private static uint GetCallTypeSize(CallType type)
            => type == CallType.Rel32
                ? 5u // call insn
                + 8u // helper method
                + 8u // gchandle
                + 8u // decoder ptr
                // or
                : 6u // call insn
                + 8u // helper method
                + 8u // gchandle
                + 8u // decoder ptr
                + 8u; // call abs address

        // TODO: move as much of this out to CoreCLR shared as possible
        private enum GenericContextPosition {
            Arg1, Arg2, Arg3
        }
        private enum GenericContextKind {
            This, MethodDesc, MethodTable, ThisMethodDesc
        }

        protected override int PlatMaxRegisterArgCount => 4;

        protected override ulong GetFloatRegisterPattern(MethodBase method) {
            ulong result = 0;
            ulong bit = 1;

            // TODO: implement correctly, considering all real arguments
            return result;
        }
        private IntPtr ReadyAssemblyHelperMethod(MethodInfo method, uint bodySize, Action<IntPtr> writeBody) {
            method = method.Pin();
            IntPtr methodBody = method.GetNativeStart();
            DetourHelper.Native.MakeWritable(methodBody, bodySize);
            writeBody(methodBody);
            DetourHelper.Native.MakeExecutable(methodBody, bodySize);
            DetourHelper.Native.FlushICache(methodBody, bodySize);
            return methodBody;
        }


        private MethodInfo platThunkGetReturnTargetHelperLazy = null;
        protected override MethodInfo PlatThunkGetReturnTargetHelper => platThunkGetReturnTargetHelperLazy ??= MakePlatThunkGetReturnTargetHelper();

        private MethodInfo MakePlatThunkGetReturnTargetHelper() {
            MethodInfo method = GetMethodOnSelf(nameof(GetReturnTarget));
            _ = ReadyAssemblyHelperMethod(method, 4, body => {
                int offs = 0;
                body.Write(ref offs, 0xC3D8894C); // { 4c, 89, d8, c3 } = mov rax,r11 ; ret
            });
            return method;
        }

        [MethodImpl(MethodImplOptions.NoInlining | (MethodImplOptions)512)]
        public static IntPtr GetReturnTarget() {
            throw new InvalidOperationException("This code should never have had the opportunity to run!");
        }

        private static IntPtr GetOrMakePtr(ref IntPtr ptr, Func<IntPtr> create) {
            if (ptr == IntPtr.Zero) {
                ptr = create();
            }
            return ptr;
        }

        private IntPtr genericThunkLoc = IntPtr.Zero;
        private IntPtr GenericThunkLoc => GetOrMakePtr(ref genericThunkLoc, () => 
            ReadyAssemblyHelperMethod(GetMethodOnSelf(nameof(GenericThunkLocStore)), 5, body => {
                int offs = 0;
                body.Write(ref offs, 0xff415b41);
                body.Write(ref offs, (byte) 0x23);
                // { 41, 5B, 41, FF, 23 } = pop r11 ; jmp [r11]
            }));

        // We make this a method so that it is more likely that a given detoured generic method is within Rel32 range of this thunk, allowing us to make smaller patchsites
        [MethodImpl(MethodImplOptions.NoInlining | (MethodImplOptions) 512)]
        private static void GenericThunkLocStore() {
            throw new InvalidOperationException("This code should never have had the opportunity to run!");
        }


        private GenericContextPosition GetGenericContextPosition(MethodBase instance) {
            if (TakesGenericsFromThis(instance)) {
                // if we take generics from the this parameter, grab the first given arg
                return GenericContextPosition.Arg1;
            }
            bool needsReturnBuffer = MethodRequiresReturnBuffer(instance);
            if (!instance.IsStatic ^ needsReturnBuffer) {
                // currently we assume that there there is never a return buffer, so always return the thunk assuming that
                // if it takes a this arg OR has a return buffer BUT NOT BOTH, grab the second given arg
                return GenericContextPosition.Arg2;
            } else if (!instance.IsStatic && needsReturnBuffer) {
                // if it takes a this arg AND has a return buffer, grab the third given arg
                return GenericContextPosition.Arg3;
            }
            // otherwise the context is in the first given arg
            return GenericContextPosition.Arg1;
        }

        private static GenericContextKind GetGenericContextKind(MethodBase instance) {
            if (TakesGenericsFromThis(instance))
                return GenericContextKind.This;
            if (RequiresMethodTableArg(instance))
                return GenericContextKind.MethodTable;
            if (RequiresMethodDescArg(instance)) {
                return instance.IsStatic
                    ? GenericContextKind.MethodDesc
                    : GenericContextKind.ThisMethodDesc;
            }
            throw new ArgumentException($"Unknown generic ABI for method {instance}", nameof(instance));
        }

        [MethodImpl(MethodImplOptions.NoInlining | (MethodImplOptions) 512)]
        private static IntPtr ObjectForPtrImpl(IntPtr ptr) => ptr;

        [MethodImpl(MethodImplOptions.NoInlining | (MethodImplOptions) 512)]
        private static unsafe object ObjectForPtr(IntPtr ptr) {
            // we basically just want to reinterpret an IntPtr as an object reference, and this lets us do that
            return ((delegate*<IntPtr, object>) (delegate*<IntPtr, IntPtr>) &ObjectForPtrImpl)(ptr);
        }

        public static MethodBase DecodeContextFromThis(InstantiationPatch patchInfo, IntPtr context)
            => RealInstFromThis(ObjectForPtr(context), patchInfo.OwningPatchInfo);
        public static MethodBase DecodeContextFromMethodTable(InstantiationPatch patchInfo, IntPtr context)
            => ((GenericDetourCoreCLRWinX64) patchInfo.OwningPatchInfo.DetourRuntime).RealInstFromMT(context, patchInfo.OwningPatchInfo);
        public static MethodBase DecodeContextFromMethodDesc(InstantiationPatch patchInfo, IntPtr context)
            => ((GenericDetourCoreCLRWinX64) patchInfo.OwningPatchInfo.DetourRuntime).RealInstFromMD(context);
        public static MethodBase DecodeContextFromThisMethodDesc(InstantiationPatch patchInfo, IntPtr context, IntPtr thisarg)
            => ((GenericDetourCoreCLRWinX64) patchInfo.OwningPatchInfo.DetourRuntime).RealInstFromMDT(ObjectForPtr(thisarg), context, patchInfo.OwningPatchInfo);

        private static MethodBase DecodeGenericContext(InstantiationPatch patchInfo, IntPtr context, IntPtr firstArg)
            => GetGenericContextKind(patchInfo.SourceInstantiation) switch {
                GenericContextKind.This => DecodeContextFromThis(patchInfo, context),
                GenericContextKind.MethodTable => DecodeContextFromMethodTable(patchInfo, context),
                GenericContextKind.MethodDesc => DecodeContextFromMethodDesc(patchInfo, context),
                GenericContextKind.ThisMethodDesc => DecodeContextFromThisMethodDesc(patchInfo, context, firstArg),
                _ => throw new NotImplementedException()
            };

        [MethodImpl(MethodImplOptions.NoInlining | (MethodImplOptions) 512)]
        private static MethodBase PrecallGenericContextDecoder(InstantiationPatch patchInfo, IntPtr arg0, IntPtr arg1, IntPtr arg2, IntPtr arg3) {
            GenericDetourCoreCLRWinX64 self = (GenericDetourCoreCLRWinX64) patchInfo.OwningPatchInfo.DetourRuntime;

            IntPtr genericCtxPtr = self.GetGenericContextPosition(patchInfo.SourceInstantiation) switch {
                GenericContextPosition.Arg1 => arg0,
                GenericContextPosition.Arg2 => arg1,
                GenericContextPosition.Arg3 => arg2,
                _ => throw new InvalidOperationException()
            };

            return DecodeGenericContext(patchInfo, genericCtxPtr, arg0);
        }

        protected override NativeDetourData PatchInstantiation(InstantiationPatch patchInfo, MethodBase orig, MethodBase methodInstance, IntPtr codeStart, int index) {
            if (!MethodIsGenericShared(methodInstance)) {
                return PatchUnshared(methodInstance, codeStart, BuildInstantiationForMethod(patchInfo.OwningPatchInfo.TargetMethod, methodInstance));
            }

            IntPtr thunk = GenericThunkLoc; //GetPrecallThunkForMethod(methodInstance);
            IntPtr handler = GetPrecallHelper(methodInstance).Pin().GetNativeStart(); //GetPrecallHandlerForMethod(methodInstance);
            CallType callType = FindCallType(codeStart, thunk);
            uint callLen = GetCallTypeSize(callType);

            IntPtr ctxDecoder = typeof(GenericDetourCoreCLRWinX64).GetMethod(nameof(PrecallGenericContextDecoder), BindingFlags.NonPublic | BindingFlags.Static)
                .Pin().GetNativeStart();

            // backup the original data
            IntPtr dataBackup = DetourHelper.Native.MemAlloc(callLen);
            Copy(codeStart, dataBackup, callLen);

            NativeDetourData detourData = new() {
                Method = codeStart,
                Target = thunk,
                Type = (byte)callType,
                Size = Math.Max(callLen, CallsiteMaxPatchSize),
                Extra = dataBackup
            };

            DetourHelper.Native.MakeWritable(detourData);

            int idx = 0;
            if (callType == CallType.Rel32) {
                codeStart.Write(ref idx, (byte) 0xE8);
                codeStart.Write(ref idx, (uint) ((ulong) thunk - (ulong) codeStart) - 5);
            } else {
                codeStart.Write(ref idx, (byte) 0xFF);
                codeStart.Write(ref idx, (byte) 0x15);
                codeStart.Write(ref idx, (uint) (8 + 8 + 8)); // offset from end of instruction to end of other data for real address
            }
            codeStart.Write(ref idx, (ulong) handler);
            codeStart.Write(ref idx, (ulong) GCHandle.ToIntPtr(GCHandle.Alloc(patchInfo)));
            codeStart.Write(ref idx, (ulong) ctxDecoder);
            if (callType == CallType.Rel32) {
                //codeStart.Write(ref idx, (ushort) 5); // the rel32 call is 5 bytes
            } else {
                //codeStart.Write(ref idx, (ushort) 6); // the abs64 call is 6 bytes
                codeStart.Write(ref idx, (ulong) thunk); // and needs the thunk's absolute address here
            }

            DetourHelper.Native.MakeExecutable(detourData);
            DetourHelper.Native.FlushICache(detourData);

            return detourData;
        }

        protected override unsafe void UnpatchInstantiation(InstantiationPatch instantiation) {
            NativeDetourData data = instantiation.OriginalData;

            // restore original code
            DetourHelper.Native.MakeWritable(data);

            // make sure to free the gchandle
            CallType callType = (CallType) data.Type;
            IntPtr gcHandle = ((IntPtr*) (data.Method + (callType == CallType.Rel32 ? 5 : 6)))[1];
            GCHandle.FromIntPtr(gcHandle).Free();

            Copy(data.Extra, data.Method, data.Size);
            DetourHelper.Native.MakeExecutable(data);
            DetourHelper.Native.FlushICache(data);

            // be sure to free the associated memory
            DetourHelper.Native.MemFree(data.Extra);
        }

        private const int CallsiteMaxPatchSize =
            6 + // call instruction
            8 + // helper address
            8 + // gchandle of patch info
            8 + // context decoder
            8;  // the real call target, if its an absolute call

#if false
        private IntPtr GetCallThunkForMethod(MethodBase srcMethod, MethodBase target) {
            if (TakesGenericsFromThis(srcMethod)) {
                if (RequiresMethodDescArg(target)) {
                    return thunkMemory.C1_T_MD;
                } else if (RequiresMethodTableArg(target)) {
                    return thunkMemory.C1_T_MT;
                } else {
                    throw new InvalidOperationException("Unknown/unprocessable generic ABI for target");
                }
            }
            if (RequiresMethodDescArg(srcMethod)) {
                if (RequiresMethodDescArg(target)) {
                    return GetGenericContextPositionEnum(srcMethod) switch {
                        GenericContextPosision.Arg1 => thunkMemory.C1_MD_MD,
                        GenericContextPosision.Arg2 => srcMethod.IsStatic ? thunkMemory.C2_MD_MD : thunkMemory.C2_MDT_MD,
                        GenericContextPosision.Arg3 => thunkMemory.C3_MDT_MD,
                        _ => throw new InvalidOperationException("Unknown generic ABI for source or target"),
                    };
                } else if (RequiresMethodTableArg(target)) {
                    return GetGenericContextPositionEnum(srcMethod) switch {
                        GenericContextPosision.Arg1 => thunkMemory.C1_MD_MT,
                        GenericContextPosision.Arg2 => srcMethod.IsStatic ? thunkMemory.C2_MD_MT : thunkMemory.C2_MDT_MT,
                        GenericContextPosision.Arg3 => thunkMemory.C3_MDT_MT,
                        _ => throw new InvalidOperationException("Unknown generic ABI for source or target"),
                    };
                } else {
                    throw new InvalidOperationException("Unknown generic ABI for target");
                }
            } else if (RequiresMethodTableArg(srcMethod)) {
                if (RequiresMethodDescArg(target)) {
                    return GetGenericContextPositionEnum(srcMethod) switch {
                        GenericContextPosision.Arg1 => thunkMemory.C1_MT_MD,
                        GenericContextPosision.Arg2 => thunkMemory.C2_MT_MD,
                        GenericContextPosision.Arg3 => throw new InvalidOperationException("Impossible generic ABI for source; third position method table"),
                        _ => throw new InvalidOperationException("Unknown generic ABI for source or target"),
                    };
                } else if (RequiresMethodTableArg(target)) {
                    return GetGenericContextPositionEnum(srcMethod) switch {
                        GenericContextPosision.Arg1 => thunkMemory.C1_MT_MT,
                        GenericContextPosision.Arg2 => thunkMemory.C2_MT_MT,
                        GenericContextPosision.Arg3 => throw new InvalidOperationException("Impossible generic ABI for source; third position method table"),
                        _ => throw new InvalidOperationException("Unknown generic ABI for source or target"),
                    };
                } else {
                    throw new InvalidOperationException("Unknown generic ABI for target");
                }
            } else {
                throw new InvalidOperationException("Unknown generic ABI source");
            }
        }

        private IntPtr GetCallConvConverterThunkForMethod(MethodBase src, MethodBase target) {
            bool hasRetBuf = MethodRequiresReturnBuffer(src);

            if (src.IsStatic) {
                return hasRetBuf
                    ? thunkMemory.CC_RG_RG
                    : thunkMemory.CC_G_G;
            } else if (TakesGenericsFromThis(src)) {
                return hasRetBuf
                    ? thunkMemory.CC_TR_RG
                    : thunkMemory.CC_T_G;
            } else {
                return hasRetBuf
                    ? thunkMemory.CC_TRG_RG
                    : thunkMemory.CC_TG_G;
            }
        }

        // TODO: make this use new system
        protected override void PatchMethodInst(GenericPatchInfo patch, MethodBase realSrc, IntPtr codeStart) {
            MethodBase realTarget = BuildInstantiationForMethod(patch.TargetMethod, realSrc);

            IntPtr thunk = GetCallThunkForMethod(realSrc, realTarget);
            IntPtr callConvConvert = GetCallConvConverterThunkForMethod(realSrc, realTarget);

            CallType callType = FindCallType(codeStart, thunk);

            ParameterInfo[] parameters = realSrc.GetParameters();
            byte flags = 0;
            ushort pushAdjust = 8; // by default, we want to completely undo the push
            if (parameters.Length >= 4) {
                Type fourthParamType = parameters[3].ParameterType;
                TypeCode typecode = Type.GetTypeCode(fourthParamType);
                if (typecode is TypeCode.Single or TypeCode.Double) {
                    flags |= 0b01;
                }
                if (typecode is TypeCode.Double) {
                    flags |= 0b10;
                }
                if ((flags & 0b11) == 0) { // not a floating point
                    int argSize = fourthParamType.GetManagedSize();
                    if (argSize is not 1 and not 2 and not 4 and not 8) {
                        // it is a value type that gets implicitly-byref'd
                        argSize = 8;
                    }
                    pushAdjust = (ushort) (8 - argSize);
                }
            }

            // we declare this to more easily use MakeWritable and the like
            NativeDetourData detourData = new() {
                Method = codeStart,
                Target = thunk,
                Type = (byte) callType,
                Size = CallsiteMaxPatchSize,
                Extra = IntPtr.Zero
            };

            DetourHelper.Native.MakeWritable(detourData);

            int idx = 0;
            if (callType == CallType.Rel32) {
                codeStart.Write(ref idx, (byte) 0xE8);
                codeStart.Write(ref idx, (uint) ((ulong) codeStart - (ulong) thunk));
            } else {
                codeStart.Write(ref idx, (byte) 0xFF);
                codeStart.Write(ref idx, (byte) 0x15);
                codeStart.Write(ref idx, (uint) (8 + 8 + 4 + 3)); // offset from end of instruction to end of other data for real address
            }
            codeStart.Write(ref idx, (ulong) callConvConvert);
            codeStart.Write(ref idx, (ulong) GetSharedMethodBody(realTarget));
            codeStart.Write(ref idx, (uint) patch.Index);
            codeStart.Write(ref idx, flags);
            codeStart.Write(ref idx, pushAdjust);
            if (callType == CallType.Abs64) {
                codeStart.Write(ref idx, (ulong) thunk); // and needs the thunk's absolute address here
            }

            DetourHelper.Native.MakeExecutable(detourData);
            DetourHelper.Native.FlushICache(detourData);
        }
#endif

        protected override unsafe IntPtr GetSharedMethodBody(MethodBase method) {
            IntPtr methodDesc = method.MethodHandle.Value;

            const int offsToWrappedMd =
                2 + // m_wFlags3AndTokenRemainder
                1 + // m_chunkIndex
                1 + // m_bFlags2
                2 + // m_wSlotNumber
                2 + // m_wFlags
                // InstantiatedMethodDesc
                0   ;
            const int offsToWFlags2 =
                offsToWrappedMd +
                8 + // m_pWrappedMethodDesc
                8 + // m_pPerInstInfo
                0;

            const int instantiationKindMask = 0x07;
            const int kindWrapperStub = 0x03;
            const int kindSharedInst = 0x02;

            short flags = *(short*) (((byte*) methodDesc) + offsToWFlags2);
            int kind = flags & instantiationKindMask;

            if (kind != kindWrapperStub) {
                throw new InvalidOperationException("Unprocessable instantiation kind");
            }

            IntPtr sharedMd = *(IntPtr*) (((byte*) methodDesc) + offsToWrappedMd);
            MethodBase sharedInstance = MethodBase.GetMethodFromHandle(netPlatform.CreateHandleForHandlePointer(sharedMd));

            return sharedInstance.GetNativeStart();
        }

        private static unsafe void CheckInstantiatedMethodKind(IntPtr md) {
            const int offsToWFlags =
                2 + // m_wFlags3AndTokenRemainder
                1 + // m_chunkIndex
                1 + // m_bFlags2
                2 + // m_wSlotNumber
                0;
            const int offsToWrappedMd =
                offsToWFlags +
                2 + // m_wFlags
                    // InstantiatedMethodDesc
                0;
            const int offsToWFlags2 =
                offsToWrappedMd +
                8 + // m_pWrappedMethodDesc
                8 + // m_pPerInstInfo
                0;

            const int mdcClassification = 7;
            const int mcInstantiated = 5;

            short wflags1 = *(short*) (((byte*) md) + offsToWFlags);
            int klass = wflags1 & mdcClassification;

            if (klass != mcInstantiated) {
                throw new InvalidOperationException($"MD is not InstantiatedMethodDesc; instead class {klass}");
            }

            const int instantiationKindMask = 0x07;
            const int kindWrapperStub = 0x03;
            const int kindSharedInst = 0x02;

            short flags = *(short*) (((byte*) md) + offsToWFlags2);
            int kind = flags & instantiationKindMask;

            if (kind != kindWrapperStub) {
                throw new InvalidOperationException("Invalid instantiation kind");
            }
        }

        private GenericContextInfo GetGenericContextInfo(MethodBase method)
            => new(GetGenericContextPosition(method), GetGenericContextKind(method));
        private static byte EncodeMethodGenericABI(GenericContextInfo info)
            => (byte)((((byte)info.Position) & 0x3) | ((((byte)info.Kind) & 0x3) << 2));
        private static GenericContextInfo DecodeMethodGenericABI(byte data) {
            return new((GenericContextPosition) (data & 0x3), (GenericContextKind) ((data >> 2) & 0x3));
        }

        protected override MethodInfo GetCallHelperFor(InstantiationPatch patch, MethodBase realSrc, MethodBase realTarget) {
            GenericContextInfo srcInfo = GetGenericContextInfo(realSrc);
            GenericContextInfo dstInfo = GetGenericContextInfo(realTarget);

            int kind = EncodeMethodGenericABI(srcInfo) | (EncodeMethodGenericABI(dstInfo) << 4);
            kind |= NeedsStackSpill(realSrc, srcInfo, realTarget, dstInfo) ? 0x100 : 0;
            return GetCallHelper(realSrc, kind);
        }

        private static void ValidateCtxPositionKind(GenericContextInfo info) {
            switch (info.Kind) {
                case GenericContextKind.This:
                    if (info.Position is not GenericContextPosition.Arg1)
                        throw new InvalidOperationException("A generic context kind of This must be in position 1");
                    break;
                case GenericContextKind.MethodTable:
                    break; // a MethodTable can be in any position I believe
                case GenericContextKind.MethodDesc:
                    break; // a MethodDesc can be in any position I believe
                case GenericContextKind.ThisMethodDesc:
                    if (info.Position is not GenericContextPosition.Arg2 or GenericContextPosition.Arg3)
                        throw new InvalidOperationException("A generic context kind of ThisMethodDesc must be in position 2 or 3");
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private struct GenericContextInfo {
            public readonly GenericContextPosition Position;
            public readonly GenericContextKind Kind;
            public GenericContextInfo(GenericContextPosition pos, GenericContextKind kind) {
                Position = pos;
                Kind = kind;
            }
            public override string ToString() => $"{Position}:{Kind}";
        }

        private struct GenericContextInfoPair {
            public readonly GenericContextInfo Src;
            public readonly GenericContextInfo Dst;
            public GenericContextInfoPair(GenericContextInfo src, GenericContextInfo dst) {
                Src = src;
                Dst = dst;
            }
            public override string ToString() => $"{Src}->{Dst}";
        }

        private static int GetGenericContextPositionIndex(GenericContextPosition pos)
            => pos switch {
                GenericContextPosition.Arg1 => 0,
                GenericContextPosition.Arg2 => 1,
                GenericContextPosition.Arg3 => 2,
                _ => throw new NotImplementedException()
            };

        private static bool NeedsStackSpill(MethodBase src, GenericContextInfo srcInfo, MethodBase dst, GenericContextInfo dstInfo) {
            switch (new GenericContextInfoPair(srcInfo, dstInfo)) {
                case { Src: { Kind: GenericContextKind.MethodDesc }, Dst: { Kind: GenericContextKind.MethodDesc } }:
                    // a MD->MD always has the same number of arguments, only reordering is needed
                    return false;
                case { Src: { Kind: GenericContextKind.MethodTable }, Dst: { Kind: GenericContextKind.MethodDesc } }:
                    // a MT->MD always has the same number of arguments, only reordering is needed
                    return false;
                case { Src: { Kind: GenericContextKind.ThisMethodDesc }, Dst: { Kind: GenericContextKind.MethodDesc } }:
                    // a MDT->MD always has the same number of arguments, only reordering is needed
                    return false;
                case { Src: { Kind: GenericContextKind.This }, Dst: { Kind: GenericContextKind.MethodDesc } }:
                    if (dstInfo.Position is GenericContextPosition.Arg1) {
                        // no return buffer, conversion is
                        //   t x y z
                        //   g t x y z
                        return src.GetParameters().Length >= 3;
                    } else if (dstInfo.Position is GenericContextPosition.Arg2) {
                        // return buffer, conversion is
                        //   t r x y
                        //   r g t x y
                        return src.GetParameters().Length >= 2;
                    } else {
                        throw new InvalidOperationException("Somehow got inconsistent information");
                    }

                case GenericContextInfoPair p:
                    throw new NotImplementedException(p.ToString());
            }
        }

        protected override CCallSite EmitArgumentFixupForCall(
            ModuleDefinition module, MethodDefinition method, ILProcessor il, VariableDefinition instantiationPatchVar,
            VariableDefinition jumpTargetVar, ulong floatRegPattern, int kind) {
            // we need to:
            // 1. decode the generic context (based on kind)
            // 2. reconstruct a generic context for the target method (based on kind)
            // 3. load and reorder arguments such that they are in the correct places for the finall call
            // 4. build and return a CCallSite for the final call

            GenericContextInfo srcInfo = DecodeMethodGenericABI((byte) (kind & 0xf));
            GenericContextInfo dstInfo = DecodeMethodGenericABI((byte) ((kind >> 4) & 0xf));
            bool hasStackSpill = (kind & 0x100) != 0; // see NeedsStackSpill

            // these are just sanity checks
            ValidateCtxPositionKind(srcInfo);
            ValidateCtxPositionKind(dstInfo);

            if (dstInfo.Kind is GenericContextKind.This or GenericContextKind.ThisMethodDesc)
                throw new InvalidOperationException("Destination context kind is not supported");

            // first we want to decode the generic context
            // load instantiation patch info
            il.Emit(OpCodes.Ldloc, instantiationPatchVar);
            // load context arg
            il.Emit(OpCodes.Ldarg, method.Parameters[GetGenericContextPositionIndex(srcInfo.Position)]);
            if (srcInfo.Kind is GenericContextKind.ThisMethodDesc) {
                // load thisarg if needed
                il.Emit(OpCodes.Ldarg, method.Parameters[0]);
            }
            il.Emit(OpCodes.Call, module.ImportReference(srcInfo.Kind switch {
                GenericContextKind.This => DecodeFromThisMeth,
                GenericContextKind.MethodTable => DecodeFromMethodTableMeth,
                GenericContextKind.MethodDesc => DecodeFromMethodDescMeth,
                GenericContextKind.ThisMethodDesc => DecodeFromThisMethodDescMeth,
                _ => throw new NotImplementedException()
            }));

            // there is now a MethodBase for the real instantiation on the stack

            il.Emit(OpCodes.Ldloc, instantiationPatchVar); // load the instantiation patch to convert the method
            il.Emit(OpCodes.Call, module.ImportReference(FindRealTargetMeth));

            // then we build up the new generic context
            il.Emit(OpCodes.Call, module.ImportReference(dstInfo.Kind switch {
                GenericContextKind.MethodTable => RealTargetToMethodTableMeth,
                GenericContextKind.MethodDesc => RealTargetToMethodDescProxyMeth,
                _ => throw new NotImplementedException()
            }));

            // now we just have the real generic context on stack, lets save it so we can do the reordering
            VariableDefinition realGCtx = new(module.ImportReference(typeof(IntPtr)));
            method.Body.Variables.Add(realGCtx);

            il.Emit(OpCodes.Stloc, realGCtx);

            // the stack is once again empty

            // and finally, we load all our arguments in the appropriate order
            switch (new GenericContextInfoPair(srcInfo, dstInfo)) {
                #region Transforms, no return buffer
                case // MD->MD static source no return buffer
                {
                    Src: { Kind: GenericContextKind.MethodDesc, Position: GenericContextPosition.Arg1 },
                    Dst: { Kind: GenericContextKind.MethodDesc, Position: GenericContextPosition.Arg1 }
                }:
                case // MT->MD static source no return buffer
                {
                    Src: { Kind: GenericContextKind.MethodTable, Position: GenericContextPosition.Arg1 },
                    Dst: { Kind: GenericContextKind.MethodDesc, Position: GenericContextPosition.Arg1 }
                }: {
                        // this is the following transformation:
                        //   g x y z
                        //   g x y z

                        CCallSite callSite = new(method.ReturnType);
                        // load gctx
                        callSite.Parameters.Add(new(realGCtx.VariableType));
                        il.Emit(OpCodes.Ldloc, realGCtx);
                        // then all remaining arguments
                        foreach (ParameterDefinition param in method.Parameters.Skip(1)) {
                            callSite.Parameters.Add(new(param.ParameterType));
                            il.Emit(OpCodes.Ldarg, param);
                        }

                        // we never have to deal with stack spillage, so we're done
                        return callSite;
                    }
                case // MDT->MD no return buffer
                {
                    Src: { Kind: GenericContextKind.ThisMethodDesc, Position: GenericContextPosition.Arg2 },
                    Dst: { Kind: GenericContextKind.MethodDesc, Position: GenericContextPosition.Arg1 }
                }: {
                        // this is the following transformation:
                        //   t g x y
                        //   g t x y

                        CCallSite callSite = new(method.ReturnType);
                        // load gctx
                        callSite.Parameters.Add(new(realGCtx.VariableType));
                        il.Emit(OpCodes.Ldloc, realGCtx);
                        // then load the this ptr
                        callSite.Parameters.Add(new(method.Parameters.First().ParameterType));
                        il.Emit(OpCodes.Ldarg, method.Parameters.First());
                        // then load everything else
                        foreach (ParameterDefinition param in method.Parameters.Skip(2)) {
                            callSite.Parameters.Add(new(param.ParameterType));
                            il.Emit(OpCodes.Ldarg, param);
                        }

                        // we never have to deal with stack spillage, so we're done
                        return callSite;
                    }
                case // T->MD no return buffer
                {
                    Src: { Kind: GenericContextKind.This },
                    Dst: { Kind: GenericContextKind.MethodDesc, Position: GenericContextPosition.Arg1 }
                }: {
                        // transform from This to MethodDesc with no return buffer (because Dst.Position is Arg1)
                        // this is the following transformation:
                        //   t x y z
                        //   g t x y z

                        CCallSite callSite = new(method.ReturnType);
                        // first is our generic context
                        callSite.Parameters.Add(new(realGCtx.VariableType));
                        il.Emit(OpCodes.Ldloc, realGCtx);
                        // then is all of the original arguments
                        foreach (ParameterDefinition param in method.Parameters) {
                            callSite.Parameters.Add(new(param.ParameterType));
                            il.Emit(OpCodes.Ldarg, param);
                        }

                        TypeReference lastParamType = callSite.Parameters.Last().ParameterType;
                        callSite.Parameters.RemoveAt(callSite.Parameters.Count - 1);

                        // if we might stack spill, pull the last param into a helper with the given jump target
                        //   and switch to using our special helper jump target
                        if (hasStackSpill) {
                            il.Emit(OpCodes.Ldloc, jumpTargetVar);
                            il.Emit(OpCodes.Call, module.ImportReference(
                                lastParamType.Name == nameof(IntPtr) // switch on whether or not this param is a ptr or not
                                    ? StackSpillStoreHelperPtrMeth
                                    : throw new NotImplementedException()));
                            il.Emit(OpCodes.Ldc_I8, (long)StackSpillCallHelperPtrPtr);
                            il.Emit(OpCodes.Conv_I);
                            il.Emit(OpCodes.Stloc, jumpTargetVar);
                        } else {
                            // remove the extra argument on stack
                            il.Emit(OpCodes.Pop);
                        }

                        // and we're done
                        return callSite;
                    }
                #endregion
                #region Transforms, return buffers
                case // MD->MD static source return buffer
                {
                    Src: { Kind: GenericContextKind.MethodDesc, Position: GenericContextPosition.Arg2 },
                    Dst: { Kind: GenericContextKind.MethodDesc, Position: GenericContextPosition.Arg2 }
                }:
                case // MT->MD static source return buffer
                {
                    Src: { Kind: GenericContextKind.MethodTable, Position: GenericContextPosition.Arg2 },
                    Dst: { Kind: GenericContextKind.MethodDesc, Position: GenericContextPosition.Arg2 }
                }: {
                        // this is the following transformation:
                        //   r g x y
                        //   r g x y

                        CCallSite callSite = new(method.ReturnType);
                        // load return buffer
                        callSite.Parameters.Add(new(method.Parameters.First().ParameterType));
                        il.Emit(OpCodes.Ldarg, callSite.Parameters.First());
                        // load gctx
                        callSite.Parameters.Add(new(realGCtx.VariableType));
                        il.Emit(OpCodes.Ldloc, realGCtx);
                        // then all remaining arguments
                        foreach (ParameterDefinition param in method.Parameters.Skip(2)) {
                            callSite.Parameters.Add(new(param.ParameterType));
                            il.Emit(OpCodes.Ldarg, param);
                        }

                        // we never have to deal with stack spillage, so we're done
                        return callSite;
                    }
                case // T->MD return buffer
                {
                    Src: { Kind: GenericContextKind.This },
                    Dst: { Kind: GenericContextKind.MethodDesc, Position: GenericContextPosition.Arg2 }
                }: {
                        // transform from This to MethodDesc with return buffer (because Dst.Position is Arg2)
                        // this is the following transformation:
                        //   t r x y
                        //   r g t x y

                        CCallSite callSite = new(method.ReturnType);
                        // first is our return buffer
                        ParameterDefinition rbParam = method.Parameters.Skip(1).First();
                        callSite.Parameters.Add(new(rbParam.ParameterType));
                        il.Emit(OpCodes.Ldarg, rbParam);
                        // then our generic context
                        callSite.Parameters.Add(new(realGCtx.VariableType));
                        il.Emit(OpCodes.Ldloc, realGCtx);
                        // then our this pointer
                        callSite.Parameters.Add(new(method.Parameters.First().ParameterType));
                        il.Emit(OpCodes.Ldarg, method.Parameters.First());
                        // then is the rest of the arguments
                        foreach (ParameterDefinition param in method.Parameters.Skip(2)) {
                            callSite.Parameters.Add(new(param.ParameterType));
                            il.Emit(OpCodes.Ldarg, param);
                        }

                        // TODO: pull out this common stack spill logic

                        TypeReference lastParamType = callSite.Parameters.Last().ParameterType;
                        callSite.Parameters.RemoveAt(callSite.Parameters.Count - 1);

                        // if we might stack spill, pull the last param into a helper with the given jump target
                        //   and switch to using our special helper jump target
                        if (hasStackSpill) {
                            il.Emit(OpCodes.Ldloc, jumpTargetVar);
                            il.Emit(OpCodes.Call, module.ImportReference(
                                lastParamType.Name == nameof(IntPtr) // switch on whether or not this param is a ptr or not
                                    ? StackSpillStoreHelperPtrMeth
                                    : throw new NotImplementedException()));
                            il.Emit(OpCodes.Ldc_I8, (long) StackSpillCallHelperPtrPtr);
                            il.Emit(OpCodes.Conv_I);
                            il.Emit(OpCodes.Stloc, jumpTargetVar);
                        } else {
                            // remove the extra argument on stack
                            il.Emit(OpCodes.Pop);
                        }

                        // and we're done
                        return callSite;
                    }
                #endregion

                // TODO: implement more transformations

                case GenericContextInfoPair p: throw new NotImplementedException(p.ToString());
            }

            // TODO: implement
            throw new NotImplementedException();
        }

        private MethodInfo stackSpillStoreHelperPtrLazy = null;
        private MethodInfo StackSpillStoreHelperPtrMeth => stackSpillStoreHelperPtrLazy ??= MakeStackSpillStoreHelperPtr();

        private MethodInfo MakeStackSpillStoreHelperPtr() {
            MethodInfo method = GetMethodOnSelf(nameof(StackSpillStoreHelperPtr));
            _ = ReadyAssemblyHelperMethod(method, 7, body => {
                int offs = 0;
                body.Write(ref offs, 0x49ca8949);
                body.Write(ref offs, (ushort) 0xde89);
                body.Write(ref offs, (byte) 0xc3);
                // { 49, 89, ca, 49, 89, d3, c3 } = mov r10,rcx ; mov r11,rdx ; ret
            });
            return method;
        }

        // We make this a method so that it is more likely that a given detoured generic method is within Rel32 range of this thunk, allowing us to make smaller patchsites
        [MethodImpl(MethodImplOptions.NoInlining | (MethodImplOptions) 512)]
        public static void StackSpillStoreHelperPtr(IntPtr value, IntPtr jmpTarget) {
            throw new InvalidOperationException("This should never execute");
        }

        private IntPtr stackSpillCallHelperPtrPtr = IntPtr.Zero;
        private IntPtr StackSpillCallHelperPtrPtr => GetOrMakePtr(ref stackSpillCallHelperPtrPtr, () =>
            ReadyAssemblyHelperMethod(GetMethodOnSelf(nameof(StackSpillCallHelperPtr)), 7, body => {
                int offs = 0;
                body.Write(ref offs, 0x50524158);
                body.Write(ref offs, (ushort) 0xff41);
                body.Write(ref offs, (byte) 0xe3);
                // { 58, 41, 52, 50, 41, ff, e3 } = pop rax ; push r10 ; push rax ; jmp r11
            }));

        // We make this a method so that it is more likely that a given detoured generic method is within Rel32 range of this thunk, allowing us to make smaller patchsites
        [MethodImpl(MethodImplOptions.NoInlining | (MethodImplOptions) 512)]
        private static void StackSpillCallHelperPtr(IntPtr value, IntPtr jmpTarget) {
            throw new InvalidOperationException("This should never execute");
        }

        // TODO: add variants of the above for float arguments

        private static MethodInfo GetMethodOnSelf(string name)
            => typeof(GenericDetourCoreCLRWinX64)
                        .GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo DecodeFromThisMeth = GetMethodOnSelf(nameof(DecodeContextFromThis));
        private static readonly MethodInfo DecodeFromMethodTableMeth = GetMethodOnSelf(nameof(DecodeContextFromMethodTable));
        private static readonly MethodInfo DecodeFromMethodDescMeth = GetMethodOnSelf(nameof(DecodeContextFromMethodDesc));
        private static readonly MethodInfo DecodeFromThisMethodDescMeth = GetMethodOnSelf(nameof(DecodeContextFromThisMethodDesc));

        private static MethodInfo FindRealTargetMeth = GetMethodOnSelf(nameof(FindRealTarget));

        public static MethodBase FindRealTarget(MethodBase source, InstantiationPatch patchInfo)
            => ((GenericDetourCoreCLRWinX64) patchInfo.OwningPatchInfo.DetourRuntime).BuildInstantiationForMethod(patchInfo.OwningPatchInfo.TargetMethod, source);

        private static readonly MethodInfo RealTargetToMethodDescProxyMeth = GetMethodOnSelf(nameof(RealTargetToMethodDescProxy));

        public static IntPtr RealTargetToMethodDescProxy(MethodBase method) {
            IntPtr ptr = RealTargetToMethodDesc(method);
            CheckInstantiatedMethodKind(ptr);
            return ptr;
        }
    }
}
#endif