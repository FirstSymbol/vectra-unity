using System.IO;
using System.Linq;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class FontMapTests
    {
        const string Folder = "Assets/__VectraFontMapTests";
        Material m_A, m_B, m_C, m_D;
        VectraFontMap m_Map;

        [SetUp]
        public void SetUp()
        {
            var shader = Shader.Find("Hidden/InternalErrorShader");
            m_A = new Material(shader) { name = "A" };
            m_B = new Material(shader) { name = "B" };
            m_C = new Material(shader) { name = "C" };
            m_D = new Material(shader) { name = "D" };
            m_Map = ScriptableObject.CreateInstance<VectraFontMap>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in new Object[] { m_A, m_B, m_C, m_D, m_Map })
                if (o != null) Object.DestroyImmediate(o);
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        void Add(string family, Object regular = null, Object bold = null, Object lettera = null, Object letteraBold = null) =>
            m_Map.entries.Add(new VectraFontMap.Entry { family = family, regular = regular, bold = bold, lettera = lettera, letteraBold = letteraBold });

        [Test]
        public void Resolve_ReadsTheFieldsOfTheAskedEngine()
        {
            Add("Inter", regular: m_A, lettera: m_B);
            Assert.AreSame(m_A, m_Map.Resolve(VectraTextEngine.TextMeshPro, "Inter", false, out _));
            Assert.AreSame(m_B, m_Map.Resolve(VectraTextEngine.Lettera, "Inter", false, out _));
            Assert.AreSame(m_A, m_Map.Resolve("Inter", false, out _), "the old overload is the TextMesh Pro one");
        }

        [Test]
        public void Resolve_MatchesFamiliesInTheListOrder_IgnoringCaseAndQuotes()
        {
            Add("Arial", regular: m_A);
            Add("inter", regular: m_B);
            Assert.AreSame(m_B, m_Map.Resolve("'Inter', Arial, sans-serif", false, out _));
            Assert.AreSame(m_A, m_Map.Resolve("Nope, \"ARIAL\"", false, out _));
            Assert.IsNull(m_Map.Resolve("Nope", false, out _));
            Assert.IsNull(m_Map.Resolve(null, false, out _));
        }

        [Test]
        public void Resolve_PrefersAnExactBoldFace_AndSaysSo()
        {
            Add("Inter", regular: m_A, bold: m_B, lettera: m_C, letteraBold: m_D);
            Assert.AreSame(m_B, m_Map.Resolve(VectraTextEngine.TextMeshPro, "Inter", true, out bool exact));
            Assert.IsTrue(exact);
            Assert.AreSame(m_D, m_Map.Resolve(VectraTextEngine.Lettera, "Inter", true, out exact));
            Assert.IsTrue(exact);
            Assert.AreSame(m_A, m_Map.Resolve(VectraTextEngine.TextMeshPro, "Inter", false, out exact));
            Assert.IsFalse(exact);
        }

        [Test]
        public void Resolve_WithoutABoldFace_GivesTheRegularOne_NotExact()
        {
            Add("Inter", regular: m_A, lettera: m_C);
            Assert.AreSame(m_A, m_Map.Resolve(VectraTextEngine.TextMeshPro, "Inter", true, out bool exact));
            Assert.IsFalse(exact);
            Assert.AreSame(m_C, m_Map.Resolve(VectraTextEngine.Lettera, "Inter", true, out exact));
            Assert.IsFalse(exact);
        }

        [Test]
        public void Resolve_FallsBackPerEngine()
        {
            m_Map.fallback = m_A;
            m_Map.letteraFallback = m_B;
            Add("Inter", lettera: m_C); // no TextMesh Pro font for this family
            Assert.AreSame(m_A, m_Map.Resolve(VectraTextEngine.TextMeshPro, "Inter", false, out _));
            Assert.AreSame(m_C, m_Map.Resolve(VectraTextEngine.Lettera, "Inter", false, out _));
            Assert.AreSame(m_B, m_Map.Resolve(VectraTextEngine.Lettera, "Other", false, out _));
        }

        [Test]
        public void HasFonts_LooksOnlyAtTheAskedEngine()
        {
            Assert.IsFalse(m_Map.HasFonts(VectraTextEngine.TextMeshPro));
            Assert.IsFalse(m_Map.HasFonts(VectraTextEngine.Lettera));
            Add("Inter", lettera: m_A);
            Assert.IsFalse(m_Map.HasFonts(VectraTextEngine.TextMeshPro));
            Assert.IsTrue(m_Map.HasFonts(VectraTextEngine.Lettera));
            m_Map.fallback = m_B;
            Assert.IsTrue(m_Map.HasFonts(VectraTextEngine.TextMeshPro));
        }

        [Test]
        public void Validate_ClearsFontsThatDoNotBelongToTheirField()
        {
            using (new RegistryScope(clear: true))
            {
                // fake engines that accept only their own material
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.TextMeshPro, accepts: o => o == m_A));
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.Lettera, accepts: o => o == m_B));
                m_Map.fallback = m_B; // wrong
                m_Map.letteraFallback = m_B; // right
                Add("Inter", regular: m_A, bold: m_B, lettera: m_A, letteraBold: m_B);
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("is not a TextMeshPro font"));
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("is not a TextMeshPro font"));
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("is not a Lettera font"));
                Assert.AreEqual(3, m_Map.Validate());
                Assert.IsNull(m_Map.fallback);
                Assert.AreSame(m_B, m_Map.letteraFallback);
                var e = m_Map.entries[0];
                Assert.AreSame(m_A, e.regular);
                Assert.IsNull(e.bold);
                Assert.IsNull(e.lettera);
                Assert.AreSame(m_B, e.letteraBold);
            }
        }

        [Test]
        public void Validate_KeepsEverythingWhenAnEngineIsNotInstalled()
        {
            using (new RegistryScope(clear: true))
            {
                m_Map.fallback = m_A;
                m_Map.letteraFallback = m_B;
                Add("Inter", regular: m_A, lettera: m_B);
                Assert.AreEqual(0, m_Map.Validate(), "nothing can be checked without the engine, so nothing is thrown away");
                Assert.IsNotNull(m_Map.letteraFallback);
            }
        }

        // --- a font map saved by Vectra 0.2.3 -------------------------------------------------------------

        static string Ref(Object o)
        {
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long file));
            return $"{{fileID: {file}, guid: {guid}, type: 2}}";
        }

        [Test]
        public void AFontMapSavedBy023_LoadsWithItsFontsAndEmptyLetteraFields()
        {
            // OnValidate would clear stand-in fonts for the engines that are installed, so keep them out of the way
            using (new RegistryScope(clear: true))
            {
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
                // two real assets to point at, standing in for TextMesh Pro fonts (the 0.2.3 fields were typed TMP_FontAsset)
                var matA = new Material(Shader.Find("Hidden/InternalErrorShader"));
                var matB = new Material(Shader.Find("Hidden/InternalErrorShader"));
                AssetDatabase.CreateAsset(matA, Folder + "/a.mat");
                AssetDatabase.CreateAsset(matB, Folder + "/b.mat");
                var probe = ScriptableObject.CreateInstance<VectraFontMap>();
                var script = MonoScript.FromScriptableObject(probe);
                Object.DestroyImmediate(probe);
                string scriptGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
                string yaml =
                    "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n" +
                    "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n" +
                    "  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n" +
                    $"  m_Script: {{fileID: 11500000, guid: {scriptGuid}, type: 3}}\n" +
                    "  m_Name: OldMap\n  m_EditorClassIdentifier: \n" +
                    $"  fallback: {Ref(matA)}\n" +
                    "  entries:\n" +
                    "  - family: Inter\n" +
                    $"    regular: {Ref(matA)}\n" +
                    $"    bold: {Ref(matB)}\n" +
                    "  - family: Mono\n" +
                    $"    regular: {Ref(matB)}\n" +
                    "    bold: {fileID: 0}\n";
                File.WriteAllText(Folder + "/OldMap.asset", yaml);
                AssetDatabase.ImportAsset(Folder + "/OldMap.asset");
                var map = AssetDatabase.LoadAssetAtPath<VectraFontMap>(Folder + "/OldMap.asset");
                Assert.IsNotNull(map, "the asset loads as a font map");
                Assert.AreEqual(2, map.entries.Count);
                Assert.AreEqual("Inter", map.entries[0].family);
                Assert.AreEqual(matA, map.fallback);
                Assert.AreEqual(matA, map.entries[0].regular);
                Assert.AreEqual(matB, map.entries[0].bold);
                Assert.IsNull(map.entries[1].bold);
                Assert.IsNull(map.letteraFallback, "the new Lettera fields start empty");
                Assert.IsNull(map.entries[0].lettera);
                Assert.IsNull(map.entries[0].letteraBold);

                // and it behaves as before for TextMesh Pro
                Assert.AreEqual(matB, map.Resolve("Inter, Arial", true, out bool exact));
                Assert.IsTrue(exact);
                Assert.AreEqual(matB, map.Resolve("Mono", false, out _));
                Assert.AreEqual(matA, map.Resolve("Unknown", false, out _), "the fallback");
                Assert.IsNull(map.Resolve(VectraTextEngine.Lettera, "Inter", false, out _));

                // saving it again keeps the old data and adds the new fields
                map.entries[0].lettera = matB;
                EditorUtility.SetDirty(map);
                AssetDatabase.SaveAssets();
                string saved = File.ReadAllText(Folder + "/OldMap.asset");
                StringAssert.Contains("lettera:", saved);
                StringAssert.Contains("regular: " + Ref(matA), saved);
                AssetDatabase.ImportAsset(Folder + "/OldMap.asset", ImportAssetOptions.ForceUpdate);
                var again = AssetDatabase.LoadAssetAtPath<VectraFontMap>(Folder + "/OldMap.asset");
                Assert.AreEqual(matB, again.entries[0].lettera);
                Assert.AreEqual(matA, again.entries[0].regular);
            }
        }
    }
}
