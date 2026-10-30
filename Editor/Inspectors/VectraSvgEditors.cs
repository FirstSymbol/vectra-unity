using System;
using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>Shared building blocks of the SVG inspectors.</summary>
    static class VxSvg
    {
        /// <summary>Header with a rendered preview of the document (with overrides) and document chips.</summary>
        public static VisualElement Header(SerializedObject so, string kind, Func<VectraSvgDocument> getDoc, Func<List<SvgOverride>> getOverrides)
        {
            var header = VxUI.Header("", kind, out var image, out var title, out var chips);
            Texture2D tex = null;
            VectraSvgDocument lastDoc = null;
            bool pending = true;
            header.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            });
            if (so != null) header.TrackSerializedObjectValue(so, _ => pending = true);

            void Refresh()
            {
                var doc = getDoc();
                if (doc != lastDoc) pending = true;
                lastDoc = doc;
                title.text = doc != null ? doc.name : "No document";
                chips.Clear();
                if (doc != null)
                {
                    int shapes = doc.elements.Count(e => !e.isText && !e.isImage);
                    int texts = doc.elements.Count(e => e.isText);
                    int images = doc.elements.Count(e => e.isImage);
                    chips.Add(VxUI.Chip($"{shapes} shapes", "green"));
                    if (texts > 0) chips.Add(VxUI.Chip($"{texts} text"));
                    if (images > 0) chips.Add(VxUI.Chip($"{images} images"));
                    chips.Add(VxUI.Chip($"{doc.size.x:0.#}×{doc.size.y:0.#}"));
                    if (doc.warnings.Count > 0) chips.Add(VxUI.Chip($"{doc.warnings.Count} warnings", "orange"));
                }
                else chips.Add(VxUI.Chip("Assign an SVG document", "orange"));

                if (!pending) return;
                pending = false;
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                tex = null;
                image.image = null;
                if (doc == null) return;
                try
                {
                    float aspect = doc.AspectRatio;
                    int w = aspect >= 1f ? 168 : Mathf.Max(8, Mathf.RoundToInt(168 * aspect));
                    int h = aspect >= 1f ? Mathf.Max(8, Mathf.RoundToInt(168 / aspect)) : 168;
                    using var r = new VectraShapeRenderer();
                    var rect = new Rect(0, 0, w, h);
                    VectraSvgBuilder.Build(doc, r, rect, 1f, Color.white, true, VectraQuality.High, getOverrides?.Invoke());
                    tex = VectraRasterizer.Render(r, rect, w, h);
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    image.image = tex;
                }
                catch (Exception)
                {
                    image.image = null;
                }
            }

            header.schedule.Execute(Refresh).Every(500);
            header.schedule.Execute(Refresh).StartingIn(30);
            return header;
        }

        /// <summary>Editable list of per-element overrides with a picker of the document's ids, classes and tags.</summary>
        public static VisualElement Overrides(SerializedObject so, SerializedProperty list, Func<VectraSvgDocument> getDoc)
        {
            var root = new VisualElement();
            var items = new VisualElement();
            root.Add(VxUI.Text("Recolour or hide parts of the document by #id, .class or tag name.", "vx-hint"));
            root.Add(items);
            int size = -1;

            void Rebuild()
            {
                items.Clear();
                so.Update();
                size = list.arraySize;
                for (int i = 0; i < size; i++)
                {
                    int index = i;
                    var e = list.GetArrayElementAtIndex(i);
                    var selector = e.FindPropertyRelative("selector");
                    var header = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, alignItems = Align.Center } };
                    header.Add(new VxIcon(VxIcon.Kind.Select, 13));
                    var title = VxUI.Text(selector.stringValue, "vx-card__title");
                    header.Add(title);
                    header.Add(VxUI.IconButton(VxIcon.Kind.Trash, "Remove override", () =>
                    {
                        so.Update();
                        list.DeleteArrayElementAtIndex(index);
                        so.ApplyModifiedProperties();
                        Rebuild();
                    }, true));

                    var body = new VisualElement();
                    var selRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
                    var tf = new TextField { style = { flexGrow = 1 } };
                    tf.BindProperty(selector);
                    selRow.Add(tf);
                    selRow.Add(VxUI.IconButton(VxIcon.Kind.More, "Pick from the document", () => PickSelector(getDoc(), selector)));
                    body.Add(VxUI.Row("Selector", selRow, "#id, .class or a tag name such as path or circle"));
                    var t = typeof(SvgOverride);
                    body.Add(VxFields.For(e.FindPropertyRelative("hide"), t, "Hide"));
                    body.Add(VxFields.For(e.FindPropertyRelative("overrideFill"), t, "Override fill"));
                    body.Add(VxFields.For(e.FindPropertyRelative("fill"), t, "Fill").VisibleWhen(e.FindPropertyRelative("overrideFill"), p => p.boolValue));
                    body.Add(VxFields.For(e.FindPropertyRelative("overrideStroke"), t, "Override stroke"));
                    body.Add(VxFields.For(e.FindPropertyRelative("stroke"), t, "Stroke").VisibleWhen(e.FindPropertyRelative("overrideStroke"), p => p.boolValue));
                    body.Bind(so);
                    var card = VxUI.Card(list.propertyPath + "." + i, header, body, true);
                    card.TrackPropertyValue(selector, p => title.text = p.stringValue);
                    items.Add(card);
                }
            }

            root.Add(VxUI.AddButton("Add override", () =>
            {
                so.Update();
                int i = list.arraySize;
                list.InsertArrayElementAtIndex(i);
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("selector").stringValue = "#id";
                e.FindPropertyRelative("hide").boolValue = false;
                e.FindPropertyRelative("overrideFill").boolValue = true;
                e.FindPropertyRelative("fill").colorValue = Color.white;
                e.FindPropertyRelative("overrideStroke").boolValue = false;
                so.ApplyModifiedProperties();
                Rebuild();
            }));
            Rebuild();
            root.schedule.Execute(() =>
            {
                if (so.targetObject == null) return;
                so.UpdateIfRequiredOrScript();
                if (list.arraySize != size) Rebuild();
            }).Every(300);
            return root;
        }

        static void PickSelector(VectraSvgDocument doc, SerializedProperty selector)
        {
            var menu = new GenericMenu();
            if (doc == null)
            {
                menu.AddDisabledItem(new GUIContent("No document"));
                menu.ShowAsContext();
                return;
            }

            void Item(string path, string value) => menu.AddItem(new GUIContent(path), selector.stringValue == value, () =>
            {
                selector.serializedObject.Update();
                selector.stringValue = value;
                selector.serializedObject.ApplyModifiedProperties();
            });

            foreach (var id in doc.elements.Where(e => !string.IsNullOrEmpty(e.id)).Select(e => e.id).Distinct().Take(200)) Item("Id/#" + id, "#" + id);
            foreach (var c in doc.elements.Where(e => !string.IsNullOrEmpty(e.className)).SelectMany(e => e.className.Split(' ')).Where(c => c.Length > 0).Distinct().Take(200))
                Item("Class/." + c, "." + c);
            foreach (var tag in doc.elements.Select(e => e.tag).Where(t => !string.IsNullOrEmpty(t)).Distinct()) Item("Tag/" + tag, tag);
            menu.ShowAsContext();
        }

        public static VisualElement Warnings(Func<VectraSvgDocument> getDoc)
        {
            var body = new VisualElement();
            void Refresh()
            {
                body.Clear();
                var doc = getDoc();
                if (doc == null || doc.warnings.Count == 0)
                {
                    body.Add(VxUI.Text("Everything in this document is supported.", "vx-empty"));
                    return;
                }

                foreach (var w in doc.warnings)
                {
                    var row = VxUI.El("vx-row");
                    row.Add(new VxIcon(VxIcon.Kind.Warning, 13) { style = { color = new Color(0.96f, 0.65f, 0.14f), marginRight = 6 } });
                    row.Add(VxUI.Text(w, "vx-hint"));
                    body.Add(row);
                }
            }

            Refresh();
            body.schedule.Execute(Refresh).Every(1000);
            return body;
        }

        public static VisualElement ElementList(VectraSvgDocument doc)
        {
            var body = new VisualElement();
            if (doc == null) return body;
            int shown = 0;
            foreach (var e in doc.elements)
            {
                if (shown++ >= 300)
                {
                    body.Add(VxUI.Text($"… and {doc.elements.Count - 300} more", "vx-hint"));
                    break;
                }

                var row = VxUI.El("vx-row");
                var icon = e.isText ? VxIcon.Kind.Text : e.isImage ? VxIcon.Kind.Image : VxIcon.Kind.Shape;
                row.Add(new VxIcon(icon, 12) { style = { marginRight = 6 } });
                var sw = new VxSwatch { style = { width = 14, height = 14, marginRight = 6 } };
                if (e.hasFill) sw.SetPaint(e.fill);
                else if (e.hasStroke) sw.SetPaint(e.stroke.paint);
                row.Add(sw);
                string label = "<" + e.tag + ">" + (string.IsNullOrEmpty(e.id) ? "" : "  #" + e.id) + (string.IsNullOrEmpty(e.className) ? "" : "  ." + e.className);
                if (e.isText) label += "  \"" + e.text + "\"";
                row.Add(VxUI.Text(label, "vx-hint"));
                body.Add(row);
            }

            return body;
        }
    }

    [CustomEditor(typeof(VectraSvgImage)), CanEditMultipleObjects]
    class VectraSvgImageEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var img = (VectraSvgImage)target;
            var so = serializedObject;
            var root = VxUI.Root();
            root.Add(VxSvg.Header(so, "UI SVG Image", () => img != null ? img.Document : null, () => img != null ? img.Overrides : null));
            var t = typeof(VectraSvgImage);

            var doc = new VisualElement();
            doc.Add(VxFields.For(so.FindProperty("m_Document"), t, "Document"));
            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 2 } };
            actions.Add(VxUI.Button("Native size", () =>
            {
                foreach (var o in targets)
                {
                    var i = (VectraSvgImage)o;
                    Undo.RecordObject(i.rectTransform, "Set Native Size");
                    i.SetNativeSize();
                }
            }, "Resize the RectTransform to the document's width and height"));
            actions.Add(VxUI.Button("Expand to hierarchy", () => VectraContextMenus.Expand(img),
                "Replace the image by one editable Vectra Shape per element (the original is hidden, undoable)"));
            doc.Add(actions);
            doc.Add(VxFields.For(so.FindProperty("m_PreserveAspect"), t, "Preserve aspect"));
            root.Add(VxUI.TitledCard("vectra.svg.doc", VxIcon.Kind.Document, "Document", doc));

            var look = new VisualElement();
            look.Add(VxFields.For(so.FindProperty("m_Color"), t, "Tint"));
            look.Add(VxUI.Row("Blend", VxSegmented.ForEnum(so.FindProperty("m_BlendMode"), new[] { "Normal", "Add", "Multiply", "Screen" },
                new[] { VxIcon.Kind.BlendNormal, VxIcon.Kind.BlendAdd, VxIcon.Kind.BlendMultiply, VxIcon.Kind.BlendScreen })));
            look.Add(VxUI.Row("Quality", VxSegmented.ForEnum(so.FindProperty("m_Quality"), new[] { "Low", "Medium", "High", "Ultra" })));
            look.Add(VxFields.For(so.FindProperty("m_RaycastTarget"), t, "Raycast target"));
            look.Add(VxFields.For(so.FindProperty("m_Maskable"), t, "Maskable"));
            root.Add(VxUI.TitledCard("vectra.svg.look", VxIcon.Kind.Fill, "Appearance", look));

            var text = new VisualElement();
            text.Add(VxFields.For(so.FindProperty("m_GenerateText"), t, "Generate text"));
            text.Add(VxFields.For(so.FindProperty("m_FontMap"), t, "Font map").VisibleWhen(so.FindProperty("m_GenerateText"), p => p.boolValue));
            text.Add(VxFields.For(so.FindProperty("m_Font"), t, "Fallback font").VisibleWhen(so.FindProperty("m_GenerateText"), p => p.boolValue));
            text.Add(VxUI.Text("<text> elements become TextMeshPro children (generated, not saved). The font map picks a font per font-family (Assets > Create > Vectra > SVG Font Map); without a match the fallback font, then the TMP default, is used.", "vx-hint"));
            root.Add(VxUI.TitledCard("vectra.svg.text", VxIcon.Kind.Text, "Text", text, null, false));

            root.Add(VxUI.TitledCard("vectra.svg.overrides", VxIcon.Kind.Select, "Overrides", VxSvg.Overrides(so, so.FindProperty("m_Overrides"), () => img != null ? img.Document : null)));
            root.Add(VxUI.TitledCard("vectra.svg.warn", VxIcon.Kind.Warning, "Import report", VxSvg.Warnings(() => img != null ? img.Document : null), null, false));
            root.Bind(so);
            return root;
        }
    }

    [CustomEditor(typeof(VectraSvgRenderer)), CanEditMultipleObjects]
    class VectraSvgRendererEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var r = (VectraSvgRenderer)target;
            var so = serializedObject;
            var root = VxUI.Root();
            root.Add(VxSvg.Header(so, "World SVG Renderer", () => r != null ? r.Document : null, () => r != null ? r.Overrides : null));
            var t = typeof(VectraSvgRenderer);

            var doc = new VisualElement();
            doc.Add(VxFields.For(so.FindProperty("m_Document"), t, "Document"));
            root.Add(VxUI.TitledCard("vectra.svgr.doc", VxIcon.Kind.Document, "Document", doc));

            var size = new VisualElement();
            size.Add(VxFields.For(so.FindProperty("m_Height"), t, "Height"));
            size.Add(VxFields.For(so.FindProperty("m_Pivot"), t, "Pivot"));
            size.Add(VxUI.Text("Width follows the document's aspect ratio. Units are world units of this object.", "vx-hint"));
            root.Add(VxUI.TitledCard("vectra.svgr.size", VxIcon.Kind.Resize, "Size", size));

            var look = new VisualElement();
            look.Add(VxFields.For(so.FindProperty("m_Color"), t, "Tint"));
            look.Add(VxUI.Row("Quality", VxSegmented.ForEnum(so.FindProperty("m_Quality"), new[] { "Low", "Medium", "High", "Ultra" })));
            look.Add(VxFields.For(so.FindProperty("m_SortingOrder"), t, "Sorting order"));
            root.Add(VxUI.TitledCard("vectra.svgr.look", VxIcon.Kind.Fill, "Appearance", look));

            root.Add(VxUI.TitledCard("vectra.svgr.overrides", VxIcon.Kind.Select, "Overrides", VxSvg.Overrides(so, so.FindProperty("m_Overrides"), () => r != null ? r.Document : null)));
            root.Add(VxUI.TitledCard("vectra.svgr.warn", VxIcon.Kind.Warning, "Import report", VxSvg.Warnings(() => r != null ? r.Document : null), null, false));
            root.Bind(so);
            return root;
        }
    }

    [CustomEditor(typeof(VectraSvgDocument))]
    class VectraSvgDocumentEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var doc = (VectraSvgDocument)target;
            var root = VxUI.Root();
            root.Add(VxSvg.Header(null, "Vectra SVG Document", () => doc, null));

            var info = new VisualElement();
            info.Add(VxUI.Row("View box", new Label($"{doc.viewBox.x:0.##}, {doc.viewBox.y:0.##}  ·  {doc.viewBox.width:0.##} × {doc.viewBox.height:0.##}")));
            info.Add(VxUI.Row("Size", new Label($"{doc.size.x:0.##} × {doc.size.y:0.##}")));
            info.Add(VxUI.Row("Aspect", new Label($"{doc.align}{(doc.slice ? " slice" : " meet")}")));
            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 4 } };
            actions.Add(VxUI.Button("Create UI image", () =>
            {
                Selection.activeObject = doc;
                EditorApplication.ExecuteMenuItem("GameObject/UI/Vectra/SVG Image");
            }));
            actions.Add(VxUI.Button("Create world renderer", () =>
            {
                Selection.activeObject = doc;
                EditorApplication.ExecuteMenuItem("GameObject/2D Object/Vectra/SVG Renderer");
            }));
            info.Add(actions);
            root.Add(VxUI.TitledCard("vectra.svgdoc.info", VxIcon.Kind.Document, "Document", info));
            root.Add(VxUI.TitledCard("vectra.svgdoc.elements", VxIcon.Kind.Shape, $"Elements ({doc.elements.Count})", VxSvg.ElementList(doc), null, false));
            root.Add(VxUI.TitledCard("vectra.svgdoc.warn", VxIcon.Kind.Warning, "Import report", VxSvg.Warnings(() => doc)));
            return root;
        }
    }

    [CustomEditor(typeof(VectraSvgImporter))]
    class VectraSvgImporterEditor : ScriptedImporterEditor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var so = serializedObject;
            var root = VxUI.Root();
            string path = ((AssetImporter)target).assetPath;
            root.Add(VxSvg.Header(null, "SVG Importer (Vectra)", () => AssetDatabase.LoadAssetAtPath<VectraSvgDocument>(path), null));

            var t = typeof(VectraSvgImporter);
            var output = new VisualElement();
            var mode = so.FindProperty("mode");
            output.Add(VxUI.Row("Import as", VxSegmented.ForEnum(mode, new[] { "Document", "Sprite", "Texture" },
                new[] { VxIcon.Kind.Document, VxIcon.Kind.Shape, VxIcon.Kind.Image },
                new[] { "Resolution-independent document for VectraSvgImage / VectraSvgRenderer", "Baked sprite", "Baked texture" })));
            output.Add(VxUI.Text("Document keeps the SVG as vectors: crisp at any size, editable colours, one draw call.", "vx-hint")
                .VisibleWhen(mode, p => p.enumValueIndex == 0));
            output.Add(VxFields.For(so.FindProperty("textureSize"), t, "Texture size").VisibleWhen(mode, p => p.enumValueIndex != 0));
            output.Add(VxUI.Row("Quality", VxSegmented.ForEnum(so.FindProperty("quality"), new[] { "Low", "Medium", "High", "Ultra" })).VisibleWhen(mode, p => p.enumValueIndex != 0));
            output.Add(VxFields.For(so.FindProperty("filterMode"), t, "Filter mode").VisibleWhen(mode, p => p.enumValueIndex != 0));
            output.Add(VxFields.For(so.FindProperty("pixelsPerUnit"), t, "Pixels per unit").VisibleWhen(mode, p => p.enumValueIndex == 1));
            output.Add(VxFields.For(so.FindProperty("spritePivot"), t, "Pivot").VisibleWhen(mode, p => p.enumValueIndex == 1));
            root.Add(VxUI.TitledCard("vectra.import.output", VxIcon.Kind.Settings, "Output", output));
            root.Add(VxUI.TitledCard("vectra.import.warn", VxIcon.Kind.Warning, "Import report",
                VxSvg.Warnings(() => AssetDatabase.LoadAssetAtPath<VectraSvgDocument>(path))));
            root.Add(new IMGUIContainer(ApplyRevertGUI) { style = { marginTop = 6 } });
            return root;
        }
    }
}
