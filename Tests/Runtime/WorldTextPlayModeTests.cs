using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>
    /// SVG text in world space in a running player loop (Lettera's world text batches its meshes from the loop), drawn by every
    /// engine that can do it into a render texture and measured by pixels: where the ink lands against the baseline the SVG names,
    /// and that a shape after the text covers it. Set VECTRA_SHOTS to a folder to keep the pictures.
    /// </summary>
    public class WorldTextPlayModeTests
    {
        const int W = 800, H = 400;

        // 200 x 100 document units drawn 4 x 2 world units: one unit is 4 pixels. The baseline y = 70 is 280 pixels below the top.
        const string Svg =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 100'>" +
            "<rect width='200' height='100' fill='#101820'/>" +
            "<text x='20' y='70' font-size='60' fill='#ffffff'>HHHH</text></svg>";

        const string Covered =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 100'>" +
            "<rect width='200' height='100' fill='#101820'/>" +
            "<text x='20' y='70' font-size='60' fill='#ffffff'>HHHH</text>" +
            "<rect x='10' y='10' width='180' height='80' fill='#cc0000'/></svg>";

        bool m_IgnoreFailing;

        [SetUp]
        public void SetUp()
        {
            m_IgnoreFailing = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true; // TextMesh Pro without its Essential Resources complains; the pixels tell
        }

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = m_IgnoreFailing;

        static List<VectraTextEngine> Engines()
        {
            var list = new List<VectraTextEngine>();
            foreach (var e in new[] { VectraTextEngine.Lettera, VectraTextEngine.TextMeshPro })
            {
                var b = VectraTextBackends.Get(e);
                if (b != null && b.SupportsWorld && b.IsUsable(null, null)) list.Add(e);
            }

            return list;
        }

        static string ShotDir()
        {
            var dir = Environment.GetEnvironmentVariable("VECTRA_SHOTS");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "vectra_world_shots");
            Directory.CreateDirectory(dir);
            return dir;
        }

        // Draws the document in world space with one engine for a few frames; returns the pixels, bottom row first.
        static IEnumerator Draw(string svg, VectraTextEngine engine, Action<Color32[]> done)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var camGo = new GameObject("shot camera") { tag = "MainCamera" };
            var go = new GameObject("world svg");
            var doc = SvgParser.Parse(svg);
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 1f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.targetTexture = rt;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 100f;
                camGo.transform.position = new Vector3(0, 0, -10);
                var r = go.AddComponent<VectraSvgRenderer>();
                r.Height = 2f;
                r.TextEngine = (VectraTextEngineChoice)((int)engine + 1);
                r.Document = doc;
                for (int i = 0; i < 4; i++) yield return null;
                cam.Render();
                yield return null;
                cam.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(ShotDir(), "world_" + engine.ToString().ToLowerInvariant() + (svg == Covered ? "_covered" : "") + ".png"), tex.EncodeToPNG());
                var px = tex.GetPixels32();
                Object.Destroy(tex);
                done(px);
            }
            finally
            {
                Object.Destroy(go);
                Object.Destroy(camGo);
                Object.Destroy(doc);
                rt.Release();
            }
        }

        static bool Bright(Color32 c) => c.r + c.g + c.b > 3 * 150;

        static bool Ink(Color32[] px, out int minX, out int maxX, out int top, out int bottom)
        {
            minX = top = int.MaxValue;
            maxX = bottom = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!Bright(px[y * W + x])) continue;
                    int fromTop = H - 1 - y;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    top = Math.Min(top, fromTop);
                    bottom = Math.Max(bottom, fromTop);
                }

            return maxX >= 0;
        }

        [UnityTest]
        public IEnumerator TheBaseline_LandsOnTheSvgY_InEveryWorldEngine()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            var engines = Engines();
            if (engines.Count == 0) Assert.Ignore("no engine that draws in world space is usable in this project");
            foreach (var engine in engines)
            {
                Color32[] px = null;
                yield return Draw(Svg, engine, p => px = p);
                Assert.IsTrue(Ink(px, out int minX, out int maxX, out int top, out int bottom), engine + ": no text was drawn");
                Debug.Log($"VECTRA-METRIC world text {engine}: ink x {minX}..{maxX}, y {top}..{bottom} (baseline expected at 280, left edge about 80..140)");
                Assert.That(bottom, Is.InRange(262, 296), engine + ": the bottom of the letters sits on the baseline");
                Assert.That(top, Is.InRange(85, 150), engine + ": four letters of 60 units are about 170 pixels tall");
                Assert.That(minX, Is.InRange(70, 150), engine + ": the text starts at x = 20");
                Assert.Greater(maxX, 300, engine + ": four letters are wide");
            }
        }

        [UnityTest]
        public IEnumerator ARectangleAfterTheText_CoversIt_InWorldSpace()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            var engines = Engines();
            if (engines.Count == 0) Assert.Ignore("no engine that draws in world space is usable in this project");
            foreach (var engine in engines)
            {
                Color32[] px = null;
                yield return Draw(Covered, engine, p => px = p);
                int white = 0, red = 0;
                for (int y = 60; y < 340; y++)
                    for (int x = 60; x < 740; x++)
                    {
                        var c = px[y * W + x];
                        if (c.r > 230 && c.g > 230 && c.b > 230) white++;
                        else if (c.r > 150 && c.g < 60 && c.b < 60) red++;
                    }

                Debug.Log($"VECTRA-METRIC world z-order {engine}: white {white}, red {red}");
                Assert.AreEqual(0, white, engine + ": text drawn above a shape that follows it");
                Assert.Greater(red, 20000, engine.ToString());
            }
        }

        [UnityTest]
        public IEnumerator WithoutAnyWorldEngine_TheDocumentStillDrawsItsShapes()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            // The legacy engine cannot draw in world space: the text is skipped, the shapes remain.
            LogAssert.ignoreFailingMessages = true;
            Color32[] px = null;
            yield return Draw(Covered, VectraTextEngine.Legacy, p => px = p);
            int red = 0;
            for (int y = 60; y < 340; y++)
                for (int x = 60; x < 740; x++)
                    if (px[y * W + x].r > 150 && px[y * W + x].g < 60) red++;
            Assert.Greater(red, 20000, "the red block still shows");
        }
    }
}
