using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>The gradient atlas grows while the Canvas rebuilds shapes: nothing is logged, nothing is dirtied, only the texture changes.</summary>
    public class GradientAtlasGrowthTests
    {
        static readonly ColorAdjust k_Identity = ColorAdjust.Identity;

        [SetUp]
        public void SetUp() => VectraGradientAtlas.ResetForTests();

        [TearDown]
        public void TearDown() => VectraGradientAtlas.ResetForTests();

        static VectraPaint Paint(int i) =>
            VectraPaint.Linear(new Color((i & 255) / 255f, ((i >> 8) & 255) / 255f, 0.25f), new Color(1f - (i & 255) / 255f, 0.5f, ((i >> 8) & 15) / 15f));

        // Every Debug.LogError of this test fails it: the old growth logged one error per shape inside the Canvas rebuild loop.
        [Test]
        public void ManyGradientShapesInOneFrame_LogNoError_AndGrowTheAtlas()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            var canvasGo = new GameObject("Growth Canvas", typeof(Canvas));
            try
            {
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var shapes = new List<VectraShape>();
                for (int i = 0; i < 400; i++)
                {
                    var go = new GameObject("S" + i, typeof(RectTransform));
                    go.transform.SetParent(canvasGo.transform, false);
                    ((RectTransform)go.transform).sizeDelta = new Vector2(20f, 20f);
                    var s = go.AddComponent<VectraShape>();
                    s.SetFill(Paint(i));
                    shapes.Add(s);
                }

                Canvas.ForceUpdateCanvases();
                Assert.Greater(VectraGradientAtlas.Height, 64, "400 distinct gradients do not fit in the first 64 rows");
                Assert.GreaterOrEqual(VectraGradientAtlas.LiveRows, 400);
                foreach (var s in shapes)
                {
                    Assert.Greater(s.Stats.vertices, 0, "every shape was built");
                    Assert.IsNotNull(s.canvasRenderer.GetMesh());
                }
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }

        [Test]
        public void GrowingTheAtlas_DoesNotAskClientsToRebuild()
        {
            var client = new CountingClient();
            VectraAtlases.Register(client);
            try
            {
                var held = new List<GradientRows>();
                for (int i = 0; i < 300; i++) held.Add(VectraGradientAtlas.Acquire(Paint(i), k_Identity));
                Assert.Greater(VectraGradientAtlas.Height, 64);
                Assert.AreEqual(0, client.invalidated, "mesh coordinates are independent of the texture height");
            }
            finally
            {
                VectraAtlases.Unregister(client);
            }
        }

        [Test]
        public void MeshRowCoordinates_ScaleExactlyToTheTextureRow()
        {
            for (int i = 0; i < 200; i++) VectraGradientAtlas.Acquire(Paint(i), k_Identity);
            int h = VectraGradientAtlas.Height;
            Assert.AreEqual(256, h);
            for (int row = 1; row < h; row += 7)
            {
                var rows = new GradientRows { key = 1, row = row, count = 5 };
                // What the shader does: v * (MaxHeight / height).
                float shader = VectraGradientAtlas.MeshRowV(rows) * (VectraGradientAtlas.MaxHeight * (1f / h));
                Assert.AreEqual(VectraGradientAtlas.RowV(rows), shader, "bit for bit");
                float span = VectraGradientAtlas.MeshBlockSpanV(rows) * (VectraGradientAtlas.MaxHeight * (1f / h));
                Assert.AreEqual(VectraGradientAtlas.BlockSpanV(rows), span, "bit for bit");
            }
        }

        // Rows baked after an upload are sent alone; the texture must end up the same as one made by a single full upload.
        [Test]
        public void PartialUploads_GiveTheSameTextureAsAFullUpload()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            var partial = BakeInSteps(true);
            VectraGradientAtlas.ResetForTests();
            var full = BakeInSteps(false);
            Assert.AreEqual(full.Length, partial.Length);
            for (int i = 0; i < full.Length; i++)
                if (full[i] != partial[i])
                    Assert.Fail($"texel {i} differs: {partial[i]} after partial uploads, {full[i]} after one full upload");
        }

        static Color[] BakeInSteps(bool flushBetween)
        {
            var held = new List<GradientRows>();
            for (int i = 0; i < 10; i++) held.Add(VectraGradientAtlas.Acquire(Paint(i), k_Identity));
            VectraAtlases.Flush();
            for (int step = 0; step < 6; step++)
            {
                held.Add(VectraGradientAtlas.Acquire(Paint(100 + step * 3), k_Identity));
                if (flushBetween) VectraAtlases.Flush();
                held.Add(VectraGradientAtlas.Acquire(Paint(101 + step * 3), k_Identity));
                if (flushBetween) VectraAtlases.Flush();
            }

            VectraAtlases.Flush();
            var tex = VectraGradientAtlas.Texture;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBAHalf, false, true);
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                return copy.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(copy);
            }
        }

        sealed class CountingClient : IVectraAtlasClient
        {
            public int invalidated;
            public void OnAtlasInvalidated() => invalidated++;
        }
    }
}
