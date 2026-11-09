using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>Text engines in a running player loop: registration at startup, generated children, pixels in a render texture.</summary>
    public class TextEnginePlayModeTests
    {
        const int Size = 256;

        const string Svg =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200'>" +
            "<text x='100' y='110' font-size='60' fill='#ffffff' text-anchor='middle'>Hx<tspan fill='#ff0000'>g</tspan></text></svg>";

        readonly List<Object> m_Owned = new List<Object>();
        Camera m_Camera;
        Canvas m_Canvas;
        RenderTexture m_Target;
        VectraTextEngine m_Project;
        bool m_IgnoreFailing;

        [SetUp]
        public void SetUp()
        {
            m_Project = VectraRuntimeSettings.ProjectTextEngine;
            m_IgnoreFailing = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true; // TextMesh Pro without its Essential Resources complains; the pixels tell
            m_Target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            m_Owned.Add(m_Target);
            var camGo = new GameObject("Vectra Text Camera");
            m_Owned.Add(camGo);
            m_Camera = camGo.AddComponent<Camera>();
            m_Camera.orthographic = true;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color(0f, 0f, 0f, 1f);
            m_Camera.targetTexture = m_Target;
            m_Camera.transform.position = new Vector3(0f, 0f, -10f);
            var canvasGo = new GameObject("Vectra Text Canvas", typeof(Canvas), typeof(CanvasScaler));
            m_Owned.Add(canvasGo);
            m_Canvas = canvasGo.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            m_Canvas.worldCamera = m_Camera;
            m_Canvas.planeDistance = 5f;
        }

        [TearDown]
        public void TearDown()
        {
            VectraRuntimeSettings.ProjectTextEngine = m_Project;
            LogAssert.ignoreFailingMessages = m_IgnoreFailing;
            foreach (var o in m_Owned)
                if (o != null) Object.Destroy(o);
            m_Owned.Clear();
        }

        VectraSvgImage AddImage(VectraTextEngineChoice engine)
        {
            var doc = VectraSvg.Parse(Svg);
            m_Owned.Add(doc);
            var go = new GameObject("SVG", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraSvgImage));
            go.transform.SetParent(m_Canvas.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(Size, Size);
            var img = go.GetComponent<VectraSvgImage>();
            img.TextEngine = engine;
            img.Document = doc;
            return img;
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

        static int Bright(Color[] px) => px.Count(c => c.r + c.g + c.b > 1.5f);

        static IEnumerable<VectraTextEngine> Usable() =>
            new[] { VectraTextEngine.Lettera, VectraTextEngine.TextMeshPro, VectraTextEngine.Legacy }
                .Where(e => VectraTextBackends.Get(e) != null && VectraTextBackends.Get(e).IsUsable(null, null));

        [Test]
        public void TheBackends_RegisterThemselvesAtStartup()
        {
            Assert.IsTrue(VectraTextBackends.IsRegistered(VectraTextEngine.Legacy));
#if VECTRA_TEST_LETTERA
            Assert.IsTrue(VectraTextBackends.IsRegistered(VectraTextEngine.Lettera));
#else
            Assert.IsFalse(VectraTextBackends.IsRegistered(VectraTextEngine.Lettera));
#endif
#if VECTRA_NO_TMP
            Assert.IsFalse(VectraTextBackends.IsRegistered(VectraTextEngine.TextMeshPro));
#else
            Assert.IsTrue(VectraTextBackends.IsRegistered(VectraTextEngine.TextMeshPro));
#endif
        }

        [UnityTest]
        public IEnumerator AnImage_BuildsItsTextInTheNextFrame_WithTheProjectEngine()
        {
            VectraRuntimeSettings.ProjectTextEngine = VectraTextEngine.Legacy;
            var img = AddImage(VectraTextEngineChoice.UseProjectSetting);
            yield return null;
            yield return null;
            var texts = img.GetComponentsInChildren<Text>();
            Assert.AreEqual(1, texts.Length);
            StringAssert.Contains("<color=#FF0000FF>g</color>", texts[0].text);
        }

        [UnityTest]
        public IEnumerator SwitchingTheEngine_ReplacesTheGeneratedText()
        {
            var img = AddImage(VectraTextEngineChoice.Legacy);
            yield return null;
            yield return null;
            Assert.AreEqual(1, img.GetComponentsInChildren<Text>().Length);
            img.TextEngine = VectraTextEngineChoice.None;
            yield return null;
            yield return null;
            Assert.AreEqual(0, img.GetComponentsInChildren<Text>().Length);
            Assert.AreEqual(0, img.transform.childCount);
            img.TextEngine = VectraTextEngineChoice.Legacy;
            yield return null;
            yield return null;
            Assert.AreEqual(1, img.GetComponentsInChildren<Text>().Length);
        }

        [UnityTest]
        public IEnumerator EveryUsableEngine_DrawsWhitePixels_WhereTheTextIs()
        {
            foreach (var engine in Usable())
            {
                var img = AddImage((VectraTextEngineChoice)((int)engine + 1));
                yield return null;
                yield return null;
                yield return null;
                var px = Render();
                int bright = Bright(px);
                Debug.Log($"[text] {engine}: {bright} bright pixels");
                Assert.Greater(bright, 150, engine + " drew the white letters");
                // the letters sit around the centre of the 200x200 document, none in the top rows
                int topRows = 0;
                for (int y = Size - 30; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                        if (px[y * Size + x].r > 0.3f) topRows++;
                Assert.AreEqual(0, topRows, engine + ": nothing in the top rows");
                Object.Destroy(img.gameObject);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator AutoDrawsText_WhateverIsInstalled()
        {
            var img = AddImage(VectraTextEngineChoice.Auto);
            yield return null;
            yield return null;
            yield return null;
            Assert.Greater(img.transform.childCount, 0);
            Assert.Greater(Bright(Render()), 150);
        }
    }
}
