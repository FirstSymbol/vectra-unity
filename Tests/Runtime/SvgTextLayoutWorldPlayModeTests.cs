using System.Collections;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>Pieces of one SVG text, each with its own x and y, land in their places in world space.</summary>
    public class SvgTextLayoutWorldPlayModeTests
    {
        const int W = 800, H = 400; // 200 x 100 document units over 4 x 2 world units: four pixels per unit

        const string Svg =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 100'><rect width='200' height='100' fill='#101820'/>" +
            "<text fill='#ffffff' xml:space='preserve' font-size='20'><tspan x='20' y='40'>HHHH&#10;</tspan><tspan x='100' y='80'>HHHH</tspan></text></svg>";

        bool m_IgnoreFailing;

        [SetUp]
        public void SetUp()
        {
            m_IgnoreFailing = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true; // TextMesh Pro without its Essential Resources complains; the pixels tell
        }

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = m_IgnoreFailing;

        static bool Ink(Color32[] px, int fromTop0, int fromTop1, out int lowest, out int left)
        {
            lowest = -1;
            left = int.MaxValue;
            for (int top = fromTop0; top <= fromTop1; top++)
                for (int x = 0; x < W; x++)
                {
                    var c = px[(H - 1 - top) * W + x];
                    if (c.r + c.g + c.b <= 3 * 150) continue;
                    lowest = top;
                    if (x < left) left = x;
                }

            return lowest >= 0;
        }

        [UnityTest]
        public IEnumerator EachPiece_SitsOnItsOwnBaseline_InEveryWorldEngine()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            bool any = false;
            foreach (var engine in new[] { VectraTextEngine.Lettera, VectraTextEngine.TextMeshPro })
            {
                var b = VectraTextBackends.Get(engine);
                if (b == null || !b.SupportsWorld || !b.IsUsable(null, null)) continue;
                any = true;
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                var camGo = new GameObject("shot camera") { tag = "MainCamera" };
                var go = new GameObject("world svg");
                var doc = SvgParser.Parse(Svg);
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
                    var px = tex.GetPixels32();
                    Object.Destroy(tex);
                    // baselines at 160 and 320 pixels from the top, left edges at 80 and 400
                    Assert.IsTrue(Ink(px, 60, 200, out int low1, out int left1), engine + ": first piece drawn");
                    Assert.IsTrue(Ink(px, 220, 380, out int low2, out int left2), engine + ": second piece drawn");
                    Assert.That(low1, Is.InRange(160 - 8, 160 + 4), engine + ": baseline of the first piece");
                    Assert.That(low2, Is.InRange(320 - 8, 320 + 4), engine + ": baseline of the second piece");
                    Assert.That(left1, Is.InRange(80 - 4, 80 + 20), engine + ": left edge of the first piece");
                    Assert.That(left2, Is.InRange(400 - 4, 400 + 20), engine + ": left edge of the second piece");
                }
                finally
                {
                    Object.Destroy(go);
                    Object.Destroy(camGo);
                    Object.Destroy(doc);
                    rt.Release();
                }
            }

            if (!any) Assert.Ignore("no engine that draws in world space is usable in this project");
        }
    }
}
