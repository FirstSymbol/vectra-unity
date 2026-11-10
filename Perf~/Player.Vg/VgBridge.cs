using System.IO;
using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.VectraPerf
{
    sealed class VgBridge : IVgBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => PerfBridge.Vg = new VgBridge();

        // The importer's own defaults.
        static readonly VectorUtils.TessellationOptions Options = new VectorUtils.TessellationOptions
        {
            StepDistance = 100f,
            MaxCordDeviation = 0.5f,
            MaxTanAngleDeviation = 0.1f,
            SamplingStepSize = 0.01f,
        };

        Material m_Ui, m_Raster;

        public bool Parse(string svg)
        {
            var info = SVGParser.ImportSVG(new StringReader(svg));
            return info.Scene != null;
        }

        public Sprite BuildSprite(string svg)
        {
            var info = SVGParser.ImportSVG(new StringReader(svg));
            if (info.Scene == null) return null;
            var geometry = VectorUtils.TessellateScene(info.Scene, Options);
            if (geometry == null || geometry.Count == 0) return null;
            return VectorUtils.BuildSprite(geometry, 100f, VectorUtils.Alignment.Center, Vector2.zero, 128, true);
        }

        public Graphic MakeImage(Transform parent, string name, Sprite sprite, Vector2 pos, Vector2 size)
        {
            var rt = PerfScene.Place(parent, name, pos, size);
            var image = rt.gameObject.AddComponent<SVGImage>();
            image.sprite = sprite;
            image.preserveAspect = true;
            m_Ui ??= Resources.Load<Material>("VgUI");
            if (m_Ui != null) image.material = m_Ui;
            return image;
        }

        public Texture2D Render(Sprite sprite, int width, int height)
        {
            m_Raster ??= Resources.Load<Material>("VgUnlit");
            if (m_Raster == null || sprite == null) return null;
            return VectorUtils.RenderSpriteToTexture2D(sprite, width, height, m_Raster, 4, false);
        }
    }
}
