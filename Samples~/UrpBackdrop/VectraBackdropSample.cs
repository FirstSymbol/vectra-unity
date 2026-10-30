using FirstSymbol.Vectra.URP;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Vectra.Samples
{
    /// <summary>
    /// URP backdrop effects over a moving colourful background: a frosted glass card and one tile per advanced
    /// blend mode. Requires the Vectra Backdrop Feature on the active URP renderer (Renderer asset > Add Renderer
    /// Feature > Vectra Backdrop Feature). The capture happens before transparent objects, so the background UI
    /// is drawn by a base camera and this canvas by an overlay camera of its stack: the overlay camera then sees
    /// the finished background.
    /// </summary>
    public class VectraBackdropSample : MonoBehaviour
    {
        [Tooltip("Container on the canvas of the base camera (the backdrop). Empty = draw the background here.")]
        [SerializeField] RectTransform m_Background;

        readonly RectTransform[] m_Blobs = new RectTransform[6];
        Font m_Font;

        void Start()
        {
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Background: large soft blobs that drift around.
            var back = m_Background != null ? m_Background : (RectTransform)transform;
            var bg = Shape<RectangleShape>("Background", Vector2.zero, new Vector2(1920, 1080), back);
            bg.SetFill(VectraPaint.Linear(new Color(0.08f, 0.09f, 0.16f), new Color(0.16f, 0.1f, 0.22f), 90f));
            for (int i = 0; i < m_Blobs.Length; i++)
            {
                var blob = Shape<EllipseShape>("Blob " + i, Vector2.zero, new Vector2(420, 420) * (0.6f + 0.15f * (i % 3)), back);
                blob.SetFill(VectraPaint.Radial(Color.HSVToRGB(i / (float)m_Blobs.Length, 0.75f, 1f), Color.HSVToRGB(i / (float)m_Blobs.Length, 0.9f, 0.6f)));
                m_Blobs[i] = blob.rectTransform;
            }

            var stripes = Shape<RectangleShape>("Stripes", new Vector2(0, -60), new Vector2(1500, 90), back);
            stripes.SetFill(new VectraPaint { type = PaintType.Stripes, color = Color.white, secondaryColor = new Color(0, 0, 0, 0), patternSize = 36, patternRotation = 30, patternRatio = 0.35f });

            // Frosted glass card.
            var glass = Shape<RectangleShape>("Frosted glass", new Vector2(-420, 180), new Vector2(520, 300));
            ((RectangleShape)glass.Geometry).cornerRadii = Vector4.one * 36f;
            glass.SetFill(new Color(1f, 1f, 1f, 0.35f));
            glass.SetStroke(new Color(1f, 1f, 1f, 0.45f), 2f, StrokeAlignment.Inside);
            glass.AddEffect<DropShadowEffect>().blur = 30f;
            glass.gameObject.AddComponent<VectraBackdrop>().Mode = BackdropMode.FrostedGlass;
            Label("Frosted glass", new Vector2(-420, 180), 30, Color.white);

            // One tile per blend mode.
            var modes = (BackdropMode[])System.Enum.GetValues(typeof(BackdropMode));
            int col = 0;
            for (int i = 1; i < modes.Length; i++, col++)
            {
                var pos = new Vector2(80 + (col % 5) * 170, 260 - (col / 5) * 130);
                var tile = Shape<RectangleShape>(modes[i].ToString(), pos, new Vector2(150, 100));
                ((RectangleShape)tile.Geometry).cornerRadii = Vector4.one * 18f;
                tile.SetFill(new Color(0.95f, 0.55f, 0.2f));
                tile.gameObject.AddComponent<VectraBackdrop>().Mode = modes[i];
                Label(modes[i].ToString(), pos + new Vector2(0, -64), 16, new Color(0.85f, 0.88f, 0.95f));
            }

            Label("Needs the Vectra Backdrop Feature on the URP renderer; the background is drawn by the base camera.", new Vector2(0, -470), 18,
                new Color(1f, 1f, 1f, 0.5f), 1400);
        }

        void Update()
        {
            float t = Time.time * 0.35f;
            for (int i = 0; i < m_Blobs.Length; i++)
            {
                if (m_Blobs[i] == null) continue;
                float a = t * (0.7f + i * 0.17f) + i * 1.3f;
                m_Blobs[i].anchoredPosition = new Vector2(Mathf.Cos(a) * (520f + i * 40f), Mathf.Sin(a * 1.4f) * (260f + i * 20f));
            }
        }

        VectraShape Shape<T>(string name, Vector2 pos, Vector2 size, Transform parent = null) where T : VectraShapeGeometry, new()
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
            go.transform.SetParent(parent != null ? parent : transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var s = go.GetComponent<VectraShape>();
            s.SetGeometry<T>();
            s.SetFill(Color.white);
            s.raycastTarget = false;
            return s;
        }

        void Label(string text, Vector2 pos, int size, Color color, float width = 400f)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width, size * 2f);
            rt.anchoredPosition = pos;
            var t = go.GetComponent<Text>();
            t.font = m_Font;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.text = text;
            t.raycastTarget = false;
        }
    }
}
