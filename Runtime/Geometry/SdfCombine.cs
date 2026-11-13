using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Geometry
{
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    unsafe struct SdfCombineJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public ushort* acc;
        [NativeDisableUnsafePtrRestriction] public ushort* other;
        public int count;
        public BooleanOp op;
        public float k;

        public void Execute() => SdfCombine.Combine(acc, other, count, op, k);
    }

    /// <summary>
    /// Blends two baked half-float distance fields of equal size with a smooth boolean (smooth minimum of the distances). The result
    /// replaces <c>acc</c>; the arithmetic is the same single-precision sequence as the managed loop it replaced, so the fields are
    /// bit for bit the same.
    /// </summary>
    public static class SdfCombine
    {
        /// <param name="acc">Left operand and result.</param>
        /// <param name="other">Right operand.</param>
        /// <param name="k">Smoothness divided by the field's range (the smooth minimum's blend width in field units).</param>
        public static unsafe void Run(NativeArray<half> acc, NativeArray<half> other, BooleanOp op, float k)
        {
            int n = math.min(acc.Length, other.Length);
            if (n == 0) return;
            new SdfCombineJob
            {
                acc = (ushort*)acc.GetUnsafePtr(),
                other = (ushort*)other.GetUnsafeReadOnlyPtr(),
                count = n,
                op = op,
                k = k
            }.Run();
        }

        internal static unsafe void Combine(ushort* acc, ushort* other, int count, BooleanOp op, float k)
        {
            switch (op)
            {
                case BooleanOp.Subtract:
                    for (int i = 0; i < count; i++)
                        acc[i] = Pack(-SMin(-Unpack(acc[i]), Unpack(other[i]), k));
                    break;
                case BooleanOp.Intersect:
                    for (int i = 0; i < count; i++)
                        acc[i] = Pack(-SMin(-Unpack(acc[i]), -Unpack(other[i]), k));
                    break;
                case BooleanOp.Exclude:
                    for (int i = 0; i < count; i++)
                    {
                        float a = Unpack(acc[i]), b = Unpack(other[i]);
                        acc[i] = Pack(math.max(math.min(a, b), -math.max(a, b)));
                    }

                    break;
                default:
                    for (int i = 0; i < count; i++)
                        acc[i] = Pack(SMin(Unpack(acc[i]), Unpack(other[i]), k));
                    break;
            }
        }

        // The same conversions as `(half)x` and `(float)h` of Unity.Mathematics.
        static float Unpack(ushort h) => math.f16tof32(h);
        static ushort Pack(float r) => (ushort)math.f32tof16(math.clamp(r, -1f, 1f));

        static float SMin(float a, float b, float k)
        {
            float h = math.max(k - math.abs(a - b), 0f) / k;
            return math.min(a, b) - h * h * k * 0.25f;
        }
    }
}
