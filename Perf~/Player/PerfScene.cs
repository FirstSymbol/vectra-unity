using System;
using FirstSymbol.Vectra;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.VectraPerf
{
    /// <summary>The canvas, the camera and the pictures the reference rows draw with.</summary>
    public static class PerfScene
    {
        public static Canvas MakeCanvas(Transform parent, string name = "Canvas")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas;
        }

        public static RectTransform MakeContainer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(1280, 720);
            return rt;
        }

        public static Camera MakeCamera(Transform parent, bool ortho, float size = 25f)
        {
            var go = new GameObject("Camera") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            cam.orthographic = ortho;
            cam.orthographicSize = size;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            go.transform.position = new Vector3(0, 0, -10);
            return cam;
        }

        public static RectTransform Place(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(pos.x, -pos.y);
            rt.sizeDelta = size;
            return rt;
        }

        public static Color Tint(int i) => Color.HSVToRGB((i * 0.0137f) % 1f, 0.55f, 0.92f);

        // ---------------------------------------------------------------- reference sprites: what plain uGUI draws the same shapes with

        public enum Kind { RoundRect, Ellipse, Star, Outline, Gradient }

        static readonly Sprite[] s_Sprites = new Sprite[5];

        public static Sprite SpriteOf(Kind kind)
        {
            int k = (int)kind;
            if (s_Sprites[k] != null) return s_Sprites[k];
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "PerfRef " + kind, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float cover;
                switch (kind)
                {
                    case Kind.Ellipse: cover = Smooth(1f - Mathf.Sqrt(u * u + v * v)); break;
                    case Kind.Star: cover = Smooth(StarDistance(u, v)); break;
                    case Kind.Outline:
                        float d = RoundRectDistance(u, v);
                        cover = Mathf.Min(Smooth(d), Smooth(0.1f - d));
                        break;
                    default: cover = Smooth(RoundRectDistance(u, v)); break;
                }

                float shade = kind == Kind.Gradient ? 1f - 0.55f * (v * 0.5f + 0.5f) : 1f;
                byte c = (byte)(255 * shade);
                px[y * n + x] = new Color32(c, c, c, (byte)(255 * cover));
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            Vector4 border = kind == Kind.Ellipse || kind == Kind.Star ? Vector4.zero : new Vector4(16, 16, 16, 16);
            s_Sprites[k] = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            s_Sprites[k].name = "PerfRef " + kind;
            return s_Sprites[k];
        }

        static float Smooth(float d) => Mathf.Clamp01(d * 32f);

        // Distance to the inside of a rounded rectangle, positive inside.
        static float RoundRectDistance(float u, float v)
        {
            const float r = 0.35f;
            float qx = Mathf.Abs(u) - (1f - r), qy = Mathf.Abs(v) - (1f - r);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return r - (outside + inside);
        }

        // A five-point star: inside when the point is left of every edge of the outline.
        static float StarDistance(float u, float v)
        {
            float ang = Mathf.Atan2(u, v);
            float sector = Mathf.PI * 2f / 5f;
            float a = Mathf.Repeat(ang + sector * 0.5f, sector) - sector * 0.5f;
            float r = Mathf.Sqrt(u * u + v * v);
            // Outer radius 0.95 at the tips, inner 0.42 between them, straight edges.
            float tip = 0.95f, dent = 0.42f;
            float t = Mathf.Abs(a) / (sector * 0.5f);
            float edge = Mathf.Lerp(tip, dent, t);
            return (edge - r) * 0.8f;
        }

        public static Image MakeImage(Transform parent, string name, Kind kind, Vector2 pos, Vector2 size, Color color)
        {
            var rt = Place(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = SpriteOf(kind);
            img.type = kind == Kind.Ellipse || kind == Kind.Star ? Image.Type.Simple : Image.Type.Sliced;
            img.color = color;
            return img;
        }

        public static Kind KindOf(int i) => (Kind)(i % 5);

        // ---------------------------------------------------------------- Vectra shapes of the same mix

        /// <summary>
        /// The mix of the shape rows: 0 rounded rectangle, 1 ellipse, 2 star (a path), 3 dashed stroke on a rounded rectangle,
        /// 4 rounded rectangle with a linear gradient.
        /// </summary>
        public static void Style(VectraShape s, int i)
        {
            Color c = Tint(i);
            switch (i % 5)
            {
                case 0:
                    s.SetGeometry<RectangleShape>().cornerRadii = new Vector4(6, 6, 6, 6);
                    s.SetFill(c);
                    break;
                case 1:
                    s.SetGeometry<EllipseShape>();
                    s.SetFill(c);
                    break;
                case 2:
                    s.SetGeometry<StarShape>();
                    s.SetFill(c);
                    break;
                case 3:
                    s.SetGeometry<RectangleShape>().cornerRadii = new Vector4(4, 4, 4, 4);
                    s.Fills.Clear();
                    s.SetStroke(c, 2f);
                    s.Strokes[0].dashed = true;
                    break;
                default:
                    s.SetGeometry<RectangleShape>().cornerRadii = new Vector4(6, 6, 6, 6);
                    s.SetFill(VectraPaint.Linear(c, Color.Lerp(c, Color.black, 0.6f), 90f));
                    break;
            }
        }

        public static VectraShape MakeVectra(Transform parent, int i, Vector2 pos, Vector2 size)
        {
            var rt = Place(parent, "S" + i, pos, size);
            var s = rt.gameObject.AddComponent<VectraShape>();
            Style(s, i);
            return s;
        }

        public static void StyleWorld(VectraRenderer r, int i)
        {
            Color c = Tint(i);
            r.Fills.Clear();
            switch (i % 5)
            {
                case 0:
                    r.SetGeometry<RectangleShape>().cornerRadii = new Vector4(0.1f, 0.1f, 0.1f, 0.1f);
                    r.Fills.Add(new FillLayer(VectraPaint.Solid(c)));
                    break;
                case 1:
                    r.SetGeometry<EllipseShape>();
                    r.Fills.Add(new FillLayer(VectraPaint.Solid(c)));
                    break;
                case 2:
                    r.SetGeometry<StarShape>();
                    r.Fills.Add(new FillLayer(VectraPaint.Solid(c)));
                    break;
                case 3:
                    r.SetGeometry<RectangleShape>().cornerRadii = new Vector4(0.08f, 0.08f, 0.08f, 0.08f);
                    r.Strokes.Clear();
                    r.Strokes.Add(new StrokeLayer(c, 0.06f) { dashed = true, dashPattern = new System.Collections.Generic.List<float> { 0.15f, 0.1f } });
                    break;
                default:
                    r.SetGeometry<RectangleShape>().cornerRadii = new Vector4(0.1f, 0.1f, 0.1f, 0.1f);
                    r.Fills.Add(new FillLayer(VectraPaint.Linear(c, Color.Lerp(c, Color.black, 0.6f), 90f)));
                    break;
            }
        }
    }
}
