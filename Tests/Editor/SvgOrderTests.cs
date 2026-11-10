using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>Paint order of text and shapes, children that stay put, complete cache keys and styles of &lt;use&gt; targets.</summary>
    public class SvgOrderTests
    {
        // shape, text, shape, shape, text, shape
        const string Sandwich =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 200'>" +
            "<rect width='400' height='200' fill='#223'/>" +
            "<text x='20' y='60' font-size='24' fill='#fff'>one</text>" +
            "<rect x='10' y='10' width='50' height='50' fill='#f00'/>" +
            "<circle cx='200' cy='100' r='30' fill='#0f0'/>" +
            "<text x='20' y='120' font-size='24' fill='#fff'>two</text>" +
            "<rect x='100' y='150' width='50' height='20' fill='#00f'/></svg>";

        readonly List<Object> m_Objects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in m_Objects)
                if (o != null) Object.DestroyImmediate(o);
            m_Objects.Clear();
        }

        VectraSvgDocument Parse(string svg)
        {
            var doc = SvgParser.Parse(svg);
            m_Objects.Add(doc);
            return doc;
        }

        // --- paint order -----------------------------------------------------------------------------------

        [Test]
        public void Split_KeepsDocumentOrder()
        {
            var doc = Parse(Sandwich);
            var items = new List<SvgDrawItem>();
            SvgDrawOrder.Split(doc, true, true, items);
            Assert.AreEqual(5, items.Count);
            CollectionAssert.AreEqual(new[] { false, true, false, true, false }, items.Select(i => i.overlay).ToArray());
            Assert.AreEqual(0, items[0].from);
            Assert.AreEqual(1, items[0].to);
            Assert.AreEqual(2, items[2].from);
            Assert.AreEqual(4, items[2].to, "two shapes between the texts are one run");
            Assert.AreEqual(5, items[4].from);
        }

        [Test]
        public void Split_WithoutText_IsOneRun()
        {
            var doc = Parse(Sandwich);
            var items = new List<SvgDrawItem>();
            SvgDrawOrder.Split(doc, false, false, items);
            Assert.AreEqual(1, items.Count);
            Assert.IsFalse(items[0].overlay);
            Assert.AreEqual(0, items[0].from);
            Assert.AreEqual(doc.elements.Count, items[0].to);
        }

        [Test]
        public void Split_LayoutHash_ChangesWithTheLayout()
        {
            var items = new List<SvgDrawItem>();
            var a = SvgDrawOrder.Split(Parse(Sandwich), true, true, items);
            var b = SvgDrawOrder.Split(Parse(Sandwich), true, true, items);
            var c = SvgDrawOrder.Split(Parse(Sandwich.Replace("<circle cx='200' cy='100' r='30' fill='#0f0'/>", "")), true, true, items);
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }

        [Test]
        public void BuildRange_DrawsOnlyThoseElements()
        {
            var doc = Parse(Sandwich);
            using var r = new VectraShapeRenderer();
            VectraSvgBuilder.Build(doc, r, new Rect(0, 0, 400, 200), 1f, Color.white, false, VectraQuality.High, null, null, null, 0, 1);
            int first = r.buffer.VertexCount;
            VectraSvgBuilder.Build(doc, r, new Rect(0, 0, 400, 200), 1f, Color.white, false, VectraQuality.High, null, null, null, 1, 2);
            Assert.AreEqual(0, r.buffer.VertexCount, "element 1 is a text: no mesh");
            VectraSvgBuilder.Build(doc, r, new Rect(0, 0, 400, 200), 1f, Color.white, false, VectraQuality.High);
            Assert.Greater(r.buffer.VertexCount, first, "the whole document has more than its first element");
        }

        [Test]
        public void TheImage_DrawsShapesAfterATextAsLaterSiblings()
        {
            using var scope = new ImageScope();
            var img = scope.Image(Sandwich);
            var names = new List<string>();
            foreach (Transform c in img.transform) names.Add(c.name);
            CollectionAssert.AreEqual(new[] { "SVG Text", "SVG Layer", "SVG Text", "SVG Layer" }, names,
                "text, the two shapes after it, text, the shape after it");
            Assert.AreEqual(2, img.GetComponentsInChildren<VectraSvgLayer>().Length);
        }

        [Test]
        public void TheImage_WithATextFirst_DrawsNothingItself()
        {
            using var scope = new ImageScope();
            var img = scope.Image("<svg viewBox='0 0 100 50'><text x='5' y='20' font-size='10'>a</text><rect width='20' height='20'/></svg>");
            var names = new List<string>();
            foreach (Transform c in img.transform) names.Add(c.name);
            CollectionAssert.AreEqual(new[] { "SVG Text", "SVG Layer" }, names);
        }

        [Test]
        public void Resizing_MovesTheChildrenWithoutRecreatingThem()
        {
            using var scope = new ImageScope();
            var img = scope.Image(Sandwich, size: 400f);
            var before = new List<GameObject>();
            foreach (Transform c in img.transform) before.Add(c.gameObject);
            var rt = (RectTransform)img.transform;
            var posBefore = ((RectTransform)before[0].transform).anchoredPosition;

            rt.sizeDelta = new Vector2(800f, 400f);
            Canvas.ForceUpdateCanvases();
            img.RefreshGenerated();

            var after = new List<GameObject>();
            foreach (Transform c in img.transform) after.Add(c.gameObject);
            CollectionAssert.AreEqual(before, after, "the same objects, in the same order");
            var posAfter = ((RectTransform)after[0].transform).anchoredPosition;
            Assert.AreNotEqual(posBefore, posAfter, "but the text moved to its new place");
        }

        [Test]
        public void ChangingTheDocument_RebuildsTheChildren()
        {
            using var scope = new ImageScope();
            var img = scope.Image(Sandwich);
            var first = img.transform.GetChild(0).gameObject;
            img.Document = Parse("<svg viewBox='0 0 10 10'><rect width='5' height='5'/></svg>");
            img.RefreshGenerated();
            Assert.AreEqual(0, img.transform.childCount);
            Assert.IsTrue(first == null, "the old child is gone");
        }

        // --- world space -----------------------------------------------------------------------------------

        static VectraSvgRenderer World(VectraSvgDocument doc, VectraTextEngineChoice engine, out GameObject go)
        {
            go = new GameObject("world svg");
            var r = go.AddComponent<VectraSvgRenderer>();
            r.Height = 2f;
            r.TextEngine = engine;
            r.Document = doc;
            return r;
        }

        [Test]
        public void World_TextGoesThroughTheWorldEntryPoint_WithWorldUnits()
        {
            using (new RegistryScope(clear: true))
            {
                var fake = new FakeBackend(VectraTextEngine.Lettera);
                VectraTextBackends.Register(fake);
                var doc = Parse(Sandwich);
                var r = World(doc, VectraTextEngineChoice.Lettera, out var go);
                try
                {
                    r.SortingOrder = 10;
                    r.RebuildNow();
                    Assert.AreEqual(2, fake.worldBuilds, "both texts");
                    Assert.AreEqual(0, fake.builds, "the Canvas entry point is not used");
                    // 400 x 200 units drawn 4 x 2 world units: one document unit is 0.01.
                    Assert.AreEqual(24f * 0.01f, fake.last.fontSize, 1e-4f);
                    Assert.AreEqual(-2f + 0.2f, fake.last.origin.x, 1e-4f, "second text: x 20");
                    Assert.AreEqual(1f - 1.2f, fake.last.origin.y, 1e-4f, "y 120");
                    Assert.AreEqual(10 + 3, fake.last.sortingOrder, "shape, text, shapes, text: the second text is step 3");
                    var layers = go.GetComponentsInChildren<MeshRenderer>().Where(m => m.gameObject != go).Select(m => m.sortingOrder).OrderBy(i => i).ToArray();
                    CollectionAssert.AreEqual(new[] { 12, 14 }, layers, "the shapes after each text sort above it");
                    Assert.AreEqual(10, go.GetComponent<MeshRenderer>().sortingOrder);
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        [Test]
        public void World_SkipsEnginesThatNeedACanvas()
        {
            using (new RegistryScope(clear: true))
            {
                var lettera = new FakeBackend(VectraTextEngine.Lettera) { supportsWorld = false };
                var tmp = new FakeBackend(VectraTextEngine.TextMeshPro);
                VectraTextBackends.Register(lettera);
                VectraTextBackends.Register(tmp);
                var choice = VectraTextBackends.Resolve(VectraTextEngine.Lettera, null, null, null, world: true);
                Assert.AreEqual(VectraTextEngine.TextMeshPro, choice.Used);
                Assert.IsTrue(choice.fellBack);
                Assert.AreEqual(VectraTextEngine.Lettera, VectraTextBackends.Resolve(VectraTextEngine.Lettera, null, null, null).Used, "on a Canvas it is fine");
            }
        }

        [Test]
        public void World_WithOnlyTheLegacyEngine_SkipsTheTextAndSaysSoOnce()
        {
            using (new RegistryScope(clear: true))
            {
                var doc = Parse(Sandwich);
                LogAssert.Expect(LogType.Warning, new Regex("world space"));
                var r = World(doc, VectraTextEngineChoice.UseProjectSetting, out var go);
                try
                {
                    r.RebuildNow();
                    r.RebuildNow(); // no second warning: LogAssert fails the test on an unexpected message
                    Assert.AreEqual(0, go.GetComponentsInChildren<Transform>().Count(t => t.name == "SVG Text"));
                    Assert.IsTrue(VectraTextBackends.Get(VectraTextEngine.Legacy) != null && !VectraTextBackends.Get(VectraTextEngine.Legacy).SupportsWorld);
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        [Test]
        public void World_AnEmptyDocument_Builds()
        {
            var r = World(null, VectraTextEngineChoice.UseProjectSetting, out var go);
            try
            {
                Assert.DoesNotThrow(() => r.RebuildNow());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // --- cache keys ------------------------------------------------------------------------------------

        static SvgPathData Square(float size, float x = 0f, float y = 0f)
        {
            var p = new SvgPathData();
            p.verbs.Add((byte)Geometry.PathVerb.MoveTo);
            p.points.Add(new Vector2(x, y));
            p.verbs.Add((byte)Geometry.PathVerb.LineTo);
            p.points.Add(new Vector2(x + size, y));
            p.verbs.Add((byte)Geometry.PathVerb.LineTo);
            p.points.Add(new Vector2(x + size, y + size));
            p.verbs.Add((byte)Geometry.PathVerb.LineTo);
            p.points.Add(new Vector2(x, y + size));
            p.verbs.Add((byte)Geometry.PathVerb.Close);
            return p;
        }

        static VectraSvgDocument OneElement(SvgPathData path)
        {
            var doc = ScriptableObject.CreateInstance<VectraSvgDocument>();
            doc.viewBox = new Rect(0, 0, 200, 200);
            doc.elements.Add(new SvgElementData { tag = "path", path = path, hasFill = true, fill = VectraPaint.Solid(Color.white) });
            return doc;
        }

        static float Width(VectraShapeRenderer r)
        {
            var b = r.buffer.bounds;
            return b.max.x - b.min.x;
        }

        [Test]
        public void EditingAPathInPlace_IsNotServedFromTheCache()
        {
            var doc = OneElement(Square(50f));
            m_Objects.Add(doc);
            using var r = new VectraShapeRenderer();
            var rect = new Rect(0, 0, 200, 200);
            VectraSvgBuilder.Build(doc, r, rect, 1f, Color.white, false, VectraQuality.High);
            float w1 = Width(r);
            doc.elements[0].path = Square(120f); // the same document instance and element index, other content
            VectraSvgBuilder.Build(doc, r, rect, 1f, Color.white, false, VectraQuality.High);
            Assert.AreEqual(50f, w1, 4f); // plus the soft edge
            Assert.AreEqual(120f, Width(r), 4f, "the new outline is built, not the cached one");
        }

        [Test]
        public void ReparsingIntoTheSameDocument_ShowsTheNewContent()
        {
            var doc = Parse("<svg viewBox='0 0 100 100'><rect width='30' height='30'/></svg>");
            using var r = new VectraShapeRenderer();
            var rect = new Rect(0, 0, 100, 100);
            VectraSvgBuilder.Build(doc, r, rect, 1f, Color.white, false, VectraQuality.High);
            float w1 = Width(r);
            SvgParser.Parse("<svg viewBox='0 0 100 100'><rect width='70' height='70'/></svg>", doc);
            VectraSvgBuilder.Build(doc, r, rect, 1f, Color.white, false, VectraQuality.High);
            Assert.AreEqual(30f, w1, 4f);
            Assert.AreEqual(70f, Width(r), 4f);
        }

        [Test]
        public void TwoClipsWithTheSameNumberOfPoints_AreNotMixedUp()
        {
            var doc = OneElement(Square(150f));
            m_Objects.Add(doc);
            doc.elements[0].clips.Add(Square(20f));
            using var r = new VectraShapeRenderer();
            var rect = new Rect(0, 0, 200, 200);
            VectraSvgBuilder.Build(doc, r, rect, 1f, Color.white, false, VectraQuality.High);
            float w1 = Width(r);
            doc.elements[0].clips[0] = Square(60f);
            VectraSvgBuilder.Build(doc, r, rect, 1f, Color.white, false, VectraQuality.High);
            Assert.AreEqual(20f, w1, 4f);
            Assert.AreEqual(60f, Width(r), 4f, "the second clip has as many points as the first");
        }

        [Test]
        public void PathContentHash_FollowsTheContent()
        {
            Assert.AreEqual(Square(10f).ContentHash(), Square(10f).ContentHash());
            Assert.AreNotEqual(Square(10f).ContentHash(), Square(11f).ContentHash());
            var evenOdd = Square(10f);
            evenOdd.rule = Geometry.FillRule.EvenOdd;
            Assert.AreNotEqual(Square(10f).ContentHash(), evenOdd.ContentHash());
        }

        // --- <use> keeps the target's own style -------------------------------------------------------------

        [Test]
        public void ADirectElement_KeepsItsStyle_AfterAUseOfItDrewFirst()
        {
            const string svg = "<svg viewBox='0 0 100 100'><use href='#a' fill='#ff0000'/>" +
                               "<g fill='#0000ff'><rect id='a' width='10' height='10'/></g></svg>";
            var doc = Parse(svg);
            Assert.AreEqual(2, doc.elements.Count);
            Assert.AreEqual(Color.red, doc.elements[0].fill.color, "through the use");
            Assert.AreEqual(Color.blue, doc.elements[1].fill.color, "drawn where it stands: the group's blue");
        }

        [Test]
        public void TwoUsesOfOneTarget_EachGetTheirOwnStyle()
        {
            const string svg = "<svg viewBox='0 0 100 100'><defs><rect id='a' width='10' height='10'/></defs>" +
                               "<use href='#a' fill='#ff0000'/><use href='#a' x='20' fill='#00ff00'/></svg>";
            var doc = Parse(svg);
            Assert.AreEqual(Color.red, doc.elements[0].fill.color);
            Assert.AreEqual(Color.green, doc.elements[1].fill.color);
        }

        [Test]
        public void GroupOpacity_IsALayer_NotAWarning()
        {
            var doc = Parse("<svg viewBox='0 0 100 100'><g opacity='0.5'><rect width='20' height='20'/><rect x='10' width='20' height='20'/></g></svg>");
            Assert.That(!doc.warnings.Any(w => w.Contains("Group opacity")), string.Join(" | ", doc.warnings));
            Assert.AreEqual(1, doc.layers.Count);
        }

        // --- z-order, in pixels ----------------------------------------------------------------------------

        [Test]
        public void ARectangleAfterTheText_CoversIt()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            // White text over a dark ground, then a solid red block over the text. The block must show no white.
            const string svg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 100'>" +
                               "<rect width='200' height='100' fill='#101820'/>" +
                               "<text x='20' y='70' font-size='60' fill='#ffffff'>HHHH</text>" +
                               "<rect x='10' y='10' width='180' height='80' fill='#cc0000'/></svg>";
            var px = RenderCanvas(svg, 400, 200, out _);
            int white = 0, red = 0;
            // The middle 60 % of the picture lies inside the red block.
            for (int y = 60; y < 140; y++)
                for (int x = 60; x < 340; x++)
                {
                    var c = px[y * 400 + x];
                    if (c.r > 230 && c.g > 230 && c.b > 230) white++;
                    else if (c.r > 150 && c.g < 60 && c.b < 60) red++;
                }

            Debug.Log($"VECTRA-METRIC z-order: white pixels inside the red block {white}, red pixels {red}");
            Assert.AreEqual(0, white, "text drawn above a shape that follows it in the document");
            Assert.Greater(red, 10000);
        }

        static Color32[] RenderCanvas(string svg, int w, int h, out Texture2D tex)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var camGo = new GameObject("shot camera", typeof(Camera));
            var canvasGo = new GameObject("shot canvas", typeof(Canvas));
            var doc = SvgParser.Parse(svg);
            var previous = RenderTexture.active;
            try
            {
                var cam = camGo.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = h / 2f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
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
                rect.sizeDelta = new Vector2(w, h);
                var img = go.AddComponent<VectraSvgImage>();
                img.Document = doc;
                img.RebuildGenerated();
                for (int i = 0; i < 3; i++)
                {
                    Canvas.ForceUpdateCanvases();
                    cam.Render();
                }

                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                var pixels = tex.GetPixels32();
                Object.DestroyImmediate(tex);
                tex = null;
                return pixels;
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
    }
}
