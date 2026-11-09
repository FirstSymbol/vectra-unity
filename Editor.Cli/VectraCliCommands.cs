using Unity.Pipeline.Commands;

namespace FirstSymbol.Vectra.Editor.Cli
{
    /// <summary>
    /// The "vectra" group of the Unity CLI (<c>unity command vectra &lt;name&gt;</c>): names and arguments only.
    /// What each command does is in <see cref="VectraCli"/>. Compiled only when the com.unity.pipeline package is installed.
    /// </summary>
    static class VectraCliCommands
    {
        const string Group = "vectra";

        [CliCommand("vectra", "stats", "Counts Vectra components in the open scenes and reports atlas usage")]
        static string Stats() => VectraCli.Stats();

        [CliCommand("vectra", "reset_atlases", "Frees the shared distance-field atlas; shapes re-bake on their next update")]
        static string ResetAtlases() => VectraCli.ResetAtlases();

        [CliCommand("vectra", "use_importer", "Switches .svg files (one file or every file under a folder) to the Vectra importer")]
        static string UseImporter([CliArg("path", "Asset path of an .svg file or a folder", Required = true)] string path) => VectraCli.UseImporter(path);

        [CliCommand("vectra", "export_png", "Renders a Vectra Shape or Renderer to a PNG file")]
        static string ExportPng(
            [CliArg("target", "Name or hierarchy path of the GameObject", Required = true)] string target,
            [CliArg("output", "File path of the PNG", Required = true)] string output,
            [CliArg("scale", "Pixels per local unit (default 2)")] float scale = 2f) => VectraCli.ExportPng(target, output, scale);

        [CliCommand("vectra", "export_svg", "Writes a Vectra Shape or Renderer as an SVG file")]
        static string ExportSvg(
            [CliArg("target", "Name or hierarchy path of the GameObject", Required = true)] string target,
            [CliArg("output", "File path of the SVG", Required = true)] string output) => VectraCli.ExportSvg(target, output);

        [CliCommand("vectra", "expand_svg", "Expands a Vectra SVG Image into a hierarchy of editable shapes (undoable)")]
        static string ExpandSvg([CliArg("target", "Name or hierarchy path of the SVG Image", Required = true)] string target) => VectraCli.ExpandSvg(target);

        [CliCommand(Group, "help", "How to drive Vectra from the CLI: commands, JSON formats and examples")]
        static object Help() => VectraCli.Help();

        [CliCommand(Group, "types", "Lists shape, effect and paint types with their fields and default values")]
        static object Types() => VectraCli.Types();

        [CliCommand(Group, "list", "Lists every Vectra shape, renderer and SVG object in the open scenes")]
        static object ListCommand() => VectraCli.ListCommand();

        [CliCommand(Group, "get", "Full JSON description of a Vectra shape (the same structure 'set' accepts)")]
        static object Get(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("full", "Include fields that still have their default value")] bool full = false) => VectraCli.Get(target, full);

        [CliCommand(Group, "create", "Creates a Vectra shape (UI by default, --world for a VectraRenderer) and returns its description")]
        static object Create(
            [CliArg("shape", "Shape type (see 'vectra types'), e.g. Rectangle, Star, Path, TracedSprite", Required = true)] string shape,
            [CliArg("name", "GameObject name")] string name = null,
            [CliArg("parent", "Parent path/name (UI: inside a canvas; created when missing)")] string parent = null,
            [CliArg("world", "Create a world-space VectraRenderer instead of a UI shape")] bool world = false,
            [CliArg("size", "w,h (UI: pixels, world: units)")] string size = null,
            [CliArg("position", "x,y (UI: anchored position, world: local position)")] string position = null,
            [CliArg("fill", "Fill colour, e.g. #3B82F6")] string fill = null,
            [CliArg("data", "Patch applied after creation (same format as 'set')")] string json = null) => VectraCli.Create(shape, name, parent, world, size, position, fill, json);

        [CliCommand(Group, "set", "Patches a shape with JSON: geometry, fills, strokes, effects, size, position, rotation, color, style, …")]
        static object Set(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("data", "Patch, e.g. {\"geometry\":{\"$type\":\"Star\",\"points\":6},\"fills\":{\"0\":{\"paint\":{\"color\":\"#ff0\"}}}}", Required = true)] string json) => VectraCli.Set(target, json);

        [CliCommand(Group, "add", "Appends a fill, stroke or effect (--type for effects) with an optional JSON patch")]
        static object Add(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("list", "fills, strokes or effects", Required = true)] string list,
            [CliArg("type", "Effect type, e.g. DropShadow, OuterGlow, Outline (effects only)")] string type = null,
            [CliArg("data", "Patch for the new element")] string json = null) => VectraCli.Add(target, list, type, json);

        [CliCommand(Group, "remove", "Removes a fill, stroke or effect by index")]
        static object Remove(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("list", "fills, strokes or effects", Required = true)] string list,
            [CliArg("index", "Element index", Required = true)] int index) => VectraCli.Remove(target, list, index);

        [CliCommand(Group, "mask", "Adds or configures a VectraMask (stencil or soft) on a UI shape")]
        static object MaskCommand(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("mode", "stencil or soft")] string mode = null,
            [CliArg("softness", "Soft edge width")] float softness = -1f,
            [CliArg("inset", "Moves the soft edge inwards")] float inset = float.NaN,
            [CliArg("invert", "Show the children outside the shape")] string invert = null,
            [CliArg("show_graphic", "Draw the mask shape itself")] string showGraphic = null,
            [CliArg("remove", "Remove the mask")] bool remove = false) => VectraCli.MaskCommand(target, mode, softness, inset, invert, showGraphic, remove);

        [CliCommand(Group, "states", "Adds or configures hover/pressed/selected/disabled looks (VectraInteractionStates)")]
        static object States(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("data", "Patch, e.g. {\"hover\":{\"scale\":1.05},\"duration\":0.2}")] string json = null,
            [CliArg("remove", "Remove the component")] bool remove = false) => VectraCli.States(target, json, remove);

        [CliCommand(Group, "convert_to_path", "Replaces the geometry by an editable Bezier path with the same outline")]
        static object ConvertToPath([CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target) => VectraCli.ConvertToPath(target);

        [CliCommand(Group, "save_style", "Saves the fills, strokes and effects of a shape as a VectraStyle asset and links it")]
        static object SaveStyle(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("path", "Asset path, e.g. Assets/Styles/Card.asset", Required = true)] string path,
            [CliArg("link", "Make the shape use the new style")] bool link = true) => VectraCli.SaveStyle(target, path, link);

        [CliCommand(Group, "save_shape", "Saves the geometry of a shape as a shared VectraShapeAsset and links it")]
        static object SaveShape(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("path", "Asset path, e.g. Assets/Shapes/Badge.asset", Required = true)] string path,
            [CliArg("link", "Make the shape use the new asset")] bool link = true) => VectraCli.SaveShape(target, path, link);

        [CliCommand(Group, "palette", "Creates or edits a VectraPalette: themes and named colours (one per theme)")]
        static object Palette(
            [CliArg("path", "Asset path of the palette (created when missing)", Required = true)] string path,
            [CliArg("themes", "Comma-separated theme names, e.g. Light,Dark")] string themes = null,
            [CliArg("data", "Colours: {\"Accent\":[\"#3B82F6\",\"#93C5FD\"],\"Surface\":\"#FFFFFF\"}")] string json = null,
            [CliArg("activate", "Make it the active palette")] bool activate = true) => VectraCli.Palette(path, themes, json, activate);

        [CliCommand(Group, "theme", "Selects the theme of a palette (adds a VectraTheme to the scene so it persists)")]
        static object Theme(
            [CliArg("palette", "Asset path of the palette", Required = true)] string palette,
            [CliArg("theme", "Theme name", Required = true)] string theme) => VectraCli.Theme(palette, theme);

        [CliCommand(Group, "svg_create", "Creates a VectraSvgImage for an SVG/document asset")]
        static object SvgCreate(
            [CliArg("asset", "Path of a .svg imported with the Vectra importer, or of a VectraSvgDocument asset", Required = true)] string asset,
            [CliArg("parent", "Parent path/name")] string parent = null,
            [CliArg("name", "GameObject name")] string name = null,
            [CliArg("size", "w,h (default: the document size)")] string size = null,
            [CliArg("text_engine", "project (default), auto, lettera, textmeshpro, legacy or none")] string textEngine = null) => VectraCli.SvgCreate(asset, parent, name, size, textEngine);

        [CliCommand(Group, "svg_parse", "Parses SVG markup into a VectraSvgDocument asset (optionally creating an SVG Image)")]
        static object SvgParse(
            [CliArg("svg", "SVG markup", Required = true)] string svg,
            [CliArg("save", "Asset path for the document, e.g. Assets/Icons/Logo.asset", Required = true)] string save,
            [CliArg("create", "Also create a VectraSvgImage in the scene")] bool create = true,
            [CliArg("parent", "Parent path/name for the image")] string parent = null,
            [CliArg("text_engine", "project (default), auto, lettera, textmeshpro, legacy or none")] string textEngine = null) => VectraCli.SvgParse(svg, save, create, parent, textEngine);

        [CliCommand(Group, "select", "Selects and frames an object in the editor so the user can see it")]
        static object SelectCommand([CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target) => VectraCli.SelectCommand(target);
    }
}
