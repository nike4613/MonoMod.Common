﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Linq;
using Mono.Cecil.Cil;
using System.Threading;
#if !NET35
using System.Collections.Concurrent;
#endif

namespace MonoMod.RuntimeDetour.Platforms {
#if !MONOMOD_INTERNAL
    public
#endif
    abstract class DetourRuntimeILPlatform : IDetourRuntimePlatform {
        protected abstract RuntimeMethodHandle GetMethodHandle(MethodBase method);

        private readonly GlueThiscallStructRetPtrOrder GlueThiscallStructRetPtr;

        // The following dicts are needed to prevent the GC from collecting DynamicMethods without any visible references.
        // PinnedHandles is also used in certain situations as a fallback when getting a method from a handle may not work normally.
#if NET35
        protected Dictionary<MethodBase, PrivateMethodPin> PinnedMethods = new Dictionary<MethodBase, PrivateMethodPin>();
        protected Dictionary<RuntimeMethodHandle, PrivateMethodPin> PinnedHandles = new Dictionary<RuntimeMethodHandle, PrivateMethodPin>();
#else
        protected ConcurrentDictionary<MethodBase, PrivateMethodPin> PinnedMethods = new ConcurrentDictionary<MethodBase, PrivateMethodPin>();
        protected ConcurrentDictionary<RuntimeMethodHandle, PrivateMethodPin> PinnedHandles = new ConcurrentDictionary<RuntimeMethodHandle, PrivateMethodPin>();
#endif

        public abstract bool OnMethodCompiledWillBeCalled { get; }
        public abstract event OnMethodCompiledEvent OnMethodCompiled;

        public DetourRuntimeILPlatform() {
            // Perform a selftest if this runtime requires special handling for instance methods returning structs.
            // This is documented behavior for coreclr, but affects other runtimes (i.e. mono) as well!
            // Specifically, this should affect all __thiscalls

            // Use reflection to make sure that the selftest isn't optimized away.
            // Delegates are quite reliable for this job.

            MethodInfo selftestGetRefPtr = typeof(DetourRuntimeILPlatform).GetMethod("_SelftestGetRefPtr", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo selftestGetRefPtrHook = typeof(DetourRuntimeILPlatform).GetMethod("_SelftestGetRefPtrHook", BindingFlags.NonPublic | BindingFlags.Static);
            _HookSelftest(selftestGetRefPtr, selftestGetRefPtrHook);

            IntPtr selfPtr = ((Func<IntPtr>) Delegate.CreateDelegate(typeof(Func<IntPtr>), this, selftestGetRefPtr))();

            MethodInfo selftestGetStruct = typeof(DetourRuntimeILPlatform).GetMethod("_SelftestGetStruct", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo selftestGetStructHook = typeof(DetourRuntimeILPlatform).GetMethod("_SelftestGetStructHook", BindingFlags.NonPublic | BindingFlags.Static);
            _HookSelftest(selftestGetStruct, selftestGetStructHook);

            unsafe {
                fixed (GlueThiscallStructRetPtrOrder* orderPtr = &GlueThiscallStructRetPtr) {
                    ((Func<IntPtr, IntPtr, IntPtr, _SelftestStruct>) Delegate.CreateDelegate(typeof(Func<IntPtr, IntPtr, IntPtr, _SelftestStruct>), this, selftestGetStruct))((IntPtr) orderPtr, (IntPtr) orderPtr, selfPtr);
                }
            }
        }

        private void _HookSelftest(MethodInfo from, MethodInfo to) {
            Pin(from);
            Pin(to);
            NativeDetourData detour = DetourHelper.Native.Create(
                GetNativeStart(from),
                GetNativeStart(to),
                null
            );
            DetourHelper.Native.MakeWritable(detour);
            DetourHelper.Native.Apply(detour);
            DetourHelper.Native.MakeExecutable(detour);
            DetourHelper.Native.FlushICache(detour);
            DetourHelper.Native.Free(detour);
            // No need to undo the detour.
        }

#region Selftests

#region Selftest: Get reference ptr

        [MethodImpl(MethodImplOptions.NoInlining)]
        private IntPtr _SelftestGetRefPtr() {
            Console.Error.WriteLine("If you're reading this, the MonoMod.RuntimeDetour selftest failed.");
            throw new Exception("This method should've been detoured!");
        }

        private static unsafe IntPtr _SelftestGetRefPtrHook(IntPtr self) {
            // This is only needed to obtain a raw IntPtr to a reference object.
            return self;
        }

#endregion

#region Selftest: Struct

        // Struct must be 3, 5, 6, 7 or 9+ bytes big.
#pragma warning disable CS0169
        private struct _SelftestStruct {
            private readonly byte A, B, C;
        }
#pragma warning restore CS0169

        [MethodImpl(MethodImplOptions.NoInlining)]
        private _SelftestStruct _SelftestGetStruct(IntPtr x, IntPtr y, IntPtr thisPtr) {
            Console.Error.WriteLine("If you're reading this, the MonoMod.RuntimeDetour selftest failed.");
            throw new Exception("This method should've been detoured!");
        }

        private static unsafe void _SelftestGetStructHook(IntPtr a, IntPtr b, IntPtr c, IntPtr d, IntPtr e) {
            // Normally, a = this, b = x, c = y, d = thisPtr, e = garbage

            // For the general selftest, x must be equal to y.
            // If b != c, b is probably pointing to the return buffer or this.
            if (b == c) {
                // Original order.
                *((GlueThiscallStructRetPtrOrder*) b) = GlueThiscallStructRetPtrOrder.Original;

            } else if (b == e) {
                // For mono in Unity 5.6.X, a = __ret, b = this, c = x, d = y, e = thisPtr
                *((GlueThiscallStructRetPtrOrder*) c) = GlueThiscallStructRetPtrOrder.RetThisArgs;

            } else {
                // For coreclr x64 __thiscall, a = this, b = __ret, c = x, d = y, e = thisPtr
                *((GlueThiscallStructRetPtrOrder*) c) = GlueThiscallStructRetPtrOrder.ThisRetArgs;

            }
        }

#endregion

#endregion

        protected virtual IntPtr GetFunctionPointer(MethodBase method, RuntimeMethodHandle handle)
            => handle.GetFunctionPointer();

        protected virtual void PrepareMethod(MethodBase method, RuntimeMethodHandle handle)
            => RuntimeHelpers.PrepareMethod(handle);

        protected virtual void PrepareMethod(MethodBase method, RuntimeMethodHandle handle, RuntimeTypeHandle[] instantiation)
            => RuntimeHelpers.PrepareMethod(handle, instantiation);

        public virtual void DisableInlining(MethodBase method, RuntimeMethodHandle handle) {
            // no-op. Not supported on all platforms, but throwing an exception doesn't make sense.
        }

        public virtual MethodPinInfo GetPin(MethodBase method) {
#if NET35
            lock (PinnedMethods)
                return PinnedMethods.TryGetValue(method, out PrivateMethodPin pin) ? pin.Pin : default;
#else
            return PinnedMethods.TryGetValue(method, out PrivateMethodPin pin) ? pin.Pin : default;
#endif
        }

        public virtual MethodPinInfo GetPin(RuntimeMethodHandle handle) {
#if NET35
            lock (PinnedMethods)
                return PinnedHandles.TryGetValue(handle, out PrivateMethodPin pin) ? pin.Pin : default;
#else
            return PinnedHandles.TryGetValue(handle, out PrivateMethodPin pin) ? pin.Pin : default;
#endif
        }

        public virtual MethodPinInfo[] GetPins() {
#if NET35
            lock (PinnedMethods)
                return PinnedHandles.Values.Select(p => p.Pin).ToArray();
#else
            return PinnedHandles.Values.ToArray().Select(p => p.Pin).ToArray();
#endif
        }

        public virtual IntPtr GetNativeStart(MethodBase method) {
            bool pinGot;
            PrivateMethodPin pin;
#if NET35
            lock (PinnedMethods)
#endif
            {
                pinGot = PinnedMethods.TryGetValue(method, out pin);
            }
            if (pinGot)
                return GetFunctionPointer(method, pin.Pin.Handle);
            return GetFunctionPointer(method, GetMethodHandle(method));
        }

        public virtual void Pin(MethodBase method) {
#if NET35
            lock (PinnedMethods) {
                if (PinnedMethods.TryGetValue(method, out PrivateMethodPin pin)) {
                    pin.Pin.Count++;
                    return;
                }

                MethodBase m = method;
                pin = new PrivateMethodPin();
                pin.Pin.Count = 1;

#else
            Interlocked.Increment(ref PinnedMethods.GetOrAdd(method, m => {
                PrivateMethodPin pin = new PrivateMethodPin();
#endif

                pin.Pin.Method = m;
                RuntimeMethodHandle handle = pin.Pin.Handle = GetMethodHandle(m);
                PinnedHandles[handle] = pin;

                DisableInlining(method, handle);
                if (method.DeclaringType?.IsGenericType ?? false) {
                    PrepareMethod(method, handle, method.DeclaringType.GetGenericArguments().Select(type => type.TypeHandle).ToArray());
                } else {
                    PrepareMethod(method, handle);
                }

#if !NET35
                return pin;
#endif
            }
#if !NET35
            ).Pin.Count);
#endif
        }

        public virtual void Unpin(MethodBase method) {
#if NET35
            lock (PinnedMethods) {
                if (!PinnedMethods.TryGetValue(method, out PrivateMethodPin pin))
                    return;

                if (pin.Pin.Count <= 1) {
                    PinnedMethods.Remove(method);
                    PinnedHandles.Remove(pin.Pin.Handle);
                    return;
                }
                pin.Pin.Count--;
            }
#else
            if (!PinnedMethods.TryGetValue(method, out PrivateMethodPin pin))
                return;

            if (Interlocked.Decrement(ref pin.Pin.Count) <= 0) {
                PinnedMethods.TryRemove(method, out _);
                PinnedHandles.TryRemove(pin.Pin.Handle, out _);
            }
#endif
        }

        public MethodInfo CreateCopy(MethodBase method) {
            if (method == null || (method.GetMethodImplementationFlags() & (MethodImplAttributes.OPTIL | MethodImplAttributes.Native | MethodImplAttributes.Runtime)) != 0) {
                throw new InvalidOperationException($"Uncopyable method: {method?.ToString() ?? "NULL"}");
            }

            using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(method))
                return dmd.Generate();
        }

        public bool TryCreateCopy(MethodBase method, out MethodInfo dm) {
            if (method == null || (method.GetMethodImplementationFlags() & (MethodImplAttributes.OPTIL | MethodImplAttributes.Native | MethodImplAttributes.Runtime)) != 0) {
                dm = null;
                return false;
            }

            try {
                dm = CreateCopy(method);
                return true;
            } catch {
                dm = null;
                return false;
            }
        }

        public MethodBase GetDetourTarget(MethodBase from, MethodBase to) {
            Type context = to.DeclaringType;

            MethodInfo dm = null;

            if (GlueThiscallStructRetPtr != GlueThiscallStructRetPtrOrder.Original &&
                from is MethodInfo fromInfo && !from.IsStatic &&
                to is MethodInfo toInfo && to.IsStatic &&
                fromInfo.ReturnType == toInfo.ReturnType &&
                fromInfo.ReturnType.IsValueType) {

                int size = fromInfo.ReturnType.GetManagedSize();
                if (size == 3 || size == 5 || size == 6 || size == 7 || size >= 9) {
                    Type thisType = from.GetThisParamType();
                    Type retType = fromInfo.ReturnType.MakeByRefType(); // Refs are shiny pointers.

                    int thisPos = 0;
                    int retPos = 1;

                    if (GlueThiscallStructRetPtr == GlueThiscallStructRetPtrOrder.RetThisArgs) {
                        thisPos = 1;
                        retPos = 0;
                    }

                    List<Type> argTypes = new List<Type> {
                        thisType
                    };
                    argTypes.Insert(retPos, retType);

                    argTypes.AddRange(from.GetParameters().Select(p => p.ParameterType));

                    using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(
                        $"Glue:ThiscallStructRetPtr<{from.GetID(simple: true)},{to.GetID(simple: true)}>",
                        typeof(void), argTypes.ToArray()
                    )) {
                        ILProcessor il = dmd.GetILProcessor();

                        // Load the return buffer address.
                        il.Emit(OpCodes.Ldarg, retPos);

                        // Invoke the target method with all remaining arguments.
                        {
                            il.Emit(OpCodes.Ldarg, thisPos);
                            for (int i = 2; i < argTypes.Count; i++)
                                il.Emit(OpCodes.Ldarg, i);
                            il.Emit(OpCodes.Call, il.Body.Method.Module.ImportReference(to));
                        }

                        // Store the returned object to the return buffer.
                        il.Emit(OpCodes.Stobj, il.Body.Method.Module.ImportReference(fromInfo.ReturnType));
                        il.Emit(OpCodes.Ret);

                        dm = dmd.Generate();
                    }
                }
            }

            return dm ?? to;
        }

        protected class PrivateMethodPin {
            public MethodPinInfo Pin = new MethodPinInfo();
        }

        public struct MethodPinInfo {
            public int Count;
            public MethodBase Method;
            public RuntimeMethodHandle Handle;

            public override string ToString() {
                return $"(MethodPinInfo: {Count}, {Method}, 0x{(long) Handle.Value:X})";
            }
        }

        private enum GlueThiscallStructRetPtrOrder {
            Original,
            ThisRetArgs,
            RetThisArgs
        }
    }
}
