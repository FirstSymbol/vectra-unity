using System;
using FirstSymbol.Lettera;
using FirstSymbol.Vectra.Svg;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.TextEngines
{
    /// <summary>
    /// Draws SVG text with Lettera. tspans with their own bold, italic, colour or size become Lettera markup inside
    /// one text. Registers itself while Lettera 1.0 or newer is installed.
    /// </summary>
    sealed class VectraLetteraBackend : IVectraTextBackend
    {
        public VectraTextEngine Engine => VectraTextEngine.Lettera;
        public Type FontAssetType => typeof(ScriptableObject);
        public bool AcceptsFont(Object font) => font is LetteraFontAsset || font is LetteraFontStack;
        public bool IsUsable(VectraFontMap map, Object fallbackFont) => true;

        public void Build(GameObject go, in VectraTextSpec spec)
        {
            var e = spec.element;
            var t = go.GetComponent<LetteraText>();
            if (t == null) t = go.AddComponent<LetteraText>();
            bool exactBold = false;
            Object resolved = spec.fontMap != null ? spec.fontMap.Resolve(VectraTextEngine.Lettera, e.fontFamily, e.bold, out exactBold) : null;
            if (resolved == null) resolved = spec.fallbackFont;
            t.Font = resolved as LetteraFontAsset;
            t.FontStack = resolved as LetteraFontStack;
            float scale = e.fontSize > 1e-4f ? spec.fontSize / e.fontSize : 1f;
            t.Text = VectraRichText.Build(e, VectraMarkupDialect.Lettera, spec.tint, scale, exactBold, false);
            t.FontSize = Mathf.Max(spec.fontSize, 0.01f);
            t.color = spec.color;
            t.WordWrap = false;
            t.Overflow = LetteraOverflow.Visible;
            t.AutoSize = false;
            t.Padding = Vector4.zero;
            t.raycastTarget = false;
            t.HorizontalAlignment = e.anchor == SvgTextAnchor.Middle ? LetteraHorizontalAlignment.Center :
                e.anchor == SvgTextAnchor.End ? LetteraHorizontalAlignment.Right : LetteraHorizontalAlignment.Left;
            t.VerticalAlignment = LetteraVerticalAlignment.Top;

            // The rect is as wide as the text and hangs from its top edge; the pivot sits on the baseline origin.
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            float pivotX = e.anchor == SvgTextAnchor.Middle ? 0.5f : e.anchor == SvgTextAnchor.End ? 1f : 0f;
            float width = Mathf.Max(t.GetMaxContentWidth(), 1f);
            rt.pivot = new Vector2(pivotX, 1f);
            rt.sizeDelta = new Vector2(width, spec.fontSize * 2f);
            t.ForceRebuild();
            float down = BaselineBelowTop(t, spec.fontSize);
            rt.anchoredPosition = new Vector2(spec.origin.x, spec.origin.y + down);
            rt.localEulerAngles = new Vector3(0, 0, spec.rotation);
        }

        // Distance from the top edge of the text rect to the baseline of its first line.
        static float BaselineBelowTop(LetteraText t, float fontSize)
        {
            var lines = t.Lines;
            if (lines == null || lines.Count == 0) return fontSize * 0.8f;
            return Mathf.Abs(lines[0].Baseline);
        }

        public string ReadText(GameObject go)
        {
            var t = go != null ? go.GetComponent<LetteraText>() : null;
            return t != null ? t.Text : null;
        }
    }

    static class VectraLetteraRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RegisterInPlayer() => Register();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif

        static void Register() => VectraTextBackends.Register(new VectraLetteraBackend());
    }
}
