// I1: editor import time of an SVG corpus, with Vectra's importer and (when installed) the default importer of com.unity.vectorgraphics.
// Arguments: -perfCorpus <folder of .svg> -perfOut <json> -perfRounds <n>
// Every round writes the files again with a different comment, so that the asset database cannot reuse an artifact of an earlier round.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FirstSymbol.Vectra.Editor;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FirstSymbol.VectraPerf.Editor
{
    public static class VectraPerfImport
    {
        [Serializable]
        class Sample
        {
            public int round;
            public string file, importer;
            public double ms;
            public bool ok;
            public string note;
        }

        [Serializable]
        class Output
        {
            public string unity;
            public List<Sample> samples = new List<Sample>();
        }

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        public static void Run()
        {
            int code = 0;
            try { Import(); }
            catch (Exception e)
            {
                Debug.LogError("VPERF_IMPORT failed: " + e);
                code = 1;
            }

            EditorApplication.Exit(code);
        }

        static void Import()
        {
            string corpus = Arg("-perfCorpus");
            int rounds = int.Parse(Arg("-perfRounds") ?? "6");
            var output = new Output { unity = Application.unityVersion };
            string folder = "Assets/PerfImport";
            var files = new List<string>(Directory.GetFiles(corpus, "*.svg"));
            files.Sort(StringComparer.Ordinal);
            for (int round = 0; round < rounds; round++)
            {
                AssetDatabase.DeleteAsset(folder);
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
                foreach (string file in files)
                {
                    string name = Path.GetFileName(file);
                    string path = folder + "/" + name;
                    string text = File.ReadAllText(file);
                    int end = text.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
                    text = end > 0 ? text.Insert(end, "<!-- perf round " + round + " -->") : text + "<!-- perf round " + round + " -->";
                    File.WriteAllText(path, text);

                    // The default importer of the project for .svg (com.unity.vectorgraphics when it is installed).
                    var watch = Stopwatch.StartNew();
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    double defaultMs = watch.Elapsed.TotalMilliseconds;
                    var defaultImporter = AssetImporter.GetAtPath(path);
                    output.samples.Add(new Sample
                    {
                        round = round, file = name, importer = "default", ms = defaultMs, ok = defaultImporter != null,
                        note = defaultImporter != null ? defaultImporter.GetType().FullName : "none",
                    });

                    // Vectra: the override, which imports the file again with Vectra's importer (document output).
                    watch.Restart();
                    AssetDatabase.SetImporterOverride<VectraSvgImporter>(path);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    double vectraMs = watch.Elapsed.TotalMilliseconds;
                    bool ok = AssetImporter.GetAtPath(path) is VectraSvgImporter && AssetDatabase.LoadMainAssetAtPath(path) != null;
                    output.samples.Add(new Sample { round = round, file = name, importer = "vectra", ms = vectraMs, ok = ok, note = "document" });

                    // Sprite output: the same file baked into a 512 px sprite (the GPU renders it).
                    var vectra = (VectraSvgImporter)AssetImporter.GetAtPath(path);
                    if (vectra != null)
                    {
                        vectra.mode = VectraSvgImporter.OutputMode.Sprite;
                        watch.Restart();
                        vectra.SaveAndReimport();
                        double spriteMs = watch.Elapsed.TotalMilliseconds;
                        output.samples.Add(new Sample { round = round, file = name, importer = "vectra_sprite", ms = spriteMs, ok = AssetDatabase.LoadMainAssetAtPath(path) != null, note = "sprite 512" });
                    }
                }
            }

            AssetDatabase.DeleteAsset(folder);
            File.WriteAllText(Arg("-perfOut"), JsonUtility.ToJson(output, true));
            Debug.Log("VPERF_IMPORT done samples=" + output.samples.Count);
        }
    }
}
