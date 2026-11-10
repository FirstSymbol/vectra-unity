using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>How the whole shape is composited with what is behind it.</summary>
    public enum ShapeBlendMode
    {
        Normal,
        Additive,
        Multiply,
        Screen
    }

    /// <summary>Which world shader fits the render pipeline in use.</summary>
    public enum VectraWorldShaderKind
    {
        /// <summary>Built-in Render Pipeline (also used for pipelines Vectra has no dedicated shader for).</summary>
        BuiltIn,
        /// <summary>Universal Render Pipeline: SRP Batcher compatible.</summary>
        Urp,
        /// <summary>High Definition Render Pipeline: SRP Batcher compatible.</summary>
        Hdrp
    }

    /// <summary>Shared materials of the Vectra shaders (one per blend mode and context).</summary>
    public static class VectraMaterials
    {
        public const string UIShaderName = "Vectra/UI";
        public const string WorldShaderName = "Vectra/World";

        static readonly int s_SrcBlend = Shader.PropertyToID("_SrcBlend");
        static readonly int s_DstBlend = Shader.PropertyToID("_DstBlend");

        public const string WorldShaderUrpName = "Vectra/World URP";
        public const string WorldShaderHdrpName = "Vectra/World HDRP";

        static Shader s_UIShader, s_WorldShader, s_WorldUrp, s_WorldHdrp;
        static readonly Material[] s_UI = new Material[4];
        // [kind][blend mode]: materials without a paint texture.
        static readonly Material[][] s_World = { new Material[4], new Material[4], new Material[4] };
        // Materials for shapes that sample a paint texture; one per texture so shapes keep their batch without a property block.
        static readonly Dictionary<(Texture, int), Material>[] s_Textured =
            { new Dictionary<(Texture, int), Material>(), new Dictionary<(Texture, int), Material>(), new Dictionary<(Texture, int), Material>() };
        static readonly List<(Texture, int)> s_Stale = new List<(Texture, int)>();
        static readonly int s_MainTexId = Shader.PropertyToID("_MainTex");

        static RenderPipelineAsset s_KindPipeline;
        static VectraWorldShaderKind s_Kind;
        static bool s_KindKnown;

        /// <summary>Test hook: use this world shader family whatever the render pipeline is.</summary>
        internal static VectraWorldShaderKind? ForcedWorldKind;

        /// <summary>The world shader family for the render pipeline in use now.</summary>
        public static VectraWorldShaderKind CurrentWorldKind
        {
            get
            {
                if (ForcedWorldKind.HasValue) return ForcedWorldKind.Value;
                var rp = GraphicsSettings.currentRenderPipeline;
                if (s_KindKnown && ReferenceEquals(rp, s_KindPipeline)) return s_Kind;
                var kind = VectraWorldShaderKind.BuiltIn;
                if (rp != null)
                {
                    string n = rp.GetType().Name;
                    if (n.IndexOf("Universal", System.StringComparison.Ordinal) >= 0 && WorldShaderFor(VectraWorldShaderKind.Urp) != null)
                        kind = VectraWorldShaderKind.Urp;
                    else if (n.IndexOf("HDRender", System.StringComparison.Ordinal) >= 0 && WorldShaderFor(VectraWorldShaderKind.Hdrp) != null)
                        kind = VectraWorldShaderKind.Hdrp;
                }

                s_KindPipeline = rp;
                s_Kind = kind;
                s_KindKnown = true;
                return kind;
            }
        }

        public static Shader WorldShaderFor(VectraWorldShaderKind kind)
        {
            switch (kind)
            {
                case VectraWorldShaderKind.Urp:
                    if (s_WorldUrp == null) s_WorldUrp = Resources.Load<Shader>("VectraURP/Vectra-World-URP") ?? Shader.Find(WorldShaderUrpName);
                    return s_WorldUrp;
                case VectraWorldShaderKind.Hdrp:
                    if (s_WorldHdrp == null) s_WorldHdrp = Resources.Load<Shader>("VectraHDRP/Vectra-World-HDRP") ?? Shader.Find(WorldShaderHdrpName);
                    return s_WorldHdrp;
                default:
                    return WorldShader;
            }
        }

        public static Shader UIShader
        {
            get
            {
                if (s_UIShader == null)
                    s_UIShader = Resources.Load<Shader>("Vectra/Vectra-UI") ?? Shader.Find(UIShaderName);
                return s_UIShader;
            }
        }

        public static Shader WorldShader
        {
            get
            {
                if (s_WorldShader == null)
                    s_WorldShader = Resources.Load<Shader>("Vectra/Vectra-World") ?? Shader.Find(WorldShaderName);
                return s_WorldShader;
            }
        }

        public static Material GetUI(ShapeBlendMode mode = ShapeBlendMode.Normal) => Get(s_UI, UIShader, mode, "Vectra UI");
        /// <summary>The shared world material for the pipeline in use.</summary>
        public static Material GetWorld(ShapeBlendMode mode = ShapeBlendMode.Normal) => GetWorld(mode, null);

        /// <summary>
        /// The world material for a blend mode and an optional paint texture. Shapes that share it also share an SRP Batcher
        /// batch; nothing is set per renderer.
        /// </summary>
        public static Material GetWorld(ShapeBlendMode mode, Texture paintTexture)
        {
            var kind = CurrentWorldKind;
            if (paintTexture == null || paintTexture == Texture2D.whiteTexture)
                return Get(s_World[(int)kind], WorldShaderFor(kind), mode, "Vectra World " + kind);
            var shader = WorldShaderFor(kind);
            if (shader == null) return null;
            var cache = s_Textured[(int)kind];
            var key = (paintTexture, (int)mode);
            if (cache.TryGetValue(key, out var m) && m != null && m.shader == shader) return m;
            // Destroyed textures leave entries behind: drop them when a new one is added.
            s_Stale.Clear();
            foreach (var kv in cache)
                if (kv.Key.Item1 == null || kv.Value == null) s_Stale.Add(kv.Key);
            foreach (var t in s_Stale) cache.Remove(t);
            m = new Material(shader) { name = $"Vectra World {kind} ({mode}, {paintTexture.name})", hideFlags = HideFlags.HideAndDontSave };
            Apply(m, mode);
            m.SetTexture(s_MainTexId, paintTexture);
            cache[key] = m;
            return m;
        }

        static Material Get(Material[] cache, Shader shader, ShapeBlendMode mode, string name)
        {
            int i = (int)mode;
            if (cache[i] != null) return cache[i];
            if (shader == null) return null;
            var m = new Material(shader) { name = $"{name} ({mode})", hideFlags = HideFlags.HideAndDontSave };
            Apply(m, mode);
            cache[i] = m;
            return m;
        }

        /// <summary>Configures the blend factors of a Vectra material (premultiplied alpha output).</summary>
        public static void Apply(Material m, ShapeBlendMode mode)
        {
            BlendMode src, dst;
            switch (mode)
            {
                case ShapeBlendMode.Additive:
                    src = BlendMode.One;
                    dst = BlendMode.One;
                    break;
                case ShapeBlendMode.Multiply:
                    src = BlendMode.DstColor;
                    dst = BlendMode.OneMinusSrcAlpha;
                    break;
                case ShapeBlendMode.Screen:
                    src = BlendMode.OneMinusDstColor;
                    dst = BlendMode.One;
                    break;
                default:
                    src = BlendMode.One;
                    dst = BlendMode.OneMinusSrcAlpha;
                    break;
            }

            m.SetFloat(s_SrcBlend, (float)src);
            m.SetFloat(s_DstBlend, (float)dst);
        }
    }
}
