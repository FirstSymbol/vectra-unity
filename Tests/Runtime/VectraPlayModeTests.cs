using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>
    /// Play mode tests: rendered pixels, batching, garbage-free rebuilds, tweens, animation bridge, masks, palettes
    /// and SVG expansion. Every test builds its own camera and Screen Space - Camera canvas rendering into a
    /// render texture, so the result does not depend on the Game view.
    /// </summary>
    public class VectraPlayModeTests
    {
        const int Size = 256;
        readonly List<Object> m_Owned = new List<Object>();
        Camera m_Camera;
        Canvas m_Canvas;
        RenderTexture m_Target;

        [SetUp]
        public void SetUp()
        {
            m_Target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            m_Owned.Add(m_Target);
            var camGo = new GameObject("Vectra Test Camera");
            m_Owned.Add(camGo);
            m_Camera = camGo.AddComponent<Camera>();
            m_Camera.orthographic = true;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            m_Camera.targetTexture = m_Target;
            m_Camera.transform.position = new Vector3(0f, 0f, -10f);

            var canvasGo = new GameObject("Vectra Test Canvas", typeof(Canvas), typeof(CanvasScaler));
            m_Owned.Add(canvasGo);
            m_Canvas = canvasGo.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            m_Canvas.worldCamera = m_Camera;
            m_Canvas.planeDistance = 5f;
        }

        [TearDown]
        public void TearDown()
        {
            VectraPalette.Active = null;
            foreach (var o in m_Owned)
                if (o != null) Object.Destroy(o);
            m_Owned.Clear();
        }

        VectraShape AddShape<T>(Transform parent, Vector2 size, Color color, Vector2 position = default) where T : VectraShapeGeometry, new()
        {
            var go = new GameObject(typeof(T).Name, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
            var s = go.GetComponent<VectraShape>();
            s.SetGeometry<T>();
            s.SetFill(color);
            return s;
        }

        /// <summary>Renders the canvas and returns the pixels (bottom row first).</summary>
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

        /// <summary>Pixel at a position relative to the centre of the target, in target pixels.</summary>
        static Color At(Color[] px, int dx, int dy) => px[(Size / 2 + dy) * Size + Size / 2 + dx];

        [UnityTest]
        public IEnumerator Circle_IsFilledInsideAndClearOutside()
        {
            AddShape<EllipseShape>(m_Canvas.transform, new Vector2(120, 120), Color.red);
            yield return null;
            var px = Render();
            var centre = At(px, 0, 0);
            Assert.Greater(centre.r, 0.9f, "centre should be red");
            Assert.Less(centre.g, 0.1f);
            Assert.Greater(centre.a, 0.9f);
            // Inside the rect but outside the circle (the corner region) stays transparent.
            Assert.Less(At(px, 55, 55).a, 0.05f, "corner of the rect outside the circle");
            Assert.Greater(At(px, 50, 0).a, 0.9f, "inside near the edge");
        }

        [UnityTest]
        public IEnumerator Stroke_DrawsRingOnly()
        {
            var s = AddShape<EllipseShape>(m_Canvas.transform, new Vector2(160, 160), Color.clear);
            s.Fills.Clear();
            s.SetStroke(Color.green, 10f, StrokeAlignment.Inside);
            yield return null;
            var px = Render();
            Assert.Less(At(px, 0, 0).a, 0.05f, "no fill in the middle");
            var ring = At(px, 75, 0);
            Assert.Greater(ring.g, 0.8f, "stroke on the ring");
            Assert.Greater(ring.a, 0.8f);
        }

        [UnityTest]
        public IEnumerator SoftMask_HidesChildOutsideTheMask()
        {
            var mask = AddShape<EllipseShape>(m_Canvas.transform, new Vector2(100, 100), new Color(1f, 1f, 1f, 0f));
            var m = mask.gameObject.AddComponent<VectraMask>();
            m.Mode = VectraMaskMode.Soft;
            m.Softness = 0f;
            AddShape<RectangleShape>(mask.transform, new Vector2(240, 240), Color.blue);
            yield return null;
            yield return null;
            var px = Render();
            Assert.Greater(At(px, 0, 0).b, 0.9f, "child visible inside the mask");
            Assert.Less(At(px, 100, 0).a, 0.05f, "child hidden outside the mask");
        }

        [UnityTest]
        public IEnumerator StencilMask_FollowsTheOutlineNotTheRect()
        {
            var mask = AddShape<EllipseShape>(m_Canvas.transform, new Vector2(120, 120), Color.white);
            mask.gameObject.AddComponent<VectraMask>().showMaskGraphic = false;
            var child = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            child.transform.SetParent(mask.transform, false);
            ((RectTransform)child.transform).sizeDelta = new Vector2(240, 240);
            child.GetComponent<Image>().color = Color.yellow;
            yield return null;
            var px = Render();
            Assert.Greater(At(px, 0, 0).g, 0.8f, "image visible inside the circle");
            Assert.Less(At(px, 56, 56).a, 0.05f, "image clipped in the rect corner outside the circle");
        }

        [UnityTest]
        public IEnumerator Palette_ThemeSwitchRecolours()
        {
            var palette = ScriptableObject.CreateInstance<VectraPalette>();
            m_Owned.Add(palette);
            palette.SetColor("Accent", 0, Color.red);
            palette.SetColor("Accent", 1, Color.blue);
            VectraPalette.Active = palette;
            var s = AddShape<RectangleShape>(m_Canvas.transform, new Vector2(100, 100), Color.white);
            s.Fills[0].paint.colorToken = "Accent";
            s.SetShapeDirty();
            yield return null;
            Assert.Greater(At(Render(), 0, 0).r, 0.9f, "light theme");
            palette.SetTheme("Dark");
            yield return null;
            var c = At(Render(), 0, 0);
            Assert.Greater(c.b, 0.9f, "dark theme");
            Assert.Less(c.r, 0.1f);
        }

        [UnityTest]
        public IEnumerator NewEffects_RenderSomethingVisible()
        {
            var s = AddShape<StarShape>(m_Canvas.transform, new Vector2(140, 140), Color.white);
            s.AddEffect<OutlineEffect>().color = Color.red;
            s.AddEffect<ColorOverlayEffect>().color = new Color(0f, 0f, 1f, 1f);
            s.AddEffect<PixelateEffect>().cellSize = 8f;
            yield return null;
            var px = Render();
            var c = At(px, 0, 0);
            Assert.Greater(c.b, 0.9f, "colour overlay covers the fill");
            Assert.Less(c.r, 0.1f);
        }

        [UnityTest]
        public IEnumerator Batching_AllShapesShareOneMaterialAndTexture()
        {
            var shapes = new List<VectraShape>();
            int i = 0;
            foreach (var make in new System.Func<Transform, VectraShape>[]
                     {
                         t => AddShape<RectangleShape>(t, new Vector2(40, 40), Color.red),
                         t => AddShape<EllipseShape>(t, new Vector2(40, 40), Color.green),
                         t => AddShape<StarShape>(t, new Vector2(40, 40), Color.blue),
                         t => AddShape<GearShape>(t, new Vector2(40, 40), Color.white),
                         t => AddShape<HeartShape>(t, new Vector2(40, 40), Color.yellow)
                     })
            {
                var s = make(m_Canvas.transform);
                ((RectTransform)s.transform).anchoredPosition = new Vector2(-100 + i * 50, 0);
                if (i % 2 == 0) s.SetFill(VectraPaint.Linear(Color.red, Color.blue, 30f));
                s.AddEffect<DropShadowEffect>();
                shapes.Add(s);
                i++;
            }

            yield return null;
            Canvas.ForceUpdateCanvases();
            var material = shapes[0].canvasRenderer.GetMaterial();
            var texture = shapes[0].mainTexture;
            Assert.IsNotNull(material);
            foreach (var s in shapes)
            {
                Assert.AreSame(material, s.canvasRenderer.GetMaterial(), s.name + " material");
                Assert.AreSame(texture, s.mainTexture, s.name + " texture");
            }
        }

        [UnityTest]
        public IEnumerator Batching_DrawCallsDoNotGrowWithShapeCount()
        {
            // Render counters differ between pipelines and players: use the first one that reports anything.
            var names = new[] { "Batches Count", "Draw Calls Count", "SetPass Calls Count" };
            var recorders = new ProfilerRecorder[names.Length];
            for (int r = 0; r < names.Length; r++) recorders[r] = ProfilerRecorder.StartNew(ProfilerCategory.Render, names[r]);
            try
            {
                for (int f = 0; f < 3; f++) yield return null;
                long[] empty = Sample(recorders);
                AddShape<RectangleShape>(m_Canvas.transform, new Vector2(30, 30), Color.red);
                for (int f = 0; f < 3; f++) yield return null;
                long[] one = Sample(recorders);

                for (int k = 0; k < 40; k++)
                {
                    var s = k % 2 == 0
                        ? AddShape<StarShape>(m_Canvas.transform, new Vector2(20, 20), Color.HSVToRGB(k / 40f, 1f, 1f))
                        : AddShape<EllipseShape>(m_Canvas.transform, new Vector2(20, 20), Color.white);
                    ((RectTransform)s.transform).anchoredPosition = new Vector2(-110 + (k % 10) * 24, -60 + (k / 10) * 30);
                }

                for (int f = 0; f < 3; f++) yield return null;
                long[] many = Sample(recorders);
                Debug.Log($"Vectra batching: {string.Join(", ", System.Linq.Enumerable.Range(0, names.Length).Select(r => $"{names[r]} {empty[r]} / {one[r]} / {many[r]}"))}");
                int used = System.Array.FindIndex(one, v => v > 0);
                if (used < 0) Assert.Ignore("Render statistics are not available in this player.");
                Assert.LessOrEqual(many[used], one[used], $"{names[used]}: 41 shapes should need no more draws than one");
            }
            finally
            {
                foreach (var r in recorders) r.Dispose();
            }

            static long[] Sample(ProfilerRecorder[] recorders)
            {
                var v = new long[recorders.Length];
                for (int r = 0; r < recorders.Length; r++) v[r] = recorders[r].Valid ? recorders[r].LastValue : 0;
                return v;
            }
        }
        [Test]
        public void Rebuild_AnimatedTrim_AllocatesNoGarbage()
        {
            var content = new VectraShapeContent { geometry = new PathShape() };
            content.fills.Clear();
            content.strokes.Add(new StrokeLayer(Color.white, 4f) { dashed = true });
            content.effects.Add(new DropShadowEffect());
            var renderer = new VectraShapeRenderer();
            var mesh = new Mesh();
            try
            {
                var frame = new ShapeFrame { rect = new Rect(-100, -100, 200, 200), pixelSize = 1f };
                for (int i = 0; i < 5; i++) Build(i);
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 60; i++) Build(i);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.AreEqual(0, allocated, "bytes allocated by 60 rebuilds with an animated trim");

                void Build(int i)
                {
                    content.strokes[0].trimEnd = 0.3f + 0.6f * (i % 30) / 30f;
                    renderer.Build(content.ToInput(frame, Color.white, false));
                    renderer.buffer.Upload(mesh);
                }
            }
            finally
            {
                renderer.Dispose();
                Object.Destroy(mesh);
            }
        }

        [UnityTest]
        public IEnumerator Tween_ReachesTargetAndCompletes()
        {
            var s = AddShape<EllipseShape>(m_Canvas.transform, new Vector2(80, 80), Color.white);
            s.SetStroke(Color.white, 4f);
            s.Strokes[0].trimEnd = 0f;
            bool done = false;
            s.TweenTrim(0f, 1f, 0.15f).SetEase(VectraEase.OutCubic).OnComplete(() => done = true);
            s.TweenFillColor(Color.red, 0.15f);
            float t0 = Time.realtimeSinceStartup;
            while (!done && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.IsTrue(done, "tween completed");
            Assert.AreEqual(1f, s.Strokes[0].trimEnd, 1e-4f);
            Assert.AreEqual(Color.red, s.Fills[0].paint.color);
        }

        [UnityTest]
        public IEnumerator Tween_StopsWhenTargetIsDestroyed()
        {
            var s = AddShape<EllipseShape>(m_Canvas.transform, new Vector2(80, 80), Color.white);
            var tween = s.TweenRotation(360f, 5f);
            yield return null;
            Object.Destroy(s.gameObject);
            yield return null;
            yield return null;
            Assert.IsFalse(tween.IsActive);
        }

        [UnityTest]
        public IEnumerator AnimatedProperties_ApplyDrivenFields()
        {
            var s = AddShape<RectangleShape>(m_Canvas.transform, new Vector2(80, 80), Color.white);
            s.SetStroke(Color.black, 2f);
            var a = s.gameObject.AddComponent<VectraAnimatedProperties>();
            a.driveTrim = true;
            a.trimEnd = 0.25f;
            a.driveFillColor = true;
            a.fillColor = Color.green;
            yield return null;
            Assert.AreEqual(0.25f, s.Strokes[0].trimEnd, 1e-5f);
            Assert.AreEqual(Color.green, s.Fills[0].paint.color);
            a.driveFillAmount = true;
            a.fillAmount = 0.4f;
            yield return null;
            Assert.AreEqual(0.4f, s.GetEffect<FillAmountEffect>().amount, 1e-5f);
        }

        const string ClippedSvg =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100' width='100' height='100'>" +
            "<defs><clipPath id='c'><circle cx='50' cy='50' r='30'/></clipPath></defs>" +
            "<rect id='bg' x='0' y='0' width='100' height='100' fill='#203040'/>" +
            "<rect id='clipped' x='10' y='10' width='80' height='80' fill='red' clip-path='url(#c)'/>" +
            "<line id='slash' x1='10' y1='90' x2='90' y2='10' stroke='lime' stroke-width='6' clip-path='url(#c)'/>" +
            "</svg>";

        [UnityTest]
        public IEnumerator Svg_ExpandCreatesEditableShapes()
        {
            var doc = VectraSvg.Parse(ClippedSvg);
            m_Owned.Add(doc);
            var go = new GameObject("SVG", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraSvgImage));
            go.transform.SetParent(m_Canvas.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(200, 200);
            var img = go.GetComponent<VectraSvgImage>();
            img.Document = doc;
            yield return null;
            var root = VectraSvg.Expand(img);
            Assert.IsNotNull(root);
            go.SetActive(false);
            var shapes = root.GetComponentsInChildren<VectraShape>();
            Assert.IsNotNull(root.Find("bg")?.GetComponent<VectraShape>(), "background shape");
            Assert.IsInstanceOf<BooleanShape>(root.Find("clipped").GetComponent<VectraShape>().Geometry, "filled element clipped by a boolean");
            Assert.IsNotNull(root.Find("slash clip")?.GetComponent<VectraMask>(), "stroked element clipped by a mask");
            Assert.GreaterOrEqual(shapes.Length, 4);
            yield return null;
            var px = Render();
            // (the lime diagonal crosses the centre; sample beside it, inside the clip circle)
            Assert.Greater(At(px, 25, -25).r, 0.6f, "clipped red rect visible inside the clip circle");
            Assert.Greater(At(px, 0, 0).g, 0.6f, "clipped lime stroke visible in the middle");
            var corner = At(px, -85, -85);
            Assert.Less(corner.r, 0.3f, "red rect clipped away near the corner");
        }

        [Test]
        public void Svg_ParseAndLoad()
        {
            var doc = VectraSvg.Parse(ClippedSvg);
            Assert.AreEqual(3, doc.elements.Count);
            var asset = new TextAsset(ClippedSvg);
            var doc2 = VectraSvg.Load(asset);
            Assert.AreEqual(doc.elements.Count, doc2.elements.Count);
            Object.Destroy(doc);
            Object.Destroy(doc2);
            Object.Destroy(asset);
        }

        [Test]
        public void Rebuild_ThroughputIsReasonable()
        {
            var content = new VectraShapeContent { geometry = new GearShape() };
            content.strokes.Add(new StrokeLayer(Color.black, 2f));
            content.effects.Add(new DropShadowEffect());
            var renderer = new VectraShapeRenderer();
            try
            {
                var frame = new ShapeFrame { rect = new Rect(-50, -50, 100, 100), pixelSize = 1f };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 200; i++)
                {
                    content.geometry.rotation = i;
                    renderer.Build(content.ToInput(frame, Color.white, false));
                }

                sw.Stop();
                Debug.Log($"Vectra: 200 gear rebuilds (tessellation, stroke, distance field) in {sw.Elapsed.TotalMilliseconds:0.0} ms");
                Assert.Less(sw.Elapsed.TotalMilliseconds, 5000.0);
            }
            finally
            {
                renderer.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator ContourGradient_FollowsTheOutline()
        {
            var s = AddShape<RectangleShape>(m_Canvas.transform, new Vector2(160, 160), Color.white);
            ((RectangleShape)s.Geometry).cornerRadii = Vector4.zero;
            s.SetFill(VectraPaint.Contour(Color.red, Color.blue, 60f));
            yield return null;
            var px = Render();
            var edge = At(px, 76, 0);
            var centre = At(px, 0, 0);
            Assert.Greater(edge.r, edge.b, "red near the edge");
            Assert.Greater(centre.b, 0.9f, "blue deeper than the depth");
            var midLeft = At(px, -76, 0);
            Assert.Greater(midLeft.r, midLeft.b, "the same colour along every side");
        }

        [UnityTest]
        public IEnumerator Extrude_DrawsSlicesBehind()
        {
            var s = AddShape<RectangleShape>(m_Canvas.transform, new Vector2(80, 80), Color.white);
            var ex = s.AddEffect<ExtrudeEffect>();
            ex.offset = new Vector2(30f, -30f);
            ex.nearColor = ex.farColor = Color.green;
            yield return null;
            var px = Render();
            Assert.Greater(At(px, 60, -60).g, 0.8f, "extruded slice below-right of the shape");
            Assert.Greater(At(px, 0, 0).r, 0.9f, "the shape stays on top");
        }

        [Serializable]
        class RingEffect : VectraCustomEffect
        {
            public override string DisplayName => "Test ring";
            public override float Reach => 40f;
            public override void Populate(VectraEffectBuilder b) => b.Band(Color.magenta, 6f, 20f);
        }

        [UnityTest]
        public IEnumerator CustomEffect_EmitsThroughTheBuilder()
        {
            var s = AddShape<EllipseShape>(m_Canvas.transform, new Vector2(100, 100), Color.white);
            s.Effects.Add(new RingEffect());
            s.SetShapeDirty();
            yield return null;
            var px = Render();
            var ring = At(px, 70, 0);
            Assert.Greater(ring.r, 0.8f, "band 20 units outside the outline");
            Assert.Greater(ring.b, 0.8f);
            Assert.Less(At(px, 60, 0).a, 0.1f, "gap between the outline and the band");
        }

        [UnityTest]
        public IEnumerator InteractionStates_TransitionToHover()
        {
            var s = AddShape<RectangleShape>(m_Canvas.transform, new Vector2(100, 60), Color.white);
            var states = s.gameObject.AddComponent<VectraInteractionStates>();
            states.duration = 0.05f;
            states.hover.scale = 1.5f;
            yield return null;
            states.SetState(VectraInteractionState.Hover);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.5f) yield return null;
            Assert.AreEqual(1.5f, s.transform.localScale.x, 1e-3f);
            states.SetState(VectraInteractionState.Normal, true);
            Assert.AreEqual(1f, s.transform.localScale.x, 1e-3f);
        }    }
}
