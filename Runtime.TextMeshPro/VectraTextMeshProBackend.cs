using System;
using FirstSymbol.Vectra.Svg;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.TextEngines
{
    /// <summary>Draws SVG text with TextMesh Pro (the way Vectra 0.2 did). Registers itself while TextMesh Pro is present.</summary>
    sealed class VectraTextMeshProBackend : IVectraTextBackend
    {
        public VectraTextEngine Engine => VectraTextEngine.TextMeshPro;
        public Type FontAssetType => typeof(TMP_FontAsset);
        public bool AcceptsFont(Object font) => font is TMP_FontAsset;

        // TextMesh Pro opens its importer window when asked for settings it does not have, so look the asset up by name first.
        static bool HasDefaultFont() =>
            Resources.Load<ScriptableObject>("TMP Settings") != null && TMP_Settings.defaultFontAsset != null;

        public bool IsUsable(VectraFontMap map, Object fallbackFont) =>
            fallbackFont is TMP_FontAsset || (map != null && map.HasFonts(VectraTextEngine.TextMeshPro)) || HasDefaultFont();

        public void Build(GameObject go, in VectraTextSpec spec)
        {
            var e = spec.element;
            var t = go.GetComponent<TextMeshProUGUI>();
            if (t == null) t = go.AddComponent<TextMeshProUGUI>();
            bool exactBold = false;
            Object resolved = spec.fontMap != null ? spec.fontMap.Resolve(VectraTextEngine.TextMeshPro, e.fontFamily, e.bold, out exactBold) : null;
            var font = (resolved as TMP_FontAsset) ?? (spec.fallbackFont as TMP_FontAsset);
            if (font != null) t.font = font;
            float scale = e.fontSize > 1e-4f ? spec.fontSize / e.fontSize : 1f;
            t.text = VectraRichText.Build(e, VectraMarkupDialect.TextMeshPro, spec.tint, scale, e.bold, e.italic);
            t.fontSize = spec.fontSize;
            t.color = spec.color;
            t.fontStyle = (e.bold && !exactBold ? FontStyles.Bold : 0) | (e.italic ? FontStyles.Italic : 0);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            var rt = (RectTransform)t.transform;
            float pivotX = e.anchor == SvgTextAnchor.Middle ? 0.5f : e.anchor == SvgTextAnchor.End ? 1f : 0f;
            t.alignment = e.anchor == SvgTextAnchor.Middle ? TextAlignmentOptions.Baseline :
                e.anchor == SvgTextAnchor.End ? TextAlignmentOptions.BaselineRight : TextAlignmentOptions.BaselineLeft;
            // SVG y is the baseline. TextMesh Pro's Baseline alignment puts the first baseline on the vertical middle of the
            // box, so the pivot goes there and the box turns about the origin. (0.2 assumed the bottom and drew text half a size too high.)
            rt.pivot = new Vector2(pivotX, 0.5f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(t.preferredWidth + 4f, t.fontSize * 1.5f);
            rt.anchoredPosition = new Vector2(spec.origin.x, spec.origin.y);
            rt.localEulerAngles = new Vector3(0, 0, spec.rotation);
        }

        public string ReadText(GameObject go)
        {
            var t = go != null ? go.GetComponent<TextMeshProUGUI>() : null;
            return t != null ? t.text : null;
        }
    }

    [UnityEngine.Scripting.Preserve]
    static class VectraTextMeshProRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration), UnityEngine.Scripting.Preserve]
        static void RegisterInPlayer() => Register();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif

        static void Register() => VectraTextBackends.Register(new VectraTextMeshProBackend());
    }
}
