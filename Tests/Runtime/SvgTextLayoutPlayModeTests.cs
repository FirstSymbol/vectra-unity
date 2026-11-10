using System.Collections;
using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>Text as Figma exports it (one tspan per line, absolute x and y) lands where the SVG says, on a Canvas, with Lettera.</summary>
    public class SvgTextLayoutPlayModeTests
    {
        const int Size = 256;

        // Capital H sits on the baseline and has no overshoot, so the lowest ink row of a line is its baseline.
        const string Svg =
            "<svg xmlns='http://www.w3.org/2000/svg' width='256' height='256' viewBox='0 0 256 256'>" +
            "<text fill='#ffffff' style='white-space: pre' xml:space='preserve' font-size='20' letter-spacing='0em'>" +
            "<tspan x='30' y='50'>HHHH&#10;</tspan><tspan x='60' y='100'>HHHH&#10;</tspan><tspan x='20' y='140'>HHHH</tspan></text>" +
            "<text fill='#ffffff' xml:space='preserve' font-size='20' letter-spacing='0px'><tspan x='20' y='190'>HHHH</tspan></text>" +
            "<text fill='#ffffff' xml:space='preserve' font-size='20' letter-spacing='8px'><tspan x='20' y='230'>HHHH</tspan></text></svg>";

        readonly List<Object> m_Owned = new List<Object>();
        RenderTexture m_Target;
        Camera m_Camera;

        [SetUp]
        public void SetUp()
        {
            if (!VectraTextBackends.IsRegistered(VectraTextEngine.Lettera)) Assert.Ignore("Lettera is not installed");
            m_Target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            m_Owned.Add(m_Target);
            var camGo = new GameObject("Vectra Text Layout Camera");
            m_Owned.Add(camGo);
            m_Camera = camGo.AddComponent<Camera>();
            m_Camera.orthographic = true;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.black;
            m_Camera.targetTexture = m_Target;
            m_Camera.transform.position = new Vector3(0f, 0f, -10f);
            var canvasGo = new GameObject("Vectra Text Layout Canvas", typeof(Canvas));
            m_Owned.Add(canvasGo);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = m_Camera;
            canvas.planeDistance = 5f;
            var doc = VectraSvg.Parse(Svg);
            m_Owned.Add(doc);
            var go = new GameObject("SVG", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraSvgImage));
            go.transform.SetParent(canvas.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(Size, Size);
            var img = go.GetComponent<VectraSvgImage>();
            img.TextEngine = VectraTextEngineChoice.Lettera;
            img.Document = doc;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in m_Owned)
                if (o != null) Object.Destroy(o);
            m_Owned.Clear();
        }

        Color[] Render()
        {
            Canvas.ForceUpdateCanvases();
            m_Camera.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = m_Target;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var pixels = tex.GetPixels();
            Object.Destroy(tex);
            return pixels;
        }

        // Ink of the rows [top, bottom] (y from the top): lowest ink row and the first and last ink column.
        static bool Ink(Color[] px, int top, int bottom, out int lowest, out int left, out int right)
        {
            lowest = -1;
            left = int.MaxValue;
            right = -1;
            for (int y = top; y <= bottom; y++)
                for (int x = 0; x < Size; x++)
                {
                    var c = px[(Size - 1 - y) * Size + x];
                    if (c.r + c.g + c.b < 1.5f) continue;
                    lowest = y;
                    if (x < left) left = x;
                    if (x > right) right = x;
                }

            return lowest >= 0;
        }

        IEnumerator Settle()
        {
            for (int i = 0; i < 3; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator EveryLine_SitsOnItsOwnBaseline_AtItsOwnLeftEdge()
        {
            yield return Settle();
            var px = Render();
            var expected = new[] { (x: 30, y: 50), (x: 60, y: 100), (x: 20, y: 140), (x: 20, y: 190), (x: 20, y: 230) };
            foreach (var (x, y) in expected)
            {
                Assert.IsTrue(Ink(px, y - 24, y + 3, out int lowest, out int left, out _), $"ink for the line at {x},{y}");
                // the last ink row is the row above the baseline; the left edge is the side bearing of H (a pixel or two) in
                Assert.That(lowest, Is.InRange(y - 2, y + 1), $"baseline of the line at {x},{y}");
                Assert.That(left, Is.InRange(x - 1, x + 5), $"left edge of the line at {x},{y}");
            }
        }

        [UnityTest]
        public IEnumerator LetterSpacing_WidensTheLine_ByTheGapTimesTheGaps()
        {
            yield return Settle();
            var px = Render();
            Assert.IsTrue(Ink(px, 190 - 24, 190 + 3, out _, out int l0, out int r0));
            Assert.IsTrue(Ink(px, 230 - 24, 230 + 3, out _, out int l1, out int r1));
            // four letters: three gaps of 8px inside the ink (the gap after the last letter is not ink)
            Assert.That((r1 - l1) - (r0 - l0), Is.InRange(24 - 3, 24 + 3));
        }

        [UnityTest]
        public IEnumerator TheLineBreakAtTheEndOfAPiece_MovesNothing()
        {
            yield return Settle();
            var px = Render();
            // the first piece ends with &#10;; its ink must be 4 letters wide, not pushed or cut by a trailing character
            Assert.IsTrue(Ink(px, 50 - 24, 50 + 3, out _, out int l, out int r));
            Assert.IsTrue(Ink(px, 140 - 24, 140 + 3, out _, out int l2, out int r2));
            Assert.That(r - l, Is.EqualTo(r2 - l2).Within(2));
        }
    }
}
