using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.VectraPerf
{
    /// <summary>Unity's com.unity.vectorgraphics, reached through this interface so that the harness compiles without it.</summary>
    public interface IVgBridge
    {
        /// <summary>Parses and tessellates an SVG into a sprite (what the package's importer does); null when it fails.</summary>
        Sprite BuildSprite(string svg);
        /// <summary>Only the parse step.</summary>
        bool Parse(string svg);
        /// <summary>An SVGImage showing the sprite.</summary>
        Graphic MakeImage(Transform parent, string name, Sprite sprite, Vector2 pos, Vector2 size);
        /// <summary>The sprite drawn into a texture of the given size (the caller destroys it).</summary>
        Texture2D Render(Sprite sprite, int width, int height);
    }

    public static class PerfBridge
    {
        public static IVgBridge Vg;
    }
}
