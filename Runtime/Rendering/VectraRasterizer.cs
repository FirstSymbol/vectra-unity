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
        public static Texture2D Render(VectraSvgDocument doc, int width, int height, VectraQuality quality = VectraQuality.High)
        {
            if (doc == null) return null;
            using var renderer = new VectraShapeRenderer();
            var rect = new Rect(0, 0, width, height);
            VectraSvgBuilder.Build(doc, renderer, rect, 1f, Color.white, true, quality);
            return Render(renderer, rect, width, height);
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
        public static Texture2D Render(VectraShapeRenderer renderer, Rect view, int width, int height)
        {
            width = Mathf.Clamp(width, 1, 8192);
            height = Mathf.Clamp(height, 1, 8192);
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            renderer.buffer.Upload(mesh);
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
            var mat = VectraMaterials.GetWorld();
            var block = new MaterialPropertyBlock();
            if (renderer.mainTexture != null) block.SetTexture("_MainTex", renderer.mainTexture);
            else block.SetTexture("_MainTex", Texture2D.whiteTexture);
            cmd.DrawMesh(mesh, Matrix4x4.identity, mat, 0, 0, block);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false) { name = "Vectra Raster" };
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(mesh);

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
