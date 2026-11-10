using System;
using System.Collections.Generic;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>How well the parser handles an SVG element.</summary>
    public enum SvgSupportLevel
    {
        /// <summary>Drawn or used as the specification says (within the notes of the support table).</summary>
        Supported,
        /// <summary>Handled with differences; the table in the documentation says which.</summary>
        Partial,
        /// <summary>Ignored. The import report names it.</summary>
        NotSupported
    }

    /// <summary>
    /// The elements the SVG parser knows and how well it handles each. The table "SVG support" in Vectra.md is checked against
    /// this list by a test; the parser reports every element marked <see cref="SvgSupportLevel.NotSupported"/> (or missing here)
    /// in the document's warnings, so the import report and the documentation cannot drift apart.
    /// </summary>
    public static class SvgSupport
    {
        static readonly Dictionary<string, SvgSupportLevel> s_Elements = Build();

        /// <summary>Every element name of the table with its level.</summary>
        public static IReadOnlyDictionary<string, SvgSupportLevel> Elements => s_Elements;

        /// <summary>The level of an element, or null when the element is not an SVG element Vectra knows about.</summary>
        public static SvgSupportLevel? Level(string name) => s_Elements.TryGetValue(name, out var level) ? level : (SvgSupportLevel?)null;

        static Dictionary<string, SvgSupportLevel> Build()
        {
            var d = new Dictionary<string, SvgSupportLevel>(StringComparer.Ordinal);
            void Add(SvgSupportLevel level, string names)
            {
                foreach (var n in names.Split(' ')) d[n] = level;
            }

            // Structure
            Add(SvgSupportLevel.Supported, "defs symbol a title desc metadata style marker");
            Add(SvgSupportLevel.Partial, "svg g use switch");
            Add(SvgSupportLevel.NotSupported, "script view");
            // Shapes
            Add(SvgSupportLevel.Supported, "path rect circle ellipse line polyline polygon");
            // Paint servers
            Add(SvgSupportLevel.Supported, "linearGradient stop");
            Add(SvgSupportLevel.Partial, "radialGradient pattern");
            Add(SvgSupportLevel.NotSupported, "solidcolor hatch hatchpath meshgradient meshrow meshpatch");
            // Clipping, masking, markers
            Add(SvgSupportLevel.Partial, "clipPath mask");
            // Text
            Add(SvgSupportLevel.Partial, "text tspan textPath");
            Add(SvgSupportLevel.NotSupported, "tref altGlyph altGlyphDef altGlyphItem glyphRef textArea");
            // Images and embedded content
            Add(SvgSupportLevel.Partial, "image foreignObject");
            Add(SvgSupportLevel.NotSupported, "video audio canvas iframe");
            // Filters
            Add(SvgSupportLevel.Partial, "filter feGaussianBlur feOffset feFlood feMerge feMergeNode feDropShadow feColorMatrix");
            Add(SvgSupportLevel.NotSupported,
                "feBlend feComponentTransfer feComposite feConvolveMatrix feDiffuseLighting feDisplacementMap feImage feMorphology " +
                "feSpecularLighting feTile feTurbulence feFuncR feFuncG feFuncB feFuncA feDistantLight fePointLight feSpotLight");
            // Animation: the first frame (the attribute values as written) is drawn
            Add(SvgSupportLevel.NotSupported, "animate animateColor animateMotion animateTransform set mpath discard");
            // SVG 1.1 fonts, colour profiles, cursors, scripting hooks
            Add(SvgSupportLevel.NotSupported,
                "font font-face font-face-src font-face-uri font-face-format font-face-name glyph missing-glyph hkern vkern " +
                "color-profile cursor handler listener prefetch");
            return d;
        }
    }
}
