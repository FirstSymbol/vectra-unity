using System;
using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Vectra.Rendering;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>
    /// The Vectra inspector: live preview header, tabs (Shape, Fill, Stroke, Effects, Settings), shape picker with
    /// real thumbnails, Figma-like layer cards and visual controls for every setting.
    /// </summary>
    public static class VectraContentInspector
    {
        public sealed class Context
        {
            public SerializedObject so;
            public SerializedProperty content;
            public Type componentType;
            public string[] componentFields;
            public string kind;
            public Func<VectraBuildStats> stats;
            public Func<VectraShapeContent> getContent;
            public Func<Vector2> getSize;
        }

        static readonly (string category, string[] names)[] s_Categories =
        {
            ("Basic", new[] { "Rectangle", "Ellipse", "Arc / Ring / Pie", "Line", "Squircle" }),
            ("Polygons", new[] { "Polygon", "Star", "Triangle", "Rhombus", "Trapezoid", "Parallelogram" }),
            ("Symbols", new[] { "Cross", "Heart", "Arrow", "Chevron", "Crescent", "Egg", "Vesica", "Flower", "Gear", "Speech Bubble", "Checkmark" }),
            ("Paths", new[] { "Path", "Polyline / Polygon", "Spiral", "Wave", "SVG Path" }),
            ("Advanced", new[] { "Boolean Group", "Morph" })
        };

        static List<(Type type, string name, string category, VectraShapeGeometry sample)> s_Shapes;

        static List<(Type type, string name, string category, VectraShapeGeometry sample)> Shapes()
        {
            if (s_Shapes != null) return s_Shapes;
            s_Shapes = new List<(Type, string, string, VectraShapeGeometry)>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<VectraShapeGeometry>())
            {
                if (t.IsAbstract || !t.IsPublic || t.GetConstructor(Type.EmptyTypes) == null) continue;
                var g = (VectraShapeGeometry)Activator.CreateInstance(t);
                string cat = s_Categories.FirstOrDefault(c => c.names.Contains(g.DisplayName)).category ?? "Advanced";
                s_Shapes.Add((t, g.DisplayName, cat, g));
            }

            s_Shapes.Sort((a, b) =>
            {
                int ca = Array.FindIndex(s_Categories, c => c.category == a.category), cb = Array.FindIndex(s_Categories, c => c.category == b.category);
                if (ca != cb) return ca.CompareTo(cb);
                var names = s_Categories[Math.Max(ca, 0)].names;
                return Array.IndexOf(names, a.name).CompareTo(Array.IndexOf(names, b.name));
            });
            return s_Shapes;
        }

        public static VisualElement Build(Context ctx)
        {
            var root = VxUI.Root();
            var header = BuildHeader(ctx, out var refreshHeader);
            root.Add(header);

            var tabs = VxUI.El("vx-tabs");
            var page = VxUI.El("vx-page");
            root.Add(tabs);
            root.Add(page);

            var defs = new (string name, VxIcon.Kind icon, Func<int> count, Func<VisualElement> build)[]
            {
                ("Shape", VxIcon.Kind.Shape, null, () => ShapePage(ctx, refreshHeader)),
                ("Fill", VxIcon.Kind.Fill, () => Count(ctx, "fills"), () => AppearancePage(ctx, "fills")),
                ("Stroke", VxIcon.Kind.Stroke, () => Count(ctx, "strokes"), () => AppearancePage(ctx, "strokes")),
                ("Effects", VxIcon.Kind.Effects, () => Count(ctx, "effects"), () => AppearancePage(ctx, "effects")),
                ("Settings", VxIcon.Kind.Settings, null, () => SettingsPage(ctx))
            };

            var tabEls = new List<VisualElement>();
            var badges = new List<Label>();
            int active = Mathf.Clamp(SessionState.GetInt("vectra.tab", 0), 0, defs.Length - 1);

            void Show(int i)
            {
                active = i;
                SessionState.SetInt("vectra.tab", i);
                for (int k = 0; k < tabEls.Count; k++) tabEls[k].EnableInClassList("vx-tab--active", k == i);
                page.Clear();
                page.Add(defs[i].build());
            }

            for (int i = 0; i < defs.Length; i++)
            {
                int index = i;
                var tab = VxUI.El("vx-tab");
                tab.Add(new VxIcon(defs[i].icon, 13));
                tab.Add(VxUI.Text(defs[i].name, "vx-tab__label"));
                var badge = VxUI.Text("", "vx-tab__badge");
                badge.style.display = defs[i].count != null ? DisplayStyle.Flex : DisplayStyle.None;
                tab.Add(badge);
                badges.Add(badge);
                tab.RegisterCallback<ClickEvent>(_ => Show(index));
                tabEls.Add(tab);
                tabs.Add(tab);
            }

            void RefreshBadges()
            {
                if (ctx.so.targetObject == null) return;
                for (int i = 0; i < defs.Length; i++)
                    if (defs[i].count != null)
                    {
                        int c = defs[i].count();
                        badges[i].text = c.ToString();
                        badges[i].style.display = c > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    }
            }

            root.schedule.Execute(() =>
            {
                RefreshBadges();
                refreshHeader(false);
            }).Every(500);
            RefreshBadges();
            Show(active);
            return root;
        }

        static int Count(Context ctx, string list)
        {
            var c = ctx.getContent?.Invoke();
            if (c == null) return 0;
            return list == "fills" ? c.ActiveFills.Count : list == "strokes" ? c.ActiveStrokes.Count : c.ActiveEffects.Count;
        }

        // ---------------------------------------------------------------------------------------------------
        // Header with live preview
        // ---------------------------------------------------------------------------------------------------

        static VisualElement BuildHeader(Context ctx, out Action<bool> refresh)
        {
            var header = VxUI.El("vx-header");
            var preview = VxUI.El("vx-preview");
            preview.generateVisualContent += mgc =>
            {
                var r = preview.contentRect;
                var p = mgc.painter2D;
                const float cell = 8f;
                for (int y = 0; y * cell < r.height; y++)
                for (int x = 0; x * cell < r.width; x++)
                {
                    p.fillColor = (x + y) % 2 == 0 ? new Color(1, 1, 1, 0.025f) : new Color(0, 0, 0, 0.025f);
                    p.BeginPath();
                    p.MoveTo(new Vector2(x * cell, y * cell));
                    p.LineTo(new Vector2(x * cell + cell, y * cell));
                    p.LineTo(new Vector2(x * cell + cell, y * cell + cell));
                    p.LineTo(new Vector2(x * cell, y * cell + cell));
                    p.ClosePath();
                    p.Fill();
                }
            };
            var image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.style.flexGrow = 1;
            preview.Add(image);
            header.Add(preview);

            var info = VxUI.El("vx-header__info");
            var title = VxUI.Text("", "vx-title");
            var subtitle = VxUI.Text(ctx.kind, "vx-subtitle");
            var chips = VxUI.El("vx-chips");
            info.Add(title);
            info.Add(subtitle);
            info.Add(chips);
            header.Add(info);

            Texture2D tex = null;
            bool pending = true;
            header.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            });
            header.TrackSerializedObjectValue(ctx.so, _ => pending = true);

            void Refresh(bool force)
            {
                if (ctx.so.targetObject == null) return;
                var content = ctx.getContent();
                title.text = content?.geometry?.DisplayName ?? "No shape";
                var s = ctx.stats();
                chips.Clear();
                bool built = s.vertices > 0;
                bool analytic = built ? s.analytic : IsAnalytic(content, ctx.getSize());
                chips.Add(VxUI.Chip(analytic ? "Analytic SDF" : "Tessellated", analytic ? "green" : "orange"));
                if (s.usesSdf) chips.Add(VxUI.Chip("Distance field", "accent"));
                if (built)
                {
                    chips.Add(VxUI.Chip($"{s.vertices} verts"));
                    chips.Add(VxUI.Chip($"{s.milliseconds:0.00} ms"));
                }
                if (content?.style != null) chips.Add(VxUI.Chip("Style: " + content.style.name, "accent"));
                if (!(pending || force) || content == null) return;
                pending = false;
                try
                {
                    var size = ctx.getSize();
                    if (size.x <= 0f || size.y <= 0f) return;
                    var t = VectraRasterizer.Render(content, size, 168, 168);
                    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                    tex = t;
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    image.image = tex;
                }
                catch (Exception)
                {
                    image.image = null;
                }
            }

            refresh = Refresh;
            header.schedule.Execute(() => Refresh(true)).StartingIn(50);
            return header;
        }

        static bool IsAnalytic(VectraShapeContent content, Vector2 size)
        {
            if (content?.geometry == null || content.forcePath) return false;
            var frame = new ShapeFrame { rect = new Rect(-size * 0.5f, size), pixelSize = 1f };
            return content.geometry.TryGetAnalytic(frame, out _);
        }

        // ---------------------------------------------------------------------------------------------------
        // Shape page
        // ---------------------------------------------------------------------------------------------------

        static VisualElement ShapePage(Context ctx, Action<bool> refreshHeader)
        {
            var page = new VisualElement();
            var geometry = ctx.content.FindPropertyRelative("geometry");

            // Current shape + picker.
            var head = new VisualElement();
            var current = VxUI.El("vx-current-shape");
            var thumb = new VxShapeThumb(geometry.managedReferenceValue as VectraShapeGeometry);
            current.Add(thumb);
            var name = VxUI.Text((geometry.managedReferenceValue as VectraShapeGeometry)?.DisplayName ?? "None", "vx-card__title");
            current.Add(name);
            head.Add(current);
            head.style.flexDirection = FlexDirection.Row;
            head.style.flexGrow = 1;
            head.style.alignItems = Align.Center;
            var change = VxUI.Text("Change shape", "vx-card__meta");
            head.Add(new VisualElement { style = { flexGrow = 1 } });
            head.Add(change);

            var picker = new VisualElement();
            var parameters = new VisualElement();
            BuildPicker(ctx, picker, geometry, () =>
            {
                var g = geometry.managedReferenceValue as VectraShapeGeometry;
                thumb.SetGeometry(g);
                name.text = g?.DisplayName ?? "None";
                BuildParameters(ctx, parameters, geometry);
                refreshHeader(true);
            });
            page.Add(VxUI.Card("vectra.picker", head, picker, false));

            var paramHeader = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, alignItems = Align.Center } };
            paramHeader.Add(new VxIcon(VxIcon.Kind.Settings, 13));
            paramHeader.Add(VxUI.Text("Parameters", "vx-card__title"));
            page.Add(VxUI.Card("vectra.parameters", paramHeader, parameters));
            BuildParameters(ctx, parameters, geometry);
            return page;
        }

        static void BuildPicker(Context ctx, VisualElement picker, SerializedProperty geometry, Action changed)
        {
            string filter = SessionState.GetString("vectra.picker.filter", "All");
            string search = "";
            var searchField = new ToolbarSearchField();
            searchField.AddToClassList("vx-search");
            searchField.style.width = StyleKeyword.Auto;
            picker.Add(searchField);
            var filters = VxUI.El("vx-filter-row");
            picker.Add(filters);
            var grid = VxUI.El("vx-grid");
            picker.Add(grid);

            void RebuildGrid()
            {
                grid.Clear();
                string currentType = geometry.managedReferenceFullTypename;
                foreach (var s in Shapes())
                {
                    if (filter != "All" && s.category != filter) continue;
                    if (search.Length > 0 && s.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var tile = VxUI.El("vx-tile");
                    bool selected = !string.IsNullOrEmpty(currentType) && currentType.EndsWith(" " + s.type.FullName);
                    tile.EnableInClassList("vx-tile--active", selected);
                    tile.Add(new VxShapeThumb(s.sample));
                    tile.Add(VxUI.Text(s.name, "vx-tile__label"));
                    tile.tooltip = s.name;
                    var type = s.type;
                    tile.RegisterCallback<ClickEvent>(_ =>
                    {
                        ctx.so.Update();
                        float rot = geometry.FindPropertyRelative("rotation")?.floatValue ?? 0f;
                        var g = (VectraShapeGeometry)Activator.CreateInstance(type);
                        g.rotation = rot;
                        geometry.managedReferenceValue = g;
                        ctx.so.ApplyModifiedProperties();
                        RebuildGrid();
                        changed();
                    });
                    grid.Add(tile);
                }

                if (grid.childCount == 0) grid.Add(VxUI.Text("No shapes match the search.", "vx-empty"));
            }

            void RebuildFilters()
            {
                filters.Clear();
                foreach (var f in new[] { "All" }.Concat(s_Categories.Select(c => c.category)))
                {
                    var chip = VxUI.Text(f, "vx-filter");
                    chip.EnableInClassList("vx-filter--active", f == filter);
                    string value = f;
                    chip.RegisterCallback<ClickEvent>(_ =>
                    {
                        filter = value;
                        SessionState.SetString("vectra.picker.filter", value);
                        RebuildFilters();
                        RebuildGrid();
                    });
                    filters.Add(chip);
                }
            }

            searchField.RegisterValueChangedCallback(e =>
            {
                search = e.newValue ?? "";
                RebuildGrid();
            });
            RebuildFilters();
            RebuildGrid();
        }

        static void BuildParameters(Context ctx, VisualElement body, SerializedProperty geometry)
        {
            body.Clear();
            ctx.so.Update();
            var g = geometry.managedReferenceValue as VectraShapeGeometry;
            if (g == null)
            {
                body.Add(VxUI.Text("Pick a shape above.", "vx-empty"));
                return;
            }

            var type = g.GetType();
            if (g is RectangleShape)
            {
                body.Add(VxUI.SectionTitle("Corners"));
                body.Add(new VxCornerRadius(geometry.FindPropertyRelative("cornerRadii")));
                var styleProp = geometry.FindPropertyRelative("cornerStyle");
                body.Add(VxUI.Row("Corner style", VxSegmented.ForEnum(styleProp, new[] { "Round", "Chamfer", "Invert", "Notch", "Smooth" },
                    tooltips: new[] { "Circular arcs", "Straight cut", "Concave arcs", "Square notch", "Continuous curvature (squircle)" })));
                body.Add(VxFields.For(geometry.FindPropertyRelative("smoothing"), type, "Smoothing").VisibleWhen(styleProp, sp => sp.enumValueIndex == (int)CornerStyle.Squircle));
                body.Add(VxUI.SectionTitle("Transform"));
                body.Add(VxFields.For(geometry.FindPropertyRelative("rotation"), type, "Rotation"));
            }
            else
            {
                if (g is PathShape || g is PolylineShape)
                {
                    body.Add(VxUI.Text("Edit points directly in the Scene view: drag points and handles, click the green dots to insert, Ctrl/Cmd+click to delete.", "vx-hint"));
                    body.Add(VxUI.AddButton("Edit points in Scene view", () =>
                    {
                        if (ctx.componentType == typeof(VectraRenderer)) ToolManager.SetActiveTool<VectraRendererTool>();
                        else ToolManager.SetActiveTool<VectraShapeTool>();
                        SceneView.lastActiveSceneView?.Focus();
                    }));
                }

                VxFields.AddChildren(body, geometry, type, "rotation");
                body.Add(VxUI.SectionTitle("Transform"));
                body.Add(VxFields.For(geometry.FindPropertyRelative("rotation"), type, "Rotation"));
            }

            body.Bind(ctx.so);
        }

        // ---------------------------------------------------------------------------------------------------
        // Appearance pages (fills, strokes, effects)
        // ---------------------------------------------------------------------------------------------------

        static VisualElement AppearancePage(Context ctx, string listName)
        {
            var page = new VisualElement();
            var styleProp = ctx.content.FindPropertyRelative("style");
            var stack = new VisualElement();

            // Style row (shared by the three pages).
            var styleRow = VxUI.El("vx-row");
            styleRow.Add(new VxIcon(VxIcon.Kind.Style, 14) { style = { marginRight = 6 } });
            var styleField = new ObjectField { objectType = typeof(VectraStyle), allowSceneObjects = false, style = { flexGrow = 1 } };
            styleField.BindProperty(styleProp);
            styleRow.Add(styleField);
            styleRow.Add(VxUI.Button("Save", () => SaveAsStyle(ctx), "Save fills, strokes and effects as a reusable Style asset"));
            styleRow.Add(VxUI.Button("Detach", () => Detach(ctx), "Copy the style into this object to edit it locally"));
            page.Add(styleRow);
            page.Add(stack);

            void Rebuild()
            {
                stack.Clear();
                ctx.so.Update();
                var style = styleProp.objectReferenceValue as VectraStyle;
                SerializedObject target = ctx.so;
                SerializedProperty list;
                if (style != null)
                {
                    target = new SerializedObject(style);
                    list = target.FindProperty(listName);
                    stack.Add(VxUI.Text($"Editing the shared style \"{style.name}\": every shape that uses it changes.", "vx-hint"));
                    stack.TrackSerializedObjectValue(target, _ => style.NotifyChanged());
                }
                else list = ctx.content.FindPropertyRelative(listName);

                if (listName == "effects") stack.Add(EffectList(target, list));
                else stack.Add(LayerList(target, list, listName == "strokes"));
            }

            styleField.RegisterValueChangedCallback(_ => Rebuild());
            Rebuild();
            return page;
        }

        static VectraShapeContent ContentOf(UnityEngine.Object o) =>
            o is VectraShape s ? s.Content : o is VectraRenderer r ? r.Content : null;

        static void SaveAsStyle(Context ctx)
        {
            var c = ContentOf(ctx.so.targetObject);
            if (c == null) return;
            string path = EditorUtility.SaveFilePanelInProject("Save Vectra Style", "NewVectraStyle", "asset", "Save the appearance as a style");
            if (string.IsNullOrEmpty(path)) return;
            var style = ScriptableObject.CreateInstance<VectraStyle>();
            style.fills = c.ActiveFills.Select(f => f?.Clone()).ToList();
            style.strokes = c.ActiveStrokes.Select(s => s?.Clone()).ToList();
            style.effects = c.ActiveEffects.Select(e => e?.Clone()).ToList();
            AssetDatabase.CreateAsset(style, path);
            AssetDatabase.SaveAssets();
            ctx.so.Update();
            ctx.content.FindPropertyRelative("style").objectReferenceValue = style;
            ctx.so.ApplyModifiedProperties();
        }

        static void Detach(Context ctx)
        {
            foreach (var t in ctx.so.targetObjects)
            {
                Undo.RecordObject(t, "Detach Vectra Style");
                ContentOf(t)?.DetachStyle();
                EditorUtility.SetDirty(t);
            }

            ctx.so.Update();
        }

        /// <summary>Re-creates a list whenever its size changes (add/remove from undo, other inspectors, code).</summary>
        static void WatchSize(VisualElement owner, SerializedObject so, SerializedProperty list, Func<int> lastSize, Action rebuild)
        {
            owner.schedule.Execute(() =>
            {
                if (so.targetObject == null) return;
                so.UpdateIfRequiredOrScript();
                if (list.arraySize != lastSize()) rebuild();
            }).Every(300);
        }

        static VisualElement LayerList(SerializedObject so, SerializedProperty list, bool strokes)
        {
            var root = new VisualElement();
            var items = new VisualElement();
            root.Add(items);
            int size = -1;

            void Rebuild()
            {
                items.Clear();
                so.Update();
                size = list.arraySize;
                if (size == 0)
                    items.Add(VxUI.Text(strokes ? "No strokes yet." : "No fills: the shape is transparent.", "vx-empty"));
                for (int i = 0; i < size; i++) items.Add(LayerCard(so, list, i, strokes, Rebuild));
            }

            root.Add(VxUI.AddButton(strokes ? "Add stroke" : "Add fill", () =>
            {
                so.Update();
                int i = list.arraySize;
                list.InsertArrayElementAtIndex(i);
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("enabled").boolValue = true;
                var paint = e.FindPropertyRelative("paint");
                paint.FindPropertyRelative("type").enumValueIndex = (int)PaintType.Solid;
                paint.FindPropertyRelative("color").colorValue = strokes ? Color.black : Color.white;
                paint.FindPropertyRelative("opacity").floatValue = 1f;
                if (strokes)
                {
                    e.FindPropertyRelative("width").floatValue = 2f;
                    e.FindPropertyRelative("miterLimit").floatValue = 4f;
                    e.FindPropertyRelative("trimEnd").floatValue = 1f;
                }

                so.ApplyModifiedProperties();
                SessionState.SetBool(list.propertyPath + "." + i, true);
                Rebuild();
            }));
            Rebuild();
            WatchSize(root, so, list, () => size, Rebuild);
            return root;
        }

        static VisualElement LayerCard(SerializedObject so, SerializedProperty list, int index, bool stroke, Action rebuild)
        {
            var e = list.GetArrayElementAtIndex(index);
            var enabled = e.FindPropertyRelative("enabled");
            var paint = e.FindPropertyRelative("paint");

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, alignItems = Align.Center } };
            var eye = VxUI.IconButton(enabled.boolValue ? VxIcon.Kind.Eye : VxIcon.Kind.EyeOff, "Show / hide", () =>
            {
                so.Update();
                enabled.boolValue = !enabled.boolValue;
                so.ApplyModifiedProperties();
            });
            header.Add(eye);
            var swatch = new VxSwatch { style = { marginLeft = 4 } };
            header.Add(swatch);
            var title = VxUI.Text("", "vx-card__title");
            header.Add(title);
            var meta = VxUI.Text("", "vx-card__meta");
            header.Add(meta);
            header.Add(VxUI.IconButton(VxIcon.Kind.Up, "Move up", () => Move(so, list, index, -1, rebuild)));
            header.Add(VxUI.IconButton(VxIcon.Kind.Down, "Move down", () => Move(so, list, index, 1, rebuild)));
            header.Add(VxUI.IconButton(VxIcon.Kind.Duplicate, "Duplicate", () =>
            {
                so.Update();
                list.InsertArrayElementAtIndex(index);
                so.ApplyModifiedProperties();
                rebuild();
            }));
            header.Add(VxUI.IconButton(VxIcon.Kind.Trash, "Remove", () =>
            {
                so.Update();
                list.DeleteArrayElementAtIndex(index);
                so.ApplyModifiedProperties();
                rebuild();
            }, true));

            var body = new VisualElement();
            body.Add(new VxPaintEditor(paint));
            if (stroke) StrokeSettings(body, e);
            body.Bind(so);

            var card = VxUI.Card(list.propertyPath + "." + index, header, body, index == 0);

            void Update()
            {
                if (so.targetObject == null) return;
                swatch.SetPaint(paint.boxedValue as VectraPaint);
                var type = (PaintType)paint.FindPropertyRelative("type").enumValueIndex;
                if (stroke)
                {
                    var align = (StrokeAlignment)e.FindPropertyRelative("alignment").enumValueIndex;
                    title.text = $"{e.FindPropertyRelative("width").floatValue:0.##} px · {align}";
                    if (e.FindPropertyRelative("dashed").boolValue) title.text += " · dashed";
                }
                else title.text = VxPaint.NameOf(type);

                meta.text = Mathf.RoundToInt(paint.FindPropertyRelative("opacity").floatValue * 100f) + "%";
                bool on = enabled.boolValue;
                eye.Q<VxIcon>().SetKind(on ? VxIcon.Kind.Eye : VxIcon.Kind.EyeOff);
                card.EnableInClassList("vx-card--disabled", !on);
            }

            card.TrackPropertyValue(e, _ => Update());
            Update();
            return card;
        }

        static void Move(SerializedObject so, SerializedProperty list, int index, int delta, Action rebuild)
        {
            int to = index + delta;
            if (to < 0 || to >= list.arraySize) return;
            so.Update();
            list.MoveArrayElement(index, to);
            so.ApplyModifiedProperties();
            rebuild();
        }

        static void StrokeSettings(VisualElement body, SerializedProperty e)
        {
            var t = typeof(StrokeLayer);
            SerializedProperty P(string n) => e.FindPropertyRelative(n);

            body.Add(VxUI.SectionTitle("Geometry"));
            var width = new Slider(0f, 64f) { showInputField = true };
            width.BindProperty(P("width"));
            body.Add(VxUI.Row("Width", width));
            body.Add(VxUI.Row("Position", VxSegmented.ForEnum(P("alignment"), new[] { "Center", "Inside", "Outside" },
                new[] { VxIcon.Kind.AlignCenter, VxIcon.Kind.AlignInside, VxIcon.Kind.AlignOutside }), "Where the stroke sits relative to the outline"));
            body.Add(VxUI.Row("Caps", VxSegmented.ForEnum(P("cap"), new[] { "Butt", "Round", "Square" },
                new[] { VxIcon.Kind.CapButt, VxIcon.Kind.CapRound, VxIcon.Kind.CapSquare }), "Ends of open paths and dashes"));
            body.Add(VxUI.Row("Joins", VxSegmented.ForEnum(P("join"), new[] { "Miter", "Round", "Bevel" },
                new[] { VxIcon.Kind.JoinMiter, VxIcon.Kind.JoinRound, VxIcon.Kind.JoinBevel }), "Shape of corners"));
            body.Add(VxFields.For(P("miterLimit"), t, "Miter limit").VisibleWhen(P("join"), j => j.enumValueIndex == 0));
            body.Add(VxFields.For(P("behindFill"), t, "Behind fill"));

            body.Add(VxUI.SectionTitle("Dashes"));
            body.Add(VxFields.For(P("dashed"), t, "Dashed"));
            body.Add(VxUI.Row("Pattern", new VxDashPattern(P("dashPattern")), "Dash and gap lengths, repeated").VisibleWhen(P("dashed"), d => d.boolValue));
            body.Add(VxFields.For(P("dashOffset"), t, "Offset").VisibleWhen(P("dashed"), d => d.boolValue));
            body.Add(VxFields.For(P("dashRelative"), t, "Relative lengths").VisibleWhen(P("dashed"), d => d.boolValue));

            body.Add(VxUI.SectionTitle("Trim path"));
            var trim = new MinMaxSlider(P("trimStart").floatValue, P("trimEnd").floatValue, 0f, 1f);
            trim.RegisterValueChangedCallback(ev =>
            {
                e.serializedObject.Update();
                P("trimStart").floatValue = ev.newValue.x;
                P("trimEnd").floatValue = ev.newValue.y;
                e.serializedObject.ApplyModifiedProperties();
            });
            trim.TrackPropertyValue(P("trimEnd"), _ => trim.SetValueWithoutNotify(new Vector2(P("trimStart").floatValue, P("trimEnd").floatValue)));
            trim.TrackPropertyValue(P("trimStart"), _ => trim.SetValueWithoutNotify(new Vector2(P("trimStart").floatValue, P("trimEnd").floatValue)));
            body.Add(VxUI.Row("Range", trim, "Visible part of the path (animate it to draw the stroke)"));
            body.Add(VxFields.For(P("trimOffset"), t, "Offset"));

            body.Add(VxUI.SectionTitle("Width profile"));
            body.Add(VxFields.For(P("useWidthProfile"), t, "Variable width"));
            body.Add(VxFields.For(P("widthProfile"), t, "Profile").VisibleWhen(P("useWidthProfile"), d => d.boolValue));
        }

        static VxIcon.Kind EffectIcon(Type t)
        {
            if (t == typeof(DropShadowEffect)) return VxIcon.Kind.FxShadow;
            if (t == typeof(InnerShadowEffect)) return VxIcon.Kind.FxInnerShadow;
            if (t == typeof(OuterGlowEffect)) return VxIcon.Kind.FxGlow;
            if (t == typeof(InnerGlowEffect)) return VxIcon.Kind.FxInnerGlow;
            if (t == typeof(BevelEffect)) return VxIcon.Kind.FxBevel;
            if (t == typeof(LayerBlurEffect)) return VxIcon.Kind.FxBlur;
            if (t == typeof(GrainEffect)) return VxIcon.Kind.FxGrain;
            if (t == typeof(ColorAdjustEffect)) return VxIcon.Kind.FxAdjust;
            if (t == typeof(FillAmountEffect)) return VxIcon.Kind.FxFillAmount;
            if (t == typeof(DissolveEffect)) return VxIcon.Kind.FxDissolve;
            return VxIcon.Kind.Effects;
        }

        static VisualElement EffectList(SerializedObject so, SerializedProperty list)
        {
            var root = new VisualElement();
            var items = new VisualElement();
            int size = -1;

            void Rebuild()
            {
                items.Clear();
                so.Update();
                size = list.arraySize;
                if (size == 0) items.Add(VxUI.Text("No effects. Add shadows, glows, bevels and more below.", "vx-empty"));
                for (int i = 0; i < size; i++)
                {
                    int index = i;
                    var e = list.GetArrayElementAtIndex(i);
                    var fx = e.managedReferenceValue as VectraEffect;
                    if (fx == null) continue;
                    var header = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, alignItems = Align.Center } };
                    header.Add(new VxIcon(EffectIcon(fx.GetType()), 15) { style = { marginLeft = 2 } });
                    header.Add(VxUI.Text(fx.DisplayName, "vx-card__title"));
                    var sw = new VxSwitch(e.FindPropertyRelative("enabled")) { tooltip = "Enabled", style = { marginRight = 4 } };
                    header.Add(sw);
                    header.Add(VxUI.IconButton(VxIcon.Kind.Up, "Move up", () => Move(so, list, index, -1, Rebuild)));
                    header.Add(VxUI.IconButton(VxIcon.Kind.Down, "Move down", () => Move(so, list, index, 1, Rebuild)));
                    header.Add(VxUI.IconButton(VxIcon.Kind.Trash, "Remove", () =>
                    {
                        so.Update();
                        list.DeleteArrayElementAtIndex(index);
                        so.ApplyModifiedProperties();
                        Rebuild();
                    }, true));
                    var body = new VisualElement();
                    VxFields.AddChildren(body, e, fx.GetType(), "enabled");
                    body.Bind(so);
                    var card = VxUI.Card(list.propertyPath + "." + i, header, body, true);
                    card.EnableInClassList("vx-card--disabled", !fx.enabled);
                    card.TrackPropertyValue(e.FindPropertyRelative("enabled"), p => card.EnableInClassList("vx-card--disabled", !p.boolValue));
                    items.Add(card);
                }
            }

            root.Add(items);
            var menu = VxUI.El("vx-effect-menu");
            menu.style.display = DisplayStyle.None;
            foreach (var t in TypeCache.GetTypesDerivedFrom<VectraEffect>().Where(t => !t.IsAbstract && t.IsPublic && t.GetConstructor(Type.EmptyTypes) != null)
                         .OrderBy(t => (int)EffectIcon(t)))
            {
                var type = t;
                var sample = (VectraEffect)Activator.CreateInstance(t);
                var tile = VxUI.El("vx-effect-tile");
                tile.Add(new VxIcon(EffectIcon(t), 18));
                tile.Add(VxUI.Text(sample.DisplayName, "vx-effect-tile__label"));
                tile.RegisterCallback<ClickEvent>(_ =>
                {
                    so.Update();
                    int i = list.arraySize;
                    list.InsertArrayElementAtIndex(i);
                    list.GetArrayElementAtIndex(i).managedReferenceValue = Activator.CreateInstance(type);
                    so.ApplyModifiedProperties();
                    menu.style.display = DisplayStyle.None;
                    Rebuild();
                });
                menu.Add(tile);
            }

            root.Add(VxUI.AddButton("Add effect", () =>
                menu.style.display = menu.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None));
            root.Add(menu);
            Rebuild();
            WatchSize(root, so, list, () => size, Rebuild);
            return root;
        }

        // ---------------------------------------------------------------------------------------------------
        // Settings
        // ---------------------------------------------------------------------------------------------------

        static VisualElement SettingsPage(Context ctx)
        {
            var page = new VisualElement();
            var render = new VisualElement();
            render.Add(VxUI.Row("Blend", VxSegmented.ForEnum(ctx.content.FindPropertyRelative("blendMode"),
                new[] { "Normal", "Add", "Multiply", "Screen" },
                new[] { VxIcon.Kind.BlendNormal, VxIcon.Kind.BlendAdd, VxIcon.Kind.BlendMultiply, VxIcon.Kind.BlendScreen }),
                "How the whole shape is composited over what is behind it"));
            render.Add(VxUI.Row("Quality", VxSegmented.ForEnum(ctx.content.FindPropertyRelative("quality"), new[] { "Low", "Medium", "High", "Ultra" }),
                "Curve tolerance and distance-field resolution"));
            render.Add(VxFields.For(ctx.content.FindPropertyRelative("forcePath"), typeof(VectraShapeContent), "Force path"));
            render.Add(VxUI.Text("Force path tessellates the outline even for analytic shapes (exact miter joins, dashes on every shape).", "vx-hint"));
            var rh = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, alignItems = Align.Center } };
            rh.Add(new VxIcon(VxIcon.Kind.Effects, 13));
            rh.Add(VxUI.Text("Rendering", "vx-card__title"));
            page.Add(VxUI.Card("vectra.settings.render", rh, render));

            var comp = new VisualElement();
            foreach (var name in ctx.componentFields)
            {
                var p = ctx.so.FindProperty(name);
                if (p != null) comp.Add(VxFields.For(p, ctx.componentType, VxUI.Nice(name)));
            }

            var ch = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, alignItems = Align.Center } };
            ch.Add(new VxIcon(VxIcon.Kind.Settings, 13));
            ch.Add(VxUI.Text(ctx.kind, "vx-card__title"));
            page.Add(VxUI.Card("vectra.settings.component", ch, comp));
            page.Bind(ctx.so);
            return page;
        }
    }

    /// <summary>Editable row of dash/gap lengths.</summary>
    public class VxDashPattern : VisualElement
    {
        readonly SerializedProperty m_List;
        int m_Size = -1;

        public VxDashPattern(SerializedProperty list)
        {
            m_List = list;
            style.flexDirection = FlexDirection.Row;
            style.flexWrap = Wrap.Wrap;
            style.alignItems = Align.Center;
            Rebuild();
            schedule.Execute(() =>
            {
                if (m_List.serializedObject.targetObject == null) return;
                m_List.serializedObject.UpdateIfRequiredOrScript();
                if (m_List.arraySize != m_Size) Rebuild();
            }).Every(300);
        }

        void Rebuild()
        {
            Clear();
            m_List.serializedObject.UpdateIfRequiredOrScript();
            m_Size = m_List.arraySize;
            for (int i = 0; i < m_List.arraySize; i++)
            {
                var f = new FloatField { style = { width = 38, marginRight = 2 }, tooltip = i % 2 == 0 ? "Dash" : "Gap" };
                f.BindProperty(m_List.GetArrayElementAtIndex(i));
                Add(f);
            }

            Add(VxUI.IconButton(VxIcon.Kind.Plus, "Add dash and gap", () =>
            {
                m_List.serializedObject.Update();
                for (int k = 0; k < 2; k++)
                {
                    int i = m_List.arraySize;
                    m_List.InsertArrayElementAtIndex(i);
                    m_List.GetArrayElementAtIndex(i).floatValue = k == 0 ? 6f : 4f;
                }

                m_List.serializedObject.ApplyModifiedProperties();
                Rebuild();
            }));
            if (m_List.arraySize > 0)
                Add(VxUI.IconButton(VxIcon.Kind.Trash, "Remove last pair", () =>
                {
                    m_List.serializedObject.Update();
                    m_List.arraySize = Mathf.Max(0, m_List.arraySize - 2);
                    m_List.serializedObject.ApplyModifiedProperties();
                    Rebuild();
                }, true));
        }
    }

    [CustomEditor(typeof(VectraShape)), CanEditMultipleObjects]
    class VectraShapeEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var shape = (VectraShape)target;
            return VectraContentInspector.Build(new VectraContentInspector.Context
            {
                so = serializedObject,
                content = serializedObject.FindProperty("m_Content"),
                componentType = typeof(VectraShape),
                componentFields = new[] { "m_Color", "m_RaycastTarget", "m_PreciseRaycast", "m_Maskable", "m_WorldPixelSize" },
                kind = "UI Shape",
                stats = () => shape != null ? shape.Stats : default,
                getContent = () => shape != null ? shape.Content : null,
                getSize = () => shape != null ? shape.rectTransform.rect.size : Vector2.zero
            });
        }
    }

    [CustomEditor(typeof(VectraRenderer)), CanEditMultipleObjects]
    class VectraRendererEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var r = (VectraRenderer)target;
            return VectraContentInspector.Build(new VectraContentInspector.Context
            {
                so = serializedObject,
                content = serializedObject.FindProperty("m_Content"),
                componentType = typeof(VectraRenderer),
                componentFields = new[] { "m_Size", "m_Pivot", "m_Color", "m_PixelSize", "m_SortingLayer", "m_SortingOrder", "m_GenerateCollider" },
                kind = "World Shape",
                stats = () => r != null ? r.Stats : default,
                getContent = () => r != null ? r.Content : null,
                getSize = () => r != null ? r.Size : Vector2.zero
            });
        }
    }

    [CustomEditor(typeof(VectraStyle))]
    class VectraStyleEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = VxUI.Root();
            root.Add(VxUI.Text("Shared appearance: every shape that references this style updates when it changes.", "vx-hint"));
            foreach (var name in new[] { "fills", "strokes", "effects" })
            {
                root.Add(VxUI.SectionTitle(name));
                var pf = new PropertyField(serializedObject.FindProperty(name), "");
                root.Add(pf);
            }

            root.TrackSerializedObjectValue(serializedObject, _ => ((VectraStyle)target).NotifyChanged());
            return root;
        }
    }
}
