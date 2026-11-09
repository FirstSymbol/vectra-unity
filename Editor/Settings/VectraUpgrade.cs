using System;
using System.Linq;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>
    /// Keeps the look of SVG text when a project moves from Vectra 0.2 to 0.3. Before 0.3 all SVG text was TextMesh Pro;
    /// from 0.3 the default engine is Auto, which prefers Lettera. A project that already used Vectra gets TextMesh Pro
    /// written into its settings once, with a log line; a new project keeps Auto.
    /// </summary>
    static class VectraUpgrade
    {
        /// <summary>The settings version that knows about text engines.</summary>
        internal const int TextEngineVersion = 1;

        const string SettingsFile = "ProjectSettings/VectraSettings.asset";

        /// <summary>
        /// Whether the project has to be pinned to TextMesh Pro: the settings were saved by a version without text engines
        /// and the project either had its settings file, a font map, or an SVG document with text.
        /// </summary>
        internal static bool ShouldPinTextMeshPro(int storedVersion, bool settingsFileExisted, bool hasFontMaps, bool hasTextDocuments) =>
            storedVersion < TextEngineVersion && (settingsFileExisted || hasFontMaps || hasTextDocuments);

        /// <summary>Applies the upgrade to <paramref name="settings"/>. Returns true when it pinned TextMesh Pro.</summary>
        internal static bool Upgrade(VectraProjectSettings settings, bool settingsFileExisted, Func<bool> hasFontMaps, Func<bool> hasTextDocuments, bool save)
        {
            if (settings.SettingsVersion >= TextEngineVersion) return false;
            bool pin = ShouldPinTextMeshPro(settings.SettingsVersion, settingsFileExisted,
                !settingsFileExisted && hasFontMaps(), !settingsFileExisted && hasTextDocuments());
            if (pin && settings.textEngine == VectraTextEngine.Auto)
            {
                settings.textEngine = VectraTextEngine.TextMeshPro;
                Debug.Log("Vectra 0.3: SVG text is drawn with TextMesh Pro in this project, as before, so it does not change. " +
                          "Set Project Settings > Vectra > Text engine to Auto or Lettera to use Lettera.");
            }
            else pin = false;

            settings.SettingsVersion = TextEngineVersion;
            settings.Apply();
            if (save) settings.Save();
            return pin;
        }

        internal static bool AnyFontMap() => AssetDatabase.FindAssets("t:VectraFontMap").Length > 0;

        internal static bool AnyTextDocument()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:VectraSvgDocument"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var doc in AssetDatabase.LoadAllAssetsAtPath(path).OfType<VectraSvgDocument>())
                    if (doc.elements != null && doc.elements.Any(e => e != null && e.isText))
                        return true;
            }

            return false;
        }

        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            // The asset database is not ready while scripts load.
            EditorApplication.delayCall += () =>
            {
                var s = VectraProjectSettings.instance;
                if (s.SettingsVersion >= TextEngineVersion)
                {
                    s.Apply();
                    return;
                }

                bool existed = System.IO.File.Exists(SettingsFile);
                Upgrade(s, existed, AnyFontMap, AnyTextDocument, true);
            };
        }
    }
}
