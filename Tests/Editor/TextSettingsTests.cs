using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FirstSymbol.Vectra.Editor.Cli;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>The text engine setting: runtime asset, project settings, the 0.2 to 0.3 upgrade and the CLI.</summary>
    public class TextSettingsTests
    {
        VectraTextEngine m_Engine;
        int m_Version;
        int m_Size;
        RegistryScope m_Scope;

        [SetUp]
        public void SetUp()
        {
            var s = VectraProjectSettings.instance;
            m_Engine = s.textEngine;
            m_Version = s.SettingsVersion;
            m_Size = s.sdfAtlasMaxSize;
            m_Scope = new RegistryScope();
            VectraBuildSettings.Remove();
        }

        [TearDown]
        public void TearDown()
        {
            VectraBuildSettings.Remove();
            var s = VectraProjectSettings.instance;
            s.textEngine = m_Engine;
            s.SettingsVersion = m_Version;
            s.sdfAtlasMaxSize = m_Size;
            m_Scope.Dispose();
            VectraSdfAtlas.MaxSize = VectraRuntimeSettings.NormalizeAtlasSize(m_Size);
        }

        // --- the runtime asset ----------------------------------------------------------------------------

        [Test]
        public void TheRuntimeAsset_CarriesTheTextEngine_AndAppliesItWhenItLoads()
        {
            var a = VectraRuntimeSettings.Create(2048, VectraTextEngine.TextMeshPro);
            try
            {
                Assert.AreEqual(VectraTextEngine.TextMeshPro, a.TextEngine);
                Assert.AreEqual(VectraTextEngine.TextMeshPro, VectraRuntimeSettings.ProjectTextEngine);
                VectraRuntimeSettings.ProjectTextEngine = VectraTextEngine.Auto;
                a.Apply();
                Assert.AreEqual(VectraTextEngine.TextMeshPro, VectraRuntimeSettings.ProjectTextEngine);
                a.TextEngine = VectraTextEngine.Legacy;
                Assert.AreEqual(VectraTextEngine.Legacy, VectraRuntimeSettings.ProjectTextEngine);
            }
            finally
            {
                Object.DestroyImmediate(a);
            }
        }

        [Test]
        public void ANewRuntimeAsset_IsAuto()
        {
            var a = VectraRuntimeSettings.Create(2048);
            try
            {
                Assert.AreEqual(VectraTextEngine.Auto, a.TextEngine);
            }
            finally
            {
                Object.DestroyImmediate(a);
            }
        }

        [Test]
        public void TheProjectSettings_PushTheEngineToTheRuntime()
        {
            var s = VectraProjectSettings.instance;
            s.textEngine = VectraTextEngine.Legacy;
            s.Apply();
            Assert.AreEqual(VectraTextEngine.Legacy, VectraRuntimeSettings.ProjectTextEngine);
            s.textEngine = VectraTextEngine.Auto;
            s.Apply();
            Assert.AreEqual(VectraTextEngine.Auto, VectraRuntimeSettings.ProjectTextEngine);
        }

        [Test]
        public void ABuild_CarriesANonDefaultEngine_EvenWithTheDefaultAtlasSize()
        {
            var s = VectraProjectSettings.instance;
            s.sdfAtlasMaxSize = VectraRuntimeSettings.DefaultAtlasSize;
            s.textEngine = VectraTextEngine.TextMeshPro;
            Assert.IsTrue(VectraBuildSettings.Install());
            var found = PlayerSettings.GetPreloadedAssets().OfType<VectraRuntimeSettings>().Single();
            Assert.AreEqual(VectraTextEngine.TextMeshPro, found.TextEngine);
            VectraBuildSettings.Remove();
            Assert.IsFalse(PlayerSettings.GetPreloadedAssets().OfType<VectraRuntimeSettings>().Any());
        }

        [Test]
        public void ABuild_OfDefaultSettings_AddsNothing()
        {
            var s = VectraProjectSettings.instance;
            s.sdfAtlasMaxSize = VectraRuntimeSettings.DefaultAtlasSize;
            s.textEngine = VectraTextEngine.Auto;
            Assert.IsFalse(VectraBuildSettings.Install());
        }

        // --- the upgrade from 0.2 -------------------------------------------------------------------------

        [TestCase(0, true, false, false, true)] // 0.2 settings file
        [TestCase(0, false, true, false, true)] // no file, but a font map
        [TestCase(0, false, false, true, true)] // no file, but SVG text
        [TestCase(0, false, false, false, false)] // a new project
        [TestCase(1, true, true, true, false)] // already upgraded
        [TestCase(5, true, false, false, false)] // from the future
        public void WhoGetsPinned(int version, bool fileExisted, bool fontMaps, bool textDocs, bool expected) =>
            Assert.AreEqual(expected, VectraUpgrade.ShouldPinTextMeshPro(version, fileExisted, fontMaps, textDocs));

        [Test]
        public void AProjectFrom02_IsPinnedToTextMeshPro_OnceWithALogLine()
        {
            var s = VectraProjectSettings.instance;
            s.textEngine = VectraTextEngine.Auto;
            s.SettingsVersion = 0;
            LogAssert.Expect(LogType.Log, new Regex("Vectra 0.3: SVG text is drawn with TextMesh Pro"));
            Assert.IsTrue(VectraUpgrade.Upgrade(s, true, () => false, () => false, false));
            Assert.AreEqual(VectraTextEngine.TextMeshPro, s.textEngine);
            Assert.AreEqual(VectraUpgrade.TextEngineVersion, s.SettingsVersion);
            Assert.AreEqual(VectraTextEngine.TextMeshPro, VectraRuntimeSettings.ProjectTextEngine, "applied at once");

            // a second time changes nothing and says nothing
            s.textEngine = VectraTextEngine.Auto;
            Assert.IsFalse(VectraUpgrade.Upgrade(s, true, () => false, () => false, false));
            Assert.AreEqual(VectraTextEngine.Auto, s.textEngine, "the user's later choice is left alone");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ANewProject_StaysAuto_WithNoLog()
        {
            var s = VectraProjectSettings.instance;
            s.textEngine = VectraTextEngine.Auto;
            s.SettingsVersion = 0;
            Assert.IsFalse(VectraUpgrade.Upgrade(s, false, () => false, () => false, false));
            Assert.AreEqual(VectraTextEngine.Auto, s.textEngine);
            Assert.AreEqual(VectraUpgrade.TextEngineVersion, s.SettingsVersion, "stamped, so it is not checked again");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AProjectWithoutASettingsFile_ButWithFontMapsOrSvgText_IsPinned()
        {
            foreach (var probe in new[] { (maps: true, docs: false), (maps: false, docs: true) })
            {
                var s = VectraProjectSettings.instance;
                s.textEngine = VectraTextEngine.Auto;
                s.SettingsVersion = 0;
                LogAssert.Expect(LogType.Log, new Regex("TextMesh Pro"));
                Assert.IsTrue(VectraUpgrade.Upgrade(s, false, () => probe.maps, () => probe.docs, false));
                Assert.AreEqual(VectraTextEngine.TextMeshPro, s.textEngine);
            }
        }

        [Test]
        public void AnEngineTheUserAlreadyChose_IsNotOverwritten()
        {
            var s = VectraProjectSettings.instance;
            s.textEngine = VectraTextEngine.Lettera;
            s.SettingsVersion = 0;
            Assert.IsFalse(VectraUpgrade.Upgrade(s, true, () => true, () => true, false));
            Assert.AreEqual(VectraTextEngine.Lettera, s.textEngine);
            Assert.AreEqual(VectraUpgrade.TextEngineVersion, s.SettingsVersion);
        }

        [Test]
        public void TheAssetProbes_RunWithoutErrors()
        {
            Assert.DoesNotThrow(() => VectraUpgrade.AnyFontMap());
            Assert.DoesNotThrow(() => VectraUpgrade.AnyTextDocument());
        }

        // --- the CLI core, without the pipeline package ---------------------------------------------------

        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("project", VectraTextEngineChoice.UseProjectSetting)]
        [TestCase("Auto", VectraTextEngineChoice.Auto)]
        [TestCase("LETTERA", VectraTextEngineChoice.Lettera)]
        [TestCase("textmeshpro", VectraTextEngineChoice.TextMeshPro)]
        [TestCase("TMP", VectraTextEngineChoice.TextMeshPro)]
        [TestCase("text mesh pro", VectraTextEngineChoice.TextMeshPro)]
        [TestCase("legacy", VectraTextEngineChoice.Legacy)]
        [TestCase("none", VectraTextEngineChoice.None)]
        public void Cli_ReadsATextEngineName(string name, VectraTextEngineChoice? expected) =>
            Assert.AreEqual(expected, VectraCli.ParseTextEngine(name));

        [Test]
        public void Cli_RejectsAnUnknownTextEngine() =>
            Assert.Throws<System.ArgumentException>(() => VectraCli.ParseTextEngine("freetype"));

        [Test]
        public void Cli_StatsReportTheEngineInUse()
        {
            VectraProjectSettings.instance.textEngine = VectraTextEngine.Legacy;
            string json = VectraCli.Stats();
            StringAssert.Contains("\"textEngine\":\"Legacy\"", json);
            StringAssert.Contains("\"textEngineUsed\":\"Legacy\"", json);
            StringAssert.Contains("\"shapes\":", json);
        }

        [Test]
        public void Cli_HelpMentionsTheTextEngineOption()
        {
            var help = VectraCli.Help();
            Assert.IsInstanceOf<Dictionary<string, object>>(help);
            var text = MiniJsonText(help);
            StringAssert.Contains("text_engine", text);
        }

        static string MiniJsonText(object o)
        {
            if (o is Dictionary<string, object> d) return string.Join(" ", d.Select(kv => kv.Key + " " + MiniJsonText(kv.Value)));
            if (o is IEnumerable<object> l) return string.Join(" ", l.Select(MiniJsonText));
            return o?.ToString() ?? "";
        }

        [Test]
        public void Cli_SvgParse_CreatesAnImageWithTheRequestedEngine()
        {
            const string folder = "Assets/__VectraCliTests";
            var before = Object.FindObjectsByType<VectraSvgImage>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();
            try
            {
                var result = (Dictionary<string, object>)VectraCli.SvgParse(
                    "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 50'><text x='5' y='20'>cli</text></svg>",
                    folder + "/doc.asset", true, null, "legacy");
                Assert.IsTrue(result.ContainsKey("image"));
                var created = Object.FindObjectsByType<VectraSvgImage>(FindObjectsInactive.Include, FindObjectsSortMode.None).Except(before).Single();
                Assert.AreEqual(VectraTextEngineChoice.Legacy, created.TextEngine);
                Object.DestroyImmediate(created.GetComponentInParent<Canvas>().gameObject);
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void Cli_SvgParse_RejectsABadEngineBeforeCreatingAnything()
        {
            Assert.Throws<System.ArgumentException>(() => VectraCli.SvgParse("<svg xmlns='http://www.w3.org/2000/svg'/>", "Assets/__never.asset", true, null, "nope"));
            Assert.IsNull(AssetDatabase.LoadMainAssetAtPath("Assets/__never.asset"));
        }
    }
}
