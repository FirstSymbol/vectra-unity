using System.Linq;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>How SVG text is split into styled runs, and how runs become the markup of each engine.</summary>
    public class TextRunTests
    {
        static SvgElementData Text(string body, string attrs = "", string viewBox = "0 0 200 100")
        {
            var doc = SvgParser.Parse($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='{viewBox}'><text x='10' y='50' font-size='20' {attrs}>{body}</text></svg>");
            try
            {
                return doc.elements.Single(e => e.isText);
            }
            finally
            {
                Object.DestroyImmediate(doc);
            }
        }

        [Test]
        public void PlainText_IsOneRun()
        {
            var e = Text("Hello   world");
            Assert.AreEqual(1, e.runs.Count);
            Assert.AreEqual("Hello world", e.runs[0].text);
            Assert.AreEqual("Hello world", e.text);
        }

        [Test]
        public void Tspans_BecomeRunsWithTheirOwnStyle()
        {
            var e = Text("Hi <tspan font-weight='bold' fill='#ff0000'>red</tspan> <tspan font-style='italic' font-size='40'>big</tspan>", "fill='#00ff00'");
            Assert.AreEqual(4, e.runs.Count);
            Assert.AreEqual(new[] { "Hi ", "red", " ", "big" }, e.runs.Select(r => r.text).ToArray());
            Assert.IsFalse(e.runs[0].bold);
            Assert.AreEqual(Color.green.g, e.runs[0].color.g, 0.01f);
            Assert.IsTrue(e.runs[1].bold);
            Assert.AreEqual(1f, e.runs[1].color.r, 0.01f);
            Assert.IsTrue(e.runs[3].italic);
            Assert.AreEqual(40f, e.runs[3].fontSize, 0.01f);
            Assert.AreEqual(20f, e.runs[0].fontSize, 0.01f);
        }

        [Test]
        public void Runs_KeepDocumentOrder_AndAbuttingTspansDoNotGainASpace()
        {
            var e = Text("A<tspan fill='red'>B</tspan>C<tspan font-weight='bold'>D</tspan>E");
            Assert.AreEqual(new[] { "A", "B", "C", "D", "E" }, e.runs.Select(r => r.text).ToArray());
            Assert.AreEqual("ABCDE", e.text);
        }

        [Test]
        public void Whitespace_CollapsesAcrossRuns()
        {
            var e = Text("one \n   <tspan font-weight='bold'>  two  </tspan>\n three  ");
            Assert.AreEqual("one two three", e.text);
            Assert.AreEqual("one two three", string.Concat(e.runs.Select(r => r.text)));
        }

        [Test]
        public void NestedTspans_InheritTheOuterStyle()
        {
            var e = Text("<tspan fill='#0000ff' font-weight='bold'>x<tspan font-style='italic'>y</tspan></tspan>");
            Assert.AreEqual(2, e.runs.Count);
            Assert.IsTrue(e.runs[1].bold, "inherited weight");
            Assert.IsTrue(e.runs[1].italic);
            Assert.AreEqual(1f, e.runs[1].color.b, 0.01f, "inherited fill");
        }

        [Test]
        public void TextEqualsTheConcatenationOfItsRuns()
        {
            var e = Text("a <tspan>b</tspan><tspan fill='red'> c</tspan> d");
            Assert.AreEqual(e.text, string.Concat(e.runs.Select(r => r.text)));
        }

        [Test]
        public void RunSizes_FollowTheDocumentScale()
        {
            var e = Text("a<tspan font-size='40'>b</tspan>", "", "0 0 100 50");
            Assert.AreEqual(e.fontSize * 2f, e.runs[1].fontSize, 0.01f);
        }

        [Test]
        public void VeryDeepTspans_DoNotOverflowTheStack()
        {
            var open = string.Concat(Enumerable.Repeat("<tspan>", 60));
            var close = string.Concat(Enumerable.Repeat("</tspan>", 60));
            var e = Text("start" + open + "deep" + close);
            Assert.IsNotNull(e);
            StringAssert.StartsWith("start", e.text);
        }

        [Test]
        public void EmptyText_MakesNoElement()
        {
            var doc = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 10 10'><text x='1' y='1'>   </text></svg>");
            Assert.IsFalse(doc.elements.Any(e => e.isText));
            Object.DestroyImmediate(doc);
        }

        // --- markup ---------------------------------------------------------------------------------------

        static SvgElementData Element(params SvgTextRun[] runs) => new SvgElementData
        {
            isText = true, text = string.Concat(runs.Select(r => r.text)), fontSize = 20f, textColor = Color.white,
            runs = runs.ToList()
        };

        static SvgTextRun Run(string text, bool bold = false, bool italic = false, Color? color = null, float size = 20f) =>
            new SvgTextRun { text = text, bold = bold, italic = italic, color = color ?? Color.white, fontSize = size };

        [Test]
        public void Markup_PlainRun_HasNoTags()
        {
            var e = Element(Run("plain"));
            Assert.AreEqual("plain", VectraRichText.Build(e, VectraMarkupDialect.TextMeshPro, Color.white, 1f, false, false));
        }

        [Test]
        public void Markup_StyledRuns_UseTags()
        {
            var e = Element(Run("a "), Run("b", bold: true), Run(" c"), Run("d", italic: true, color: Color.red, size: 40f));
            string s = VectraRichText.Build(e, VectraMarkupDialect.TextMeshPro, Color.white, 1f, false, false);
            Assert.AreEqual("a <b>b</b> c<i><color=#FF0000FF><size=200%>d</size></color></i>", s);
        }

        [Test]
        public void Markup_Legacy_UsesPixelSizes()
        {
            var e = Element(Run("a"), Run("b", size: 40f));
            Assert.AreEqual("a<size=80>b</size>", VectraRichText.Build(e, VectraMarkupDialect.Legacy, Color.white, 2f, false, false));
        }

        [Test]
        public void Markup_Tint_MultipliesRunColours()
        {
            var e = Element(Run("a"), Run("b", color: Color.red));
            string s = VectraRichText.Build(e, VectraMarkupDialect.Lettera, new Color(1, 1, 1, 0.5f), 1f, false, false);
            Assert.AreEqual("a<color=#FF000080>b</color>", s);
        }

        [Test]
        public void Markup_WhatTheComponentAlreadyDoes_IsNotRepeated()
        {
            var e = Element(Run("a", bold: true), Run("b", bold: true, italic: true));
            string s = VectraRichText.Build(e, VectraMarkupDialect.TextMeshPro, Color.white, 1f, true, true);
            Assert.AreEqual("ab", s);
        }

        [Test]
        public void Markup_Lettera_EscapesItsMarkupCharacters()
        {
            var e = Element(Run("<b>*x*_y_[z]~`\\"));
            Assert.AreEqual("\\<b>\\*x\\*\\_y\\_\\[z]\\~\\`\\\\", VectraRichText.Build(e, VectraMarkupDialect.Lettera, Color.white, 1f, false, false));
        }

        [Test]
        public void Markup_TextMeshPro_ProtectsALiteralLessThan()
        {
            var e = Element(Run("1 < 2"));
            Assert.AreEqual("<noparse>1 < 2</noparse>", VectraRichText.Build(e, VectraMarkupDialect.TextMeshPro, Color.white, 1f, false, false));
        }

        [Test]
        public void Markup_Legacy_BreaksALiteralLessThan()
        {
            var e = Element(Run("<b>x"));
            var s = VectraRichText.Build(e, VectraMarkupDialect.Legacy, Color.white, 1f, false, false);
            Assert.AreEqual("<​b>x", s);
        }

        [Test]
        public void Markup_DocumentWithoutRuns_UsesTheFlatText()
        {
            var e = new SvgElementData { isText = true, text = "old", fontSize = 16f, bold = true, textColor = Color.white };
            var runs = VectraRichText.RunsOf(e);
            Assert.AreEqual(1, runs.Count);
            Assert.AreEqual("old", runs[0].text);
            Assert.AreEqual("old", VectraRichText.Build(e, VectraMarkupDialect.Lettera, Color.white, 1f, true, false));
        }

        [Test]
        public void HasStyledRuns_OnlyForRunsThatDiffer()
        {
            Assert.IsFalse(VectraRichText.HasStyledRuns(Element(Run("a"), Run("b"))));
            Assert.IsTrue(VectraRichText.HasStyledRuns(Element(Run("a"), Run("b", bold: true))));
            Assert.IsFalse(VectraRichText.HasStyledRuns(Element(Run("a", bold: true))));
        }
    }
}
