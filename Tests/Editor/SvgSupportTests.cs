using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>Keeps the "SVG support" table of the guides, the parser's registry and the import report in agreement.</summary>
    public class SvgSupportTests
    {
        static readonly Regex s_Row = new Regex(
            @"^\|\s*`<(?<name>[^>`]+)>`\s*\|\s*(?<level>Supported|Partial|Not supported|Поддерживается|Частично|Не поддерживается)\s*\|",
            RegexOptions.Compiled);

        static string Root()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VectraShape).Assembly);
            if (info == null) Assert.Ignore("Vectra is a folder under Assets in this project: the guide tests read the package folder");
            return info.resolvedPath;
        }

        static SvgSupportLevel Level(string text)
        {
            switch (text)
            {
                case "Supported": case "Поддерживается": return SvgSupportLevel.Supported;
                case "Partial": case "Частично": return SvgSupportLevel.Partial;
                default: return SvgSupportLevel.NotSupported;
            }
        }

        static Dictionary<string, SvgSupportLevel> TableOf(string file)
        {
            var rows = new Dictionary<string, SvgSupportLevel>();
            foreach (var line in File.ReadAllLines(Path.Combine(Root(), "Documentation~", file)))
            {
                var m = s_Row.Match(line);
                if (!m.Success) continue;
                string name = m.Groups["name"].Value;
                Assert.IsFalse(rows.ContainsKey(name), file + ": <" + name + "> is listed twice");
                rows[name] = Level(m.Groups["level"].Value);
            }

            return rows;
        }

        [TestCase("Vectra.md")]
        [TestCase("Vectra.ru.md")]
        public void TheTable_ListsExactlyTheElementsOfTheRegistry_WithTheSameLevels(string file)
        {
            var table = TableOf(file);
            var missing = SvgSupport.Elements.Keys.Where(k => !table.ContainsKey(k)).ToList();
            var extra = table.Keys.Where(k => !SvgSupport.Elements.ContainsKey(k)).ToList();
            Assert.IsEmpty(missing, file + ": registry elements missing from the table: " + string.Join(", ", missing));
            Assert.IsEmpty(extra, file + ": table rows the parser does not know: " + string.Join(", ", extra));
            foreach (var kv in table)
                Assert.AreEqual(SvgSupport.Elements[kv.Key], kv.Value, file + ": <" + kv.Key + ">");
        }

        static string Wrap(string body) => "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\">" + body + "</svg>";

        static List<string> Warnings(string svg)
        {
            var doc = SvgParser.Parse(svg);
            var list = new List<string>(doc.warnings);
            UnityEngine.Object.DestroyImmediate(doc);
            return list;
        }

        static IEnumerable<string> NotSupported() => SvgSupport.Elements.Where(kv => kv.Value == SvgSupportLevel.NotSupported).Select(kv => kv.Key);
        static IEnumerable<string> Handled() => SvgSupport.Elements.Where(kv => kv.Value != SvgSupportLevel.NotSupported).Select(kv => kv.Key);

        [Test]
        public void EveryUnsupportedElement_IsInTheImportReport()
        {
            foreach (var name in NotSupported())
            {
                var w = Warnings(Wrap("<rect width=\"10\" height=\"10\"/><" + name + "/>"));
                Assert.IsTrue(w.Any(x => x.Contains("<" + name + ">")), "<" + name + "> is silent: " + string.Join(" | ", w));
            }
        }

        [Test]
        public void EveryUnsupportedElement_IsReportedInsideOtherContainersToo()
        {
            foreach (var name in new[] { "animate", "set", "feConvolveMatrix", "foreignObject", "script" })
            {
                var w = Warnings(Wrap("<defs><filter id=\"f\"><" + name + "/></filter></defs><g><rect width=\"5\" height=\"5\"><" + name + "/></rect></g>"));
                Assert.IsTrue(w.Any(x => x.Contains("<" + name + ">")), name + ": " + string.Join(" | ", w));
            }
        }

        [Test]
        public void SupportedAndPartialElements_AreNotReportedAsUnsupported()
        {
            foreach (var name in Handled())
            {
                var w = Warnings(Wrap("<" + name + "/>"));
                Assert.IsFalse(w.Any(x => x.Contains("<" + name + ">")), "<" + name + "> is reported though the table says it works: " + string.Join(" | ", w));
            }
        }

        [Test]
        public void UnknownElements_AreReported_ButEditorDataIsNot()
        {
            var w = Warnings(Wrap("<blink/><sodipodi:namedview/><inkscape:perspective/><metadata><rdf:RDF><cc:Work/></rdf:RDF></metadata>"));
            Assert.IsTrue(w.Any(x => x.Contains("<blink>")), string.Join(" | ", w));
            Assert.AreEqual(1, w.Count, string.Join(" | ", w));
        }

        [Test]
        public void TheShapes_AreDrawn()
        {
            var samples = new Dictionary<string, string>
            {
                ["path"] = "<path d=\"M0 0 L10 0 L10 10 Z\"/>",
                ["rect"] = "<rect width=\"10\" height=\"10\"/>",
                ["circle"] = "<circle r=\"5\"/>",
                ["ellipse"] = "<ellipse rx=\"5\" ry=\"3\"/>",
                ["line"] = "<line x2=\"10\" y2=\"10\" stroke=\"#000\"/>",
                ["polyline"] = "<polyline points=\"0,0 10,0 10,10\" stroke=\"#000\"/>",
                ["polygon"] = "<polygon points=\"0,0 10,0 10,10\"/>"
            };
            foreach (var kv in samples)
            {
                var doc = SvgParser.Parse(Wrap(kv.Value));
                Assert.AreEqual(1, doc.elements.Count, kv.Key + ": " + string.Join(" | ", doc.warnings));
                Assert.IsEmpty(doc.warnings, kv.Key);
                UnityEngine.Object.DestroyImmediate(doc);
            }
        }

        [TestCase("<rect width=\"9\" height=\"9\" paint-order=\"stroke\"/>", "paint-order")]
        [TestCase("<rect width=\"9\" height=\"9\" style=\"mix-blend-mode:multiply\"/>", "mix-blend-mode")]
        [TestCase("<text word-spacing=\"2\">a</text>", "word-spacing")]
        [TestCase("<text dominant-baseline=\"middle\">a</text>", "dominant-baseline")]
        [TestCase("<text textLength=\"50\">a</text>", "textLength")]
        [TestCase("<text rotate=\"10\">a</text>", "rotate")]
        [TestCase("<text><textPath href=\"#p\">a</textPath></text>", "textPath")]
        [TestCase("<rect width=\"9\" height=\"9\" style=\"transform:rotate(9deg)\"/>", "CSS transform")]
        [TestCase("<rect width=\"9\" height=\"9\" style=\"fill:var(--c)\"/>", "var()")]
        [TestCase("<rect width=\"9\" height=\"9\" style=\"clip-path:circle(5px)\"/>", "clip-path")]
        [TestCase("<rect width=\"9\" height=\"9\" style=\"filter:blur(3px)\"/>", "filter")]
        [TestCase("<defs><radialGradient id=\"g\" fr=\"0.2\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"1\" stop-color=\"blue\"/></radialGradient></defs><rect fill=\"url(#g)\" width=\"9\" height=\"9\"/>", "focal radius")]
        [TestCase("<use href=\"other.svg#a\"/>", "other files")]
        [TestCase("<use href=\"#nothing\"/>", "does not exist")]
        [TestCase("<clipPath id=\"c\"><text>a</text><rect width=\"5\" height=\"5\"/></clipPath><rect clip-path=\"url(#c)\" width=\"9\" height=\"9\"/>", "clipPath")]
        [TestCase("<image width=\"9\" height=\"9\" preserveAspectRatio=\"none\" href=\"a.png\"/>", "preserveAspectRatio")]
        public void SilentGaps_AreReported(string body, string word)
        {
            var w = Warnings(Wrap(body));
            Assert.IsTrue(w.Any(x => x.Contains(word)), word + ": " + string.Join(" | ", w));
        }

        [TestCase(".a:hover{fill:red}", "pseudo")]
        [TestCase(".a+.b{fill:red}", "selector")]
        [TestCase("[id^=a]{fill:red}", "selector")]
        [TestCase("@font-face{font-family:x}", "@font-face")]
        [TestCase("@keyframes k{from{fill:red}}", "@keyframes")]
        [TestCase("@media print{.a{fill:red}}", "@media")]
        [TestCase("@import url(a.css);", "@import")]
        public void UnsupportedCss_IsReported(string css, string word)
        {
            var w = Warnings(Wrap("<style>" + css + "</style><rect class=\"a\" width=\"9\" height=\"9\"/>"));
            Assert.IsTrue(w.Any(x => x.Contains(word)), word + ": " + string.Join(" | ", w));
        }

        [Test]
        public void AnImportStatement_DoesNotSwallowTheRuleThatFollowsIt()
        {
            var doc = SvgParser.Parse(Wrap("<style>@import url(a.css);\n.a{fill:#00ff00}</style><rect class=\"a\" width=\"9\" height=\"9\"/>"));
            Assert.AreEqual(1, doc.elements.Count);
            Assert.AreEqual(1f, doc.elements[0].fill.color.g, 1e-3f);
            UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
