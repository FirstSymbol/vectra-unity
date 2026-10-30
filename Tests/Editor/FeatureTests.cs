using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>Sprite tracing, path conversion, shared shape assets and the new paints and effects.</summary>
    public class FeatureTests
    {
        static float[] Disc(int size, float radius, float hole = 0f)
        {
            var a = new float[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                a[y * size + x] = d <= radius && d >= hole ? 1f : 0f;
            }

            return a;
        }

        static float Area(Vector2[] p)
        {
            float s = 0f;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++) s += (p[j].x - p[i].x) * (p[j].y + p[i].y);
            return Mathf.Abs(s * 0.5f);
        }

        [Test]
        public void Tracer_DiscAreaMatchesPiR2()
        {
            var contours = VectraSpriteTracer.TraceAlpha(Disc(128, 50f), 128, 128, 0.5f, 128, 0.5f);
            Assert.AreEqual(1, contours.Count);
            float area = Area(contours[0]);
            float expected = Mathf.PI * (50f / 128f) * (50f / 128f);
            Assert.AreEqual(expected, area, expected * 0.03f, "normalized area of the traced disc");
            Assert.Less(contours[0].Length, 120, "simplification keeps the point count low");
        }

        [Test]
        public void Tracer_RingHasOuterAndInnerContour()
        {
            var contours = VectraSpriteTracer.TraceAlpha(Disc(96, 40f, 20f), 96, 96, 0.5f, 96, 0.5f);
            Assert.AreEqual(2, contours.Count);
        }

        [Test]
        public void Tracer_ResolutionDownsamples()
        {
            var coarse = VectraSpriteTracer.TraceAlpha(Disc(256, 100f), 256, 256, 0.5f, 32, 0f);
            var fine = VectraSpriteTracer.TraceAlpha(Disc(256, 100f), 256, 256, 0.5f, 256, 0f);
            Assert.Less(coarse[0].Length, fine[0].Length);
        }

        [Test]
        public void PathConverter_KeepsTheOutline()
        {
            var frame = new ShapeFrame { rect = new Rect(-50, -40, 100, 80), pixelSize = 1f };
            var rect = new RectangleShape { cornerRadii = new Vector4(10, 10, 10, 10) };
            var path = VectraPathConverter.ToPathShape(rect, frame);
            Assert.AreEqual(1, path.contours.Count);
            Assert.IsTrue(path.contours[0].closed);
            Assert.GreaterOrEqual(path.contours[0].points.Count, 8, "four corners, two points each");
            Assert.AreEqual(OutlineArea(rect, frame), OutlineArea(path, frame), 1.0, "area of the converted path");
        }

        [Test]
        public void PathConverter_StarFromSvgPath()
        {
            var frame = new ShapeFrame { rect = new Rect(0, 0, 100, 100), pixelSize = 1f };
            var svg = new SvgPathShape();
            var path = VectraPathConverter.ToPathShape(svg, frame);
            Assert.AreEqual(1, path.contours.Count);
            Assert.AreEqual(10, path.contours[0].points.Count, "the default SVG star has 10 corners");
            Assert.AreEqual(OutlineArea(svg, frame), OutlineArea(path, frame), 0.5);
        }

        static double OutlineArea(VectraShapeGeometry g, in ShapeFrame frame)
        {
            var path = new NativePath(Allocator.TempJob, 32);
            var poly = new Polylines(Allocator.TempJob, 64);
            var traps = new NativeList<Trapezoid>(64, Allocator.TempJob);
            g.BuildPath(frame, new PathBuilder(path));
            VectraGeometry.Flatten(path, Affine2.Identity, 0.05f, 0, true, ref poly);
            VectraGeometry.Tessellate(poly, g.FillRule, ref traps);
            double area = ScanlineTessellator.Area(traps.AsArray());
            path.Dispose();
            poly.Dispose();
            traps.Dispose();
            return area;
        }

        [Test]
        public void ShapeAsset_OverridesLocalGeometry()
        {
            var asset = ScriptableObject.CreateInstance<VectraShapeAsset>();
            asset.geometry = new StarShape();
            var content = new VectraShapeContent { geometry = new EllipseShape(), shapeAsset = asset };
            Assert.IsInstanceOf<StarShape>(content.ActiveGeometry);
            Assert.IsInstanceOf<StarShape>(content.ToInput(new ShapeFrame { rect = new Rect(0, 0, 10, 10), pixelSize = 1f }, Color.white, false).geometry);
            content.DetachShape();
            Assert.IsNull(content.shapeAsset);
            Assert.IsInstanceOf<StarShape>(content.geometry);
            Assert.AreNotSame(asset.geometry, content.geometry, "detaching copies the geometry");
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void NewPaintsAndEffects_BuildWithoutErrors()
        {
            var renderer = new Rendering.VectraShapeRenderer();
            try
            {
                var frame = new ShapeFrame { rect = new Rect(-50, -50, 100, 100), pixelSize = 1f };
                foreach (var geometry in new VectraShapeGeometry[] { new StarShape(), new GearShape() })
                {
                    var content = new VectraShapeContent { geometry = geometry };
                    content.fills[0].paint = VectraPaint.Contour(Color.red, Color.blue, 20f);
                    content.effects.Add(new ExtrudeEffect());
                    content.effects.Add(new DeformEffect());
                    renderer.Build(content.ToInput(frame, Color.white, false));
                    Assert.Greater(renderer.buffer.VertexCount, 0, geometry.DisplayName);
                    if (geometry is GearShape) Assert.IsTrue(renderer.stats.usesSdf, "path shapes draw contour paints from a distance field");
                }
            }
            finally
            {
                renderer.Dispose();
            }
        }

        [Test]
        public void Encoding_Pack6RoundTrips()
        {
            float packed = Rendering.VectraEncoding.Pack6(0.25f, 0.5f, 1f, 0f);
            int v = (int)packed;
            Assert.AreEqual(16, v >> 18);
            Assert.AreEqual(32, (v >> 12) & 63);
            Assert.AreEqual(63, (v >> 6) & 63);
            Assert.AreEqual(0, v & 63);
            Assert.Less(packed, 16777216f, "exact in a float");
        }
    }
}
