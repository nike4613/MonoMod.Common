using MonoMod.RuntimeDetour;
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

        #region Precall Thunks

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
        private static readonly byte[] Pre1Thunk = {
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
        private static readonly byte[] Pre2Thunk = {
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
        private static readonly byte[] Pre3Thunk = {
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

        #endregion

        #region Call Thunks
        /*
; calling any M thunks means that you must have, immediately following the call instruction,
; * a pointer to the actual target code
; * the real generic context for the call (64-bit)
; * a byte containing additional flags.
; * a byte containing the amount to adjust the stack pointer after pushing the fourth arg as
;   a 64-bit integer (computed as 8 - sizeof(arg4))

; the flags byte has the following bits:
; * 0: set if the fourth argument is floating point
; * 1: set if the fourth argument is floating point and is a double
        */

        #region Call Move 2+
        /*
; our goal is to move all arguments including and after 2 down and insert our generic context
; this is used when the source uses the this pointer for context and has no return buffer

pop r10 ; load the return address to use for more info

pop r11 ; load the return-return address so we can manipulate the stack a bit

test byte [r10 + 10h], 1b ; check if the flag for floating point is set
jnz flop

; conveniently, our calling convention pushes arguments in reverse order
; do regular push
push r9 ; push the last register argument to the stack
add rsp, [r10 + 11h] ; do final adjustment

jmp movRegs
flop:
; idfk what to do here????

test byte [r10 + 10h], 10b ; check if double flag is set
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

push r11 ; make sure to push the return address back onto the stack

; integer args
mov r9, r8
mov r8, rdx

; floating point args
movsd xmm3, xmm2
movsd xmm2, xmm1
; do I need movss or movsd???

mov rdx, [r10 + 8h] ; the location of the new generic context into position 2

; now we're set up to just call the actual target
jmp [r10] ; call the actual target
         */
        private static readonly byte[] CallM2Thunk = {
            0x41, 0x5A, 0x41, 0x5B, 0x41, 0xF6, 0x42, 0x10,
            0x01, 0x75, 0x08, 0x41, 0x51, 0x49, 0x03, 0x62,
            0x11, 0xEB, 0x1B, 0x41, 0xF6, 0x42, 0x10, 0x02,
            0x75, 0x0B, 0x48, 0x83, 0xEC, 0x04, 0xF3, 0x0F,
            0x11, 0x1C, 0x24, 0xEB, 0x09, 0x48, 0x83, 0xEC,
            0x08, 0xF2, 0x0F, 0x11, 0x1C, 0x24, 0x41, 0x53,
            0x4D, 0x89, 0xC1, 0x49, 0x89, 0xD0, 0xF2, 0x0F,
            0x10, 0xDA, 0xF2, 0x0F, 0x10, 0xD1, 0x49, 0x8B,
            0x52, 0x08, 0x41, 0xFF, 0x22,
        };
        #endregion

        #region Call Move 3+
        /*
; our goal is to move all arguments including and after 3 down and insert our generic context
; this is used when the source uses the this pointer for context and has no return buffer

pop r10 ; load the return address to use for more info

pop r11 ; load the return-return address so we can manipulate the stack a bit

test byte [r10 + 10h], 1b ; check if the flag for floating point is set
jnz flop

; conveniently, our calling convention pushes arguments in reverse order
; do regular push
push r9 ; push the last register argument to the stack
add rsp, [r10 + 11h] ; do final adjustment

jmp movRegs
flop:
; idfk what to do here????

test byte [r10 + 10h], 10b ; check if double flag is set
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

push r11 ; make sure to push the return address back onto the stack

; integer args
mov r9, r8

; floating point args
movsd xmm3, xmm2
; do I need movss or movsd???

mov r8, [r10 + 8h] ; the location of the new generic context into position 2

; now we're set up to just call the actual target
jmp [r10] ; call the actual target
         */
        private static readonly byte[] CallM3Thunk = {
            0x41, 0x5A, 0x41, 0x5B, 0x41, 0xF6, 0x42, 0x10,
            0x01, 0x75, 0x08, 0x41, 0x51, 0x49, 0x03, 0x62,
            0x11, 0xEB, 0x1B, 0x41, 0xF6, 0x42, 0x10, 0x02,
            0x75, 0x0B, 0x48, 0x83, 0xEC, 0x04, 0xF3, 0x0F,
            0x11, 0x1C, 0x24, 0xEB, 0x09, 0x48, 0x83, 0xEC,
            0x08, 0xF2, 0x0F, 0x11, 0x1C, 0x24, 0x41, 0x53,
            0x4D, 0x89, 0xC1, 0xF2, 0x0F, 0x10, 0xDA, 0x4D,
            0x8B, 0x42, 0x08, 0x41, 0xFF, 0x22,
        };
        #endregion

        /*
; calling any R thunks means that you must have, immediately following the call instruction,
; * a pointer to the actual target code
; * the real generic context for the call (64-bit)
        */
        #region Call Replace 1
        /*
; our goal is to replace the generic context in position 1 with our own.

; start by figuring out what our new generic context should be
pop r10 ; load the return address to use for more info

; position 1 is held in rcx; we assume we have a generic context already here
; so we just overwrite it
mov rcx, [r10 + 8h] ; the location of the new generic context

; now we're set up to just call the actual target
jmp [r10] ; call the actual target
         */
        private static readonly byte[] CallR1Thunk = {
            0x41, 0x5A, 0x49, 0x8B, 0x4A, 0x08, 0x41, 0xFF,
            0x22,
        };
        #endregion

        #region Call Replace 2
        /*
; our goal is to replace the generic context in position 2 with our own.

; start by figuring out what our new generic context should be
pop r10 ; load the return address to use for more info

; position 2 is held in rdx; we assume we have a generic context already here
; so we just overwrite it
mov rdx, [r10 + 8h] ; the location of the new generic context

; now we're set up to just call the actual target
jmp [r10] ; call the actual target
         */
        private static readonly byte[] CallR2Thunk = {
            0x41, 0x5A, 0x49, 0x8B, 0x52, 0x08, 0x41, 0xFF,
            0x22,
        };
        #endregion

        #region Call Replace 3
        /*
; our goal is to replace the generic context in position 3 with our own.

; start by figuring out what our new generic context should be
pop r10 ; load the return address to use for more info

; position 3 is held in rdx; we assume we have a generic context already here
; so we just overwrite it
mov r8, [r10 + 8h] ; the location of the new generic context

; now we're set up to just call the actual target
jmp [r10] ; call the actual target
         */
        private static readonly byte[] CallR3Thunk = {
            0x41, 0x5A, 0x4D, 0x8B, 0x42, 0x08, 0x41, 0xFF,
            0x22,
        };
        #endregion

        #endregion

        #region Thunk executable segment
        private struct ConstThunkMemory {
            public IntPtr MemStart;
            public IntPtr Pre1;
            public IntPtr Pre2;
            public IntPtr Pre3;

        }

        private static uint RoundLength(uint len, uint amt = 32) {
            return ((len / amt) + 1) * amt;
        }

        private static unsafe ConstThunkMemory BuildThunkMemory() {
            uint allocSize = 
                RoundLength((uint) Pre1Thunk.Length) + 
                RoundLength((uint) Pre2Thunk.Length) + 
                RoundLength((uint) Pre3Thunk.Length) +
                RoundLength((uint) CallR1Thunk.Length, 8) +
                RoundLength((uint) CallR2Thunk.Length, 8) +
                RoundLength((uint) CallR3Thunk.Length, 8) +
                RoundLength((uint) CallM2Thunk.Length, 16) +
                RoundLength((uint) CallM3Thunk.Length, 16);
            IntPtr alloc = DetourHelper.Native.MemAlloc(allocSize);
            DetourHelper.Native.MakeWritable(alloc, allocSize);

            byte* data = (byte*) alloc;
            for (uint i = 0; i < allocSize / 4; i++)
                ((uint*)data)[i] = 0xCCCCCCCCu; // fill with 0xCC

            ConstThunkMemory mem = new ConstThunkMemory { MemStart = alloc };

            fixed (byte* tpThunk = Pre1Thunk) {
                mem.Pre1 = (IntPtr) data;
                Copy(tpThunk, data, (uint) Pre1Thunk.Length);
                data += RoundLength((uint) Pre1Thunk.Length);
            }

            fixed (byte* tpThunk = Pre2Thunk) {
                mem.Pre2 = (IntPtr) data;
                Copy(tpThunk, data, (uint) Pre2Thunk.Length);
                data += RoundLength((uint) Pre2Thunk.Length);
            }

            fixed (byte* tpThunk = Pre3Thunk) {
                mem.Pre3 = (IntPtr) data;
                Copy(tpThunk, data, (uint) Pre3Thunk.Length);
                data += RoundLength((uint) Pre3Thunk.Length);
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
                return thunkMemory.Pre1;
            }
            if (!instance.IsStatic ^ false /* has return buffer */) {
                // currently we assume that there there is never a return buffer, so always return the thunk assuming that
                // if it takes a this arg OR has a return buffer BUT NOT BOTH, grab the second given arg
                return thunkMemory.Pre2;
            } else if (!instance.IsStatic && false /* has return buffer */) {
                // if it takes a this arg AND has a return buffer, grab the third given arg
                return thunkMemory.Pre3;
            }
            // otherwise the context is in the first given arg
            return thunkMemory.Pre1;
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

        protected override void BackpatchJump(IntPtr source, IntPtr target, object backpatchInfo) {
            base.BackpatchJump(source, target, backpatchInfo);
        }
    }
}
