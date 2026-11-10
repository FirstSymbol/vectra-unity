using System.IO;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>
    /// SVGs exported from Figma (Tests/FigmaCorpus~) rendered by the rasterizer and compared with Figma's own 2x render: a pixel counts as
    /// wrong when no pixel within one pixel of it in Figma's picture has a colour within 32 of it; the case passes under 2 % wrong pixels.
    /// </summary>
    public class FigmaLayerCorpusTests
    {
        static string Corpus()
        {
            string script = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FigmaLayerCorpusTests t:MonoScript")[0]);
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(script);
            string root = info != null ? info.resolvedPath : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script), ".."));
            return Path.Combine(root, info != null ? "Tests" : "", "FigmaCorpus~");
        }

        // The cases that become layers, and the ones whose masks must stay geometric clips.
        [TestCase("06_background_blur", true)]
        [TestCase("14_blend_multiply", true)]
        [TestCase("15_blend_screen", true)]
        [TestCase("16_fill_blend_mode", true)]
        [TestCase("17_group_opacity", true)]
        [TestCase("18_mask_alpha_gradient", true)]
        [TestCase("20_mask_luminance", true)]
        [TestCase("30_layer_opacity_fill_stroke", true)]
        [TestCase("19_mask_vector", false)]
        [TestCase("23_stroke_align", false)]
        [TestCase("29_per_side_strokes", false)]
        public void RasterizerMatchesFigma(string name, bool layered)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            string dir = Corpus();
            var doc = SvgParser.Parse(File.ReadAllText(Path.Combine(dir, "svg", name + ".svg")));
            var reference = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D ours = null;
            try
            {
                Assert.IsTrue(reference.LoadImage(File.ReadAllBytes(Path.Combine(dir, "figma", name + ".png"))));
                Assert.AreEqual(layered, doc.layers.Count > 0, name + (layered ? " should be composited" : " should stay a geometric clip"));
                ours = VectraRasterizer.Render(doc, reference.width, reference.height);
                double bad = WrongFraction(ours, reference);
                Assert.Less(bad, 0.02, $"{name}: {bad * 100:0.00} % of the pixels differ from Figma");
            }
            finally
            {
                Object.DestroyImmediate(doc);
                Object.DestroyImmediate(reference);
                if (ours != null) Object.DestroyImmediate(ours);
            }
        }

        static double WrongFraction(Texture2D ours, Texture2D figma)
        {
            int w = figma.width, h = figma.height;
            Assert.AreEqual((w, h), (ours.width, ours.height));
            var a = ours.GetPixels32();
            var b = figma.GetPixels32();
            var a2 = new Color32[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                // Over white, like the Figma export.
                var c = a[i];
                int inv = 255 - c.a;
                a2[i] = new Color32((byte)((c.r * c.a + 255 * inv) / 255), (byte)((c.g * c.a + 255 * inv) / 255), (byte)((c.b * c.a + 255 * inv) / 255), 255);
            }

            int wrong = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = a2[y * w + x];
                int best = int.MaxValue;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = Mathf.Clamp(x + dx, 0, w - 1), yy = Mathf.Clamp(y + dy, 0, h - 1);
                    var f = b[yy * w + xx];
                    int d = Mathf.Max(Mathf.Abs(c.r - f.r), Mathf.Max(Mathf.Abs(c.g - f.g), Mathf.Abs(c.b - f.b)));
                    if (d < best) best = d;
                }

                if (best > 32) wrong++;
            }

            return wrong / (double)(w * h);
        }
    }
}
