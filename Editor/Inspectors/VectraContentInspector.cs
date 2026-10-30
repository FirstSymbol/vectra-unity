using System;
using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Vectra.Rendering;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>
    /// Builds the Vectra inspector (UI Toolkit) for a serialized <see cref="VectraShapeContent"/>: shape type grid,
    /// geometry settings, style, fill/stroke/effect stacks as cards, rendering options and live statistics.
    /// </summary>
    public static class VectraContentInspector
    {
        static readonly Color s_CardBg = new Color(0f, 0f, 0f, 0.12f);
        static readonly Color s_Accent = new Color(0.3f, 0.6f, 1f);
        static List<(Type type, string name, bool analytic)> s_ShapeTypes;
        static List<(Type type, string name, string category)> s_EffectTypes;

        public static VisualElement Build(SerializedObject so, SerializedProperty content, string[] rendering,
            Func<VectraBuildStats> stats, Action<VisualElement> extraHeader = null)
        {
            var root = new VisualElement();
            extraHeader?.Invoke(root);
            root.Add(Section("Shape", BuildShape(so, content), "vectra.shape"));
            root.Add(Section("Appearance", BuildAppearance(so, content), "vectra.appearance"));

            var render = new VisualElement();
            foreach (var name in new[] { "blendMode", "quality", "forcePath" })
                render.Add(new PropertyField(content.FindPropertyRelative(name)));
            if (rendering != null)
                foreach (var name in rendering)
                {
                    var p = so.FindProperty(name);
                    if (p != null) render.Add(new PropertyField(p));
                }

            root.Add(Section("Rendering", render, "vectra.render"));

            if (stats != null)
            {
                var label = new Label { style = { opacity = 0.7f, fontSize = 10, marginTop = 6, whiteSpace = WhiteSpace.Normal } };
                root.Add(label);
                label.schedule.Execute(() =>
                {
                    var s = stats();
                    label.text = $"{(s.analytic ? "Analytic SDF" : "Tessellated path")}{(s.usesSdf ? " + distance field" : "")} · " +
                                 $"{s.vertices} vertices · {s.triangles} triangles · {s.milliseconds:F2} ms";
                }).Every(500);
            }

            return root;
        }

        // ---------------------------------------------------------------------------------------------------
        // Shape
        // ---------------------------------------------------------------------------------------------------

        static List<(Type type, string name, bool analytic)> ShapeTypes()
        {
            if (s_ShapeTypes != null) return s_ShapeTypes;
            var frame = new ShapeFrame { rect = new Rect(-50, -50, 100, 100), pixelSize = 1f };
            s_ShapeTypes = new List<(Type, string, bool)>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<VectraShapeGeometry>())
            {
                if (t.IsAbstract || !t.IsPublic || t.GetConstructor(Type.EmptyTypes) == null) continue;
                var g = (VectraShapeGeometry)Activator.CreateInstance(t);
                s_ShapeTypes.Add((t, g.DisplayName, g.TryGetAnalytic(frame, out _)));
            }

            s_ShapeTypes.Sort((a, b) => a.analytic != b.analytic ? (a.analytic ? -1 : 1) : string.CompareOrdinal(a.name, b.name));
            return s_ShapeTypes;
        }

        static VisualElement BuildShape(SerializedObject so, SerializedProperty content)
        {
            var root = new VisualElement();
            var geometry = content.FindPropertyRelative("geometry");
            var grid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 6 } };
            var body = new VisualElement();
            root.Add(grid);
            root.Add(body);

            void RebuildGrid()
            {
                grid.Clear();
                string current = geometry.managedReferenceFullTypename;
                foreach (var (type, name, analytic) in ShapeTypes())
                {
                    bool selected = !string.IsNullOrEmpty(current) && current.EndsWith(" " + type.FullName);
                    var b = new Button(() =>
                    {
                        so.Update();
                        float rot = 0f;
                        var rp = geometry.FindPropertyRelative("rotation");
                        if (rp != null) rot = rp.floatValue;
                        var g = (VectraShapeGeometry)Activator.CreateInstance(type);
                        g.rotation = rot;
                        geometry.managedReferenceValue = g;
                        so.ApplyModifiedProperties();
                        RebuildGrid();
                        RebuildBody();
                    })
                    {
                        text = name,
                        tooltip = analytic ? "Analytic: exact distance function, effects are free" : "Path: tessellated outline",
                        style =
                        {
                            fontSize = 10, height = 20, marginLeft = 1, marginRight = 1, marginTop = 1, marginBottom = 1,
                            paddingLeft = 4, paddingRight = 4,
                            borderBottomWidth = analytic ? 2 : 1, borderBottomColor = analytic ? new Color(0.3f, 0.8f, 0.5f, 0.6f) : new Color(0.9f, 0.6f, 0.2f, 0.6f)
                        }
                    };
                    if (selected) b.style.backgroundColor = s_Accent * 0.7f;
                    grid.Add(b);
                }
            }

            void RebuildBody()
            {
                body.Clear();
                so.Update();
                if (geometry.managedReferenceValue == null) return;
                AddChildren(body, geometry);
                body.Bind(so);
            }

            RebuildGrid();
            RebuildBody();
            return root;
        }

        // ---------------------------------------------------------------------------------------------------
        // Appearance (fills / strokes / effects, local or from a style)
        // ---------------------------------------------------------------------------------------------------

        static VisualElement BuildAppearance(SerializedObject so, SerializedProperty content)
        {
            var root = new VisualElement();
            var styleProp = content.FindPropertyRelative("style");
            var styleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var styleField = new PropertyField(styleProp, "Style") { style = { flexGrow = 1 } };
            styleRow.Add(styleField);
            styleRow.Add(new Button(() => SaveAsStyle(so, content)) { text = "Save…", tooltip = "Save the appearance as a reusable Style asset" });
            styleRow.Add(new Button(() =>
            {
                foreach (var t in so.targetObjects)
                {
                    Undo.RecordObject(t, "Detach Vectra Style");
                    var c = ContentOf(t);
                    c?.DetachStyle();
                    EditorUtility.SetDirty(t);
                }

                so.Update();
            }) { text = "Detach", tooltip = "Copy the style into this object so it can be edited locally" });
            root.Add(styleRow);

            var stacks = new VisualElement();
            root.Add(stacks);

            void RebuildStacks()
            {
                stacks.Clear();
                so.Update();
                var style = styleProp.objectReferenceValue as VectraStyle;
                SerializedObject target = so;
                SerializedProperty fills, strokes, effects;
                if (style != null)
                {
                    target = new SerializedObject(style);
                    fills = target.FindProperty("fills");
                    strokes = target.FindProperty("strokes");
                    effects = target.FindProperty("effects");
                    stacks.Add(new HelpBox($"Editing the shared style \"{style.name}\" — every shape using it changes.", HelpBoxMessageType.Info));
                }
                else
                {
                    fills = content.FindPropertyRelative("fills");
                    strokes = content.FindPropertyRelative("strokes");
                    effects = content.FindPropertyRelative("effects");
                }

                stacks.Add(LayerList(target, fills, "Fills", "Fill", i => "Fill " + (i + 1), NewFill, FillBody));
                stacks.Add(LayerList(target, strokes, "Strokes", "Stroke", i => "Stroke " + (i + 1), NewStroke, StrokeBody));
                stacks.Add(EffectList(target, effects));
                if (style != null)
                    stacks.TrackSerializedObjectValue(target, _ => style.NotifyChanged());
            }

            styleField.RegisterValueChangeCallback(_ => RebuildStacks());
            RebuildStacks();
            return root;
        }

        static VectraShapeContent ContentOf(UnityEngine.Object o) =>
            o is VectraShape s ? s.Content : o is VectraRenderer r ? r.Content : null;

        static void SaveAsStyle(SerializedObject so, SerializedProperty content)
        {
            var c = ContentOf(so.targetObject);
            if (c == null) return;
            string path = EditorUtility.SaveFilePanelInProject("Save Vectra Style", "NewVectraStyle", "asset", "Save the appearance as a style");
            if (string.IsNullOrEmpty(path)) return;
            var style = ScriptableObject.CreateInstance<VectraStyle>();
            var clone = c.Clone();
            style.fills = clone.ActiveFills.Select(f => f?.Clone()).ToList();
            style.strokes = clone.ActiveStrokes.Select(s => s?.Clone()).ToList();
            style.effects = clone.ActiveEffects.Select(e => e?.Clone()).ToList();
            AssetDatabase.CreateAsset(style, path);
            AssetDatabase.SaveAssets();
            so.Update();
            content.FindPropertyRelative("style").objectReferenceValue = style;
            so.ApplyModifiedProperties();
        }

        static void NewFill(SerializedProperty e)
        {
            e.FindPropertyRelative("enabled").boolValue = true;
            var paint = e.FindPropertyRelative("paint");
            paint.FindPropertyRelative("type").enumValueIndex = (int)PaintType.Solid;
            paint.FindPropertyRelative("color").colorValue = Color.white;
            paint.FindPropertyRelative("opacity").floatValue = 1f;
        }

        static void NewStroke(SerializedProperty e)
        {
            e.FindPropertyRelative("enabled").boolValue = true;
            e.FindPropertyRelative("width").floatValue = 2f;
            e.FindPropertyRelative("miterLimit").floatValue = 4f;
            e.FindPropertyRelative("trimEnd").floatValue = 1f;
            var paint = e.FindPropertyRelative("paint");
            paint.FindPropertyRelative("type").enumValueIndex = (int)PaintType.Solid;
            paint.FindPropertyRelative("color").colorValue = Color.black;
            paint.FindPropertyRelative("opacity").floatValue = 1f;
        }

        static void FillBody(VisualElement body, SerializedProperty e) =>
            body.Add(new PropertyField(e.FindPropertyRelative("paint"), ""));

        static void StrokeBody(VisualElement body, SerializedProperty e)
        {
            body.Add(new PropertyField(e.FindPropertyRelative("paint"), ""));
            foreach (var n in new[] { "width", "alignment", "cap", "join", "miterLimit", "behindFill" })
                body.Add(new PropertyField(e.FindPropertyRelative(n)));

            var dash = new Foldout { text = "Dashes", value = e.FindPropertyRelative("dashed").boolValue };
            foreach (var n in new[] { "dashed", "dashPattern", "dashOffset", "dashRelative" })
                dash.Add(new PropertyField(e.FindPropertyRelative(n)));
            body.Add(dash);

            var trim = new Foldout { text = "Trim Path", value = e.FindPropertyRelative("trimStart").floatValue > 0f || e.FindPropertyRelative("trimEnd").floatValue < 1f };
            foreach (var n in new[] { "trimStart", "trimEnd", "trimOffset" })
                trim.Add(new PropertyField(e.FindPropertyRelative(n)));
            body.Add(trim);

            var profile = new Foldout { text = "Width Profile", value = e.FindPropertyRelative("useWidthProfile").boolValue };
            profile.Add(new PropertyField(e.FindPropertyRelative("useWidthProfile"), "Enabled"));
            profile.Add(new PropertyField(e.FindPropertyRelative("widthProfile"), "Profile"));
            body.Add(profile);
        }

        /// <summary>A list of fill or stroke layers shown as reorderable cards.</summary>
        static VisualElement LayerList(SerializedObject so, SerializedProperty list, string title, string addLabel,
            Func<int, string> name, Action<SerializedProperty> init, Action<VisualElement, SerializedProperty> body)
        {
            var root = new VisualElement { style = { marginTop = 6 } };
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            header.Add(new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } });
            var items = new VisualElement();
            int lastSize = -1;

            void Rebuild()
            {
                items.Clear();
                so.Update();
                lastSize = list.arraySize;
                for (int i = 0; i < list.arraySize; i++)
                {
                    int index = i;
                    var e = list.GetArrayElementAtIndex(i);
                    var content = new VisualElement();
                    body(content, e);
                    items.Add(Card(name(i), e.FindPropertyRelative("enabled"), content, so, list, index,
                        list.propertyPath + "." + i, Rebuild));
                }

                items.Bind(so);
            }

            header.Add(new Button(() =>
            {
                so.Update();
                int i = list.arraySize;
                list.InsertArrayElementAtIndex(i);
                init(list.GetArrayElementAtIndex(i));
                so.ApplyModifiedProperties();
                Rebuild();
            }) { text = "+ " + addLabel });
            root.Add(header);
            root.Add(items);
            Rebuild();
            items.schedule.Execute(() =>
            {
                if (so.targetObject == null) return;
                so.Update();
                if (list.arraySize != lastSize) Rebuild();
            }).Every(300);
            return root;
        }

        static List<(Type type, string name, string category)> EffectTypes()
        {
            if (s_EffectTypes != null) return s_EffectTypes;
            s_EffectTypes = new List<(Type, string, string)>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<VectraEffect>())
            {
                if (t.IsAbstract || !t.IsPublic || t.GetConstructor(Type.EmptyTypes) == null) continue;
                var e = (VectraEffect)Activator.CreateInstance(t);
                string cat = e is DropShadowEffect || e is InnerShadowEffect ? "Shadows" :
                    e is OuterGlowEffect || e is InnerGlowEffect ? "Glows" :
                    e is BevelEffect || e is GrainEffect || e is LayerBlurEffect ? "Surface" :
                    e is ColorAdjustEffect ? "Color" : "Animation";
                s_EffectTypes.Add((t, e.DisplayName, cat));
            }

            s_EffectTypes.Sort((a, b) => string.CompareOrdinal(a.category + a.name, b.category + b.name));
            return s_EffectTypes;
        }

        static VisualElement EffectList(SerializedObject so, SerializedProperty list)
        {
            var root = new VisualElement { style = { marginTop = 6 } };
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            header.Add(new Label("Effects") { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } });
            var items = new VisualElement();
            int lastSize = -1;

            void Rebuild()
            {
                items.Clear();
                so.Update();
                lastSize = list.arraySize;
                for (int i = 0; i < list.arraySize; i++)
                {
                    var e = list.GetArrayElementAtIndex(i);
                    if (e.managedReferenceValue == null) continue;
                    var content = new VisualElement();
                    AddChildren(content, e, "enabled");
                    string title = (e.managedReferenceValue as VectraEffect)?.DisplayName ?? "Effect";
                    items.Add(Card(title, e.FindPropertyRelative("enabled"), content, so, list, i, list.propertyPath + "." + i, Rebuild));
                }

                items.Bind(so);
            }

            var add = new Button { text = "+ Effect" };
            add.clicked += () =>
            {
                var menu = new GenericMenu();
                foreach (var (type, name, category) in EffectTypes())
                {
                    var t = type;
                    menu.AddItem(new GUIContent(category + "/" + name), false, () =>
                    {
                        so.Update();
                        int i = list.arraySize;
                        list.InsertArrayElementAtIndex(i);
                        list.GetArrayElementAtIndex(i).managedReferenceValue = Activator.CreateInstance(t);
                        so.ApplyModifiedProperties();
                        Rebuild();
                    });
                }

                menu.ShowAsContext();
            };
            header.Add(add);
            root.Add(header);
            root.Add(items);
            Rebuild();
            items.schedule.Execute(() =>
            {
                if (so.targetObject == null) return;
                so.Update();
                if (list.arraySize != lastSize) Rebuild();
            }).Every(300);
            return root;
        }

        /// <summary>Card with an eye toggle, foldout title, move up/down and remove buttons.</summary>
        static VisualElement Card(string title, SerializedProperty enabled, VisualElement content, SerializedObject so,
            SerializedProperty list, int index, string key, Action rebuild)
        {
            var card = new VisualElement
            {
                style =
                {
                    backgroundColor = s_CardBg, marginTop = 2, marginBottom = 2, paddingLeft = 4, paddingRight = 4,
                    paddingBottom = 4, borderTopLeftRadius = 4, borderTopRightRadius = 4, borderBottomLeftRadius = 4,
                    borderBottomRightRadius = 4, borderLeftWidth = 3, borderLeftColor = s_Accent * 0.8f
                }
            };
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, height = 22 } };
            var eye = new Toggle { tooltip = "Visible", style = { marginRight = 2 } };
            if (enabled != null) eye.BindProperty(enabled);
            header.Add(eye);
            bool open = SessionState.GetBool(key, true);
            var fold = new Label((open ? "▾ " : "▸ ") + title) { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } };
            fold.RegisterCallback<ClickEvent>(_ =>
            {
                open = !open;
                SessionState.SetBool(key, open);
                content.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                fold.text = (open ? "▾ " : "▸ ") + title;
            });
            header.Add(fold);

            void Move(int delta)
            {
                int to = index + delta;
                if (to < 0 || to >= list.arraySize) return;
                so.Update();
                list.MoveArrayElement(index, to);
                so.ApplyModifiedProperties();
                rebuild();
            }

            header.Add(new Button(() => Move(-1)) { text = "▲", tooltip = "Move up (drawn earlier)" });
            header.Add(new Button(() => Move(1)) { text = "▼", tooltip = "Move down (drawn later)" });
            header.Add(new Button(() =>
            {
                so.Update();
                list.DeleteArrayElementAtIndex(index);
                so.ApplyModifiedProperties();
                rebuild();
            }) { text = "✕", tooltip = "Remove" });
            card.Add(header);
            content.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            card.Add(content);
            return card;
        }

        static VisualElement Section(string title, VisualElement content, string key)
        {
            var f = new Foldout { text = title, value = SessionState.GetBool(key, true) };
            f.RegisterValueChangedCallback(e =>
            {
                if (e.target == f) SessionState.SetBool(key, e.newValue);
            });
            f.style.marginTop = 4;
            f.Q<Toggle>().style.unityFontStyleAndWeight = FontStyle.Bold;
            f.Add(content);
            return f;
        }

        /// <summary>Adds a PropertyField for every visible child of <paramref name="prop"/>.</summary>
        public static void AddChildren(VisualElement parent, SerializedProperty prop, params string[] skip)
        {
            var it = prop.Copy();
            var end = it.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                if (Array.IndexOf(skip, it.name) >= 0) continue;
                parent.Add(new PropertyField(it.Copy()));
            }
        }
    }

    [CustomEditor(typeof(VectraShape)), CanEditMultipleObjects]
    class VectraShapeEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var shape = (VectraShape)target;
            return VectraContentInspector.Build(serializedObject, serializedObject.FindProperty("m_Content"),
                new[] { "m_Color", "m_RaycastTarget", "m_PreciseRaycast", "m_Maskable", "m_WorldPixelSize" },
                () => shape != null ? shape.Stats : default);
        }
    }

    [CustomEditor(typeof(VectraRenderer)), CanEditMultipleObjects]
    class VectraRendererEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var r = (VectraRenderer)target;
            return VectraContentInspector.Build(serializedObject, serializedObject.FindProperty("m_Content"),
                new[] { "m_Size", "m_Pivot", "m_Color", "m_PixelSize", "m_SortingLayer", "m_SortingOrder", "m_GenerateCollider" },
                () => r != null ? r.Stats : default);
        }
    }

    [CustomEditor(typeof(VectraStyle))]
    class VectraStyleEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new HelpBox("Shared appearance: every shape that references this style updates when it changes.", HelpBoxMessageType.None));
            root.Add(new PropertyField(serializedObject.FindProperty("fills")));
            root.Add(new PropertyField(serializedObject.FindProperty("strokes")));
            root.Add(new PropertyField(serializedObject.FindProperty("effects")));
            root.TrackSerializedObjectValue(serializedObject, _ => ((VectraStyle)target).NotifyChanged());
            return root;
        }
    }
}
