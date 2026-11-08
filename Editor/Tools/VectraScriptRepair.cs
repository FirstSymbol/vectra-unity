using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>
    /// Before 0.2.2 <c>VectraStyle</c>, <c>VectraBooleanOperand</c> and <c>VectraSvgRenderer</c> shared a script file with
    /// another class, so Unity saved them with an empty script reference and they came back as missing scripts. Unity
    /// writes the class name beside the reference, so this writes the reference back. It runs once per project on load
    /// without asking, and on demand from the menu.
    /// </summary>
    [InitializeOnLoad]
    public static class VectraScriptRepair
    {
        const string DoneKey = "FirstSymbol.Vectra.ScriptRepair.v1.";

        static readonly string[] s_Classes = { "VectraStyle", "VectraBooleanOperand", "VectraSvgRenderer" };

        static readonly Regex s_Identifier = new Regex(@"m_EditorClassIdentifier: FirstSymbol\.Vectra[\w.]*:[\w.]*:(\w+)", RegexOptions.Compiled);
        static readonly Regex s_Key = new Regex(@"^  (\w+):", RegexOptions.Compiled | RegexOptions.Multiline);
        static readonly Regex s_NoIdentifier = new Regex(@"m_EditorClassIdentifier: *\r?$", RegexOptions.Compiled | RegexOptions.Multiline);
        const string Lost = "m_Script: {fileID: 0}";

        static readonly HashSet<string> s_ObjectKeys = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
            "m_Enabled", "m_EditorHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier"
        };

        static VectraScriptRepair()
        {
            EditorApplication.delayCall += RunOnce;
        }

        static string Key => DoneKey + Application.dataPath;

        static void RunOnce()
        {
            if (EditorPrefs.GetBool(Key, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunOnce;
                return;
            }
            EditorPrefs.SetBool(Key, true);
            Repair("Assets", skipDirtyScenes: true, out _);
        }

        /// <summary>The class a lost-script object was, or null: from the identifier Unity wrote, else from the fields of a style.</summary>
        public static string ClassOf(string document)
        {
            Match m = s_Identifier.Match(document);
            if (m.Success)
            {
                string name = m.Groups[1].Value;
                return Array.IndexOf(s_Classes, name) >= 0 ? name : null;
            }
            // An identifier-less style asset: only fills, strokes and effects beside the standard object fields.
            if (!document.Contains("MonoBehaviour:") || !document.Contains("m_GameObject: {fileID: 0}")) return null;
            if (!s_NoIdentifier.IsMatch(document)) return null;
            bool fills = false, strokes = false;
            foreach (Match k in s_Key.Matches(document))
            {
                string key = k.Groups[1].Value;
                if (s_ObjectKeys.Contains(key)) continue;
                if (key == "fills") fills = true;
                else if (key == "strokes") strokes = true;
                else if (key != "effects") return null;
            }
            return fills && strokes ? "VectraStyle" : null;
        }

        /// <summary>The text with each lost reference written back, given a function from a class's name to its script's guid. Pure.</summary>
        public static string Rewrite(string text, Func<string, string> guidOf, out int repaired)
        {
            int count = 0;
            string[] parts = Regex.Split(text, @"(?=^--- !u!)", RegexOptions.Multiline);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (!part.Contains(Lost)) continue;
                string cls = ClassOf(part);
                if (cls == null) continue;
                string guid = guidOf(cls);
                if (string.IsNullOrEmpty(guid)) continue;
                parts[i] = part.Replace(Lost, "m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}");
                count++;
            }
            repaired = count;
            return count == 0 ? text : string.Concat(parts);
        }

        public static string GuidOfClass(string className)
        {
            foreach (string guid in AssetDatabase.FindAssets(className + " t:MonoScript"))
                if (Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)) == className)
                    return guid;
            return null;
        }

        /// <summary>Repairs the files below a folder and returns the project paths it changed.</summary>
        public static List<string> Repair(string folder, bool skipDirtyScenes, out int objects)
        {
            var changed = new List<string>();
            int total = 0;
            var guids = new Dictionary<string, string>();
            string Cached(string name)
            {
                if (!guids.TryGetValue(name, out string guid)) guids[name] = guid = GuidOfClass(name);
                return guid;
            }
            var dirty = new HashSet<string>();
            if (skipDirtyScenes)
                for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                {
                    var scene = EditorSceneManager.GetSceneAt(i);
                    if (scene.isDirty) dirty.Add(scene.path);
                }
            if (!Directory.Exists(folder)) { objects = 0; return changed; }
            foreach (string pattern in new[] { "*.asset", "*.prefab", "*.unity" })
            {
                foreach (string file in Directory.GetFiles(folder, pattern, SearchOption.AllDirectories))
                {
                    string path = file.Replace('\\', '/');
                    if (dirty.Contains(path)) continue;
                    string text;
                    try { text = File.ReadAllText(path); }
                    catch (IOException) { continue; }
                    if (!text.Contains(Lost)) continue;
                    string next = Rewrite(text, Cached, out int repaired);
                    if (repaired == 0) continue;
                    File.WriteAllText(path, next);
                    total += repaired;
                    changed.Add(path);
                    Debug.Log("Vectra: relinked " + repaired + " lost script reference" + (repaired == 1 ? "" : "s") + " in " + path);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
            objects = total;
            return changed;
        }

        [MenuItem("Tools/Vectra/Repair Lost Script References", false, 60)]
        static void Run()
        {
            List<string> changed = Repair("Assets", skipDirtyScenes: true, out int objects);
            Debug.Log(changed.Count == 0
                ? "Vectra: found no lost script reference in Assets."
                : "Vectra: repaired " + objects + " objects in " + changed.Count + " files.");
        }
    }
}
