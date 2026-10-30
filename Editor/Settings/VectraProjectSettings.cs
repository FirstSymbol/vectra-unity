using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>Project-wide editor settings of Vectra (stored in ProjectSettings/VectraSettings.asset).</summary>
    [FilePath("ProjectSettings/VectraSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class VectraProjectSettings : ScriptableSingleton<VectraProjectSettings>
    {
        [Tooltip("Import every .svg with Vectra instead of Unity's built-in SVG importer.")]
        public bool useVectraSvgImporter;
        public VectraQuality defaultQuality = VectraQuality.High;
        [Tooltip("Maximum size of the shared signed-distance atlas.")]
        public int sdfAtlasMaxSize = 2048;
        [Tooltip("Font map given to SVG images created from the menu.")]
        public Svg.VectraFontMap defaultFontMap;

        public void Save() => Save(true);

        void OnEnable() => Apply();

        public void Apply() => VectraSdfAtlas.MaxSize = Mathf.ClosestPowerOfTwo(Mathf.Clamp(sdfAtlasMaxSize, 512, 8192));
    }

    static class VectraSettingsProvider
    {
        [SettingsProvider]
        static SettingsProvider Create() => new SettingsProvider("Project/Vectra", SettingsScope.Project)
        {
            label = "Vectra",
            keywords = new HashSet<string> { "Vectra", "SVG", "Vector", "Shape" },
            activateHandler = (search, root) =>
            {
                var s = VectraProjectSettings.instance;
                s.hideFlags &= ~HideFlags.NotEditable;
                var so = new SerializedObject(s);
                var page = VxUI.Root();
                page.style.paddingLeft = 10;
                page.style.paddingRight = 10;
                page.style.paddingTop = 6;
                page.style.maxWidth = 640;
                root.Add(page);
                var header = VxUI.IconHeader(VxIcon.Kind.Shape, "Vectra Vector Graphics", "Project settings", out var chips);
                chips.Add(VxUI.Chip("v0.2.0", "accent"));
                page.Add(header);
                page.Add(new VisualElement { style = { height = 8 } });

                void Save()
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    s.Save();
                    s.Apply();
                }

                var t = typeof(VectraProjectSettings);
                var svg = new VisualElement();
                svg.Add(VxFields.For(so.FindProperty("useVectraSvgImporter"), t, "Use Vectra importer"));
                svg.Add(VxUI.Text("When on, new .svg files use the Vectra importer instead of Unity's built-in one. Existing files can be switched with the button below.", "vx-hint"));
                svg.Add(VxUI.AddButton("Switch every existing SVG to Vectra", () =>
                {
                    int n = 0;
                    foreach (var guid in AssetDatabase.FindAssets("glob:\"*.svg\""))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (!path.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase)) continue;
                        AssetDatabase.SetImporterOverride<VectraSvgImporter>(path);
                        n++;
                    }

                    Debug.Log($"Vectra: {n} SVG file(s) now use the Vectra importer.");
                }));
                svg.Add(VxFields.For(so.FindProperty("defaultFontMap"), t, "Default font map"));
                svg.Add(VxUI.Text("Maps SVG font-family names to TextMeshPro fonts for new SVG images (Assets > Create > Vectra > SVG Font Map).", "vx-hint"));
                page.Add(VxUI.TitledCard("vectra.settings.svg", VxIcon.Kind.Document, "SVG import", svg));

                var render = new VisualElement();
                render.Add(VxUI.Row("Default quality", VxSegmented.ForEnum(so.FindProperty("defaultQuality"), new[] { "Low", "Medium", "High", "Ultra" }, changed: Save),
                    "Quality of shapes created from the GameObject menu"));
                var atlas = new SliderInt(512, 8192) { showInputField = true };
                atlas.BindProperty(so.FindProperty("sdfAtlasMaxSize"));
                render.Add(VxUI.Row("Distance atlas max", atlas, "Largest size of the shared signed-distance atlas (power of two)"));
                render.Add(VxUI.Text("The distance atlas only holds tessellated shapes that use effects. Analytic shapes need no textures.", "vx-hint"));
                render.Add(VxUI.AddButton("Reset shared atlases", () => VectraSdfAtlas.Reset()));
                page.Add(VxUI.TitledCard("vectra.settings.render", VxIcon.Kind.Settings, "Rendering", render));

                page.TrackSerializedObjectValue(so, _ => Save());
                page.Bind(so);
            }
        };
    }
}
