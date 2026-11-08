using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>Each Vectra class lives in a file of its own name, and assets saved before that are relinked.</summary>
    public class ScriptRepairTests
    {
        const string Folder = "Assets/VectraScriptRepairTests";

        const string LostStyle =
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n" +
            "  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n" +
            "  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 0}\n  m_Name: Lost\n" +
            "  m_EditorClassIdentifier: FirstSymbol.Vectra:FirstSymbol.Vectra:VectraStyle\n  fills: []\n  strokes: []\n";

        const string Foreign =
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n" +
            "  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n" +
            "  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 0}\n  m_Name: Other\n" +
            "  m_EditorClassIdentifier: Other.Package:Other.Package:SomeThing\n  fills: []\n  strokes: []\n";

        const string NoIdentifierOtherFields =
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n" +
            "  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n" +
            "  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 0}\n  m_Name: Unknown\n" +
            "  m_EditorClassIdentifier: \n  speed: 3\n";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void EveryVectraBehaviourAndAssetClassIsInAFileOfItsName()
        {
            foreach (var type in new[] { typeof(VectraStyle), typeof(VectraBooleanOperand), typeof(VectraSvgRenderer),
                         typeof(VectraBooleanGroup), typeof(VectraSvgImage), typeof(VectraShape), typeof(VectraRenderer),
                         typeof(VectraPalette), typeof(VectraShapeAsset) })
            {
                var go = new GameObject("t");
                try
                {
                    MonoScript script = null;
                    if (typeof(ScriptableObject).IsAssignableFrom(type))
                    {
                        var so = ScriptableObject.CreateInstance(type);
                        script = MonoScript.FromScriptableObject(so);
                        Object.DestroyImmediate(so);
                    }
                    else
                    {
                        script = MonoScript.FromMonoBehaviour((MonoBehaviour)(object)AddAny(go, type));
                    }
                    Assert.IsNotNull(script, type.Name + " has no script of its own");
                    Assert.AreEqual(type.Name, script.name);
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        static Component AddAny(GameObject go, System.Type type)
        {
            if (typeof(UnityEngine.UI.Graphic).IsAssignableFrom(type)) go.AddComponent<RectTransform>();
            return go.AddComponent(type);
        }

        [Test]
        public void ACreatedStyleSavesWithAScriptReference()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            string path = Folder + "/Made.asset";
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<VectraStyle>(), path);
            AssetDatabase.SaveAssets();
            string text = File.ReadAllText(path);
            Assert.IsFalse(text.Contains("m_Script: {fileID: 0}"), text);
            Assert.IsTrue(text.Contains("m_Script: {fileID: 11500000, guid: "), text);
            Assert.IsNotNull(MonoScript.FromScriptableObject(AssetDatabase.LoadAssetAtPath<VectraStyle>(path)));
        }

        [Test]
        public void RewriteRelinksALostStyleAndLeavesOthersAlone()
        {
            string Guid(string name) => name == "VectraStyle" ? "0123456789abcdef0123456789abcdef" : null;

            string next = VectraScriptRepair.Rewrite(LostStyle, Guid, out int count);
            Assert.AreEqual(1, count);
            StringAssert.Contains("m_Script: {fileID: 11500000, guid: 0123456789abcdef0123456789abcdef, type: 3}", next);
            Assert.IsFalse(next.Contains("{fileID: 0}\n  m_Name"));

            Assert.AreEqual(Foreign, VectraScriptRepair.Rewrite(Foreign, Guid, out count));
            Assert.AreEqual(0, count);
            Assert.AreEqual(NoIdentifierOtherFields, VectraScriptRepair.Rewrite(NoIdentifierOtherFields, Guid, out count));
            Assert.AreEqual(0, count);
        }

        [Test]
        public void RewriteRelinksOnlyTheLostObjectsOfAMultiObjectFile()
        {
            string prefab = "%YAML 1.1\n--- !u!114 &1\nMonoBehaviour:\n  m_GameObject: {fileID: 5}\n  m_Script: {fileID: 0}\n  m_Name: \n" +
                            "  m_EditorClassIdentifier: FirstSymbol.Vectra:FirstSymbol.Vectra:VectraBooleanOperand\n  operation: 0\n" +
                            "--- !u!114 &2\nMonoBehaviour:\n  m_GameObject: {fileID: 5}\n  m_Script: {fileID: 0}\n  m_Name: \n" +
                            "  m_EditorClassIdentifier: Other:Other:Thing\n";
            string next = VectraScriptRepair.Rewrite(prefab, n => "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", out int count);
            Assert.AreEqual(1, count);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(next, "m_Script: \\{fileID: 0\\}").Count);
        }

        [Test]
        public void RepairRelinksTheFileAndKeepsUnrelatedMissingScriptsLost()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/Lost.asset", LostStyle);
            File.WriteAllText(Folder + "/Foreign.asset", Foreign);
            File.WriteAllText(Folder + "/Unknown.asset", NoIdentifierOtherFields);
            AssetDatabase.Refresh();
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var changed = VectraScriptRepair.Repair(Folder, false, out int objects);
                Assert.AreEqual(1, objects);
                Assert.AreEqual(1, changed.Count);
                Assert.AreEqual(Folder + "/Lost.asset", changed[0]);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
            Assert.IsFalse(File.ReadAllText(Folder + "/Lost.asset").Contains("m_Script: {fileID: 0}"));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<VectraStyle>(Folder + "/Lost.asset"));
            Assert.IsTrue(File.ReadAllText(Folder + "/Foreign.asset").Contains("m_Script: {fileID: 0}"));
            Assert.IsTrue(File.ReadAllText(Folder + "/Unknown.asset").Contains("m_Script: {fileID: 0}"));
        }
    }
}
