using System.Diagnostics;
using System.Linq;
using System.Text;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class SvgLimitsTests
    {
        static bool Reported(VectraSvgDocument doc, string word) =>
            doc.warnings.Any(w => w.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0);

        static string DeepGroups(int depth)
        {
            var sb = new StringBuilder("<svg viewBox='0 0 10 10'>");
            for (int i = 0; i < depth; i++) sb.Append("<g>");
            sb.Append("<rect width='5' height='5'/>");
            for (int i = 0; i < depth; i++) sb.Append("</g>");
            return sb.Append("</svg>").ToString();
        }

        /// <summary>Level k holds <paramref name="fanOut"/> uses of level k-1: a few kilobytes that name fanOut^levels rects.</summary>
        static string UseBomb(int levels, int fanOut)
        {
            var sb = new StringBuilder("<svg viewBox='0 0 10 10'><defs><rect id='l0' width='1' height='1'/>");
            for (int k = 1; k <= levels; k++)
            {
                sb.Append("<g id='l").Append(k).Append("'>");
                for (int j = 0; j < fanOut; j++) sb.Append("<use href='#l").Append(k - 1).Append("'/>");
                sb.Append("</g>");
            }

            return sb.Append("</defs><use href='#l").Append(levels).Append("'/></svg>").ToString();
        }

        [Test]
        public void UseBomb_IsCutOffAndReported()
        {
            var sw = Stopwatch.StartNew();
            var doc = SvgParser.Parse(UseBomb(5, 10)); // 100000 rects
            sw.Stop();
            UnityEngine.Debug.Log($"VECTRA-METRIC use bomb 5x10: {doc.elements.Count} elements, {sw.ElapsedMilliseconds} ms");
            Assert.Less(doc.elements.Count, 20000, "elements generated from a 1 KB file");
            Assert.IsTrue(Reported(doc, "limit"), string.Join(" | ", doc.warnings));
            Object.DestroyImmediate(doc);
        }

        [Test]
        public void DeepGroups_AreReported()
        {
            var doc = SvgParser.Parse(DeepGroups(3000));
            Assert.IsTrue(Reported(doc, "limit"), string.Join(" | ", doc.warnings));
            Object.DestroyImmediate(doc);
        }

        [Test]
        public void HugePathData_IsSkippedAndReported()
        {
            var sb = new StringBuilder("<svg viewBox='0 0 10 10'><path d='M0 0");
            while (sb.Length < 3000000) sb.Append(" L1 1 L2 0");
            sb.Append("'/><rect width='1' height='1'/></svg>");
            var doc = SvgParser.Parse(sb.ToString());
            Assert.IsTrue(Reported(doc, "limit"), string.Join(" | ", doc.warnings));
            Assert.AreEqual(1, doc.elements.Count, "only the small rect survives");
            Object.DestroyImmediate(doc);
        }

        [Test]
        public void TooManyElements_AreReported()
        {
            var sb = new StringBuilder("<svg viewBox='0 0 10 10'>");
            for (int i = 0; i < 250000; i++) sb.Append("<g/>");
            sb.Append("</svg>");
            var doc = SvgParser.Parse(sb.ToString());
            Assert.IsTrue(Reported(doc, "limit"), string.Join(" | ", doc.warnings));
            Object.DestroyImmediate(doc);
        }

        [Test]
        public void ModestDocument_IsUntouchedByTheLimits()
        {
            var doc = SvgParser.Parse(UseBomb(2, 10)); // 100 rects
            Assert.AreEqual(100, doc.elements.Count);
            Assert.IsFalse(Reported(doc, "limit"), string.Join(" | ", doc.warnings));
            Object.DestroyImmediate(doc);
        }

        // Only safe once the parser is bounded: these used to overflow the stack or run for hours.
        [Test]
        public void HostileDepthAndBomb_FinishQuickly()
        {
            var sw = Stopwatch.StartNew();
            var deep = SvgParser.Parse(DeepGroups(200000));
            var bomb = SvgParser.Parse(UseBomb(12, 10));
            var cycle = SvgParser.Parse("<svg viewBox='0 0 1 1'><g id='a'><use href='#a'/><use href='#a'/><rect width='1' height='1'/></g></svg>");
            sw.Stop();
            UnityEngine.Debug.Log($"VECTRA-METRIC hostile set parsed in {sw.ElapsedMilliseconds} ms");
            Assert.Less(sw.ElapsedMilliseconds, 15000);
            Assert.IsTrue(Reported(deep, "limit"));
            Assert.IsTrue(Reported(bomb, "limit"));
            Object.DestroyImmediate(deep);
            Object.DestroyImmediate(bomb);
            Object.DestroyImmediate(cycle);
        }

        [Test]
        public void Switch_RendersTheFirstChildThatPasses()
        {
            const string svg = @"<svg viewBox='0 0 10 10'><switch>
                <rect width='1' height='1' fill='#f00' systemLanguage='de, fr'/>
                <rect width='1' height='1' fill='#0f0' requiredExtensions='http://example.com/ext'/>
                <rect width='1' height='1' fill='#00f' systemLanguage='en-US'/>
                <rect width='1' height='1' fill='#fff'/></switch></svg>";
            var doc = SvgParser.Parse(svg);
            Assert.AreEqual(1, doc.elements.Count);
            Assert.AreEqual(Color.blue, doc.elements[0].fill.color);
            Assert.IsTrue(Reported(doc, "switch"), string.Join(" | ", doc.warnings));
            Object.DestroyImmediate(doc);
        }

        [Test]
        public void Switch_FallbackChildWithoutConditionsPasses()
        {
            const string svg = @"<svg viewBox='0 0 10 10'><switch>
                <rect width='1' height='1' fill='#f00' systemLanguage='de'/>
                <rect width='1' height='1' fill='#fff'/></switch></svg>";
            var doc = SvgParser.Parse(svg);
            Assert.AreEqual(1, doc.elements.Count);
            Assert.AreEqual(Color.white, doc.elements[0].fill.color);
            Object.DestroyImmediate(doc);
        }

        [Test]
        public void Switch_WithNoPassingChild_RendersNothing()
        {
            var doc = SvgParser.Parse("<svg viewBox='0 0 10 10'><switch><rect width='1' height='1' systemLanguage='de'/></switch></svg>");
            Assert.AreEqual(0, doc.elements.Count);
            Object.DestroyImmediate(doc);
        }

        [Test]
        public void Markers_AreDrawnNotReported()
        {
            const string svg = @"<svg viewBox='0 0 10 10'><defs><marker id='m' markerWidth='2' markerHeight='2'><path d='M0 0 L2 1 L0 2z'/></marker></defs>
                <path d='M1 1 L8 8' stroke='black' marker-end='url(#m)'/></svg>";
            var doc = SvgParser.Parse(svg);
            Assert.AreEqual(2, doc.elements.Count, "the path and its end marker");
            Assert.IsFalse(Reported(doc, "marker"), string.Join(" | ", doc.warnings));
            Object.DestroyImmediate(doc);
        }

        [Test]
        public void NoMarkers_NoMarkerWarning()
        {
            var doc = SvgParser.Parse("<svg viewBox='0 0 10 10'><path d='M1 1 L8 8' stroke='black' marker-end='none'/></svg>");
            Assert.IsFalse(Reported(doc, "marker"));
            Object.DestroyImmediate(doc);
        }
    }
}
