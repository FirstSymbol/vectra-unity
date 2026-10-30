using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>Shows the shared gradient and distance-field atlases, their usage and allocated regions.</summary>
    public class VectraAtlasDebugger : EditorWindow
    {
        readonly List<RectInt> m_Allocs = new List<RectInt>();
        Label m_GradInfo, m_SdfInfo;
        Image m_GradImage, m_SdfImage;
        VisualElement m_SdfOverlay;

        [MenuItem("Window/Vectra/Atlas Debugger", false, 2200)]
        public static void Open() => GetWindow<VectraAtlasDebugger>("Vectra Atlases").minSize = new Vector2(360, 420);

        void CreateGUI()
        {
            var root = VxUI.Root();
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 6;
            rootVisualElement.Add(new ScrollView { contentContainer = { style = { flexGrow = 1 } } });
            rootVisualElement.Q<ScrollView>().Add(root);

            var grad = new VisualElement();
            m_GradInfo = VxUI.Text("", "vx-hint");
            grad.Add(m_GradInfo);
            m_GradImage = new Image { scaleMode = ScaleMode.StretchToFill, style = { height = 120, marginTop = 4 } };
            grad.Add(m_GradImage);
            grad.Add(VxUI.Text("One row per gradient or pattern (freeform gradients use a block of rows).", "vx-hint"));
            root.Add(VxUI.TitledCard("vectra.atlas.grad", VxIcon.Kind.PaintLinear, "Gradient atlas", grad));

            var sdf = new VisualElement();
            m_SdfInfo = VxUI.Text("", "vx-hint");
            sdf.Add(m_SdfInfo);
            var frame = new VisualElement { style = { marginTop = 4, alignSelf = Align.Center } };
            m_SdfImage = new Image { scaleMode = ScaleMode.ScaleToFit, style = { width = 320, height = 320, backgroundColor = new Color(0f, 0f, 0f, 0.4f) } };
            m_SdfOverlay = new VisualElement { pickingMode = PickingMode.Ignore, style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 } };
            m_SdfOverlay.generateVisualContent += DrawAllocations;
            m_SdfImage.Add(m_SdfOverlay);
            frame.Add(m_SdfImage);
            sdf.Add(frame);
            sdf.Add(VxUI.Text("Baked distance fields of path shapes with effects, smooth booleans and soft masks. Analytic shapes need none.", "vx-hint"));
            root.Add(VxUI.TitledCard("vectra.atlas.sdf", VxIcon.Kind.FxBlur, "Distance atlas", sdf));

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 4 } };
            actions.Add(VxUI.Button("Reset atlases", () =>
            {
                VectraSdfAtlas.Reset();
                Refresh();
            }, "Frees every region; shapes re-bake on their next update"));
            root.Add(actions);

            root.schedule.Execute(Refresh).Every(500);
            Refresh();
        }

        void Refresh()
        {
            if (m_GradInfo == null) return;
            var gt = VectraGradientAtlas.Texture;
            m_GradImage.image = gt;
            m_GradInfo.text = gt != null
                ? $"{gt.width} × {gt.height} {gt.format} · {VectraGradientAtlas.UsedRows} of {VectraGradientAtlas.Height} rows used"
                : "Not created yet (no gradients drawn).";

            var st = VectraSdfAtlas.Texture;
            m_SdfImage.image = st;
            VectraSdfAtlas.GetAllocations(m_Allocs);
            long used = m_Allocs.Sum(r => (long)r.width * r.height);
            int size = VectraSdfAtlas.Size;
            m_SdfInfo.text = st != null && size > 0
                ? $"{size} × {size} {st.format} · {m_Allocs.Count} regions · {used * 100.0 / ((long)size * size):0.#}% used · max {VectraSdfAtlas.MaxSize}"
                : "Not created yet (no distance fields baked).";
            m_SdfOverlay.MarkDirtyRepaint();
        }

        void DrawAllocations(MeshGenerationContext mgc)
        {
            int size = VectraSdfAtlas.Size;
            if (size <= 0 || m_Allocs.Count == 0) return;
            var r = m_SdfOverlay.contentRect;
            float s = Mathf.Min(r.width, r.height) / size;
            float ox = (r.width - size * s) * 0.5f, oy = (r.height - size * s) * 0.5f;
            var p = mgc.painter2D;
            p.strokeColor = new Color(0.3f, 0.9f, 1f, 0.9f);
            p.lineWidth = 1f;
            foreach (var a in m_Allocs)
            {
                // Texture rows start at the bottom; the image is drawn with row 0 at the bottom too.
                float x = ox + a.x * s, y = oy + (size - a.yMax) * s;
                p.BeginPath();
                p.MoveTo(new Vector2(x, y));
                p.LineTo(new Vector2(x + a.width * s, y));
                p.LineTo(new Vector2(x + a.width * s, y + a.height * s));
                p.LineTo(new Vector2(x, y + a.height * s));
                p.ClosePath();
                p.Stroke();
            }
        }
    }

    /// <summary>Lists every Vectra SVG document of the project with the features its import could not reproduce.</summary>
    public class VectraSvgReportWindow : EditorWindow
    {
        VisualElement m_List;
        Toggle m_OnlyWarnings;

        [MenuItem("Window/Vectra/SVG Import Report", false, 2201)]
        public static void Open() => GetWindow<VectraSvgReportWindow>("SVG Import Report").minSize = new Vector2(380, 300);

        void CreateGUI()
        {
            var root = VxUI.Root();
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 6;
            var bar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            m_OnlyWarnings = new Toggle("Only with warnings") { value = true };
            m_OnlyWarnings.RegisterValueChangedCallback(_ => Rebuild());
            bar.Add(m_OnlyWarnings);
            bar.Add(new VisualElement { style = { flexGrow = 1 } });
            bar.Add(VxUI.Button("Refresh", Rebuild));
            root.Add(bar);
            var scroll = new ScrollView();
            m_List = new VisualElement();
            scroll.Add(m_List);
            root.Add(scroll);
            rootVisualElement.Add(root);
            Rebuild();
        }

        void OnProjectChange() => Rebuild();

        void Rebuild()
        {
            if (m_List == null) return;
            m_List.Clear();
            int shown = 0, total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(VectraSvgDocument)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var doc in AssetDatabase.LoadAllAssetsAtPath(path).OfType<VectraSvgDocument>())
                {
                    total++;
                    int n = doc.warnings?.Count ?? 0;
                    if (m_OnlyWarnings.value && n == 0) continue;
                    shown++;
                    var body = new VisualElement();
                    if (n == 0) body.Add(VxUI.Text("Imported without warnings.", "vx-hint"));
                    else
                        foreach (var w in doc.warnings)
                            body.Add(VxUI.Text("• " + w, "vx-hint"));
                    var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd } };
                    var target = doc;
                    actions.Add(VxUI.Button("Select", () =>
                    {
                        Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(path);
                        EditorGUIUtility.PingObject(Selection.activeObject);
                    }));
                    body.Add(actions);
                    var badge = VxUI.Chip(n == 0 ? "ok" : n + (n == 1 ? " warning" : " warnings"), n == 0 ? "green" : "orange");
                    m_List.Add(VxUI.TitledCard("vectra.svgreport." + guid + target.name, n == 0 ? VxIcon.Kind.Document : VxIcon.Kind.Warning,
                        path, body, badge, n > 0));
                }
            }

            if (shown == 0)
                m_List.Add(VxUI.Text(total == 0
                    ? "No SVG documents imported with Vectra yet (choose the Vectra importer on an .svg file)."
                    : "Every SVG document imported without warnings.", "vx-empty"));
        }
    }
}
