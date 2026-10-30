using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using Unity.Pipeline.Commands;
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
    static partial class VectraCliCommands
    {
        [CliCommand("vectra", "stats", "Counts Vectra components in the open scenes and reports atlas usage")]
        static string Stats()
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
            Field(sb, "sdfRegions", VectraSdfAtlas.AllocationCount);
            sb.Append('}');
            return sb.ToString();
        }

        [CliCommand("vectra", "reset_atlases", "Frees the shared distance-field atlas; shapes re-bake on their next update")]
        static string ResetAtlases()
        {
            VectraSdfAtlas.Reset();
            return "ok";
        }

        [CliCommand("vectra", "use_importer", "Switches .svg files (one file or every file under a folder) to the Vectra importer")]
        static string UseImporter([CliArg("path", "Asset path of an .svg file or a folder", Required = true)] string path)
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

        [CliCommand("vectra", "export_png", "Renders a Vectra Shape or Renderer to a PNG file")]
        static string ExportPng(
            [CliArg("target", "Name or hierarchy path of the GameObject", Required = true)] string target,
            [CliArg("output", "File path of the PNG", Required = true)] string output,
            [CliArg("scale", "Pixels per local unit (default 2)")] float scale = 2f)
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

        [CliCommand("vectra", "export_svg", "Writes a Vectra Shape or Renderer as an SVG file")]
        static string ExportSvg(
            [CliArg("target", "Name or hierarchy path of the GameObject", Required = true)] string target,
            [CliArg("output", "File path of the SVG", Required = true)] string output)
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

        [CliCommand("vectra", "expand_svg", "Expands a Vectra SVG Image into a hierarchy of editable shapes (undoable)")]
        static string ExpandSvg([CliArg("target", "Name or hierarchy path of the SVG Image", Required = true)] string target)
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
