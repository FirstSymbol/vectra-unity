using System.Collections;
using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>One check per frame instead of one per object: the registry that replaced the LateUpdate of the components.</summary>
    public class WatchPlayModeTests
    {
        readonly List<Object> m_Owned = new List<Object>();
        System.Func<int> m_ExtraCount;
        System.Func<int, Camera> m_ExtraCamera;

        // The editor lets every Scene view magnify canvases, so there shapes are checked every frame; a player has no such views.
        [SetUp]
        public void SetUp()
        {
            m_ExtraCount = VectraScreenDensity.ExtraCameraCount;
            m_ExtraCamera = VectraScreenDensity.ExtraCamera;
            VectraScreenDensity.ExtraCameraCount = null;
            VectraScreenDensity.ExtraCamera = null;
        }

        [TearDown]
        public void TearDown()
        {
            VectraScreenDensity.ExtraCameraCount = m_ExtraCount;
            VectraScreenDensity.ExtraCamera = m_ExtraCamera;
            foreach (var o in m_Owned)
                if (o != null) Object.Destroy(o);
            m_Owned.Clear();
        }

        Transform NewCanvas(RenderMode mode = RenderMode.ScreenSpaceOverlay)
        {
            var go = new GameObject("Watch Canvas", typeof(Canvas));
            go.GetComponent<Canvas>().renderMode = mode;
            m_Owned.Add(go);
            return go.transform;
        }

        static VectraShape NewShape(Transform parent, int i)
        {
            var go = new GameObject("S" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(20f, 20f);
            var s = go.GetComponent<VectraShape>();
            s.SetGeometry<EllipseShape>();
            s.SetFill(Color.HSVToRGB((i * 0.013f) % 1f, 0.6f, 0.9f));
            return s;
        }

        [UnityTest]
        public IEnumerator UnchangingShapes_AreCheckedOnlyInSlices_NotEveryFrame()
        {
            var canvas = NewCanvas();
            for (int i = 0; i < 640; i++) NewShape(canvas, i);
            for (int i = 0; i < 6; i++) yield return null;
            Assert.GreaterOrEqual(VectraWatch.Count, 640, "all registered");

            int ticked = 0;
            const int frames = 128;
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                ticked += VectraWatch.LastTicked;
            }

            // The slice re-reads each shape once in 64 frames (640 in 64 frames); a per-object LateUpdate would be 640 per frame.
            Assert.LessOrEqual(ticked, 640 * frames / 64 + 64, "checks in " + frames + " frames");
        }

        [UnityTest]
        public IEnumerator ScaleChange_RebuildsOnlyTheShapeThatChanged()
        {
            var canvas = NewCanvas();
            var shapes = new List<VectraShape>();
            for (int i = 0; i < 50; i++) shapes.Add(NewShape(canvas, i));
            for (int i = 0; i < 6; i++) yield return null;

            int before = VectraShape.s_Rebuilds;
            shapes[7].transform.localScale = Vector3.one * 2f;
            yield return null;
            yield return null;
            Assert.AreEqual(1, VectraShape.s_Rebuilds - before, "only the scaled shape is rebuilt (scale step over 25%)");
        }

        [UnityTest]
        public IEnumerator ParentScaleChange_RebuildsTheChildren()
        {
            var canvas = NewCanvas();
            var parent = new GameObject("Parent", typeof(RectTransform));
            parent.transform.SetParent(canvas, false);
            var shapes = new List<VectraShape>();
            for (int i = 0; i < 10; i++) shapes.Add(NewShape(parent.transform, i));
            for (int i = 0; i < 6; i++) yield return null;

            int before = VectraShape.s_Rebuilds;
            parent.transform.localScale = Vector3.one * 3f;
            yield return null;
            yield return null;
            Assert.AreEqual(10, VectraShape.s_Rebuilds - before);
        }

        [UnityTest]
        public IEnumerator ToggledAndDestroyedShapes_LeaveTheRegistryConsistent()
        {
            var canvas = NewCanvas();
            var shapes = new List<VectraShape>();
            for (int i = 0; i < 200; i++) shapes.Add(NewShape(canvas, i));
            for (int i = 0; i < 3; i++) yield return null;
            for (int round = 0; round < 4; round++)
            {
                for (int i = 0; i < shapes.Count; i += 2) shapes[i].gameObject.SetActive(false);
                yield return null;
                for (int i = 0; i < shapes.Count; i += 2) shapes[i].gameObject.SetActive(true);
                if (round == 2)
                    for (int i = 1; i < shapes.Count; i += 4) Object.Destroy(shapes[i].gameObject);
                yield return null;
            }

            yield return null;
            int alive = 0;
            foreach (var s in shapes)
                if (s != null && s.isActiveAndEnabled) alive++;
            Assert.AreEqual(150, alive);
            Assert.AreEqual(alive, VectraWatch.Count, "registered items are the enabled ones");
            Assert.AreEqual(0, VectraWatch.PendingCount);
        }

        [UnityTest]
        public IEnumerator DisabledShapes_KeepTheirSlotBriefly_ThenGiveItUp()
        {
            var canvas = NewCanvas();
            var group = new GameObject("Group", typeof(RectTransform));
            group.transform.SetParent(canvas, false);
            for (int i = 0; i < 100; i++) NewShape(group.transform, i);
            // Slots left over from earlier tests are given up first, so that the counts below only change by what this test does.
            for (int i = 0; i < 40; i++) yield return null;
            int enabled = VectraWatch.Count, retired = VectraWatch.RetiredCount;

            group.SetActive(false);
            yield return null;
            Assert.AreEqual(enabled - 100, VectraWatch.Count);
            Assert.AreEqual(retired + 100, VectraWatch.RetiredCount, "the slots are kept for a quick re-enable");

            group.SetActive(true);
            yield return null;
            Assert.AreEqual(enabled, VectraWatch.Count);
            Assert.AreEqual(retired, VectraWatch.RetiredCount, "taken back without registering again");
            Assert.AreEqual(0, VectraWatch.PendingCount);

            int before = VectraShape.s_Rebuilds;
            group.transform.localScale = Vector3.one * 3f;
            yield return null;
            yield return null;
            Assert.AreEqual(100, VectraShape.s_Rebuilds - before, "a taken-back slot still sees the scale change");

            group.SetActive(false);
            for (int i = 0; i < 60; i++) yield return null;
            Assert.AreEqual(enabled - 100, VectraWatch.Count);
            Assert.AreEqual(retired, VectraWatch.RetiredCount, "slots nobody took back are given up");
        }

        [UnityTest]
        public IEnumerator Renderer_MeshExistsRightAfterAddComponent_AndIdleFramesBuildNothing()
        {
            var go = new GameObject("World", typeof(VectraRenderer));
            m_Owned.Add(go);
            var r = go.GetComponent<VectraRenderer>();
            r.Size = new Vector2(2f, 1f);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh, "the mesh exists at once, as before");
            Assert.Greater(mesh.vertexCount, 0);
            Assert.Greater(r.Stats.vertices, 0);
            yield return null;
            yield return null;
            Assert.Greater(go.GetComponent<MeshRenderer>().bounds.size.x, 1.5f, "bounds follow the setup after the frame");
            int before = VectraRenderer.s_Rebuilds;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(0, VectraRenderer.s_Rebuilds - before);
        }

        [UnityTest]
        public IEnumerator Renderer_GeneratedCollider_ExistsRightAfterEnable()
        {
            var go = new GameObject("World");
            m_Owned.Add(go);
            go.SetActive(false);
            var r = go.AddComponent<VectraRenderer>();
            typeof(VectraRenderer).GetField("m_GenerateCollider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(r, true);
            go.SetActive(true);
            Assert.IsNotNull(go.GetComponent<PolygonCollider2D>(), "physics queries in the same frame see the shape");
            yield return null;
        }

        static Color[] Capture(Camera cam, RenderTexture rt)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            var px = tex.GetPixels();
            Object.Destroy(tex);
            RenderTexture.active = prev;
            return px;
        }

        // A renderer dirtied after setup is built by the first render; the atlas rows it bakes (or grows to hold) must be uploaded before that
        // camera draws. Reference: the same scene built by hand and flushed before rendering.
        [UnityTest]
        public IEnumerator RenderTimeBuild_UploadsTheAtlasBeforeDrawing([Values(8, 300)] int count)
        {
            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            m_Owned.Add(rt);
            var camGo = new GameObject("Cam") { tag = "MainCamera" };
            m_Owned.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 8f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.targetTexture = rt;
            camGo.transform.position = new Vector3(0, 0, -10);
            var list = new List<VectraRenderer>();
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("W" + i, typeof(VectraRenderer));
                m_Owned.Add(go);
                go.transform.position = new Vector3(-7f + (i % 15), 7f - (i / 15), 0f);
                var r = go.GetComponent<VectraRenderer>();
                r.Size = new Vector2(0.8f, 0.8f);
                list.Add(r);
            }

            yield return null;
            // New gradients, set from code after the frame: the next render builds them (and may have to grow the atlas).
            for (int i = 0; i < count; i++)
            {
                var r = list[i];
                r.Fills.Clear();
                r.Fills.Add(new FillLayer(VectraPaint.Linear(Color.HSVToRGB((i * 0.017f) % 1f, 1f, 1f), Color.HSVToRGB((i * 0.031f + 0.5f) % 1f, 1f, 0.5f), 90f)));
                r.SetShapeDirty();
            }

            var direct = Capture(cam, rt);
            foreach (var r in list) r.Rebuild();
            VectraAtlases.Flush();
            var reference = Capture(cam, rt);
            int different = 0;
            for (int i = 0; i < direct.Length; i++)
                if (direct[i] != reference[i]) different++;
            Assert.AreEqual(0, different, "pixels that differ between a render-time build and a hand built scene");
        }

        [UnityTest]
        public IEnumerator BooleanGroup_NoticesAnOperandAddedToAChild_InTheSameFrame()
        {
            var canvas = NewCanvas();
            var go = new GameObject("Group", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape), typeof(VectraBooleanGroup));
            go.transform.SetParent(canvas, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(100f, 100f);
            var child = NewShape(go.transform, 1);
            yield return null;
            yield return null;
            var owner = go.GetComponent<VectraShape>();
            var shape = owner.Geometry as BooleanShape;
            Assert.IsNotNull(shape);
            Assert.AreEqual(BooleanOp.Union, shape.operands[0].operation);
            child.gameObject.AddComponent<VectraBooleanOperand>().operation = BooleanOp.Subtract;
            yield return null;
            Assert.AreEqual(BooleanOp.Subtract, ((BooleanShape)owner.Geometry).operands[0].operation, "noticed in the next LateUpdate, not within 16 ticks");
        }

        [UnityTest]
        public IEnumerator SvgImageCanvas_RenderModeSwitchWithoutTransformChange_IsNoticedNextFrame()
        {
            var canvas = NewCanvas();
            var shape = NewShape(canvas, 3);
            for (int i = 0; i < 4; i++) yield return null;
            var camGo = new GameObject("Cam");
            m_Owned.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            var c = canvas.GetComponent<Canvas>();
            int before = VectraShape.s_Rebuilds;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            yield return null;
            // The canvas change is seen at once (every shape is checked); whether it rebuilds depends on the density, so only the check is asserted.
            Assert.GreaterOrEqual(VectraWatch.LastTicked, 1);
            Assert.IsNotNull(shape);
        }

        [UnityTest]
        public IEnumerator Renderer_FollowsTheMainCamerasZoom_AndNothingElse()
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            m_Owned.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            camGo.transform.position = new Vector3(0f, 0f, -10f);

            var renderers = new List<VectraRenderer>();
            for (int i = 0; i < 20; i++)
            {
                var go = new GameObject("W" + i, typeof(VectraRenderer));
                m_Owned.Add(go);
                go.transform.position = new Vector3(i * 0.5f, 0f, 0f);
                renderers.Add(go.GetComponent<VectraRenderer>());
            }

            for (int i = 0; i < 6; i++) yield return null;
            int before = VectraRenderer.s_Rebuilds;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(0, VectraRenderer.s_Rebuilds - before, "nothing changes, nothing is built");

            cam.orthographicSize = 1f; // a pixel is five times smaller: over the 1.5 band
            yield return null;
            yield return null;
            Assert.AreEqual(20, VectraRenderer.s_Rebuilds - before, "every renderer follows the zoom");

            before = VectraRenderer.s_Rebuilds;
            renderers[3].transform.localScale = Vector3.one * 4f;
            yield return null;
            yield return null;
            Assert.AreEqual(1, VectraRenderer.s_Rebuilds - before, "only the scaled renderer");
        }

        [UnityTest]
        public IEnumerator Renderer_SetDirty_BuildsInTheSameFrame()
        {
            var go = new GameObject("World", typeof(VectraRenderer));
            m_Owned.Add(go);
            var r = go.GetComponent<VectraRenderer>();
            yield return null;
            yield return null;
            int before = VectraRenderer.s_Rebuilds;
            r.Color = Color.blue;
            yield return null;
            Assert.AreEqual(1, VectraRenderer.s_Rebuilds - before);
        }
    }
}
