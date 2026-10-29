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

    /// <summary>Shared materials of the Vectra shaders (one per blend mode and context).</summary>
    public static class VectraMaterials
    {
        public const string UIShaderName = "Vectra/UI";
        public const string WorldShaderName = "Vectra/World";

        static readonly int s_SrcBlend = Shader.PropertyToID("_SrcBlend");
        static readonly int s_DstBlend = Shader.PropertyToID("_DstBlend");

        static Shader s_UIShader, s_WorldShader;
        static readonly Material[] s_UI = new Material[4];
        static readonly Material[] s_World = new Material[4];

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
        public static Material GetWorld(ShapeBlendMode mode = ShapeBlendMode.Normal) => Get(s_World, WorldShader, mode, "Vectra World");

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
