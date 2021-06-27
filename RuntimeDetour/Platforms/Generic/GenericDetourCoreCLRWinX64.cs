#if !NET35
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
                    if (info.Position is not GenericContextPosition.Arg2 and not GenericContextPosition.Arg3)
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
                case // MDT->MD no return buffer
                {
                    Src: { Kind: GenericContextKind.ThisMethodDesc, Position: GenericContextPosition.Arg3 },
                    Dst: { Kind: GenericContextKind.MethodDesc, Position: GenericContextPosition.Arg2 }
                }: {
                        // this is the following transformation:
                        //   t r g x y
                        //   r g t x y

                        CCallSite callSite = new(method.ReturnType);
                        // load return buffer
                        ParameterDefinition rbParam = method.Parameters.Skip(1).First();
                        callSite.Parameters.Add(new(rbParam.ParameterType));
                        il.Emit(OpCodes.Ldarg, rbParam);
                        // then load gctx
                        callSite.Parameters.Add(new(realGCtx.VariableType));
                        il.Emit(OpCodes.Ldloc, realGCtx);
                        // then load the this ptr
                        callSite.Parameters.Add(new(method.Parameters.First().ParameterType));
                        il.Emit(OpCodes.Ldarg, method.Parameters.First());
                        // then load everything else
                        foreach (ParameterDefinition param in method.Parameters.Skip(3)) {
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
                body.Write(ref offs, (ushort) 0xd389);
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

        private static readonly MethodInfo FindRealTargetMeth = GetMethodOnSelf(nameof(FindRealTarget));

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