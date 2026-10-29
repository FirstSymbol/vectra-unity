using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>A path stored in document (viewBox) coordinates, y pointing down as in SVG.</summary>
    [Serializable]
    public class SvgPathData
    {
        public List<byte> verbs = new List<byte>();
        public List<Vector2> points = new List<Vector2>();
        public FillRule rule = FillRule.NonZero;

        public bool IsEmpty => verbs.Count == 0;

        public void CopyFrom(in NativePath p)
        {
            verbs.Clear();
            points.Clear();
            for (int i = 0; i < p.verbs.Length; i++) verbs.Add(p.verbs[i]);
            for (int i = 0; i < p.points.Length; i++) points.Add(p.points[i]);
        }

        public void WriteTo(PathBuilder b)
        {
            int pi = 0;
            for (int i = 0; i < verbs.Count; i++)
            {
                switch ((PathVerb)verbs[i])
                {
                    case PathVerb.MoveTo: b.MoveTo(points[pi++]); break;
                    case PathVerb.LineTo: b.LineTo(points[pi++]); break;
                    case PathVerb.QuadTo: b.QuadTo(points[pi], points[pi + 1]); pi += 2; break;
                    case PathVerb.CubicTo: b.CubicTo(points[pi], points[pi + 1], points[pi + 2]); pi += 3; break;
                    case PathVerb.Close: b.Close(); break;
                }
            }
        }

        public Bounds2 ControlBounds()
        {
            var bb = Bounds2.Empty;
            foreach (var p in points) bb.Encapsulate(p);
            return bb;
        }
    }

    public enum SvgTextAnchor
    {
        Start,
        Middle,
        End
    }

    /// <summary>One drawable SVG element with its resolved style.</summary>
    [Serializable]
    public class SvgElementData
    {
        public string tag;
        public string id;
        public string className;

        public SvgPathData path = new SvgPathData();
        public bool hasFill;
        public VectraPaint fill;
        public bool hasStroke;
        public StrokeLayer stroke;
        /// <summary>Clip paths (in document space); the element is drawn inside every one of them.</summary>
        public List<SvgPathData> clips = new List<SvgPathData>();
        [SerializeReference] public List<VectraEffect> effects = new List<VectraEffect>();

        public bool isText;
        public string text;
        public Vector2 textPosition;
        public float fontSize = 16f;
        public string fontFamily;
        public bool bold, italic;
        public SvgTextAnchor anchor;
        public Color textColor = Color.black;
        /// <summary>Text rotation in degrees (from the element transform).</summary>
        public float textRotation;
        public float textScale = 1f;

        public bool isImage;
        public Rect imageRect;
        public byte[] imageData;
        public Texture2D image;
    }

    public enum SvgAlign
    {
        None,
        XMinYMin, XMidYMin, XMaxYMin,
        XMinYMid, XMidYMid, XMaxYMid,
        XMinYMax, XMidYMax, XMaxYMax
    }

    /// <summary>An SVG document converted to Vectra data (created by the importer or <see cref="SvgParser"/>).</summary>
    [CreateAssetMenu(menuName = "Vectra/SVG Document (empty)", fileName = "SvgDocument", order = 21)]
    public class VectraSvgDocument : ScriptableObject
    {
        public Rect viewBox = new Rect(0, 0, 100, 100);
        /// <summary>Intrinsic size (width/height attributes, or the viewBox size).</summary>
        public Vector2 size = new Vector2(100, 100);
        public SvgAlign align = SvgAlign.XMidYMid;
        public bool slice;
        public List<SvgElementData> elements = new List<SvgElementData>();
        /// <summary>Messages about unsupported features met during parsing.</summary>
        public List<string> warnings = new List<string>();

        public float AspectRatio => viewBox.height > 0f ? viewBox.width / viewBox.height : 1f;

        /// <summary>Maps document coordinates into <paramref name="rect"/> (y flipped) honouring preserveAspectRatio.</summary>
        public Affine2 DocumentToLocal(Rect rect, SvgAlign? alignOverride = null, bool? sliceOverride = null)
        {
            var a = alignOverride ?? align;
            bool sl = sliceOverride ?? slice;
            float vw = Mathf.Max(viewBox.width, 1e-6f), vh = Mathf.Max(viewBox.height, 1e-6f);
            float sx = rect.width / vw, sy = rect.height / vh;
            float tx = 0f, ty = 0f;
            if (a != SvgAlign.None)
            {
                float s = sl ? Mathf.Max(sx, sy) : Mathf.Min(sx, sy);
                sx = sy = s;
                int ix = ((int)a - 1) % 3, iy = ((int)a - 1) / 3;
                tx = (rect.width - vw * s) * ix * 0.5f;
                ty = (rect.height - vh * s) * iy * 0.5f;
            }

            // local = (rect.xMin + tx + (x - vb.x) * sx, rect.yMax - ty - (y - vb.y) * sy)
            return new Affine2(sx, 0f, 0f, -sy, rect.xMin + tx - viewBox.x * sx, rect.yMax - ty + viewBox.y * sy);
        }
    }

    /// <summary>Geometry that replays a stored SVG path (document coordinates).</summary>
    [Serializable]
    sealed class SvgElementGeometry : VectraShapeGeometry
    {
        public SvgPathData data;
        public int key;

        public override string DisplayName => "SVG Element";
        public override FillRule FillRule => data?.rule ?? FillRule.NonZero;
        public override void BuildPath(in ShapeFrame frame, PathBuilder b) => data?.WriteTo(b);
        protected override void Hash(ref HashBuilder h) => h.Add(key);
    }

    /// <summary>Per-element style overrides applied by SVG components (selector: #id, .class or tag name).</summary>
    [Serializable]
    public class SvgOverride
    {
        public string selector = "#id";
        public bool hide;
        public bool overrideFill;
        [ColorUsage(true, true)] public Color fill = Color.white;
        public bool overrideStroke;
        [ColorUsage(true, true)] public Color stroke = Color.black;

        public bool Matches(SvgElementData e)
        {
            if (string.IsNullOrEmpty(selector)) return false;
            if (selector[0] == '#') return e.id == selector.Substring(1);
            if (selector[0] == '.')
            {
                if (string.IsNullOrEmpty(e.className)) return false;
                foreach (var c in e.className.Split(' '))
                    if (c == selector.Substring(1)) return true;
                return false;
            }

            return e.tag == selector;
        }
    }

    /// <summary>Builds the mesh of a whole SVG document with one <see cref="VectraShapeRenderer"/>.</summary>
    public static class VectraSvgBuilder
    {
        static readonly SvgElementGeometry s_Geometry = new SvgElementGeometry();
        static readonly List<FillLayer> s_Fills = new List<FillLayer> { new FillLayer() };
        static readonly List<StrokeLayer> s_Strokes = new List<StrokeLayer>();
        static readonly List<VectraEffect> s_Effects = new List<VectraEffect>();

        public static void Build(VectraSvgDocument doc, VectraShapeRenderer renderer, Rect localRect, float localPixelSize,
            Color tint, bool world, VectraQuality quality, List<SvgOverride> overrides = null, SvgAlign? align = null,
            bool? slice = null)
        {
            renderer.BeginBatch();
            if (doc == null || doc.elements == null)
            {
                renderer.EndBatch();
                return;
            }

            var toLocal = doc.DocumentToLocal(localRect, align, slice);
            float scale = math.max(toLocal.UniformScale, 1e-6f);
            var frame = new ShapeFrame { rect = doc.viewBox, pixelSize = localPixelSize / scale };
            var clip = new Polylines(Allocator.TempJob, 64);
            var clipPath = new NativePath(Allocator.TempJob, 32);
            var clipBuilder = new PathBuilder(clipPath);

            for (int i = 0; i < doc.elements.Count; i++)
            {
                var e = doc.elements[i];
                if (e == null || e.isText || e.isImage || e.path == null || e.path.IsEmpty) continue;
                bool hidden = false;
                Color? fillOverride = null, strokeOverride = null;
                if (overrides != null)
                    foreach (var o in overrides)
                    {
                        if (o == null || !o.Matches(e)) continue;
                        if (o.hide) hidden = true;
                        if (o.overrideFill) fillOverride = o.fill;
                        if (o.overrideStroke) strokeOverride = o.stroke;
                    }

                if (hidden) continue;
                s_Geometry.data = e.path;
                s_Geometry.key = i * 7919 + doc.GetHashCode();
                s_Fills[0].enabled = e.hasFill && e.fill != null;
                s_Fills[0].paint = e.fill;
                if (fillOverride.HasValue && e.fill != null)
                {
                    var p = e.fill.Clone();
                    p.type = PaintType.Solid;
                    p.color = fillOverride.Value;
                    s_Fills[0].paint = p;
                }

                s_Strokes.Clear();
                if (e.hasStroke && e.stroke != null)
                {
                    var st = e.stroke;
                    if (strokeOverride.HasValue)
                    {
                        st = st.Clone();
                        st.paint = VectraPaint.Solid(strokeOverride.Value);
                    }

                    s_Strokes.Add(st);
                }

                s_Effects.Clear();
                if (e.effects != null) s_Effects.AddRange(e.effects);

                var input = new VectraBuildInput
                {
                    geometry = s_Geometry,
                    fills = s_Fills,
                    strokes = s_Strokes,
                    effects = s_Effects,
                    frame = frame,
                    tint = tint,
                    worldSpace = world,
                    quality = quality,
                    forcePath = true
                };

                if (e.clips != null && e.clips.Count > 0)
                {
                    clip.Clear();
                    // Several clips intersect: the first is the operand, the others are intersected geometrically
                    // by flattening them into the same operand only when there is one (the common case).
                    clipPath.Clear();
                    clipBuilder.Reset(clipPath, Affine2.Identity);
                    e.clips[0].WriteTo(clipBuilder);
                    VectraGeometry.Flatten(clipPath, Affine2.Identity, frame.pixelSize * 0.2f, 0, true, ref clip);
                    input.clip = clip;
                    input.clipRule = e.clips[0].rule;
                }

                renderer.Build(input);
            }

            renderer.EndBatch();
            clip.Dispose();
            clipPath.Dispose();

            // Document space to local space (positions only; shading works in document space).
            var buf = renderer.buffer;
            var bounds = Bounds2.Empty;
            for (int v = 0; v < buf.vertices.Length; v++)
            {
                var vert = buf.vertices[v];
                var p = toLocal.Apply(vert.position.xy);
                vert.position = new float3(p, 0f);
                bounds.Encapsulate(p);
                buf.vertices[v] = vert;
            }

            buf.bounds = bounds;
            for (int i = 0; i < s_Strokes.Count; i++) s_Strokes[i] = null;
            s_Strokes.Clear();
            s_Effects.Clear();
            s_Fills[0].paint = null;
            s_Geometry.data = null;
        }
    }
}
