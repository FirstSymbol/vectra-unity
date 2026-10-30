using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class RenderingTests
    {
        static VectraBuildInput Input(VectraShapeGeometry g, List<VectraEffect> effects = null, List<StrokeLayer> strokes = null) => new VectraBuildInput
        {
            geometry = g,
            fills = new List<FillLayer> { new FillLayer(VectraPaint.Linear(Color.red, Color.blue)) },
            strokes = strokes ?? new List<StrokeLayer>(),
            effects = effects ?? new List<VectraEffect>(),
            frame = new ShapeFrame { rect = new Rect(-50, -40, 100, 80), pixelSize = 1f },
            tint = Color.white,
            quality = VectraQuality.High
        };

        static IEnumerable<Type> ShapeTypes()
        {
            foreach (var t in TypeCache.GetTypesDerivedFrom<VectraShapeGeometry>())
                if (!t.IsAbstract && t.IsPublic && t.GetConstructor(Type.EmptyTypes) != null) yield return t;
        }

        [Test]
        public void EveryShape_BuildsAnalyticAndPathMeshes([ValueSource(nameof(ShapeTypes))] Type type)
        {
            using var r = new VectraShapeRenderer();
            var g = (VectraShapeGeometry)Activator.CreateInstance(type);
            var strokes = new List<StrokeLayer> { new StrokeLayer(Color.black, 3f) };
            var effects = new List<VectraEffect> { new DropShadowEffect(), new InnerShadowEffect() };
            r.Build(Input(g, effects, strokes));
            Assert.Greater(r.buffer.VertexCount, 0, "default build");
            var forced = Input(g, effects, strokes);
            forced.forcePath = true;
            r.Build(forced);
            Assert.Greater(r.buffer.VertexCount, 0, "path build");
            Assert.AreEqual(0, r.buffer.indices.Length % 3);
            for (int i = 0; i < r.buffer.vertices.Length; i++)
            {
                var v = r.buffer.vertices[i];
                Assert.IsTrue(Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(v.uv1)) &&
                              Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(v.uv2)), "finite parameters");
            }
        }

        [Test]
        public void AnalyticRectangle_IsOneQuadPerLayer()
        {
            using var r = new VectraShapeRenderer();
            r.Build(Input(new RectangleShape()));
            Assert.IsTrue(r.stats.analytic);
            Assert.AreEqual(4, r.buffer.VertexCount);
            Assert.AreEqual(6, r.buffer.indices.Length);
        }

        [Test]
        public void Rebuild_IsAllocationFree()
        {
            using var r = new VectraShapeRenderer();
            var input = Input(new StarShape(), new List<VectraEffect> { new DropShadowEffect() },
                new List<StrokeLayer> { new StrokeLayer(Color.black, 2f) { join = LineJoin.Round } });
            r.Build(input);
            r.Build(input);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) r.Build(input);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "bytes allocated by 10 rebuilds");
        }

        [Test]
        public void Header_RoundTripsAboveTwoPow23()
        {
            float h = VectraEncoding.Header(ShapeKind.Star, LayerKind.Shadow, PaintKind.Solid, 1, ModifierKind.None, false, false, 0, true);
            Assert.Greater(h, 8388608f);
            uint u = (uint)Mathf.Round(h);
            Assert.AreEqual((uint)ShapeKind.Star, u & 31u);
            Assert.AreEqual((uint)LayerKind.Shadow, (u >> 5) & 15u);
            Assert.AreEqual(1u, (u >> 23) & 1u);
        }

        [Test]
        public void LogCodes_RoundTrip()
        {
            foreach (float v in new[] { 0.01f, 0.5f, 3f, 120f, 900f })
            {
                Assert.AreEqual(v, VectraEncoding.DecodeLog12(VectraEncoding.Log12(v)), v * 0.01f);
                Assert.AreEqual(-v, VectraEncoding.DecodeSignedLog12(VectraEncoding.SignedLog12(-v)), v * 0.01f);
            }
        }

        [Test]
        public void Shapes_ShareMaterialAndTexture_ForBatching()
        {
            var go = new GameObject("Canvas", typeof(Canvas));
            try
            {
                var a = new GameObject("A", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
                var b = new GameObject("B", typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
                a.transform.SetParent(go.transform);
                b.transform.SetParent(go.transform);
                var sa = a.GetComponent<VectraShape>();
                var sb = b.GetComponent<VectraShape>();
                sa.SetGeometry<EllipseShape>();
                sb.SetFill(VectraPaint.Radial(Color.white, Color.black));
                Canvas.ForceUpdateCanvases();
                Assert.AreSame(sa.materialForRendering, sb.materialForRendering);
                Assert.AreSame(sa.mainTexture, sb.mainTexture);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
