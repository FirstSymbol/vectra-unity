using System;
using UnityEngine;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>How SVG documents blend colours in a project that uses the Linear colour space.</summary>
    public enum SvgColorBlending
    {
        /// <summary>Like browsers and Figma: translucency, anti-aliasing and gradients work on sRGB values (the default).</summary>
        Browser = 0,
        /// <summary>Like the rest of Unity in a Linear project: blending in linear light.</summary>
        Linear = 1
    }

    /// <summary>What a component asks for: the project setting, or its own choice.</summary>
    public enum SvgColorBlendingChoice
    {
        UseProjectSetting = 0,
        Browser = 1,
        Linear = 2
    }

    /// <summary>
    /// The colour space Vectra builds colours in. In a Linear project an SVG document set to <see cref="SvgColorBlending.Browser"/> is
    /// drawn inside a <see cref="Gamma"/> scope: colours and gradients stay sRGB-encoded and everything is drawn into a texture that is
    /// not sRGB, so blending happens on sRGB values exactly as in a Gamma project; the finished picture is then shown once. Gamma
    /// projects never enter the scope and cost nothing.
    /// </summary>
    public static class VectraColorSpace
    {
        static int s_GammaDepth, s_Locked;

        /// <summary>The project's colour space is Linear and no gamma scope is open: colours are built in linear light.</summary>
        public static bool Linear => s_GammaDepth == 0 && QualitySettings.activeColorSpace == ColorSpace.Linear;

        /// <summary>A gamma scope is open in a Linear project.</summary>
        public static bool InGammaScope => s_GammaDepth > 0 && QualitySettings.activeColorSpace == ColorSpace.Linear;

        /// <summary>The project's choice for SVG documents (set by the project settings or the preloaded settings asset).</summary>
        public static SvgColorBlending ProjectBlending { get; set; } = SvgColorBlending.Browser;

        /// <summary>
        /// Whether a document is drawn in the gamma scope: a Linear project, the choice resolves to <see cref="SvgColorBlending.Browser"/>,
        /// and no scope is open already (nested drawing, such as a pattern tile, follows the outer one).
        /// </summary>
        public static bool UseGamma(SvgColorBlendingChoice choice = SvgColorBlendingChoice.UseProjectSetting)
        {
            if (s_GammaDepth > 0 || s_Locked > 0 || QualitySettings.activeColorSpace != ColorSpace.Linear) return false;
            switch (choice)
            {
                case SvgColorBlendingChoice.Browser: return true;
                case SvgColorBlendingChoice.Linear: return false;
                default: return ProjectBlending == SvgColorBlending.Browser;
            }
        }

        public static Scope EnterGamma() => new Scope(true);

        /// <summary>Until disposed, <see cref="UseGamma"/> is false: drawing that is part of another drawing (a pattern tile) keeps the colour space of the outer one.</summary>
        public static Lock KeepCurrent() => new Lock(true);

        public readonly struct Lock : IDisposable
        {
            readonly bool m_Active;

            internal Lock(bool active)
            {
                m_Active = active;
                if (active) s_Locked++;
            }

            public void Dispose()
            {
                if (m_Active) s_Locked--;
            }
        }

        /// <summary>Opens a gamma scope (when asked) until disposed.</summary>
        public readonly struct Scope : IDisposable
        {
            readonly bool m_Active;

            internal Scope(bool active)
            {
                m_Active = active;
                if (active) s_GammaDepth++;
            }

            public void Dispose()
            {
                if (m_Active) s_GammaDepth--;
            }
        }
    }
}
