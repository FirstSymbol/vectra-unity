using System.IO;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>
    /// Saves baked Vectra sprites as PNG assets with matching sprite import settings (pivot, border, mesh type, mipmaps),
    /// and adds the menu entries for it.
    /// </summary>
    public static class VectraSpriteExport
    {
        /// <summary>Bakes <paramref name="content"/> and saves it as a sprite PNG at <paramref name="assetPath"/> (under Assets). Returns the imported sprite.</summary>
        public static Sprite SaveShape(VectraShapeContent content, Vector2 size, VectraSpriteOptions options, string assetPath)
        {
            var sprite = VectraSpriteBaker.Bake(content, size, options);
            return Save(sprite, options, assetPath);
        }

        public static Sprite SaveDocument(VectraSvgDocument doc, VectraSpriteOptions options, string assetPath)
        {
            var sprite = VectraSpriteBaker.Bake(doc, options);
            return Save(sprite, options, assetPath);
        }

        /// <summary>Writes the texture of a baked sprite and configures its importer; the transient sprite and texture are destroyed.</summary>
        public static Sprite Save(Sprite baked, VectraSpriteOptions options, string assetPath)
        {
            if (baked == null) return null;
            var tex = baked.texture;
            var pivot = new Vector2(baked.pivot.x / baked.rect.width, baked.pivot.y / baked.rect.height);
            float ppu = baked.pixelsPerUnit;
            var border = baked.border;
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(baked);
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = ppu;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = options.generateMipmaps;
                ti.filterMode = options.filterMode;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.spriteBorder = border;
                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = pivot;
                settings.spriteMeshType = options.tightMesh && border == Vector4.zero ? SpriteMeshType.Tight : SpriteMeshType.FullRect;
                ti.SetTextureSettings(settings);
                ti.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        static VectraSpriteOptions OptionsFor(VectraShapeContent content, Vector2 size, Vector2 pivot, bool nineSlice)
        {
            var o = VectraSpriteOptions.Default;
            o.resolution = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(size.x, size.y) * 2f), 16, 2048);
            o.pivot = pivot;
            o.pixelsPerUnit = 0f;
            o.generateMipmaps = true;
            o.autoBorder = nineSlice;
            return o;
        }

        static void Run(IVectraShape shape, Vector2 size, Vector2 pivot, bool nineSlice)
        {
            string path = EditorUtility.SaveFilePanelInProject(nineSlice ? "Bake 9-slice sprite" : "Bake sprite",
                ((Component)shape).name, "png", "Where to save the sprite");
            if (string.IsNullOrEmpty(path)) return;
            var sprite = SaveShape(shape.Content, size, OptionsFor(shape.Content, size, pivot, nineSlice), path);
            if (sprite != null) EditorGUIUtility.PingObject(sprite);
        }

        [MenuItem("CONTEXT/VectraShape/Bake to Sprite Asset...", false, 620)]
        static void BakeUI(MenuCommand c)
        {
            var s = (VectraShape)c.context;
            Run(s, s.rectTransform.rect.size, s.rectTransform.pivot, false);
        }

        [MenuItem("CONTEXT/VectraShape/Bake to 9-Slice Sprite Asset...", false, 621)]
        static void BakeUINine(MenuCommand c)
        {
            var s = (VectraShape)c.context;
            Run(s, s.rectTransform.rect.size, s.rectTransform.pivot, true);
        }

        [MenuItem("CONTEXT/VectraRenderer/Bake to Sprite Asset...", false, 620)]
        static void BakeWorld(MenuCommand c)
        {
            var s = (VectraRenderer)c.context;
            Run(s, s.Size, s.Pivot, false);
        }

        [MenuItem("CONTEXT/VectraRenderer/Bake to 9-Slice Sprite Asset...", false, 621)]
        static void BakeWorldNine(MenuCommand c)
        {
            var s = (VectraRenderer)c.context;
            Run(s, s.Size, s.Pivot, true);
        }

        [MenuItem("Assets/Vectra/Bake SVG to Sprite", false, 60)]
        static void BakeSvg()
        {
            foreach (var o in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(o);
                if (!path.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase)) continue;
                var doc = AssetDatabase.LoadAssetAtPath<VectraSvgDocument>(path);
                if (doc == null)
                {
                    Debug.LogWarning($"Vectra: {path} is not imported by the Vectra SVG importer; choose it in the importer dropdown first.");
                    continue;
                }

                var options = VectraSpriteOptions.Default;
                options.generateMipmaps = true;
                var target = Path.ChangeExtension(path, null) + " Sprite.png";
                SaveDocument(doc, options, target);
            }
        }

        [MenuItem("Assets/Vectra/Bake SVG to Sprite", true)]
        static bool BakeSvgValidate()
        {
            foreach (var o in Selection.objects)
                if (AssetDatabase.GetAssetPath(o).EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
