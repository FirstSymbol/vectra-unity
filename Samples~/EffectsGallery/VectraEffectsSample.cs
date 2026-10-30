using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Vectra.Samples
{
    /// <summary>
    /// One card per effect, plus masks and a palette with a theme switch (click the button).
    /// Everything is created from code at startup under this RectTransform.
    /// </summary>
    public class VectraEffectsSample : MonoBehaviour
    {
        [SerializeField] float m_Cell = 215f;
        [SerializeField] int m_Columns = 6;

        VectraPalette m_Palette;
        VectraTheme m_Theme;
        VectraShape m_Dissolve, m_Glow;
        Font m_Font;

        void Start()
        {
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();

            // A palette with a light and a dark theme; the cards use its colours through tokens.
            m_Palette = ScriptableObject.CreateInstance<VectraPalette>();
            m_Palette.SetColor("Card", 0, new Color(0.93f, 0.94f, 0.97f));
            m_Palette.SetColor("Card", 1, new Color(0.16f, 0.17f, 0.22f));
            m_Palette.SetColor("Accent", 0, new Color(0.2f, 0.45f, 1f));
            m_Palette.SetColor("Accent", 1, new Color(0.55f, 0.75f, 1f));
            m_Palette.SetColor("Ink", 0, new Color(0.12f, 0.13f, 0.18f));
            m_Palette.SetColor("Ink", 1, new Color(0.9f, 0.92f, 0.96f));
            m_Theme = gameObject.AddComponent<VectraTheme>();
            m_Theme.Palette = m_Palette;

            var cards = new List<(string label, Action<VectraShape> build)>
            {
                ("Drop Shadow", s => { s.AddEffect<DropShadowEffect>().blur = 16f; }),
                ("Inner Shadow", s => { s.AddEffect<InnerShadowEffect>().blur = 8f; }),
                ("Outer Glow", s => { m_Glow = s; s.AddEffect<OuterGlowEffect>().size = 18f; }),
                ("Inner Glow", s => { s.AddEffect<InnerGlowEffect>(); }),
                ("Bevel & Emboss", s => { s.AddEffect<BevelEffect>().size = 10f; }),
                ("Layer Blur", s => { s.AddEffect<LayerBlurEffect>().blur = 10f; }),
                ("Noise / Grain", s => { s.AddEffect<GrainEffect>().amount = 0.3f; }),
                ("Color Adjust", s => { var c = s.AddEffect<ColorAdjustEffect>(); c.hueShift = 120f; c.saturation = 1.4f; }),
                ("Dissolve", s => { m_Dissolve = s; s.AddEffect<DissolveEffect>(); }),
                ("Pixelate", s => { s.AddEffect<PixelateEffect>().cellSize = 10f; }),
                ("Outline", s => { var o = s.AddEffect<OutlineEffect>(); o.color = new Color(1f, 0.85f, 0.3f); o.width = 3f; o.offset = 6f; }),
                ("Gradient Overlay", s => { s.AddEffect<GradientOverlayEffect>(); }),
            };

            for (int i = 0; i < cards.Count; i++)
            {
                var pos = new Vector2((i % m_Columns - (m_Columns - 1) * 0.5f) * m_Cell, 250f - (i / m_Columns) * m_Cell);
                var panel = Shape<RectangleShape>(transform, cards[i].label + " card", pos + new Vector2(0, -14), new Vector2(196, 196));
                ((RectangleShape)panel.Geometry).cornerRadii = Vector4.one * 22f;
                panel.Fills[0].paint.colorToken = "Card";
                var shape = Shape<StarShape>(transform, cards[i].label, pos, new Vector2(130, 130));
                shape.Fills[0].paint.colorToken = "Accent";
                cards[i].build(shape);
                Label(cards[i].label, pos + new Vector2(0, -88)).color = new Color(0.45f, 0.5f, 0.6f);
            }

            // Masks: a soft mask feathers its children, an inverted one punches a soft hole, a stencil mask
            // clips any UI graphic to the exact outline.
            var soft = Shape<RectangleShape>(transform, "Soft Mask", new Vector2(-440, -230), new Vector2(300, 150));
            ((RectangleShape)soft.Geometry).cornerRadii = Vector4.one * 75f;
            soft.Fills[0].paint.colorToken = "Card";
            var sm = soft.gameObject.AddComponent<VectraMask>();
            sm.Mode = VectraMaskMode.Soft;
            sm.Softness = 30f;
            for (int i = 0; i < 7; i++)
            {
                var dot = Shape<EllipseShape>(soft.transform, "Dot", new Vector2(-180 + i * 60, (i % 2) * 50 - 25), new Vector2(100, 100));
                dot.SetFill(Color.HSVToRGB(i / 7f, 0.75f, 1f));
            }

            Label("Soft mask", new Vector2(-440, -330));

            var hole = Shape<EllipseShape>(transform, "Inverted Soft Mask", new Vector2(0, -230), new Vector2(110, 110));
            hole.SetFill(Color.clear);
            var hm = hole.gameObject.AddComponent<VectraMask>();
            hm.Mode = VectraMaskMode.Soft;
            hm.Invert = true;
            hm.Softness = 20f;
            var under = Shape<RectangleShape>(hole.transform, "Panel", Vector2.zero, new Vector2(260, 160));
            under.SetFill(VectraPaint.Linear(new Color(0.3f, 0.9f, 0.6f), new Color(0.2f, 0.5f, 1f), 30f));
            Label("Inverted soft mask", new Vector2(0, -330));

            var stencil = Shape<StarShape>(transform, "Stencil Mask", new Vector2(440, -230), new Vector2(170, 170));
            stencil.gameObject.AddComponent<VectraMask>().showMaskGraphic = false;
            var img = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            img.transform.SetParent(stencil.transform, false);
            ((RectTransform)img.transform).sizeDelta = new Vector2(220, 220);
            img.GetComponent<RawImage>().texture = MakeChecker();
            Label("Stencil mask (any graphic)", new Vector2(440, -330));

            // Theme switch.
            var button = Shape<RectangleShape>(transform, "Theme Button", new Vector2(0, 420), new Vector2(240, 56));
            ((RectangleShape)button.Geometry).cornerRadii = Vector4.one * 28f;
            button.Fills[0].paint.colorToken = "Accent";
            var b = button.gameObject.AddComponent<Button>();
            b.targetGraphic = button;
            b.onClick.AddListener(() => m_Theme.NextTheme());
            var text = Label("Switch theme", Vector2.zero, button.transform);
            text.color = Color.white;
        }

        void Update()
        {
            if (m_Dissolve != null)
            {
                m_Dissolve.GetEffect<DissolveEffect>().amount = Mathf.PingPong(Time.time * 0.4f, 0.9f);
                m_Dissolve.SetShapeDirty();
            }

            if (m_Glow != null)
            {
                m_Glow.GetEffect<OuterGlowEffect>().size = 12f + Mathf.Sin(Time.time * 3f) * 6f;
                m_Glow.SetShapeDirty();
            }
        }

        void OnDestroy()
        {
            if (m_Palette != null) Destroy(m_Palette);
        }

        static VectraShape Shape<T>(Transform parent, string name, Vector2 pos, Vector2 size) where T : VectraShapeGeometry, new()
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var s = go.GetComponent<VectraShape>();
            s.SetGeometry<T>();
            s.SetFill(Color.white);
            return s;
        }

        Text Label(string text, Vector2 pos, Transform parent = null)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent != null ? parent : transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(m_Cell, 30);
            rt.anchoredPosition = pos;
            var t = go.GetComponent<Text>();
            t.font = m_Font;
            t.fontSize = 18;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(0.6f, 0.64f, 0.72f);
            t.text = text;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Adds an EventSystem with the input module of the active input handling (new Input System or legacy).</summary>
        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            var module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            go.AddComponent(module ?? typeof(UnityEngine.EventSystems.StandaloneInputModule));
        }

        static Texture2D MakeChecker()
        {
            var tex = new Texture2D(8, 8) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                tex.SetPixel(x, y, ((x + y) & 1) == 0 ? new Color(1f, 0.4f, 0.5f) : new Color(1f, 0.85f, 0.3f));
            tex.Apply();
            return tex;
        }
    }
}
