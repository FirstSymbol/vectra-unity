using System;
using FirstSymbol.Vectra.Svg;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.TextEngines
{
    /// <summary>What a backend needs to draw one SVG text element.</summary>
    public struct VectraTextSpec
    {
        public SvgElementData element;
        /// <summary>Font size in the parent's local units (document size times the fit scale).</summary>
        public float fontSize;
        /// <summary>The component tint (the Graphic colour).</summary>
        public Color tint;
        /// <summary>The element's fill times the tint.</summary>
        public Color color;
        /// <summary>The baseline origin of the first line, relative to the center of the parent rect.</summary>
        public Vector2 origin;
        /// <summary>Counter-clockwise rotation about the origin, in degrees.</summary>
        public float rotation;
        public VectraFontMap fontMap;
        /// <summary>The component's own font for this engine (used when the map has no match); may be null.</summary>
        public Object fallbackFont;
        /// <summary>World space only: the sorting order of this text, so it keeps its place among the layers of the document.</summary>
        public int sortingOrder;
    }

    /// <summary>
    /// Draws SVG text with one text engine. Backends live in their own assemblies and register themselves in
    /// <see cref="VectraTextBackends"/>; the core never references an engine.
    /// </summary>
    public interface IVectraTextBackend
    {
        VectraTextEngine Engine { get; }

        /// <summary>The asset type a font map field for this engine holds (null when the engine takes no font assets).</summary>
        Type FontAssetType { get; }

        /// <summary>Whether <paramref name="font"/> may be stored in this engine's font map fields.</summary>
        bool AcceptsFont(Object font);

        /// <summary>Whether text can be drawn now, with this map and fallback (for example a font resolves).</summary>
        bool IsUsable(VectraFontMap map, Object fallbackFont);

        /// <summary>Adds the engine's component to <paramref name="go"/> when missing and sets it up. Safe to call again.</summary>
        void Build(GameObject go, in VectraTextSpec spec);

        /// <summary>Whether <see cref="BuildWorld"/> works: the engine has text that needs no Canvas.</summary>
        bool SupportsWorld { get; }

        /// <summary>
        /// Like <see cref="Build"/> for text in world space (no Canvas). <see cref="VectraTextSpec.fontSize"/> is in world units and
        /// <see cref="VectraTextSpec.origin"/> is the baseline origin relative to the parent object, in world units.
        /// </summary>
        void BuildWorld(GameObject go, in VectraTextSpec spec);

        /// <summary>The string handed to the engine (markup included), for diagnostics and tests.</summary>
        string ReadText(GameObject go);
    }
}
