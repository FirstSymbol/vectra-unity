using System.Collections.Generic;
using FirstSymbol.Vectra.Svg;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>
    /// Renders Vectra content into textures with the GPU (same shaders as on screen): SVG documents, shapes or any
    /// prepared <see cref="VectraMeshBuffer"/>. Works in the editor (importer, PNG export) and at runtime.
    /// </summary>
    public static class VectraRasterizer
    {
        public static Texture2D Render(VectraSvgDocument doc, int width, int height, VectraQuality quality = VectraQuality.High, System.Collections.Generic.List<SvgOverride> overrides = null)
        {
            if (doc == null) return null;
            var rect = new Rect(0, 0, width, height);
            if (SvgLayers.HasLayers(doc)) return RenderLayered(doc, rect, rect, width, height, quality, overrides);
            var renderers = BuildDocument(doc, rect, quality, overrides);
            try
            {
                return Render(renderers, rect, width, height);
            }
            finally
            {
                foreach (var r in renderers) r.Dispose();
            }
        }

        /// <summary>
        /// Builds the path elements of a document, one renderer per mesh: a mesh draws with one texture, so a pattern with another
        /// texture starts the next one. The caller disposes the renderers.
        /// </summary>
        internal static List<VectraShapeRenderer> BuildDocument(VectraSvgDocument doc, Rect rect, VectraQuality quality, List<SvgOverride> overrides = null)
        {
            var renderers = new List<VectraShapeRenderer>();
            var items = new List<SvgDrawItem>();
            SvgDrawOrder.Split(doc, false, false, items);
            if (items.Count <= 1)
            {
                var single = new VectraShapeRenderer();
                renderers.Add(single);
                VectraSvgBuilder.Build(doc, single, rect, 1f, Color.white, true, quality, overrides);
                return renderers;
            }

            foreach (var item in items)
            {
                var r = new VectraShapeRenderer();
                renderers.Add(r);
                VectraSvgBuilder.Build(doc, r, rect, 1f, Color.white, true, quality, overrides, null, null, item.from, item.to);
            }

            return renderers;
        }

        // Documents with layers (group opacity, masks, blend modes, background blur) go through the compositor.
        internal static Texture2D RenderLayered(VectraSvgDocument doc, Rect rect, Rect view, int width, int height, VectraQuality quality,
            System.Collections.Generic.List<SvgOverride> overrides = null)
        {
            width = Mathf.Clamp(width, 1, 8192);
            height = Mathf.Clamp(height, 1, 8192);
            var rt = SvgCompositor.Allocate(width, height);
            try
            {
                using var compositor = new SvgCompositor();
                compositor.Render(doc, 0, doc.elements.Count, -1, new SvgCompositeSettings
                {
                    localRect = rect, view = view, width = width, height = height, localPixelSize = Mathf.Max(view.width / width, view.height / height),
                    tint = Color.white, quality = quality, overrides = overrides, pointScale = 1f
                }, rt);
                return Read(rt, width, height);
            }
            finally
            {
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        public static Texture2D Render(VectraShapeContent content, Vector2 size, int width, int height)
        {
            using var renderer = new VectraShapeRenderer();
            var rect = new Rect(-size.x * 0.5f, -size.y * 0.5f, size.x, size.y);
            float px = Mathf.Max(size.x / width, size.y / height);
            renderer.Build(content.ToInput(new ShapeFrame { rect = rect, pixelSize = px }, Color.white, true));
            // Include effects that reach outside the rect (shadows, glows).
            var b = renderer.buffer.bounds;
            var view = b.IsValid ? Rect.MinMaxRect(Mathf.Min(rect.xMin, b.min.x), Mathf.Min(rect.yMin, b.min.y),
                Mathf.Max(rect.xMax, b.max.x), Mathf.Max(rect.yMax, b.max.y)) : rect;
            return Render(renderer, view, width, height);
        }

        /// <summary>Renders the renderer's current buffer; <paramref name="view"/> is the local area mapped to the texture.</summary>
        public static Texture2D Render(VectraShapeRenderer renderer, Rect view, int width, int height) =>
            Render(new List<VectraShapeRenderer> { renderer }, view, width, height);

        /// <summary>Renders several renderers in order, each with its own texture; later ones are drawn above earlier ones.</summary>
        internal static Texture2D Render(List<VectraShapeRenderer> renderers, Rect view, int width, int height)
        {
            width = Mathf.Clamp(width, 1, 8192);
            height = Mathf.Clamp(height, 1, 8192);
            var meshes = new Mesh[renderers.Count];
            for (int i = 0; i < meshes.Length; i++)
            {
                meshes[i] = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                renderers[i].buffer.Upload(meshes[i]);
            }

            VectraAtlases.Flush();

            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
                msaaSamples = 1
            });
            var cmd = new CommandBuffer { name = "Vectra Rasterize" };
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(true, true, Color.clear);
            var proj = Matrix4x4.Ortho(view.xMin, view.xMax, view.yMin, view.yMax, -1f, 1f);
            // CommandBuffer.SetViewProjectionMatrices applies the platform's render-texture Y flip itself.
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, proj);
            for (int i = 0; i < meshes.Length; i++)
                cmd.DrawMesh(meshes[i], Matrix4x4.identity, VectraMaterials.GetWorld(ShapeBlendMode.Normal, renderers[i].mainTexture, VectraWorldShaderKind.BuiltIn), 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();

            var tex = Read(rt, width, height);
            RenderTexture.ReleaseTemporary(rt);
            foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
            return tex;
        }

        // Reads a render texture of premultiplied colours into a Texture2D of straight alpha.
        static Texture2D Read(RenderTexture rt, int width, int height)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false) { name = "Vectra Raster" };
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            RenderTexture.active = prev;

            // The shaders output premultiplied alpha; textures are usually straight alpha.
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.a == 0 || c.a == 255) continue;
                float inv = 255f / c.a;
                px[i] = new Color32((byte)Mathf.Min(255, c.r * inv), (byte)Mathf.Min(255, c.g * inv), (byte)Mathf.Min(255, c.b * inv), c.a);
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }
    }
}
