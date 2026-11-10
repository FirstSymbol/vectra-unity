using System.IO;
using System.Linq;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>SVG text positions, chunks, letter spacing, decoration and white space: what the parser hands to every text engine.</summary>
    public class SvgTextLayoutTests
    {
        static SvgElementData[] Texts(string body, out VectraSvgDocument doc, string svgAttrs = "viewBox='0 0 200 100'")
        {
            doc = SvgParser.Parse($"<svg xmlns='http://www.w3.org/2000/svg' {svgAttrs}>{body}</svg>");
            return doc.elements.Where(e => e.isText).ToArray();
        }

        static SvgElementData[] Texts(string body)
        {
            var t = Texts(body, out var doc);
            Object.DestroyImmediate(doc);
            return t;
        }

        static string CorpusFile(string name)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VectraShape).Assembly);
            if (info == null) Assert.Ignore("Vectra is a folder under Assets in this project: the corpus tests read the package folder");
            return Path.Combine(info.resolvedPath, "Tests", "FigmaCorpus~", name);
        }

        // --- Figma's own export --------------------------------------------------------------------------------

        [Test]
        public void FigmaMultiline_EveryTspanIsALineAtItsOwnXAndY_AndTheLineBreakMovesNothing()
        {
            var doc = VectraSvg.Parse(File.ReadAllText(CorpusFile("21_text_multiline_live.svg")));
            try
            {
                var t = doc.elements.Where(e => e.isText).ToArray();
                Assert.AreEqual(4, t.Length);
                var expected = new[] { (31.2031f, 27.8182f, "Hello, Figma!"), (16.8359f, 51.8182f, "Second line here"), (41.8281f, 75.8182f, "Third one, "), (46.2109f, 99.8182f, "centered") };
                for (int i = 0; i < 4; i++)
                {
                    Assert.AreEqual(expected[i].Item1, t[i].textPosition.x, 1e-3f, "x of line " + i);
                    Assert.AreEqual(expected[i].Item2, t[i].textPosition.y, 1e-3f, "y of line " + i);
                    Assert.AreEqual(expected[i].Item3, t[i].text, "text of line " + i);
                    Assert.AreEqual(SvgTextAnchor.Start, t[i].anchor);
                    Assert.AreEqual(16f, t[i].fontSize, 1e-4f);
                }

                Assert.IsFalse(doc.warnings.Any(w => w.Contains("text")), string.Join("; ", doc.warnings));
            }
            finally
            {
                Object.DestroyImmediate(doc);
            }
        }

        [Test]
        public void FigmaStyled_SeparateTextElementsKeepTheirStyle_SpacingAndDecoration()
        {
            var doc = VectraSvg.Parse(File.ReadAllText(CorpusFile("22_text_styled_live.svg")));
            try
            {
                // the whitespace-only piece makes no element; the five others come in document order
                var t = doc.elements.Where(e => e.isText).ToArray();
                Assert.AreEqual(new[] { " word,", "Bold", "red", "spaced ", "letters", "underlined" }, t.Select(e => e.text).ToArray());
                Assert.AreEqual(74.3594f, t[0].textPosition.x, 1e-3f);
                Assert.IsTrue(t[1].bold);
                Assert.AreEqual(10f, t[1].textPosition.x, 1e-3f);
                Assert.AreEqual(0xE6 / 255f, t[2].textColor.r, 0.01f);
                Assert.AreEqual(49.4219f, t[2].textPosition.x, 1e-3f);
                Assert.AreEqual(3f, t[3].runs[0].letterSpacing, 1e-4f, "3px");
                Assert.AreEqual(44.3182f, t[3].textPosition.y, 1e-3f);
                Assert.AreEqual(63.3182f, t[4].textPosition.y, 1e-3f);
                Assert.AreEqual(3f, t[4].runs[0].letterSpacing, 1e-4f);
                Assert.IsTrue(t[5].runs[0].underline);
                Assert.IsFalse(t[5].runs[0].lineThrough);
                Assert.AreEqual(0f, t[0].runs[0].letterSpacing, 1e-6f, "0em");
                Assert.IsFalse(doc.warnings.Any(w => w.Contains("letter-spacing") || w.Contains("text-decoration")));
            }
            finally
            {
                Object.DestroyImmediate(doc);
            }
        }

        // --- chunks ------------------------------------------------------------------------------------------------

        [Test]
        public void TspansWithoutPosition_StayInTheChunkOfTheirText()
        {
            var t = Texts("<text x='10' y='50' font-size='20'>A<tspan fill='red'>B</tspan>C</text>");
            Assert.AreEqual(1, t.Length);
            Assert.AreEqual(new[] { "A", "B", "C" }, t[0].runs.Select(r => r.text).ToArray());
            Assert.AreEqual(new Vector2(10f, 50f), t[0].textPosition);
        }

        [Test]
        public void ATspanWithXOrY_StartsANewChunk()
        {
            var t = Texts("<text x='10' y='20' font-size='20'>one<tspan x='30' y='60'>two</tspan><tspan x='5'>three</tspan></text>");
            Assert.AreEqual(new[] { "one", "two", "three" }, t.Select(e => e.text).ToArray());
            Assert.AreEqual(new Vector2(30f, 60f), t[1].textPosition);
            Assert.AreEqual(new Vector2(5f, 60f), t[2].textPosition, "y continues");
        }

        [Test]
        public void Dy_AddsUpFromTheCurrentLine_AndEmIsTheFontSize()
        {
            var t = Texts("<text font-size='20'><tspan x='10' y='20'>a</tspan><tspan x='10' dy='1.2em'>b</tspan><tspan x='10' dy='1.2em'>c</tspan></text>");
            Assert.AreEqual(20f, t[0].textPosition.y, 1e-4f);
            Assert.AreEqual(44f, t[1].textPosition.y, 1e-3f);
            Assert.AreEqual(68f, t[2].textPosition.y, 1e-3f);
        }

        [Test]
        public void DxOnAChunkStart_MovesIt()
        {
            var t = Texts("<text x='10' y='20' dx='5' dy='-3'>a</text>");
            Assert.AreEqual(new Vector2(15f, 17f), t[0].textPosition);
        }

        [Test]
        public void ListsOfX_PlaceEveryGlyph()
        {
            var t = Texts("<text x='10 20 35' y='7' font-size='10'>abc</text>");
            Assert.AreEqual(new[] { "a", "b", "c" }, t.Select(e => e.text).ToArray());
            Assert.AreEqual(new[] { 10f, 20f, 35f }, t.Select(e => e.textPosition.x).ToArray());
            Assert.IsTrue(t.All(e => Mathf.Approximately(e.textPosition.y, 7f)));
        }

        [Test]
        public void AListShorterThanTheText_LeavesTheRestInTheLastChunk()
        {
            var t = Texts("<text x='10 20' y='7' font-size='10'>abcd</text>");
            Assert.AreEqual(new[] { "a", "bcd" }, t.Select(e => e.text).ToArray());
        }

        [Test]
        public void DxAndDyInTheMiddleOfALine_BecomeRunOffsets()
        {
            var t = Texts("<text x='10' y='50' font-size='20'>ab<tspan dx='5' dy='-4'>c</tspan><tspan dy='2'>d</tspan></text>");
            Assert.AreEqual(1, t.Length);
            var r = t[0].runs;
            Assert.AreEqual(new[] { "ab", "c", "d" }, r.Select(x => x.text).ToArray());
            Assert.AreEqual(5f, r[1].dx, 1e-4f);
            Assert.AreEqual(-4f, r[1].dy, 1e-4f);
            Assert.AreEqual(-2f, r[2].dy, 1e-4f, "dy adds up along the line");
            Assert.AreEqual(0f, r[2].dx, 1e-4f);
        }

        [Test]
        public void TextAnchor_IsPerChunk()
        {
            var t = Texts("<text x='100' y='20' text-anchor='middle' font-size='10'>mid<tspan x='190' y='40' text-anchor='end'>end</tspan></text>");
            Assert.AreEqual(SvgTextAnchor.Middle, t[0].anchor);
            Assert.AreEqual(SvgTextAnchor.End, t[1].anchor);
        }

        [Test]
        public void Chunks_FollowTheTransform()
        {
            var t = Texts("<g transform='translate(5 6) scale(2)'><text letter-spacing='3' font-size='10'><tspan x='10' y='20'>a</tspan></text></g>");
            Assert.AreEqual(new Vector2(25f, 46f), t[0].textPosition);
            Assert.AreEqual(20f, t[0].fontSize, 1e-3f);
            Assert.AreEqual(6f, t[0].runs[0].letterSpacing, 1e-3f);
        }

        [Test]
        public void TooManyChunks_AreCutOff_WithAWarning()
        {
            var xs = string.Join(" ", Enumerable.Range(0, 600));
            var t = Texts("<text x='" + xs + "' y='7'>" + new string('a', 600) + "</text>", out var doc);
            try
            {
                Assert.AreEqual(512, t.Length);
                Assert.IsTrue(doc.warnings.Any(w => w.Contains("512")));
            }
            finally
            {
                Object.DestroyImmediate(doc);
            }
        }

        // --- letter-spacing and decoration -------------------------------------------------------------------------

        [TestCase("0.1em", 2f)]
        [TestCase("3px", 3f)]
        [TestCase("3", 3f)]
        [TestCase("-1px", -1f)]
        [TestCase("normal", 0f)]
        public void LetterSpacing_Units(string value, float expected)
        {
            var t = Texts($"<text x='0' y='10' font-size='20' letter-spacing='{value}'>ab</text>");
            Assert.AreEqual(expected, t[0].runs[0].letterSpacing, 1e-4f);
        }

        [Test]
        public void LetterSpacing_InTheStyleAttribute_IsInherited_AndATspanMayChangeIt()
        {
            var t = Texts("<text x='0' y='10' font-size='10' style='letter-spacing:2px'>ab<tspan letter-spacing='5px'>cd</tspan></text>");
            Assert.AreEqual(2f, t[0].runs[0].letterSpacing, 1e-4f);
            Assert.AreEqual(5f, t[0].runs[1].letterSpacing, 1e-4f);
        }

        [Test]
        public void TextDecoration_UnderlineAndLineThrough_PassToTspans_AndNoneChangesNothing()
        {
            var t = Texts("<text x='0' y='10' text-decoration='underline'>a<tspan style='text-decoration: line-through'>b</tspan>c</text>" +
                          "<text x='0' y='30' text-decoration='none'>d</text>");
            var r = t[0].runs;
            Assert.AreEqual(3, r.Count);
            Assert.IsTrue(r[0].underline && !r[0].lineThrough);
            Assert.IsTrue(r[1].underline && r[1].lineThrough, "a decoration of the parent is drawn across the child");
            Assert.IsTrue(r[2].underline && !r[2].lineThrough);
            Assert.IsFalse(t[1].runs[0].underline);
        }

        [Test]
        public void TextDecoration_Overline_IsReported()
        {
            Texts("<text x='0' y='10' text-decoration='overline'>a</text>", out var doc);
            try
            {
                Assert.IsTrue(doc.warnings.Any(w => w.Contains("overline")));
            }
            finally
            {
                Object.DestroyImmediate(doc);
            }
        }

        // --- white space -------------------------------------------------------------------------------------------

        [Test]
        public void DefaultSpacing_Collapses()
        {
            var t = Texts("<text x='0' y='10'>  a   b \n c  </text>");
            Assert.AreEqual("a b c", t[0].text);
        }

        [Test]
        public void Preserve_KeepsSpaces_AndTurnsTabsAndInnerLineBreaksIntoSpaces()
        {
            var t = Texts("<text x='0' y='10' xml:space='preserve'>  a   b&#10;c\td  </text>");
            Assert.AreEqual("  a   b c d  ", t[0].text);
        }

        [Test]
        public void WhiteSpacePre_InTheStyle_AlsoPreserves()
        {
            var t = Texts("<text x='0' y='10' style='white-space: pre'>a   b</text>");
            Assert.AreEqual("a   b", t[0].text);
        }

        [Test]
        public void ALineBreakAtTheEndOfAPiece_IsDropped()
        {
            var t = Texts("<text x='0' y='0' xml:space='preserve'><tspan x='5' y='10'>Hello&#10;</tspan><tspan x='5' y='30'>World&#10;</tspan></text>");
            Assert.AreEqual(new[] { "Hello", "World" }, t.Select(e => e.text).ToArray());
        }

        [Test]
        public void APieceOfOnlySpaces_DrawsNothing()
        {
            var t = Texts("<text xml:space='preserve'><tspan x='5' y='10'> </tspan><tspan x='9' y='10'> a</tspan></text>");
            Assert.AreEqual(1, t.Length);
            Assert.AreEqual(" a", t[0].text);
            Assert.AreEqual(9f, t[0].textPosition.x, 1e-4f);
        }

        // --- styles of a chunk ---------------------------------------------------------------------------------------

        [Test]
        public void TheElementTakesItsLookFromItsFirstRun()
        {
            var t = Texts("<text><tspan x='5' y='10' font-weight='bold' font-style='italic' font-size='12' fill='#00ff00'>g</tspan></text>");
            Assert.IsTrue(t[0].bold);
            Assert.IsTrue(t[0].italic);
            Assert.AreEqual(12f, t[0].fontSize, 1e-4f);
            Assert.AreEqual(1f, t[0].textColor.g, 0.01f);
        }

        [Test]
        public void DisplayNoneTspan_IsSkipped()
        {
            var t = Texts("<text x='0' y='10'>a<tspan display='none'>b</tspan>c</text>");
            Assert.AreEqual("ac", t[0].text);
        }

        // --- markup of the engines --------------------------------------------------------------------------------

        static SvgElementData Spaced(float spacing, bool underline = false, bool strike = false) => new SvgElementData
        {
            isText = true, text = "ab", fontSize = 16f, textColor = Color.white,
            runs = { new SvgTextRun { text = "ab", fontSize = 16f, color = Color.white, letterSpacing = spacing, underline = underline, lineThrough = strike } }
        };

        [Test]
        public void Markup_LetterSpacingAndDecoration_UseTheEnginesTags()
        {
            var e = Spaced(3f, true, true);
            Assert.AreEqual("<cspace=0.1875em><u><s>ab</s></u></cspace>", VectraRichText.Build(e, VectraMarkupDialect.TextMeshPro, Color.white, 1f, false, false));
            Assert.AreEqual("<tracking=0.1875em><u><s>ab</s></u></tracking>", VectraRichText.Build(e, VectraMarkupDialect.Lettera, Color.white, 1f, false, false));
            Assert.AreEqual("ab", VectraRichText.Build(e, VectraMarkupDialect.Legacy, Color.white, 1f, false, false), "uGUI Text has no such tags");
        }

        [Test]
        public void Markup_Offsets_AreForTextMeshPro()
        {
            var e = new SvgElementData
            {
                isText = true, text = "ab", fontSize = 10f, textColor = Color.white,
                runs = { new SvgTextRun { text = "ab", fontSize = 10f, color = Color.white, dx = 4f, dy = -3f } }
            };
            Assert.AreEqual("<space=8><voffset=6>ab</voffset>", VectraRichText.Build(e, VectraMarkupDialect.TextMeshPro, Color.white, 2f, false, false));
            Assert.AreEqual("ab", VectraRichText.Build(e, VectraMarkupDialect.Lettera, Color.white, 2f, false, false));
        }

        [Test]
        public void HasStyledRuns_IsTrueForASingleSpacedOrUnderlinedRun()
        {
            Assert.IsTrue(VectraRichText.HasStyledRuns(Spaced(2f)));
            Assert.IsTrue(VectraRichText.HasStyledRuns(Spaced(0f, true)));
            Assert.IsFalse(VectraRichText.HasStyledRuns(Spaced(0f)));
        }
    }
}
