using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Geometry
{
    [BurstCompile]
    struct FlattenJob : IJob
    {
        [ReadOnly] public NativeArray<byte> verbs;
        [ReadOnly] public NativeArray<float2> points;
        public Affine2 transform;
        public float tolerance;
        public int operand;
        public bool closeAll;
        public Polylines output;

        public void Execute() =>
            PathFlattener.Flatten(verbs, points, transform, tolerance, operand, closeAll, ref output);
    }

    [BurstCompile]
    struct TessellateJob : IJob
    {
        [ReadOnly] public NativeArray<float2> points;
        [ReadOnly] public NativeArray<Contour> contours;
        [ReadOnly] public NativeArray<Operand> operands;
        public NativeList<Trapezoid> output;

        public void Execute() => ScanlineTessellator.Tessellate(points, contours, operands, ref output);
    }

    [BurstCompile]
    struct StrokeJob : IJob
    {
        public Polylines input;
        public StrokeParams parameters;
        [ReadOnly] public NativeArray<float> dash;
        [ReadOnly] public NativeArray<float> profile;
        public Polylines output;

        public void Execute() => Stroker.Stroke(input, parameters, dash, profile, ref output);
    }

    /// <summary>Strokes polylines and tessellates the stroke in one job (avoids an intermediate schedule).</summary>
    [BurstCompile]
    struct StrokeTessellateJob : IJob
    {
        public Polylines input;
        public StrokeParams parameters;
        [ReadOnly] public NativeArray<float> dash;
        [ReadOnly] public NativeArray<float> profile;
        /// <summary>Optional clip area (fill of the source shape) for inside/outside aligned strokes.</summary>
        public Polylines clip;
        public FillRule clipRule;
        /// <summary>0 = none, 1 = keep inside the clip, 2 = keep outside the clip.</summary>
        public int clipMode;
        /// <summary>Use <see cref="input"/> itself as the clip (avoids aliasing the same container twice).</summary>
        public bool clipSelf;
        public NativeList<Trapezoid> output;

        public void Execute()
        {
            var outline = new Polylines(Allocator.Temp, 256);
            Stroker.Stroke(input, parameters, dash, profile, ref outline);
            if (clipMode == 0)
            {
                ScanlineTessellator.Tessellate(outline.points.AsArray(), outline.contours.AsArray(),
                    default, ref output);
            }
            else
            {
                outline.Append(clipSelf ? input : clip, 1);
                var ops = new NativeArray<Operand>(2, Allocator.Temp);
                ops[0] = new Operand(FillRule.NonZero);
                ops[1] = new Operand(clipRule, clipMode == 1 ? BooleanOp.Intersect : BooleanOp.Subtract);
                ScanlineTessellator.Tessellate(outline.points.AsArray(), outline.contours.AsArray(), ops, ref output);
                ops.Dispose();
            }

            outline.Dispose();
        }
    }

    [BurstCompile]
    struct SdfBakeJob : IJob
    {
        [ReadOnly] public NativeArray<Trapezoid> traps;
        public Bounds2 region;
        public int width, height;
        public float range;
        [NativeDisableContainerSafetyRestriction] public NativeArray<half> dst;
        public int stride, x, y;

        public void Execute() => SdfBaker.Bake(traps, region, width, height, range, dst, stride, x, y);
    }

    [BurstCompile]
    struct ContourJob : IJob
    {
        [ReadOnly] public NativeArray<Trapezoid> traps;
        public Polylines output;
        public int operand;

        public void Execute() => ContourBuilder.Build(traps, ref output, operand);
    }

    /// <summary>
    /// Synchronous entry points of the geometry kernel. Every call runs a Burst-compiled job on the calling thread
    /// (on platforms without Burst, such as WebGL, the same code runs as managed IL2CPP code).
    /// </summary>
    public static class VectraGeometry
    {
        public static void Flatten(in NativePath path, in Affine2 transform, float tolerance, int operand, bool closeAll,
            ref Polylines output)
        {
            if (path.IsEmpty) return;
            new FlattenJob
            {
                verbs = path.verbs.AsArray(),
                points = path.points.AsArray(),
                transform = transform,
                tolerance = tolerance,
                operand = operand,
                closeAll = closeAll,
                output = output
            }.Run();
        }

        static NativeArray<T> OrEmpty<T>(NativeArray<T> a, ref bool allocated) where T : struct
        {
            if (a.IsCreated) return a;
            allocated = true;
            return new NativeArray<T>(0, Allocator.TempJob);
        }

        public static void Tessellate(in Polylines input, NativeArray<Operand> operands, ref NativeList<Trapezoid> output)
        {
            if (input.IsEmpty) return;
            bool alloc = false;
            var ops = OrEmpty(operands, ref alloc);
            new TessellateJob
            {
                points = input.points.AsArray(),
                contours = input.contours.AsArray(),
                operands = ops,
                output = output
            }.Run();
            if (alloc) ops.Dispose();
        }

        public static void Tessellate(in Polylines input, FillRule rule, ref NativeList<Trapezoid> output)
        {
            if (input.IsEmpty) return;
            if (rule == FillRule.NonZero)
            {
                Tessellate(input, default(NativeArray<Operand>), ref output);
                return;
            }

            var ops = new NativeArray<Operand>(1, Allocator.TempJob);
            ops[0] = new Operand(rule);
            Tessellate(input, ops, ref output);
            ops.Dispose();
        }

        public static void Stroke(in Polylines input, in StrokeParams parameters, NativeArray<float> dash,
            NativeArray<float> profile, ref Polylines output)
        {
            if (input.IsEmpty) return;
            bool a1 = false, a2 = false;
            var d = OrEmpty(dash, ref a1);
            var pr = OrEmpty(profile, ref a2);
            new StrokeJob
            {
                input = input,
                parameters = parameters,
                dash = d,
                profile = pr,
                output = output
            }.Run();
            if (a1) d.Dispose();
            if (a2) pr.Dispose();
        }

        /// <param name="clipMode">0 = centered, 1 = keep inside <paramref name="clip"/>, 2 = keep outside.</param>
        public static void StrokeAndTessellate(in Polylines input, in StrokeParams parameters, NativeArray<float> dash,
            NativeArray<float> profile, in Polylines clip, FillRule clipRule, int clipMode, ref NativeList<Trapezoid> output,
            bool clipSelf = false)
        {
            if (input.IsEmpty) return;
            bool a1 = false, a2 = false;
            var d = OrEmpty(dash, ref a1);
            var pr = OrEmpty(profile, ref a2);
            bool hasClip = clip.IsCreated && !clipSelf;
            var c = hasClip ? clip : new Polylines(Allocator.TempJob, 0);
            new StrokeTessellateJob
            {
                input = input,
                parameters = parameters,
                dash = d,
                profile = pr,
                clip = c,
                clipRule = clipRule,
                clipMode = hasClip || clipSelf ? clipMode : 0,
                clipSelf = clipSelf,
                output = output
            }.Run();
            if (a1) d.Dispose();
            if (a2) pr.Dispose();
            if (!hasClip) c.Dispose();
        }

        public static void BakeSdf(NativeArray<Trapezoid> traps, Bounds2 region, int width, int height, float range,
            NativeArray<half> dst, int stride, int x, int y)
        {
            new SdfBakeJob
            {
                traps = traps,
                region = region,
                width = width,
                height = height,
                range = range,
                dst = dst,
                stride = stride,
                x = x,
                y = y
            }.Run();
        }

        public static void BuildContours(NativeArray<Trapezoid> traps, ref Polylines output, int operand = 0)
        {
            if (traps.Length == 0) return;
            new ContourJob { traps = traps, output = output, operand = operand }.Run();
        }

        public static Bounds2 Bounds(NativeArray<Trapezoid> traps)
        {
            var b = Bounds2.Empty;
            for (int i = 0; i < traps.Length; i++)
            {
                var t = traps[i];
                b.Encapsulate(new float2(math.min(t.xlb, t.xlt), t.yb));
                b.Encapsulate(new float2(math.max(t.xrb, t.xrt), t.yt));
            }

            return b;
        }

        public static Bounds2 Bounds(in Polylines p)
        {
            var b = Bounds2.Empty;
            for (int i = 0; i < p.points.Length; i++) b.Encapsulate(p.points[i]);
            return b;
        }
    }
}
