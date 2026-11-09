using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>
    /// Lets Vectra see Lettera when Lettera is a folder under Assets. A package announces itself to the assemblies that depend on it (versionDefines); a folder cannot,
    /// so this adds <c>VECTRA_LETTERA</c> to the project's scripting define symbols and takes it away again when the folder goes, before anything is compiled
    /// against a missing assembly. It removes only what it added, and never a symbol the user set.
    /// </summary>
    internal static class VectraAutoDefines
    {
        internal const string Define = "VECTRA_LETTERA";
        const string LetteraAssembly = "FirstSymbol.Lettera";
        const string LetteraFile = "FirstSymbol.Lettera.asmdef";
        internal const string TrackingFile = "ProjectSettings/FirstSymbolAutoDefines.txt";

        internal enum Step { None, Add, Remove, Forget }

        static readonly NamedBuildTarget[] s_Targets =
        {
            NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.WebGL,
            NamedBuildTarget.tvOS, NamedBuildTarget.VisionOS, NamedBuildTarget.WindowsStoreApps,
        };

        /// <summary>What to do for one target. A symbol that is there and that this tool did not add belongs to the user: it is left as it is, even when no longer wanted.</summary>
        internal static Step Plan(bool wanted, bool present, bool tracked)
        {
            if (wanted) return present ? Step.None : Step.Add;
            if (!tracked) return Step.None;
            return present ? Step.Remove : Step.Forget;
        }

        /// <summary>Whether a Lettera that is a folder under Assets should switch the bridge on: its version is 1.0.0 or newer, or unknown.</summary>
        internal static bool Wanted(string asmdefPath, Func<string, string> versionOf)
        {
            if (string.IsNullOrEmpty(asmdefPath) || !asmdefPath.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            string version = versionOf?.Invoke(asmdefPath);
            if (string.IsNullOrEmpty(version)) return true;
            string core = version.Split('-', '+')[0];
            string[] parts = core.Split('.');
            return int.TryParse(parts[0], out int major) && major >= 1;
        }

        static string VersionNear(string asmdefPath)
        {
            string dir = Path.GetDirectoryName(asmdefPath)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(dir))
            {
                string file = Path.Combine(dir, "package.json");
                if (File.Exists(file))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(file), "\"version\"\\s*:\\s*\"([^\"]+)\"");
                    return m.Success ? m.Groups[1].Value : null;
                }
                int slash = dir.LastIndexOf('/');
                if (slash <= 0) break;
                dir = dir.Substring(0, slash);
            }
            return null;
        }

        internal static HashSet<string> ReadTracking(string file = TrackingFile)
        {
            var set = new HashSet<string>();
            if (File.Exists(file))
                foreach (string line in File.ReadAllLines(file))
                    if (line.Trim().Length > 0) set.Add(line.Trim());
            return set;
        }

        /// <summary>Applies the plan to every target, one write per target that changes. Returns the number of targets changed.</summary>
        internal static int Sync(bool wanted)
        {
            HashSet<string> tracked = ReadTracking();
            int changed = 0;
            bool trackingChanged = false;
            foreach (NamedBuildTarget target in s_Targets)
            {
                string key = Define + "@" + target.TargetName;
                try
                {
                    List<string> symbols = PlayerSettings.GetScriptingDefineSymbols(target).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    switch (Plan(wanted, symbols.Contains(Define), tracked.Contains(key)))
                    {
                        case Step.Add:
                            symbols.Add(Define);
                            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
                            trackingChanged |= tracked.Add(key);
                            changed++;
                            break;
                        case Step.Remove:
                            symbols.Remove(Define);
                            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
                            trackingChanged |= tracked.Remove(key);
                            changed++;
                            break;
                        case Step.Forget:
                            trackingChanged |= tracked.Remove(key);
                            break;
                    }
                }
                catch (Exception)
                {
                    // A platform the editor has no module for: nothing to define there.
                }
            }
            if (trackingChanged) File.WriteAllLines(TrackingFile, tracked.OrderBy(k => k, StringComparer.Ordinal));
            if (changed > 0)
                Debug.Log("Vectra: " + (wanted ? "added " : "removed ") + Define + (wanted ? " to" : " from") + " the scripting define symbols (" + changed + " build targets). "
                          + (wanted ? "Lettera is a folder under Assets, so Vectra could not be told by the Package Manager." : "Lettera is gone."));
            return changed;
        }

        static bool IsLettera(string path) => path != null && path.EndsWith("/" + LetteraFile, StringComparison.Ordinal);

        /// <summary>Where Lettera's assembly definition is, leaving out the ones being deleted or moved away.</summary>
        static string FindLettera(string[] imported, string[] gone)
        {
            string path = imported?.FirstOrDefault(IsLettera) ?? CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(LetteraAssembly);
            return path == null || gone != null && gone.Contains(path) ? null : path;
        }

        [InitializeOnLoadMethod]
        static void OnLoad() => EditorApplication.delayCall += () => Sync(Wanted(FindLettera(null, null), VersionNear));

        sealed class Watcher : AssetPostprocessor
        {
            // Runs in the domain that still has the old scripts, before they are compiled again: the define goes before the assembly it gates is built against a missing reference.
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                bool touched = imported.Any(IsLettera) || deleted.Any(IsLettera) || movedFrom.Any(IsLettera);
                if (!touched) return;
                string[] gone = deleted.Concat(movedFrom).ToArray();
                Sync(Wanted(FindLettera(imported.Concat(moved).ToArray(), gone), VersionNear));
            }
        }
    }
}
