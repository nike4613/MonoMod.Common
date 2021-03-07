#if !NET35
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.RuntimeDetour.Platforms {
#if !MONOMOD_INTERNAL
    public
#endif
    class GenericDetourCoreCLRWinX64 : GenericDetourCoreCLR {

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
mov rcx, r8

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
            0x4C, 0x89, 0xC1, 0x41, 0x8B, 0x52, 0x10, 0xE8,
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
            0x5F, 0x41, 0xF6, 0x42, 0x14, 0x01, 0x75, 0x0C,
            0x41, 0x51, 0x66, 0x41, 0x8B, 0x72, 0x15, 0x48,
            0x01, 0xF4, 0xEB, 0x1B, 0x41, 0xF6, 0x42, 0x14,
            0x02, 0x75, 0x0B, 0x48, 0x83, 0xEC, 0x04, 0xF3,
            0x0F, 0x11, 0x1C, 0x24, 0xEB, 0x09, 0x48, 0x83,
            0xEC, 0x08, 0xF2, 0x0F, 0x11, 0x1C, 0x24, 0x57,
            0x4D, 0x89, 0xC1, 0x49, 0x89, 0xD0, 0x48, 0x89,
            0xCA, 0xF2, 0x0F, 0x10, 0xDA, 0xF2, 0x0F, 0x10,
            0xD1, 0xF2, 0x0F, 0x10, 0xC8, 0x48, 0x89, 0xC1,
            0x41, 0xFF, 0xE3,
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
            0x5F, 0x41, 0xF6, 0x42, 0x14, 0x01, 0x75, 0x0C,
            0x41, 0x51, 0x66, 0x41, 0x8B, 0x72, 0x15, 0x48,
            0x01, 0xF4, 0xEB, 0x1B, 0x41, 0xF6, 0x42, 0x14,
            0x02, 0x75, 0x0B, 0x48, 0x83, 0xEC, 0x04, 0xF3,
            0x0F, 0x11, 0x1C, 0x24, 0xEB, 0x09, 0x48, 0x83,
            0xEC, 0x08, 0xF2, 0x0F, 0x11, 0x1C, 0x24, 0x57,
            0x4D, 0x89, 0xC1, 0xF2, 0x0F, 0x10, 0xDA, 0x49,
            0x89, 0xC8, 0x48, 0x89, 0xD1, 0x48, 0x89, 0xC2,
            0x41, 0xFF, 0xE3,
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
;xor rdx, rdx
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
            0x4C, 0x89, 0x4D, 0xE0, 0x4D, 0x89, 0xD0, 0x66,
            0x41, 0x8B, 0x52, 0x0C, 0x49, 0x29, 0xD0, 0x41,
            0x8B, 0x52, 0x08, 0x41, 0xFF, 0x12, 0x48, 0x8B,
            0x4D, 0xF8, 0x48, 0x8B, 0x55, 0xF0, 0x4C, 0x8B,
            0x45, 0xE8, 0x4C, 0x8B, 0x4D, 0xE0, 0x48, 0x8D,
            0x65, 0x00, 0x5D, 0xFF, 0xE0,
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
;xor rdx, rdx
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
            0x89, 0xD0, 0x66, 0x41, 0x8B, 0x52, 0x0C, 0x49,
            0x29, 0xD0, 0x41, 0x8B, 0x52, 0x08, 0x41, 0xFF,
            0x12, 0x48, 0x8B, 0x4D, 0xF8, 0x48, 0x8B, 0x55,
            0xF0, 0x4C, 0x8B, 0x45, 0xE8, 0x4C, 0x8B, 0x4D,
            0xE0, 0x48, 0x8D, 0x65, 0x00, 0x5D, 0xFF, 0xE0,
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

; third arg is the generic context, in r8
mov rcx, r8

; the third arg is the source start
mov r8, r10 ; r8 is the argument we want it in
;xor rdx, rdx
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
            0x4C, 0x89, 0x4D, 0xE0, 0x4C, 0x89, 0xC1, 0x4D,
            0x89, 0xD0, 0x66, 0x41, 0x8B, 0x52, 0x0C, 0x49,
            0x29, 0xD0, 0x41, 0x8B, 0x52, 0x08, 0x41, 0xFF,
            0x12, 0x48, 0x8B, 0x4D, 0xF8, 0x48, 0x8B, 0x55,
            0xF0, 0x4C, 0x8B, 0x45, 0xE8, 0x4C, 0x8B, 0x4D,
            0xE0, 0x48, 0x8D, 0x65, 0x00, 0x5D, 0xFF, 0xE0,
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
        };
        #endregion

        #endregion

        #region Thunk executable segment
        private struct ConstThunkMemory {
            public IntPtr MemStart;

            public IntPtr Pre1;
            public IntPtr Pre2;
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

            public IntPtr C3_MD_MT;
            public IntPtr C3_MD_MD;
        }

        private static uint RoundLength(uint len, uint amt = 16) {
            return ((len / amt) + 1) * amt;
        }

        private static unsafe ConstThunkMemory BuildThunkMemory() {
            uint allocSize = 
                RoundLength((uint) precall_1.Length) + 
                RoundLength((uint) precall_2.Length) + 
                RoundLength((uint) precall_3.Length) +
                RoundLength((uint) cconv_g_g.Length) +
                RoundLength((uint) cconv_rg_rg.Length) +
                RoundLength((uint) cconv_tg_g.Length) +
                RoundLength((uint) cconv_trg_rg.Length) +
                RoundLength((uint) cconv_tr_rg.Length) +
                RoundLength((uint) cconv_t_g.Length) + 
                (RoundLength((uint) call_p1_form.Length) * 6) + // there are 6 variants of the C1 thunk
                (RoundLength((uint) call_p2_form.Length) * 4) + //           4 variants of the C2 thunk
                (RoundLength((uint) call_p3_form.Length) * 2) + //       and 2 variants of the C3 thunk
                RoundLength(6u * 6) + // there are 6 fixup targets, which requires 6 6-byte absolute jumps
                (8u * 6);             //       and 6 absolute jump targets

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
            // all variants of C3
            data = CopyToData(out mem.C3_MD_MT, data, call_p3_form);
            data = CopyToData(out mem.C3_MD_MD, data, call_p3_form);

            // calculate offsets to jump block offsets
            int rel_t_mt = 2;
            int rel_t_md = rel_t_mt + 6;
            int rel_mt_mt = rel_t_md + 6;
            int rel_mt_md = rel_mt_mt + 6;
            int rel_md_mt = rel_mt_md + 6;
            int rel_md_md = rel_md_mt + 6;
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

                WriteOffset(mem.C3_MD_MT, call_p3_from_fix_fn_offs, jumpBlock, rel_md_mt - 2);
                WriteOffset(mem.C3_MD_MD, call_p3_from_fix_fn_offs, jumpBlock, rel_md_md - 2);
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
                + 8u // handler method
                + 4u // index
                + 2u // call insn size
                // or
                : 6u // call insn
                + 8u // handler method
                + 4u // index
                + 2u // call insn size
                + 8u; // call abs address

        private enum GenericContextPosision {
            Arg1, Arg2, Arg3
        }

        private static GenericContextPosision GetGenericContextPosition(MethodBase instance) {
            if (TakesGenericsFromThis(instance)) {
                // if we take generics from the this parameter, grab the first given arg
                return GenericContextPosision.Arg1;
            }
            bool needsReturnBuffer = MethodRequiresReturnBuffer(instance);
            if (!instance.IsStatic ^ needsReturnBuffer) {
                // currently we assume that there there is never a return buffer, so always return the thunk assuming that
                // if it takes a this arg OR has a return buffer BUT NOT BOTH, grab the second given arg
                return GenericContextPosision.Arg2;
            } else if (!instance.IsStatic && needsReturnBuffer) {
                // if it takes a this arg AND has a return buffer, grab the third given arg
                return GenericContextPosision.Arg3;
            }
            // otherwise the context is in the first given arg
            return GenericContextPosision.Arg1;
        }

        private IntPtr GetPrecallThunkForMethod(MethodBase instance)
            => GetGenericContextPosition(instance) switch {
                GenericContextPosision.Arg1 => thunkMemory.Pre1,
                GenericContextPosision.Arg2 => thunkMemory.Pre2,
                GenericContextPosision.Arg3 => thunkMemory.Pre3,
                _ => throw new InvalidOperationException()
            };

        private IntPtr GetPrecallHandlerForMethod(MethodBase instance) {
            if (TakesGenericsFromThis(instance))
                return FixupForThisPtrContext;
            if (RequiresMethodTableArg(instance))
                return FixupForMethodTableContext;
            if (RequiresMethodDescArg(instance))
                return FixupForMethodDescContext;
            return UnknownMethodABI;
        }

        protected override NativeDetourData PatchInstantiation(MethodBase orig, MethodBase methodInstance, IntPtr codeStart, int index) {
            // TODO: correctly handle the case where this is a unique instantiation
            
            IntPtr thunk = GetPrecallThunkForMethod(methodInstance);
            IntPtr handler = GetPrecallHandlerForMethod(methodInstance);
            CallType callType = FindCallType(codeStart, thunk);
            uint callLen = GetCallTypeSize(callType);

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
                codeStart.Write(ref idx, (uint) ((ulong) codeStart - (ulong) thunk));
            } else {
                codeStart.Write(ref idx, (byte) 0xFF);
                codeStart.Write(ref idx, (byte) 0x15);
                codeStart.Write(ref idx, (uint) (8 + 4 + 2)); // offset from end of instruction to end of other data for real address
            }
            codeStart.Write(ref idx, (ulong) handler);
            codeStart.Write(ref idx, (uint) index);
            if (callType == CallType.Rel32) {
                codeStart.Write(ref idx, (ushort) 5); // the rel32 call is 5 bytes
            } else {
                codeStart.Write(ref idx, (ushort) 6); // the abs64 call is 6 bytes
                codeStart.Write(ref idx, (ulong) thunk); // and needs the thunk's absolute address here
            }

            DetourHelper.Native.MakeExecutable(detourData);
            DetourHelper.Native.FlushICache(detourData);

            return detourData;
        }

        protected override void UnpatchInstantiation(InstantiationPatch instantiation) {
            NativeDetourData data = instantiation.OriginalData;

            // restore original code
            DetourHelper.Native.MakeWritable(data);
            Copy(data.Extra, data.Method, data.Size);
            DetourHelper.Native.MakeExecutable(data);
            DetourHelper.Native.FlushICache(data);

            // be sure to free the associated memory
            DetourHelper.Native.MemFree(data.Extra);
        }

        private const int CallsiteMaxPatchSize =
            6 + // call instruction
            8 + // context conversion function
            8 + // real method target
            4 + // index
            3 + // additional data used by some call handlers
            8;  // the real call target, if its an absolute call

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
                    return GetGenericContextPosition(srcMethod) switch {
                        GenericContextPosision.Arg1 => thunkMemory.C1_MD_MD,
                        GenericContextPosision.Arg2 => thunkMemory.C2_MD_MD,
                        GenericContextPosision.Arg3 => thunkMemory.C3_MD_MD,
                        _ => throw new InvalidOperationException("Unknown generic ABI for source or target"),
                    };
                } else if (RequiresMethodTableArg(target)) {
                    return GetGenericContextPosition(srcMethod) switch {
                        GenericContextPosision.Arg1 => thunkMemory.C1_MD_MT,
                        GenericContextPosision.Arg2 => thunkMemory.C2_MD_MT,
                        GenericContextPosision.Arg3 => thunkMemory.C3_MD_MT,
                        _ => throw new InvalidOperationException("Unknown generic ABI for source or target"),
                    };
                } else {
                    throw new InvalidOperationException("Unknown generic ABI for target");
                }
            } else if (RequiresMethodTableArg(srcMethod)) {
                if (RequiresMethodDescArg(target)) {
                    return GetGenericContextPosition(srcMethod) switch {
                        GenericContextPosision.Arg1 => thunkMemory.C1_MT_MD,
                        GenericContextPosision.Arg2 => thunkMemory.C2_MT_MD,
                        GenericContextPosision.Arg3 => throw new InvalidOperationException("Impossible generic ABI for source; third position method table"),
                        _ => throw new InvalidOperationException("Unknown generic ABI for source or target"),
                    };
                } else if (RequiresMethodTableArg(target)) {
                    return GetGenericContextPosition(srcMethod) switch {
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

        protected override void PatchMethodInst(GenericPatchInfo patch, MethodBase realSrc, IntPtr codeStart) {
            MethodBase realTarget = BuildInstantiationForMethod(patch.TargetMethod, realSrc);

            IntPtr thunk = GetCallThunkForMethod(realSrc, realTarget);
            IntPtr callConvConvert = GetCallConvConverterThunkForMethod(realSrc, realTarget);

            CallType callType = FindCallType(codeStart, thunk);

            ParameterInfo[] parameters = realSrc.GetParameters();
            byte flags = 0;
            ushort pushAdjust = 0;
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
            codeStart.Write(ref idx, (ulong) realTarget.GetNativeStart());
            codeStart.Write(ref idx, (uint) patch.Index);
            codeStart.Write(ref idx, flags);
            codeStart.Write(ref idx, pushAdjust);
            if (callType == CallType.Abs64) {
                codeStart.Write(ref idx, (ulong) thunk); // and needs the thunk's absolute address here
            }

            DetourHelper.Native.MakeExecutable(detourData);
            DetourHelper.Native.FlushICache(detourData);
        }
    }
}
#endif