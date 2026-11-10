// Builds the harness player (or a size-probe player) from a batch editor.
// Arguments: -perfOut <exe> -perfBackend mono|il2cpp -perfDev 0|1 -perfMode harness|size -perfDeep 0|1
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace FirstSymbol.VectraPerf.Editor
{
    public static class VectraPerfBuild
    {
        [Serializable]
        class BuildInfo
        {
            public string result, backend, mode;
            public bool dev;
            public long totalSize;
            public double totalSeconds;
            public int errors, warnings;
            public List<string> largest = new List<string>();
            public List<string> shaders = new List<string>();
        }

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        public static void Run()
        {
            int code = 1;
            try { code = Build() ? 0 : 1; }
            catch (Exception e) { Debug.LogError("VPERF_BUILD failed: " + e); }
            EditorApplication.Exit(code);
        }

        // Switches the project to the Gamma colour space and quits, so that the next editor session imports everything in it.
        public static void SetGamma()
        {
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            AssetDatabase.SaveAssets();
            Debug.Log("VPERF_GAMMA set " + PlayerSettings.colorSpace);
            EditorApplication.Exit(PlayerSettings.colorSpace == ColorSpace.Gamma ? 0 : 1);
        }

        static bool Build()
        {
            string output = Arg("-perfOut");
            bool il2cpp = Arg("-perfBackend") == "il2cpp";
            bool dev = Arg("-perfDev") == "1";
            string mode = Arg("-perfMode") ?? "harness";
            Directory.CreateDirectory("Assets/Perf");
            string scenePath = "Assets/Perf/PerfScene_" + mode + ".unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (mode == "size") MakeSizeScene();
            EditorSceneManager.SaveScene(scene, scenePath);
            if (mode == "harness") MakeVgMaterials();

            var target = NamedBuildTarget.Standalone;
            PlayerSettings.SetScriptingBackend(target, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);
            // A release player reports GPU time only when asked to at build time.
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12 });
            var options = BuildOptions.None;
            if (dev) options |= BuildOptions.Development;
            if (dev && Arg("-perfDeep") == "1") options |= BuildOptions.EnableDeepProfilingSupport;
            var build = new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                target = BuildTarget.StandaloneWindows64,
                locationPathName = output,
                options = options,
            };
            if (mode == "harness") build.extraScriptingDefines = new[] { "VECTRA_PERF" };
            var report = BuildPipeline.BuildPlayer(build);
            var info = new BuildInfo
            {
                result = report.summary.result.ToString(),
                backend = il2cpp ? "il2cpp" : "mono",
                mode = mode,
                dev = dev,
                totalSize = (long)report.summary.totalSize,
                totalSeconds = report.summary.totalTime.TotalSeconds,
                errors = report.summary.totalErrors,
                warnings = report.summary.totalWarnings,
            };
            foreach (var packed in report.packedAssets)
            foreach (var content in packed.contents)
                info.largest.Add(content.packedSize + "\t" + content.type.Name + "\t" + content.sourceAssetPath);
            info.largest = info.largest.OrderByDescending(s => long.Parse(s.Split('\t')[0])).Take(60).ToList();
            foreach (var file in report.GetFiles())
                if (file.role == "ManagedLibrary" || file.role == "DependentManagedLibrary" || file.path.EndsWith("GameAssembly.dll") || file.path.EndsWith(".dll"))
                    info.shaders.Add(file.size + "\tfile\t" + file.path);
            File.WriteAllText(output + ".buildinfo.json", JsonUtility.ToJson(info, true));
            Debug.Log("VPERF_BUILD " + report.summary.result + " errors=" + report.summary.totalErrors + " size=" + report.summary.totalSize);
            return report.summary.result == BuildResult.Succeeded;
        }

        // A canvas with one Vectra shape (or one Image when the package is absent): the cheapest scene that still uses the package.
        static void MakeSizeScene()
        {
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var child = new GameObject("Item", typeof(RectTransform));
            child.transform.SetParent(canvasGo.transform, false);
            ((RectTransform)child.transform).sizeDelta = new Vector2(100, 100);
            Type shape = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("FirstSymbol.Vectra.VectraShape", false)).FirstOrDefault(t => t != null);
            if (shape != null && Arg("-perfNoVectra") != "1") child.AddComponent(shape);
            else child.AddComponent<Image>();
        }

        // The package's materials, as assets in a Resources folder, so that a player has their shaders.
        static void MakeVgMaterials()
        {
            string folder = "Assets/Perf/Resources";
            Directory.CreateDirectory(folder);
            // The hidden ones are looked up by name by the package's own renderer, and a shader nothing references is left out of a player.
            foreach (var pair in new[]
            {
                ("VgUI", "Unlit/VectorGradientUI"), ("VgUnlit", "Unlit/Vector"), ("VgGradient", "Unlit/VectorGradient"), ("VgUiPlain", "Unlit/VectorUI"),
                ("VgBlendMax", "Hidden/VectorBlendMax"), ("VgDemultiply", "Hidden/VectorDemultiply"), ("VgExpandEdges", "Hidden/VectorExpandEdges"),
            })
            {
                var shader = Shader.Find(pair.Item2);
                string path = folder + "/" + pair.Item1 + ".mat";
                if (shader == null || File.Exists(path)) continue;
                AssetDatabase.CreateAsset(new Material(shader), path);
            }

            AssetDatabase.SaveAssets();
        }
    }
}
