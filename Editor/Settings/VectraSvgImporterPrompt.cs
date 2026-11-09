using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>
    /// The question asked once per project after Vectra is first loaded: which importer reads .svg files. A floating window, not a dialog, so nothing is blocked;
    /// never in batch mode or when VECTRA_NO_PROMPTS is set.
    /// </summary>
    internal sealed class VectraSvgImporterPrompt : EditorWindow
    {
        internal const string SuppressVariable = "VECTRA_NO_PROMPTS";
        const string LaterKey = "FirstSymbol.Vectra.SvgPromptLater";
        const string VectorGraphicsPackage = "com.unity.vectorgraphics";
        bool m_Chosen;

        /// <summary>The whole rule for showing the question.</summary>
        internal static bool ShouldAsk(bool batchMode, bool suppressed, bool answered, bool vectraImporterOn, bool laterThisSession) =>
            !batchMode && !suppressed && !answered && !vectraImporterOn && !laterThisSession;

        internal static bool VectorGraphicsInstalled() => UnityEditor.PackageManager.PackageInfo.FindForPackageName(VectorGraphicsPackage) != null;

        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                VectraProjectSettings s = VectraProjectSettings.instance;
                bool suppressed = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(SuppressVariable));
                if (ShouldAsk(Application.isBatchMode, suppressed, s.SvgImporterAnswered, s.useVectraSvgImporter, SessionState.GetBool(LaterKey, false)))
                    Open();
            };
        }

        internal static void Open()
        {
            var window = GetWindow<VectraSvgImporterPrompt>(true, "Vectra: which importer for .svg files?", true);
            window.minSize = window.maxSize = new Vector2(470, 300);
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.x + (main.width - 470) / 2, main.y + (main.height - 300) / 3, 470, 300);
            window.ShowUtility();
        }

        internal static int ExistingSvgCount()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("glob:\"*.svg\""))
                if (AssetDatabase.GUIDToAssetPath(guid).EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        internal static int SwitchExisting()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("glob:\"*.svg\""))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase)) continue;
                AssetDatabase.SetImporterOverride<VectraSvgImporter>(path);
                n++;
            }
            Debug.Log($"Vectra: {n} SVG file(s) now use the Vectra importer.");
            return n;
        }

        internal static void Answer(bool useVectra)
        {
            VectraProjectSettings s = VectraProjectSettings.instance;
            s.hideFlags &= ~HideFlags.NotEditable;
            s.useVectraSvgImporter = useVectra;
            s.SvgImporterAnswered = true;
            s.Save();
        }

        void CreateGUI()
        {
            VisualElement root = VxUI.Root();
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;
            rootVisualElement.Add(root);
            root.Add(VxUI.IconHeader(VxIcon.Kind.Document, "Import .svg files with Vectra?", "Asked once for this project. You can change it later in Project Settings > FirstSymbol > Vectra.", out _));

            bool conflict = VectorGraphicsInstalled();
            var body = new VisualElement();
            body.Add(VxUI.Text("Vectra's importer turns an .svg into Vectra shapes: gradients, strokes, effects and text stay editable, and the file draws in a Canvas or in the world, sharp at any size.", "vx-hint"));
            body.Add(VxUI.Text("Unity's importer (the Vector Graphics package) turns an .svg into a sprite or a mesh. Choose it if your project already depends on those sprites.", "vx-hint"));
            if (conflict)
            {
                Label warning = VxUI.Text("The Vector Graphics package (com.unity.vectorgraphics) is installed and imports .svg files as well. If you choose Vectra's importer, the files switch to it and stop being the sprites that package made; Keep Unity's leaves everything as it is. Neither is recommended over the other for you.", "vx-hint");
                warning.style.color = new Color(0.96f, 0.65f, 0.14f);
                body.Add(warning);
            }
            root.Add(VxUI.TitledCard("vectra.prompt", VxIcon.Kind.Settings, "Which importer?", body));

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 6 } };
            VisualElement use = VxUI.Button("Use Vectra's importer", UseVectra, "Switches the project setting, and offers to switch the .svg files that are already there");
            if (!conflict)
            {
                use.style.backgroundColor = new Color(0.3f, 0.55f, 1f);
                use.style.color = Color.white;
            }
            row.Add(use);
            row.Add(VxUI.Button("Keep Unity's importer", () => Finish(false), "Leaves .svg files to Unity"));
            row.Add(VxUI.Button("Ask me later", Later, "Asks again the next time the editor starts"));
            root.Add(row);
            foreach (VisualElement b in row.Children())
            {
                b.style.marginRight = 6;
                b.style.marginBottom = 4;
            }
        }

        void UseVectra()
        {
            Answer(true);
            int existing = ExistingSvgCount();
            if (existing > 0 && EditorUtility.DisplayDialog("Vectra", existing + " .svg file(s) are already in the project. Switch them to Vectra's importer too?", "Switch them", "Leave them")) SwitchExisting();
            m_Chosen = true;
            Close();
        }

        void Finish(bool useVectra)
        {
            Answer(useVectra);
            m_Chosen = true;
            Close();
        }

        void Later()
        {
            SessionState.SetBool(LaterKey, true);
            m_Chosen = true;
            Close();
        }

        void OnDestroy()
        {
            if (!m_Chosen) SessionState.SetBool(LaterKey, true);
        }
    }
}
