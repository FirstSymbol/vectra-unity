using System;
using System.Collections.Generic;
using System.Diagnostics;
using FirstSymbol.Vectra.Geometry;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra.Rendering
{
    public enum VectraQuality
    {
        Low,
        Medium,
        High,
        Ultra
    }

    /// <summary>Statistics of the last build (shown by the inspector).</summary>
    public struct VectraBuildStats
    {
        public int vertices;
        public int triangles;
        public bool analytic;
        public bool usesSdf;
        public int trapezoids;
        public double milliseconds;
    }

    /// <summary>Everything a shape renderer needs to build a mesh.</summary>
    public struct VectraBuildInput
    {
        public VectraShapeGeometry geometry;
        public List<FillLayer> fills;
        public List<StrokeLayer> strokes;
        public List<VectraEffect> effects;
        public ShapeFrame frame;
        public Color tint;
        /// <summary>World meshes are not converted by a canvas; colours are written in the working colour space.</summary>
        public bool worldSpace;
        public VectraQuality quality;
        /// <summary>Always tessellate instead of using analytic distance functions.</summary>
        public bool forcePath;
        /// <summary>Optional extra boolean operands (used by boolean groups and SVG clip paths).</summary>
        public Polylines? clip;
        public FillRule clipRule;
        /// <summary>Bake a distance field even without effects (path shapes used as soft masks).</summary>
        public bool requireDistance;
        /// <summary>How far outside the outline the required distance field must reach (local units).</summary>
        public float distanceReach;
    }

    /// <summary>Distance function of a built shape: what the shader needs to evaluate its outline elsewhere.</summary>
    public struct VectraDistanceShape
    {
        public bool valid;
        public ShapeKind kind;
        public int style;
        public float4 s1;
        public float4 s2;
        /// <summary>Maps the component's local space into the space of the distance function.</summary>
        public Affine2 localToShape;
    }

    /// <summary>
    /// Converts a shape description into a <see cref="VectraMeshBuffer"/>. One instance per component; it caches
    /// tessellation results, stroke geometry, baked distance fields and atlas rows between builds.
    /// </summary>
    public sealed class VectraShapeRenderer : IDisposable
    {
        public readonly VectraMeshBuffer buffer = new VectraMeshBuffer();
        public Texture mainTexture;
        public VectraBuildStats stats;

        NativePath m_Path;
        Polylines m_Poly;
        NativeList<Trapezoid> m_Fill;
        readonly List<NativeList<Trapezoid>> m_StrokeTraps = new List<NativeList<Trapezoid>>();
        readonly List<int> m_StrokeKeys = new List<int>();
        int m_PolyKey, m_FillKey, m_SdfKey;
        bool m_SmoothSdf;
        Polylines? m_ExternalClip;
        FillRule m_ExternalClipRule;
        bool m_Batch;
        List<SdfRegion> m_BatchSdf = new List<SdfRegion>();
        List<SdfRegion> m_PrevBatchSdf = new List<SdfRegion>();
        SdfRegion m_Sdf;
        Bounds2 m_SdfBounds;
        float m_SdfTexel, m_SdfRange;
        PathBuilder m_Builder;
        List<GradientRows> m_Rows = new List<GradientRows>();
        List<GradientRows> m_NextRows = new List<GradientRows>();

        // Per-build scratch.
        ShapeFrame m_Frame;
        bool m_World;
        Color m_Tint;
        ColorAdjust m_Adjust;
        ModifierKind m_Mod;
        bool m_ModFlag;
        float m_S0;
        float m_Margin;
        AnalyticShape m_A;
        Affine2 m_ShapeToLocal;
        float2 m_Origin;

        VectraDistanceShape m_Distance;

        static readonly List<VectraEffect> s_Before = new List<VectraEffect>();
        static readonly List<VectraEffect> s_After = new List<VectraEffect>();
        static readonly List<VectraEffect> s_Overlays = new List<VectraEffect>();
        static readonly List<VectraEffect> s_Top = new List<VectraEffect>();

        public VectraShapeRenderer()
        {
            m_Path = new NativePath(Allocator.Persistent, 32);
            m_Poly = new Polylines(Allocator.Persistent, 128);
            m_Fill = new NativeList<Trapezoid>(64, Allocator.Persistent);
            m_Builder = new PathBuilder(m_Path);
        }

        /// <summary>Flattened outline of the last build (valid when the shape used path geometry).</summary>
        public Polylines Outline => m_Poly;
        public NativeList<Trapezoid> FillTrapezoids => m_Fill;

        /// <summary>Distance function of the last build (invalid for path shapes without a baked distance field).</summary>
        public VectraDistanceShape Distance => m_Distance;

        /// <summary>Forgets cached geometry (call after the atlas was reset or the shape changed type).</summary>
        public void Invalidate()
        {
            m_PolyKey = m_FillKey = m_SdfKey = 0;
            for (int i = 0; i < m_StrokeKeys.Count; i++) m_StrokeKeys[i] = 0;
        }

        public void Build(in VectraBuildInput input)
        {
            long startTicks = Stopwatch.GetTimestamp();
            if (!m_Batch)
            {
                buffer.Clear();
                mainTexture = null;
                stats = default;
                m_NextRows.Clear();
            }
            else
            {
                // Every batch item gets its own distance field region.
                m_Sdf = default;
                m_SdfKey = 0;
            }

            m_Distance = default;
            m_Frame = input.frame;
            m_Frame.pixelSize = math.max(m_Frame.pixelSize, 1e-5f);
            m_World = input.worldSpace;
            m_Tint = input.tint;
            m_Margin = m_Frame.pixelSize * 1.5f;
            m_Origin = m_Frame.Center;
            m_ExternalClip = input.clip.HasValue && input.clip.Value.IsCreated && !input.clip.Value.IsEmpty ? input.clip : null;
            m_ExternalClipRule = input.clipRule;
            var geometry = input.geometry;
            if (geometry == null || m_Frame.rect.width < 0f || m_Frame.rect.height < 0f)
            {
                if (!m_Batch) FinishRows();
                return;
            }

            // Effects that modify everything else.
            m_Adjust = ColorAdjust.Identity;
            FillAmountEffect fillAmount = null;
            DissolveEffect dissolve = null;
            PixelateEffect pixelate = null;
            float layerBlur = 0f;
            float sdfReach = input.requireDistance ? math.max(input.distanceReach, 0f) : 0f;
            bool needSdf = input.requireDistance;
            s_Before.Clear();
            s_After.Clear();
            s_Overlays.Clear();
            s_Top.Clear();
            if (input.effects != null)
            {
                foreach (var e in input.effects)
                {
                    if (e == null || !e.enabled) continue;
                    switch (e)
                    {
                        case ColorAdjustEffect ca:
                            m_Adjust = new ColorAdjust { hue = ca.hueShift, saturation = ca.saturation, brightness = ca.brightness, contrast = ca.contrast };
                            break;
                        case FillAmountEffect fa:
                            fillAmount = fa;
                            break;
                        case DissolveEffect d:
                            dissolve = d;
                            break;
                        case LayerBlurEffect lb:
                            layerBlur = math.max(layerBlur, lb.blur * 0.5f);
                            break;
                        case DropShadowEffect ds:
                            s_Before.Add(e);
                            sdfReach = math.max(sdfReach, math.length((float2)ds.offset) + math.max(ds.spread, 0f) + ds.blur * 1.5f);
                            needSdf = true;
                            break;
                        case OuterGlowEffect og:
                            s_Before.Add(e);
                            sdfReach = math.max(sdfReach, og.size + math.max(og.spread, 0f));
                            needSdf = true;
                            break;
                        case InnerShadowEffect ins:
                            s_After.Add(e);
                            sdfReach = math.max(sdfReach, math.length((float2)ins.offset) + ins.choke + ins.blur * 1.5f);
                            needSdf = true;
                            break;
                        case InnerGlowEffect ig:
                            s_After.Add(e);
                            sdfReach = math.max(sdfReach, ig.size + ig.choke);
                            needSdf = true;
                            break;
                        case BevelEffect bv:
                            s_After.Add(e);
                            sdfReach = math.max(sdfReach, bv.size * 1.2f);
                            needSdf = true;
                            break;
                        case GrainEffect:
                            s_After.Add(e);
                            needSdf = true;
                            break;
                        case PixelateEffect px:
                            pixelate = px;
                            needSdf = true;
                            break;
                        case OutlineEffect ol:
                            s_Top.Add(e);
                            sdfReach = math.max(sdfReach, math.max(ol.offset, 0f) + ol.width);
                            needSdf = true;
                            break;
                        case ColorOverlayEffect:
                        case GradientOverlayEffect:
                            s_Overlays.Add(e);
                            break;
                    }
                }
            }

            if (layerBlur > 0f)
            {
                sdfReach = math.max(sdfReach, layerBlur * 3f);
                needSdf = true;
            }

            bool analytic = !input.forcePath && !input.clip.HasValue && geometry.TryGetAnalytic(m_Frame, out m_A);
            stats.analytic = analytic;
            if (analytic)
            {
                m_ShapeToLocal = m_A.localToShape.Inverse();
                SetupAnalyticModifier(fillAmount, pixelate, dissolve);
            }
            else
            {
                m_ShapeToLocal = Affine2.Translate(m_Origin);
                SetupPathModifier(fillAmount, pixelate, dissolve);
            }

            // Pixelated path fills are drawn from the distance field.
            bool distanceFills = layerBlur > 0f || m_Mod == ModifierKind.Pixelate;

            // Which geometry is needed.
            bool anyFill = s_Overlays.Count > 0;
            if (input.fills != null)
                foreach (var f in input.fills)
                    if (f != null && f.enabled && f.paint != null && f.paint.IsVisible) anyFill = true;

            bool needPathStroke = false;
            if (input.strokes != null)
                foreach (var s in input.strokes)
                    if (StrokeVisible(s) && !CanStrokeAnalytically(analytic, s)) needPathStroke = true;

            bool smooth = !analytic && geometry is BooleanShape bs && bs.smoothness > 0f;
            m_SmoothSdf = smooth;
            bool needPoly = (!analytic || needPathStroke) && !smooth;
            bool needFillTraps = !analytic && (anyFill || needSdf) && !smooth;
            needSdf &= !analytic;

            float tol = m_Frame.pixelSize * QualityTolerance(input.quality);
            if (needPoly) EnsurePolylines(geometry, tol, input);
            if (needFillTraps) EnsureFillTraps(geometry, input, fillAmount);
            if (smooth) EnsureSmoothSdf((BooleanShape)geometry, sdfReach + m_Margin * 2f, tol, input.quality);
            else if (needSdf) EnsureSdf(sdfReach + m_Margin * 2f, input.quality);
            stats.usesSdf = needSdf && m_Sdf.IsValid;

            // Behind the fill: shadows, glows, strokes marked "behind".
            foreach (var e in s_Before) EmitEffect(e, analytic);
            if (input.strokes != null)
                for (int i = 0; i < input.strokes.Count; i++)
                    if (input.strokes[i] != null && input.strokes[i].behindFill) EmitStroke(i, input.strokes[i], analytic, geometry, tol);

            // Fills, then colour and gradient overlays.
            if (input.fills != null)
            {
                foreach (var f in input.fills)
                {
                    if (f == null || !f.enabled || f.paint == null || !f.paint.IsVisible) continue;
                    EmitFill(f.paint, analytic, layerBlur, distanceFills);
                }
            }

            foreach (var e in s_Overlays)
            {
                var paint = e is ColorOverlayEffect co ? co.ToPaint() : ((GradientOverlayEffect)e).ToPaint();
                if (paint.IsVisible) EmitFill(paint, analytic, layerBlur, distanceFills);
            }

            // Inner effects on top of the fills.
            foreach (var e in s_After) EmitEffect(e, analytic);

            // Strokes.
            if (input.strokes != null)
                for (int i = 0; i < input.strokes.Count; i++)
                    if (input.strokes[i] != null && !input.strokes[i].behindFill) EmitStroke(i, input.strokes[i], analytic, geometry, tol);

            // Effects drawn over everything.
            foreach (var e in s_Top) EmitEffect(e, analytic);

            // Distance description of the outline (used by soft masks).
            if (analytic)
                m_Distance = new VectraDistanceShape
                {
                    valid = true, kind = m_A.kind, style = m_A.style, s1 = m_A.s1, s2 = new float4(m_A.s2, 0f, 0f),
                    localToShape = m_A.localToShape
                };
            else if (m_Sdf.IsValid && !m_Batch)
                m_Distance = new VectraDistanceShape
                {
                    valid = true, kind = ShapeKind.SdfTexture, s1 = SdfParams1(), s2 = SdfParams2(float2.zero),
                    localToShape = Affine2.Translate(-m_Origin)
                };

            if (m_Batch)
            {
                if (m_Sdf.id > 0) m_BatchSdf.Add(m_Sdf);
                m_Sdf = default;
            }
            else FinishRows();
            stats.vertices = buffer.VertexCount;
            stats.triangles = buffer.TriangleCount;
            if (!m_Batch) stats.trapezoids = m_Fill.IsCreated ? m_Fill.Length : 0;
            stats.milliseconds += (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Starts accumulating several builds (e.g. SVG elements) into one buffer.</summary>
        public void BeginBatch()
        {
            buffer.Clear();
            mainTexture = null;
            stats = default;
            m_NextRows.Clear();
            (m_BatchSdf, m_PrevBatchSdf) = (m_PrevBatchSdf, m_BatchSdf);
            m_BatchSdf.Clear();
            if (m_Sdf.id > 0) VectraSdfAtlas.Free(ref m_Sdf);
            m_Batch = true;
        }

        /// <summary>Ends a batch: releases the atlas resources of the previous batch.</summary>
        public void EndBatch()
        {
            m_Batch = false;
            for (int i = 0; i < m_PrevBatchSdf.Count; i++)
            {
                var r = m_PrevBatchSdf[i];
                VectraSdfAtlas.Free(ref r);
            }

            m_PrevBatchSdf.Clear();
            FinishRows();
            stats.vertices = buffer.VertexCount;
            stats.triangles = buffer.TriangleCount;
        }

        static float QualityTolerance(VectraQuality q)
        {
            switch (q)
            {
                case VectraQuality.Low: return 0.6f;
                case VectraQuality.Medium: return 0.3f;
                case VectraQuality.Ultra: return 0.05f;
                default: return 0.15f;
            }
        }

        static int QualityMaxSdf(VectraQuality q)
        {
            switch (q)
            {
                case VectraQuality.Low: return 96;
                case VectraQuality.Medium: return 160;
                case VectraQuality.Ultra: return 512;
                default: return 256;
            }
        }

        static bool StrokeVisible(StrokeLayer s) =>
            s != null && s.enabled && s.width > 0f && s.paint != null && s.paint.IsVisible && s.trimStart != s.trimEnd;

        bool CanStrokeAnalytically(bool analytic, StrokeLayer s)
        {
            if (!analytic || s.NeedsGeometry) return false;
            if (m_A.sharpCorners && s.join != LineJoin.Round && s.alignment != StrokeAlignment.Inside) return false;
            return true;
        }

        // ------------------------------------------------------------------------------------------------------
        // Geometry caches
        // ------------------------------------------------------------------------------------------------------

        void EnsurePolylines(VectraShapeGeometry geometry, float tol, in VectraBuildInput input)
        {
            var h = new HashBuilder(geometry.ComputeHash());
            h.Add(m_Frame.rect.x);
            h.Add(m_Frame.rect.y);
            h.Add(m_Frame.rect.width);
            h.Add(m_Frame.rect.height);
            h.Add((int)math.round(math.log2(math.max(tol, 1e-6f)) * 4f));
            if (input.clip.HasValue) h.Add(input.clip.Value.points.Length * 31 + (int)input.clipRule);
            int key = h.Value | 1;
            if (key == m_PolyKey) return;
            m_PolyKey = key;
            m_FillKey = 0;
            m_SdfKey = 0;
            for (int i = 0; i < m_StrokeKeys.Count; i++) m_StrokeKeys[i] = 0;

            m_Path.Clear();
            m_Poly.Clear();
            m_Builder.Reset(m_Path, Affine2.Identity);
            geometry.BuildPath(m_Frame, m_Builder);
            VectraGeometry.Flatten(m_Path, Affine2.Identity, tol, 0, false, ref m_Poly);
        }

        void EnsureFillTraps(VectraShapeGeometry geometry, in VectraBuildInput input, FillAmountEffect fillAmount)
        {
            var h = new HashBuilder(m_PolyKey);
            if (fillAmount != null)
            {
                h.Add((int)fillAmount.mode);
                h.Add(fillAmount.amount);
                h.Add(fillAmount.reverse);
                h.Add(fillAmount.startAngle);
            }

            int key = h.Value | 1;
            if (key == m_FillKey) return;
            m_FillKey = key;
            m_SdfKey = 0;
            m_Fill.Clear();

            bool hasClip = input.clip.HasValue && input.clip.Value.IsCreated && !input.clip.Value.IsEmpty;
            bool hasAmount = fillAmount != null && fillAmount.amount < 1f;
            if (!hasClip && !hasAmount)
            {
                VectraGeometry.Tessellate(m_Poly, geometry.FillRule, ref m_Fill);
                return;
            }

            // Shape intersected with the fill-amount region and/or the external clip.
            var all = new Polylines(Allocator.TempJob, m_Poly.points.Length + 64);
            all.Append(m_Poly, 0);
            int opCount = 1;
            var ops = new NativeArray<Operand>(3, Allocator.TempJob);
            ops[0] = new Operand(geometry.FillRule);
            if (hasClip)
            {
                all.Append(input.clip.Value, opCount);
                ops[opCount++] = new Operand(input.clipRule, BooleanOp.Intersect);
            }

            if (hasAmount)
            {
                AppendFillAmountRegion(ref all, fillAmount, opCount);
                ops[opCount++] = new Operand(FillRule.NonZero, BooleanOp.Intersect);
            }

            var used = new NativeArray<Operand>(opCount, Allocator.TempJob);
            NativeArray<Operand>.Copy(ops, used, opCount);
            VectraGeometry.Tessellate(all, used, ref m_Fill);
            used.Dispose();
            ops.Dispose();
            all.Dispose();
        }

        void AppendFillAmountRegion(ref Polylines all, FillAmountEffect fa, int operand)
        {
            var b = VectraGeometry.Bounds(m_Poly);
            if (!b.IsValid) return;
            b = b.Expanded(1f);
            float t = math.saturate(fa.amount);
            all.BeginContour(operand);
            switch (fa.mode)
            {
                case FillAmountMode.Horizontal:
                {
                    float x0 = fa.reverse ? math.lerp(b.max.x, b.min.x, t) : b.min.x;
                    float x1 = fa.reverse ? b.max.x : math.lerp(b.min.x, b.max.x, t);
                    all.AddPoint(new float2(x0, b.min.y));
                    all.AddPoint(new float2(x1, b.min.y));
                    all.AddPoint(new float2(x1, b.max.y));
                    all.AddPoint(new float2(x0, b.max.y));
                    break;
                }
                case FillAmountMode.Vertical:
                {
                    float y0 = fa.reverse ? math.lerp(b.max.y, b.min.y, t) : b.min.y;
                    float y1 = fa.reverse ? b.max.y : math.lerp(b.min.y, b.max.y, t);
                    all.AddPoint(new float2(b.min.x, y0));
                    all.AddPoint(new float2(b.max.x, y0));
                    all.AddPoint(new float2(b.max.x, y1));
                    all.AddPoint(new float2(b.min.x, y1));
                    break;
                }
                default:
                {
                    float r = math.length(b.Size) * 2f;
                    float start = math.radians(fa.startAngle);
                    float sweep = t * 2f * math.PI * (fa.reverse ? -1f : 1f);
                    int steps = math.max(2, (int)math.ceil(math.abs(sweep) / 0.1f));
                    all.AddPoint(m_Origin);
                    for (int i = 0; i <= steps; i++)
                    {
                        float a = start + sweep * i / steps;
                        all.AddPoint(m_Origin + new float2(math.cos(a), math.sin(a)) * r);
                    }

                    break;
                }
            }

            all.EndContour(true);
        }

        void EnsureSdf(float reach, VectraQuality quality)
        {
            var fb = VectraGeometry.Bounds(m_Fill.AsArray());
            if (!fb.IsValid)
            {
                VectraSdfAtlas.Free(ref m_Sdf);
                return;
            }

            float range = math.max(reach, m_Frame.pixelSize * 2f);
            var region = fb.Expanded(range);
            float maxDim = math.cmax(region.Size);
            float texel = math.max(m_Frame.pixelSize, maxDim / QualityMaxSdf(quality));
            int w = math.max(2, (int)math.ceil(region.Size.x / texel));
            int hgt = math.max(2, (int)math.ceil(region.Size.y / texel));

            var h = new HashBuilder(m_FillKey);
            h.Add(range);
            h.Add(texel);
            int key = h.Value | 1;
            if (key == m_SdfKey && m_Sdf.IsValid) return;
            m_SdfKey = key;

            if (!m_Sdf.IsValid || m_Sdf.rect.width != w || m_Sdf.rect.height != hgt)
            {
                VectraSdfAtlas.Free(ref m_Sdf);
                if (!VectraSdfAtlas.Allocate(w, hgt, out m_Sdf)) return;
            }

            // Texel centres are inset by half a texel; the region spans exactly w x h texels.
            region.max = region.min + new float2(w, hgt) * texel;
            m_SdfBounds = region;
            m_SdfTexel = texel;
            m_SdfRange = range;
            VectraGeometry.BakeSdf(m_Fill.AsArray(), region, w, hgt, range, VectraSdfAtlas.Data, VectraSdfAtlas.Size,
                m_Sdf.rect.x, m_Sdf.rect.y);
            VectraSdfAtlas.MarkDirty();
        }

        /// <summary>Smooth booleans: bakes every operand separately and blends the distance fields.</summary>
        void EnsureSmoothSdf(BooleanShape shape, float reach, float tol, VectraQuality quality)
        {
            var h = new HashBuilder(shape.ComputeHash());
            h.Add(m_Frame.rect.x);
            h.Add(m_Frame.rect.y);
            h.Add(m_Frame.rect.width);
            h.Add(m_Frame.rect.height);
            h.Add(reach);
            h.Add((int)math.round(math.log2(math.max(tol, 1e-6f)) * 4f));
            int key = h.Value | 1;
            if (key == m_SdfKey && m_Sdf.IsValid) return;
            m_SdfKey = key;
            m_PolyKey = m_FillKey = 0;

            var poly = new Polylines(Allocator.TempJob, 256);
            var ops = new List<Operand>();
            shape.FlattenOperands(m_Frame, tol, ref poly, ops);
            int n = ops.Count;
            if (n == 0)
            {
                poly.Dispose();
                VectraSdfAtlas.Free(ref m_Sdf);
                return;
            }

            // Tessellate each operand on its own.
            var traps = new List<NativeList<Trapezoid>>(n);
            var bounds = Bounds2.Empty;
            var single = new Polylines(Allocator.TempJob, 128);
            for (int k = 0; k < n; k++)
            {
                single.Clear();
                for (int ci = 0; ci < poly.contours.Length; ci++)
                {
                    var c = poly.contours[ci];
                    if (c.operand != k) continue;
                    single.BeginContour(0);
                    for (int i = 0; i < c.count; i++) single.AddPoint(poly.points[c.start + i]);
                    single.EndContour(true);
                }

                var t = new NativeList<Trapezoid>(64, Allocator.TempJob);
                VectraGeometry.Tessellate(single, ops[k].rule, ref t);
                bounds.Encapsulate(VectraGeometry.Bounds(t.AsArray()));
                traps.Add(t);
            }

            single.Dispose();
            poly.Dispose();
            if (!bounds.IsValid)
            {
                foreach (var t in traps) t.Dispose();
                VectraSdfAtlas.Free(ref m_Sdf);
                return;
            }

            float k2 = math.max(shape.smoothness, 1e-4f);
            float range = math.max(reach, k2 * 2f) + m_Frame.pixelSize * 2f;
            var region = bounds.Expanded(range);
            float texel = math.max(m_Frame.pixelSize, math.cmax(region.Size) / QualityMaxSdf(quality));
            int w = math.max(2, (int)math.ceil(region.Size.x / texel));
            int hh = math.max(2, (int)math.ceil(region.Size.y / texel));
            region.max = region.min + new float2(w, hh) * texel;
            if (!m_Sdf.IsValid || m_Sdf.rect.width != w || m_Sdf.rect.height != hh)
            {
                VectraSdfAtlas.Free(ref m_Sdf);
                if (!VectraSdfAtlas.Allocate(w, hh, out m_Sdf))
                {
                    foreach (var t in traps) t.Dispose();
                    return;
                }
            }

            var acc = new NativeArray<half>(w * hh, Allocator.TempJob);
            var tmp = new NativeArray<half>(w * hh, Allocator.TempJob);
            float kn = k2 / range;
            for (int k = 0; k < n; k++)
            {
                VectraGeometry.BakeSdf(traps[k].AsArray(), region, w, hh, range, k == 0 ? acc : tmp, w, 0, 0);
                if (k == 0) continue;
                var op = ops[k].op;
                for (int i = 0; i < acc.Length; i++)
                {
                    float a = acc[i], b = tmp[i];
                    float r;
                    switch (op)
                    {
                        case BooleanOp.Subtract: r = -SMin(-a, b, kn); break;
                        case BooleanOp.Intersect: r = -SMin(-a, -b, kn); break;
                        case BooleanOp.Exclude: r = math.max(math.min(a, b), -math.max(a, b)); break;
                        default: r = SMin(a, b, kn); break;
                    }

                    acc[i] = (half)math.clamp(r, -1f, 1f);
                }
            }

            var dst = VectraSdfAtlas.Data;
            int stride = VectraSdfAtlas.Size;
            for (int y = 0; y < hh; y++)
                NativeArray<half>.Copy(acc, y * w, dst, (m_Sdf.rect.y + y) * stride + m_Sdf.rect.x, w);
            VectraSdfAtlas.MarkDirty();
            acc.Dispose();
            tmp.Dispose();
            foreach (var t in traps) t.Dispose();

            m_SdfBounds = region;
            m_SdfTexel = texel;
            m_SdfRange = range;
        }

        static float SMin(float a, float b, float k)
        {
            float h = math.max(k - math.abs(a - b), 0f) / k;
            return math.min(a, b) - h * h * k * 0.25f;
        }
        NativeList<Trapezoid> EnsureStrokeTraps(int index, StrokeLayer s, VectraShapeGeometry geometry, float tol)
        {
            while (m_StrokeTraps.Count <= index)
            {
                m_StrokeTraps.Add(new NativeList<Trapezoid>(64, Allocator.Persistent));
                m_StrokeKeys.Add(0);
            }

            var h = new HashBuilder(m_PolyKey);
            h.Add(s.ComputeGeometryHash());
            int key = h.Value | 1;
            var traps = m_StrokeTraps[index];
            if (m_StrokeKeys[index] == key) return traps;
            m_StrokeKeys[index] = key;
            traps.Clear();

            var p = StrokeParams.Default(s.alignment == StrokeAlignment.Center ? s.width : s.width * 2f);
            p.cap = s.cap;
            p.join = s.join;
            p.miterLimit = s.miterLimit;
            p.tolerance = tol;
            p.trimStart = s.trimStart;
            p.trimEnd = s.trimEnd;
            p.trimOffset = s.trimOffset;
            p.dashOffset = s.dashOffset;
            p.dashRelative = s.dashRelative;

            var dash = new NativeArray<float>(s.dashed && s.dashPattern != null ? s.dashPattern.Count : 0, Allocator.TempJob);
            for (int i = 0; i < dash.Length; i++)
                dash[i] = s.dashRelative ? s.dashPattern[i] : s.dashPattern[i];
            var profile = new NativeArray<float>(s.useWidthProfile && s.widthProfile != null ? 33 : 0, Allocator.TempJob);
            for (int i = 0; i < profile.Length; i++) profile[i] = math.max(s.widthProfile.Evaluate(i / 32f), 0f);

            int clipMode = s.alignment == StrokeAlignment.Inside ? 1 : s.alignment == StrokeAlignment.Outside ? 2 : 0;
            if (clipMode != 0 && !geometry.IsClosed) clipMode = 0;
            if (clipMode == 0 && m_ExternalClip.HasValue)
                VectraGeometry.StrokeAndTessellate(m_Poly, p, dash, profile, m_ExternalClip.Value, m_ExternalClipRule, 1, ref traps);
            else
                VectraGeometry.StrokeAndTessellate(m_Poly, p, dash, profile, default, geometry.FillRule, clipMode, ref traps,
                    clipMode != 0);
            dash.Dispose();
            profile.Dispose();
            m_StrokeTraps[index] = traps;
            return traps;
        }

        // ------------------------------------------------------------------------------------------------------
        // Modifiers
        // ------------------------------------------------------------------------------------------------------

        void SetupAnalyticModifier(FillAmountEffect fa, PixelateEffect pixelate, DissolveEffect dissolve)
        {
            m_Mod = ModifierKind.None;
            m_ModFlag = false;
            m_S0 = 0f;
            if (fa != null && fa.amount < 1f)
            {
                float t = math.saturate(fa.amount);
                float2 he = m_A.halfExtents;
                switch (fa.mode)
                {
                    case FillAmountMode.Horizontal:
                        m_Mod = ModifierKind.CutX;
                        m_ModFlag = fa.reverse;
                        m_S0 = fa.reverse ? math.lerp(he.x, -he.x, t) : math.lerp(-he.x, he.x, t);
                        break;
                    case FillAmountMode.Vertical:
                        m_Mod = ModifierKind.CutY;
                        m_ModFlag = fa.reverse;
                        m_S0 = fa.reverse ? math.lerp(he.y, -he.y, t) : math.lerp(-he.y, he.y, t);
                        break;
                    default:
                        m_Mod = ModifierKind.Radial;
                        m_ModFlag = fa.reverse;
                        float start = fa.startAngle / 360f;
                        start -= math.floor(start);
                        m_S0 = VectraEncoding.Pack12(start, t);
                        break;
                }

                return;
            }

            if (pixelate != null)
            {
                // Shape space may be scaled relative to local space.
                float scale = math.length(m_A.localToShape.ApplyVector(new float2(1f, 0f)));
                SetupPixelate(pixelate, scale);
                return;
            }

            SetupDissolve(dissolve);
        }

        void SetupPixelate(PixelateEffect px, float scale)
        {
            m_Mod = ModifierKind.Pixelate;
            m_S0 = math.max(px.cellSize, 0.5f) * math.max(scale, 1e-6f);
        }

        void SetupPathModifier(FillAmountEffect fa, PixelateEffect pixelate, DissolveEffect dissolve)
        {
            m_Mod = ModifierKind.None;
            m_ModFlag = false;
            m_S0 = 0f;
            // The fill amount is applied geometrically to path fills; distance-based layers use the shader cut, which
            // is expressed relative to the shape centre (the path origin).
            if (fa != null && fa.amount < 1f && fa.mode == FillAmountMode.Radial)
            {
                m_Mod = ModifierKind.Radial;
                m_ModFlag = fa.reverse;
                float start = fa.startAngle / 360f;
                start -= math.floor(start);
                m_S0 = VectraEncoding.Pack12(start, math.saturate(fa.amount));
                return;
            }

            if (pixelate != null)
            {
                SetupPixelate(pixelate, 1f);
                return;
            }

            SetupDissolve(dissolve);
        }

        void SetupDissolve(DissolveEffect d)
        {
            if (d == null || d.amount <= 0f) return;
            m_Mod = ModifierKind.Dissolve;
            float scaleCode = (math.log2(math.max(d.scale, 1e-4f)) + 4f) / 16f;
            m_S0 = VectraEncoding.Pack8(d.amount, d.softness, scaleCode);
        }

        // ------------------------------------------------------------------------------------------------------
        // Paint resolution
        // ------------------------------------------------------------------------------------------------------

        struct ResolvedPaint
        {
            public PaintKind kind;
            public int spread;
            public Affine2 toPaint;
            public float v;
            public float param;
            public Color32 color;
            public bool additive;
        }

        ResolvedPaint Resolve(VectraPaint paint)
        {
            var r = new ResolvedPaint { toPaint = Affine2.Identity, param = 1f, additive = paint.blend == LayerBlend.Additive };
            float2 mn = m_Frame.Min;
            float2 size = math.max(m_Frame.Size, 1e-6f);
            var normalized = Affine2.Scale(1f / size) * Affine2.Translate(-mn);
            Color tint = m_Tint;
            tint.a *= paint.opacity;

            switch (paint.type)
            {
                case PaintType.Solid:
                {
                    r.kind = PaintKind.Solid;
                    var c = m_Adjust.Apply(paint.ResolvedColor);
                    float intensity = math.max(math.max(c.r, c.g), math.max(c.b, 1f));
                    if (intensity > 1f)
                    {
                        c.r /= intensity;
                        c.g /= intensity;
                        c.b /= intensity;
                    }

                    r.param = QualitySettings.activeColorSpace == ColorSpace.Linear ? math.pow(intensity, 2.2f) : intensity;
                    r.color = ToVertex(c * tint);
                    return r;
                }
                case PaintType.Texture:
                {
                    r.kind = PaintKind.Texture;
                    mainTexture = paint.texture;
                    r.color = ToVertex(m_Adjust.Apply(paint.ResolvedColor) * tint);
                    r.toPaint = TextureMapping(paint, mn, size);
                    r.spread = paint.textureFit == TextureFit.Tile ? 1 : 0;
                    return r;
                }
                case PaintType.Freeform:
                {
                    r.kind = PaintKind.Freeform;
                    var rows = VectraGradientAtlas.Acquire(paint, m_Adjust);
                    m_NextRows.Add(rows);
                    r.v = VectraGradientAtlas.RowV(rows);
                    r.param = VectraGradientAtlas.BlockSpanV(rows);
                    r.toPaint = normalized;
                    r.color = ToVertex(tint);
                    return r;
                }
            }

            {
                var rows = VectraGradientAtlas.Acquire(paint, m_Adjust);
                m_NextRows.Add(rows);
                r.v = VectraGradientAtlas.RowV(rows);
                r.color = ToVertex(tint);
                r.spread = (int)paint.spread;
            }

            if (paint.useCustomMapping && paint.IsGradient)
            {
                r.toPaint = paint.customMapping;
                r.kind = paint.type == PaintType.LinearGradient ? PaintKind.Linear :
                    paint.type == PaintType.AngularGradient ? PaintKind.Angular :
                    paint.type == PaintType.DiamondGradient ? PaintKind.Diamond : PaintKind.Radial;
                if (r.kind == PaintKind.Radial && paint.focalPoint.sqrMagnitude > 1e-6f)
                {
                    r.kind = PaintKind.Focal;
                    var fc = (float2)paint.focalPoint;
                    if (math.length(fc) > 0.98f) fc = math.normalize(fc) * 0.98f;
                    r.param = VectraEncoding.Pack12(fc.x * 0.5f + 0.5f, fc.y * 0.5f + 0.5f);
                }

                return r;
            }

            float2 S = mn + size * (float2)paint.gradientStart;
            float2 E = mn + size * (float2)paint.gradientEnd;
            switch (paint.type)
            {
                case PaintType.LinearGradient:
                {
                    r.kind = PaintKind.Linear;
                    float2 d = E - S;
                    float l2 = math.max(math.dot(d, d), 1e-12f);
                    r.toPaint = new Affine2(d.x / l2, -d.y / l2, d.y / l2, d.x / l2,
                        -(d.x * S.x + d.y * S.y) / l2, (d.y * S.x - d.x * S.y) / l2);
                    break;
                }
                case PaintType.RadialGradient:
                case PaintType.AngularGradient:
                case PaintType.DiamondGradient:
                {
                    float2 a = E - S;
                    if (math.lengthsq(a) < 1e-12f) a = new float2(1e-3f, 0f);
                    float2 b = new float2(-a.y, a.x) * paint.gradientAspect;
                    r.toPaint = new Affine2(a.x, a.y, b.x, b.y, S.x, S.y).Inverse();
                    if (paint.type == PaintType.AngularGradient) r.kind = PaintKind.Angular;
                    else if (paint.type == PaintType.DiamondGradient) r.kind = PaintKind.Diamond;
                    else if (paint.focalPoint.sqrMagnitude > 1e-6f)
                    {
                        r.kind = PaintKind.Focal;
                        var f = math.clamp((float2)paint.focalPoint, -0.98f, 0.98f);
                        if (math.length(f) > 0.98f) f = math.normalize(f) * 0.98f;
                        r.param = VectraEncoding.Pack12(f.x * 0.5f + 0.5f, f.y * 0.5f + 0.5f);
                    }
                    else r.kind = PaintKind.Radial;

                    break;
                }
                default:
                {
                    r.kind = paint.type switch
                    {
                        PaintType.Stripes => PaintKind.Stripes,
                        PaintType.Checker => PaintKind.Checker,
                        PaintType.Dots => PaintKind.Dots,
                        PaintType.Grid => PaintKind.Grid,
                        _ => PaintKind.Noise
                    };
                    float cell = math.max(paint.patternSize, 1e-4f);
                    r.toPaint = Affine2.Scale(new float2(1f / cell)) * Affine2.Rotate(-math.radians(paint.patternRotation)) *
                                Affine2.Translate(-(m_Origin + (float2)paint.patternOffset));
                    r.param = paint.patternRatio;
                    r.spread = 0;
                    break;
                }
            }

            return r;
        }

        static Affine2 TextureMapping(VectraPaint paint, float2 mn, float2 size)
        {
            var tex = paint.texture;
            var uvRect = paint.textureUvRect;
            float2 scale = math.max(math.abs((float2)paint.textureScale), 1e-4f);
            Affine2 toUv;
            if (paint.textureFit == TextureFit.Tile && tex != null)
            {
                float2 tile = new float2(tex.width * uvRect.width, tex.height * uvRect.height) * scale;
                toUv = Affine2.Scale(1f / math.max(tile, 1e-4f)) * Affine2.Translate(-mn);
            }
            else
            {
                float2 fit = new float2(1f, 1f);
                if (tex != null && paint.textureFit != TextureFit.Stretch)
                {
                    float ta = tex.width * uvRect.width / math.max(tex.height * uvRect.height, 1e-4f);
                    float ra = size.x / size.y;
                    bool wider = ra > ta;
                    if (paint.textureFit == TextureFit.Fit) fit = wider ? new float2(ra / ta, 1f) : new float2(1f, ta / ra);
                    else fit = wider ? new float2(1f, ta / ra) : new float2(ra / ta, 1f);
                }

                toUv = Affine2.Translate(new float2(0.5f)) * Affine2.Scale(fit / scale) * Affine2.Translate(new float2(-0.5f)) *
                       Affine2.Scale(1f / size) * Affine2.Translate(-mn);
            }

            var rot = Affine2.Rotate(-math.radians(paint.textureRotation), new float2(0.5f));
            var off = Affine2.Translate(-(float2)paint.textureOffset);
            var rect = Affine2.Translate(new float2(uvRect.x, uvRect.y)) * Affine2.Scale(new float2(uvRect.width, uvRect.height));
            return rect * off * rot * toUv;
        }

        Color32 ToVertex(Color c)
        {
            if (m_World && QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                float a = c.a;
                c = c.linear;
                c.a = a;
            }

            return c;
        }

        Color32 SolidColor(Color c, out float intensity)
        {
            c = m_Adjust.Apply(c);
            intensity = math.max(math.max(c.r, c.g), math.max(c.b, 1f));
            if (intensity > 1f)
            {
                c.r /= intensity;
                c.g /= intensity;
                c.b /= intensity;
            }

            if (QualitySettings.activeColorSpace == ColorSpace.Linear) intensity = math.pow(intensity, 2.2f);
            var t = m_Tint;
            return ToVertex(c * new Color(t.r, t.g, t.b, t.a));
        }

        // ------------------------------------------------------------------------------------------------------
        // Emission
        // ------------------------------------------------------------------------------------------------------

        LayerConstants Constants(ShapeKind shape, LayerKind layer, in ResolvedPaint rp, int style, float4 s1, float4 s2,
            bool additive)
        {
            int spread = rp.spread;
            return new LayerConstants
            {
                header = VectraEncoding.Header(shape, layer, rp.kind, spread, m_Mod, m_ModFlag, false, style,
                    additive || rp.additive),
                s0 = m_S0,
                s1 = s1,
                s2 = s2,
                paintV = rp.v,
                paintParam = rp.param,
                color = rp.color
            };
        }

        void EmitAnalytic(LayerKind layer, VectraPaint paint, float2 lp, float extent, float2 offsetLocal,
            bool glow = false, bool additive = false)
        {
            var rp = Resolve(paint);
            EmitAnalytic(layer, rp, lp, extent, offsetLocal, glow, additive);
        }

        void EmitAnalytic(LayerKind layer, in ResolvedPaint rpIn, float2 lp, float extent, float2 offsetLocal,
            bool glow, bool additive)
        {
            var rp = rpIn;
            if (glow) rp.spread = 1;
            float2 he = m_A.halfExtents + extent + m_Margin;
            float2 pOffset = m_A.localToShape.ApplyVector(offsetLocal);
            var c = Constants(m_A.kind, layer, rp, m_A.style, m_A.s1, new float4(m_A.s2, lp), additive);
            buffer.AddQuad(-he, he, m_ShapeToLocal, pOffset, c, rp.toPaint);
        }

        void EmitSdf(LayerKind layer, VectraPaint paint, float2 lp, float2 offsetLocal, bool glow = false, bool additive = false)
        {
            var rp = Resolve(paint);
            EmitSdf(layer, rp, lp, offsetLocal, glow, additive);
        }

        void EmitSdf(LayerKind layer, in ResolvedPaint rpIn, float2 lp, float2 offsetLocal, bool glow, bool additive)
        {
            if (!m_Sdf.IsValid) return;
            var rp = rpIn;
            if (glow) rp.spread = 1;
            var c = Constants(ShapeKind.SdfTexture, layer, rp, 0, SdfParams1(), SdfParams2(lp), additive);
            float2 pMin = m_SdfBounds.min - m_Origin, pMax = m_SdfBounds.max - m_Origin;
            buffer.AddQuad(pMin, pMax, m_ShapeToLocal, offsetLocal, c, rp.toPaint);
        }

        float4 SdfParams1()
        {
            float inv = 1f / m_SdfTexel;
            var rect = m_Sdf.rect;
            return new float4(inv, rect.x + (m_Origin.x - m_SdfBounds.min.x) * inv,
                rect.y + (m_Origin.y - m_SdfBounds.min.y) * inv, m_SdfRange);
        }

        float4 SdfParams2(float2 lp)
        {
            var rect = m_Sdf.rect;
            return new float4(VectraEncoding.Pack12Raw(rect.x, rect.y), VectraEncoding.Pack12Raw(rect.xMax - 1, rect.yMax - 1),
                lp.x, lp.y);
        }

        void EmitFill(VectraPaint paint, bool analytic, float layerBlur, bool distanceFills)
        {
            if (analytic) EmitAnalytic(LayerKind.Fill, paint, new float2(layerBlur, 0f), layerBlur * 3f, float2.zero);
            else if ((distanceFills || m_SmoothSdf) && m_Sdf.IsValid) EmitSdf(LayerKind.Fill, paint, new float2(layerBlur, 0f), float2.zero);
            else EmitTrapezoids(m_Fill, paint);
        }

        void EmitTrapezoids(NativeList<Trapezoid> traps, VectraPaint paint)
        {
            if (!traps.IsCreated || traps.Length == 0) return;
            var rp = Resolve(paint);
            var c = Constants(ShapeKind.Path, LayerKind.Fill, rp, 0, float4.zero, float4.zero, false);
            // Path layers carry the dissolve modifier only; cuts are applied geometrically.
            if (m_Mod != ModifierKind.Dissolve)
            {
                c.header = VectraEncoding.Header(ShapeKind.Path, LayerKind.Fill, rp.kind, rp.spread, ModifierKind.None,
                    false, false, 0, rp.additive);
                c.s0 = 0f;
            }

            buffer.AddTrapezoids(traps.AsArray(), m_Origin, m_Margin, c, rp.toPaint);
            stats.trapezoids += traps.Length;
        }

        void EmitStroke(int index, StrokeLayer s, bool analytic, VectraShapeGeometry geometry, float tol)
        {
            if (!StrokeVisible(s)) return;
            if (CanStrokeAnalytically(analytic, s))
            {
                float hw = s.width * 0.5f;
                float offset = s.alignment == StrokeAlignment.Inside ? -hw : s.alignment == StrokeAlignment.Outside ? hw : 0f;
                EmitAnalytic(LayerKind.Stroke, s.paint, new float2(hw, offset), math.max(offset + hw, 0f), float2.zero);
                return;
            }

            if (m_SmoothSdf)
            {
                // Smooth booleans have no exact outline: the stroke is a band of the distance field.
                float hw = s.width * 0.5f;
                float offset = s.alignment == StrokeAlignment.Inside ? -hw : s.alignment == StrokeAlignment.Outside ? hw : 0f;
                EmitSdf(LayerKind.Stroke, s.paint, new float2(hw, offset), float2.zero);
                return;
            }

            if (!m_Poly.IsCreated || m_Poly.IsEmpty) return;
            var traps = EnsureStrokeTraps(index, s, geometry, tol);
            var rp = Resolve(s.paint);
            var c = Constants(ShapeKind.Path, LayerKind.Fill, rp, 0, float4.zero, float4.zero, false);
            if (m_Mod != ModifierKind.Dissolve)
            {
                c.header = VectraEncoding.Header(ShapeKind.Path, LayerKind.Fill, rp.kind, rp.spread, ModifierKind.None,
                    false, false, 0, rp.additive);
                c.s0 = 0f;
            }

            buffer.AddTrapezoids(traps.AsArray(), m_Origin, m_Margin, c, rp.toPaint);
        }

        void EmitEffect(VectraEffect e, bool analytic)
        {
            switch (e)
            {
                case DropShadowEffect ds:
                {
                    float sigma = ds.blur * 0.5f;
                    var rp = SolidPaint(ds.color, ds.blend);
                    var lp = new float2(sigma, ds.spread);
                    if (analytic) EmitAnalytic(LayerKind.Shadow, rp, lp, math.max(ds.spread, 0f) + sigma * 3f, ds.offset, false, false);
                    else EmitSdf(LayerKind.Shadow, rp, lp, ds.offset, false, false);
                    break;
                }
                case OuterGlowEffect og:
                {
                    float sigma = og.size * 0.5f;
                    var rp = SolidPaint(og.color, og.blend);
                    var lp = new float2(sigma, og.spread);
                    if (analytic) EmitAnalytic(LayerKind.Shadow, rp, lp, math.max(og.spread, 0f) + og.size, float2.zero, true, false);
                    else EmitSdf(LayerKind.Shadow, rp, lp, float2.zero, true, false);
                    break;
                }
                case InnerShadowEffect ins:
                {
                    float2 off = analytic ? m_A.localToShape.ApplyVector(ins.offset) : (float2)ins.offset;
                    var lp = new float2(
                        VectraEncoding.Pack12Raw(VectraEncoding.SignedLog12(off.x), VectraEncoding.SignedLog12(off.y)),
                        VectraEncoding.Pack12Raw(VectraEncoding.Log12(ins.blur * 0.5f), VectraEncoding.Log12(ins.choke)));
                    var rp = SolidPaint(ins.color, ins.blend);
                    if (analytic) EmitAnalytic(LayerKind.InnerShadow, rp, lp, 0f, float2.zero, false, false);
                    else EmitSdf(LayerKind.InnerShadow, rp, lp, float2.zero, false, false);
                    break;
                }
                case InnerGlowEffect ig:
                {
                    var lp = new float2(0f,
                        VectraEncoding.Pack12Raw(VectraEncoding.Log12(ig.size * 0.5f), VectraEncoding.Log12(ig.choke)));
                    var rp = SolidPaint(ig.color, ig.blend);
                    if (analytic) EmitAnalytic(LayerKind.InnerShadow, rp, lp, 0f, float2.zero, false, false);
                    else EmitSdf(LayerKind.InnerShadow, rp, lp, float2.zero, false, false);
                    break;
                }
                case BevelEffect bv:
                {
                    float angle = bv.lightAngle / 360f;
                    angle -= math.floor(angle);
                    var lp = new float2(
                        VectraEncoding.Pack12Raw(VectraEncoding.Log12(bv.size), (int)math.round(angle * 4095f)),
                        VectraEncoding.Pack12(bv.intensity, bv.softness));
                    var rp = SolidPaint(bv.highlight, LayerBlend.Normal);
                    if (analytic) EmitAnalytic(LayerKind.Bevel, rp, lp, 0f, float2.zero, false, false);
                    else EmitSdf(LayerKind.Bevel, rp, lp, float2.zero, false, false);
                    break;
                }
                case GrainEffect gr:
                {
                    var rp = SolidPaint(Color.white, LayerBlend.Normal);
                    var lp = new float2(gr.amount, gr.size);
                    if (analytic) EmitAnalytic(LayerKind.Grain, rp, lp, 0f, float2.zero, false, false);
                    else EmitSdf(LayerKind.Grain, rp, lp, float2.zero, false, false);
                    break;
                }
                case OutlineEffect ol:
                {
                    if (ol.width <= 0f) break;
                    var rp = SolidPaint(ol.color, ol.blend);
                    float hw = ol.width * 0.5f;
                    var lp = new float2(hw, ol.offset);
                    if (analytic) EmitAnalytic(LayerKind.Stroke, rp, lp, math.max(ol.offset + hw, 0f), float2.zero, false, false);
                    else EmitSdf(LayerKind.Stroke, rp, lp, float2.zero, false, false);
                    break;
                }
            }
        }

        ResolvedPaint SolidPaint(Color c, LayerBlend blend)
        {
            var col = SolidColor(c, out float intensity);
            return new ResolvedPaint
            {
                kind = PaintKind.Solid,
                toPaint = Affine2.Identity,
                param = intensity,
                color = col,
                additive = blend == LayerBlend.Additive
            };
        }

        void FinishRows()
        {
            for (int i = 0; i < m_Rows.Count; i++)
            {
                var r = m_Rows[i];
                VectraGradientAtlas.Release(ref r);
            }

            (m_Rows, m_NextRows) = (m_NextRows, m_Rows);
            m_NextRows.Clear();
        }

        public void Dispose()
        {
            buffer.Dispose();
            m_Path.Dispose();
            m_Poly.Dispose();
            if (m_Fill.IsCreated) m_Fill.Dispose();
            foreach (var t in m_StrokeTraps)
                if (t.IsCreated) t.Dispose();
            m_StrokeTraps.Clear();
            m_StrokeKeys.Clear();
            VectraSdfAtlas.Free(ref m_Sdf);
            foreach (var list in new[] { m_BatchSdf, m_PrevBatchSdf })
                for (int i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    VectraSdfAtlas.Free(ref r);
                }

            for (int i = 0; i < m_Rows.Count; i++)
            {
                var r = m_Rows[i];
                VectraGradientAtlas.Release(ref r);
            }

            m_Rows.Clear();
        }
    }
}
