using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class AtlasUploadTests
    {
        static VectraBuildInput Star(float width) => new VectraBuildInput
        {
            geometry = new StarShape(),
            fills = new List<FillLayer> { new FillLayer(VectraPaint.Linear(Color.red, Color.blue)) },
            strokes = new List<StrokeLayer>(),
            effects = new List<VectraEffect> { new DropShadowEffect() },
            frame = new ShapeFrame { rect = new Rect(-width * 0.5f, -40f, width, 80f), pixelSize = 1f },
            tint = Color.white,
            quality = VectraQuality.High,
            forcePath = true,
            requireDistance = true,
            distanceReach = 8f
        };

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            VectraSdfAtlas.Reset();
            VectraAtlases.Flush();
        }

        [Test]
        public void ChangingOneShape_UploadsOnlyItsRegion()
        {
            using var a = new VectraShapeRenderer();
            using var b = new VectraShapeRenderer();
            a.Build(Star(100f));
            b.Build(Star(60f));
            Assert.GreaterOrEqual(VectraSdfAtlas.AllocationCount, 2, "both shapes use the atlas");
            VectraAtlases.Flush();
            long atlas = (long)VectraSdfAtlas.Size * VectraSdfAtlas.Size;

            b.Build(Star(64f));
            VectraAtlases.Flush();
            long uploaded = VectraSdfAtlas.LastUploadTexels;
            Debug.Log($"VECTRA-METRIC atlas {VectraSdfAtlas.Size}x{VectraSdfAtlas.Size}, uploaded {uploaded} texels of {atlas} for one changed shape");
            Assert.Greater(uploaded, 0);
            Assert.Less(uploaded, atlas / 4, "texels uploaded for one changed shape");

            VectraAtlases.Flush();
            Assert.AreEqual(0, VectraSdfAtlas.LastUploadTexels, "nothing changed, nothing uploaded");
        }

        [Test]
        public void AtDefaultSize_ARegionUploadCostsFarLessThanTheWholeTexture()
        {
            int max = VectraSdfAtlas.MaxSize;
            VectraSdfAtlas.MaxSize = 2048;
            VectraSdfAtlas.Allocate(1100, 1100, out var big); // forces the atlas to 2048 x 2048
            using var a = new VectraShapeRenderer();
            try
            {
                Assert.AreEqual(2048, VectraSdfAtlas.Size);
                a.Build(Star(100f));
                VectraAtlases.Flush();

                const int rounds = 30;
                var sw = new System.Diagnostics.Stopwatch();
                long regionTexels = 0, fullTexels = 0;
                double regionMs, fullMs;
                for (int i = 0; i < rounds; i++)
                {
                    a.Build(Star(100f + (i % 2) * 8f));
                    sw.Start();
                    VectraAtlases.Flush();
                    sw.Stop();
                    regionTexels += VectraSdfAtlas.LastUploadTexels;
                }

                regionMs = sw.Elapsed.TotalMilliseconds / rounds;
                sw.Reset();
                for (int i = 0; i < rounds; i++)
                {
                    a.Build(Star(100f + (i % 2) * 8f));
                    VectraSdfAtlas.MarkDirty(); // what every change used to do
                    sw.Start();
                    VectraAtlases.Flush();
                    sw.Stop();
                    fullTexels += VectraSdfAtlas.LastUploadTexels;
                }

                fullMs = sw.Elapsed.TotalMilliseconds / rounds;
                Debug.Log($"VECTRA-METRIC 2048 atlas, one changed shape, per flush: region {regionTexels / rounds} texels {regionMs:F3} ms; whole texture {fullTexels / rounds} texels {fullMs:F3} ms");
                Assert.Less(regionTexels * 20, fullTexels, "texels uploaded by region flushes against whole-texture flushes");
            }
            finally
            {
                VectraSdfAtlas.Free(ref big);
                VectraSdfAtlas.MaxSize = max;
            }
        }

        [Test]
        public void IncrementalUploads_MatchTheCpuCopyOnTheGpu()
        {
            if (VectraSdfAtlas.Texture.format != TextureFormat.RHalf) Assert.Ignore("half-float atlas only");
            var renderers = new List<VectraShapeRenderer>();
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    var r = new VectraShapeRenderer();
                    renderers.Add(r);
                    r.Build(Star(40f + i * 13f));
                    if (i % 2 == 1) VectraAtlases.Flush();
                }

                VectraAtlases.Flush();
                renderers[1].Build(Star(90f));
                renderers[4].Build(Star(33f));
                VectraAtlases.Flush();
                renderers[2].Dispose();
                renderers[2] = new VectraShapeRenderer();
                renderers[2].Build(Star(120f));
                renderers[0].Build(Star(47f));
                VectraAtlases.Flush();

                var request = AsyncGPUReadback.Request(VectraSdfAtlas.Texture);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError);
                var gpu = request.GetData<Unity.Mathematics.half>();
                var cpu = VectraSdfAtlas.Data;
                Assert.AreEqual(cpu.Length, gpu.Length);
                int different = 0;
                for (int i = 0; i < cpu.Length; i++)
                    if (cpu[i].value != gpu[i].value) different++;
                Assert.AreEqual(0, different, "texels that differ between the CPU copy and the GPU");
            }
            finally
            {
                foreach (var r in renderers) r.Dispose();
            }
        }
    }
}
