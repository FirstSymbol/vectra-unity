using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>Component context menus: style clipboard, sprite export and SVG expansion.</summary>
    static class VectraContextMenus
    {
        const string ClipboardPrefix = "VectraStyle:";

        // -----------------------------------------------------------------------------------------------------
        // Style clipboard (fills, strokes, effects and blend mode), stored as JSON in the system clipboard.
        // -----------------------------------------------------------------------------------------------------

        [MenuItem("CONTEXT/VectraShape/Copy Vectra Style", false, 600)]
        [MenuItem("CONTEXT/VectraRenderer/Copy Vectra Style", false, 600)]
        static void CopyStyle(MenuCommand c) => Copy(((IVectraShape)c.context).Content);

        [MenuItem("CONTEXT/VectraShape/Paste Vectra Style", false, 601)]
        [MenuItem("CONTEXT/VectraRenderer/Paste Vectra Style", false, 601)]
        static void PasteStyle(MenuCommand c)
        {
            var target = (Object)c.context;
            var style = ReadClipboard();
            if (style == null) return;
            Undo.RecordObject(target, "Paste Vectra Style");
            var content = ((IVectraShape)target).Content;
            content.style = null;
            content.fills = new System.Collections.Generic.List<FillLayer>();
            foreach (var f in style.fills) content.fills.Add(f?.Clone());
            content.strokes = new System.Collections.Generic.List<StrokeLayer>();
            foreach (var s in style.strokes) content.strokes.Add(s?.Clone());
            content.effects = new System.Collections.Generic.List<VectraEffect>();
            foreach (var e in style.effects) content.effects.Add(e?.Clone());
            Object.DestroyImmediate(style);
            EditorUtility.SetDirty(target);
            ((IVectraShape)target).SetShapeDirty();
        }

        [MenuItem("CONTEXT/VectraShape/Paste Vectra Style", true)]
        [MenuItem("CONTEXT/VectraRenderer/Paste Vectra Style", true)]
        static bool PasteStyleValidate() => EditorGUIUtility.systemCopyBuffer != null && EditorGUIUtility.systemCopyBuffer.StartsWith(ClipboardPrefix);

        [MenuItem("CONTEXT/VectraStyle/Copy Vectra Style", false, 600)]
        static void CopyStyleAsset(MenuCommand c)
        {
            var s = (VectraStyle)c.context;
            var tmp = ScriptableObject.CreateInstance<VectraStyle>();
            EditorUtility.CopySerialized(s, tmp);
            EditorGUIUtility.systemCopyBuffer = ClipboardPrefix + EditorJsonUtility.ToJson(tmp);
            Object.DestroyImmediate(tmp);
        }

        [MenuItem("CONTEXT/VectraStyle/Paste Vectra Style", false, 601)]
        static void PasteStyleAsset(MenuCommand c)
        {
            var s = (VectraStyle)c.context;
            var style = ReadClipboard();
            if (style == null) return;
            Undo.RecordObject(s, "Paste Vectra Style");
            EditorUtility.CopySerialized(style, s);
            Object.DestroyImmediate(style);
            s.NotifyChanged();
        }

        [MenuItem("CONTEXT/VectraStyle/Paste Vectra Style", true)]
        static bool PasteStyleAssetValidate() => PasteStyleValidate();

        /// <summary>Puts the appearance of <paramref name="content"/> on the clipboard.</summary>
        public static void Copy(VectraShapeContent content)
        {
            var tmp = ScriptableObject.CreateInstance<VectraStyle>();
            tmp.fills.Clear();
            foreach (var f in content.ActiveFills) tmp.fills.Add(f?.Clone());
            foreach (var s in content.ActiveStrokes) tmp.strokes.Add(s?.Clone());
            foreach (var e in content.ActiveEffects) tmp.effects.Add(e?.Clone());
            EditorGUIUtility.systemCopyBuffer = ClipboardPrefix + EditorJsonUtility.ToJson(tmp);
            Object.DestroyImmediate(tmp);
        }

        static VectraStyle ReadClipboard()
        {
            var text = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(text) || !text.StartsWith(ClipboardPrefix)) return null;
            var style = ScriptableObject.CreateInstance<VectraStyle>();
            EditorJsonUtility.FromJsonOverwrite(text.Substring(ClipboardPrefix.Length), style);
            return style;
        }

        // -----------------------------------------------------------------------------------------------------
        // Export as sprite asset
        // -----------------------------------------------------------------------------------------------------

        [MenuItem("CONTEXT/VectraShape/Export as Sprite Asset...", false, 620)]
        static void ExportSprite(MenuCommand c)
        {
            var shape = (VectraShape)c.context;
            var r = shape.rectTransform.rect;
            string path = EditorUtility.SaveFilePanelInProject("Export Sprite", shape.name, "png", "Where to save the sprite");
            if (string.IsNullOrEmpty(path)) return;
            var tex = VectraRasterizer.Render(shape.Content, r.size, Mathf.CeilToInt(r.width * 2f), Mathf.CeilToInt(r.height * 2f));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spritePixelsPerUnit = 200f;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Sprite>(path));
        }

        // -----------------------------------------------------------------------------------------------------
        // SVG
        // -----------------------------------------------------------------------------------------------------

        [MenuItem("CONTEXT/VectraSvgImage/Expand to Hierarchy", false, 600)]
        static void ExpandSvg(MenuCommand c) => Expand((VectraSvgImage)c.context);

        [MenuItem("CONTEXT/VectraSvgImage/Expand to Hierarchy", true)]
        static bool ExpandSvgValidate(MenuCommand c) => c.context is VectraSvgImage img && img.Document != null;

        /// <summary>Expands an SVG image into editable shapes (undoable) and hides the original.</summary>
        public static void Expand(VectraSvgImage image)
        {
            var root = VectraSvg.Expand(image);
            if (root == null) return;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Expand SVG");
            Undo.RecordObject(image.gameObject, "Expand SVG");
            image.gameObject.SetActive(false);
            Selection.activeGameObject = root.gameObject;
        }
    }
}
