using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class BooleanAllocationTests
    {
        static VectraBuildInput Input(VectraShapeGeometry g) => new VectraBuildInput
        {
            geometry = g,
            fills = new List<FillLayer> { new FillLayer(VectraPaint.Linear(Color.red, Color.blue)) },
            strokes = new List<StrokeLayer>(),
            effects = new List<VectraEffect>(),
            frame = new ShapeFrame { rect = new Rect(-50, -40, 100, 80), pixelSize = 1f },
            tint = Color.white,
            quality = VectraQuality.High
        };

        static BooleanShape Boolean(float smoothness)
        {
            var b = new BooleanShape { smoothness = smoothness };
            b.operands.Add(new BooleanOperand(new RectangleShape(), BooleanOp.Subtract, new Rect(0.2f, 0.2f, 0.4f, 0.4f)));
            return b;
        }

        [Test]
        public void Probe_SeesManagedAllocations()
        {
            var sink = new List<byte[]>();
            var (bytes, count) = GcProbe.Measure(i => sink.Add(new byte[1000]), 100);
            Assert.GreaterOrEqual(bytes, 100000, "bytes the probe saw");
            Assert.GreaterOrEqual(count, 100, "allocations the probe saw");
        }

        [TestCase(0f)]
        [TestCase(6f)]
        public void Rebuild_AfterAnOperandMoves_AllocatesNothing(float smoothness)
        {
            if (smoothness > 0f && SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            using var r = new VectraShapeRenderer();
            var shape = Boolean(smoothness);
            var input = Input(shape);
            var (bytes, count) = GcProbe.Measure(i =>
            {
                shape.operands[1].rect = new Rect(0.3f + (i % 7) * 0.01f, 0.3f, 0.7f, 0.7f);
                r.Build(input);
            }, 20, out string all);
            Debug.Log($"VECTRA-METRIC boolean smoothness {smoothness}: {bytes} bytes in {count} allocations over 20 rebuilds");
            Assert.Greater(r.buffer.VertexCount, 0);
            Assert.AreEqual(0, bytes, "managed bytes on this thread (" + all + ")");
            Assert.AreEqual(0, count, "managed allocations");
        }

        [Test]
        public void Rebuild_WhenTheDistanceRegionChangesSize_AllocatesNothing()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            using var r = new VectraShapeRenderer();
            var shape = Boolean(6f);
            var input = Input(shape);
            // The union grows and shrinks, so the block sent to the atlas changes size from one rebuild to the next.
            var (bytes, count) = GcProbe.Measure(i =>
            {
                shape.operands[1].rect = new Rect(0.2f, 0.2f, 0.3f + (i % 9) * 0.07f, 0.5f);
                r.Build(input);
                VectraAtlases.Flush();
            }, 18, out string all);
            Assert.AreEqual(0, bytes, "managed bytes on this thread (" + all + ")");
            Assert.AreEqual(0, count, "managed allocations");
        }

        [Test]
        public void NestedBoolean_MatchesTheFlatEquivalent()
        {
            var inner = new BooleanShape();
            inner.operands.Clear();
            inner.operands.Add(new BooleanOperand(new RectangleShape(), BooleanOp.Union, new Rect(0f, 0f, 0.5f, 0.5f)));
            inner.operands.Add(new BooleanOperand(new EllipseShape(), BooleanOp.Union, new Rect(0.3f, 0.3f, 0.5f, 0.5f)));
            var outer = new BooleanShape();
            outer.operands.Clear();
            outer.operands.Add(new BooleanOperand(inner, BooleanOp.Union, new Rect(0f, 0f, 1f, 1f)));
            outer.operands.Add(new BooleanOperand(new RectangleShape(), BooleanOp.Subtract, new Rect(0.4f, 0.4f, 0.2f, 0.2f)));

            using var nested = new VectraShapeRenderer();
            nested.Build(Input(outer));
            Assert.Greater(nested.buffer.VertexCount, 0);

            var flat = new BooleanShape();
            flat.operands.Clear();
            flat.operands.Add(new BooleanOperand(new RectangleShape(), BooleanOp.Union, new Rect(0f, 0f, 0.5f, 0.5f)));
            flat.operands.Add(new BooleanOperand(new EllipseShape(), BooleanOp.Union, new Rect(0.3f, 0.3f, 0.5f, 0.5f)));
            flat.operands.Add(new BooleanOperand(new RectangleShape(), BooleanOp.Subtract, new Rect(0.4f, 0.4f, 0.2f, 0.2f)));
            using var reference = new VectraShapeRenderer();
            reference.Build(Input(flat));

            Assert.AreEqual(reference.buffer.VertexCount, nested.buffer.VertexCount, "vertices of the nested result");
        }

        // A boolean that contains itself used to recurse until the stack overflowed (the hash, the flattening).
        [Test]
        public void BooleansThatContainEachOther_StopInsteadOfOverflowing()
        {
            var a = new BooleanShape();
            var b = new BooleanShape();
            a.operands[0].geometry = b;
            b.operands[0].geometry = a;
            Assert.DoesNotThrow(() => a.ComputeHash());
            using var r = new VectraShapeRenderer();
            Assert.DoesNotThrow(() => r.Build(Input(a)));
        }
    }
}
