using System.Collections;
using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>Edge cases of the mesh upload, the colour-only update, the stroke cache and the scratch buffers.</summary>
    public class OptimizationReviewPlayModeTests
    {
        readonly List<Object> m_Owned = new List<Object>();
        Canvas m_Canvas;

        [SetUp]
        public void SetUp()
        {
            var camGo = new GameObject("Review Camera");
            m_Owned.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            var canvasGo = new GameObject("Review Canvas", typeof(Canvas));
            m_Owned.Add(canvasGo);
            m_Canvas = canvasGo.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            m_Canvas.worldCamera = cam;
            m_Canvas.planeDistance = 5f;
            VectraStrokeCache.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in m_Owned)
                if (o != null) Object.Destroy(o);
            m_Owned.Clear();
        }

        VectraShape Shape(float scale = 1f)
        {
            var go = new GameObject("S", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
            go.transform.SetParent(m_Canvas.transform, false);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            ((RectTransform)go.transform).sizeDelta = new Vector2(60, 40);
            return go.GetComponent<VectraShape>();
        }

        static byte[] Bytes(VectraShape s)
        {
            var v = s.BuiltRenderer.buffer.vertices.AsArray();
            var bytes = new byte[v.Length * UnsafeUtility.SizeOf<VectraVertex>()];
            if (bytes.Length > 0) NativeArray<byte>.Copy(v.Reinterpret<byte>(UnsafeUtility.SizeOf<VectraVertex>()), bytes, bytes.Length);
            return bytes;
        }

        VectraShape Styled(float scale, Color tint)
        {
            var s = Shape(scale);
            s.SetGeometry<RectangleShape>().cornerRadii = new Vector4(9, 9, 9, 9);
            s.SetFill(new Color(0.3f, 0.6f, 0.9f));
            s.SetStroke(Color.black, 2f);
            s.Strokes[0].dashed = true;
            s.color = tint;
            return s;
        }

        [UnityTest]
        public IEnumerator ScaleDriftBelowTheRebuildThreshold_ThenTint_EqualsAFreshBuild()
        {
            var tint = new Color(1f, 0.4f, 0.4f, 0.7f);
            var a = Styled(1f, Color.white);
            yield return null;
            Canvas.ForceUpdateCanvases();
            a.transform.localScale = new Vector3(1.2f, 1.2f, 1f);
            a.color = tint;
            Canvas.ForceUpdateCanvases(); // before LateUpdate can notice the drift
            var twin = Styled(1.2f, tint);
            Canvas.ForceUpdateCanvases();
            CollectionAssert.AreEqual(Bytes(twin), Bytes(a));
        }

        static VectraMeshBuffer MakeBuffer(int vertices, int indexCount)
        {
            var b = new VectraMeshBuffer();
            for (int i = 0; i < vertices; i++) b.vertices.Add(new VectraVertex { position = new float3(i % 7, i % 5, 0), color = new Color32(1, 2, 3, 4) });
            for (int i = 0; i < indexCount; i++) b.indices.Add(i % vertices);
            return b;
        }

        [Test]
        public void BigMeshThenSmall_OnTheSameMesh_SwitchesIndexFormatCleanly()
        {
            var mesh = new Mesh();
            m_Owned.Add(mesh);
            var big = MakeBuffer(70000, 90);
            var small = MakeBuffer(4, 6);
            big.Upload(mesh);
            Assert.AreEqual(IndexFormat.UInt32, mesh.indexFormat);
            Assert.AreEqual(70000, mesh.vertexCount);
            small.Upload(mesh);
            Assert.AreEqual(IndexFormat.UInt16, mesh.indexFormat);
            Assert.AreEqual(4, mesh.vertexCount);
            Assert.AreEqual(6u, mesh.GetIndexCount(0));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 0, 1 }, mesh.GetIndices(0));
            big.Upload(mesh);
            Assert.AreEqual(70000, mesh.vertexCount);
            LogAssert.NoUnexpectedReceived();
            big.Dispose();
            small.Dispose();
        }

        [Test]
        public void NonFiniteVertices_AreZeroedLikeBefore()
        {
            var mesh = new Mesh();
            m_Owned.Add(mesh);
            var b = new VectraMeshBuffer();
            b.vertices.Add(new VectraVertex { position = new float3(float.NaN, 1, 2), uv0 = new float4(1, float.PositiveInfinity, 3, 4), uv3 = new float4(float.NaN, 2, 3, 4) });
            b.vertices.Add(new VectraVertex { position = new float3(1, 2, 3), uv1 = new float4(5, 6, 7, 8) });
            b.vertices.Add(new VectraVertex());
            b.indices.Add(0);
            b.indices.Add(1);
            b.indices.Add(2);
            b.Upload(mesh);
            var mda = Mesh.AcquireReadOnlyMeshData(mesh);
            var v = mda[0].GetVertexData<VectraVertex>(0);
            Assert.AreEqual(float3.zero, v[0].position);
            Assert.AreEqual(new float4(1, 0, 3, 4), v[0].uv0);
            Assert.AreEqual(new float4(0, 2, 3, 4), v[0].uv3);
            Assert.AreEqual(new float3(1, 2, 3), v[1].position);
            Assert.AreEqual(new float4(5, 6, 7, 8), v[1].uv1);
            mda.Dispose();
            b.Dispose();
        }

        [UnityTest]
        public IEnumerator BooleanGroup_ChildCountShrinksAndGrows()
        {
            var owner = Shape();
            owner.gameObject.AddComponent<VectraBooleanGroup>();
            var kids = new List<GameObject>();
            for (int i = 0; i < 3; i++)
            {
                var k = new GameObject("K" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
                k.transform.SetParent(owner.transform, false);
                ((RectTransform)k.transform).sizeDelta = new Vector2(20, 20);
                k.GetComponent<VectraShape>().SetGeometry<EllipseShape>();
                kids.Add(k);
            }

            yield return null;
            Canvas.ForceUpdateCanvases();
            var shape = (BooleanShape)owner.Geometry;
            Assert.AreEqual(3, shape.operands.Count);
            kids[2].SetActive(false);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(2, shape.operands.Count);
            kids[2].SetActive(true);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(3, shape.operands.Count);
            foreach (var o in shape.operands) Assert.IsNotNull(o.geometry);
        }

        VectraShape PathShape(VectraShapeGeometry g)
        {
            var s = Shape();
            s.Geometry = g;
            s.Content.forcePath = true;
            s.SetFill(new Color(0.9f, 0.5f, 0.1f));
            return s;
        }

        [UnityTest]
        public IEnumerator StarPolygonPolyline_AreUnchangedByTheSharedScratch()
        {
            var a = PathShape(new StarShape { points = 5 });
            var b = PathShape(new PolygonShape { sides = 5 });
            var c = PathShape(new StarShape { points = 7 });
            var poly = PathShape(new PolylineShape { closed = true });
            yield return null;
            Canvas.ForceUpdateCanvases();
            var expected = Bytes(a);
            var twin = PathShape(new StarShape { points = 5 });
            Canvas.ForceUpdateCanvases();
            CollectionAssert.AreEqual(expected, Bytes(twin));
            Assert.Greater(Bytes(b).Length, 0);
            Assert.Greater(Bytes(poly).Length, 0);
            Assert.AreNotEqual(Bytes(a).Length, Bytes(c).Length);
        }

        [UnityTest]
        public IEnumerator StrokeCache_KeysDifferOnDashRelativeAndTrimOffset()
        {
            VectraShape Make(int variant)
            {
                var s = Shape();
                s.SetGeometry<RectangleShape>().cornerRadii = new Vector4(5, 5, 5, 5);
                s.Fills.Clear();
                s.SetStroke(Color.red, 3f);
                var st = s.Strokes[0];
                st.dashed = true;
                st.dashPattern = new List<float> { 6f, 4f };
                if (variant == 1) { st.dashRelative = true; st.dashPattern = new List<float> { 0.2f, 0.1f }; }
                if (variant == 2) { st.trimStart = 0.1f; st.trimEnd = 0.8f; st.trimOffset = 0.3f; }
                return s;
            }

            var first = new List<byte[]>();
            for (int round = 0; round < 3; round++)
            for (int v = 0; v < 3; v++)
            {
                var s = Make(v);
                yield return null;
                Canvas.ForceUpdateCanvases();
                if (round == 0) first.Add(Bytes(s));
                else CollectionAssert.AreEqual(first[v], Bytes(s), "variant " + v + " round " + round);
            }

            CollectionAssert.AreNotEqual(first[0], first[1]);
            CollectionAssert.AreNotEqual(first[1], first[2]);
        }
    }
}
