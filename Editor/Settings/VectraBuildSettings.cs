using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>
    /// Carries the project's Vectra settings into player builds (the atlas size and the text engine). Only a value that
    /// differs from the runtime default needs an asset, so projects that never touched the settings build exactly as before.
    /// </summary>
    sealed class VectraBuildSettings : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        internal const string Folder = "Assets/Vectra Build Temp";
        internal const string AssetPath = Folder + "/VectraRuntimeSettings.asset";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Install();

        public void OnPostprocessBuild(BuildReport report) => Remove();

        /// <summary>Adds the settings asset to the Preloaded Assets when the project needs one. Returns whether it did.</summary>
        internal static bool Install()
        {
            Remove();
            int size = VectraRuntimeSettings.NormalizeAtlasSize(VectraProjectSettings.instance.sdfAtlasMaxSize);
            var engine = VectraProjectSettings.instance.textEngine;
            var blending = VectraProjectSettings.instance.svgColorBlending;
            if (size == VectraRuntimeSettings.DefaultAtlasSize && engine == TextEngines.VectraTextEngine.Auto && blending == SvgColorBlending.Browser) return false;
            var preloaded = new List<Object>(PlayerSettings.GetPreloadedAssets());
            foreach (var o in preloaded)
                if (o is VectraRuntimeSettings) return false; // the project supplies its own

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Vectra Build Temp");
            var asset = VectraRuntimeSettings.Create(size, engine, blending);
            AssetDatabase.CreateAsset(asset, AssetPath);
            preloaded.Add(asset);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            s_SawBuilding = false;
            EditorApplication.update -= WatchBuild;
            EditorApplication.update += WatchBuild;
            return true;
        }

        static bool s_SawBuilding;

        // A failed or cancelled build never reaches the post-process step: clean up once the build is over.
        static void WatchBuild()
        {
            if (BuildPipeline.isBuildingPlayer)
            {
                s_SawBuilding = true;
                return;
            }

            if (!s_SawBuilding) return;
            EditorApplication.update -= WatchBuild;
            Remove();
        }

        /// <summary>Takes the generated asset out of the Preloaded Assets and deletes it.</summary>
        internal static void Remove()
        {
            var preloaded = new List<Object>(PlayerSettings.GetPreloadedAssets());
            int removed = preloaded.RemoveAll(o => o == null || AssetDatabase.GetAssetPath(o) == AssetPath);
            if (removed > 0) PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        // A build that failed never reaches the post-process step.
        [InitializeOnLoadMethod]
        static void CleanLeftovers()
        {
            EditorApplication.delayCall += () =>
            {
                if (BuildPipeline.isBuildingPlayer || !AssetDatabase.IsValidFolder(Folder)) return;
                Remove();
            };
        }
    }
}
