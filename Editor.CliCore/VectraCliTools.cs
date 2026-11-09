using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using UnityEditor;
using UnityEngine;

// FindObjectsByType(FindObjectsInactive) without a sort mode does not exist in every supported Unity version.
#pragma warning disable CS0618

namespace FirstSymbol.Vectra.Editor.Cli
{
    /// <summary>
    /// Vectra commands for the Unity CLI (<c>unity command vectra &lt;name&gt;</c>). Compiled only when the
    /// com.unity.pipeline package is installed.
    /// </summary>
    static partial class VectraCli
    {
        internal static string Stats()
        {
            int shapes = Object.FindObjectsByType<VectraShape>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int renderers = Object.FindObjectsByType<VectraRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int svgImages = Object.FindObjectsByType<VectraSvgImage>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int svgRenderers = Object.FindObjectsByType<VectraSvgRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int masks = Object.FindObjectsByType<VectraMask>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            var sb = new StringBuilder();
            sb.Append('{');
            Field(sb, "shapes", shapes).Append(',');
            Field(sb, "renderers", renderers).Append(',');
            Field(sb, "svgImages", svgImages).Append(',');
            Field(sb, "svgRenderers", svgRenderers).Append(',');
            Field(sb, "masks", masks).Append(',');
            Field(sb, "gradientRowsUsed", VectraGradientAtlas.UsedRows).Append(',');
            Field(sb, "gradientRows", VectraGradientAtlas.Height).Append(',');
            Field(sb, "sdfAtlasSize", VectraSdfAtlas.Size).Append(',');
            Field(sb, "sdfRegions", VectraSdfAtlas.AllocationCount).Append(',');
            var project = VectraProjectSettings.instance.textEngine;
            sb.Append("\"textEngine\":\"").Append(project).Append("\",");
            sb.Append("\"textEngineUsed\":\"").Append(VectraTextBackends.Resolve(project, null, null, null).Used).Append('"');
            sb.Append('}');
            return sb.ToString();
        }

        internal static string ResetAtlases()
        {
            VectraSdfAtlas.Reset();
            return "ok";
        }

        internal static string UseImporter(string path)
        {
            var files = AssetDatabase.IsValidFolder(path)
                ? AssetDatabase.FindAssets("", new[] { path }).Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase)).Distinct().ToArray()
                : new[] { path };
            int n = 0;
            foreach (var f in files)
            {
                if (!f.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase) || !File.Exists(f)) continue;
                AssetDatabase.SetImporterOverride<VectraSvgImporter>(f);
                n++;
            }

            return $"{n} file(s) now use the Vectra importer";
        }

        internal static string ExportPng(string target, string output, float scale = 2f)
        {
            var go = Find(target);
            if (go == null) return "error: GameObject not found: " + target;
            VectraShapeContent content;
            Vector2 size;
            if (go.TryGetComponent(out VectraShape s))
            {
                content = s.Content;
                size = s.rectTransform.rect.size;
            }
            else if (go.TryGetComponent(out VectraRenderer r))
            {
                content = r.Content;
                size = r.Size;
            }
            else return "error: no VectraShape or VectraRenderer on " + target;

            scale = scale > 0f ? scale : 2f;
            var tex = VectraRasterizer.Render(content, size, Mathf.Max(1, Mathf.CeilToInt(size.x * scale)), Mathf.Max(1, Mathf.CeilToInt(size.y * scale)));
            var dir = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(output, tex.EncodeToPNG());
            int w = tex.width, h = tex.height;
            Object.DestroyImmediate(tex);
            return $"{output} ({w}x{h})";
        }

        internal static string ExportSvg(string target, string output)
        {
            var go = Find(target);
            if (go == null) return "error: GameObject not found: " + target;
            string svg;
            if (go.TryGetComponent(out VectraShape s)) svg = SvgWriter.Write(s.Content, s.rectTransform.rect);
            else if (go.TryGetComponent(out VectraRenderer r)) svg = SvgWriter.Write(r.Content, r.LocalRect);
            else return "error: no VectraShape or VectraRenderer on " + target;
            File.WriteAllText(output, svg);
            return output;
        }

        internal static string ExpandSvg(string target)
        {
            var go = Find(target);
            if (go == null || !go.TryGetComponent(out VectraSvgImage img)) return "error: no VectraSvgImage found: " + target;
            var root = VectraSvg.Expand(img);
            if (root == null) return "error: the image has no document";
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Expand SVG");
            Undo.RecordObject(img.gameObject, "Expand SVG");
            img.gameObject.SetActive(false);
            return $"{root.name} ({root.childCount} elements)";
        }

        static GameObject Find(string target) => CliScene.Find(target);

        static StringBuilder Field(StringBuilder sb, string name, int value) =>
            sb.Append('"').Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
    }
}
