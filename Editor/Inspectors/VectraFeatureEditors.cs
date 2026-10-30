using FirstSymbol.Vectra.Svg;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    [CustomEditor(typeof(VectraMask)), CanEditMultipleObjects]
    class VectraMaskEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var so = serializedObject;
            var t = typeof(VectraMask);
            var root = VxUI.Root();
            root.Add(VxUI.IconHeader(VxIcon.Kind.Select, "Vectra Mask", "Masks the children by the outline of this shape", out var chips));
            var mode = so.FindProperty("m_Mode");
            var chip = VxUI.Chip("", "accent");
            chips.Add(chip);
            void UpdateChip() => chip.text = mode.enumValueIndex == 0 ? "Stencil" : "Soft";
            root.TrackPropertyValue(mode, _ => UpdateChip());
            UpdateChip();

            var body = new VisualElement();
            body.Add(VxUI.Row("Mode", VxSegmented.ForEnum(mode, new[] { "Stencil", "Soft" }), "How the children are masked"));
            body.Add(VxFields.For(so.FindProperty("m_ShowMaskGraphic"), typeof(UnityEngine.UI.Mask), "Show mask graphic"));
            var soft = new VisualElement();
            soft.Add(VxFields.For(so.FindProperty("m_Softness"), t, "Softness"));
            soft.Add(VxFields.For(so.FindProperty("m_Inset"), t, "Inset"));
            soft.Add(VxFields.For(so.FindProperty("m_Invert"), t, "Invert"));
            soft.Add(VxUI.Text("Soft masks are evaluated per pixel by the Vectra shader: they apply to Vectra Shape and SVG Image children. Other graphics stay unmasked.", "vx-hint"));
            body.Add(soft.VisibleWhen(mode, p => p.enumValueIndex == 1));
            body.Add(VxUI.Text("The stencil follows the exact outline of the shape (not its rect) and masks every UI graphic below it.", "vx-hint")
                .VisibleWhen(mode, p => p.enumValueIndex == 0));
            root.Add(VxUI.TitledCard("vectra.mask.main", VxIcon.Kind.Settings, "Mask", body));
            root.Bind(so);
            return root;
        }
    }

    [CustomEditor(typeof(VectraTheme)), CanEditMultipleObjects]
    class VectraThemeEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var theme = (VectraTheme)target;
            var so = serializedObject;
            var root = VxUI.Root();
            root.Add(VxUI.IconHeader(VxIcon.Kind.Style, "Vectra Theme", "Activates a palette and selects its theme", out _));
            var body = new VisualElement();
            body.Add(VxFields.For(so.FindProperty("m_Palette"), typeof(VectraTheme), "Palette"));
            var themes = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 4 } };
            body.Add(themes);

            void Rebuild()
            {
                themes.Clear();
                var palette = theme != null ? theme.Palette : null;
                if (palette == null)
                {
                    themes.Add(VxUI.Text("Assign a palette (Assets > Create > Vectra > Palette).", "vx-hint"));
                    return;
                }

                foreach (var name in palette.themes)
                {
                    var n = name;
                    var b = VxUI.Button(n, () =>
                    {
                        Undo.RecordObject(theme, "Switch Theme");
                        theme.SetTheme(n);
                        EditorUtility.SetDirty(theme);
                        Rebuild();
                    }, "Switch to this theme");
                    if (n == theme.Theme) b.AddToClassList("vx-button--active");
                    b.style.unityFontStyleAndWeight = n == theme.Theme ? FontStyle.Bold : FontStyle.Normal;
                    themes.Add(b);
                }
            }

            root.TrackPropertyValue(so.FindProperty("m_Palette"), _ => Rebuild());
            Rebuild();
            body.Add(VxUI.Text("Paints whose colour has a palette token are recoloured when the theme changes. From code: theme.SetTheme(\"Dark\") or VectraPalette.Active.", "vx-hint"));
            root.Add(VxUI.TitledCard("vectra.theme.main", VxIcon.Kind.Style, "Theme", body));
            root.Bind(so);
            return root;
        }
    }

    [CustomEditor(typeof(VectraPalette))]
    class VectraPaletteEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var palette = (VectraPalette)target;
            var so = serializedObject;
            var root = VxUI.Root();
            root.Add(VxUI.IconHeader(VxIcon.Kind.Style, palette.name, "Named colours with one value per theme", out var chips));
            chips.Add(VxUI.Chip(palette == VectraPalette.Active ? "Active" : "Inactive", palette == VectraPalette.Active ? "green" : null));

            var themesBody = new VisualElement();
            themesBody.Add(new PropertyField(so.FindProperty("themes"), "Themes"));
            root.Add(VxUI.TitledCard("vectra.palette.themes", VxIcon.Kind.Settings, "Themes", themesBody));

            var table = new VisualElement();
            var entries = so.FindProperty("entries");
            int size = -1, themeCount = -1;

            void Rebuild()
            {
                table.Clear();
                so.Update();
                size = entries.arraySize;
                themeCount = palette.themes.Count;
                var head = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
                head.Add(new Label("Name") { style = { width = 110, opacity = 0.7f } });
                foreach (var th in palette.themes) head.Add(new Label(th) { style = { flexGrow = 1, flexBasis = 0, opacity = 0.7f } });
                head.Add(new VisualElement { style = { width = 22 } });
                table.Add(head);
                for (int i = 0; i < size; i++)
                {
                    int index = i;
                    var e = entries.GetArrayElementAtIndex(i);
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };
                    var name = new TextField { style = { width = 110 } };
                    name.BindProperty(e.FindPropertyRelative("name"));
                    row.Add(name);
                    var colors = e.FindPropertyRelative("colors");
                    while (colors.arraySize < palette.themes.Count) colors.InsertArrayElementAtIndex(colors.arraySize);
                    for (int k = 0; k < palette.themes.Count; k++)
                    {
                        var cf = new ColorField { hdr = true, style = { flexGrow = 1, flexBasis = 0 } };
                        cf.BindProperty(colors.GetArrayElementAtIndex(k));
                        row.Add(cf);
                    }

                    row.Add(VxUI.IconButton(VxIcon.Kind.Trash, "Remove", () =>
                    {
                        so.Update();
                        entries.DeleteArrayElementAtIndex(index);
                        so.ApplyModifiedProperties();
                        Rebuild();
                    }, true));
                    table.Add(row);
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                table.Bind(so);
            }

            var colorsBody = new VisualElement();
            colorsBody.Add(table);
            colorsBody.Add(VxUI.AddButton("Add colour", () =>
            {
                so.Update();
                int i = entries.arraySize;
                entries.InsertArrayElementAtIndex(i);
                entries.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue = "Colour " + (i + 1);
                so.ApplyModifiedProperties();
                Rebuild();
            }));
            colorsBody.Add(VxUI.Text("Select a name in the Palette row of any fill or stroke to use these colours.", "vx-hint"));
            root.Add(VxUI.TitledCard("vectra.palette.colors", VxIcon.Kind.Fill, "Colours", colorsBody));
            root.TrackSerializedObjectValue(so, _ =>
            {
                if (entries.arraySize != size || palette.themes.Count != themeCount) Rebuild();
                palette.NotifyChanged();
            });
            Rebuild();
            root.Bind(so);
            return root;
        }
    }

    [CustomEditor(typeof(VectraAnimatedProperties)), CanEditMultipleObjects]
    class VectraAnimatedPropertiesEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var so = serializedObject;
            var t = typeof(VectraAnimatedProperties);
            var root = VxUI.Root();
            root.Add(VxUI.IconHeader(VxIcon.Kind.Effects, "Animated Properties", "Keyable shape parameters for Animator and Timeline", out _));

            VisualElement Group(string title, VxIcon.Kind icon, string key, params (string drive, string[] fields)[] items)
            {
                var body = new VisualElement();
                foreach (var (drive, fields) in items)
                {
                    var d = so.FindProperty(drive);
                    body.Add(VxFields.For(d, t, VxUI.Nice(drive)));
                    foreach (var f in fields) body.Add(VxFields.For(so.FindProperty(f), t).VisibleWhen(d, p => p.boolValue));
                }

                return VxUI.TitledCard(key, icon, title, body);
            }

            var idx = new VisualElement();
            idx.Add(VxFields.For(so.FindProperty("fillIndex"), t, "Fill layer"));
            idx.Add(VxFields.For(so.FindProperty("strokeIndex"), t, "Stroke layer"));
            idx.Add(VxUI.Text("Only the parameters with Drive on are written to the shape. Key the values in the Animation window or Timeline.", "vx-hint"));
            var capture = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd } };
            capture.Add(VxUI.Button("Capture from shape", () =>
            {
                foreach (var o in targets)
                {
                    Undo.RecordObject(o, "Capture");
                    ((VectraAnimatedProperties)o).Capture();
                }
            }, "Copy the current values of the shape into the fields"));
            idx.Add(capture);
            root.Add(VxUI.TitledCard("vectra.anim.target", VxIcon.Kind.Settings, "Target layers", idx));
            root.Add(Group("Fill", VxIcon.Kind.Fill, "vectra.anim.fill",
                ("driveFillColor", new[] { "fillColor" }), ("driveFillOpacity", new[] { "fillOpacity" })));
            root.Add(Group("Stroke", VxIcon.Kind.Stroke, "vectra.anim.stroke",
                ("driveStrokeColor", new[] { "strokeColor" }), ("driveStrokeWidth", new[] { "strokeWidth" }),
                ("driveTrim", new[] { "trimStart", "trimEnd", "trimOffset" }), ("driveDashOffset", new[] { "dashOffset" })));
            root.Add(Group("Shape & effects", VxIcon.Kind.Shape, "vectra.anim.shape",
                ("driveRotation", new[] { "rotation" }), ("driveFillAmount", new[] { "fillAmount" }),
                ("driveDissolve", new[] { "dissolve" }), ("driveMorph", new[] { "morph" })));
            root.Bind(so);
            return root;
        }
    }

    [CustomEditor(typeof(VectraFontMap))]
    class VectraFontMapEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var so = serializedObject;
            var root = VxUI.Root();
            root.Add(VxUI.IconHeader(VxIcon.Kind.Text, target.name, "SVG font-family → TextMeshPro font", out _));
            var body = new VisualElement();
            body.Add(VxFields.For(so.FindProperty("fallback"), typeof(VectraFontMap), "Fallback"));
            body.Add(new PropertyField(so.FindProperty("entries"), "Families"));
            body.Add(VxUI.Text("Families are matched case-insensitively against each name of the CSS font-family list, in order.", "vx-hint"));
            root.Add(VxUI.TitledCard("vectra.fontmap.main", VxIcon.Kind.Text, "Fonts", body));
            root.Bind(so);
            return root;
        }
    }

    [CustomEditor(typeof(VectraShapeAsset))]
    class VectraShapeAssetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => VectraContentInspector.BuildShapeAssetEditor(serializedObject, (VectraShapeAsset)target);
    }

    [CustomEditor(typeof(VectraInteractionStates)), CanEditMultipleObjects]
    class VectraInteractionStatesEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var so = serializedObject;
            var t = typeof(VectraInteractionStates);
            var root = VxUI.Root();
            root.Add(VxUI.IconHeader(VxIcon.Kind.Select, "Interaction States", "Hover, press, focus and disabled looks with transitions", out var chips));
            var stateChip = VxUI.Chip(Application.isPlaying ? ((VectraInteractionStates)target).State.ToString() : "Play mode only", "accent");
            chips.Add(stateChip);
            root.schedule.Execute(() =>
            {
                if (target != null && Application.isPlaying) stateChip.text = ((VectraInteractionStates)target).State.ToString();
            }).Every(200);

            var tr = new VisualElement();
            tr.Add(VxFields.For(so.FindProperty("duration"), t, "Duration"));
            tr.Add(VxFields.For(so.FindProperty("ease"), t, "Ease"));
            tr.Add(VxFields.For(so.FindProperty("unscaledTime"), t, "Unscaled time"));
            tr.Add(VxUI.Text("A Selectable on the same object (Button, Toggle…) drives Disabled through its Interactable flag.", "vx-hint"));
            root.Add(VxUI.TitledCard("vectra.states.transition", VxIcon.Kind.Settings, "Transition", tr));

            foreach (var (name, icon) in new[] { ("normal", VxIcon.Kind.Shape), ("hover", VxIcon.Kind.Select), ("pressed", VxIcon.Kind.Down), ("selected", VxIcon.Kind.Link), ("disabled", VxIcon.Kind.EyeOff) })
            {
                var prop = so.FindProperty(name);
                var body = new VisualElement();
                body.Add(VxFields.For(prop.FindPropertyRelative("tint"), typeof(VectraStateLook), "Tint"));
                body.Add(VxFields.For(prop.FindPropertyRelative("scale"), typeof(VectraStateLook), "Scale"));
                var of = prop.FindPropertyRelative("overrideFill");
                body.Add(VxFields.For(of, typeof(VectraStateLook), "Override fill"));
                body.Add(VxFields.For(prop.FindPropertyRelative("fill"), typeof(VectraStateLook), "Fill").VisibleWhen(of, p => p.boolValue));
                var os = prop.FindPropertyRelative("overrideStroke");
                body.Add(VxFields.For(os, typeof(VectraStateLook), "Override stroke"));
                body.Add(VxFields.For(prop.FindPropertyRelative("stroke"), typeof(VectraStateLook), "Stroke").VisibleWhen(os, p => p.boolValue));
                body.Add(VxFields.For(prop.FindPropertyRelative("effectStrength"), typeof(VectraStateLook), "Effect strength"));
                root.Add(VxUI.TitledCard("vectra.states." + name, icon, ObjectNames.NicifyVariableName(name), body, null, name != "normal" && name != "disabled"));
            }

            root.Bind(so);
            return root;
        }
    }}
