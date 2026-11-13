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
    /// replaces <c>acc</c>; the arithmetic is the same single-precision sequence, with the same half rounding, as the managed loop it replaced.
    /// Checked bit for bit against that loop compiled by IL2CPP (8 million texels, all four operations) and against IEEE single precision. The
    /// Mono editor evaluates float expressions at higher precision and can differ by one half-ulp in rare texels.
    /// </summary>
    internal static class SdfCombine
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

        static float Unpack(ushort h) => math.f16tof32(h);

        // The software conversion of Unity.Mathematics written out: Burst replaces a call to math.f32tof16 by the hardware instruction, which rounds
        // exact ties to even where this rounds them up (the managed (half) cast of the loop this replaced did the same as this). Values are in [-1, 1].
        static ushort Pack(float r)
        {
            float x = math.clamp(r, -1f, 1f);
            uint ux = math.asuint(x);
            uint uux = ux & 0x7FFFF000u;
            uint h = (uint)(math.asuint(math.min(math.asfloat(uux) * 1.92592994e-34f, 260042752.0f)) + 0x1000) >> 13;
            return (ushort)(h | ((ux & ~0x7FFFF000u) >> 16));
        }

        static float SMin(float a, float b, float k)
        {
            float h = math.max(k - math.abs(a - b), 0f) / k;
            return math.min(a, b) - h * h * k * 0.25f;
        }
    }
}
