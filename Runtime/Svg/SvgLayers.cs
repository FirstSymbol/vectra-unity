using System;
using System.Collections.Generic;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>CSS mix-blend-mode values (Compositing and Blending Level 1). The order matches the composite shader.</summary>
    public enum SvgBlendMode
    {
        Normal,
        Multiply,
        Screen,
        Overlay,
        Darken,
        Lighten,
        ColorDodge,
        ColorBurn,
        HardLight,
        SoftLight,
        Difference,
        Exclusion,
        Hue,
        Saturation,
        Color,
        Luminosity,
        PlusLighter
    }

    public enum SvgMaskKind
    {
        None,
        /// <summary>The mask value is the luminance of the mask content times its alpha (the SVG default).</summary>
        Luminance,
        /// <summary>The mask value is the alpha of the mask content (<c>mask-type: alpha</c>).</summary>
        Alpha
    }

    /// <summary>
    /// A subtree of the document that is drawn on its own and then composited: group opacity, a mask that is not a plain
    /// white shape, mix-blend-mode, or a background blur. Covers the elements [from, to) of <see cref="VectraSvgDocument.elements"/>.
    /// </summary>
    [Serializable]
    public class SvgLayerData
    {
        public int from, to;
        /// <summary>Index in <see cref="VectraSvgDocument.layers"/> of the layer that encloses this one, or -1.</summary>
        public int parent = -1;
        public float opacity = 1f;
        public SvgBlendMode blend;
        public SvgMaskKind mask;
        /// <summary>The mask content: [maskFrom, maskTo) of <see cref="VectraSvgDocument.maskElements"/>.</summary>
        public int maskFrom, maskTo;
        /// <summary>
        /// Background blur (standard deviation in document units). The blurred content below is drawn under the layer, limited to
        /// the mask elements (the silhouette of the shape).
        /// </summary>
        public float blurSigma;

        /// <summary>The layer reads what was drawn below it (blend modes other than normal, background blur).</summary>
        public bool NeedsBackdrop => blend != SvgBlendMode.Normal || blurSigma > 0f || FilterNeedsBackdrop;

        /// <summary>
        /// The filter of the layer (Figma shadow chains, layer blur) in paint order; computed from the picture of the layer's subtree. Null
        /// for layers without one.
        /// </summary>
        public List<SvgFilterStep> filter;

        /// <summary>A drop shadow of the filter blends with the backdrop (the content drawn below the layer) in a mode other than normal.</summary>
        public bool FilterNeedsBackdrop
        {
            get
            {
                if (filter == null) return false;
                for (int i = 0; i < filter.Count; i++)
                    if (filter[i].kind == SvgFilterKind.DropShadow && filter[i].blend != SvgBlendMode.Normal) return true;
                return false;
            }
        }
    }

    public enum SvgFilterKind
    {
        /// <summary>The picture of the layer itself (SourceGraphic) goes over what the steps before it made.</summary>
        Source,
        /// <summary>A drop shadow from the alpha of the layer picture: spread, offset, blur, colour; drawn below the Source step.</summary>
        DropShadow,
        /// <summary>An inner shadow from the alpha of the layer picture: choke, offset, blur, colour; blended over what the steps before it made.</summary>
        InnerShadow,
        /// <summary>A Gaussian blur of everything made so far.</summary>
        Blur
    }

    /// <summary>
    /// One step of a filter that is applied to the picture of a layer. Lengths are in document units (already scaled by the element's
    /// transform), the offset points down the page, <see cref="sigma"/> is the standard deviation of the Gaussian.
    /// </summary>
    [Serializable]
    public class SvgFilterStep
    {
        public SvgFilterKind kind;
        /// <summary>Straight colour with its alpha, as the SVG writes it (sRGB values).</summary>
        public UnityEngine.Color color = new UnityEngine.Color(0f, 0f, 0f, 0.25f);
        public float dx, dy, sigma, spread, choke;
        /// <summary>
        /// How a shadow is composited: a drop shadow against the backdrop below the layer, an inner shadow against the layer's own picture.
        /// </summary>
        public SvgBlendMode blend;
    }

    /// <summary>Reads the layer tree of a document and maps CSS blend-mode names.</summary>
    public static class SvgLayers
    {
        public static bool HasLayers(VectraSvgDocument doc) => doc != null && doc.layers != null && doc.layers.Count > 0;

        public static SvgBlendMode ParseBlend(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "multiply": return SvgBlendMode.Multiply;
                case "screen": return SvgBlendMode.Screen;
                case "overlay": return SvgBlendMode.Overlay;
                case "darken": return SvgBlendMode.Darken;
                case "lighten": return SvgBlendMode.Lighten;
                case "color-dodge": return SvgBlendMode.ColorDodge;
                case "color-burn": return SvgBlendMode.ColorBurn;
                case "hard-light": return SvgBlendMode.HardLight;
                case "soft-light": return SvgBlendMode.SoftLight;
                case "difference": return SvgBlendMode.Difference;
                case "exclusion": return SvgBlendMode.Exclusion;
                case "hue": return SvgBlendMode.Hue;
                case "saturation": return SvgBlendMode.Saturation;
                case "color": return SvgBlendMode.Color;
                case "luminosity": return SvgBlendMode.Luminosity;
                case "plus-lighter": return SvgBlendMode.PlusLighter;
                default: return SvgBlendMode.Normal;
            }
        }

        /// <summary>
        /// Fills <paramref name="into"/> (cleared first) with the layers whose parent is <paramref name="parent"/> and that lie inside
        /// [from, to), in paint order.
        /// </summary>
        public static void Children(VectraSvgDocument doc, int parent, int from, int to, List<int> into)
        {
            into.Clear();
            if (!HasLayers(doc)) return;
            var layers = doc.layers;
            for (int i = 0; i < layers.Count; i++)
            {
                var l = layers[i];
                if (l == null || l.parent != parent || l.to <= l.from || l.from < from || l.to > to) continue;
                int at = into.Count;
                while (at > 0 && layers[into[at - 1]].from > l.from) at--;
                into.Insert(at, i);
            }
        }
    }
}
