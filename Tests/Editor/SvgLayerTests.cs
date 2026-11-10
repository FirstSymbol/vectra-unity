using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>Which parts of an SVG become compositing layers (group opacity, masks, blend modes, background blur), and how they are cut into draw steps.</summary>
    public class SvgLayerTests
    {
        readonly List<Object> m_Owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in m_Owned)
                if (o != null) Object.DestroyImmediate(o);
            m_Owned.Clear();
        }

        VectraSvgDocument Parse(string body)
        {
            var doc = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'>" + body + "</svg>");
            m_Owned.Add(doc);
            return doc;
        }

        const string TwoRects = "<rect width='20' height='20' fill='#f00'/><rect x='10' width='20' height='20' fill='#00f'/>";

        [Test]
        public void GroupOpacity_WithTwoShapes_IsOneLayer_AndThePaintsKeepTheirOpacity()
        {
            var doc = Parse("<g opacity='0.5'>" + TwoRects + "</g>");
            Assert.AreEqual(1, doc.layers.Count);
            var l = doc.layers[0];
            Assert.AreEqual(0.5f, l.opacity, 1e-4f);
            Assert.AreEqual(0, l.from);
            Assert.AreEqual(2, l.to);
            Assert.AreEqual(-1, l.parent);
            Assert.AreEqual(SvgBlendMode.Normal, l.blend);
            Assert.AreEqual(SvgMaskKind.None, l.mask);
            Assert.AreEqual(1f, doc.elements[0].fill.opacity, 1e-4f, "the opacity is applied once, by the layer");
            Assert.IsFalse(l.NeedsBackdrop);
        }

        [Test]
        public void ShapeOpacity_WithOnlyAFill_GoesIntoThePaint()
        {
            var doc = Parse("<rect width='20' height='20' fill='#f00' opacity='0.5'/>");
            Assert.AreEqual(0, doc.layers.Count);
            Assert.AreEqual(0.5f, doc.elements[0].fill.opacity, 1e-4f);
        }

        [Test]
        public void GroupOpacity_WithOneShape_GoesIntoThePaint()
        {
            var doc = Parse("<g opacity='0.5'><rect width='20' height='20' fill='#f00'/></g>");
            Assert.AreEqual(0, doc.layers.Count);
            Assert.AreEqual(0.5f, doc.elements[0].fill.opacity, 1e-4f);
        }

        [Test]
        public void ShapeOpacity_WithFillAndStroke_IsALayer()
        {
            var doc = Parse("<circle cx='50' cy='50' r='20' fill='#f00' stroke='#00f' stroke-width='8' opacity='0.5'/>");
            Assert.AreEqual(1, doc.layers.Count);
            Assert.AreEqual(0.5f, doc.layers[0].opacity, 1e-4f);
            Assert.AreEqual(1f, doc.elements[0].fill.opacity, 1e-4f);
            Assert.AreEqual(1f, doc.elements[0].stroke.paint.opacity, 1e-4f);
        }

        [Test]
        public void FullOpacity_NeverMakesALayer()
        {
            var doc = Parse("<g opacity='1'>" + TwoRects + "</g><g>" + TwoRects + "</g>");
            Assert.AreEqual(0, doc.layers.Count);
        }

        [Test]
        public void NestedGroups_AreNestedLayers_InnerFirst()
        {
            var doc = Parse("<g opacity='0.5'><rect width='5' height='5'/><g opacity='0.5'>" + TwoRects + "</g></g>");
            Assert.AreEqual(2, doc.layers.Count);
            Assert.AreEqual(1, doc.layers[0].parent, "the inner layer sits in the outer one");
            Assert.AreEqual(-1, doc.layers[1].parent);
            Assert.AreEqual(1, doc.layers[0].from);
            Assert.AreEqual(3, doc.layers[0].to);
            Assert.AreEqual(0, doc.layers[1].from);
            Assert.AreEqual(3, doc.layers[1].to);
        }

        [Test]
        public void HostileNesting_IsLimited_AndReported()
        {
            string open = string.Concat(Enumerable.Repeat("<g opacity='0.9'>", 20));
            string close = string.Concat(Enumerable.Repeat("</g>", 20));
            var doc = Parse(open + TwoRects + close);
            Assert.AreEqual(8, doc.layers.Count, "eight nested layers at most");
            Assert.IsTrue(doc.warnings.Any(w => w.Contains("layers nested deeper")), string.Join(" | ", doc.warnings));
            var wide = Parse(string.Concat(Enumerable.Repeat("<g opacity='0.5'>" + TwoRects + "</g>", 300)));
            Assert.AreEqual(256, wide.layers.Count, "256 layers at most");
        }

        [Test]
        public void ElementWithoutPaint_MakesNoLayer()
        {
            var doc = Parse("<g opacity='0.5'><rect width='5' height='5' fill='none'/></g>");
            Assert.AreEqual(0, doc.layers.Count);
        }

        // ---- masks ----

        const string Gradient = "<defs><linearGradient id='g'><stop stop-color='#fff'/><stop offset='1' stop-color='#000'/></linearGradient></defs>";

        [Test]
        public void WhiteShapeMask_StaysAGeometricClip()
        {
            var doc = Parse("<mask id='m' fill='#fff'><circle cx='50' cy='50' r='30'/></mask><g mask='url(#m)'>" + TwoRects + "</g>");
            Assert.AreEqual(0, doc.layers.Count);
            Assert.AreEqual(1, doc.elements[0].clips.Count, "the mask became a clip path");
            Assert.AreEqual(0, doc.maskElements.Count);
        }

        [Test]
        public void AlphaMask_OfAnOpaqueShape_StaysAGeometricClip_WhateverItsColour()
        {
            var doc = Parse("<mask id='m' style='mask-type:alpha'><circle cx='50' cy='50' r='30' fill='#000'/></mask><g mask='url(#m)'>" + TwoRects + "</g>");
            Assert.AreEqual(0, doc.layers.Count);
            Assert.AreEqual(1, doc.elements[0].clips.Count);
        }

        [Test]
        public void LuminanceMask_WithAGradient_IsALayer()
        {
            var doc = Parse(Gradient + "<mask id='m'><rect width='100' height='100' fill='url(#g)'/></mask><g mask='url(#m)'>" + TwoRects + "</g>");
            Assert.AreEqual(1, doc.layers.Count);
            var l = doc.layers[0];
            Assert.AreEqual(SvgMaskKind.Luminance, l.mask);
            Assert.AreEqual(1, l.maskTo - l.maskFrom);
            Assert.AreEqual(1, doc.maskElements.Count);
            Assert.AreEqual(0, doc.elements[0].clips.Count, "the mask is not a clip any more");
            Assert.IsFalse(doc.warnings.Any(w => w.Contains("geometric")), string.Join(" | ", doc.warnings));
        }

        [Test]
        public void MaskType_FromTheAttributeAndFromTheStyle_IsAlpha()
        {
            foreach (var tag in new[] { "<mask id='m' mask-type='alpha'>", "<mask id='m' style='mask-type:alpha'>" })
            {
                var doc = Parse(Gradient + tag + "<rect width='100' height='100' fill='url(#g)'/></mask><g mask='url(#m)'>" + TwoRects + "</g>");
                Assert.AreEqual(1, doc.layers.Count, tag);
                Assert.AreEqual(SvgMaskKind.Alpha, doc.layers[0].mask, tag);
            }
        }

        [Test]
        public void GrayMask_IsALayer_ButAnOpaqueGrayAlphaMask_IsNot()
        {
            string gray = "<rect width='100' height='100' fill='#808080'/></mask><g mask='url(#m)'>" + TwoRects + "</g>";
            Assert.AreEqual(1, Parse("<mask id='m'>" + gray).layers.Count, "luminance 0.5 is not a plain shape");
            Assert.AreEqual(0, Parse("<mask id='m' style='mask-type:alpha'>" + gray).layers.Count, "alpha is 1");
            string half = "<rect width='100' height='100' fill='#fff' fill-opacity='0.5'/></mask><g mask='url(#m)'>" + TwoRects + "</g>";
            Assert.AreEqual(1, Parse("<mask id='m' style='mask-type:alpha'>" + half).layers.Count, "alpha 0.5");
        }

        [Test]
        public void MaskRegion_InUserSpace_ClipsTheMaskContent()
        {
            var doc = Parse(Gradient + "<mask id='m' maskUnits='userSpaceOnUse' x='10' y='10' width='50' height='50'><rect width='100' height='100' fill='url(#g)'/></mask><g mask='url(#m)'>" + TwoRects + "</g>");
            Assert.AreEqual(1, doc.maskElements.Count);
            Assert.AreEqual(1, doc.maskElements[0].clips.Count, "the region is a clip of the mask content");
            var b = doc.maskElements[0].clips[0].ControlBounds();
            Assert.AreEqual(10f, b.min.x, 1e-3f);
            Assert.AreEqual(60f, b.max.y, 1e-3f);
        }

        [Test]
        public void MaskAndOpacity_OnTheSameGroup_AreOneLayer()
        {
            var doc = Parse(Gradient + "<mask id='m'><rect width='100' height='100' fill='url(#g)'/></mask><g mask='url(#m)' opacity='0.5'>" + TwoRects + "</g>");
            Assert.AreEqual(1, doc.layers.Count);
            Assert.AreEqual(0.5f, doc.layers[0].opacity, 1e-4f);
            Assert.AreEqual(SvgMaskKind.Luminance, doc.layers[0].mask);
        }

        // ---- blend modes ----

        [TestCase("multiply", SvgBlendMode.Multiply)]
        [TestCase("screen", SvgBlendMode.Screen)]
        [TestCase("overlay", SvgBlendMode.Overlay)]
        [TestCase("darken", SvgBlendMode.Darken)]
        [TestCase("lighten", SvgBlendMode.Lighten)]
        [TestCase("color-dodge", SvgBlendMode.ColorDodge)]
        [TestCase("color-burn", SvgBlendMode.ColorBurn)]
        [TestCase("hard-light", SvgBlendMode.HardLight)]
        [TestCase("soft-light", SvgBlendMode.SoftLight)]
        [TestCase("difference", SvgBlendMode.Difference)]
        [TestCase("exclusion", SvgBlendMode.Exclusion)]
        [TestCase("hue", SvgBlendMode.Hue)]
        [TestCase("saturation", SvgBlendMode.Saturation)]
        [TestCase("color", SvgBlendMode.Color)]
        [TestCase("luminosity", SvgBlendMode.Luminosity)]
        [TestCase("plus-lighter", SvgBlendMode.PlusLighter)]
        public void MixBlendMode_OnAShape_IsALayerThatReadsItsBackdrop(string css, SvgBlendMode mode)
        {
            var doc = Parse("<rect width='50' height='50' fill='#888'/><rect width='20' height='20' fill='#f00' style='mix-blend-mode:" + css + "'/>");
            Assert.AreEqual(1, doc.layers.Count);
            Assert.AreEqual(mode, doc.layers[0].blend);
            Assert.AreEqual(1, doc.layers[0].from);
            Assert.IsTrue(doc.layers[0].NeedsBackdrop);
            Assert.IsFalse(doc.warnings.Any(w => w.Contains("mix-blend-mode")), string.Join(" | ", doc.warnings));
        }

        [Test]
        public void MixBlendMode_Normal_OrUnknown_IsNoLayer()
        {
            Assert.AreEqual(0, Parse("<rect width='20' height='20' style='mix-blend-mode:normal'/>").layers.Count);
            Assert.AreEqual(0, Parse("<rect width='20' height='20' style='mix-blend-mode:nonsense'/>").layers.Count);
        }

        [Test]
        public void MixBlendMode_IsNotInherited()
        {
            var doc = Parse("<g style='mix-blend-mode:multiply'>" + TwoRects + "</g>");
            Assert.AreEqual(1, doc.layers.Count, "one layer for the group, not one per child");
            Assert.AreEqual(2, doc.layers[0].to - doc.layers[0].from);
        }

        // ---- background blur (Figma) ----

        [Test]
        public void FigmaBackgroundBlur_IsALayerWithTheShapeAsItsSilhouette()
        {
            var doc = Parse("<rect width='100' height='100' fill='#f00'/>" +
                            "<foreignObject x='18' y='18' width='64' height='64'><div xmlns='http://www.w3.org/1999/xhtml' style='backdrop-filter:blur(6px);height:100%;width:100%'></div></foreignObject>" +
                            "<rect data-figma-bg-blur-radius='12' x='20' y='20' width='60' height='60' rx='8' fill='#fff' fill-opacity='0.3'/>");
            Assert.AreEqual(1, doc.layers.Count);
            var l = doc.layers[0];
            Assert.AreEqual(6f, l.blurSigma, 1e-4f, "Figma's radius is twice the standard deviation");
            Assert.AreEqual(SvgMaskKind.Alpha, l.mask);
            Assert.AreEqual(1, l.maskTo - l.maskFrom);
            Assert.AreEqual(1, l.from);
            Assert.AreEqual(2, l.to);
            Assert.IsTrue(l.NeedsBackdrop);
        }

        [Test]
        public void ForeignObject_IsReported_ExceptFiguresBackgroundBlurAndSkipParseGroups()
        {
            var plain = Parse("<foreignObject width='9' height='9'><div xmlns='http://www.w3.org/1999/xhtml'>x</div></foreignObject>");
            Assert.IsTrue(plain.warnings.Any(w => w.Contains("foreignObject")), string.Join(" | ", plain.warnings));
            var blur = Parse("<foreignObject width='9' height='9'><div xmlns='http://www.w3.org/1999/xhtml' style='backdrop-filter:blur(6px)'></div></foreignObject>");
            Assert.IsFalse(blur.warnings.Any(w => w.Contains("foreignObject")), string.Join(" | ", blur.warnings));
            var skip = Parse("<g data-figma-skip-parse='true'><foreignObject width='9' height='9'><div xmlns='http://www.w3.org/1999/xhtml'>x</div></foreignObject></g>");
            Assert.IsFalse(skip.warnings.Any(w => w.Contains("foreignObject")), string.Join(" | ", skip.warnings));
        }

        [Test]
        public void TwoElementsFromOneShape_StayInsideTheLayer()
        {
            // The fill and the stroke of a shape may become two elements (patterns): the layer covers both.
            var doc = Parse("<g opacity='0.5'><rect width='20' height='20' fill='#f00' stroke='#00f' stroke-width='4'/><rect x='5' width='20' height='20'/></g>");
            Assert.AreEqual(1, doc.layers.Count);
            Assert.AreEqual(doc.elements.Count, doc.layers[0].to);
        }

        // ---- draw steps ----

        static List<SvgDrawItem> Steps(VectraSvgDocument doc, bool text = true)
        {
            var items = new List<SvgDrawItem>();
            SvgDrawOrder.Split(doc, text, true, items);
            return items;
        }

        [Test]
        public void ADocumentWithoutLayers_HasNoCompositeStep()
        {
            var items = Steps(Parse(TwoRects));
            Assert.AreEqual(1, items.Count);
            Assert.IsFalse(items[0].overlay);
            Assert.IsFalse(items[0].composite);
        }

        [Test]
        public void ALayer_IsACompositeStep_BetweenMeshRuns()
        {
            var doc = Parse("<rect width='5' height='5'/><g opacity='0.5'>" + TwoRects + "</g><rect width='9' height='9'/>");
            var items = Steps(doc);
            Assert.AreEqual(3, items.Count);
            Assert.IsFalse(items[0].overlay);
            Assert.AreEqual((0, 1), (items[0].from, items[0].to));
            Assert.IsTrue(items[1].composite);
            Assert.IsTrue(items[1].overlay, "code that only knows meshes, texts and images skips it");
            Assert.AreEqual((1, 3), (items[1].from, items[1].to));
            Assert.IsFalse(items[2].overlay);
            Assert.AreEqual((3, 4), (items[2].from, items[2].to));
        }

        [Test]
        public void ABlendLayer_TakesEverythingBeforeIt_IntoItsTexture()
        {
            var doc = Parse(TwoRects + "<g opacity='0.5'>" + TwoRects + "</g><rect width='5' height='5' fill='#f00' style='mix-blend-mode:multiply'/><rect width='9' height='9'/>");
            var items = Steps(doc);
            Assert.AreEqual(2, items.Count, string.Join(", ", items.Select(i => $"[{i.from},{i.to}{(i.composite ? "c" : "")}]")));
            Assert.IsTrue(items[0].composite);
            Assert.AreEqual((0, 5), (items[0].from, items[0].to));
            Assert.IsFalse(items[1].overlay);
            Assert.AreEqual((5, 6), (items[1].from, items[1].to));
        }

        [Test]
        public void ABlendLayer_DoesNotReachBackBeyondAText()
        {
            var doc = Parse("<rect width='9' height='9'/><text x='1' y='9'>T</text><rect width='5' height='5' fill='#f00' style='mix-blend-mode:multiply'/>");
            var items = Steps(doc);
            Assert.AreEqual(3, items.Count);
            Assert.IsFalse(items[0].overlay);
            Assert.IsTrue(items[1].overlay && !items[1].composite, "the text");
            Assert.IsTrue(items[2].composite);
            Assert.AreEqual((2, 3), (items[2].from, items[2].to));
        }

        [Test]
        public void ATextInsideALayer_IsDrawnAfterTheLayerTexture()
        {
            var doc = Parse("<g opacity='0.5'>" + TwoRects + "<text x='1' y='9'>T</text></g>");
            var items = Steps(doc);
            Assert.AreEqual(2, items.Count);
            Assert.IsTrue(items[0].composite);
            Assert.IsTrue(items[1].overlay && !items[1].composite);
            Assert.IsTrue(doc.elements[items[1].from].isText);
        }

        [Test]
        public void TextInsideAGroupWithOpacity_KeepsItsOpacity()
        {
            var doc = Parse("<g opacity='0.5'>" + TwoRects + "<text x='1' y='9' fill='#000'>T</text></g>");
            var text = doc.elements.First(e => e.isText);
            Assert.AreEqual(0.5f, text.runs.Count > 0 ? text.runs[0].color.a : text.textColor.a, 1e-3f);
        }

        // ---- the document ----

        [Test]
        public void ParsingAgain_ReplacesTheLayers()
        {
            var doc = Parse("<g opacity='0.5'>" + TwoRects + "</g>");
            Assert.AreEqual(1, doc.layers.Count);
            SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 10 10'><rect width='5' height='5'/></svg>", doc);
            Assert.AreEqual(0, doc.layers.Count);
            Assert.AreEqual(0, doc.maskElements.Count);
        }

        [Test]
        public void Layers_SurviveSerialization()
        {
            var doc = Parse(Gradient + "<mask id='m'><rect width='100' height='100' fill='url(#g)'/></mask><g mask='url(#m)' opacity='0.5' style='mix-blend-mode:screen'>" + TwoRects + "</g>");
            string json = UnityEditor.EditorJsonUtility.ToJson(doc);
            var copy = ScriptableObject.CreateInstance<VectraSvgDocument>();
            m_Owned.Add(copy);
            UnityEditor.EditorJsonUtility.FromJsonOverwrite(json, copy);
            Assert.AreEqual(1, copy.layers.Count);
            Assert.AreEqual(SvgBlendMode.Screen, copy.layers[0].blend);
            Assert.AreEqual(SvgMaskKind.Luminance, copy.layers[0].mask);
            Assert.AreEqual(0.5f, copy.layers[0].opacity, 1e-4f);
            Assert.AreEqual(1, copy.maskElements.Count);
        }
    }
}
