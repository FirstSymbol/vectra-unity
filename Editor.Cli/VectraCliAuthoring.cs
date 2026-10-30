using System;
using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

#pragma warning disable CS0618 // FindObjectsByType with a sort mode exists in every supported Unity version

namespace FirstSymbol.Vectra.Editor.Cli
{
    /// <summary>
    /// Authoring commands for agents and scripts: inspect, create and edit Vectra shapes without the GUI.
    /// Every command returns JSON; edits are undoable and mark the scene dirty. Start with
    /// <c>unity command vectra help</c>.
    /// </summary>
    static partial class VectraCliCommands
    {
        const string Group = "vectra";

        // -----------------------------------------------------------------------------------------------------
        // Discovery
        // -----------------------------------------------------------------------------------------------------

        [CliCommand(Group, "help", "How to drive Vectra from the CLI: commands, JSON formats and examples")]
        static object Help() => new Dictionary<string, object>
        {
            ["about"] = "Vectra vector shapes. Targets are hierarchy paths (\"/Canvas/Logo\"), names or instance ids (\"#1234\"). " +
                        "Edits are undoable (Ctrl+Z in the editor) and mark the scene dirty; save with 'unity command scene save'.",
            ["workflow"] = new List<object>
            {
                "1. 'vectra types' lists shapes, effects, paints and their fields with defaults.",
                "2. 'vectra create --shape Star --fill #FFC83D' creates a UI shape (a canvas is created when missing); add --world for a VectraRenderer.",
                "3. 'vectra get --target /Canvas/Star' returns the full description; 'vectra set --target ... --data {...}' patches any part of it.",
                "4. 'vectra export_png --target ... --output preview.png' renders a preview image to check the result."
            },
            ["commands"] = new List<object>
            {
                "help, types, list, get --target, stats",
                "create --shape <type> [--name] [--parent] [--world] [--size w,h] [--position x,y] [--fill color] [--data patch]",
                "set --target <t> --data <patch>   (geometry, fills, strokes, effects, size, position, rotation, color, style, shapeAsset, blendMode, quality, …)",
                "add --target <t> --list fills|strokes|effects [--type <effect>] [--data <element>]",
                "remove --target <t> --list fills|strokes|effects --index <i>",
                "mask --target <t> [--mode stencil|soft] [--softness] [--inset] [--invert] [--remove]",
                "states --target <t> [--data <VectraInteractionStates patch>] [--remove]",
                "convert_to_path --target <t>, save_style --target <t> --path, save_shape --target <t> --path",
                "palette --path <asset> [--themes Light,Dark] [--data {\"Accent\":[\"#3B82F6\",\"#93C5FD\"]}], theme --palette <asset> --theme <name>",
                "svg_create --asset <svg path> [--parent] [--size], svg_parse --svg <markup> --save <asset path> [--create true], expand_svg --target",
                "export_png --target --output [--scale], export_svg --target --output, use_importer --path, reset_atlases, select --target"
            },
            ["json"] = new Dictionary<string, object>
            {
                ["colors"] = "\"#RRGGBB\", \"#RRGGBBAA\", colour names (\"red\") or [r,g,b,a] (HDR above 1)",
                ["vectors"] = "[x, y]; rects [x, y, w, h]",
                ["enums"] = "names, e.g. \"Outside\", \"LinearGradient\", \"Round\"",
                ["assets"] = "asset paths, e.g. \"Assets/Styles/Card.asset\"",
                ["polymorphic"] = "geometry and effects take \"$type\" (\"Star\", \"DropShadow\") or just the type name as a string",
                ["lists"] = "an array replaces the list; an object {\"0\": {...}} patches elements by index",
                ["gradients"] = "{\"stops\":[{\"position\":0,\"color\":\"#f00\"},…]} or the shorthand \"#f00,#00f\""
            },
            ["examples"] = new List<object>
            {
                "unity command vectra create --shape Rectangle --name Card --size 320,180 --data \"{geometry:{cornerRadii:[24,24,24,24]},effects:[{$type:DropShadow,blur:24}]}\"",
                "unity command vectra set --target /Canvas/Card --data \"{fills:[{paint:{type:LinearGradient,gradient:'#6366F1,#EC4899'}}]}\"",
                "unity command vectra set --target /Canvas/Card --data \"{strokes:[{paint:{color:'#FFFFFF80'},width:2,alignment:Inside}]}\"",
                "unity command vectra add --target /Canvas/Card --list effects --type InnerGlow --data \"{size:12}\"",
                "unity command vectra set --target /Canvas/Card --data \"{geometry:{$type:Star,points:6,innerRatio:0.5}}\""
            }
        };

        [CliCommand(Group, "types", "Lists shape, effect and paint types with their fields and default values")]
        static object Types()
        {
            object Describe(Type t)
            {
                var o = Activator.CreateInstance(t);
                var d = (Dictionary<string, object>)CliData.ToData(o);
                var display = t.GetProperty("DisplayName")?.GetValue(o) as string;
                var r = new Dictionary<string, object> { ["type"] = CliData.ShortName(t), ["name"] = display, ["defaults"] = d };
                if (o is VectraShapeGeometry g) r["analytic"] = g.TryGetAnalytic(new ShapeFrame { rect = new Rect(-50, -50, 100, 100), pixelSize = 1f }, out _);
                return r;
            }

            return new Dictionary<string, object>
            {
                ["shapes"] = CliData.GeometryTypes.Values.Distinct().OrderBy(t => t.Name).Select(Describe).ToList(),
                ["effects"] = CliData.EffectTypes.Values.Distinct().OrderBy(t => t.Name).Select(Describe).ToList(),
                ["paint"] = CliData.ToData(new VectraPaint()),
                ["paintTypes"] = Enum.GetNames(typeof(PaintType)).ToList<object>(),
                ["fill"] = CliData.ToData(new FillLayer()),
                ["stroke"] = CliData.ToData(new StrokeLayer()),
                ["blendModes"] = Enum.GetNames(typeof(ShapeBlendMode)).ToList<object>(),
                ["quality"] = Enum.GetNames(typeof(VectraQuality)).ToList<object>()
            };
        }

        [CliCommand(Group, "list", "Lists every Vectra shape, renderer and SVG object in the open scenes")]
        static object ListCommand()
        {
            var result = new List<object>();
            foreach (var s in Object.FindObjectsByType<VectraShape>(FindObjectsInactive.Include, FindObjectsSortMode.None)) result.Add(Summary(s.gameObject, s, s.rectTransform.rect.size));
            foreach (var r in Object.FindObjectsByType<VectraRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) result.Add(Summary(r.gameObject, r, r.Size));
            foreach (var s in Object.FindObjectsByType<VectraSvgImage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                result.Add(new Dictionary<string, object> { ["path"] = CliScene.Path(s.gameObject), ["kind"] = "svgImage", ["document"] = CliData.ToData(s.Document) });
            foreach (var s in Object.FindObjectsByType<VectraSvgRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                result.Add(new Dictionary<string, object> { ["path"] = CliScene.Path(s.gameObject), ["kind"] = "svgRenderer", ["document"] = CliData.ToData(s.Document) });
            return result.OrderBy(x => (string)((Dictionary<string, object>)x)["path"]).ToList();
        }

        static Dictionary<string, object> Summary(GameObject go, IVectraShape s, Vector2 size)
        {
            var c = s.Content;
            return new Dictionary<string, object>
            {
                ["path"] = CliScene.Path(go),
                ["id"] = "#" + go.GetInstanceID(),
                ["kind"] = s is VectraShape ? "ui" : "world",
                ["active"] = go.activeInHierarchy,
                ["shape"] = c.ActiveGeometry != null ? CliData.ShortName(c.ActiveGeometry.GetType()) : null,
                ["size"] = CliData.ToData(size),
                ["fills"] = c.ActiveFills.Count,
                ["strokes"] = c.ActiveStrokes.Count,
                ["effects"] = c.ActiveEffects.Where(e => e != null).Select(e => (object)CliData.ShortName(e.GetType())).ToList(),
                ["style"] = CliData.ToData(c.style),
                ["shapeAsset"] = CliData.ToData(c.shapeAsset)
            };
        }

        [CliCommand(Group, "get", "Full JSON description of a Vectra shape (the same structure 'set' accepts)")]
        static object Get([CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("full", "Include fields that still have their default value")] bool full = false) => Describe(Require(target), full);

        static Dictionary<string, object> Describe(GameObject go, bool full = false)
        {
            CliData.compact = !full;
            try
            {
                return DescribeCore(go);
            }
            finally
            {
                CliData.compact = false;
            }
        }

        static Dictionary<string, object> DescribeCore(GameObject go)
        {
            var s = Shape(go);
            var c = s.Content;
            var d = new Dictionary<string, object>
            {
                ["path"] = CliScene.Path(go),
                ["id"] = "#" + go.GetInstanceID(),
                ["kind"] = s is VectraShape ? "ui" : "world",
                ["geometry"] = CliData.ToData(c.ActiveGeometry),
                ["fills"] = CliData.ToData(c.ActiveFills),
                ["strokes"] = CliData.ToData(c.ActiveStrokes),
                ["effects"] = CliData.ToData(c.ActiveEffects),
                ["style"] = CliData.ToData(c.style),
                ["shapeAsset"] = CliData.ToData(c.shapeAsset),
                ["blendMode"] = c.blendMode.ToString(),
                ["quality"] = c.quality.ToString(),
                ["forcePath"] = c.forcePath
            };
            if (s is VectraShape ui)
            {
                var rt = ui.rectTransform;
                d["size"] = CliData.ToData(rt.rect.size);
                d["position"] = CliData.ToData(rt.anchoredPosition);
                d["rotation"] = Math.Round(rt.localEulerAngles.z, 3);
                d["color"] = CliData.ToData(ui.color);
                var st = ui.Stats;
                d["stats"] = new Dictionary<string, object> { ["vertices"] = st.vertices, ["analytic"] = st.analytic, ["distanceField"] = st.usesSdf, ["ms"] = Math.Round(st.milliseconds, 3) };
            }
            else if (s is VectraRenderer w)
            {
                d["size"] = CliData.ToData(w.Size);
                d["position"] = CliData.ToData(w.transform.localPosition);
                d["rotation"] = CliData.ToData(w.transform.localEulerAngles);
                d["color"] = CliData.ToData(w.Color);
                d["sortingOrder"] = w.SortingOrder;
            }

            if (go.TryGetComponent(out VectraMask m))
                d["mask"] = new Dictionary<string, object> { ["mode"] = m.Mode.ToString(), ["softness"] = m.Softness, ["inset"] = m.Inset, ["invert"] = m.Invert, ["showGraphic"] = m.showMaskGraphic };
            if (go.TryGetComponent(out VectraInteractionStates states)) d["states"] = CliData.ToData(states);
            return d;
        }

        // -----------------------------------------------------------------------------------------------------
        // Creation and editing
        // -----------------------------------------------------------------------------------------------------

        [CliCommand(Group, "create", "Creates a Vectra shape (UI by default, --world for a VectraRenderer) and returns its description")]
        static object Create(
            [CliArg("shape", "Shape type (see 'vectra types'), e.g. Rectangle, Star, Path, TracedSprite", Required = true)] string shape,
            [CliArg("name", "GameObject name")] string name = null,
            [CliArg("parent", "Parent path/name (UI: inside a canvas; created when missing)")] string parent = null,
            [CliArg("world", "Create a world-space VectraRenderer instead of a UI shape")] bool world = false,
            [CliArg("size", "w,h (UI: pixels, world: units)")] string size = null,
            [CliArg("position", "x,y (UI: anchored position, world: local position)")] string position = null,
            [CliArg("fill", "Fill colour, e.g. #3B82F6")] string fill = null,
            [CliArg("data", "Patch applied after creation (same format as 'set')")] string json = null)
        {
            var type = CliData.ResolveType(shape, typeof(VectraShapeGeometry));
            var geometry = (VectraShapeGeometry)Activator.CreateInstance(type);
            GameObject go;
            Transform parentT = string.IsNullOrEmpty(parent) ? null : Require(parent).transform;
            if (world)
            {
                go = new GameObject(name ?? "Vectra " + CliData.ShortName(type), typeof(VectraRenderer));
                if (parentT != null) go.transform.SetParent(parentT, false);
                var r = go.GetComponent<VectraRenderer>();
                r.Geometry = geometry;
                r.Content.fills.Clear();
                r.Content.fills.Add(new FillLayer(VectraPaint.Solid(new Color(0.24f, 0.55f, 0.96f))));
                if (size != null) r.Size = (Vector2)CliData.Convert(size, typeof(Vector2));
                if (position != null) go.transform.localPosition = (Vector2)CliData.Convert(position, typeof(Vector2));
            }
            else
            {
                if (parentT == null || parentT.GetComponentInParent<Canvas>() == null) parentT = parentT != null ? EnsureCanvasUnder(parentT) : EnsureCanvas();
                go = new GameObject(name ?? "Vectra " + CliData.ShortName(type), typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
                go.transform.SetParent(parentT, false);
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = size != null ? (Vector2)CliData.Convert(size, typeof(Vector2)) : new Vector2(160, 160);
                if (position != null) rt.anchoredPosition = (Vector2)CliData.Convert(position, typeof(Vector2));
                var s = go.GetComponent<VectraShape>();
                s.Geometry = geometry;
                s.Content.fills.Clear();
                s.Content.fills.Add(new FillLayer(VectraPaint.Solid(new Color(0.24f, 0.55f, 0.96f))));
            }

            try
            {
                var shapeComp = Shape(go);
                if (fill != null) shapeComp.Content.fills[0].paint = VectraPaint.Solid(CliData.ToColor(fill));
                if (!string.IsNullOrEmpty(json)) ApplyPatch(go, Parse(json));
            }
            catch
            {
                // A bad patch must not leave a half-configured object behind.
                Object.DestroyImmediate(go);
                throw;
            }

            Undo.RegisterCreatedObjectUndo(go, "Vectra CLI create");
            Touch(go);
            return Describe(go);
        }

        [CliCommand(Group, "set", "Patches a shape with JSON: geometry, fills, strokes, effects, size, position, rotation, color, style, …")]
        static object Set(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("data", "Patch, e.g. {\"geometry\":{\"$type\":\"Star\",\"points\":6},\"fills\":{\"0\":{\"paint\":{\"color\":\"#ff0\"}}}}", Required = true)] string json)
        {
            var go = Require(target);
            ApplyPatch(go, Parse(json));
            Touch(go);
            return Describe(go);
        }

        static void ApplyPatch(GameObject go, Dictionary<string, object> patch)
        {
            var s = Shape(go);
            var comp = (Component)s;
            Undo.RecordObject(comp, "Vectra CLI edit");
            Undo.RecordObject(go.transform, "Vectra CLI edit");
            var c = s.Content;
            var contentPatch = new Dictionary<string, object>();
            foreach (var kv in patch)
            {
                switch (kv.Key.ToLowerInvariant())
                {
                    case "name": go.name = kv.Value.ToString(); break;
                    case "active": go.SetActive((bool)CliData.Convert(kv.Value, typeof(bool))); break;
                    case "size":
                        var size = (Vector2)CliData.Convert(kv.Value, typeof(Vector2));
                        if (s is VectraShape ui) ui.rectTransform.sizeDelta = size;
                        else ((VectraRenderer)s).Size = size;
                        break;
                    case "position":
                        if (s is VectraShape ui2) ui2.rectTransform.anchoredPosition = (Vector2)CliData.Convert(kv.Value, typeof(Vector2));
                        else go.transform.localPosition = (Vector3)CliData.Convert(kv.Value is List<object> l && l.Count == 2 ? new List<object> { l[0], l[1], 0.0 } : kv.Value, typeof(Vector3));
                        break;
                    case "rotation":
                        go.transform.localEulerAngles = kv.Value is List<object>
                            ? (Vector3)CliData.Convert(kv.Value, typeof(Vector3))
                            : new Vector3(0f, 0f, (float)CliData.Convert(kv.Value, typeof(float)));
                        break;
                    case "color":
                        if (s is VectraShape ui3) ui3.color = CliData.ToColor(kv.Value);
                        else ((VectraRenderer)s).Color = CliData.ToColor(kv.Value);
                        break;
                    case "sortingorder":
                        if (s is VectraRenderer wr) wr.SortingOrder = (int)CliData.Convert(kv.Value, typeof(int));
                        break;
                    case "geometry":
                        // Editing the geometry of a shape that uses a shared asset edits the asset.
                        if (c.shapeAsset != null && !(kv.Value is string))
                        {
                            Undo.RecordObject(c.shapeAsset, "Vectra CLI edit");
                            c.shapeAsset.geometry = (VectraShapeGeometry)CliData.Convert(kv.Value, typeof(VectraShapeGeometry), c.shapeAsset.geometry);
                            EditorUtility.SetDirty(c.shapeAsset);
                            c.shapeAsset.NotifyChanged();
                        }
                        else
                        {
                            c.shapeAsset = null;
                            c.geometry = (VectraShapeGeometry)CliData.Convert(kv.Value, typeof(VectraShapeGeometry), c.geometry);
                        }

                        break;
                    case "fills":
                    case "strokes":
                    case "effects":
                        if (c.style != null)
                        {
                            // Shared style: edit the asset so every user updates.
                            Undo.RecordObject(c.style, "Vectra CLI edit");
                            CliData.Apply(c.style, new Dictionary<string, object> { [kv.Key] = kv.Value });
                            EditorUtility.SetDirty(c.style);
                            c.style.NotifyChanged();
                        }
                        else contentPatch[kv.Key] = kv.Value;

                        break;
                    default:
                        contentPatch[kv.Key] = kv.Value;
                        break;
                }
            }

            if (contentPatch.Count > 0) CliData.Apply(c, contentPatch);
            s.SetShapeDirty();
            EditorUtility.SetDirty(comp);
        }

        [CliCommand(Group, "add", "Appends a fill, stroke or effect (--type for effects) with an optional JSON patch")]
        static object Add(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("list", "fills, strokes or effects", Required = true)] string list,
            [CliArg("type", "Effect type, e.g. DropShadow, OuterGlow, Outline (effects only)")] string type = null,
            [CliArg("data", "Patch for the new element")] string json = null)
        {
            var go = Require(target);
            var s = Shape(go);
            Undo.RecordObject((Component)s, "Vectra CLI add");
            var c = s.Content;
            var patch = string.IsNullOrEmpty(json) ? new Dictionary<string, object>() : Parse(json);
            switch (list.ToLowerInvariant())
            {
                case "fills":
                    c.ActiveFills.Add((FillLayer)CliData.Convert(patch, typeof(FillLayer), new FillLayer(VectraPaint.Solid(Color.white))));
                    break;
                case "strokes":
                    c.ActiveStrokes.Add((StrokeLayer)CliData.Convert(patch, typeof(StrokeLayer), new StrokeLayer(Color.black, 2f)));
                    break;
                case "effects":
                    if (string.IsNullOrEmpty(type)) throw new ArgumentException("--type is required for effects (see 'vectra types')");
                    var fx = (VectraEffect)Activator.CreateInstance(CliData.ResolveType(type, typeof(VectraEffect)));
                    CliData.Apply(fx, patch);
                    c.ActiveEffects.Add(fx);
                    break;
                default: throw new ArgumentException("--list must be fills, strokes or effects");
            }

            c.style?.NotifyChanged();
            Touch(go);
            return Describe(go);
        }

        [CliCommand(Group, "remove", "Removes a fill, stroke or effect by index")]
        static object Remove(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("list", "fills, strokes or effects", Required = true)] string list,
            [CliArg("index", "Element index", Required = true)] int index)
        {
            var go = Require(target);
            var s = Shape(go);
            Undo.RecordObject((Component)s, "Vectra CLI remove");
            var c = s.Content;
            System.Collections.IList l = list.ToLowerInvariant() switch
            {
                "fills" => c.ActiveFills,
                "strokes" => c.ActiveStrokes,
                "effects" => c.ActiveEffects,
                _ => throw new ArgumentException("--list must be fills, strokes or effects")
            };
            if (index < 0 || index >= l.Count) throw new ArgumentException($"Index {index} out of range (count {l.Count})");
            l.RemoveAt(index);
            c.style?.NotifyChanged();
            Touch(go);
            return Describe(go);
        }

        [CliCommand(Group, "mask", "Adds or configures a VectraMask (stencil or soft) on a UI shape")]
        static object MaskCommand(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("mode", "stencil or soft")] string mode = null,
            [CliArg("softness", "Soft edge width")] float softness = -1f,
            [CliArg("inset", "Moves the soft edge inwards")] float inset = float.NaN,
            [CliArg("invert", "Show the children outside the shape")] string invert = null,
            [CliArg("show_graphic", "Draw the mask shape itself")] string showGraphic = null,
            [CliArg("remove", "Remove the mask")] bool remove = false)
        {
            var go = Require(target);
            if (!go.TryGetComponent(out VectraShape _)) throw new ArgumentException("Masks need a VectraShape (UI)");
            if (remove)
            {
                if (go.TryGetComponent(out VectraMask old)) Undo.DestroyObjectImmediate(old);
                Touch(go);
                return Describe(go);
            }

            if (!go.TryGetComponent(out VectraMask m)) m = Undo.AddComponent<VectraMask>(go);
            Undo.RecordObject(m, "Vectra CLI mask");
            if (!string.IsNullOrEmpty(mode)) m.Mode = (VectraMaskMode)CliData.Convert(mode, typeof(VectraMaskMode));
            if (softness >= 0f) m.Softness = softness;
            if (!float.IsNaN(inset)) m.Inset = inset;
            if (invert != null) m.Invert = bool.Parse(invert);
            if (showGraphic != null) m.showMaskGraphic = bool.Parse(showGraphic);
            Touch(go);
            return Describe(go);
        }

        [CliCommand(Group, "states", "Adds or configures hover/pressed/selected/disabled looks (VectraInteractionStates)")]
        static object States(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("data", "Patch, e.g. {\"hover\":{\"scale\":1.05},\"duration\":0.2}")] string json = null,
            [CliArg("remove", "Remove the component")] bool remove = false)
        {
            var go = Require(target);
            if (remove)
            {
                if (go.TryGetComponent(out VectraInteractionStates old)) Undo.DestroyObjectImmediate(old);
            }
            else
            {
                if (!go.TryGetComponent(out VectraInteractionStates st)) st = Undo.AddComponent<VectraInteractionStates>(go);
                Undo.RecordObject(st, "Vectra CLI states");
                if (!string.IsNullOrEmpty(json)) CliData.Apply(st, Parse(json));
                EditorUtility.SetDirty(st);
            }

            Touch(go);
            return Describe(go);
        }

        [CliCommand(Group, "convert_to_path", "Replaces the geometry by an editable Bezier path with the same outline")]
        static object ConvertToPath([CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target)
        {
            var go = Require(target);
            var s = Shape(go);
            Undo.RecordObject((Component)s, "Vectra CLI convert");
            var size = s is VectraShape ui ? ui.rectTransform.rect.size : ((VectraRenderer)s).Size;
            var frame = new ShapeFrame { rect = new Rect(-size * 0.5f, size), pixelSize = 1f };
            var path = VectraPathConverter.ToPathShape(s.Content.ActiveGeometry, frame);
            s.Content.shapeAsset = null;
            s.Content.geometry = path;
            Touch(go);
            return Describe(go);
        }

        [CliCommand(Group, "save_style", "Saves the fills, strokes and effects of a shape as a VectraStyle asset and links it")]
        static object SaveStyle(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("path", "Asset path, e.g. Assets/Styles/Card.asset", Required = true)] string path,
            [CliArg("link", "Make the shape use the new style")] bool link = true)
        {
            var go = Require(target);
            var s = Shape(go);
            var c = s.Content;
            var style = ScriptableObject.CreateInstance<VectraStyle>();
            style.fills = c.ActiveFills.Select(f => f?.Clone()).ToList();
            style.strokes = c.ActiveStrokes.Select(x => x?.Clone()).ToList();
            style.effects = c.ActiveEffects.Select(e => e?.Clone()).ToList();
            CreateAsset(style, path);
            if (link)
            {
                Undo.RecordObject((Component)s, "Vectra CLI style");
                c.style = style;
                Touch(go);
            }

            return new Dictionary<string, object> { ["style"] = path, ["shape"] = Describe(go) };
        }

        [CliCommand(Group, "save_shape", "Saves the geometry of a shape as a shared VectraShapeAsset and links it")]
        static object SaveShape(
            [CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target,
            [CliArg("path", "Asset path, e.g. Assets/Shapes/Badge.asset", Required = true)] string path,
            [CliArg("link", "Make the shape use the new asset")] bool link = true)
        {
            var go = Require(target);
            var s = Shape(go);
            var asset = ScriptableObject.CreateInstance<VectraShapeAsset>();
            asset.geometry = s.Content.ActiveGeometry?.Clone();
            asset.defaultStyle = s.Content.style;
            CreateAsset(asset, path);
            if (link)
            {
                Undo.RecordObject((Component)s, "Vectra CLI shape asset");
                s.Content.shapeAsset = asset;
                Touch(go);
            }

            return new Dictionary<string, object> { ["shapeAsset"] = path, ["shape"] = Describe(go) };
        }

        [CliCommand(Group, "palette", "Creates or edits a VectraPalette: themes and named colours (one per theme)")]
        static object Palette(
            [CliArg("path", "Asset path of the palette (created when missing)", Required = true)] string path,
            [CliArg("themes", "Comma-separated theme names, e.g. Light,Dark")] string themes = null,
            [CliArg("data", "Colours: {\"Accent\":[\"#3B82F6\",\"#93C5FD\"],\"Surface\":\"#FFFFFF\"}")] string json = null,
            [CliArg("activate", "Make it the active palette")] bool activate = true)
        {
            var palette = AssetDatabase.LoadAssetAtPath<VectraPalette>(path);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<VectraPalette>();
                CreateAsset(palette, path);
            }

            Undo.RecordObject(palette, "Vectra CLI palette");
            if (!string.IsNullOrEmpty(themes)) palette.themes = themes.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            if (!string.IsNullOrEmpty(json))
                foreach (var kv in Parse(json))
                {
                    var values = kv.Value is List<object> l ? l : new List<object> { kv.Value };
                    for (int i = 0; i < palette.themes.Count; i++)
                        palette.SetColor(kv.Key, i, CliData.ToColor(values[Math.Min(i, values.Count - 1)]));
                }

            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();
            if (activate) VectraPalette.Active = palette;
            palette.NotifyChanged();
            return CliData.ToData(palette);
        }

        [CliCommand(Group, "theme", "Selects the theme of a palette (adds a VectraTheme to the scene so it persists)")]
        static object Theme(
            [CliArg("palette", "Asset path of the palette", Required = true)] string palette,
            [CliArg("theme", "Theme name", Required = true)] string theme)
        {
            var p = AssetDatabase.LoadAssetAtPath<VectraPalette>(palette) ?? throw new ArgumentException("No palette at " + palette);
            var comp = Object.FindObjectsByType<VectraTheme>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.Palette == p);
            if (comp == null)
            {
                var go = new GameObject("Vectra Theme");
                Undo.RegisterCreatedObjectUndo(go, "Vectra CLI theme");
                comp = go.AddComponent<VectraTheme>();
                comp.Palette = p;
            }

            Undo.RecordObject(comp, "Vectra CLI theme");
            if (!p.themes.Contains(theme)) throw new ArgumentException($"Theme '{theme}' not in [{string.Join(", ", p.themes)}]");
            comp.SetTheme(theme);
            EditorUtility.SetDirty(comp);
            EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
            return new Dictionary<string, object> { ["palette"] = palette, ["theme"] = theme, ["component"] = CliScene.Path(comp.gameObject) };
        }

        [CliCommand(Group, "svg_create", "Creates a VectraSvgImage for an SVG/document asset")]
        static object SvgCreate(
            [CliArg("asset", "Path of a .svg imported with the Vectra importer, or of a VectraSvgDocument asset", Required = true)] string asset,
            [CliArg("parent", "Parent path/name")] string parent = null,
            [CliArg("name", "GameObject name")] string name = null,
            [CliArg("size", "w,h (default: the document size)")] string size = null)
        {
            var doc = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<VectraSvgDocument>().FirstOrDefault()
                      ?? throw new ArgumentException($"No Vectra SVG document at '{asset}' (choose the Vectra importer: 'vectra use_importer --path {asset}')");
            var parentT = string.IsNullOrEmpty(parent) ? EnsureCanvas() : Require(parent).transform;
            if (parentT.GetComponentInParent<Canvas>() == null) parentT = EnsureCanvasUnder(parentT);
            var go = new GameObject(name ?? doc.name, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraSvgImage));
            go.transform.SetParent(parentT, false);
            var img = go.GetComponent<VectraSvgImage>();
            img.Document = doc;
            img.FontMap = VectraProjectSettings.instance.defaultFontMap;
            if (size != null) ((RectTransform)go.transform).sizeDelta = (Vector2)CliData.Convert(size, typeof(Vector2));
            else img.SetNativeSize();
            Undo.RegisterCreatedObjectUndo(go, "Vectra CLI svg");
            Touch(go);
            return new Dictionary<string, object> { ["path"] = CliScene.Path(go), ["elements"] = doc.elements.Count, ["warnings"] = doc.warnings.ToList<object>() };
        }

        [CliCommand(Group, "svg_parse", "Parses SVG markup into a VectraSvgDocument asset (optionally creating an SVG Image)")]
        static object SvgParse(
            [CliArg("svg", "SVG markup", Required = true)] string svg,
            [CliArg("save", "Asset path for the document, e.g. Assets/Icons/Logo.asset", Required = true)] string save,
            [CliArg("create", "Also create a VectraSvgImage in the scene")] bool create = true,
            [CliArg("parent", "Parent path/name for the image")] string parent = null)
        {
            var doc = VectraSvg.Parse(svg);
            doc.name = System.IO.Path.GetFileNameWithoutExtension(save);
            CreateAsset(doc, save);
            var result = new Dictionary<string, object> { ["document"] = save, ["elements"] = doc.elements.Count, ["warnings"] = doc.warnings.ToList<object>() };
            if (create) result["image"] = SvgCreate(save, parent, doc.name, null);
            return result;
        }

        [CliCommand(Group, "select", "Selects and frames an object in the editor so the user can see it")]
        static object SelectCommand([CliArg("target", "Hierarchy path, name or #instanceId", Required = true)] string target)
        {
            var go = Require(target);
            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();
            return CliScene.Path(go);
        }

        // -----------------------------------------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------------------------------------

        static Dictionary<string, object> Parse(string json)
        {
            var v = MiniJson.Parse(json);
            return v as Dictionary<string, object> ?? throw new ArgumentException("Expected a JSON object");
        }

        static GameObject Require(string target) => CliScene.Find(target) ?? throw new ArgumentException($"GameObject not found: {target}");

        static IVectraShape Shape(GameObject go)
        {
            if (go.TryGetComponent(out VectraShape s)) return s;
            if (go.TryGetComponent(out VectraRenderer r)) return r;
            throw new ArgumentException($"{CliScene.Path(go)} has no VectraShape or VectraRenderer");
        }

        static void Touch(GameObject go)
        {
            if (go.TryGetComponent(out IVectraShape s)) s.SetShapeDirty();
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }

        static void CreateAsset(Object asset, string path)
        {
            var dir = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                var parts = dir.Split('/');
                string cur = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    if (!AssetDatabase.IsValidFolder(cur + "/" + parts[i])) AssetDatabase.CreateFolder(cur, parts[i]);
                    cur += "/" + parts[i];
                }
            }

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
        }

        static Transform EnsureCanvas()
        {
            var canvas = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(c => c.isRootCanvas);
            if (canvas != null) return canvas.transform;
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            Undo.RegisterCreatedObjectUndo(go, "Vectra CLI canvas");
            if (Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                var module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                es.AddComponent(module ?? typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(es, "Vectra CLI event system");
            }

            return go.transform;
        }

        static Transform EnsureCanvasUnder(Transform parent)
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            Undo.RegisterCreatedObjectUndo(go, "Vectra CLI canvas");
            return go.transform;
        }
    }
}
