using MonoMod.RuntimeDetour;
using MonoMod.RuntimeDetour.Platforms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.Common.RuntimeDetour.Platforms {

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
                method.IsGenericMethod &&
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

        #region Thunk Jump Targets
        private static MethodBase GetMethodOnSelf(string name)
            => typeof(GenericDetourCoreCLR)
                        .GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        private static void BackpatchJump(IntPtr source, IntPtr target) {
            IDetourNativePlatform platform = DetourHelper.Native;
            NativeDetourData data = platform.Create(source, target);
            platform.MakeWritable(data);
            platform.Apply(data); // we just write a standard jump at this point
            platform.MakeExecutable(data);
        }

        private static IntPtr fixupForThisPtrCtx = IntPtr.Zero;
        private static IntPtr FixupForThisPtrContext
            => fixupForThisPtrCtx != IntPtr.Zero
                    ? fixupForThisPtrCtx
                    : (fixupForThisPtrCtx = GetMethodOnSelf(nameof(FindAndFixupThunkForThisPtrContext)).GetNativeStart());

        private static IntPtr FindAndFixupThunkForThisPtrContext(object thisptr, int index, IntPtr origStart) {
            try {
                // this will only be called from ThisPtrThunk
                IntPtr target = Instance.FindTargetForThisPtrContext(thisptr, index);
                BackpatchJump(origStart, target);
                return target;
            } catch (Exception e) {
                MMDbgLog.Log($"An error ocurred while trying to resolve the target method pointer for index {index}: {e}");
                return PatchResolveFailureTarget;
            }
        }

        private static IntPtr fixupForMethodDescContext = IntPtr.Zero;
        private static IntPtr FixupForMethodDescContext
            => fixupForMethodDescContext != IntPtr.Zero
                    ? fixupForMethodDescContext
                    : (fixupForMethodDescContext = GetMethodOnSelf(nameof(FindAndFixupThunkForMethodDescContext)).GetNativeStart());

        private static IntPtr FindAndFixupThunkForMethodDescContext(IntPtr methodDesc, int index, IntPtr origStart) {
            try {
                // methodDesc contains the MethodDesc* for the current method
                IntPtr target = Instance.FindTargetForMethodDescContext(methodDesc, index);
                BackpatchJump(origStart, target);
                return target;
            } catch (Exception e) {
                MMDbgLog.Log($"An error ocurred while trying to resolve the target method pointer for index {index}: {e}");
                return PatchResolveFailureTarget;
            }
        }

        private static IntPtr fixupForMethodTableContext = IntPtr.Zero;
        private static IntPtr FixupForMethodTableContext
            => fixupForMethodTableContext != IntPtr.Zero
                    ? fixupForMethodTableContext
                    : (fixupForMethodTableContext = GetMethodOnSelf(nameof(FindAndFixupThunkForMethodTableContext)).GetNativeStart());

        private static IntPtr FindAndFixupThunkForMethodTableContext(IntPtr methodTable, int index, IntPtr origStart) {
            try {
                // methodTable contains the MethodTable* (the type) the current method is on
                IntPtr target = Instance.FindTargetForMethodTableContext(methodTable, index);
                BackpatchJump(origStart, target);
                return target;
            } catch (Exception e) {
                MMDbgLog.Log($"An error ocurred while trying to resolve the target method pointer for index {index}: {e}");
                return PatchResolveFailureTarget;
            }
        }

        private static IntPtr patchResolveFailureTarget = IntPtr.Zero;
        private static IntPtr PatchResolveFailureTarget
            => patchResolveFailureTarget != IntPtr.Zero
                    ? patchResolveFailureTarget
                    : (patchResolveFailureTarget = GetMethodOnSelf(nameof(FailedToResolvePatchTarget)).GetNativeStart());
        private static void FailedToResolvePatchTarget() {
            throw new Exception("Could not resolve patch target; see mmdbg for more information");
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
            if (!method.IsGenericMethod && !method.DeclaringType.IsGenericType)
                return; // the method is not generic at all

            MethodBase methodDecl = method;
            if (methodDecl is MethodInfo minfo && minfo.IsGenericMethod) {
                methodDecl = minfo.GetGenericMethodDefinition();
            }
            if (methodDecl.DeclaringType.IsGenericType) {
                methodDecl = MethodBase.GetMethodFromHandle(methodDecl.MethodHandle, methodDecl.DeclaringType.GetGenericTypeDefinition().TypeHandle); ;
            }

            int index;
            lock (genericPatchesLockObject) {
                if (!patchedMethodIndexes.TryGetValue(method, out index))
                    return; // we have no patches for this registered
            }

            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);
            CreatePatchInstance(patchInfo, methodDecl, method, codeStart, index);
        }

        private void CreatePatchInstance(GenericPatchInfo patchInfo, MethodBase decl, MethodBase instance, IntPtr codeStart, int index) {
            NativeDetourData detourData = PatchInstantiation(decl, instance, codeStart, index);
            InstantiationPatch patch = new InstantiationPatch(instance, detourData);
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
            public readonly MethodBase SourceMethod;
            public readonly MethodBase TargetMethod;
            public readonly Dictionary<MethodBase, InstantiationPatch> PatchedInstantiations = new Dictionary<MethodBase, InstantiationPatch>();

            public GenericPatchInfo(MethodBase source, MethodBase target) {
                SourceMethod = source;
                TargetMethod = target;
            }
        }

        private readonly object genericPatchesLockObject = new object();
        private readonly List<GenericPatchInfo> genericPatches = new List<GenericPatchInfo>();
        private int lastCleared = 0;
        private readonly Dictionary<MethodBase, int> patchedMethodIndexes = new Dictionary<MethodBase, int>();

        int IGenericDetourPlatform.AddPatch(MethodBase from, MethodBase to)
            => AddMethodPatch(from, to);

        protected int AddMethodPatch(MethodBase from, MethodBase to) {
            // TODO: allow multi-patching, whether here or somewhere else
            lock (genericPatchesLockObject) {
                int index = lastCleared;
                GenericPatchInfo patchInfo = new GenericPatchInfo(from, to);
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
            lock (genericPatchesLockObject) {
                return genericPatches[index];
            }
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
        protected IntPtr FindTargetForThisPtrContext(object thisptr, int index) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);

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

            MethodBase realMethod = MethodBase.GetMethodFromHandle(origHandle, realTypeHandle);

            return GetRealTarget(realMethod, patchInfo.TargetMethod);
        }

        protected IntPtr FindTargetForMethodDescContext(IntPtr methodDesc, int index) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);

            MethodBase origSrc = patchInfo.SourceMethod;

            RuntimeMethodHandle handle = netPlatform.CreateHandleForHandlePointer(methodDesc);

            MethodBase realMethod = MethodBase.GetMethodFromHandle(handle);

            return GetRealTarget(realMethod, patchInfo.TargetMethod);
        }

        protected IntPtr FindTargetForMethodTableContext(IntPtr methodTable, int index) {
            GenericPatchInfo patchInfo = GetPatchInfoFromIndex(index);

            MethodBase origSrc = patchInfo.SourceMethod;

            Type realType = netPlatform.GetTypeFromNativeHandle(methodTable);

            // find the actual method impl using this whacky type handle workaround

            RuntimeMethodHandle origHandle = origSrc.MethodHandle;
            RuntimeTypeHandle realTypeHandle = realType.TypeHandle;

            MethodBase realMethod = MethodBase.GetMethodFromHandle(origHandle, realTypeHandle);

            return GetRealTarget(realMethod, patchInfo.TargetMethod);
        }

        protected IntPtr GetRealTarget(MethodBase realSrc, MethodBase origTarget) {

            throw new NotImplementedException();
        }
        #endregion
    }
}
