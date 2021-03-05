﻿using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.RuntimeDetour.Platforms.Generic {
#if !MONOMOD_INTERNAL
    public
#endif
    class GenericDetourCoreCLRWinX64 : GenericDetourCoreCLR {

        // for all of the thunks, the initial detour jump will be a call instruction
        // immediately following that instruction will be the pointer to the correct handling method
        // immediately following that pointer will be a 32 bit index to determine the hook target (index into list?)
        // immediately following that index will be a 16 bit length of the call instruction (to determine how to backpatch)

        // we use ;# for comments in assembly to be slightly more portable across assemblers

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

        #region Read from position 1
        // reads from first arg
        /**** this pointer context thunk assembly ****\
pop r10 ;# r10 isn't used to pass arguments on Windows; it now contains the return position

;# setup stack frame
push rbp
sub rsp, 40h ;# 4 qword (20h) + 3 qword arguments (18h) + some buffer
lea rbp, [rsp + 40h]

;# save register-passed arguments, specifically in the top of our allocated range
mov [rbp - 8h], rcx
mov [rbp - 10h], rdx
mov [rbp - 18h], r8
mov [rbp - 20h], r9

;# setup call
;# the methods being called here have no strangeness, only user args passed in register

;# first arg is the this ptr/generic context

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
mov rcx, [rbp - 8h]
mov rdx, [rbp - 10h]
mov r8, [rbp - 18h]
mov r9, [rbp - 20h]

;# clean up our stack frame
lea rsp, [rbp]
pop rbp

;# we're finally ready to call our target
jmp rax
        */
        private static readonly byte[] Pos1Thunk = {
            0x41, 0x5A, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48,
            0x8D, 0x6C, 0x24, 0x40, 0x48, 0x89, 0x4D, 0xF8,
            0x48, 0x89, 0x55, 0xF0, 0x4C, 0x89, 0x45, 0xE8,
            0x4C, 0x89, 0x4D, 0xE0, 0x4D, 0x89, 0xD0, 0x48,
            0x31, 0xD2, 0x66, 0x41, 0x8B, 0x52, 0x0C, 0x49,
            0x29, 0xD0, 0x48, 0x31, 0xD2, 0x41, 0x8B, 0x52,
            0x08, 0x41, 0xFF, 0x12, 0x48, 0x8B, 0x4D, 0xF8,
            0x48, 0x8B, 0x55, 0xF0, 0x4C, 0x8B, 0x45, 0xE8,
            0x4C, 0x8B, 0x4D, 0xE0, 0x48, 0x8D, 0x65, 0x00,
            0x5D, 0xFF, 0xE0,
        };
        #endregion

        #region Read from position 2
        // reads from second arg
        /**** instance no return buffer generic cookie/static return buffer generic cookie ****\
;#   the position of the generic cookie is the same when there is a this pointer and no return buffer as if there is 
;# no this pointer and a return buffer

pop r10 ;# get the return address for where we're calling from
     
;# setup stack frame
push rbp
sub rsp, 40h ;# 4 qword (20h) + 3 qword arguments (18h) + some buffer
lea rbp, [rsp + 40h]

;# save register-passed arguments, specifically in the top of our allocated range
mov [rbp - 8h], rcx
mov [rbp - 10h], rdx
mov [rbp - 18h], r8
mov [rbp - 20h], r9

;# setup call
;# the methods being called here have no strangeness, only user args passed in register

;# second arg is the generic cookie, which is in rdx
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
mov rcx, [rbp - 8h]
mov rdx, [rbp - 10h]
mov r8, [rbp - 18h]
mov r9, [rbp - 20h]

;# clean up our stack frame
lea rsp, [rbp]
pop rbp

;# we're finally ready to call our target
jmp rax
        */
        private static readonly byte[] Pos2Thunk = {
            0x41, 0x5A, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48,
            0x8D, 0x6C, 0x24, 0x40, 0x48, 0x89, 0x4D, 0xF8,
            0x48, 0x89, 0x55, 0xF0, 0x4C, 0x89, 0x45, 0xE8,
            0x4C, 0x89, 0x4D, 0xE0, 0x48, 0x89, 0xD1, 0x4D,
            0x89, 0xD0, 0x48, 0x31, 0xD2, 0x66, 0x41, 0x8B,
            0x52, 0x0C, 0x49, 0x29, 0xD0, 0x48, 0x31, 0xD2,
            0x41, 0x8B, 0x52, 0x08, 0x41, 0xFF, 0x12, 0x48,
            0x8B, 0x4D, 0xF8, 0x48, 0x8B, 0x55, 0xF0, 0x4C,
            0x8B, 0x45, 0xE8, 0x4C, 0x8B, 0x4D, 0xE0, 0x48,
            0x8D, 0x65, 0x00, 0x5D, 0xFF, 0xE0,
        };
        #endregion

        #region Read from position 3
        // reads from third arg
        /**** instance return buffer generic cookie ****\
pop r10 ;# get the return address for where we're calling from
        
;# setup stack frame
push rbp
sub rsp, 40h ;# 4 qword (20h) + 3 qword arguments (18h) + some buffer
lea rbp, [rsp + 40h]

;# save register-passed arguments, specifically in the top of our allocated range
mov [rbp - 8h], rcx
mov [rbp - 10h], rdx
mov [rbp - 18h], r8
mov [rbp - 20h], r9

;# setup call
;# the methods being called here have no strangeness, only user args passed in register

;# third arg is the generic cookie, which is in r8
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
mov rcx, [rbp - 8h]
mov rdx, [rbp - 10h]
mov r8, [rbp - 18h]
mov r9, [rbp - 20h]

;# clean up our stack frame
lea rsp, [rbp]
pop rbp

;# we're finally ready to call our target
jmp rax
        */
        private static readonly byte[] Pos3Thunk = {
            0x41, 0x5A, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48,
            0x8D, 0x6C, 0x24, 0x40, 0x48, 0x89, 0x4D, 0xF8,
            0x48, 0x89, 0x55, 0xF0, 0x4C, 0x89, 0x45, 0xE8,
            0x4C, 0x89, 0x4D, 0xE0, 0x4C, 0x89, 0xC1, 0x4D,
            0x89, 0xD0, 0x48, 0x31, 0xD2, 0x66, 0x41, 0x8B,
            0x52, 0x0C, 0x49, 0x29, 0xD0, 0x48, 0x31, 0xD2,
            0x41, 0x8B, 0x52, 0x08, 0x41, 0xFF, 0x12, 0x48,
            0x8B, 0x4D, 0xF8, 0x48, 0x8B, 0x55, 0xF0, 0x4C,
            0x8B, 0x45, 0xE8, 0x4C, 0x8B, 0x4D, 0xE0, 0x48,
            0x8D, 0x65, 0x00, 0x5D, 0xFF, 0xE0,
        };
        #endregion

        #region Thunk executable segment
        private struct ConstThunkMemory {
            public IntPtr MemStart;
            public IntPtr A1_thunk;
            public IntPtr A2_thunk;
            public IntPtr A3_thunk;
        }

        private static uint RoundLength(uint len) {
            return ((len / 64) + 1) * 64;
        }

        private static unsafe ConstThunkMemory BuildThunkMemory() {
            uint allocSize = RoundLength((uint) Pos1Thunk.Length) + RoundLength((uint) Pos2Thunk.Length) + RoundLength((uint) Pos3Thunk.Length);
            IntPtr alloc = DetourHelper.Native.MemAlloc(allocSize);
            DetourHelper.Native.MakeWritable(alloc, allocSize);

            byte* data = (byte*) alloc;
            for (uint i = 0; i < allocSize; i++)
                data[i] = 0xCC; // fill with 0xCC

            ConstThunkMemory mem = new ConstThunkMemory { MemStart = alloc };

            fixed (byte* tpThunk = Pos1Thunk) {
                mem.A1_thunk = (IntPtr) data;
                Copy(tpThunk, data, (uint) Pos1Thunk.Length);
                data += RoundLength((uint) Pos1Thunk.Length);
            }

            fixed (byte* tpThunk = Pos2Thunk) {
                mem.A2_thunk = (IntPtr) data;
                Copy(tpThunk, data, (uint) Pos2Thunk.Length);
                data += RoundLength((uint) Pos2Thunk.Length);
            }

            fixed (byte* tpThunk = Pos3Thunk) {
                mem.A3_thunk = (IntPtr) data;
                Copy(tpThunk, data, (uint) Pos3Thunk.Length);
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

        private IntPtr GetThunkForMethod(MethodBase instance) {
            // TODO: figure out how to determine if we have a return buffer
            if (TakesGenericsFromThis(instance)) {
                // if we take generics from the this parameter, grab the first given arg
                return thunkMemory.A1_thunk;
            }
            if (!instance.IsStatic ^ false /* has return buffer */) {
                // currently we assume that there there is never a return buffer, so always return the thunk assuming that
                // if it takes a this arg OR has a return buffer BUT NOT BOTH, grab the second given arg
                return thunkMemory.A2_thunk;
            } else if (!instance.IsStatic && false /* has return buffer */) {
                // if it takes a this arg AND has a return buffer, grab the third given arg
                return thunkMemory.A3_thunk;
            }
            // otherwise the context is in the first given arg
            return thunkMemory.A1_thunk;
        }

        private IntPtr GetHandlerForMethod(MethodBase instance) {
            if (TakesGenericsFromThis(instance))
                return FixupForThisPtrContext;
            if (RequiresMethodTableArg(instance))
                return FixupForMethodTableContext;
            if (RequiresMethodDescArg(instance))
                return FixupForMethodDescContext;
            return UnknownMethodABI;
        }

        protected override NativeDetourData PatchInstantiation(MethodBase orig, MethodBase methodInstance, IntPtr codeStart, int index) {
            IntPtr thunk = GetThunkForMethod(methodInstance);
            IntPtr handler = GetHandlerForMethod(methodInstance);
            CallType callType = FindCallType(codeStart, thunk);
            uint callLen = GetCallTypeSize(callType);

            // backup the original data
            IntPtr dataBackup = DetourHelper.Native.MemAlloc(callLen);
            Copy(codeStart, dataBackup, callLen);

            NativeDetourData detourData = new NativeDetourData {
                Method = codeStart,
                Target = thunk,
                Type = (byte)callType,
                Size = callLen,
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

        protected override IntPtr GetRealTarget(GenericPatchInfo patchInfo, MethodBase realSrc, MethodBase origTarget, out object backpatchInfo) {
            throw new NotImplementedException();
        }
    }
}
