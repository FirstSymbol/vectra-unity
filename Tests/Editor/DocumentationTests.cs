using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace FirstSymbol.Vectra.Editor.Tests
{
    // Keeps the guides true. Every C# block in Documentation~ and the READMEs compiles against the package as it stands, and every
    // English guide has a Russian twin with the same shape (headings, code blocks, tables, links). Blocks go to the C# compiler the
    // editor ships, run apart from the editor, so a block that fails costs no domain reload.
    public class DocumentationTests
    {
        // A block may use these names without declaring them, as a guide's prose does: "shape" is a shape the reader already has.
        const string Context = @"using UnityEngine;
using FirstSymbol.Vectra;
using FirstSymbol.Vectra.Svg;

namespace VectraDocs
{
    public abstract class DocContext : MonoBehaviour
    {
        protected VectraShape shape;
        protected VectraRenderer worldShape;
        protected VectraTheme theme;
        protected VectraPalette palette;
        protected VectraSvgImage svgImage;
        protected VectraSvgRenderer worldSvg;
        protected VectraSvgDocument document;
        protected string svgText, url, path;
        protected Camera cam;
        protected void Next() { }
    }
}
";

        const string Usings =
            "using System;\nusing System.Collections.Generic;\nusing UnityEngine;\nusing FirstSymbol.Vectra;\nusing FirstSymbol.Vectra.Svg;\n" +
            "using FirstSymbol.Vectra.Geometry;\nusing FirstSymbol.Vectra.Rendering;\nusing FirstSymbol.Vectra.TextEngines;\n";

        const string NoCompile = "<!-- no-compile -->";
        // Fragments by design: a member shown out of its class, a block that needs a package this project may lack. Keep them few.
        const int MostSkipped = 4;

        static readonly Regex s_TypeDeclaration =
            new Regex(@"^(\[[^\]]*\]\s*)*((public|internal|sealed|static|abstract|partial)\s+)*(class|struct|interface|enum|record)\s+\w", RegexOptions.Compiled);

        static readonly Regex s_Error = new Regex(@"^(?<file>.*?)\((?<line>\d+),(?<col>\d+)\): error (?<code>CS\d+): (?<message>.*)$", RegexOptions.Compiled);

        struct Block
        {
            public string Doc;
            public int Line;
            public string Code;
        }

        static string PackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VectraShape).Assembly);
            if (info == null) Assert.Ignore("Vectra is a folder under Assets in this project: the documentation tests read the package folder");
            return info.resolvedPath;
        }

        /// <summary>The README files and every guide.</summary>
        static List<string> Docs(string root)
        {
            var docs = Directory.GetFiles(root, "README*.md").ToList();
            string folder = Path.Combine(root, "Documentation~");
            if (Directory.Exists(folder)) docs.AddRange(Directory.GetFiles(folder, "*.md"));
            docs.Sort(StringComparer.Ordinal);
            return docs;
        }

        static List<Block> Blocks(string doc, ref int skipped)
        {
            var blocks = new List<Block>();
            string[] lines = File.ReadAllLines(doc);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "```csharp") continue;
                bool skip = false;
                for (int p = i - 1; p >= 0; p--)
                {
                    if (lines[p].Trim().Length == 0) continue;
                    skip = lines[p].Trim() == NoCompile;
                    break;
                }

                int start = i + 1, end = start;
                while (end < lines.Length && lines[end].Trim() != "```") end++;
                Assert.Less(end, lines.Length, $"{Path.GetFileName(doc)}:{i + 1}: a csharp block never closes");
                if (skip) skipped++;
                else blocks.Add(new Block { Doc = doc, Line = start + 1, Code = string.Join("\n", lines, start, end - start) });
                i = end;
            }

            return blocks;
        }

        // A block of statements becomes a method's body; a block declaring types stands in a namespace of its own. #line sends every error
        // back to the guide's own line.
        static string Wrap(Block block, string name)
        {
            var head = new StringBuilder(Usings);
            var body = new List<string>();
            int bodyLine = block.Line;
            bool inHead = true;
            foreach (string line in block.Code.Split('\n'))
            {
                string t = line.Trim();
                if (inHead && t.StartsWith("using ") && t.EndsWith(";") && !t.StartsWith("using var") && !t.Contains("("))
                {
                    head.Append(t).Append('\n');
                    bodyLine++;
                    continue;
                }

                if (inHead && t.Length == 0)
                {
                    bodyLine++;
                    continue;
                }

                inHead = false;
                body.Add(line);
            }

            bool declares = body.Any(l => s_TypeDeclaration.IsMatch(l));
            string file = block.Doc.Replace('\\', '/');
            var s = new StringBuilder(head.ToString());
            s.Append("namespace VectraDocs.").Append(name).Append("\n{\n");
            if (!declares)
                s.Append("class Stub : VectraDocs.DocContext\n{\n").Append(block.Code.Contains("await ") ? "async void Run()\n" : "void Run()\n").Append("{\n");
            s.Append("#line ").Append(bodyLine).Append(" \"").Append(file).Append("\"\n");
            foreach (string line in body) s.Append(line).Append('\n');
            s.Append("#line default\n");
            if (!declares) s.Append("}\n}\n");
            s.Append("}\n");
            return s.ToString();
        }

        static List<string> References()
        {
            string data = EditorApplication.applicationContentsPath;
            string api = Path.Combine(data, "UnityReferenceAssemblies", "unity-4.8-api");
            var refs = new List<string>();
            foreach (string name in new[] { "mscorlib", "System", "System.Core", "System.Xml", "System.Xml.Linq", "System.Numerics", "System.Runtime.Serialization", "System.Net.Http", "System.Data", "System.Drawing", "System.IO.Compression", "Microsoft.CSharp" })
                refs.Add(Path.Combine(api, name + ".dll"));
            refs.AddRange(Directory.GetFiles(Path.Combine(api, "Facades"), "*.dll"));
            string modules = Path.Combine(data, "Managed", "UnityEngine");
            refs.AddRange(Directory.GetFiles(modules, "UnityEngine*.dll"));
            refs.AddRange(Directory.GetFiles(modules, "UnityEditor*.dll"));
            string scripts = Path.GetFullPath("Library/ScriptAssemblies");
            refs.AddRange(Directory.GetFiles(scripts, "*.dll").Where(f =>
            {
                string n = Path.GetFileNameWithoutExtension(f);
                return !n.EndsWith(".Tests") && !n.StartsWith("Assembly-CSharp");
            }));
            return refs.Where(File.Exists).ToList();
        }

        // What the compiler said, by the file each error names. Runs the editor's own Roslyn on the .NET it ships with.
        static (Dictionary<string, List<string>> errors, string output) Compile(IList<(string name, string source)> files)
        {
            string data = EditorApplication.applicationContentsPath;
            string dotnet = Path.Combine(data, "NetCoreRuntime", UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet");
            string csc = Path.Combine(data, "DotNetSdkRoslyn", "csc.dll");
            Assert.IsTrue(File.Exists(dotnet) && File.Exists(csc), "The editor's C# compiler was not found at " + csc);
            string dir = Path.GetFullPath("Temp/VectraDocBlocks");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var rsp = new StringBuilder();
            rsp.Append("-target:library\n-nostdlib+\n-langversion:9.0\n-preferreduilang:en-US\n-nologo\n-warn:0\n-unsafe\n");
            // The editor's own symbols, so that a block under #if UNITY_EDITOR is compiled and not skipped.
            rsp.Append("-define:").Append(string.Join(";", EditorUserBuildSettings.activeScriptCompilationDefines)).Append('\n');
            rsp.Append("-out:\"").Append(Path.Combine(dir, "VectraDocBlocks.dll")).Append("\"\n");
            foreach (string r in References()) rsp.Append("-r:\"").Append(r).Append("\"\n");
            foreach (var (name, source) in files)
            {
                string p = Path.Combine(dir, name + ".cs");
                File.WriteAllText(p, source);
                rsp.Append('"').Append(p).Append("\"\n");
            }

            string rspPath = Path.Combine(dir, "blocks.rsp");
            File.WriteAllText(rspPath, rsp.ToString());
            var info = new ProcessStartInfo(dotnet, $"\"{csc}\" -noconfig @\"{rspPath}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            info.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            string output;
            using (Process process = Process.Start(info))
            {
                var stderr = process.StandardError.ReadToEndAsync();
                output = process.StandardOutput.ReadToEnd();
                Assert.IsTrue(process.WaitForExit(240000), "The compiler did not finish in four minutes");
                output += stderr.Result;
            }

            var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in output.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                Match m = s_Error.Match(line);
                string key = m.Success ? m.Groups["file"].Value.Replace('\\', '/') : line.Contains(": error ") ? "" : null;
                if (key == null) continue;
                if (!errors.TryGetValue(key, out List<string> list)) errors[key] = list = new List<string>();
                list.Add(line);
            }

            return (errors, output);
        }

        [Test]
        public void EveryCSharpBlockCompiles()
        {
            string root = PackageRoot();
            int skipped = 0;
            var blocks = new List<Block>();
            foreach (string doc in Docs(root)) blocks.AddRange(Blocks(doc, ref skipped));
            Assert.Greater(blocks.Count, 0, "No csharp block was found under " + root);
            Assert.LessOrEqual(skipped, MostSkipped, $"{skipped} blocks are marked {NoCompile}; keep fragments few");

            // The route is proved each run: a block that must fail does, and one that must pass does.
            var files = new List<(string, string)>
            {
                ("Context", Context),
                ("CanaryGood", Wrap(new Block { Doc = "canary-good", Line = 1, Code = "shape.SetShapeDirty();" }, "CanaryGood")),
                ("CanaryBad", Wrap(new Block { Doc = "canary-bad", Line = 1, Code = "shape.SetShapeDirty(5);\nshape.NoSuchMember();" }, "CanaryBad"))
            };
            for (int i = 0; i < blocks.Count; i++) files.Add(("Block" + i, Wrap(blocks[i], "Block" + i)));
            var (errors, output) = Compile(files);

            Assert.IsFalse(errors.ContainsKey(""), "The compiler failed outside any block:\n" + output);
            Assert.IsTrue(errors.Keys.Any(k => k.EndsWith("canary-bad")), "A block that cannot compile went through, so nothing was really compiled:\n" + output);
            Assert.IsFalse(errors.Keys.Any(k => k.EndsWith("canary-good")), "A block that must compile failed, so the route itself is broken:\n" + output);

            var failures = errors.Where(e => !e.Key.EndsWith("canary-bad") && !e.Key.EndsWith("canary-good")).SelectMany(e => e.Value).ToList();
            TestContext.WriteLine($"{blocks.Count} blocks compiled from {Docs(root).Count} files, {skipped} marked {NoCompile}");
            Assert.IsEmpty(failures, "Blocks that do not compile:\n" + string.Join("\n", failures));
        }

        // --- English and Russian twins ---------------------------------------------------------------------

        /// <summary>The shape of a guide: what has to match between its two languages.</summary>
        struct Shape
        {
            public List<int> HeadingLevels;
            public List<string> CodeLanguages;
            public int Tables;
            public List<string> Links;
        }

        static readonly Regex s_Link = new Regex(@"\]\((?<target>[^)\s]+)\)", RegexOptions.Compiled);

        static Shape ShapeOf(string path)
        {
            var shape = new Shape { HeadingLevels = new List<int>(), CodeLanguages = new List<string>(), Links = new List<string>() };
            bool inCode = false, inTable = false;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.TrimEnd();
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("```"))
                {
                    if (!inCode) shape.CodeLanguages.Add(trimmed.Substring(3).Trim());
                    inCode = !inCode;
                    continue;
                }

                if (inCode) continue;
                if (trimmed.StartsWith("#"))
                {
                    int level = 0;
                    while (level < trimmed.Length && trimmed[level] == '#') level++;
                    if (level < trimmed.Length && trimmed[level] == ' ') shape.HeadingLevels.Add(level);
                }

                bool row = trimmed.StartsWith("|");
                if (row && !inTable) shape.Tables++;
                inTable = row;
                foreach (Match m in s_Link.Matches(line))
                {
                    string target = m.Groups["target"].Value;
                    if (target.StartsWith("http") || target.StartsWith("mailto:")) continue;
                    // In-page anchors differ between languages; files are compared without the language suffix.
                    int hash = target.IndexOf('#');
                    if (hash == 0) continue;
                    if (hash > 0) target = target.Substring(0, hash);
                    shape.Links.Add(target.Replace(".ru.md", ".md"));
                }
            }

            shape.Links.Sort(StringComparer.Ordinal);
            return shape;
        }

        /// <summary>(English, Russian) file pairs: README.md with README.ru.md, and X.md with X.ru.md under Documentation~.</summary>
        static List<(string en, string ru)> Pairs(string root)
        {
            var pairs = new List<(string, string)>();
            foreach (string folder in new[] { root, Path.Combine(root, "Documentation~") })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string ru in Directory.GetFiles(folder, "*.ru.md"))
                {
                    string en = ru.Substring(0, ru.Length - ".ru.md".Length) + ".md";
                    pairs.Add((en, ru));
                }
            }

            return pairs;
        }

        [Test]
        public void EveryRussianGuideHasAnEnglishTwin_AndTheOtherWayRound()
        {
            string root = PackageRoot();
            var missing = new List<string>();
            foreach (var (en, ru) in Pairs(root))
                if (!File.Exists(en)) missing.Add("no English file for " + Path.GetFileName(ru));
            foreach (string doc in Docs(root))
            {
                string name = Path.GetFileName(doc);
                if (name.EndsWith(".ru.md") || name == "AgentGuide.md") continue; // the agent guide is one file for tools, in English
                string twin = doc.Substring(0, doc.Length - ".md".Length) + ".ru.md";
                if (!File.Exists(twin)) missing.Add("no Russian file for " + name);
            }

            Assert.IsEmpty(missing, string.Join("\n", missing));
            Assert.IsTrue(File.Exists(Path.Combine(root, "README.md")) && File.Exists(Path.Combine(root, "README.ru.md")), "both READMEs must exist");
            Assert.GreaterOrEqual(Pairs(root).Count, 3, "README, the guide and the platforms page");
        }

        [Test]
        public void EnglishAndRussianGuides_HaveTheSameShape()
        {
            var problems = new List<string>();
            foreach (var (en, ru) in Pairs(PackageRoot()))
            {
                if (!File.Exists(en)) continue;
                var a = ShapeOf(en);
                var b = ShapeOf(ru);
                string name = Path.GetFileName(en);
                if (!a.HeadingLevels.SequenceEqual(b.HeadingLevels))
                    problems.Add($"{name}: headings differ (English {a.HeadingLevels.Count}: {string.Join("", a.HeadingLevels)}, Russian {b.HeadingLevels.Count}: {string.Join("", b.HeadingLevels)})");
                if (!a.CodeLanguages.SequenceEqual(b.CodeLanguages))
                    problems.Add($"{name}: code blocks differ (English [{string.Join(",", a.CodeLanguages)}], Russian [{string.Join(",", b.CodeLanguages)}])");
                if (a.Tables != b.Tables) problems.Add($"{name}: tables differ (English {a.Tables}, Russian {b.Tables})");
                if (!a.Links.SequenceEqual(b.Links))
                    problems.Add($"{name}: file links differ (English [{string.Join(", ", a.Links)}], Russian [{string.Join(", ", b.Links)}])");
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void TheParityCheck_NoticesADifference()
        {
            // The check can fail: a Russian copy with one heading dropped is told apart from the English one.
            string dir = Path.GetFullPath("Temp/VectraDocParity");
            Directory.CreateDirectory(dir);
            string en = Path.Combine(dir, "X.md"), ru = Path.Combine(dir, "X.ru.md");
            File.WriteAllText(en, "# A\n\n## B\n\n```csharp\nx\n```\n\n| a | b |\n|---|---|\n| 1 | 2 |\n");
            File.WriteAllText(ru, "# A\n\n```csharp\nx\n```\n\n| a | b |\n|---|---|\n| 1 | 2 |\n");
            Assert.IsFalse(ShapeOf(en).HeadingLevels.SequenceEqual(ShapeOf(ru).HeadingLevels));
            File.WriteAllText(ru, File.ReadAllText(en));
            Assert.IsTrue(ShapeOf(en).HeadingLevels.SequenceEqual(ShapeOf(ru).HeadingLevels));
            Assert.AreEqual(1, ShapeOf(ru).Tables);
            Directory.Delete(dir, true);
        }

        [Test]
        public void EveryRelativeLinkInTheDocsPointsAtAFile()
        {
            string root = PackageRoot();
            var broken = new List<string>();
            foreach (string doc in Docs(root))
            {
                int number = 0;
                bool inCode = false;
                foreach (string line in File.ReadAllLines(doc))
                {
                    number++;
                    if (line.TrimStart().StartsWith("```")) inCode = !inCode;
                    if (inCode) continue;
                    foreach (Match m in s_Link.Matches(line))
                    {
                        string target = m.Groups["target"].Value;
                        if (target.StartsWith("http") || target.StartsWith("mailto:") || target.StartsWith("#")) continue;
                        int hash = target.IndexOf('#');
                        if (hash > 0) target = target.Substring(0, hash);
                        string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(doc), target));
                        if (!File.Exists(full) && !Directory.Exists(full)) broken.Add($"{Path.GetFileName(doc)}:{number}: {m.Groups["target"].Value}");
                    }
                }
            }

            Assert.IsEmpty(broken, "Links to files that do not exist:\n" + string.Join("\n", broken));
        }
    }
}
