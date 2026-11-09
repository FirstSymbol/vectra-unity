using System.IO;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>
    /// Imports .svg files as <see cref="VectraSvgDocument"/> (resolution independent, for VectraSvgImage and
    /// VectraSvgRenderer) or bakes them into a Sprite/Texture. Registered as an override importer so it coexists with
    /// Unity's built-in SVG importer; select it in the importer dropdown or enable it project-wide in
    /// Project Settings > FirstSymbol > Vectra.
    /// </summary>
    [ScriptedImporter(4, new string[0], new[] { "svg" })]
    public class VectraSvgImporter : ScriptedImporter
    {
        public enum OutputMode
        {
            Document,
            Sprite,
            Texture
        }

        public OutputMode mode = OutputMode.Document;
        [Tooltip("Size of the longest side of baked textures and previews.")]
        public int textureSize = 512;
        public VectraQuality quality = VectraQuality.High;
        public FilterMode filterMode = FilterMode.Bilinear;
        public float pixelsPerUnit = 100f;
        public Vector2 spritePivot = new Vector2(0.5f, 0.5f);

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text = File.ReadAllText(ctx.assetPath);
            var doc = SvgParser.Parse(text);
            doc.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            foreach (var warning in doc.warnings) ctx.LogImportWarning($"{doc.name}: {warning}");

            var icon = AssetPreview.GetMiniTypeThumbnail(typeof(ScriptableObject));
            ctx.AddObjectToAsset("document", doc, icon);
            if (mode == OutputMode.Document)
            {
                ctx.SetMainObject(doc);
                return;
            }

            float aspect = doc.AspectRatio;
            int size = Mathf.Clamp(textureSize, 1, 8192);
            int w = aspect >= 1f ? size : Mathf.Max(1, Mathf.RoundToInt(size * aspect));
            int h = aspect >= 1f ? Mathf.Max(1, Mathf.RoundToInt(size / aspect)) : size;
            Texture2D tex;
            try
            {
                tex = VectraRasterizer.Render(doc, w, h, quality);
            }
            catch (System.Exception e)
            {
                ctx.LogImportError($"Could not rasterize {doc.name}: {e.Message}");
                ctx.SetMainObject(doc);
                return;
            }

            tex.name = doc.name;
            tex.filterMode = filterMode;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.alphaIsTransparency = true;
            ctx.AddObjectToAsset("texture", tex);
            if (mode == OutputMode.Sprite)
            {
                var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), spritePivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
                sprite.name = doc.name;
                ctx.AddObjectToAsset("sprite", sprite);
                ctx.SetMainObject(sprite);
            }
            else ctx.SetMainObject(tex);
        }
    }

    /// <summary>Makes Vectra the importer of new .svg files when enabled in the project settings.</summary>
    class VectraSvgImporterPostprocessor : AssetPostprocessor
    {
        static readonly System.Collections.Generic.HashSet<string> s_Retried = new System.Collections.Generic.HashSet<string>();

        /// <summary>
        /// An .svg that carries Vectra's importer but came out as a plain asset: the import ran before the importer's own scripts were
        /// registered (the first import of a project that already holds the file), and Unity caches the failure. Imported again once the
        /// scripts are there, it works.
        /// </summary>
        internal static bool ImportFailed(string path) =>
            path.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase) && AssetImporter.GetAtPath(path) is VectraSvgImporter &&
            AssetDatabase.LoadMainAssetAtPath(path) is DefaultAsset;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (!s_Retried.Contains(path) && ImportFailed(path))
                {
                    s_Retried.Add(path);
                    string again = path;
                    EditorApplication.delayCall += () => AssetDatabase.ImportAsset(again, ImportAssetOptions.ForceUpdate);
                }
            }

            if (!VectraProjectSettings.instance.useVectraSvgImporter) return;
            foreach (var path in imported)
            {
                if (!path.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (AssetDatabase.GetImporterOverride(path) == typeof(VectraSvgImporter)) continue;
                AssetDatabase.SetImporterOverride<VectraSvgImporter>(path);
            }
        }
    }
}
