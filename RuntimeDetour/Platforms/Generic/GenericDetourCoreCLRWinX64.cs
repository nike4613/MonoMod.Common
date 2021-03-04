using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        #region ThisPtr context thunk
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
        #endregion

        #region InstNoBuf context/StaticBuf context
        /**** instance no return buffer generic cookie/static return buffer generic cookie ****\
;#   the position of the generic cookie is the same when there is a this pointer and no return buffer as if there is 
;# no this pointer and a return buffer

pop r10 ;# get the return address for where we're calling from
        
;# save register-passed arguments
push rcx
push rdx
push r8
push r9

;# setup call
;# the methods being called here have no strangeness, only user args passed in register

;# first arg is the generic cookie, which is in rdx
mov rcx, rdx

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
        private static readonly byte[] ThisPtrNoBufThunk = {
            0x41, 0x5A,                     // pop r10
            0x51,                           // push rcx
            0x52,                           // push rdx
            0x41, 0x50,                     // push r8
            0x41, 0x51,                     // push r9
            0x48, 0x89, 0xD1,               // mov rcx, rdx
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
        // ^^^ the above is also used when there is no this pointer but there is a return buffer
        #endregion

        #region InstBuf context
        /**** instance return buffer generic cookiee ****\
pop r10 ;# get the return address for where we're calling from
        
;# save register-passed arguments
push rcx
push rdx
push r8
push r9

;# setup call
;# the methods being called here have no strangeness, only user args passed in register

;# first arg is the generic cookie, which is in rdx
mov rcx, r8

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
        private static readonly byte[] ThisPtrBufThunk = {
            0x41, 0x5A,                     // pop r10
            0x51,                           // push rcx
            0x52,                           // push rdx
            0x41, 0x50,                     // push r8
            0x41, 0x51,                     // push r9
            0x4C, 0x89, 0xC1,               // mov rcx, r8
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
        #endregion

        #region Thunk executable segment
        private struct ConstThunkMemory {
            public IntPtr MemStart;
            public IntPtr TP_thunk;
            public IntPtr INB_SB_thunk;
            public IntPtr IB_thunk;
        }

        private static uint RoundLength(uint len) {
            return ((len / 64) + 1) * 64;
        }

        private static unsafe ConstThunkMemory BuildThunkMemory() {
            uint allocSize = RoundLength((uint)ThisPtrBufThunk.Length) + RoundLength((uint) ThisPtrThunk.Length) + RoundLength((uint) ThisPtrNoBufThunk.Length);
            IntPtr alloc = DetourHelper.Native.MemAlloc(allocSize);
            DetourHelper.Native.MakeWritable(alloc, allocSize);

            byte* data = (byte*) alloc;
            for (uint i = 0; i < allocSize; i++)
                data[i] = 0xCC; // fill with 0xCC

            ConstThunkMemory mem = new ConstThunkMemory { MemStart = alloc };

            fixed (byte* tpThunk = ThisPtrThunk) {
                mem.TP_thunk = (IntPtr) data;
                Copy(tpThunk, data, (uint) ThisPtrThunk.Length);
                data += RoundLength((uint) ThisPtrThunk.Length);
            }

            fixed (byte* tpThunk = ThisPtrNoBufThunk) {
                mem.INB_SB_thunk = (IntPtr) data;
                Copy(tpThunk, data, (uint) ThisPtrNoBufThunk.Length);
                data += RoundLength((uint) ThisPtrNoBufThunk.Length);
            }

            fixed (byte* tpThunk = ThisPtrBufThunk) {
                mem.IB_thunk = (IntPtr) data;
                Copy(tpThunk, data, (uint) ThisPtrBufThunk.Length);
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

        private enum CallType {
            Rel32,
            Abs64
        }

        private static bool Is32Bit(long to)
            // JMP rel32 is "sign extended to 64-bits"
            => (((ulong) to) & 0x000000007FFFFFFFUL) == ((ulong) to);

        private static CallType FindCallType(IntPtr from, IntPtr to) {
            return Is32Bit((long) to - (long) from)
                ? CallType.Rel32
                : CallType.Abs64;
        }

        protected override NativeDetourData PatchInstantiation(MethodBase orig, MethodBase methodInstance, IntPtr codeStart) {
            throw new NotImplementedException();
        }

        protected override void UnpatchInstantiation(InstantiationPatch instantiation) {
            throw new NotImplementedException();
        }
    }
}
