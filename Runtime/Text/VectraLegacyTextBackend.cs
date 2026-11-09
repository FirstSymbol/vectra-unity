using System;
using FirstSymbol.Vectra.Svg;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.TextEngines
{
    /// <summary>
    /// The last resort: uGUI <c>Text</c> with the built-in font. It needs nothing installed, so SVG text always shows
    /// something. Font families are ignored; bold and italic come from the font style.
    /// </summary>
    sealed class VectraLegacyTextBackend : IVectraTextBackend
    {
        // The built-in font has a descent of about a fifth of its size.
        const float DescentEm = 0.21f;
        static Font s_Font;

        public VectraTextEngine Engine => VectraTextEngine.Legacy;
        public Type FontAssetType => null;
        public bool AcceptsFont(Object font) => false;
        public bool IsUsable(VectraFontMap map, Object fallbackFont) => true;

        // uGUI Text only draws inside a Canvas, so it has no world-space mode.
        public bool SupportsWorld => false;
        public void BuildWorld(GameObject go, in VectraTextSpec spec) { }

        static Font BuiltIn()
        {
            if (s_Font == null) s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return s_Font;
        }

        public void Build(GameObject go, in VectraTextSpec spec)
        {
            var e = spec.element;
            var t = go.GetComponent<Text>();
            if (t == null) t = go.AddComponent<Text>();
            float scale = e.fontSize > 1e-4f ? spec.fontSize / e.fontSize : 1f;
            t.font = BuiltIn();
            t.supportRichText = true;
            t.text = VectraRichText.Build(e, VectraMarkupDialect.Legacy, spec.tint, scale, e.bold, e.italic);
            t.fontSize = Mathf.Max(1, Mathf.RoundToInt(spec.fontSize));
            t.fontStyle = e.bold && e.italic ? FontStyle.BoldAndItalic : e.bold ? FontStyle.Bold : e.italic ? FontStyle.Italic : FontStyle.Normal;
            t.color = spec.color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.alignment = e.anchor == SvgTextAnchor.Middle ? TextAnchor.LowerCenter : e.anchor == SvgTextAnchor.End ? TextAnchor.LowerRight : TextAnchor.LowerLeft;
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(e.anchor == SvgTextAnchor.Middle ? 0.5f : e.anchor == SvgTextAnchor.End ? 1f : 0f, 0f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Mathf.Max(spec.fontSize, 1f), spec.fontSize * 1.4f);
            rt.anchoredPosition = new Vector2(spec.origin.x, spec.origin.y - spec.fontSize * DescentEm);
            rt.localEulerAngles = new Vector3(0, 0, spec.rotation);
        }

        public string ReadText(GameObject go)
        {
            var t = go != null ? go.GetComponent<Text>() : null;
            return t != null ? t.text : null;
        }
    }
}
