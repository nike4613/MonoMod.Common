#if !NET35
using Mono.Cecil;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil.Cil;
using System.Runtime.InteropServices;
using GenericParameterAttributes = System.Reflection.GenericParameterAttributes;
using CTypeAttributes = Mono.Cecil.TypeAttributes;
using CMethodAttributes = Mono.Cecil.MethodAttributes;
using CMethodImplAttributes = Mono.Cecil.MethodImplAttributes;
using CCallSite = Mono.Cecil.CallSite;

namespace MonoMod.RuntimeDetour.Platforms {

    // There are 4 cases we need to handle:
    // - Unshared generics. This is trivial.
    // - Shared, using MethodDesc as a generic cookie
    // - Shared, using MethodTable as a generic cookie
    // - Shared, using this as the generic cookie

    //   In the first case, we don't need to do anything fancy, and can instead, at hooked method JIT time, 
    // generate the appropriate detour target instantiation, just like a normal detour.

    //   In the second case, the method itself has generic parameters and is static, and so we need to do the generic
    // parameter thunk strategy.

    //   In the third case, the method itself does not have generic parameters, but its containing type does,
    // and it is static, so we again need to do the thunk strategy.

    //   In the fourth case, the method is an instance method and does not have generic parameters itself,
    // and uses the target object as a MethodTable to determine which types it has, and so we need to do the
    // generic thunk strategy.

    //   The generic thunk strategy involves having a thunk that we detour a method to instead of its target,
    // because we don't know what target implementation to use at detour time. This thunk will do several things:
    //   1. Determine the value that is used for the generic context
    //   2. Call a C# (static) method that can handle that specific kind of context, which determines an actual
    //      target implementation and returns a pointer to its body, as well as fixing up the call to the thunk
    //      to point to the actual body
    //   3. Jump to the actual body
    // Because this relies on fragments of assembly, each architecture requires its own implementation of this
    // which does the same basic things for that architecture. For some architectures, the calling conventions
    // also vary based on operating system, and so there must be variants for that as well.
    //   We need to have the thunks in place to handle generic constraints. For methods which are shared, the
    // shared arguments are filled with System.__Canon, and that will meet no constraints other than a bare class
    // constraint. The runtime, of course, doesn't let us construct generic types unless the provided types meet
    // the constraints, and the JIT isn't given anything other that System.__Canon for types that will be fillled
    // like that, so we have to wait until a given instantiation is actually called to figure out where it needs
    // to detour to. This is what the thunks do.
    //   Importantly, there will need to be a total of 6 thunk bodies: one which is capable obtaining each context
    // object, for each possible position of it; there can optionally be a return buffer pointer between the this
    // argument and the generic context. This can be reduced to just 3 however; the this object (with the exception
    // of Framework <4.5) is always the first argument, eliminating the need for one of the bodies, and the generic
    // cookie pointer for both the MethodDesc and MethodTable cases are in the same place, removing another two.

#if !MONOMOD_INTERNAL
    public
#endif
    abstract partial class GenericDetourCoreCLR : IGenericDetourPlatform {

        // src/coreclr/src/vm/generics.cpp
        // ^^^ the above contains the logic that determines sharing
        // src/coreclr/src/vm/method.cpp line 1685
        // ^^^ is the start of the logic for determining what kind of generic cookie to use

        #region Method property tests
        protected static bool RequiresMethodTableArg(MethodBase method) {
            /*
            return
                IsSharedByGenericInstantiations() &&
                !HasMethodInstantiation() &&
                (IsStatic() || GetMethodTable()->IsValueType() || (GetMethodTable()->IsInterface() && !IsAbstract()));
            */

            return
                !method.IsGenericMethod && // this can be the case when only the containing types are generic
                MethodIsGenericShared(method) &&
                (method.IsStatic || method.DeclaringType.IsValueType || (method.DeclaringType.IsInterface && !method.IsAbstract));
        }

        protected static bool RequiresMethodDescArg(MethodBase method) {
            /*
            return IsSharedByGenericInstantiations() &&
                HasMethodInstantiation();
            */

            return
                method.IsGenericMethod &&
                MethodIsGenericShared(method);
        }

        protected static bool TakesGenericsFromThis(MethodBase method) {
            /* 
            return
                IsSharedByGenericInstantiations()  &&
                !HasMethodInstantiation() &&
                !IsStatic() &&
                !GetMethodTable()->IsValueType() &&
                !(GetMethodTable()->IsInterface() && !IsAbstract());
            */

            return
                !method.IsGenericMethod &&
                MethodIsGenericShared(method) &&
                !method.IsStatic &&
                !method.DeclaringType.IsValueType &&
                !(method.DeclaringType.IsInterface && !method.IsAbstract);
        }

        protected static readonly Type CanonClass = Type.GetType("System.__Canon");

        protected static bool TypeIsGenericShared(Type type) {
            if (type.ContainsGenericParameters) {
                throw new InvalidOperationException("Cannot determine generic-sharedness without specific instantiation");
            }

            if (type.IsPrimitive) {
                return false;
            } else if (type.IsValueType) {
                if (type.IsGenericType && type.GetGenericArguments().Any(TypeIsGenericShared)) {
                    return true; // this method asks if this instantiation is shared *at all*, so any nested share types cause it to be
                }
                return false;
            }

            return true; // reference types are always shared
        }

        protected static bool MethodIsGenericShared(MethodBase method) {
            if (!method.IsGenericMethod) {
                return method.DeclaringType.GetGenericArguments().Any(TypeIsGenericShared);
            }

            if (method.ContainsGenericParameters) {
                throw new InvalidOperationException("Cannot determine generic-sharedness without specific instantiation");
            }

            return method.GetGenericArguments().Any(TypeIsGenericShared) || method.DeclaringType.GetGenericArguments().Any(TypeIsGenericShared);
        }
        #endregion

        private static MethodBase GetMethodOnSelf(string name)
            => typeof(GenericDetourCoreCLR)
                        .GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

#if false

        private static IntPtr GetPtrForCtx(ref IntPtr ctx, string memberName)
            => ctx != IntPtr.Zero
                    ? ctx
                    : (ctx = GetMethodOnSelf(memberName).GetNativeStart());

        #region Thunk Jump Targets

        private static IntPtr fixupForThisPtrCtx = IntPtr.Zero;
        protected static IntPtr FixupForThisPtrContext
            => GetPtrForCtx(ref fixupForThisPtrCtx, nameof(FindAndFixupThunkForThisPtrContext));

        [MethodImpl((MethodImplOptions)512)] // mark it AggressiveOptimization if the runtime supports it
        private static IntPtr FindAndFixupThunkForThisPtrContext(object thisptr, int index, IntPtr origStart) {
            try {
                // this will only be called from ThisPtrThunk
                Instance.PatchInstanceForThisPtrContxt(thisptr, index, origStart);
                return origStart; // always re-call the original method to go through the patch
            } catch (Exception e) {
                MMDbgLog.Log($"An error ocurred while trying to resolve the target method pointer for index {index}: {e}");
                return PatchResolveFailureTarget;
            }
        }

        private static IntPtr fixupForMethodDescContext = IntPtr.Zero;
        protected static IntPtr FixupForMethodDescContext
            => GetPtrForCtx(ref fixupForMethodDescContext, nameof(FindAndFixupThunkForMethodDescContext));

        [MethodImpl((MethodImplOptions) 512)] // mark it AggressiveOptimization if the runtime supports it
        private static IntPtr FindAndFixupThunkForMethodDescContext(IntPtr methodDesc, int index, IntPtr origStart) {
            try {
                // methodDesc contains the MethodDesc* for the current method
                Instance.PatchInstanceForMethodDescContext(methodDesc, index, origStart);
                return origStart; // always re-call the original method to go through the patch
            } catch (Exception e) {
                MMDbgLog.Log($"An error ocurred while trying to resolve the target method pointer for index {index}: {e}");
                return PatchResolveFailureTarget;
            }
        }

        private static IntPtr fixupForMethodDescThisContext = IntPtr.Zero;
        protected static IntPtr FixupForMethodDescThisContext
            => GetPtrForCtx(ref fixupForMethodDescThisContext, nameof(FindAndFixupThunkForMethodDescThisContext));

        [MethodImpl((MethodImplOptions) 512)] // mark it AggressiveOptimization if the runtime supports it
        private static IntPtr FindAndFixupThunkForMethodDescThisContext(IntPtr methodDesc, int index, IntPtr origStart, object thisptr) {
            try {
                // methodDesc contains the MethodDesc* for the current method
                Instance.PatchInstanceForMethodDescThisContext(thisptr, methodDesc, index, origStart);
                return origStart; // always re-call the original method to go through the patch
            } catch (Exception e) {
                MMDbgLog.Log($"An error ocurred while trying to resolve the target method pointer for index {index}: {e}");
                return PatchResolveFailureTarget;
            }
        }

        private static IntPtr fixupForMethodTableContext = IntPtr.Zero;
        protected static IntPtr FixupForMethodTableContext
            => GetPtrForCtx(ref fixupForMethodTableContext, nameof(FindAndFixupThunkForMethodTableContext));

        [MethodImpl((MethodImplOptions) 512)] // mark it AggressiveOptimization if the runtime supports it
        private static IntPtr FindAndFixupThunkForMethodTableContext(IntPtr methodTable, int index, IntPtr origStart) {
            try {
                // methodTable contains the MethodTable* (the type) the current method is on
                Instance.PatchInstanceForMethodTableContext(methodTable, index, origStart);
                return origStart; // always re-call the original method to go through the patch
            } catch (Exception e) {
                MMDbgLog.Log($"An error ocurred while trying to resolve the target method pointer for index {index}: {e}");
                return PatchResolveFailureTarget;
            }
        }

        private static IntPtr patchResolveFailureTarget = IntPtr.Zero;
        private static IntPtr PatchResolveFailureTarget
            => GetPtrForCtx(ref patchResolveFailureTarget, nameof(FailedToResolvePatchTarget));
        [MethodImpl((MethodImplOptions) 512)] // mark it AggressiveOptimization if the runtime supports it
        private static void FailedToResolvePatchTarget() {
            throw new Exception("Could not resolve patch target; see mmdbglog for more information");
        }


        private static IntPtr unknownMethodAbi = IntPtr.Zero;
        protected static IntPtr UnknownMethodABI
            => GetPtrForCtx(ref unknownMethodAbi, nameof(UnknownMethodABIResolve));
        [MethodImpl((MethodImplOptions) 512)] // mark it AggressiveOptimization if the runtime supports it
        private static IntPtr UnknownMethodABIResolve() {
            return UnknownMethodABITarget;
        }

        private static IntPtr unknownMethodAbiTarget = IntPtr.Zero;
        private static IntPtr UnknownMethodABITarget
            => GetPtrForCtx(ref unknownMethodAbiTarget, nameof(UnknownMethodABIThrow));
        [MethodImpl((MethodImplOptions) 512)] // mark it AggressiveOptimization if the runtime supports it
        private static void UnknownMethodABIThrow() {
            throw new Exception("Unknown ABI for generic method instance");
        }
        #endregion

        #region Call thunk context adjustment
        private static IntPtr fixCtxThis2MTTarget = IntPtr.Zero;
        protected static IntPtr FixCtxThis2MTTarget
            => GetPtrForCtx(ref fixCtxThis2MTTarget, nameof(FixCtxThis2MT));
        [MethodImpl((MethodImplOptions) 512)]
        private static IntPtr FixCtxThis2MT(object ctx, int index) {
            return Instance.ConvertThis2MTCached(ctx, index);
        }
        private static IntPtr fixCtxThis2MDTarget = IntPtr.Zero;
        protected static IntPtr FixCtxThis2MDTarget
            => GetPtrForCtx(ref fixCtxThis2MDTarget, nameof(FixCtxThis2MD));
        [MethodImpl((MethodImplOptions) 512)]
        private static IntPtr FixCtxThis2MD(object ctx, int index) {
            return Instance.ConvertThis2MDCached(ctx, index);
        }

        private static IntPtr fixCtxMT2MTTarget = IntPtr.Zero;
        protected static IntPtr FixCtxMT2MTTarget
            => GetPtrForCtx(ref fixCtxMT2MTTarget, nameof(FixCtxMT2MT));
        [MethodImpl((MethodImplOptions) 512)]
        private static IntPtr FixCtxMT2MT(IntPtr ctx, int index) {
            return Instance.ConvertMT2MTCached(ctx, index);
        }

        private static IntPtr fixCtxMT2MDTarget = IntPtr.Zero;
        protected static IntPtr FixCtxMT2MDTarget
            => GetPtrForCtx(ref fixCtxMT2MDTarget, nameof(FixCtxMT2MD));
        [MethodImpl((MethodImplOptions) 512)]
        private static IntPtr FixCtxMT2MD(IntPtr ctx, int index) {
            return Instance.ConvertMT2MDCached(ctx, index);
        }

        private static IntPtr fixCtxMD2MTTarget = IntPtr.Zero;
        protected static IntPtr FixCtxMD2MTTarget
            => GetPtrForCtx(ref fixCtxMD2MTTarget, nameof(FixCtxMD2MT));
        [MethodImpl((MethodImplOptions) 512)]
        private static IntPtr FixCtxMD2MT(IntPtr ctx, int index) {
            return Instance.ConvertMD2MTCached(ctx, index);
        }

        private static IntPtr fixCtxMD2MDTarget = IntPtr.Zero;
        protected static IntPtr FixCtxMD2MDTarget
            => GetPtrForCtx(ref fixCtxMD2MDTarget, nameof(FixCtxMD2MD));
        [MethodImpl((MethodImplOptions) 512)]
        private static IntPtr FixCtxMD2MD(IntPtr ctx, int index) {
            return Instance.ConvertMD2MDCached(ctx, index);
        }

        private static IntPtr fixCtxMDT2MTTarget = IntPtr.Zero;
        protected static IntPtr FixCtxMDT2MTTarget
            => GetPtrForCtx(ref fixCtxMDT2MTTarget, nameof(FixCtxMDT2MT));
        [MethodImpl((MethodImplOptions) 512)]
        private static IntPtr FixCtxMDT2MT(IntPtr ctx, int index, object thisptr) {
            return Instance.ConvertMDT2MTCached(thisptr, ctx, index);
        }

        private static IntPtr fixCtxMDT2MDTarget = IntPtr.Zero;
        protected static IntPtr FixCtxMDT2MDTarget
            => GetPtrForCtx(ref fixCtxMDT2MDTarget, nameof(FixCtxMDT2MD));
        [MethodImpl((MethodImplOptions) 512)]
        private static IntPtr FixCtxMDT2MD(IntPtr ctx, int index, object thisptr) {
            return Instance.ConvertMDT2MDCached(thisptr, ctx, index);
        }
        #endregion
#endif

        //private static GenericDetourCoreCLR Instance;

        // we currently use stuff from this type, and this should only be running when this is the current anyway
        protected readonly DetourRuntimeNETCore30Platform netPlatform;

        protected GenericDetourCoreCLR() {
            //Instance = this;
            netPlatform = (DetourRuntimeNETCore30Platform) DetourHelper.Runtime;
            netPlatform.OnMethodCompiled += OnMethodCompiled;
        }

        private void OnMethodCompiled(MethodBase method, IntPtr codeStart, ulong codeSize) {
            if (method is null)
                return;
            if (!method.IsGenericMethod && !(method.DeclaringType?.IsGenericType ?? false))
                return; // the method is not generic at all

            MethodBase methodDecl = method;
            if (methodDecl is MethodInfo minfo && minfo.IsGenericMethod) {
                methodDecl = minfo.GetGenericMethodDefinition();
            }
            if (methodDecl.DeclaringType?.IsGenericType ?? false) {
                methodDecl = MethodBase.GetMethodFromHandle(methodDecl.MethodHandle, methodDecl.DeclaringType.GetGenericTypeDefinition().TypeHandle); ;
            }

            int index;
            lock (genericPatchesLockObject) {
                if (!patchedMethodIndexes.TryGetValue(methodDecl, out index))
                    return; // we have no patches for this registered
            }

            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);
            CreatePatchInstance(patchInfo, methodDecl, method, codeStart, index);
        }

        private void CreatePatchInstance(GenericPatchInfo patchInfo, MethodBase decl, MethodBase instance, IntPtr codeStart, int index) {
            lock (patchInfo) {
                InstantiationPatch patch = new(patchInfo, instance);
                InstantiationPatch existing = null;
                if (patchInfo.PatchedInstantiations.TryGetValue(instance, out InstantiationPatch existingnn)) {
                    existing = existingnn;
                    patchInfo.PatchedInstantiations[instance] = patch;
                } else {
                    patchInfo.PatchedInstantiations.Add(instance, patch);
                }

                if (existing != null) {
                    UnpatchInstantiation(existing);
                }
                patch.OriginalData = PatchInstantiation(patch, decl, instance, codeStart, index);
            }
        }

        protected NativeDetourData PatchUnshared(MethodBase from, IntPtr fromCodeStart, MethodBase to) {
            to = DetourHelper.Runtime.GetDetourTarget(from, to);
            NativeDetourData data = DetourHelper.Native.Create(fromCodeStart, to.GetNativeStart());

            // yes i overwrite data.Extra, so sue me
            data.Extra = DetourHelper.Native.MemAlloc(data.Size);
            DetourHelper.Native.Copy(fromCodeStart, data.Extra, data.Type);

            DetourHelper.Native.MakeWritable(data);
            DetourHelper.Native.Apply(data);
            DetourHelper.Native.MakeExecutable(data);

            data.Type |= 0x80; // set the high bit of Type so we can figure out which ones are ours
            return data;
        }

        private void UnpatchUnshared(NativeDetourData data) {
            data.Type &= 0x7F; // clear high bit

            DetourHelper.Native.MakeWritable(data);
            DetourHelper.Native.Copy(data.Extra, data.Method, data.Type);
            DetourHelper.Native.MakeExecutable(data);

            DetourHelper.Native.MemFree(data.Extra);
        }

        protected abstract NativeDetourData PatchInstantiation(InstantiationPatch patchInfo, MethodBase orig, MethodBase methodInstance, IntPtr codeStart, int index);
        protected abstract void UnpatchInstantiation(InstantiationPatch instantiation);
        private void UnpatchInstanceInternal(InstantiationPatch instantiation) {
            // we check the high bit of the patch type to figure out if this is our allocation
            if ((instantiation.OriginalData.Type & 0x80) != 0) {
                UnpatchUnshared(instantiation.OriginalData);
            } else {
                UnpatchInstantiation(instantiation);
            }
        }

        public class InstantiationPatch {
            public readonly GenericPatchInfo OwningPatchInfo;
            public readonly MethodBase SourceInstantiation;
            public NativeDetourData OriginalData;

            public InstantiationPatch(GenericPatchInfo gpi, MethodBase source) {
                OwningPatchInfo = gpi;
                SourceInstantiation = source;
            }
        }

        public class GenericPatchInfo {
            public readonly GenericDetourCoreCLR DetourRuntime;
            public readonly MethodBase SourceMethod;
            public readonly MethodInfo TargetMethod;
            public readonly Dictionary<MethodBase, InstantiationPatch> PatchedInstantiations = new();
            public readonly int Index;

            public GenericPatchInfo(GenericDetourCoreCLR runtime, int index, MethodBase source, MethodInfo target) {
                DetourRuntime = runtime;
                Index = index;
                SourceMethod = source;
                TargetMethod = target;
            }
        }

        private readonly object genericPatchesLockObject = new();
        private readonly List<GenericPatchInfo> genericPatches = new();
        private int lastCleared = 0;
        private readonly Dictionary<MethodBase, int> patchedMethodIndexes = new();

        int IGenericDetourPlatform.AddPatch(MethodBase from, MethodInfo to)
            => AddMethodPatch(from, to);

        protected int AddMethodPatch(MethodBase from, MethodInfo to) {
            // TODO: allow multi-patching, whether here or somewhere else

            if (!to.IsStatic) {
                throw new ArgumentException("Generic patch target must be static", nameof(to));
            }

            if (!to.IsGenericMethodDefinition) {
                throw new ArgumentException("Generic patch target must be generic method definition", nameof(to));
            }

            if (!from.IsGenericMethodDefinition && !(from.DeclaringType?.IsGenericType ?? false)) {
                throw new ArgumentException("Generic patch source must be a generic method definition", nameof(to));
            }

            Type[] allGenericArgs = GetAllGenericArguments(from, out int genericArgsInType, to.GetGenericArguments().Length);
            Type[] targetGenericArgs = to.GetGenericArguments();

            if (targetGenericArgs.Length != allGenericArgs.Length) {
                throw new ArgumentException("Generic patch target must have the same number of generic parameters as the source", nameof(to));
            }

            // ensure constraint compatibility
            for (int i = 0; i < allGenericArgs.Length; i++) {
                if (!CheckGenericParamCompatibility(allGenericArgs[i], targetGenericArgs[i]))
                    throw new ArgumentException("Generic patch target must have compatible constraints", nameof(to));
            }

            // ensure argument compatibility
            IEnumerable<Type> fromArgumentsE = from.GetParameters().Select(p => p.ParameterType);
            if (!from.IsStatic) {
                fromArgumentsE = new[] { from.GetThisParamType() }.Concat(fromArgumentsE);
            }
            Type[] fromArguments = fromArgumentsE.ToArray();
            Type[] toArguments = to.GetParameters().Select(p => p.ParameterType).ToArray();

            if (fromArguments.Length != toArguments.Length) {
                throw new ArgumentException("Generic patch target must have the same number of parameters as the source", nameof(to));
            }

            for (int i = 0; i < fromArguments.Length; i++) {
                if (!CheckArgumentCompatibility(fromArguments[i], toArguments[i], genericArgsInType))
                    throw new ArgumentException("Generic patch target arguments must match source", nameof(to));
            }

            // ensure return compatibility
            if (from is MethodInfo meth) {
                if (!CheckArgumentCompatibility(to.ReturnType, meth.ReturnType, genericArgsInType))
                    throw new ArgumentException("Generic patch target must return a compatible type", nameof(to));
            } else {
                if (to.ReturnType != typeof(void)) {
                    throw new ArgumentException("Generic patch target must return void", nameof(to));
                }
            }

            lock (genericPatchesLockObject) {
                if (patchedMethodIndexes.TryGetValue(from, out _))
                    throw new ArgumentException("Generic patch source has already been patched", nameof(from));

                int index = lastCleared;
                GenericPatchInfo patchInfo = new(this, index, from, to);
                if (index >= genericPatches.Count) {
                    genericPatches.Add(patchInfo);
                } else {
                    genericPatches[index] = patchInfo;
                }
                patchedMethodIndexes.Add(from, index);

                netPlatform.DisableInlining(from, from.MethodHandle);

                // find the next empty space and update lastCleared, if there is one
                do {
                    lastCleared++;
                } while (lastCleared < genericPatches.Count && genericPatches[lastCleared] == null);

                return index;
            }
        }

        private static bool CheckGenericParamCompatibility(Type from, Type to) {
            // our general rule is that to must have at most from attributes.
            //
            // in other words, we want this truth table for each bit of the attributes, where
            // the first bit is from, the second is to:
            // * 0 0 -> 1
            // * 0 1 -> 0
            // * 1 0 -> 1
            // * 1 1 -> 1
            // when inverted, is
            // * 0 0 -> 0
            // * 0 1 -> 1
            // * 1 0 -> 0
            // * 1 1 -> 0
            // so our original is just
            // * ~((a ^ b) & b)
            // but since we want every bit to be 1, we loose the compliment and compare to zero
            // * ((a ^ b) & b) == 0

            GenericParameterAttributes fromAttrs = from.GenericParameterAttributes;
            GenericParameterAttributes toAttrs = to.GenericParameterAttributes;
            if (((fromAttrs ^ toAttrs) & toAttrs) != 0) {
                return false;
            }

            // we want the same logic with constraints, but they're rather harder to do that with
            Type[] fromConstraints = from.GetGenericParameterConstraints();
            Type[] toConstraints = to.GetGenericParameterConstraints();

            if (fromConstraints.Length == 0 && toConstraints.Length == 0)
                return true; // short circuit if no other constraints

            Type fromBaseType = fromConstraints.FirstOrDefault(t => t.IsClass);
            Type toBaseType = toConstraints.FirstOrDefault(t => t.IsClass);

            if ((fromBaseType is not null ^ toBaseType is not null) && toBaseType is not null) {
                // to has a base constraint but from does not
                return false;
            }

            if (fromBaseType is not null && toBaseType is not null) {
                // both specify a base type
                if (!toBaseType.IsAssignableFrom(fromBaseType))
                    return false; // ensure compat between them
            }

            // all other generic parameters must be interfaces
            HashSet<Type> toInterfaces = new(toConstraints.Where(t => t.IsInterface));
            // given to's interface constraints, if we remove all of from's, then we should be left
            // with either none, or interfaces that from's base type constraint impelments
            foreach (Type type in fromConstraints)
                toInterfaces.Remove(type);
            if (toInterfaces.Count == 0)
                return true; // nothing left, we're good
            // otherwise, all remaining interfaces must be implemented by from's base type
            // if from doesn't have a base type, then we fail
            if (fromBaseType == null)
                return false;

            Type[] fromBaseInterfaces = fromBaseType.GetInterfaces();
            // we again remove them all to make sure none are left
            foreach (Type type in fromBaseInterfaces)
                toInterfaces.Remove(type);
            // our result is then whether or not toInterfaces is empty
            return toInterfaces.Count == 0;
        }

        private static bool CheckArgumentCompatibility(Type from, Type to, int fromArgsInType) {
            if (from.IsGenericParameter != to.IsGenericParameter) {
                throw new ArgumentException("Generic");
            }

            if (from.IsGenericParameter) {
                int position = from.GenericParameterPosition;
                if (from.DeclaringMethod != null) {
                    // the arg was declared by the method
                    position += fromArgsInType;
                }

                return position == to.GenericParameterPosition;
            } else {
                if (to == from)
                    return true;
                // parameter is just a normal type, so we do IsAssignableFrom
                if (to.IsAssignableFrom(from))
                    return true;
                // but From may be an inaccessible enum, so we also allow its underlying type
                if (from.IsEnum) {
                    if (to == from.GetEnumUnderlyingType())
                        return true;
                }

                return false;
            }
        }

        protected GenericPatchInfo GetPatchInfoFromIndex(int index) {
            return genericPatches[index];
        }

        void IGenericDetourPlatform.RemovePatch(int handle)
            => RemoveMethodPatch(handle);

        protected void RemoveMethodPatch(int index) {
            GenericPatchInfo patchInfo;
            lock (genericPatchesLockObject) {
                if (index < 0 || index >= genericPatches.Count || genericPatches[index] == null)
                    throw new ArgumentException("Invalid patch handle", nameof(index));

                patchInfo = genericPatches[index];
                genericPatches[index] = null;
                lastCleared = Math.Min(lastCleared, index);
                patchedMethodIndexes.Remove(patchInfo.SourceMethod);
            }

            foreach (KeyValuePair<MethodBase, InstantiationPatch> instances in patchInfo.PatchedInstantiations) {
                UnpatchInstanceInternal(instances.Value);
            }
        }

#if false
#region Real Target Locators
        protected void PatchInstanceForThisPtrContxt(object thisptr, int index, IntPtr realCodeStart) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);

            MethodBase realMethod = RealInstFromThis(thisptr, patchInfo);

            PatchMethodInst(patchInfo, realMethod, realCodeStart);
        }

        protected void PatchInstanceForMethodDescContext(IntPtr methodDesc, int index, IntPtr realCodeStart) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);

            MethodBase realMethod = RealInstFromMD(methodDesc);

            PatchMethodInst(patchInfo, realMethod, realCodeStart);
        }

        protected void PatchInstanceForMethodDescThisContext(object thisptr, IntPtr methodDesc, int index, IntPtr realCodeStart) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);

            MethodBase realMethod = RealInstFromMDT(thisptr, methodDesc, patchInfo);

            PatchMethodInst(patchInfo, realMethod, realCodeStart);
        }

        protected void PatchInstanceForMethodTableContext(IntPtr methodTable, int index, IntPtr realCodeStart) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);

            MethodBase realMethod = RealInstFromMT(methodTable, patchInfo);

            PatchMethodInst(patchInfo, realMethod, realCodeStart);
        }
#endregion
#endif
        
        // the maximum number of registers that may be used as arguments in the calling convention
        protected abstract int PlatMaxRegisterArgCount { get; }
        // a helper which, when called, returns the address of the information about a patchsite
        protected abstract MethodInfo PlatThunkGetReturnTargetHelper { get; }

        // bits 0 through PlatMaxRegisterArgCount-1 indicate the floatiness of arguments, bit PlatMaxRegisterArgCount indicates the floatiness of the return value
        protected abstract ulong GetFloatRegisterPattern(MethodBase method);

        private MethodInfo CreateStaticMethod(string typeName, Func<ModuleDefinition, MethodDefinition> createMethod) {
            const string Namespace = "MonoMod.RuntimeDetour.Platforms.Generic";
            using (ModuleDefinition module = ModuleDefinition.CreateModule($"{Namespace}.{typeName}", ModuleKind.Dll)) {
                TypeDefinition containingType = new(Namespace, typeName, CTypeAttributes.Public | CTypeAttributes.Abstract | CTypeAttributes.Sealed); // public static class
                containingType.BaseType = module.ImportReference(typeof(object));

                MethodDefinition method = createMethod(module);

                containingType.Methods.Add(method);
                module.Types.Add(containingType);

                module.Write($"{typeName.Replace('<', '_').Replace('>', '_')}.dll");

                Assembly helperAssembly = ReflectionHelper.Load(module);
                return helperAssembly.GetType(containingType.FullName, true).GetMethod(method.Name);
            }
        }

        private MethodDefinition CreateCallOrPrecallHelperBase(ModuleDefinition module, ulong floatRegisterPattern) {
            TypeReference intArg = module.ImportReference(typeof(IntPtr));
            TypeReference floatArg = module.ImportReference(typeof(double)); // I don't think there's a platform without native double support that CoreCLR targets

            MethodDefinition precallHelper = new("Helper",
                CMethodAttributes.Public | CMethodAttributes.Static,
                ((floatRegisterPattern >> PlatMaxRegisterArgCount) & 0x1) == 0 ? intArg : floatArg); // check the last bit of the register pattern for return type floatiness
            precallHelper.ImplAttributes = CMethodImplAttributes.IL | CMethodImplAttributes.NoInlining | (CMethodImplAttributes) 512; // flag it for aggressive optimization

            ulong bit = 1;
            for (int i = 0; i < PlatMaxRegisterArgCount; i++) {
                precallHelper.Parameters.Add(new((floatRegisterPattern & bit) == 0 ? intArg : floatArg));
                bit <<= 1;
            }

            return precallHelper;
        }

        private static readonly MethodInfo GCHandle_FromIntPtr = typeof(GCHandle).GetMethod(nameof(GCHandle.FromIntPtr), BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo GCHandle_Target = typeof(GCHandle).GetProperty(nameof(GCHandle.Target), BindingFlags.Public | BindingFlags.Instance).GetGetMethod();
        protected static readonly FieldInfo InstantiationPatch_OwningPatchInfo = typeof(InstantiationPatch).GetField(nameof(InstantiationPatch.OwningPatchInfo));
        protected static readonly FieldInfo GenericPatchInfo_DetourRuntime = typeof(GenericPatchInfo).GetField(nameof(GenericPatchInfo.DetourRuntime));

        #region Precall helpers
        private static readonly MethodInfo GenericDetourCoreCLR_PrecallBackpatch = typeof(GenericDetourCoreCLR).GetMethod(nameof(GenericPrecallDoFixup), BindingFlags.Public | BindingFlags.Instance);

        private MethodInfo CreatePrecallHelper(ulong floatRegisterPattern)
            => CreateStaticMethod($"Precall<{floatRegisterPattern:X16}>", module => {
                MethodDefinition precallHelper = CreateCallOrPrecallHelperBase(module, floatRegisterPattern);
                ILProcessor il = precallHelper.Body.GetILProcessor();

                TypeReference intPtr = module.ImportReference(typeof(IntPtr));

                VariableDefinition patchCallTargetPtrPtr = new(intPtr);
                VariableDefinition gctxDecoderPtrPtr = new(intPtr);
                VariableDefinition genericPatchInfo = new(module.ImportReference(typeof(InstantiationPatch)));
                VariableDefinition gcHandle = new(module.ImportReference(typeof(GCHandle)));
                precallHelper.Body.Variables.AddRange(new[] { patchCallTargetPtrPtr, gctxDecoderPtrPtr, genericPatchInfo, gcHandle });

                // first thing we always do is get our patchsite info
                il.Emit(OpCodes.Call, module.ImportReference(PlatThunkGetReturnTargetHelper));
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Stloc, patchCallTargetPtrPtr);

                il.Emit(OpCodes.Ldc_I4, IntPtr.Size);
                il.Emit(OpCodes.Add);

                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldind_I); // we don't store the pointer here, because we never need to modify this; it will remain the same even post-precall
                // lets load our gchandle inline
                il.Emit(OpCodes.Call, module.ImportReference(GCHandle_FromIntPtr));
                il.Emit(OpCodes.Stloc, gcHandle);
                il.Emit(OpCodes.Ldloca, gcHandle);
                il.Emit(OpCodes.Call, module.ImportReference(GCHandle_Target));
                il.Emit(OpCodes.Castclass, genericPatchInfo.VariableType);
                il.Emit(OpCodes.Stloc, genericPatchInfo);

                il.Emit(OpCodes.Ldc_I4, IntPtr.Size);
                il.Emit(OpCodes.Add);

                il.Emit(OpCodes.Stloc, gctxDecoderPtrPtr);

                // stack should be empty here

                // we'll start preparing to call into ourselves for the backpatch
                il.Emit(OpCodes.Ldloc, genericPatchInfo);
                il.Emit(OpCodes.Ldfld, module.ImportReference(InstantiationPatch_OwningPatchInfo));
                il.Emit(OpCodes.Ldfld, module.ImportReference(GenericPatchInfo_DetourRuntime));

                il.Emit(OpCodes.Ldloc, genericPatchInfo);

                // now that we have our patchsite info, lets call our generic context decoder

                // load arguments while building our callsites
                CCallSite decoderCallSite = new(module.ImportReference(typeof(MethodBase))) {
                    CallingConvention = MethodCallingConvention.Default,
                    HasThis = false,
                    ExplicitThis = false
                };

                il.Emit(OpCodes.Ldloc, genericPatchInfo);
                decoderCallSite.Parameters.Add(new(genericPatchInfo.VariableType));
                foreach (ParameterDefinition param in precallHelper.Parameters) {
                    il.Emit(OpCodes.Ldarg, param);
                    decoderCallSite.Parameters.Add(new(param.ParameterType));
                }
                il.Emit(OpCodes.Ldloc, gctxDecoderPtrPtr);
                il.Emit(OpCodes.Ldind_I); // get the decoder pointer on top of the stack
                // perform the call
                il.Emit(OpCodes.Calli, decoderCallSite);

                // load the patchsite data ptr, then call our fixup method
                il.Emit(OpCodes.Ldloc, patchCallTargetPtrPtr);
                il.Emit(OpCodes.Call, module.ImportReference(GenericDetourCoreCLR_PrecallBackpatch));
                // this returns where we should call to in the end
                il.Emit(OpCodes.Stloc, patchCallTargetPtrPtr); // reuse this variable for my sanity

                // build the callsite while loading parameters
                CCallSite tailCallSite = new(precallHelper.ReturnType) {
                    CallingConvention = MethodCallingConvention.Default,
                    HasThis = false,
                    ExplicitThis = false
                };
                foreach (ParameterDefinition param in precallHelper.Parameters) {
                    il.Emit(OpCodes.Ldarg, param);
                    tailCallSite.Parameters.Add(new(param.ParameterType));
                }
                il.Emit(OpCodes.Ldloc, patchCallTargetPtrPtr);
                // tailcall to our target
                il.Emit(OpCodes.Tail);
                il.Emit(OpCodes.Calli, tailCallSite);
                il.Emit(OpCodes.Ret);

                return precallHelper;
            });

        private readonly ConcurrentDictionary<ulong, MethodInfo> precallHelperMethods = new();
        private MethodInfo GetOrCreatePrecallHelper(ulong floatRegisterPattern) {
            return precallHelperMethods.GetOrAdd(floatRegisterPattern, CreatePrecallHelper);
        }

        // The precall helper ABI is as follows:
        // - PlatThunkGetReturnTargetHelper must take no arguments and return a pointer to an array of 3 pointers:
        //   - 0: the pointer to the precall helper body. This will be updated by the precall helper to point to the final thunk.
        //   - 1: a GCHandle pointer to the InstantiationPatch for the method instantiation the precall helper is attempting to patch.
        //   - 2: a pointer to the appropriate generic context decoder for the method being patched.
        // - The generic context decoder in slot 2 returns the MethodBase for the generic instantiation, and takes the following parameters, in order:
        //   - The InstantiationPatch object for the method instance being patched
        //   - PlatMaxRegisterArgCount pointer or floating point arguments, most of which should be ignored, save the one which corresponds to the generic context.

        protected MethodInfo GetPrecallHelper(MethodBase srcMethod) {
            return GetOrCreatePrecallHelper(GetFloatRegisterPattern(srcMethod));
        }
        #endregion

        public unsafe IntPtr GenericPrecallDoFixup(InstantiationPatch patchInfo, MethodBase instance, IntPtr* patchsiteData) {
            // patchsiteData has, in slot 0, the call target ptr, and in slot 1, the patchInfo GCHandle

            MethodBase targetMethod = BuildInstantiationForMethod(patchInfo.OwningPatchInfo.TargetMethod, instance);
            IntPtr callHelper = GetCallHelperFor(patchInfo, instance, targetMethod).Pin().GetNativeStart();
            IntPtr targetMethodBody = GetSharedMethodBody(targetMethod);

            DetourHelper.Native.MakeWritable((IntPtr) patchsiteData, (uint)IntPtr.Size * 3);
            patchsiteData[0] = callHelper; // set the call helper target
            patchsiteData[2] = targetMethodBody; // set the target body
            // the patchsiteData is in an executable block, so we want to make sure we restore its permissions correctly
            DetourHelper.Native.MakeExecutable((IntPtr) patchsiteData, (uint) IntPtr.Size * 3);

            // whatever this returns will be what the precall jumps to afterward
            // therefore, it should return the address of the original patch call to automatically execute the 
            //   newly-patched instance
            // this is stored in patchInfo.OriginalData.Method
            return patchInfo.OriginalData.Method;
        }

        protected abstract IntPtr GetSharedMethodBody(MethodBase method);

        protected abstract MethodInfo GetCallHelperFor(InstantiationPatch patch, MethodBase realSrc, MethodBase realTarget);

        #region Call helpers
        private struct CallHelperCacheKey : IEquatable<CallHelperCacheKey> {
            public readonly ulong FloatPattern;
            public readonly int Kind;
            public CallHelperCacheKey(ulong floatPattern, int kind) {
                FloatPattern = floatPattern;
                Kind = kind;
            }
            public bool Equals(CallHelperCacheKey other)
                => FloatPattern == other.FloatPattern && Kind == other.Kind;
            public override int GetHashCode() {
                int hashCode = 1193496116;
                hashCode = hashCode * -1521134295 + FloatPattern.GetHashCode();
                hashCode = hashCode * -1521134295 + Kind.GetHashCode();
                return hashCode;
            }
        }

        private MethodInfo CreateCallHelper(CallHelperCacheKey cacheKey)
            => CreateStaticMethod($"Call<{cacheKey.FloatPattern:X16}-{cacheKey.Kind}>", module => {
                MethodDefinition callHelper = CreateCallOrPrecallHelperBase(module, cacheKey.FloatPattern);
                ILProcessor il = callHelper.Body.GetILProcessor();

                TypeReference intPtr = module.ImportReference(typeof(IntPtr));

                VariableDefinition callTarget = new(intPtr);
                VariableDefinition genericPatchInfo = new(module.ImportReference(typeof(InstantiationPatch)));
                VariableDefinition gcHandle = new(module.ImportReference(typeof(GCHandle)));
                callHelper.Body.Variables.AddRange(new[] { callTarget, genericPatchInfo, gcHandle });

                // first thing we always do is get our patchsite info
                il.Emit(OpCodes.Call, module.ImportReference(PlatThunkGetReturnTargetHelper));

                // starting at the gchandle
                il.Emit(OpCodes.Ldc_I4, IntPtr.Size);
                il.Emit(OpCodes.Add);

                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldind_I);
                // lets load our gchandle inline
                il.Emit(OpCodes.Call, module.ImportReference(GCHandle_FromIntPtr));
                il.Emit(OpCodes.Stloc, gcHandle);
                il.Emit(OpCodes.Ldloca, gcHandle);
                il.Emit(OpCodes.Call, module.ImportReference(GCHandle_Target));
                il.Emit(OpCodes.Castclass, genericPatchInfo.VariableType);
                il.Emit(OpCodes.Stloc, genericPatchInfo);

                il.Emit(OpCodes.Ldc_I4, IntPtr.Size);
                il.Emit(OpCodes.Add);

                il.Emit(OpCodes.Ldind_I);
                il.Emit(OpCodes.Stloc, callTarget);

                // stack is empty here

                // do argument fixups
                CCallSite finalCallSite = EmitArgumentFixupForCall(module, callHelper, il, genericPatchInfo, callTarget, cacheKey.FloatPattern, cacheKey.Kind);

                // by this point, the stack should be set up for our final call, minus the actual method pointer
                // so lets load the method pointer
                il.Emit(OpCodes.Ldloc, callTarget);

                // now we should be fully set up to just do the call
                il.Emit(OpCodes.Tail);
                il.Emit(OpCodes.Calli, finalCallSite);
                il.Emit(OpCodes.Ret);

                return callHelper;
            });

        private readonly ConcurrentDictionary<CallHelperCacheKey, MethodInfo> callHelperMethods = new();
        private MethodInfo GetOrCreateCallHelper(ulong floatRegisterPattern, int kind) {
            return callHelperMethods.GetOrAdd(new(floatRegisterPattern, kind), CreateCallHelper);
        }

        // The call helper ABI is as follows:
        // - PlatThunkGetReturnTargetHelper must take no arguments and return a pointer to an array of 3 pointers:
        //   - 0: the pointer to the call helper body.
        //   - 1: A GCHandle pointer to the InstantiationPatch for the method instantion for the patched method.
        //   - 2: A pointer to the target method body.

        protected MethodInfo GetCallHelper(MethodBase srcMethod, int kind) {
            return GetOrCreateCallHelper(GetFloatRegisterPattern(srcMethod), kind);
        }

        //   When control leaves code emitted by this method, the stack must contain all arguments, in their new order,
        // as required by the returned CCallSite.
        protected abstract CCallSite EmitArgumentFixupForCall(ModuleDefinition module, MethodDefinition method, ILProcessor il,
            VariableDefinition instantiationPatchVar, VariableDefinition jumpTargetVar, ulong floatRegPattern, int kind);
        #endregion

#if false
        #region Actual Conversions
        private class IntPtrWrapper {
            public IntPtr Value = IntPtr.Zero;
        }

        // TODO: when unpatching a patch, clear the associated index entry
        private readonly ConditionalWeakTable<object, ConcurrentDictionary<int, IntPtr>> thisConvCache = new();
        private IntPtr ConvertThis2MTCached(object thisptr, int index) {
            ConcurrentDictionary<int, IntPtr> lookup = thisConvCache.GetOrCreateValue(thisptr);
            return lookup.GetOrAdd(index, i => ConvertThis2MT(thisptr, index));
        }
        private IntPtr ConvertThis2MT(object thisptr, int index) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);
            MethodBase realSrc = RealInstFromThis(thisptr, patchInfo);
            MethodBase target = GetTargetInstantiation(patchInfo, realSrc);
            return RealTargetToMT(target);
        }
        private IntPtr ConvertThis2MDCached(object thisptr, int index) {
            ConcurrentDictionary<int, IntPtr> lookup = thisConvCache.GetOrCreateValue(thisptr);
            return lookup.GetOrAdd(index, i => ConvertThis2MD(thisptr, index));
        }
        private IntPtr ConvertThis2MD(object thisptr, int index) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);
            MethodBase realSrc = RealInstFromThis(thisptr, patchInfo);
            MethodBase target = GetTargetInstantiation(patchInfo, realSrc);
            return RealTargetToMD(target);
        }

        // TODO: figure out how to clear the correct parts of this cache when unpatching
        private readonly ConcurrentDictionary<IntPtr, IntPtr> otherCtxConvCache = new();
        private IntPtr ConvertMT2MTCached(IntPtr ctx, int index) {
            return otherCtxConvCache.GetOrAdd(ctx, c => ConvertMT2MT(c, index));
        }
        private IntPtr ConvertMT2MT(IntPtr ctx, int index) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);
            MethodBase realSrc = RealInstFromMT(ctx, patchInfo);
            MethodBase target = GetTargetInstantiation(patchInfo, realSrc);
            return RealTargetToMT(target);
        }
        private IntPtr ConvertMT2MDCached(IntPtr ctx, int index) {
            return otherCtxConvCache.GetOrAdd(ctx, c => ConvertMT2MD(c, index));
        }
        private IntPtr ConvertMT2MD(IntPtr ctx, int index) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);
            MethodBase realSrc = RealInstFromMT(ctx, patchInfo);
            MethodBase target = GetTargetInstantiation(patchInfo, realSrc);
            return RealTargetToMD(target);
        }
        private IntPtr ConvertMD2MTCached(IntPtr ctx, int index) {
            return otherCtxConvCache.GetOrAdd(ctx, c => ConvertMD2MT(c, index));
        }
        private IntPtr ConvertMD2MT(IntPtr ctx, int index) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);
            MethodBase realSrc = RealInstFromMD(ctx);
            MethodBase target = GetTargetInstantiation(patchInfo, realSrc);
            return RealTargetToMT(target);
        }
        private IntPtr ConvertMD2MDCached(IntPtr ctx, int index) {
            return otherCtxConvCache.GetOrAdd(ctx, c => ConvertMD2MD(c, index));
        }
        private IntPtr ConvertMD2MD(IntPtr ctx, int index) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);
            MethodBase realSrc = RealInstFromMD(ctx);
            MethodBase target = GetTargetInstantiation(patchInfo, realSrc);
            return RealTargetToMD(target);
        }


        private readonly ConditionalWeakTable<object, ConcurrentDictionary<IntPtr, IntPtr>> mdThisConvCache = new();
        private IntPtr ConvertMDT2MTCached(object thisptr, IntPtr ctx, int index) {
            ConcurrentDictionary<IntPtr, IntPtr> lookup = mdThisConvCache.GetOrCreateValue(thisptr);
            return lookup.GetOrAdd(ctx, i => ConvertMDT2MT(thisptr, ctx, index));
        }
        private IntPtr ConvertMDT2MT(object thisptr, IntPtr ctx, int index) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);
            MethodBase realSrc = RealInstFromMDT(thisptr, ctx, patchInfo);
            MethodBase target = GetTargetInstantiation(patchInfo, realSrc);
            return RealTargetToMT(target);
        }
        private IntPtr ConvertMDT2MDCached(object thisptr, IntPtr ctx, int index) {
            ConcurrentDictionary<IntPtr, IntPtr> lookup = mdThisConvCache.GetOrCreateValue(thisptr);
            return lookup.GetOrAdd(ctx, i => ConvertMDT2MD(thisptr, ctx, index));
        }
        private IntPtr ConvertMDT2MD(object thisptr, IntPtr ctx, int index) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);
            MethodBase realSrc = RealInstFromMDT(thisptr, ctx, patchInfo);
            MethodBase target = GetTargetInstantiation(patchInfo, realSrc);
            return RealTargetToMD(target);
        }
        #endregion
#endif

        #region Instantiation Lookup
        private static Type RealTypeFromThis(object thisptr, GenericPatchInfo patchInfo) {
            MethodBase origSrc = patchInfo.SourceMethod;
            Type origType = origSrc.DeclaringType;
            Type realType = thisptr.GetType();

            if (origType.IsInterface) {
                realType = realType.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == origType)
                    .First();
            } else {
                while (realType.GetGenericTypeDefinition() != origType) {
                    realType = realType.BaseType;
                }
            }

            return realType;
        }

        protected static MethodBase RealInstFromThis(object thisptr, GenericPatchInfo patchInfo) {
            Type realType = RealTypeFromThis(thisptr, patchInfo);

            // find the actual method impl using this whacky type handle workaround

            RuntimeMethodHandle origHandle = patchInfo.SourceMethod.MethodHandle;
            RuntimeTypeHandle realTypeHandle = realType.TypeHandle;

            return MethodBase.GetMethodFromHandle(origHandle, realTypeHandle);
        }

        protected MethodBase RealInstFromMD(IntPtr methodDesc) {
            RuntimeMethodHandle handle = netPlatform.CreateHandleForHandlePointer(methodDesc);
            return MethodBase.GetMethodFromHandle(handle);
        }

        protected MethodBase RealInstFromMDT(object thisptr, IntPtr methodDesc, GenericPatchInfo patchInfo) {
            RuntimeMethodHandle handle = netPlatform.CreateHandleForHandlePointer(methodDesc);
            Type realType = RealTypeFromThis(thisptr, patchInfo);
            return MethodBase.GetMethodFromHandle(handle, realType.TypeHandle);
        }

        protected MethodBase RealInstFromMT(IntPtr methodTable, GenericPatchInfo patchInfo) {
            MethodBase origSrc = patchInfo.SourceMethod;

            Type realType = netPlatform.GetTypeFromNativeHandle(methodTable);

            // find the actual method impl using this whacky type handle workaround

            RuntimeMethodHandle origHandle = origSrc.MethodHandle;
            RuntimeTypeHandle realTypeHandle = realType.TypeHandle;

            return MethodBase.GetMethodFromHandle(origHandle, realTypeHandle);
        }

        protected static readonly MethodBase RealTargetToMethodDescMeth = GetMethodOnSelf(nameof(RealTargetToMethodDesc));
        protected static readonly MethodBase RealTargetToMethodTableMeth = GetMethodOnSelf(nameof(RealTargetToMethodTable));

        public static IntPtr RealTargetToMethodDesc(MethodBase method) {
            return method.MethodHandle.Value;
        }

        public static IntPtr RealTargetToMethodTable(MethodBase method) {
            return method.DeclaringType.TypeHandle.Value;
        }
#endregion

        protected virtual MethodBase GetTargetInstantiation(GenericPatchInfo patch, MethodBase realSrc) {
            return BuildInstantiationForMethod(patch.TargetMethod, realSrc);
        }

        private Type[] GetAllGenericArguments(MethodBase instance, out int numInType, int countHint = 0) {
            List<Type> typeArguments = new(countHint);

            if (instance.DeclaringType?.IsGenericType ?? false) {
                typeArguments.AddRange(instance.DeclaringType.GetGenericArguments());
            }

            numInType = typeArguments.Count;

            if (instance.IsGenericMethod) {
                typeArguments.AddRange(instance.GetGenericArguments());
            }

            return typeArguments.ToArray();
        }

        protected virtual MethodBase BuildInstantiationForMethod(MethodInfo def, MethodBase instance) {
            //   for now, we'll support only one kind of transformation: into a method with all of the arguments
            // in order
            Type[] allGenericArgs = GetAllGenericArguments(instance, out _, def.GetGenericArguments().Length);
            return def.MakeGenericMethod(allGenericArgs);
        }

    }
}
#endif