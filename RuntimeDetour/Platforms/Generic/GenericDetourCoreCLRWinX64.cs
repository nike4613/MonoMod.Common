﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Common.RuntimeDetour.Platforms.Generic {
#if !MONOMOD_INTERNAL
    public
#endif
    class GenericDetourCoreCLRWinX64 : GenericDetourCoreCLR {

        // for all of the thunks, the initial detour jump will be a call instruction
        // immediately following that instruction will be the pointer to the correct handling method
        // immediately following that pointer will be a 32 bit index to determine the hook target (index into list?)
        // immediately following that index will be a 16 bit length of the call instruction (to determine how to backpatch)

        // we use ;# for comments in assembly to be slightly more portable across assemblers
        /**** this pointer context thunk assembly ****\
        pop r10 ;# r10 isn't used to pass arguments on Windows; it now contains the return position

        ;# save register-passed arguments
        push rcx
        push rdx
        push r8
        push r9

        ;# setup call
        ;# the methods being called here have no strangeness, only user args passed in register

        ;# first arg is the this ptr

        ;# the third arg is the source start
        mov r8, r10 ;# r8 is the argument we want it in
        xor rdx, rdx
        mov dx, [r10 + 12] ;# offset of the call insn length
        sub r8, rdx

        ;# the second arg is the index
        xor rdx, rdx
        mov edx, [r10 + 8] ;# offset of the index

        ;# finally call handler
        call [r10]

        ;# rax now contains our target method, but we need to re-load our arguments
        pop r9
        pop r8
        pop rdx
        pop rcx

        ;# we're finally ready to call our target
        jmp rax
        */
        private static readonly byte[] ThisPtrThunk = { 
            0x41, 0x5A,                     // pop r10
            0x51,                           // push rcx
            0x52,                           // push rdx
            0x41, 0x50,                     // push r8
            0x41, 0x51,                     // push r9
            0x4D, 0x89, 0xD0,               // mov r8, r10
            0x48, 0x31, 0xD2,               // xor rdx, rdx
            0x66, 0x41, 0x8B, 0x52, 0x0C,   // mov dx, [r10 + 12]
            0x49, 0x29, 0xD0,               // sub r8, rdx
            0x48, 0x31, 0xD2,               // xor rdx, rdx
            0x41, 0x8B, 0x52, 0x08,         // mov edx, [r10 + 8]
            0x41, 0xFF, 0x12,               // call [r10]
            0x41, 0x59,                     // pop r9
            0x41, 0x58,                     // pop r8
            0x5A,                           // pop rdx
            0x59,                           // pop rcx
            0xFF, 0xE0                      // jmp rax
        };
    }
}
