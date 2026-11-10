using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit
{
    /// <summary>
    /// A composited step of an SVG document (group opacity, mask, blend mode, background blur): the compositor draws it into a render
    /// texture at the panel's pixel density and the element shows the texture as one quad.
    /// </summary>
    sealed class VectraSvgCompositeElement : VisualElement
    {
        static readonly Vertex[] s_Vertices = new Vertex[4];
        RenderTexture m_Texture;
        Rect m_Quad;

        /// <summary>Pixels per point the texture was drawn for.</summary>
        public float builtScale;
        public int renderCount;

        public RenderTexture Texture => m_Texture;
        public bool IsAlive => m_Texture != null && m_Texture.IsCreated();

        public VectraSvgCompositeElement()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0;
            generateVisualContent = OnGenerate;
        }

        /// <summary><paramref name="rect"/> is the document's rectangle in local space (y up) and <paramref name="contentRect"/> the same area in element space.</summary>
        public void Render(VectraSvgDocument doc, int from, int to, Rect rect, Rect contentRect, float density, VectraQuality quality,
            List<SvgOverride> overrides, SvgAlign? align, bool gamma)
        {
            renderCount++;
            builtScale = density;
            m_Quad = contentRect;
            SvgCompositor.RenderStep(doc, from, to, rect, 1f / Mathf.Max(density, 1e-3f), quality, overrides, align, density, ref m_Texture, 4096, gamma);
            MarkDirtyRepaint();
        }

        public void Release() => SvgCompositor.Free(ref m_Texture);

        void OnGenerate(MeshGenerationContext mgc)
        {
            if (m_Texture == null) return;
            var q = m_Quad;
            var tint = new Color32(255, 255, 255, 255);
            mgc.AllocateTempMesh(4, 6, out NativeSlice<Vertex> v, out NativeSlice<ushort> idx);
            s_Vertices[0] = new Vertex { position = new Vector3(q.xMin, q.yMin, Vertex.nearZ), tint = tint, uv = new Vector2(0f, 1f) };
            s_Vertices[1] = new Vertex { position = new Vector3(q.xMax, q.yMin, Vertex.nearZ), tint = tint, uv = new Vector2(1f, 1f) };
            s_Vertices[2] = new Vertex { position = new Vector3(q.xMax, q.yMax, Vertex.nearZ), tint = tint, uv = new Vector2(1f, 0f) };
            s_Vertices[3] = new Vertex { position = new Vector3(q.xMin, q.yMax, Vertex.nearZ), tint = tint, uv = new Vector2(0f, 0f) };
            for (int i = 0; i < 4; i++) v[i] = s_Vertices[i];
            idx[0] = 0;
            idx[1] = 1;
            idx[2] = 2;
            idx[3] = 2;
            idx[4] = 3;
            idx[5] = 0;
            mgc.DrawMesh(v, idx, m_Texture, TextureOptions.PremultipliedAlpha | TextureOptions.SkipDynamicAtlas);
        }
    }
}
