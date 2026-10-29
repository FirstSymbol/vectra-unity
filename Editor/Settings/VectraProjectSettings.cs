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
                var so = new SerializedObject(s);
                root.style.paddingLeft = 10;
                root.style.paddingTop = 6;
                root.Add(new Label("Vectra Vector Graphics") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 18, marginBottom = 8 } });
                foreach (var name in new[] { "useVectraSvgImporter", "defaultQuality", "sdfAtlasMaxSize" })
                {
                    var field = new UnityEditor.UIElements.PropertyField(so.FindProperty(name));
                    field.Bind(so);
                    field.RegisterValueChangeCallback(_ =>
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        s.Save();
                        s.Apply();
                    });
                    root.Add(field);
                }

                root.Add(new Button(() =>
                {
                    foreach (var guid in AssetDatabase.FindAssets("glob:\"*.svg\""))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (path.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase))
                            AssetDatabase.SetImporterOverride<VectraSvgImporter>(path);
                    }
                }) { text = "Use Vectra importer for all existing SVG files", style = { marginTop = 10, maxWidth = 320 } });

                root.Add(new Button(() =>
                {
                    VectraSdfAtlas.Reset();
                }) { text = "Reset shared atlases", style = { maxWidth = 320 } });
            }
        };
    }
}
