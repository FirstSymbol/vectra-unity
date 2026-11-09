using System.Linq;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>The editor pieces for text engines: the settings card, status lines, font fields and the inspectors.</summary>
    public class TextEditorUiTests
    {
        static string AllText(VisualElement root) =>
            string.Join("|", root.Query<Label>().ToList().Select(l => l.text));

        [Test]
        public void Status_SaysWhatEachEngineHas()
        {
            using (new RegistryScope(clear: true))
            {
                StringAssert.Contains("not installed", VectraTextSettingsUI.Status(VectraTextEngine.Lettera));
                StringAssert.Contains("not available", VectraTextSettingsUI.Status(VectraTextEngine.TextMeshPro));
                StringAssert.Contains("built in", VectraTextSettingsUI.Status(VectraTextEngine.Legacy));
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.Lettera));
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.TextMeshPro, usable: false));
                Assert.AreEqual("Lettera: installed", VectraTextSettingsUI.Status(VectraTextEngine.Lettera));
                StringAssert.Contains("no default font", VectraTextSettingsUI.Status(VectraTextEngine.TextMeshPro));
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.TextMeshPro, usable: true));
                StringAssert.Contains("default font found", VectraTextSettingsUI.Status(VectraTextEngine.TextMeshPro));
            }
        }

        [Test]
        public void TheSettingsCard_ShowsTheChoice_TheStatusAndWhatAutoDraws()
        {
            using (new RegistryScope(clear: true))
            {
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.Lettera));
                var s = VectraProjectSettings.instance;
                var old = s.textEngine;
                try
                {
                    s.textEngine = VectraTextEngine.Auto;
                    var card = VectraTextSettingsUI.Build(new SerializedObject(s), s, () => { });
                    string text = AllText(card);
                    StringAssert.Contains("Text engine", text);
                    StringAssert.Contains("Lettera: installed", text);
                    StringAssert.Contains("Drawn with: Lettera.", text);
                    StringAssert.Contains("Legacy", text);

                    s.textEngine = VectraTextEngine.TextMeshPro; // not registered here
                    text = AllText(VectraTextSettingsUI.Build(new SerializedObject(s), s, () => { }));
                    StringAssert.Contains("Drawn with: Lettera (TextMeshPro is not available).", text);

                    s.textEngine = VectraTextEngine.None;
                    StringAssert.Contains("SVG text is not drawn.", AllText(VectraTextSettingsUI.Build(new SerializedObject(s), s, () => { })));
                }
                finally
                {
                    s.textEngine = old;
                }
            }
        }

        [Test]
        public void AFontField_IsDisabled_WhenItsEngineIsNotInstalled()
        {
            var map = ScriptableObject.CreateInstance<VectraFontMap>();
            try
            {
                using (new RegistryScope(clear: true))
                {
                    var so = new SerializedObject(map);
                    var row = VectraTextSettingsUI.FontField(so.FindProperty("letteraFallback"), VectraTextEngine.Lettera, "Fallback", "tip");
                    Assert.IsFalse(row.Q<ObjectField>().enabledSelf);
                    StringAssert.Contains("not available", row.tooltip);

                    VectraTextBackends.Register(new FakeBackend(VectraTextEngine.Lettera));
                    var row2 = VectraTextSettingsUI.FontField(so.FindProperty("letteraFallback"), VectraTextEngine.Lettera, "Fallback", "tip");
                    Assert.IsTrue(row2.Q<ObjectField>().enabledSelf);
                }
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void TheFontMapInspector_Builds_WithBothEnginesFields()
        {
            var map = ScriptableObject.CreateInstance<VectraFontMap>();
            UnityEditor.Editor editor = null;
            try
            {
                editor = UnityEditor.Editor.CreateEditor(map);
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root);
                string text = AllText(root);
                StringAssert.Contains("Fallback (TMP)", text);
                StringAssert.Contains("Fallback (Lettera)", text);
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void TheSvgImageInspector_Builds_WithTheEngineChoiceAndBothFallbackFonts()
        {
            using (var scope = new ImageScope())
            {
                var img = scope.Image();
                var editor = UnityEditor.Editor.CreateEditor(img);
                try
                {
                    var root = editor.CreateInspectorGUI();
                    Assert.IsNotNull(root);
                    string text = AllText(root);
                    StringAssert.Contains("Text engine", text);
                    StringAssert.Contains("Fallback font (TMP)", text);
                    StringAssert.Contains("Fallback font (Lettera)", text);
                    Assert.IsNotNull(editor.serializedObject.FindProperty("m_TextEngine"));
                    Assert.IsNotNull(editor.serializedObject.FindProperty("m_LetteraFont"));
                }
                finally
                {
                    Object.DestroyImmediate(editor);
                }
            }
        }

        [Test]
        public void TheProjectSettingsAsset_RoundTrips_TheEngineAndTheVersion()
        {
            var s = VectraProjectSettings.instance;
            var engine = s.textEngine;
            var version = s.SettingsVersion;
            try
            {
                s.textEngine = VectraTextEngine.Legacy;
                s.SettingsVersion = 1;
                var so = new SerializedObject(s);
                Assert.AreEqual((int)VectraTextEngine.Legacy, so.FindProperty("textEngine").enumValueIndex);
                Assert.AreEqual(1, so.FindProperty("settingsVersion").intValue);
            }
            finally
            {
                s.textEngine = engine;
                s.SettingsVersion = version;
            }
        }
    }
}
