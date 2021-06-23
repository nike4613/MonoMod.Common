#if !NET35
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoMod.RuntimeDetour.Platforms {
    partial class GenericDetourCoreCLR {


        protected bool MethodRequiresReturnBuffer(MethodBase method) {
            // TODO: implement

            // src/coreclr/src/jit/importer.cpp line 9065
            // src/coreclr/src/jit/compiler.cpp line 913

            // MAX_PASS_SINGLEREG_BYTES is either 8 or 16
            // MAX_RET_MULTIREG_BYTES is either 0, 8, 32, or 64

            if (method is not MethodInfo meth)
                return false;

            Type returnType = meth.ReturnType;

            if (returnType == typeof(void))
                return false;

            if (!returnType.IsValueType)
                return false; // only value types get return buffers

            if (returnType.IsPrimitive)
                return false; // no primitives get return buffers

            int typeSize = returnType.GetManagedSize();

            if (typeSize > 64)
                return true; // types larger than 64 bytes are always return buffered

            StructRetBufferInfo retBufInfo = GetReturnBufferInfoForType(returnType);

            return (retBufInfo & casesWithNoRetBuffer) == 0; // return true if this case has tested to not have a return buffer
        }

        private readonly ConcurrentDictionary<Type, StructRetBufferInfo> retBufferInfoCache = new();
        private StructRetBufferInfo GetReturnBufferInfoForType(Type type) {
            return retBufferInfoCache.GetOrAdd(type, GetReturnBufferInfoForTypeImpl);
        }

        private static StructRetBufferInfo GetReturnBufferInfoForTypeImpl(Type type) {
            // we assume type is a value type

            int typeSize = type.GetManagedSize();

            StructRetBufferInfo result = typeSize switch {
                0 => StructRetBufferInfo.Empty,
                1 => StructRetBufferInfo.Byte,
                2 => StructRetBufferInfo.Short,
                3 => StructRetBufferInfo.OddSize3,
                // can't actually test for 4 because it might be an HFA
                5 => StructRetBufferInfo.OddSize5,
                6 => StructRetBufferInfo.OddSize6,
                7 => StructRetBufferInfo.OddSize7,
                // can't actually test for 8 because it might be an HFA
                9 => StructRetBufferInfo.OddSize9,
                // TODO: maybe assume that these intermediary sizes are equivalent to the next test up?
                12 => StructRetBufferInfo.Int3,
                16 => StructRetBufferInfo.Long2,
                24 => StructRetBufferInfo.Long3,
                32 => StructRetBufferInfo.Long4,
                _ => StructRetBufferInfo.None
            };

            if (result is not StructRetBufferInfo.None) {
                return result;
            }

            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (fields.Length == 0) {
                return StructRetBufferInfo.Empty;
            }

            Type firstFieldType = fields[0].FieldType;
            if (fields.All(f => f.FieldType == firstFieldType)) {
                // all fields are the same type, so it might be an HFA

                // only floating point types may be HFAs
                if (firstFieldType == typeof(float)) {
                    result = fields.Length switch {
                        1 => StructRetBufferInfo.HfaFloat1,
                        2 => StructRetBufferInfo.HfaFloat2,
                        3 => StructRetBufferInfo.HfaFloat3,
                        4 => StructRetBufferInfo.HfaFloat4,
                        _ => StructRetBufferInfo.None
                    };
                } else if (firstFieldType == typeof(double)) {
                    result = fields.Length switch {
                        1 => StructRetBufferInfo.HfaDouble1,
                        2 => StructRetBufferInfo.HfaDouble2,
                        3 => StructRetBufferInfo.HfaDouble3,
                        4 => StructRetBufferInfo.HfaDouble4,
                        _ => StructRetBufferInfo.None
                    };
                }

                if (result is not StructRetBufferInfo.None) {
                    return result;
                }
            }

            // now we can check for size of 4 or 8
            return typeSize switch {
                4 => StructRetBufferInfo.Int1,
                8 => StructRetBufferInfo.Long1,
                _ => StructRetBufferInfo.None
            };
        }

        private readonly StructRetBufferInfo casesWithNoRetBuffer =
            GetFlagFor(TestReturnForStruct<HfaFloat1>(), StructRetBufferInfo.HfaFloat1) |
            GetFlagFor(TestReturnForStruct<HfaFloat2>(), StructRetBufferInfo.HfaFloat2) |
            GetFlagFor(TestReturnForStruct<HfaFloat3>(), StructRetBufferInfo.HfaFloat3) |
            GetFlagFor(TestReturnForStruct<HfaFloat4>(), StructRetBufferInfo.HfaFloat4) |
            GetFlagFor(TestReturnForStruct<HfaDouble1>(), StructRetBufferInfo.HfaDouble1) |
            GetFlagFor(TestReturnForStruct<HfaDouble2>(), StructRetBufferInfo.HfaDouble2) |
            GetFlagFor(TestReturnForStruct<HfaDouble3>(), StructRetBufferInfo.HfaDouble3) |
            GetFlagFor(TestReturnForStruct<HfaDouble4>(), StructRetBufferInfo.HfaDouble4) |
            GetFlagFor(TestReturnForStruct<Int1>(), StructRetBufferInfo.Int1) |
            GetFlagFor(TestReturnForStruct<Long1>(), StructRetBufferInfo.Long1) |
            GetFlagFor(TestReturnForStruct<Int3>(), StructRetBufferInfo.Int3) |
            GetFlagFor(TestReturnForStruct<Long2>(), StructRetBufferInfo.Long2) |
            GetFlagFor(TestReturnForStruct<Long3>(), StructRetBufferInfo.Long3) |
            GetFlagFor(TestReturnForStruct<Long4>(), StructRetBufferInfo.Long4) |
            GetFlagFor(TestReturnForStruct<Byte>(), StructRetBufferInfo.Byte) |
            GetFlagFor(TestReturnForStruct<Short>(), StructRetBufferInfo.Short) |
            GetFlagFor(TestReturnForStruct<OddSize3>(), StructRetBufferInfo.OddSize3) |
            GetFlagFor(TestReturnForStruct<OddSize5>(), StructRetBufferInfo.OddSize5) |
            GetFlagFor(TestReturnForStruct<OddSize6>(), StructRetBufferInfo.OddSize6) |
            GetFlagFor(TestReturnForStruct<OddSize7>(), StructRetBufferInfo.OddSize7) |
            GetFlagFor(TestReturnForStruct<OddSize9>(), StructRetBufferInfo.OddSize9) |
            GetFlagFor(TestReturnForStruct<Empty>(), StructRetBufferInfo.Empty);

        [MethodImpl((MethodImplOptions) 512)]
        private static unsafe bool TestReturnForStruct<T>() where T : struct {
            MethodBase from = typeof(GenericDetourCoreCLR)
                .GetMethod(nameof(StructRetTest), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(typeof(T));
            MethodBase to = typeof(GenericDetourCoreCLR)
                .GetMethod(nameof(RetTestTarget), BindingFlags.NonPublic | BindingFlags.Static);

            DetourHelper.Runtime.Pin(from);
            DetourHelper.Runtime.Pin(to);
            NativeDetourData detour = DetourHelper.Native.Create(
                DetourHelper.Runtime.GetNativeStart(from),
                DetourHelper.Runtime.GetNativeStart(to),
                null
            );
            DetourHelper.Native.MakeWritable(detour);
            DetourHelper.Native.Apply(detour);
            DetourHelper.Native.MakeExecutable(detour);
            DetourHelper.Native.FlushICache(detour);
            DetourHelper.Native.Free(detour);

            bool result;
            try {
                int empty = 0;
                _ = StructRetTest<T>(&result, &empty, &empty, null);
            } finally {
                DetourHelper.Runtime.Unpin(from);
                DetourHelper.Runtime.Unpin(to);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static unsafe T StructRetTest<T>(void* a, void* b, void* c, void* d) where T : struct {
            throw new InvalidOperationException("Call should have been detoured");
        }

        [MethodImpl((MethodImplOptions) 512)]
        private static unsafe void RetTestTarget(byte* a, byte* b, byte* c, byte* d, byte* e) {
            if (b == c) {
                // this means that there isn't a return buffer, so a is our out param
                *(bool*) a = true; // no ret buffer
                return;
            }
            if (c == d) {
                // this means that we have a return buffer at the start, so b is our out param
                *(bool*) b = false; // there is ret buffer
                return;
            }

            // idk what the fuck this case would be
            throw new InvalidOperationException("Unknown ABI");
        }

        [Flags]
        private enum StructRetBufferInfo : uint {
            None            = 0,
            HfaFloat1       = 0x00000001,
            HfaFloat2       = 0x00000002,
            HfaFloat3       = 0x00000004,
            HfaFloat4       = 0x00000008,
            HfaDouble1      = 0x00000010,
            HfaDouble2      = 0x00000020,
            HfaDouble3      = 0x00000040,
            HfaDouble4      = 0x00000080,
            Int1            = 0x00000100,
            Short           = 0x00000200,
            Int3            = 0x00000400,
            OddSize6        = 0x00000800,
            Long1           = 0x00001000,
            Long2           = 0x00002000,
            Long3           = 0x00004000,
            Long4           = 0x00008000,
            Byte            = 0x00010000,
            OddSize3        = 0x00020000,
            OddSize5        = 0x00040000,
            OddSize7        = 0x00080000,
            OddSize9        = 0x00100000,
            Empty           = 0x00200000,
        }

        private static StructRetBufferInfo GetFlagFor(bool value, StructRetBufferInfo setFlag)
            => value ? setFlag : 0;

        #region HFA
        private struct HfaFloat1 {
            public float A;
        }
        private struct HfaFloat2 {
            public float A;
            public float B;
        }
        private struct HfaFloat3 {
            public float A;
            public float B;
            public float C;
        }
        private struct HfaFloat4 {
            public float A;
            public float B;
            public float C;
            public float D;
        }
        private struct HfaDouble1 {
            public double A;
        }
        private struct HfaDouble2 {
            public double A;
            public double B;
        }
        private struct HfaDouble3 {
            public double A;
            public double B;
            public double C;
        }
        private struct HfaDouble4 {
            public double A;
            public double B;
            public double C;
            public double D;
        }
        #endregion

        #region Small sized
        private struct Byte {
            public byte A;
        }
        private struct Short {
            public short A;
        }
        #endregion

        #region Int fields
        private struct Int1 {
            public int A;
        }
        private struct Int3 {
            public int A;
            public int B;
            public int C;
        }
        #endregion

        #region Long fields
        private struct Long1 {
            public long A;
        }
        private struct Long2 {
            public long A;
            public long B;
        }
        private struct Long3 {
            public long A;
            public long B;
            public long C;
        }
        private struct Long4 {
            public long A;
            public long B;
            public long C;
            public long D;
        }
        #endregion

        #region Odd sizes
        private struct OddSize3 {
            public short S;
            public byte A;
        }
        private struct OddSize5 {
            public int I;
            public byte A;
        }
        private struct OddSize6 {
            public int I;
            public short A;
        }
        private struct OddSize7 {
            public int I;
            public short S;
            public byte A;
        }
        private struct OddSize9 {
            public int I;
            public int S;
            public byte A;
        }
        #endregion

        private struct Empty { }

    }
}
#endif