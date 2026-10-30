using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>Analytic shape kinds understood by the shader (mirrors VectraCore.hlsl).</summary>
    public enum ShapeKind : byte
    {
        Rect = 0,
        Ellipse = 1,
        Arc = 2,
        Star = 3,
        Triangle = 4,
        Segment = 5,
        Cross = 6,
        Heart = 7,
        Rhombus = 8,
        Trapezoid = 9,
        Parallelogram = 10,
        Moon = 11,
        Egg = 12,
        Vesica = 13,
        Squircle = 14,
        Flower = 15,
        Arrow = 16,
        Chevron = 17,
        SdfTexture = 28,
        Path = 29
    }

    public enum LayerKind : byte
    {
        Fill = 0,
        Stroke = 1,
        Shadow = 2,
        InnerShadow = 3,
        Bevel = 4,
        Grain = 5
    }

    public enum PaintKind : byte
    {
        Solid = 0,
        Linear = 1,
        Radial = 2,
        Angular = 3,
        Diamond = 4,
        Focal = 5,
        Texture = 6,
        Stripes = 7,
        Checker = 8,
        Dots = 9,
        Grid = 10,
        Noise = 11,
        Freeform = 12
    }

    public enum ModifierKind : byte
    {
        None = 0,
        CutX = 1,
        CutY = 2,
        Radial = 3,
        Dissolve = 4,
        Pixelate = 5
    }

    /// <summary>
    /// Vertex consumed by the Vectra shaders. Only position, colour and four texture coordinates are used so that
    /// the format survives uGUI canvas batching (which transforms normals and tangents).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VectraVertex
    {
        public float3 position;
        public Color32 color;
        /// <summary>xy = shape-space position, z = header, w = modifier parameter.</summary>
        public float4 uv0;
        /// <summary>Shape parameters 1-4.</summary>
        public float4 uv1;
        /// <summary>xy = shape parameters 5-6, zw = layer parameters.</summary>
        public float4 uv2;
        /// <summary>xy = paint coordinates, z = gradient row, w = paint parameter.</summary>
        public float4 uv3;

        public static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4)
        };
    }

    /// <summary>Bit layout of the per-primitive header and helpers to pack parameters exactly into floats.</summary>
    public static class VectraEncoding
    {
        /// <summary>Signed distance used for sides that must never be anti-aliased (always inside).</summary>
        public const float Inside = -1e6f;
        public const float Far = 1e8f;

        public static float Header(ShapeKind shape, LayerKind layer, PaintKind paint, int spread = 0,
            ModifierKind modifier = ModifierKind.None, bool modifierFlag = false, bool masked = false, int style = 0,
            bool additive = false)
        {
            uint h = (uint)shape & 31u;
            h |= ((uint)layer & 15u) << 5;
            h |= ((uint)paint & 15u) << 9;
            h |= ((uint)spread & 3u) << 13;
            h |= ((uint)modifier & 7u) << 15;
            if (modifierFlag) h |= 1u << 18;
            if (masked) h |= 1u << 19;
            h |= ((uint)style & 7u) << 20;
            if (additive) h |= 1u << 23;
            return h;
        }

        /// <summary>Packs two values in [0, 1] with 12 bits each.</summary>
        public static float Pack12(float a, float b)
        {
            int ia = (int)math.round(math.saturate(a) * 4095f);
            int ib = (int)math.round(math.saturate(b) * 4095f);
            return ia * 4096 + ib;
        }

        public static float Pack12Raw(int a, int b) => math.clamp(a, 0, 4095) * 4096 + math.clamp(b, 0, 4095);

        /// <summary>Packs three values in [0, 1] with 8 bits each.</summary>
        public static float Pack8(float a, float b, float c)
        {
            int ia = (int)math.round(math.saturate(a) * 255f);
            int ib = (int)math.round(math.saturate(b) * 255f);
            int ic = (int)math.round(math.saturate(c) * 255f);
            return ia * 65536 + ib * 256 + ic;
        }

        /// <summary>Logarithmic 12-bit code of a non-negative value: 0 = zero, otherwise 2^[-12, 12].</summary>
        public static int Log12(float v)
        {
            if (v <= 1e-9f) return 0;
            float t = (math.log2(v) + 12f) / 24f;
            return 1 + (int)math.round(math.saturate(t) * 4094f);
        }

        /// <summary>Signed logarithmic 12-bit code: bit 11 = sign, 0 = zero, otherwise 2^[-10, 10].</summary>
        public static int SignedLog12(float v)
        {
            float a = math.abs(v);
            if (a <= 1e-9f) return 0;
            float t = (math.log2(a) + 10f) / 20f;
            int code = 1 + (int)math.round(math.saturate(t) * 2046f);
            return v < 0f ? code + 2048 : code;
        }

        public static float DecodeLog12(int code) =>
            code == 0 ? 0f : math.exp2((code - 1) / 4094f * 24f - 12f);

        public static float DecodeSignedLog12(int code)
        {
            bool neg = code >= 2048;
            int m = neg ? code - 2048 : code;
            float v = m == 0 ? 0f : math.exp2((m - 1) / 2046f * 20f - 10f);
            return neg ? -v : v;
        }
    }
}
