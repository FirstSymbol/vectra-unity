using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using FirstSymbol.Vectra.Rendering;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>
    /// A tint change alone rewrites the vertex colours of the last build instead of building again; the result must be
    /// exactly what a build with that tint writes, and any other change must still build.
    /// </summary>
    public class ColorOnlyPlayModeTests
    {
        readonly List<Object> m_Owned = new List<Object>();
        Canvas m_Canvas;

        [SetUp]
        public void SetUp()
        {
            var camGo = new GameObject("Color Test Camera");
            m_Owned.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            var canvasGo = new GameObject("Color Test Canvas", typeof(Canvas));
            m_Owned.Add(canvasGo);
            m_Canvas = canvasGo.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            m_Canvas.worldCamera = cam;
            m_Canvas.planeDistance = 5f;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in m_Owned)
                if (o != null) Object.Destroy(o);
            m_Owned.Clear();
        }

        const int Configurations = 8;

        VectraShape Make(int kind, Color tint)
        {
            var go = new GameObject("S" + kind, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
            go.transform.SetParent(m_Canvas.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(64, 48);
            var s = go.GetComponent<VectraShape>();
            switch (kind)
            {
                case 0:
                    s.SetGeometry<RectangleShape>().cornerRadii = new Vector4(8, 8, 8, 8);
                    s.SetFill(new Color(0.9f, 0.3f, 0.2f, 0.8f));
                    break;
                case 1:
                    s.SetGeometry<EllipseShape>();
                    s.SetFill(VectraPaint.Linear(Color.red, Color.blue, 40f));
                    s.Fills[0].paint.opacity = 0.6f;
                    break;
                case 2:
                    s.SetGeometry<StarShape>();
                    s.SetFill(new Color(0.2f, 0.7f, 0.4f));
                    s.SetStroke(new Color(0f, 0.2f, 0.9f, 0.7f), 3f);
                    break;
                case 3:
                    s.SetGeometry<RectangleShape>().cornerRadii = new Vector4(4, 4, 4, 4);
                    s.Fills.Clear();
                    s.SetStroke(Color.yellow, 2f);
                    s.Strokes[0].dashed = true;
                    break;
                case 4:
                    s.SetGeometry<RectangleShape>().cornerRadii = new Vector4(6, 6, 6, 6);
                    s.SetFill(new Color(0.3f, 0.4f, 0.9f));
                    s.AddEffect<DropShadowEffect>();
                    break;
                case 5:
                    s.SetGeometry<EllipseShape>();
                    s.SetFill(new Color(1f, 0.8f, 0.1f));
                    s.AddEffect<InnerGlowEffect>();
                    s.AddEffect<OuterGlowEffect>();
                    break;
                case 6:
                    s.SetGeometry<RectangleShape>().cornerRadii = new Vector4(10, 10, 10, 10);
                    s.SetFill(new Color(0.5f, 0.5f, 0.5f));
                    s.SetStroke(new Color(1f, 1f, 1f, 0.5f), 4f, StrokeAlignment.Inside);
                    s.Fills.Add(new FillLayer(VectraPaint.Solid(new Color(0.1f, 0.8f, 0.8f, 0.5f))));
                    break;
                default:
                    s.SetGeometry<HeartShape>();
                    s.SetFill(new Color(0.9f, 0.1f, 0.5f));
                    s.AddEffect<ColorAdjustEffect>().saturation = 0.7f;
                    break;
            }

            s.color = tint;
            return s;
        }

        static byte[] Bytes(VectraShape s)
        {
            var v = s.BuiltRenderer.buffer.vertices.AsArray();
            var bytes = new byte[v.Length * UnsafeUtility.SizeOf<VectraVertex>()];
            if (bytes.Length > 0) NativeArray<byte>.Copy(v.Reinterpret<byte>(UnsafeUtility.SizeOf<VectraVertex>()), bytes, bytes.Length);
            return bytes;
        }

        [UnityTest]
        public IEnumerator ColorOnlyChange_WritesWhatABuildWrites()
        {
            var tints = new[]
            {
                new Color(0.25f, 0.5f, 1f, 1f), new Color(1f, 1f, 1f, 0.35f), new Color(0.9f, 0.2f, 0.4f, 0.8f), Color.white
            };
            for (int kind = 0; kind < Configurations; kind++)
            {
                var a = Make(kind, Color.white);
                yield return null;
                Canvas.ForceUpdateCanvases();
                int builds = a.BuiltRenderer.buildCount;
                Assert.Greater(builds, 0, "config " + kind + " built");
                foreach (var tint in tints)
                {
                    a.color = tint;
                    Canvas.ForceUpdateCanvases();
                    Assert.AreEqual(builds, a.BuiltRenderer.buildCount, "config " + kind + ": the tint change did not build again");
                    var twin = Make(kind, tint);
                    Canvas.ForceUpdateCanvases();
                    CollectionAssert.AreEqual(Bytes(twin), Bytes(a), "config " + kind + " tint " + tint + ": vertices equal a fresh build");
                    Object.Destroy(twin.gameObject);
                }
            }
        }

        [UnityTest]
        public IEnumerator OtherChange_AfterATintChange_StillBuilds()
        {
            var a = Make(0, Color.white);
            yield return null;
            Canvas.ForceUpdateCanvases();
            int builds = a.BuiltRenderer.buildCount;

            // Tint and fill changed before one rebuild: the fill must show.
            a.color = new Color(0.5f, 0.8f, 1f, 0.9f);
            a.SetFill(new Color(0.1f, 0.9f, 0.3f));
            Canvas.ForceUpdateCanvases();
            Assert.Greater(a.BuiltRenderer.buildCount, builds, "built again");
            var twin = Make(0, new Color(0.5f, 0.8f, 1f, 0.9f));
            twin.SetFill(new Color(0.1f, 0.9f, 0.3f));
            Canvas.ForceUpdateCanvases();
            CollectionAssert.AreEqual(Bytes(twin), Bytes(a));

            // A size change after a tint change.
            builds = a.BuiltRenderer.buildCount;
            a.color = Color.white;
            ((RectTransform)a.transform).sizeDelta = new Vector2(80, 30);
            Canvas.ForceUpdateCanvases();
            Assert.Greater(a.BuiltRenderer.buildCount, builds, "size change builds");

            // An edit of the content announced with SetVerticesDirty, together with a tint change.
            builds = a.BuiltRenderer.buildCount;
            a.color = new Color(1f, 0.5f, 0.5f, 1f);
            a.Fills[0].paint = VectraPaint.Solid(new Color(0.9f, 0.9f, 0.1f));
            a.SetVerticesDirty();
            Canvas.ForceUpdateCanvases();
            Assert.Greater(a.BuiltRenderer.buildCount, builds, "SetVerticesDirty builds");
        }

        [UnityTest]
        public IEnumerator DisableEnable_Builds()
        {
            var a = Make(1, Color.white);
            yield return null;
            Canvas.ForceUpdateCanvases();
            a.color = new Color(1f, 0.5f, 0.5f, 1f);
            a.enabled = false;
            a.enabled = true;
            int builds = a.BuiltRenderer.buildCount;
            Canvas.ForceUpdateCanvases();
            Assert.Greater(a.BuiltRenderer.buildCount, builds, "re-enabled graphic builds");
        }
    }
}
