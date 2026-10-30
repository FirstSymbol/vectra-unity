using System.Linq;
using FirstSymbol.Vectra.URP;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor.URP
{
    [CustomEditor(typeof(VectraBackdrop)), CanEditMultipleObjects]
    class VectraBackdropEditor : UnityEditor.Editor
    {
        static readonly (BackdropMode mode, string label)[] s_Modes =
        {
            (BackdropMode.Overlay, "Overlay"), (BackdropMode.SoftLight, "Soft Light"), (BackdropMode.HardLight, "Hard Light"),
            (BackdropMode.ColorDodge, "Color Dodge"), (BackdropMode.ColorBurn, "Color Burn"), (BackdropMode.Darken, "Darken"),
            (BackdropMode.Lighten, "Lighten"), (BackdropMode.LinearBurn, "Linear Burn"), (BackdropMode.LinearDodge, "Linear Dodge"),
            (BackdropMode.Difference, "Difference"), (BackdropMode.Exclusion, "Exclusion"), (BackdropMode.Hue, "Hue"),
            (BackdropMode.Saturation, "Saturation"), (BackdropMode.Color, "Color"), (BackdropMode.Luminosity, "Luminosity")
        };

        public override VisualElement CreateInspectorGUI()
        {
            var so = serializedObject;
            var root = VxUI.Root();
            var header = VxUI.IconHeader(VxIcon.Kind.Glass, "Backdrop", "Composites the shape with the scene behind it (URP)", out var chips);
            root.Add(header);
            var modeProp = so.FindProperty("m_Mode");

            // Pipeline status.
            var status = new VisualElement();
            void RefreshStatus()
            {
                status.Clear();
                chips.Clear();
                bool ok = FindRenderer(out var data, out bool hasFeature) && hasFeature;
                chips.Add(VxUI.Chip(ok ? "Renderer feature active" : "Renderer feature missing", ok ? "green" : "orange"));
                chips.Add(VxUI.Chip(modeProp.enumValueIndex == 0 ? "Frosted glass" : "Blend: " + (BackdropMode)modeProp.enumValueIndex, "accent"));
                if (ok) return;
                status.Add(VxUI.Text(data == null
                    ? "The active render pipeline is not URP, so backdrop textures are not produced."
                    : $"Add the Vectra Backdrop feature to \"{data.name}\" so the scene behind the shape is captured.", "vx-hint"));
                if (data != null)
                    status.Add(VxUI.AddButton("Add Vectra Backdrop feature", () =>
                    {
                        var f = CreateInstance<VectraBackdropFeature>();
                        f.name = "VectraBackdrop";
                        Undo.RegisterCreatedObjectUndo(f, "Add Vectra Backdrop");
                        AssetDatabase.AddObjectToAsset(f, data);
                        data.rendererFeatures.Add(f);
                        data.SetDirty();
                        EditorUtility.SetDirty(data);
                        AssetDatabase.SaveAssets();
                        RefreshStatus();
                    }));
            }

            root.Add(status);
            root.Add(new VisualElement { style = { height = 6 } });

            // Mode picker: frosted glass or one of the blend modes.
            var modes = new VisualElement();
            var glass = VxUI.El("vx-effect-tile");
            glass.style.width = Length.Percent(100);
            glass.style.flexDirection = FlexDirection.Row;
            glass.Add(new VxIcon(VxIcon.Kind.Glass, 18));
            glass.Add(VxUI.Text("Frosted glass (background blur)", "vx-effect-tile__label"));
            glass.Q<Label>().style.marginLeft = 8;
            modes.Add(glass);
            modes.Add(VxUI.SectionTitle("Blend modes"));
            var grid = VxUI.El("vx-grid");
            var tiles = new System.Collections.Generic.List<(VisualElement tile, BackdropMode mode)>();
            foreach (var (mode, label) in s_Modes)
            {
                var tile = VxUI.El("vx-filter");
                tile.Add(new Label(label));
                tiles.Add((tile, mode));
                grid.Add(tile);
            }

            modes.Add(grid);

            void Select(BackdropMode m)
            {
                so.Update();
                modeProp.enumValueIndex = (int)m;
                so.ApplyModifiedProperties();
                foreach (var t in targets) ((VectraBackdrop)t).Mode = m;
            }

            void RefreshModes()
            {
                int v = modeProp.enumValueIndex;
                glass.style.borderTopColor = glass.style.borderBottomColor = glass.style.borderLeftColor = glass.style.borderRightColor =
                    v == 0 ? new Color(0.3f, 0.55f, 1f) : new Color(0, 0, 0, 0);
                foreach (var (tile, mode) in tiles) tile.EnableInClassList("vx-filter--active", (int)mode == v);
                RefreshStatus();
            }

            glass.RegisterCallback<ClickEvent>(_ => Select(BackdropMode.FrostedGlass));
            foreach (var (tile, mode) in tiles)
            {
                var m = mode;
                tile.RegisterCallback<ClickEvent>(_ => Select(m));
            }

            modes.TrackPropertyValue(modeProp, _ => RefreshModes());
            root.Add(VxUI.TitledCard("vectra.backdrop.mode", VxIcon.Kind.BlendNormal, "Mode", modes));

            var t = typeof(VectraBackdrop);
            var look = new VisualElement();
            look.Add(VxFields.For(so.FindProperty("m_TintStrength"), t, "Tint strength"));
            look.Add(VxFields.For(so.FindProperty("m_Saturation"), t, "Saturation"));
            look.Add(VxFields.For(so.FindProperty("m_Brightness"), t, "Brightness"));
            look.Add(VxUI.Text("The shape's own fill colour tints the blurred background; its outline and effects shape the glass.", "vx-hint"));
            look.VisibleWhen(modeProp, p => p.enumValueIndex == 0);
            root.Add(VxUI.TitledCard("vectra.backdrop.glass", VxIcon.Kind.Glass, "Glass", look));
            root.TrackSerializedObjectValue(so, _ =>
            {
                foreach (var o in targets) ((VectraBackdrop)o).Mode = ((VectraBackdrop)o).Mode;
            });

            RefreshModes();
            root.Bind(so);
            return root;
        }

        static bool FindRenderer(out ScriptableRendererData data, out bool hasFeature)
        {
            data = null;
            hasFeature = false;
            var rp = (QualitySettings.renderPipeline ?? GraphicsSettings.currentRenderPipeline) as UniversalRenderPipelineAsset;
            if (rp == null || rp.rendererDataList.Length == 0) return false;
            data = rp.rendererDataList[0];
            if (data == null) return false;
            hasFeature = data.rendererFeatures.Any(f => f is VectraBackdropFeature);
            return true;
        }
    }
}
