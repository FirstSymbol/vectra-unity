using System.Collections.Generic;
using FirstSymbol.Hub;
using FirstSymbol.Vectra.TextEngines;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor.HubPage
{
    /// <summary>How Vectra appears in the FirstSymbol Hub. This assembly compiles only when the hub is installed; Vectra does not need it.</summary>
    internal sealed class VectraHubProduct : HubProduct
    {
        public override string PackageName => "com.firstsymbol.vectra";
        public override string DisplayName => "Vectra";
        public override string Description => "Vector shapes for uGUI and world space: analytic SDF primitives, paths, boolean operations, gradients, strokes, effects, and SVG import and export.";
        public override string AccentHex => "#4c8dff";
        public override string SettingsPath => "Project/FirstSymbol/Vectra";

        public override IEnumerable<HubStatusRow> StatusRows()
        {
            VectraProjectSettings settings = VectraProjectSettings.instance;
            VectraTextEngine engine = settings.textEngine;
            bool usable = engine == VectraTextEngine.Auto || engine == VectraTextEngine.None || VectraTextBackends.IsRegistered(engine);
            yield return new HubStatusRow("Text engine", engine + (usable ? "" : " (not available here)"), usable ? HubTone.Normal : HubTone.Warn);
            yield return new HubStatusRow("Lettera text", VectraTextBackends.IsRegistered(VectraTextEngine.Lettera) ? "installed" : "not installed");
            yield return new HubStatusRow("SVG importer", settings.useVectraSvgImporter ? "Vectra's for every .svg" : "Unity's built-in, Vectra's on request");
            yield return new HubStatusRow("Distance atlas", "up to " + settings.sdfAtlasMaxSize + " px");
        }

        public override void ApplyTheme(VisualElement pageRoot, bool light)
        {
            foreach (VisualElement root in pageRoot.Query<VisualElement>(className: "vx-root").ToList())
                root.EnableInClassList("vx--light", light);
        }
    }
}
