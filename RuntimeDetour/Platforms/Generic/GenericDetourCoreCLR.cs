#if !NET35
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

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
    abstract class GenericDetourCoreCLR : IGenericDetourPlatform {

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
                return TypeIsGenericShared(method.DeclaringType);
            }

            if (method.ContainsGenericParameters) {
                throw new InvalidOperationException("Cannot determine generic-sharedness without specific instantiation");
            }

            return method.GetGenericArguments().Any(TypeIsGenericShared);
        }

        protected static bool MethodRequiresReturnBuffer(MethodBase method) {
            // TODO: implement
            return false;
        }

        /*
//*******************************************************************************
BOOL MethodDesc::IsSharedByGenericInstantiations()
{
    LIMITED_METHOD_DAC_CONTRACT;

    if (IsWrapperStub())
        return FALSE;
    else if (GetMethodTable()->IsSharedByGenericInstantiations())
        return TRUE;
    else return IsSharedByGenericMethodInstantiations();
}

//*******************************************************************************
BOOL MethodDesc::IsSharedByGenericMethodInstantiations()
{
    LIMITED_METHOD_DAC_CONTRACT;

    if (GetClassification() == mcInstantiated)
        return AsInstantiatedMethodDesc()->IMD_IsSharedByGenericMethodInstantiations();
    else return FALSE;
}
        
//*******************************************************************************
    BOOL IMD_IsGenericMethodDefinition()
    {
        LIMITED_METHOD_DAC_CONTRACT;

        return((m_wFlags2 & KindMask) == GenericMethodDefinition);
    }

    BOOL IMD_IsSharedByGenericMethodInstantiations()
    {
        LIMITED_METHOD_DAC_CONTRACT;

        return((m_wFlags2 & KindMask) == SharedMethodInstantiation);
    }
        */

        /*
        MT->IsSharedByGenericInstantiations() is whether or not this is the canonical instance
         */
        #endregion
        private static MethodBase GetMethodOnSelf(string name)
            => typeof(GenericDetourCoreCLR)
                        .GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

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
        #endregion

        private static GenericDetourCoreCLR Instance;

        // we currently use stuff from this type, and this should only be running when this is the current anyway
        private readonly DetourRuntimeNETCore30Platform netPlatform;

        protected GenericDetourCoreCLR() {
            Instance = this;
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
            NativeDetourData detourData = PatchInstantiation(decl, instance, codeStart, index);
            InstantiationPatch patch = new(instance, detourData);
            InstantiationPatch? existing = null;
            lock (patchInfo) {
                if (patchInfo.PatchedInstantiations.TryGetValue(instance, out InstantiationPatch existingnn)) {
                    existing = existingnn;
                    patchInfo.PatchedInstantiations[instance] = patch;
                } else {
                    patchInfo.PatchedInstantiations.Add(instance, patch);
                }
            }
            if (existing != null) {
                UnpatchInstantiation(existing.Value); // specifically do it *outside* the lock
            }
        }


        protected abstract NativeDetourData PatchInstantiation(MethodBase orig, MethodBase methodInstance, IntPtr codeStart, int index);
        protected abstract void UnpatchInstantiation(InstantiationPatch instantiation);

        protected struct InstantiationPatch {
            public readonly MethodBase SourceInstantiation;
            public readonly NativeDetourData OriginalData;

            public InstantiationPatch(MethodBase source, NativeDetourData origData) {
                SourceInstantiation = source;
                OriginalData = origData;
            }
        }

        protected class GenericPatchInfo {
            public readonly int Index;
            public readonly MethodBase SourceMethod;
            public readonly MethodInfo TargetMethod;
            public readonly Dictionary<MethodBase, InstantiationPatch> PatchedInstantiations = new();

            public GenericPatchInfo(int index, MethodBase source, MethodInfo target) {
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
            // TODO: validate that provided methods are compatible
            lock (genericPatchesLockObject) {
                int index = lastCleared;
                GenericPatchInfo patchInfo = new(index, from, to);
                if (index >= genericPatches.Count) {
                    genericPatches.Add(patchInfo);
                } else {
                    genericPatches[index] = patchInfo;
                }
                patchedMethodIndexes.Add(from, index);

                // find the next empty space and update lastCleared, if there is one
                do {
                    lastCleared++;
                } while (lastCleared < genericPatches.Count && genericPatches[lastCleared] == null);

                return index;
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
                UnpatchInstantiation(instances.Value);
            }
        }

        #region Real Target Locators
        protected void PatchInstanceForThisPtrContxt(object thisptr, int index, IntPtr realCodeStart) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);

            MethodBase realMethod = RealInstFromThis(thisptr, patchInfo);

            PatchMethodInst(patchInfo, realMethod, realCodeStart);
        }

        protected void PatchInstanceForMethodDescContext(IntPtr methodDesc, int index, IntPtr realCodeStart) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);

            RuntimeMethodHandle handle = netPlatform.CreateHandleForHandlePointer(methodDesc);

            MethodBase realMethod = MethodBase.GetMethodFromHandle(handle);

            PatchMethodInst(patchInfo, realMethod, realCodeStart);
        }

        protected void PatchInstanceForMethodTableContext(IntPtr methodTable, int index, IntPtr realCodeStart) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);

            MethodBase realMethod = RealInstFromMT(methodTable, patchInfo);

            PatchMethodInst(patchInfo, realMethod, realCodeStart);
        }
        #endregion

        protected abstract void PatchMethodInst(GenericPatchInfo patch, MethodBase realSrc, IntPtr realCodeStart);

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
        #endregion

        #region Instantiation Lookup
        private MethodBase RealInstFromThis(object thisptr, GenericPatchInfo patchInfo) {
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

            // find the actual method impl using this whacky type handle workaround

            RuntimeMethodHandle origHandle = origSrc.MethodHandle;
            RuntimeTypeHandle realTypeHandle = realType.TypeHandle;

            return MethodBase.GetMethodFromHandle(origHandle, realTypeHandle);
        }

        private MethodBase RealInstFromMD(IntPtr methodDesc) {
            RuntimeMethodHandle handle = netPlatform.CreateHandleForHandlePointer(methodDesc);
            return MethodBase.GetMethodFromHandle(handle);
        }

        private MethodBase RealInstFromMT(IntPtr methodTable, GenericPatchInfo patchInfo) {
            MethodBase origSrc = patchInfo.SourceMethod;

            Type realType = netPlatform.GetTypeFromNativeHandle(methodTable);

            // find the actual method impl using this whacky type handle workaround

            RuntimeMethodHandle origHandle = origSrc.MethodHandle;
            RuntimeTypeHandle realTypeHandle = realType.TypeHandle;

            return MethodBase.GetMethodFromHandle(origHandle, realTypeHandle);
        }

        private IntPtr RealTargetToMD(MethodBase method) {
            return method.MethodHandle.Value;
        }

        private IntPtr RealTargetToMT(MethodBase method) {
            return method.DeclaringType.TypeHandle.Value;
        }
        #endregion

        protected virtual MethodBase GetTargetInstantiation(GenericPatchInfo patch, MethodBase realSrc) {
            return BuildInstantiationForMethod(patch.TargetMethod, realSrc);
        }

        protected virtual MethodBase BuildInstantiationForMethod(MethodInfo def, MethodBase instance) {
            //   for now, we'll support only one kind of transformation: into a method with all of the arguments
            // in order

            List<Type> typeArguments = new(def.GetGenericArguments().Length);

            if (instance.DeclaringType?.IsGenericType ?? false) {
                typeArguments.AddRange(instance.DeclaringType.GetGenericArguments());
            }

            if (instance.IsGenericMethod) {
                typeArguments.AddRange(instance.GetGenericArguments());
            }

            return def.MakeGenericMethod(typeArguments.ToArray());
        }

    }
}
#endif