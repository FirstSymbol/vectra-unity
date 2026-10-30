using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FirstSymbol.Vectra.Geometry;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>Runtime entry points for SVG: parsing, loading and expanding documents into editable shapes.</summary>
    public static class VectraSvg
    {
        /// <summary>Parses SVG markup into a new document (warnings are listed in <see cref="VectraSvgDocument.warnings"/>).</summary>
        public static VectraSvgDocument Parse(string svgText) => SvgParser.Parse(svgText);

        /// <summary>Parses a TextAsset (an .svg file imported as text, e.g. renamed to .svg.txt).</summary>
        public static VectraSvgDocument Load(TextAsset asset) => asset != null ? SvgParser.Parse(asset.text) : null;

        /// <summary>Downloads and parses an SVG file. <paramref name="done"/> receives the document or an error.</summary>
        public static UnityWebRequestAsyncOperation Download(string url, Action<VectraSvgDocument, string> done)
        {
            var request = UnityWebRequest.Get(url);
            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (request.result != UnityWebRequest.Result.Success) done?.Invoke(null, request.error);
                    else done?.Invoke(SvgParser.Parse(request.downloadHandler.text), null);
                }
                catch (Exception e)
                {
                    done?.Invoke(null, e.Message);
                }
                finally
                {
                    request.Dispose();
                }
            };
            return op;
        }

        /// <summary>
        /// Converts the document drawn by <paramref name="image"/> into a hierarchy of separate
        /// <see cref="VectraShape"/> objects (one per element; text and images become TextMeshPro and RawImage
        /// objects) so every element can be edited and animated on its own. The new root is created next to the
        /// image with the same rect; the image itself is left untouched.
        /// </summary>
        public static RectTransform Expand(VectraSvgImage image)
        {
            if (image == null || image.Document == null) return null;
            var doc = image.Document;
            var src = image.rectTransform;
            var rootGo = new GameObject(image.name + " (expanded)", typeof(RectTransform));
            var root = (RectTransform)rootGo.transform;
            root.SetParent(src.parent, false);
            root.SetSiblingIndex(src.GetSiblingIndex() + 1);
            root.anchorMin = src.anchorMin;
            root.anchorMax = src.anchorMax;
            root.pivot = src.pivot;
            root.anchoredPosition3D = src.anchoredPosition3D;
            root.sizeDelta = src.sizeDelta;
            root.localRotation = src.localRotation;
            root.localScale = src.localScale;

            var rect = root.rect;
            var toLocal = doc.DocumentToLocal(rect, image.PreserveAspect ? (SvgAlign?)null : SvgAlign.None);
            float scale = toLocal.UniformScale;
            var used = new HashSet<string>();
            for (int i = 0; i < doc.elements.Count; i++)
            {
                var e = doc.elements[i];
                if (e == null) continue;
                string name = UniqueName(string.IsNullOrEmpty(e.id) ? e.tag + " " + i : e.id, used);
                if (e.isText) ExpandText(e, root, rect, toLocal, scale, name, image);
                else if (e.isImage) ExpandImage(e, root, rect, toLocal, name, image.color);
                else if (e.path != null && !e.path.IsEmpty) ExpandPath(e, root, rect, toLocal, scale, name, image.color);
            }

            return root;
        }

        static string UniqueName(string name, HashSet<string> used)
        {
            string n = name;
            for (int k = 2; !used.Add(n); k++) n = name + " " + k;
            return n;
        }

        static RectTransform Child(Transform parent, string name, Rect parentRect, float2 a, float2 b)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(math.abs(b.x - a.x), math.abs(b.y - a.y));
            rt.anchoredPosition = new Vector2((a.x + b.x) * 0.5f - parentRect.center.x, (a.y + b.y) * 0.5f - parentRect.center.y);
            return rt;
        }

        static void ExpandPath(SvgElementData e, Transform root, Rect rootRect, Affine2 toLocal, float scale, string name, Color tint)
        {
            var bb = e.path.ControlBounds();
            if (!bb.IsValid) return;
            float2 a = toLocal.Apply(bb.min), b = toLocal.Apply(bb.max);
            bool hasClips = e.clips != null && e.clips.Count > 0;

            // Stroked elements are clipped by nested stencil masks (a boolean would drop strokes of open paths).
            Transform parent = root;
            var parentRect = rootRect;
            if (hasClips && e.hasStroke)
            {
                foreach (var clip in e.clips)
                {
                    var cb = clip.ControlBounds();
                    if (!cb.IsValid) continue;
                    float2 ca = toLocal.Apply(cb.min), cbm = toLocal.Apply(cb.max);
                    var mrt = Child(parent, name + " clip", parentRect, ca, cbm);
                    var clipShape = mrt.gameObject.AddComponent<VectraShape>();
                    clipShape.Geometry = new SvgPathShape { data = PathData(clip), fit = SvgPathFit.Stretch, fillRule = clip.rule };
                    clipShape.raycastTarget = false;
                    var mask = mrt.gameObject.AddComponent<VectraMask>();
                    mask.showMaskGraphic = false;
                    parent = mrt;
                    float2 mn = math.min(ca, cbm), mx = math.max(ca, cbm);
                    parentRect = new Rect(mn.x, mn.y, mx.x - mn.x, mx.y - mn.y);
                }

                hasClips = false;
            }

            var rt = Child(parent, name, parentRect, a, b);
            var shape = rt.gameObject.AddComponent<VectraShape>();
            shape.color = tint;
            var element = new SvgPathShape { data = PathData(e.path), fit = SvgPathFit.Stretch, fillRule = e.path.rule };
            if (!hasClips) shape.Geometry = element;
            else
            {
                // Clip paths become intersect operands framed by their own bounds (relative to the element bounds).
                var boolean = new BooleanShape();
                boolean.operands.Clear();
                boolean.operands.Add(new BooleanOperand(element, BooleanOp.Union, new Rect(0f, 0f, 1f, 1f)));
                float2 size = math.max(math.abs(b - a), 1e-4f), min = math.min(a, b);
                foreach (var clip in e.clips)
                {
                    var cb = clip.ControlBounds();
                    if (!cb.IsValid) continue;
                    float2 ca = toLocal.Apply(cb.min), cbm = toLocal.Apply(cb.max);
                    float2 cmin = math.min(ca, cbm), csize = math.abs(cbm - ca);
                    var r = new Rect((cmin.x - min.x) / size.x, (cmin.y - min.y) / size.y, csize.x / size.x, csize.y / size.y);
                    var clipShape = new SvgPathShape { data = PathData(clip), fit = SvgPathFit.Stretch, fillRule = clip.rule };
                    boolean.operands.Add(new BooleanOperand(clipShape, BooleanOp.Intersect, r));
                }

                shape.Geometry = boolean;
            }

            // Child local space -> document space (for gradients mapped in document units).
            float2 center = (a + b) * 0.5f;
            var childToDoc = toLocal.Inverse() * Affine2.Translate(center);

            var c = shape.Content;
            c.fills.Clear();
            if (e.hasFill && e.fill != null)
            {
                var p = e.fill.Clone();
                if (p.useCustomMapping) p.customMapping = p.customMapping * childToDoc;
                c.fills.Add(new FillLayer(p));
            }

            c.strokes.Clear();
            if (e.hasStroke && e.stroke != null)
            {
                var s = e.stroke.Clone();
                s.width *= scale;
                if (s.dashPattern != null && !s.dashRelative)
                    for (int k = 0; k < s.dashPattern.Count; k++) s.dashPattern[k] *= scale;
                s.dashOffset *= scale;
                if (s.paint != null && s.paint.useCustomMapping) s.paint.customMapping = s.paint.customMapping * childToDoc;
                c.strokes.Add(s);
            }

            c.effects.Clear();
            if (e.effects != null)
                foreach (var fx in e.effects)
                    if (fx != null) c.effects.Add(ScaleEffect(fx.Clone(), scale));

            shape.SetShapeDirty();
        }

        static VectraEffect ScaleEffect(VectraEffect fx, float s)
        {
            switch (fx)
            {
                case DropShadowEffect d:
                    d.offset *= s;
                    d.blur *= s;
                    d.spread *= s;
                    break;
                case LayerBlurEffect lb:
                    lb.blur *= s;
                    break;
                case OuterGlowEffect og:
                    og.size *= s;
                    og.spread *= s;
                    break;
            }

            return fx;
        }

        static void ExpandText(SvgElementData e, Transform root, Rect rootRect, Affine2 toLocal, float scale, string name,
            VectraSvgImage image)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            image.ConfigureText(t, e, toLocal, scale, rootRect);
        }

        static void ExpandImage(SvgElementData e, Transform root, Rect rootRect, Affine2 toLocal, string name, Color tint)
        {
            if (e.image == null && e.imageData != null)
            {
                e.image = new Texture2D(2, 2);
                e.image.LoadImage(e.imageData);
            }

            if (e.image == null) return;
            float2 a = toLocal.Apply(new float2(e.imageRect.xMin, e.imageRect.yMin));
            float2 b = toLocal.Apply(new float2(e.imageRect.xMax, e.imageRect.yMax));
            var rt = Child(root, name, rootRect, a, b);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = e.image;
            img.color = e.textColor * tint;
        }

        /// <summary>Writes stored path data as an SVG <c>d</c> attribute (document coordinates).</summary>
        public static string PathData(SvgPathData path)
        {
            var sb = new StringBuilder(path.points.Count * 12);
            int pi = 0;
            foreach (var verb in path.verbs)
            {
                switch ((PathVerb)verb)
                {
                    case PathVerb.MoveTo: sb.Append('M'); Point(sb, path.points[pi++]); break;
                    case PathVerb.LineTo: sb.Append('L'); Point(sb, path.points[pi++]); break;
                    case PathVerb.QuadTo:
                        sb.Append('Q');
                        Point(sb, path.points[pi++]);
                        sb.Append(' ');
                        Point(sb, path.points[pi++]);
                        break;
                    case PathVerb.CubicTo:
                        sb.Append('C');
                        Point(sb, path.points[pi++]);
                        sb.Append(' ');
                        Point(sb, path.points[pi++]);
                        sb.Append(' ');
                        Point(sb, path.points[pi++]);
                        break;
                    case PathVerb.Close: sb.Append('Z'); break;
                }
            }

            return sb.ToString();
        }

        static void Point(StringBuilder sb, Vector2 p)
        {
            sb.Append(p.x.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(p.y.ToString("0.###", CultureInfo.InvariantCulture));
        }

        /// <summary>Destroys an object in play mode or edit mode.</summary>
        internal static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
