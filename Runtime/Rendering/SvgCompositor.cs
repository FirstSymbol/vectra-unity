using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.Svg;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>Where a document is laid out and which part of it a composited texture shows.</summary>
    public struct SvgCompositeSettings
    {
        /// <summary>The rectangle the document is laid out in, in local units (what <see cref="VectraSvgBuilder.Build"/> takes).</summary>
        public Rect localRect;
        /// <summary>The local area the texture covers (usually the same as <see cref="localRect"/>).</summary>
        public Rect view;
        public int width, height;
        /// <summary>Local units per device pixel of the texture.</summary>
        public float localPixelSize;
        public Color tint;
        public VectraQuality quality;
        public List<SvgOverride> overrides;
        public SvgAlign? align;
        public bool? slice;
        public float pointScale;
        /// <summary>
        /// Draw in the gamma scope (<see cref="VectraColorSpace"/>): colours stay sRGB-encoded and blend on sRGB values in a Linear project. A
        /// target that is not sRGB receives the sRGB-encoded picture as it is; an sRGB target receives it decoded to linear light.
        /// </summary>
        public bool gamma;
    }

    /// <summary>
    /// Draws the layers of an SVG document (group opacity, masks, blend modes, background blur) into a render texture. Every layer
    /// is drawn into its own texture and composited into its parent; blend modes and the background blur read what was drawn
    /// before them in the same texture. All textures hold premultiplied alpha.
    /// </summary>
    public sealed class SvgCompositor : IDisposable
    {
        const int PassOver = 0, PassBlend = 1, PassMask = 2, PassBlur = 3, PassDecode = 4, PassAlphaBlur = 5, PassMorph = 6, PassTint = 7, PassDivide = 8, PassInner = 9, PassMasked = 10;
        const int MaxBlurTaps = 40;

        static Shader s_Shader;
        static Material s_Material;
        static readonly int s_Opacity = Shader.PropertyToID("_Opacity");
        static readonly int s_Mode = Shader.PropertyToID("_Mode");
        static readonly int s_MaskKind = Shader.PropertyToID("_MaskKind");
        static readonly int s_Blur = Shader.PropertyToID("_Blur");
        static readonly int s_BackTex = Shader.PropertyToID("_BackTex");
        static readonly int s_Linear = Shader.PropertyToID("_Linear");
        static readonly int s_Offset = Shader.PropertyToID("_Offset");
        static readonly int s_Morph = Shader.PropertyToID("_Morph");
        static readonly int s_Color = Shader.PropertyToID("_Color");

        readonly VectraShapeRenderer m_Renderer = new VectraShapeRenderer();
        readonly CommandBuffer m_Cmd = new CommandBuffer { name = "Vectra Composite" };
        readonly List<List<int>> m_ChildLists = new List<List<int>>();
        Mesh m_Mesh;
        VectraSvgDocument m_Doc;
        SvgCompositeSettings m_S;
        RenderTextureDescriptor m_Desc;
        int m_Depth;

        public static Shader Shader
        {
            get
            {
                if (s_Shader == null) s_Shader = Resources.Load<Shader>("Vectra/Vectra-Composite") ?? Shader.Find("Hidden/Vectra/Composite");
                return s_Shader;
            }
        }

        static Material Mat
        {
            get
            {
                if (s_Material == null)
                {
                    var shader = Shader;
                    if (shader == null) return null;
                    s_Material = new Material(shader) { name = "Vectra Composite", hideFlags = HideFlags.HideAndDontSave };
                }

                return s_Material;
            }
        }

        static Material s_LayerMaterial;

        /// <summary>How many times <see cref="RenderStep"/> has drawn since the domain loaded (diagnostics and tests).</summary>
        public static int StepsRendered { get; private set; }

        static Material s_ImageMaterial;

        /// <summary>The material for the pictures of an SVG (<c>&lt;image&gt;</c>) on a Canvas: the default UI look with crisp texels when magnified.</summary>
        public static Material ImageMaterial
        {
            get
            {
                if (s_ImageMaterial == null)
                {
                    var shader = Resources.Load<Shader>("Vectra/Vectra-Image") ?? UnityEngine.Shader.Find("Vectra/Image");
                    if (shader == null) return null;
                    s_ImageMaterial = new Material(shader) { name = "Vectra Image", hideFlags = HideFlags.HideAndDontSave };
                }

                return s_ImageMaterial;
            }
        }

        /// <summary>The material that draws a composited texture as a quad (Canvas and world space): premultiplied alpha, with Canvas masking.</summary>
        public static Material LayerMaterial
        {
            get
            {
                if (s_LayerMaterial == null)
                {
                    var shader = Resources.Load<Shader>("Vectra/Vectra-Layer") ?? UnityEngine.Shader.Find("Vectra/Layer");
                    if (shader == null) return null;
                    s_LayerMaterial = new Material(shader) { name = "Vectra Layer", hideFlags = HideFlags.HideAndDontSave };
                }

                return s_LayerMaterial;
            }
        }

        /// <summary>
        /// Renders the elements [from, to) of the document (with their layers) into <paramref name="texture"/>, which is created or
        /// resized to cover <paramref name="rect"/> at <paramref name="pixelSize"/> local units per device pixel. The caller owns the texture.
        /// </summary>
        public static bool RenderStep(VectraSvgDocument doc, int from, int to, Rect rect, float pixelSize, VectraQuality quality,
            List<SvgOverride> overrides, SvgAlign? align, float pointScale, ref RenderTexture texture, int maxSize = 4096, bool gamma = false)
        {
            if (doc == null || rect.width <= 0f || rect.height <= 0f) return false;
            float ps = Mathf.Max(pixelSize, 1e-6f);
            int w = Mathf.Clamp(Mathf.CeilToInt(rect.width / ps), 1, maxSize), h = Mathf.Clamp(Mathf.CeilToInt(rect.height / ps), 1, maxSize);
            if (texture == null || texture.width != w || texture.height != h || !texture.IsCreated())
            {
                if (texture != null)
                {
                    texture.Release();
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                texture = Allocate(w, h);
            }

            StepsRendered++;
            using var compositor = new SvgCompositor();
            compositor.Render(doc, from, to, -1, new SvgCompositeSettings
            {
                localRect = rect, view = rect, width = w, height = h, localPixelSize = Mathf.Max(rect.width / w, rect.height / h),
                tint = Color.white, quality = quality, overrides = overrides, align = align, pointScale = pointScale, gamma = gamma
            }, texture);
            return true;
        }

        /// <summary>Releases a texture made by <see cref="RenderStep"/> or <see cref="Allocate"/>.</summary>
        public static void Free(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
        }

        /// <summary>Creates a render texture of the format layers are composited in. The caller owns and releases it.</summary>
        public static RenderTexture Allocate(int width, int height)
        {
            var rt = new RenderTexture(Descriptor(Mathf.Clamp(width, 1, 8192), Mathf.Clamp(height, 1, 8192)))
            {
                name = "Vectra Composite",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        static RenderTextureDescriptor Descriptor(int w, int h) => new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0)
        {
            sRGB = VectraColorSpace.Linear,
            msaaSamples = 1
        };

        /// <summary>
        /// Draws the elements [from, to) of <paramref name="doc"/> into <paramref name="target"/> (cleared first). Layers whose
        /// parent is <paramref name="parent"/> inside that range are composited; pass -1 for the top level.
        /// </summary>
        static readonly Unity.Profiling.ProfilerMarker s_RenderMarker = new Unity.Profiling.ProfilerMarker("Vectra.SvgCompositor.Render");

        public void Render(VectraSvgDocument doc, int from, int to, int parent, in SvgCompositeSettings settings, RenderTexture target)
        {
            using (s_RenderMarker.Auto()) RenderCore(doc, from, to, parent, settings, target);
        }

        void RenderCore(VectraSvgDocument doc, int from, int to, int parent, in SvgCompositeSettings settings, RenderTexture target)
        {
            if (doc == null || target == null || Mat == null) return;
            m_Doc = doc;
            m_S = settings;
            using var scope = settings.gamma && QualitySettings.activeColorSpace == ColorSpace.Linear ? VectraColorSpace.EnterGamma() : default;
            m_Desc = Descriptor(target.width, target.height);
            Mat.SetFloat(s_Linear, VectraColorSpace.Linear ? 1f : 0f);
            m_Depth = 0;
            var prev = RenderTexture.active;
            RenderTexture work = null;
            try
            {
                // An sRGB target would encode what is written to it: the gamma picture is drawn into a plain texture and decoded into it.
                if (VectraColorSpace.InGammaScope && target.sRGB) work = RenderTexture.GetTemporary(m_Desc);
                var into = work != null ? work : target;
                Clear(into);
                RenderRange(from, Mathf.Min(to, doc.elements.Count), parent, into);
                if (work != null) Graphics.Blit(work, target, Mat, PassDecode);
            }
            finally
            {
                if (work != null) RenderTexture.ReleaseTemporary(work);
                RenderTexture.active = prev;
                m_Doc = null;
            }
        }

        void RenderRange(int from, int to, int parent, RenderTexture target)
        {
            while (m_ChildLists.Count <= m_Depth) m_ChildLists.Add(new List<int>());
            var children = m_ChildLists[m_Depth];
            SvgLayers.Children(m_Doc, parent, from, to, children);
            m_Depth++;
            int at = from;
            for (int c = 0; c < children.Count; c++)
            {
                int li = children[c];
                var layer = m_Doc.layers[li];
                if (layer.from < at) continue; // overlapping layers cannot occur in a parsed document
                DrawRun(at, layer.from, null, target);
                RenderLayer(li, target);
                at = layer.to;
            }

            DrawRun(at, to, null, target);
            m_Depth--;
        }

        void RenderLayer(int index, RenderTexture target)
        {
            var layer = m_Doc.layers[index];
            var content = RenderTexture.GetTemporary(m_Desc);
            Clear(content);
            RenderRange(layer.from, layer.to, index, content);
            // Drop shadows that blend with the backdrop in a mode other than normal are drawn into the target before the layer.
            List<RenderTexture> behind = null;
            List<SvgBlendMode> behindModes = null;
            if (layer.filter != null && layer.filter.Count > 0) content = ApplyFilter(layer.filter, content, ref behind, ref behindModes);
            bool blur = layer.blurSigma > 0f;
            if (blur) DrawBackdropBlur(layer, target);
            else if (layer.mask != SvgMaskKind.None)
            {
                if (layer.maskTo > layer.maskFrom)
                {
                    var mask = RenderTexture.GetTemporary(m_Desc);
                    Clear(mask);
                    DrawRun(layer.maskFrom, layer.maskTo, m_Doc.maskElements, mask);
                    ApplyMask(mask, content, layer.mask);
                    if (behind != null)
                        foreach (var s in behind) ApplyMask(mask, s, layer.mask);
                    RenderTexture.ReleaseTemporary(mask);
                }
                else
                {
                    Clear(content); // an empty mask hides everything
                    if (behind != null)
                        foreach (var s in behind) Clear(s);
                }
            }

            float opacity = Mathf.Clamp01(layer.opacity);
            if (behind != null)
            {
                for (int i = 0; i < behind.Count; i++)
                {
                    CompositeInto(behind[i], target, behindModes[i], opacity);
                    RenderTexture.ReleaseTemporary(behind[i]);
                }
            }

            CompositeInto(content, target, layer.blend, opacity);
            RenderTexture.ReleaseTemporary(content);
        }

        // Draws a layer texture into the target with a blend mode and an opacity.
        void CompositeInto(RenderTexture source, RenderTexture target, SvgBlendMode mode, float opacity)
        {
            var mat = Mat;
            mat.SetFloat(s_Opacity, opacity);
            // In a Linear project every translucent edge or opacity inside a layer is mixed on sRGB values (as Figma does), which needs the backdrop pass.
            if (mode == SvgBlendMode.Normal && !VectraColorSpace.Linear) Graphics.Blit(source, target, mat, PassOver);
            else
            {
                var back = RenderTexture.GetTemporary(m_Desc);
                Graphics.Blit(target, back);
                mat.SetTexture(s_BackTex, back);
                mat.SetFloat(s_Mode, (float)mode);
                Graphics.Blit(source, target, mat, PassBlend);
                mat.SetTexture(s_BackTex, null);
                RenderTexture.ReleaseTemporary(back);
            }
        }

        // The filter of a layer, in the order the SVG chain has it, computed from the picture of the layer. Returns the filtered picture
        // (the content texture is released); drop shadows with a blend mode go to behind/modes (textures owned by the caller).
        RenderTexture ApplyFilter(List<SvgFilterStep> steps, RenderTexture content, ref List<RenderTexture> behind, ref List<SvgBlendMode> modes)
        {
            var toLocal = m_Doc.DocumentToLocal(m_S.localRect, m_S.align, m_S.slice);
            float scale = toLocal.UniformScale;
            float px = m_S.view.width > 0f ? m_S.width / m_S.view.width * scale : scale; // document units to texture pixels
            var acc = RenderTexture.GetTemporary(m_Desc);
            Clear(acc);
            bool sourced = false;
            for (int i = 0; i < steps.Count; i++)
            {
                var st = steps[i];
                switch (st.kind)
                {
                    case SvgFilterKind.Source:
                        CompositeInto(content, acc, SvgBlendMode.Normal, 1f);
                        sourced = true;
                        break;
                    case SvgFilterKind.DropShadow:
                    {
                        var shadow = MakeShadow(content, st, px, scale, false);
                        if (st.blend == SvgBlendMode.Normal)
                        {
                            CompositeInto(shadow, acc, SvgBlendMode.Normal, 1f);
                            RenderTexture.ReleaseTemporary(shadow);
                        }
                        else
                        {
                            (behind ??= new List<RenderTexture>()).Add(shadow);
                            (modes ??= new List<SvgBlendMode>()).Add(st.blend);
                        }

                        break;
                    }
                    case SvgFilterKind.InnerShadow:
                    {
                        if (!sourced)
                        {
                            CompositeInto(content, acc, SvgBlendMode.Normal, 1f);
                            sourced = true;
                        }

                        var inner = MakeShadow(content, st, px, scale, true);
                        CompositeInto(inner, acc, st.blend, 1f);
                        RenderTexture.ReleaseTemporary(inner);
                        break;
                    }
                    case SvgFilterKind.Blur:
                    {
                        var blurred = Blur(acc, st.sigma * px);
                        RenderTexture.ReleaseTemporary(acc);
                        acc = blurred;
                        break;
                    }
                }
            }

            if (!sourced) CompositeInto(content, acc, SvgBlendMode.Normal, 1f);
            RenderTexture.ReleaseTemporary(content);
            return acc;
        }

        // A drop shadow (spread, offset, blur of the alpha of the picture) or an inner shadow (the picture's alpha minus the blurred, shifted,
        // choked alpha), painted with the step's colour. Premultiplied, same size as the picture.
        RenderTexture MakeShadow(RenderTexture picture, SvgFilterStep st, float pxPerUnit, float localPerUnit, bool inner)
        {
            var mat = Mat;
            float radius = inner ? st.choke : st.spread;
            var alpha = picture;
            RenderTexture morphed = null;
            if (radius * pxPerUnit > 0.01f)
            {
                radius = Mathf.Min(radius * pxPerUnit, 96f);
                var tmp = RenderTexture.GetTemporary(m_Desc);
                morphed = RenderTexture.GetTemporary(m_Desc);
                tmp.filterMode = morphed.filterMode = FilterMode.Point;
                mat.SetVector(s_Morph, new Vector4(1f / picture.width, 0f, radius, inner ? 0f : 1f));
                Graphics.Blit(picture, tmp, mat, PassMorph);
                mat.SetVector(s_Morph, new Vector4(0f, 1f / picture.height, radius, inner ? 0f : 1f));
                Graphics.Blit(tmp, morphed, mat, PassMorph);
                RenderTexture.ReleaseTemporary(tmp);
                alpha = morphed;
            }

            // Document y points down, texture y up.
            var offset = new Vector2(st.dx * localPerUnit / Mathf.Max(m_S.view.width, 1e-6f), -st.dy * localPerUnit / Mathf.Max(m_S.view.height, 1e-6f));
            var blurred = BlurAlpha(alpha, st.sigma * pxPerUnit, offset);
            if (morphed != null) RenderTexture.ReleaseTemporary(morphed);
            var result = RenderTexture.GetTemporary(m_Desc);
            mat.SetVector(s_Color, new Vector4(st.color.r, st.color.g, st.color.b, st.color.a)); // raw values: a Color property would be converted
            if (inner)
            {
                mat.SetTexture(s_BackTex, blurred);
                Graphics.Blit(picture, result, mat, PassInner);
                mat.SetTexture(s_BackTex, null);
            }
            else Graphics.Blit(blurred, result, mat, PassTint);

            RenderTexture.ReleaseTemporary(blurred);
            return result;
        }

        // Gaussian blur of the alpha channel with the picture shifted by offset (in uv). Wide blurs run on a smaller copy; the result stays small
        // (bilinear) and is scaled up by the pass that reads it.
        RenderTexture BlurAlpha(RenderTexture src, float sigmaPixels, Vector2 offset)
        {
            int d = 1;
            while (sigmaPixels / d > 12f && d < 16) d *= 2;
            int w = Mathf.Max(1, src.width / d), h = Mathf.Max(1, src.height / d);
            float sigma = Mathf.Max(sigmaPixels / d, 0.05f);
            int taps = sigmaPixels / d < 0.2f ? 0 : Mathf.Clamp(Mathf.CeilToInt(sigma * 3f), 1, MaxBlurTaps);
            var small = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0) { sRGB = m_Desc.sRGB, msaaSamples = 1 };
            var a = RenderTexture.GetTemporary(small);
            var b = RenderTexture.GetTemporary(small);
            a.filterMode = b.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, a);
            var mat = Mat;
            mat.SetVector(s_Blur, new Vector4(1f / w, 0f, sigma, taps));
            mat.SetVector(s_Offset, new Vector4(offset.x, 0f, 0f, 0f));
            Graphics.Blit(a, b, mat, PassAlphaBlur);
            mat.SetVector(s_Blur, new Vector4(0f, 1f / h, sigma, taps));
            mat.SetVector(s_Offset, new Vector4(0f, offset.y, 0f, 0f));
            Graphics.Blit(b, a, mat, PassAlphaBlur);
            mat.SetVector(s_Offset, Vector4.zero);
            RenderTexture.ReleaseTemporary(b);
            return a;
        }

        // The blurred content below, limited to the silhouette of the layer, is drawn into the target before the layer itself.
        void DrawBackdropBlur(SvgLayerData layer, RenderTexture target)
        {
            if (layer.maskTo <= layer.maskFrom) return; // no silhouette: nothing to show the blur in
            var toLocal = m_Doc.DocumentToLocal(m_S.localRect, m_S.align, m_S.slice);
            float pixelsPerLocal = m_S.view.width > 0f ? m_S.width / m_S.view.width : 1f;
            float sigma = layer.blurSigma * toLocal.UniformScale * pixelsPerLocal;
            var mask = RenderTexture.GetTemporary(m_Desc);
            Clear(mask);
            DrawRun(layer.maskFrom, layer.maskTo, m_Doc.maskElements, mask);
            // Figma blurs only what lies inside the shape: the weights of the samples outside it are left out and the rest renormalised
            // (a plain blur of the whole backdrop pulls the colours of the neighbouring stripes in at the edges).
            var mat = Mat;
            var inside = RenderTexture.GetTemporary(m_Desc);
            mat.SetTexture(s_BackTex, mask);
            Graphics.Blit(target, inside, mat, PassMasked);
            var sum = Blur(inside, sigma);
            var weight = Blur(mask, sigma);
            RenderTexture.ReleaseTemporary(inside);
            mat.SetTexture(s_BackTex, weight);
            var blurred = RenderTexture.GetTemporary(m_Desc);
            Graphics.Blit(sum, blurred, mat, PassDivide);
            mat.SetTexture(s_BackTex, null);
            RenderTexture.ReleaseTemporary(sum);
            RenderTexture.ReleaseTemporary(weight);
            ApplyMask(mask, blurred, SvgMaskKind.Alpha);
            RenderTexture.ReleaseTemporary(mask);

            mat.SetFloat(s_Opacity, 1f);
            Graphics.Blit(blurred, target, mat, PassOver);
            RenderTexture.ReleaseTemporary(blurred);
        }

        void ApplyMask(RenderTexture mask, RenderTexture content, SvgMaskKind kind)
        {
            var mat = Mat;
            mat.SetFloat(s_MaskKind, kind == SvgMaskKind.Luminance ? 1f : 0f);
            Graphics.Blit(mask, content, mat, PassMask);
        }

        // Gaussian blur of a whole texture. Wide blurs run on a smaller copy (the kernel stays short) and are scaled back up.
        RenderTexture Blur(RenderTexture src, float sigmaPixels)
        {
            int d = 1;
            while (sigmaPixels / d > 12f && d < 16) d *= 2;
            int w = Mathf.Max(1, src.width / d), h = Mathf.Max(1, src.height / d);
            float sigma = Mathf.Max(sigmaPixels / d, 0.3f);
            int taps = Mathf.Clamp(Mathf.CeilToInt(sigma * 3f), 1, MaxBlurTaps);
            var small = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0) { sRGB = m_Desc.sRGB, msaaSamples = 1 };
            var a = RenderTexture.GetTemporary(small);
            var b = RenderTexture.GetTemporary(small);
            a.filterMode = b.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, a);
            var mat = Mat;
            mat.SetVector(s_Blur, new Vector4(1f / w, 0f, sigma, taps));
            Graphics.Blit(a, b, mat, PassBlur);
            mat.SetVector(s_Blur, new Vector4(0f, 1f / h, sigma, taps));
            Graphics.Blit(b, a, mat, PassBlur);
            var result = RenderTexture.GetTemporary(m_Desc);
            result.filterMode = FilterMode.Bilinear;
            Graphics.Blit(a, result);
            RenderTexture.ReleaseTemporary(a);
            RenderTexture.ReleaseTemporary(b);
            return result;
        }

        void Clear(RenderTexture rt)
        {
            m_Cmd.Clear();
            m_Cmd.SetRenderTarget(rt);
            m_Cmd.ClearRenderTarget(true, true, Color.clear);
            Graphics.ExecuteCommandBuffer(m_Cmd);
        }

        // Builds the path elements [from, to) (of the document, or of the mask content) and draws them into the target.
        // A mesh draws with one pattern texture: the run is cut where the texture changes.
        void DrawRun(int from, int to, List<SvgElementData> source, RenderTexture target)
        {
            var list = source ?? m_Doc.elements;
            to = Mathf.Min(to, list.Count);
            int start = from, runKey = 0;
            for (int i = from; i < to; i++)
            {
                var e = list[i];
                if (e == null || e.isText || e.isImage) continue;
                int key = SvgPatterns.TextureKey(e);
                if (key != 0 && runKey != 0 && key != runKey)
                {
                    DrawMesh(start, i, source, target);
                    start = i;
                    runKey = 0;
                }

                if (key != 0) runKey = key;
            }

            DrawMesh(start, to, source, target);
        }

        void DrawMesh(int from, int to, List<SvgElementData> source, RenderTexture target)
        {
            if (to <= from) return;
            VectraSvgBuilder.Build(m_Doc, m_Renderer, m_S.localRect, m_S.localPixelSize, m_S.tint, true, m_S.quality,
                source == null ? m_S.overrides : null, m_S.align, m_S.slice, from, to, m_S.pointScale, source);
            var buf = m_Renderer.buffer;
            if (buf.VertexCount == 0) return;
            if (m_Mesh == null) m_Mesh = new Mesh { name = "Vectra Composite", hideFlags = HideFlags.HideAndDontSave };
            buf.Upload(m_Mesh);
            VectraAtlases.Flush();
            var view = m_S.view;
            m_Cmd.Clear();
            m_Cmd.SetRenderTarget(target);
            // CommandBuffer.SetViewProjectionMatrices applies the platform's render-texture Y flip itself.
            m_Cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(view.xMin, view.xMax, view.yMin, view.yMax, -1f, 1f));
            m_Cmd.DrawMesh(m_Mesh, Matrix4x4.identity,
                VectraMaterials.GetWorld(ShapeBlendMode.Normal, m_Renderer.mainTexture, VectraWorldShaderKind.BuiltIn), 0, 0);
            Graphics.ExecuteCommandBuffer(m_Cmd);
        }

        public void Dispose()
        {
            m_Renderer.Dispose();
            m_Cmd.Release();
            if (m_Mesh != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(m_Mesh);
                else UnityEngine.Object.DestroyImmediate(m_Mesh);
            }

            m_Mesh = null;
        }
    }
}
