using System;
using FirstSymbol.Vectra.TextEngines;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>The pieces of the editor UI that deal with text engines: the settings card, status lines and font fields.</summary>
    static class VectraTextSettingsUI
    {
        static readonly string[] k_ProjectLabels = { "Auto", "Lettera", "TextMesh Pro", "Legacy", "None" };

        static readonly string[] k_ProjectTips =
        {
            "Lettera when it is installed, else TextMesh Pro when a font resolves, else the legacy uGUI Text",
            "Lettera text engine (needs the Lettera package)",
            "TextMesh Pro (needs TMP Essential Resources or a font in the font map)",
            "uGUI Text with the built-in font. Always available, ignores font families",
            "SVG text is not drawn"
        };

        /// <summary>One line telling whether an engine can draw text in this project.</summary>
        internal static string Status(VectraTextEngine engine)
        {
            switch (engine)
            {
                case VectraTextEngine.Lettera:
                    return VectraTextBackends.IsRegistered(engine) ? "Lettera: installed" : "Lettera: not installed";
                case VectraTextEngine.TextMeshPro:
                    if (!VectraTextBackends.IsRegistered(engine)) return "TextMesh Pro: not available";
                    return VectraTextBackends.Get(engine).IsUsable(null, null) ? "TextMesh Pro: default font found" : "TextMesh Pro: no default font (import TMP Essential Resources or map a font)";
                case VectraTextEngine.Legacy:
                    return "Legacy uGUI Text: built in";
                default:
                    return "";
            }
        }

        internal static VisualElement Build(SerializedObject so, VectraProjectSettings settings, Action save)
        {
            var card = new VisualElement();
            var status = new VisualElement();
            var uses = VxUI.Text("", "vx-hint");
            card.Add(VxUI.Row("Text engine", VxSegmented.ForEnum(so.FindProperty("textEngine"), k_ProjectLabels, null, k_ProjectTips, () =>
            {
                save();
                Refresh();
            }), "Which engine draws the text of SVG documents. Components can override it."));

            card.Add(status);
            card.Add(uses);
            card.Add(VxUI.Text("Auto picks Lettera when it is installed, else TextMesh Pro when a font resolves, else the legacy uGUI Text. Projects that used Vectra 0.2 keep TextMesh Pro so their SVG text does not change. Each SVG image can override the choice. Fonts per family: Assets > Create > Vectra > SVG Font Map.", "vx-hint"));

            void Refresh()
            {
                status.Clear();
                var chips = VxUI.El("vx-chips");
                chips.Add(VxUI.Chip(Status(VectraTextEngine.Lettera), VectraTextBackends.IsRegistered(VectraTextEngine.Lettera) ? "accent" : null));
                bool tmp = VectraTextBackends.IsRegistered(VectraTextEngine.TextMeshPro) && VectraTextBackends.Get(VectraTextEngine.TextMeshPro).IsUsable(null, null);
                chips.Add(VxUI.Chip(Status(VectraTextEngine.TextMeshPro), tmp ? "accent" : null));
                chips.Add(VxUI.Chip(Status(VectraTextEngine.Legacy)));
                chips.style.flexDirection = FlexDirection.Row;
                chips.style.flexWrap = Wrap.Wrap;
                status.Add(chips);
                var choice = VectraTextBackends.Resolve(settings.textEngine, null, null, null);
                uses.text = settings.textEngine == VectraTextEngine.None
                    ? "SVG text is not drawn."
                    : "Drawn with: " + choice.Used + (choice.fellBack ? " (" + settings.textEngine + " is not available)" : "") + ".";
            }

            Refresh();
            return VxUI.TitledCard("vectra.settings.text", VxIcon.Kind.Text, "Text", card);
        }

        /// <summary>
        /// An object field for one engine's font: it takes only the types that engine accepts, and is disabled when the
        /// engine is not available in this project.
        /// </summary>
        internal static VisualElement FontField(SerializedProperty p, VectraTextEngine engine, string label, string tooltip)
        {
            var backend = VectraTextBackends.Get(engine);
            var field = new ObjectField
            {
                objectType = backend != null && backend.FontAssetType != null ? backend.FontAssetType : typeof(Object),
                allowSceneObjects = false
            };
            field.BindProperty(p);
            if (backend == null)
            {
                field.SetEnabled(false);
                tooltip = (tooltip ?? "") + " (" + engine + " is not available in this project.)";
            }
            else
            {
                field.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue == null || backend.AcceptsFont(evt.newValue)) return;
                    Debug.LogWarning($"Vectra: {evt.newValue.name} is not a {engine} font.");
                    field.SetValueWithoutNotify(evt.previousValue);
                    p.serializedObject.Update();
                    p.objectReferenceValue = evt.previousValue;
                    p.serializedObject.ApplyModifiedProperties();
                });
            }

            return VxUI.Row(label, field, tooltip);
        }
    }
}
