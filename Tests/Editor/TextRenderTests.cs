using System;
using System.Collections.Generic;
using System.IO;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>
    /// Draws SVG text with each engine the project can use into a render texture (never the screen) and looks at the pixels.
    /// Set VECTRA_SHOTS to a folder to keep the pictures.
    /// </summary>
    public class TextRenderTests
    {
        const int W = 800, H = 400; // twice the document

        const string Showcase =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 200'>" +
            "<rect width='400' height='200' fill='#1b2230'/>" +
            "<text x='20' y='44' font-size='30' fill='#ffffff'>Vectra text</text>" +
            "<text x='20' y='88' font-size='20' fill='#cfd8ea'>Hello <tspan font-weight='bold' fill='#ffb347'>bold orange</tspan> and " +
            "<tspan font-style='italic' fill='#7bd88f' font-size='28'>big italic</tspan></text>" +
            "<text x='380' y='124' font-size='18' text-anchor='end' fill='#ffd2d2'>right aligned</text>" +
            "<text x='200' y='158' font-size='18' text-anchor='middle' fill='#9ad0ff'>centred &lt;tag&gt; *star* _under_</text>" +
            "<text x='30' y='192' font-size='16' fill='#ffffff' transform='rotate(-8 30 192)'>rotated text</text></svg>";

        static IEnumerable<VectraTextEngine> Engines()
        {
            foreach (var e in new[] { VectraTextEngine.Lettera, VectraTextEngine.TextMeshPro, VectraTextEngine.Legacy })
            {
                var b = VectraTextBackends.Get(e);
                if (b != null && b.IsUsable(null, null)) yield return e;
            }
        }

        static string ShotDir()
        {
            var dir = Environment.GetEnvironmentVariable("VECTRA_SHOTS");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "vectra_text_shots");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Renders an SVG whose text uses <paramref name="engine"/>; returns the pixels, bottom row first.</summary>
        static Color32[] Render(string svg, VectraTextEngine engine, out Texture2D tex)
        {
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            var camGo = new GameObject("shot camera", typeof(Camera));
            var canvasGo = new GameObject("shot canvas", typeof(Canvas));
            var doc = SvgParser.Parse(svg);
            var previous = RenderTexture.active;
            try
            {
                var cam = camGo.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = H / 2f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.063f, 0.09f, 0.125f, 1f);
                cam.targetTexture = rt;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 1000f;
                camGo.transform.position = new Vector3(0, 0, -10);
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 5f;
                var go = new GameObject("svg", typeof(RectTransform));
                go.transform.SetParent(canvasGo.transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(W, H);
                var img = go.AddComponent<VectraSvgImage>();
                img.Document = doc;
                img.TextEngine = (VectraTextEngineChoice)(engine == VectraTextEngine.Auto ? 1 : (int)engine + 1);
                img.RebuildGenerated();
                for (int i = 0; i < 3; i++)
                {
                    Canvas.ForceUpdateCanvases();
                    cam.Render();
                }

                RenderTexture.active = rt;
                tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                return tex.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(canvasGo);
                Object.DestroyImmediate(doc);
            }
        }

        static bool Bright(Color32 c) => c.r + c.g + c.b > 3 * 150;

        /// <summary>Ink bounds (x/y in pixels, y from the top) of bright pixels, or false when there are none.</summary>
        static bool Ink(Color32[] px, out int minX, out int maxX, out int minY, out int maxY)
        {
            minX = minY = int.MaxValue;
            maxX = maxY = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!Bright(px[y * W + x])) continue;
                    int top = H - 1 - y;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (top < minY) minY = top;
                    if (top > maxY) maxY = top;
                }

            return maxX >= 0;
        }

        static string EngineName(VectraTextEngine e) => e.ToString().ToLowerInvariant();

        [Test]
        public void Showcase_IsDrawnByEveryEngine_AndSavedForLooking()
        {
            // TextMesh Pro complains when the project has no default font and the test has none to give; that is the
            // sandbox's business, the pictures show whether text appeared
            var ignore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                string tag = Environment.GetEnvironmentVariable("VECTRA_CELL") ?? "cell";
                int drawn = 0;
                foreach (var engine in Engines())
                {
                    var px = Render(Showcase, engine, out var tex);
                    File.WriteAllBytes(Path.Combine(ShotDir(), $"showcase_{tag}_{EngineName(engine)}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    int bright = 0;
                    foreach (var c in px)
                        if (Bright(c)) bright++;
                    TestContext.WriteLine($"{engine}: {bright} bright pixels");
                    Assert.Greater(bright, 800, engine + " drew text");
                    drawn++;
                }

                Assert.GreaterOrEqual(drawn, 1, "at least the legacy engine");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = ignore;
            }
        }

        [Test]
        public void TheSvgShowcaseSample_ShowsItsTitle_WithEveryEngine()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VectraSvgImage).Assembly);
            string file = Path.Combine(package.resolvedPath, "Samples~", "SvgShowcase", "VectraBadge.svg");
            Assert.IsTrue(File.Exists(file), file);
            string svg = File.ReadAllText(file);
            var ignore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                string tag = Environment.GetEnvironmentVariable("VECTRA_CELL") ?? "cell";
                foreach (var engine in Engines())
                {
                    var px = Render(svg, engine, out var tex);
                    File.WriteAllBytes(Path.Combine(ShotDir(), $"badge_{tag}_{EngineName(engine)}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    int different = 0;
                    var bg = px[0];
                    foreach (var c in px)
                        if (Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b) > 60) different++;
                    Assert.Greater(different, 2000, engine + " drew the badge");
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = ignore;
            }
        }

        [TestCase("start")]
        [TestCase("middle")]
        [TestCase("end")]
        public void TheBaselineAndTheAnchor_LandWhereTheSvgPutsThem(string anchor)
        {
            var ignore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                // 'HHHH' has no descender, so the bottom of the ink is the baseline: y = 120 * 2 = 240 px from the top
                string svg = $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 200'><text x='200' y='120' font-size='40' fill='#fff' text-anchor='{anchor}'>HHHH</text></svg>";
                foreach (var engine in Engines())
                {
                    var px = Render(svg, engine, out var tex);
                    string tag = Environment.GetEnvironmentVariable("VECTRA_CELL") ?? "cell";
                    File.WriteAllBytes(Path.Combine(ShotDir(), $"place_{tag}_{EngineName(engine)}_{anchor}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    Assert.IsTrue(Ink(px, out int minX, out int maxX, out int minY, out int maxY), engine + " drew something");
                    float size = 80f; // pixels
                    Assert.AreEqual(240f, maxY, size * 0.12f, engine + ": baseline row");
                    float expectedX = 400f; // x = 200 at twice the size
                    float at = anchor == "start" ? minX : anchor == "end" ? maxX : (minX + maxX) / 2f;
                    Assert.AreEqual(expectedX, at, size * 0.15f, engine + ": " + anchor + " edge");
                    // cap height of the text is about 0.7 of the size
                    Assert.AreEqual(size * 0.7f, maxY - minY, size * 0.2f, engine + ": cap height");
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = ignore;
            }
        }
    }
}
