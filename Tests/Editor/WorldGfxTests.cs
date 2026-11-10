using System;
using System.Collections.Generic;
using System.Reflection;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>World shaders (SRP Batcher), hairline strokes and baked vector sprites.</summary>
    public class WorldGfxTests
    {
        static bool HasUrp => Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipeline, Unity.RenderPipelines.Universal.Runtime") != null;
        static bool HasHdrp => Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipeline, Unity.RenderPipelines.HighDefinition.Runtime") != null;

        // ------------------------------------------------------------------------------------------------------
        // Shaders
        // ------------------------------------------------------------------------------------------------------

        /// <summary>Reason the shader cannot use the SRP Batcher, or null when it can (or the editor offers no way to ask).</summary>
        static string BatcherIssue(Shader shader, out bool askable) => BatcherIssue(shader, out askable, out _);

        static string BatcherIssue(Shader shader, out bool askable, out string report)
        {
            askable = false;
            report = "";
            var type = typeof(ShaderUtil);
            var code = type.GetMethod("GetSRPBatcherCompatibilityCode", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var reason = type.GetMethod("GetSRPBatcherCompatibilityIssueReason", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (code == null || reason == null) return null;
            askable = true;
            for (int sub = 0; sub < shader.subshaderCount; sub++)
            {
                int c = (int)code.Invoke(null, new object[] { shader, sub });
                var sb = new System.Text.StringBuilder();
                for (int pass = 0; pass < shader.GetPassCountInSubshader(sub); pass++)
                    sb.Append(reason.Invoke(null, new object[] { shader, sub, pass })).Append(" | ");
                report += $"subshader {sub} code {c}: {sb}; ";
                // Passes the editor has not compiled yet report "Not initialized"; the others must say OK.
                bool bad = false;
                int ok = 0;
                for (int pass = 0; pass < shader.GetPassCountInSubshader(sub); pass++)
                {
                    var text = (string)reason.Invoke(null, new object[] { shader, sub, pass });
                    if (text.StartsWith("OK")) ok++;
                    else if (!text.StartsWith("Not initialized")) bad = true;
                    // "OK (_Color)": the name in brackets is the property that sits outside UnityPerMaterial.
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\(.+\)")) bad = true;
                }

                if (!bad && ok > 0) continue;
                return $"subshader {sub} code {c}: {sb}";
            }

            return null;
        }

        static string CompileErrors(Shader shader)
        {
            int n = ShaderUtil.GetShaderMessageCount(shader);
            var sb = new System.Text.StringBuilder();
            foreach (var m in ShaderUtil.GetShaderMessages(shader))
                if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) sb.AppendLine(m.message + " " + m.file + ":" + m.line);
            return sb.Length == 0 ? null : sb.ToString();
        }

        [Test]
        public void UrpWorldShader_CompilesAndIsSrpBatcherCompatible()
        {
            if (!HasUrp) Assert.Ignore("URP is not installed in this project.");
            var shader = VectraMaterials.WorldShaderFor(VectraWorldShaderKind.Urp);
            Assert.NotNull(shader, "Vectra/World URP must load from Resources");
            Assert.IsNull(CompileErrors(shader), "shader errors");
            Assert.IsTrue(shader.isSupported);
            var issue = BatcherIssue(shader, out bool askable, out string report);
            if (!askable) Assert.Inconclusive("This editor has no SRP Batcher compatibility query.");
            Assert.IsNull(issue, "SRP Batcher: " + report);
            Debug.Log("Vectra/World URP SRP Batcher: " + report);
        }

        [Test]
        public void HdrpWorldShader_CompilesAndIsSrpBatcherCompatible()
        {
            if (!HasHdrp) Assert.Ignore("HDRP is not installed in this project.");
            var shader = VectraMaterials.WorldShaderFor(VectraWorldShaderKind.Hdrp);
            Assert.NotNull(shader, "Vectra/World HDRP must load from Resources");
            Assert.IsNull(CompileErrors(shader), "shader errors");
            Assert.IsTrue(shader.isSupported);
            var issue = BatcherIssue(shader, out bool askable);
            if (!askable) Assert.Inconclusive("This editor has no SRP Batcher compatibility query.");
            Assert.IsNull(issue, "SRP Batcher");
        }

        [Test]
        public void BuiltInWorldShader_IsNotBatchable_SoTheQueryIsMeaningful()
        {
            var shader = VectraMaterials.WorldShaderFor(VectraWorldShaderKind.BuiltIn);
            var issue = BatcherIssue(shader, out bool askable, out string report);
            if (!askable) Assert.Inconclusive("This editor has no SRP Batcher compatibility query.");
            Assert.IsNotNull(issue, "the CG shader has no UnityPerMaterial buffer and must be reported as incompatible: " + report);
        }

        [Test]
        public void BuiltInWorldShader_StillCompiles()
        {
            var shader = VectraMaterials.WorldShaderFor(VectraWorldShaderKind.BuiltIn);
            Assert.NotNull(shader);
            Assert.IsNull(CompileErrors(shader));
        }

        [Test]
        public void WorldMaterials_AreSharedPerTextureAndMode()
        {
            var a = VectraMaterials.GetWorld(ShapeBlendMode.Normal);
            Assert.AreSame(a, VectraMaterials.GetWorld(ShapeBlendMode.Normal, null), "no texture = the shared material");
            Assert.AreSame(a, VectraMaterials.GetWorld(ShapeBlendMode.Normal, Texture2D.whiteTexture));
            var t1 = new Texture2D(2, 2);
            var t2 = new Texture2D(2, 2);
            try
            {
                var m1 = VectraMaterials.GetWorld(ShapeBlendMode.Normal, t1);
                Assert.AreSame(m1, VectraMaterials.GetWorld(ShapeBlendMode.Normal, t1), "same texture, same material");
                Assert.AreNotSame(m1, VectraMaterials.GetWorld(ShapeBlendMode.Normal, t2));
                Assert.AreNotSame(m1, VectraMaterials.GetWorld(ShapeBlendMode.Additive, t1), "blend mode is part of the key");
                Assert.AreSame(t1, m1.GetTexture("_MainTex"));
            }
            finally
            {
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
            }
        }

        [Test]
        public void ManyShapesWithTextures_NeedNoPropertyBlocks()
        {
            var go = new GameObject("Vectra Block Test", typeof(MeshFilter), typeof(MeshRenderer), typeof(VectraRenderer));
            try
            {
                var r = go.GetComponent<VectraRenderer>();
                r.Rebuild();
                Assert.IsFalse(go.GetComponent<MeshRenderer>().HasPropertyBlock(), "a world shape sets no MaterialPropertyBlock");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TestCase("Vectra SDF Shape", 5, 1)]
        [TestCase("Vectra Coverage", 2, 1)]
        [TestCase("Vectra Outline", 4, 1)]
        [TestCase("Vectra Pixel Size", 1, 1)]
        [TestCase("Vectra Paint", 6, 1)]
        public void ShaderGraphSubGraphs_Import(string name, int inputs, int outputs)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VectraRenderer).Assembly);
            if (info == null) Assert.Ignore("Vectra is a folder under Assets in this project.");
            if (Type.GetType("UnityEditor.ShaderGraph.SubGraphAsset, Unity.ShaderGraph.Editor") == null) Assert.Ignore("Shader Graph is not installed in this project.");
            string path = info.assetPath + "/Runtime/ShaderGraph/" + name + ".shadersubgraph";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            Assert.NotNull(asset, path);
            Assert.AreEqual("SubGraphAsset", asset.GetType().Name, "imported as a Sub Graph, not as a plain asset");
            var t = asset.GetType();
            Assert.IsTrue((bool)t.GetField("isValid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(asset), "the Sub Graph is valid");
            Assert.IsNotEmpty((string)t.GetField("functionName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(asset));
        }

        // ------------------------------------------------------------------------------------------------------
        // Hairline strokes
        // ------------------------------------------------------------------------------------------------------

        const int Tex = 128;

        /// <summary>Renders a stroked rectangle whose sides are 100 pixels long; the texture shows it with 14 pixels of margin.</summary>
        static Texture2D RenderStroke(float width, StrokeWidthMode mode, float pixelSize, StrokeAlignment align = StrokeAlignment.Center)
        {
            var content = new VectraShapeContent { geometry = new RectangleShape { cornerRadii = Vector4.zero } };
            content.fills.Clear();
            content.strokes.Add(new StrokeLayer(Color.white, width) { widthMode = mode, alignment = align });
            float size = 100f * pixelSize;
            var rect = new Rect(-size / 2f, -size / 2f, size, size);
            using var renderer = new VectraShapeRenderer();
            renderer.Build(content.ToInput(new ShapeFrame { rect = rect, pixelSize = pixelSize }, Color.white, true));
            var view = new Rect(rect.xMin - 14f * pixelSize, rect.yMin - 14f * pixelSize, 128f * pixelSize, 128f * pixelSize);
            return VectraRasterizer.Render(renderer, view, Tex, Tex);
        }

        /// <summary>Total opacity down the middle column through the upper half (one side of the frame).</summary>
        static float CoverageOfOneSide(Texture2D t)
        {
            var px = t.GetPixels32();
            float sum = 0f;
            for (int y = Tex / 2; y < Tex; y++) sum += px[y * Tex + Tex / 2].a / 255f;
            return sum;
        }

        [Test, Combinatorial]
        public void PixelStroke_KeepsItsWidthAtAnyScale([Values(1f, 2f, 3f)] float width, [Values(0.25f, 1f, 4f, 40f)] float pixelSize)
        {
            var t = RenderStroke(width, StrokeWidthMode.Pixels, pixelSize);
            try
            {
                Assert.AreEqual(width, CoverageOfOneSide(t), 0.12f, $"opacity across a {width} px stroke when one pixel is {pixelSize} units");
            }
            finally
            {
                Object.DestroyImmediate(t);
            }
        }

        [Test]
        public void LocalStroke_ScalesWithTheShape()
        {
            var big = RenderStroke(4f, StrokeWidthMode.Local, 1f);
            var small = RenderStroke(4f, StrokeWidthMode.Local, 4f);
            try
            {
                Assert.AreEqual(4f, CoverageOfOneSide(big), 0.12f);
                Assert.AreEqual(1f, CoverageOfOneSide(small), 0.12f, "a local width of 4 units is one pixel when a pixel is 4 units");
            }
            finally
            {
                Object.DestroyImmediate(big);
                Object.DestroyImmediate(small);
            }
        }

        [Test]
        public void PixelStroke_IsAntiAliased()
        {
            // A 1.5 px stroke offset by half a pixel has partially covered pixels on both sides.
            var t = RenderStroke(1.5f, StrokeWidthMode.Pixels, 2f);
            try
            {
                var px = t.GetPixels32();
                int partial = 0;
                for (int y = Tex / 2; y < Tex; y++)
                {
                    byte a = px[y * Tex + Tex / 2].a;
                    if (a > 10 && a < 245) partial++;
                }

                Assert.GreaterOrEqual(partial, 1, "edge pixels carry fractional coverage");
            }
            finally
            {
                Object.DestroyImmediate(t);
            }
        }

        [Test]
        public void PixelStroke_PathStrokeKeepsWidthToo()
        {
            // Dashed strokes are tessellated, not analytic: they take the same width.
            var content = new VectraShapeContent { geometry = new RectangleShape { cornerRadii = Vector4.zero } };
            content.fills.Clear();
            content.strokes.Add(new StrokeLayer(Color.white, 2f) { widthMode = StrokeWidthMode.Pixels, dashed = true, dashPattern = new List<float> { 1000f, 0f } });
            using var renderer = new VectraShapeRenderer();
            float px = 4f;
            var rect = new Rect(-200f, -200f, 400f, 400f);
            renderer.Build(content.ToInput(new ShapeFrame { rect = rect, pixelSize = px }, Color.white, true));
            var view = new Rect(rect.xMin - 14f * px, rect.yMin - 14f * px, 128f * px, 128f * px);
            var t = VectraRasterizer.Render(renderer, view, Tex, Tex);
            try
            {
                Assert.AreEqual(2f, CoverageOfOneSide(t), 0.2f);
            }
            finally
            {
                Object.DestroyImmediate(t);
            }
        }

        [Test]
        public void PixelStroke_SurvivesSerialization()
        {
            var s = new StrokeLayer(Color.red, 1f) { widthMode = StrokeWidthMode.Pixels };
            var copy = JsonUtility.FromJson<StrokeLayer>(JsonUtility.ToJson(s));
            Assert.AreEqual(StrokeWidthMode.Pixels, copy.widthMode);
            Assert.AreEqual(StrokeWidthMode.Pixels, s.Clone().widthMode);
            Assert.AreEqual(3f, copy.EffectiveWidth(3f), 1e-5f);
            Assert.AreEqual(1f, new StrokeLayer(Color.red, 1f).EffectiveWidth(3f), 1e-5f, "local width ignores the pixel size");
        }

        [Test]
        public void WorldRenderer_PixelStrokeHasConstantScreenWidth([Values(1f, 2f, 4f)] float scale)
        {
            // The default scene has a Main Camera of its own: hide it so Camera.main is the one below.
            var hidden = new List<Camera>();
            foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                if (other.CompareTag("MainCamera"))
                {
                    other.tag = "Untagged";
                    hidden.Add(other);
                }

            var camGo = new GameObject("Vectra Hairline Camera", typeof(Camera)) { tag = "MainCamera" };
            var rt = new RenderTexture(200, 200, 0);
            var go = new GameObject("Vectra Hairline", typeof(MeshFilter), typeof(MeshRenderer), typeof(VectraRenderer));
            try
            {
                var cam = camGo.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 5f;
                cam.targetTexture = rt;
                go.transform.localScale = new Vector3(scale, scale, 1f);
                var r = go.GetComponent<VectraRenderer>();
                r.Size = new Vector2(2f, 2f);
                r.Fills.Clear();
                r.Strokes.Clear();
                var stroke = new StrokeLayer(Color.white, 0.01f) { widthMode = StrokeWidthMode.Pixels };
                r.Strokes.Add(stroke);
                r.Rebuild();
                float Reach() => go.GetComponent<MeshFilter>().sharedMesh.bounds.extents.x * scale - scale;
                float thin = Reach();
                stroke.width = 4f;
                r.SetShapeDirty();
                r.Rebuild();
                // Four pixels of width push the quad out by two screen pixels, whatever the scale of the object.
                float pixelWorld = VectraRenderer.EstimatePixelSize(cam, go.transform.position, scale) * scale;
                Assert.AreEqual(2f * pixelWorld, Reach() - thin, pixelWorld * 0.15f, $"scale {scale}");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(rt);
                foreach (var c in hidden) c.tag = "MainCamera";
            }
        }

        // ------------------------------------------------------------------------------------------------------
        // Vector sprites
        // ------------------------------------------------------------------------------------------------------

        static VectraShapeContent Disc(Color c)
        {
            var content = new VectraShapeContent { geometry = new EllipseShape() };
            content.fills.Clear();
            content.fills.Add(new FillLayer(VectraPaint.Solid(c)));
            return content;
        }

        static void Destroy(Sprite s)
        {
            if (s == null) return;
            Object.DestroyImmediate(s.texture);
            Object.DestroyImmediate(s);
        }

        [Test]
        public void Sprite_HasRequestedSizePivotAndPixels()
        {
            var o = VectraSpriteOptions.Default;
            o.resolution = 64;
            o.pixelsPerUnit = 32f;
            o.pivot = new Vector2(0f, 1f);
            var s = VectraSpriteBaker.Bake(Disc(Color.red), new Vector2(2f, 2f), o);
            try
            {
                Assert.NotNull(s);
                Assert.AreEqual(64, s.texture.width);
                Assert.AreEqual(64, s.texture.height);
                Assert.AreEqual(32f, s.pixelsPerUnit);
                Assert.AreEqual(new Vector2(0f, 64f), s.pivot, "pivot in sprite pixels (top left)");
                var centre = s.texture.GetPixel(32, 32);
                Assert.Greater(centre.r, 0.95f);
                Assert.Greater(centre.a, 0.95f);
                Assert.Less(s.texture.GetPixel(1, 1).a, 0.01f, "outside the disc");
            }
            finally
            {
                Destroy(s);
            }
        }

        [Test]
        public void Sprite_TightMeshHugsTheShape()
        {
            var o = VectraSpriteOptions.Default;
            o.resolution = 64;
            var tight = VectraSpriteBaker.Bake(Disc(Color.white), new Vector2(1f, 1f), o);
            o.tightMesh = false;
            var full = VectraSpriteBaker.Bake(Disc(Color.white), new Vector2(1f, 1f), o);
            try
            {
                Assert.AreEqual(4, full.vertices.Length, "full rect is a quad");
                Assert.Greater(tight.vertices.Length, 4, "tight mesh follows the disc");
                float tightArea = MeshArea(tight), fullArea = MeshArea(full);
                Assert.Less(tightArea, fullArea * 0.97f, "the mesh cuts the corners of the square");
                Assert.Greater(tightArea, fullArea * 0.7f);
            }
            finally
            {
                Destroy(tight);
                Destroy(full);
            }
        }

        static float MeshArea(Sprite s)
        {
            float a = 0f;
            var v = s.vertices;
            var t = s.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector2 p0 = v[t[i]], p1 = v[t[i + 1]], p2 = v[t[i + 2]];
                a += Mathf.Abs((p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y)) * 0.5f;
            }

            return a;
        }

        [Test]
        public void Sprite_FrameGetsNineSliceBorderFromItsCorners()
        {
            var content = new VectraShapeContent { geometry = new RectangleShape { cornerRadii = new Vector4(16f, 16f, 16f, 16f) } };
            content.fills.Clear();
            content.fills.Add(new FillLayer(VectraPaint.Solid(Color.white)));
            var o = VectraSpriteOptions.Default;
            o.resolution = 128;
            o.autoBorder = true;
            var s = VectraSpriteBaker.Bake(content, new Vector2(128f, 64f), o);
            try
            {
                Assert.GreaterOrEqual(s.border.x, 16f);
                Assert.LessOrEqual(s.border.x, 20f, "left border is about the corner radius");
                Assert.AreEqual(s.border.x, s.border.z, 1f);
                Assert.AreEqual(s.border.y, s.border.w, 1f);
                Assert.Greater(s.border.x + s.border.z, 0f);
                Assert.Less(s.border.y + s.border.w, s.rect.height, "a stretchable middle remains");
            }
            finally
            {
                Destroy(s);
            }
        }

        [Test]
        public void Sprite_EffectsGrowTheTextureAndKeepThePivotOnTheShape()
        {
            var content = Disc(Color.white);
            content.effects.Add(new DropShadowEffect { blur = 16f, color = new Color(0, 0, 0, 0.6f), offset = Vector2.zero });
            var o = VectraSpriteOptions.Default;
            o.resolution = 64;
            var s = VectraSpriteBaker.Bake(content, new Vector2(64f, 64f), o);
            try
            {
                Assert.Greater(s.texture.width, 64, "room for the shadow");
                Assert.AreEqual(s.texture.width / 2f, s.pivot.x, 1.01f, "the pivot stays at the centre of the shape");
                Assert.Greater(s.texture.GetPixel(2, s.texture.height / 2).a, 0f, "shadow reaches the edge region");
            }
            finally
            {
                Destroy(s);
            }
        }

        [Test]
        public void Sprite_BuildsMipmapsOnRequest()
        {
            var o = VectraSpriteOptions.Default;
            o.resolution = 64;
            o.generateMipmaps = true;
            var s = VectraSpriteBaker.Bake(Disc(Color.white), new Vector2(1f, 1f), o);
            o.generateMipmaps = false;
            var flat = VectraSpriteBaker.Bake(Disc(Color.white), new Vector2(1f, 1f), o);
            try
            {
                Assert.Greater(s.texture.mipmapCount, 1);
                Assert.AreEqual(1, flat.texture.mipmapCount);
            }
            finally
            {
                Destroy(s);
                Destroy(flat);
            }
        }

        [Test]
        public void Sprite_TransparentTexelsKeepTheEdgeColour()
        {
            var o = VectraSpriteOptions.Default;
            o.resolution = 32;
            var s = VectraSpriteBaker.Bake(Disc(new Color(1f, 0f, 0f, 1f)), new Vector2(1f, 1f), o);
            try
            {
                // A texel next to the disc, fully transparent: bilinear filtering must not blend towards black.
                var px = s.texture.GetPixels32();
                int w = s.texture.width;
                int checkedTexels = 0;
                for (int y = 1; y < w - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    var c = px[y * w + x];
                    if (c.a != 0) continue;
                    bool nearOpaque = px[y * w + x + 1].a > 0 || px[y * w + x - 1].a > 0 || px[(y + 1) * w + x].a > 0 || px[(y - 1) * w + x].a > 0;
                    if (!nearOpaque) continue;
                    checkedTexels++;
                    Assert.Greater(c.r, 150, $"texel {x},{y} next to the disc keeps its red");
                }

                Assert.Greater(checkedTexels, 0);
            }
            finally
            {
                Destroy(s);
            }
        }

        [Test]
        public void Sprite_FromSvgDocument()
        {
            var doc = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 10'><rect width='20' height='10' fill='#00ff00'/></svg>");
            var o = VectraSpriteOptions.Default;
            o.resolution = 40;
            o.includeOverflow = false;
            var s = VectraSpriteBaker.Bake(doc, o);
            try
            {
                Assert.NotNull(s);
                Assert.AreEqual(40, s.texture.width);
                Assert.AreEqual(20, s.texture.height);
                var c = s.texture.GetPixel(20, 10);
                Assert.Greater(c.g, 0.9f);
                Assert.Greater(c.a, 0.9f);
            }
            finally
            {
                Destroy(s);
            }
        }

        [Test]
        public void SpriteExport_WritesAnImportedSpriteWithBorderAndPivot()
        {
            var content = new VectraShapeContent { geometry = new RectangleShape { cornerRadii = new Vector4(10f, 10f, 10f, 10f) } };
            content.fills.Clear();
            content.fills.Add(new FillLayer(VectraPaint.Solid(Color.white)));
            var o = VectraSpriteOptions.Default;
            o.resolution = 64;
            o.autoBorder = true;
            o.pivot = new Vector2(0.25f, 0.75f);
            string dir = "Assets/VectraSpriteExportTest";
            AssetDatabase.CreateFolder("Assets", "VectraSpriteExportTest");
            try
            {
                var sprite = VectraSpriteExport.SaveShape(content, new Vector2(64f, 32f), o, dir + "/Frame.png");
                Assert.NotNull(sprite);
                Assert.GreaterOrEqual(sprite.border.x, 10f);
                Assert.AreEqual(0.25f * sprite.rect.width, sprite.pivot.x, 1f);
                Assert.AreEqual(0.75f * sprite.rect.height, sprite.pivot.y, 1f);
                var importer = (TextureImporter)AssetImporter.GetAtPath(dir + "/Frame.png");
                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
                Assert.IsTrue(importer.alphaIsTransparency);
            }
            finally
            {
                AssetDatabase.DeleteAsset(dir);
            }
        }
    }
}
