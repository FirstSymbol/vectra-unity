using System.Globalization;
using System.Text;
using FirstSymbol.Vectra.Geometry;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>Exports Vectra shapes as SVG (paths keep their curves; gradients, strokes and shadows are written too).</summary>
    public static class SvgWriter
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Write(VectraShapeContent content, Rect rect)
        {
            var sb = new StringBuilder();
            float w = rect.width, h = rect.height;
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(F(w)).Append("\" height=\"").Append(F(h))
                .Append("\" viewBox=\"0 0 ").Append(F(w)).Append(' ').Append(F(h)).Append("\">\n");

            // Local (y up) to SVG (y down).
            var toSvg = new Affine2(1, 0, 0, -1, -rect.xMin, rect.yMax);
            var path = new NativePath(Allocator.TempJob, 64);
            content.ActiveGeometry?.BuildPath(new ShapeFrame { rect = rect, pixelSize = 1f }, new PathBuilder(path, toSvg));
            string d = PathData(path);
            path.Dispose();

            var defs = new StringBuilder();
            string filter = null;
            foreach (var e in content.ActiveEffects)
            {
                if (e is DropShadowEffect ds && ds.enabled)
                {
                    filter = "vectraShadow";
                    defs.Append("  <filter id=\"vectraShadow\" x=\"-50%\" y=\"-50%\" width=\"200%\" height=\"200%\"><feDropShadow dx=\"")
                        .Append(F(ds.offset.x)).Append("\" dy=\"").Append(F(-ds.offset.y)).Append("\" stdDeviation=\"")
                        .Append(F(ds.blur * 0.5f)).Append("\" flood-color=\"").Append(Hex(ds.color)).Append("\" flood-opacity=\"")
                        .Append(F(ds.color.a)).Append("\"/></filter>\n");
                    break;
                }
            }

            string rule = content.ActiveGeometry != null && content.ActiveGeometry.FillRule == FillRule.EvenOdd ? " fill-rule=\"evenodd\"" : "";
            int gid = 0;
            var body = new StringBuilder();
            foreach (var f in content.ActiveFills)
            {
                if (f == null || !f.enabled || f.paint == null) continue;
                string paint = Paint(f.paint, rect, defs, ref gid);
                body.Append("  <path d=\"").Append(d).Append("\" fill=\"").Append(paint).Append('"').Append(rule);
                if (f.paint.opacity < 1f) body.Append(" fill-opacity=\"").Append(F(f.paint.opacity)).Append('"');
                if (filter != null) body.Append(" filter=\"url(#").Append(filter).Append(")\"");
                body.Append("/>\n");
                filter = null;
            }

            foreach (var s in content.ActiveStrokes)
            {
                if (s == null || !s.enabled || s.paint == null) continue;
                string paint = Paint(s.paint, rect, defs, ref gid);
                body.Append("  <path d=\"").Append(d).Append("\" fill=\"none\" stroke=\"").Append(paint)
                    .Append("\" stroke-width=\"").Append(F(s.width)).Append('"');
                if (s.paint.opacity < 1f) body.Append(" stroke-opacity=\"").Append(F(s.paint.opacity)).Append('"');
                if (s.cap != LineCap.Butt) body.Append(" stroke-linecap=\"").Append(s.cap == LineCap.Round ? "round" : "square").Append('"');
                if (s.join != LineJoin.Miter) body.Append(" stroke-linejoin=\"").Append(s.join == LineJoin.Round ? "round" : "bevel").Append('"');
                if (s.dashed && s.dashPattern != null && s.dashPattern.Count > 0)
                {
                    body.Append(" stroke-dasharray=\"");
                    for (int i = 0; i < s.dashPattern.Count; i++) body.Append(i > 0 ? " " : "").Append(F(s.dashPattern[i]));
                    body.Append('"');
                    if (s.dashOffset != 0f) body.Append(" stroke-dashoffset=\"").Append(F(s.dashOffset)).Append('"');
                }

                body.Append("/>\n");
            }

            if (defs.Length > 0) sb.Append("  <defs>\n").Append(defs).Append("  </defs>\n");
            sb.Append(body).Append("</svg>\n");
            return sb.ToString();
        }

        static string Paint(VectraPaint p, Rect rect, StringBuilder defs, ref int gid)
        {
            if (!p.IsGradient || p.gradient == null || p.gradient.stops.Count == 0 || p.useCustomMapping)
                return Hex(p.type == PaintType.Solid ? p.color : p.gradient != null && p.gradient.stops.Count > 0 ? p.gradient.stops[0].color : p.color);
            string id = "vectraGradient" + gid++;
            // Handles are normalized with y up; SVG bounding box units have y down.
            if (p.type == PaintType.LinearGradient)
                defs.Append("  <linearGradient id=\"").Append(id).Append("\" x1=\"").Append(F(p.gradientStart.x)).Append("\" y1=\"")
                    .Append(F(1f - p.gradientStart.y)).Append("\" x2=\"").Append(F(p.gradientEnd.x)).Append("\" y2=\"")
                    .Append(F(1f - p.gradientEnd.y)).Append('"');
            else
            {
                var c = p.gradientStart;
                float r = (p.gradientEnd - p.gradientStart).magnitude;
                defs.Append("  <radialGradient id=\"").Append(id).Append("\" cx=\"").Append(F(c.x)).Append("\" cy=\"")
                    .Append(F(1f - c.y)).Append("\" r=\"").Append(F(r)).Append('"');
            }

            if (p.spread != SpreadMode.Pad) defs.Append(" spreadMethod=\"").Append(p.spread == SpreadMode.Reflect ? "reflect" : "repeat").Append('"');
            defs.Append(">");
            foreach (var s in p.gradient.stops)
                defs.Append("<stop offset=\"").Append(F(s.position)).Append("\" stop-color=\"").Append(Hex(s.color))
                    .Append("\" stop-opacity=\"").Append(F(s.color.a)).Append("\"/>");
            defs.Append(p.type == PaintType.LinearGradient ? "</linearGradient>\n" : "</radialGradient>\n");
            return "url(#" + id + ")";
        }

        public static string PathData(in NativePath path)
        {
            var sb = new StringBuilder();
            int pi = 0;
            for (int i = 0; i < path.verbs.Length; i++)
            {
                switch ((PathVerb)path.verbs[i])
                {
                    case PathVerb.MoveTo: sb.Append('M').Append(P(path.points[pi++])); break;
                    case PathVerb.LineTo: sb.Append('L').Append(P(path.points[pi++])); break;
                    case PathVerb.QuadTo:
                        sb.Append('Q').Append(P(path.points[pi])).Append(' ').Append(P(path.points[pi + 1]));
                        pi += 2;
                        break;
                    case PathVerb.CubicTo:
                        sb.Append('C').Append(P(path.points[pi])).Append(' ').Append(P(path.points[pi + 1])).Append(' ').Append(P(path.points[pi + 2]));
                        pi += 3;
                        break;
                    case PathVerb.Close: sb.Append('Z'); break;
                }
            }

            return sb.ToString();
        }

        static string P(float2 p) => F(p.x) + "," + F(p.y);
        static string F(float v) => math.round(v * 1000f) / 1000f == 0f ? "0" : (math.round(v * 1000f) / 1000f).ToString("0.###", Inv);

        static string Hex(Color c)
        {
            Color32 c32 = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            return $"#{c32.r:x2}{c32.g:x2}{c32.b:x2}";
        }
    }
}
