using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>The colour blending choice for SVG documents in Linear projects, and how a document is cut into steps when it is on.</summary>
    public class SvgColorBlendingTests
    {
        static bool Linear => QualitySettings.activeColorSpace == ColorSpace.Linear;

        [Test]
        public void Browser_IsTheDefault_AndOnlyLinearProjectsUseIt()
        {
            var saved = VectraColorSpace.ProjectBlending;
            try
            {
                VectraColorSpace.ProjectBlending = SvgColorBlending.Browser;
                Assert.AreEqual(Linear, VectraColorSpace.UseGamma());
                Assert.AreEqual(Linear, VectraColorSpace.UseGamma(SvgColorBlendingChoice.Browser));
                Assert.IsFalse(VectraColorSpace.UseGamma(SvgColorBlendingChoice.Linear));
                VectraColorSpace.ProjectBlending = SvgColorBlending.Linear;
                Assert.IsFalse(VectraColorSpace.UseGamma());
                Assert.AreEqual(Linear, VectraColorSpace.UseGamma(SvgColorBlendingChoice.Browser), "the component overrides the project");
            }
            finally
            {
                VectraColorSpace.ProjectBlending = saved;
            }
        }

        [Test]
        public void TheGammaScope_TurnsLinearOff_AndNests()
        {
            Assert.AreEqual(Linear, VectraColorSpace.Linear);
            using (VectraColorSpace.EnterGamma())
            {
                Assert.IsFalse(VectraColorSpace.Linear);
                Assert.AreEqual(Linear, VectraColorSpace.InGammaScope);
                Assert.IsFalse(VectraColorSpace.UseGamma(), "a nested drawing follows the outer one");
                using (VectraColorSpace.EnterGamma()) Assert.IsFalse(VectraColorSpace.Linear);
                Assert.IsFalse(VectraColorSpace.Linear);
            }

            Assert.AreEqual(Linear, VectraColorSpace.Linear);
            Assert.IsFalse(VectraColorSpace.InGammaScope);
        }

        [Test]
        public void RuntimeSettings_CarryTheChoice()
        {
            var saved = VectraColorSpace.ProjectBlending;
            var s = VectraRuntimeSettings.Create(2048, TextEngines.VectraTextEngine.Auto, SvgColorBlending.Linear);
            try
            {
                Assert.AreEqual(SvgColorBlending.Linear, VectraColorSpace.ProjectBlending, "Create applies it");
                s.SvgColorBlending = SvgColorBlending.Browser;
                Assert.AreEqual(SvgColorBlending.Browser, VectraColorSpace.ProjectBlending);
            }
            finally
            {
                Object.DestroyImmediate(s);
                VectraColorSpace.ProjectBlending = saved;
            }
        }

        static List<SvgDrawItem> Steps(string body, bool gamma)
        {
            var doc = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'>" + body + "</svg>");
            var items = new List<SvgDrawItem>();
            SvgDrawOrder.Split(doc, true, true, items, gamma);
            Object.DestroyImmediate(doc);
            return items;
        }

        const string Group = "<g opacity='0.5'><rect width='9' height='9' fill='#f00'/><rect x='5' width='9' height='9' fill='#00f'/></g>";

        [Test]
        public void WithoutGamma_ShapesStayMesh()
        {
            var items = Steps("<rect width='5' height='5'/>" + Group + "<rect width='5' height='5'/>", false);
            Assert.AreEqual(3, items.Count);
            Assert.IsFalse(items[0].overlay);
        }

        [Test]
        public void WithGamma_NeighbouringRunsAndLayersAreOneCompositeStep()
        {
            var items = Steps("<rect width='5' height='5'/>" + Group + "<rect width='5' height='5'/>", true);
            Assert.AreEqual(1, items.Count);
            Assert.IsTrue(items[0].composite && items[0].overlay);
            Assert.AreEqual((0, 4), (items[0].from, items[0].to));
        }

        [Test]
        public void WithGamma_ATextSplitsTheCompositeSteps_AndStaysAnOverlay()
        {
            var items = Steps("<rect width='5' height='5'/><text x='1' y='9'>T</text><rect width='5' height='5'/>", true);
            Assert.AreEqual(3, items.Count);
            Assert.IsTrue(items[0].composite);
            Assert.IsTrue(items[1].overlay && !items[1].composite);
            Assert.IsTrue(items[2].composite);
        }
    }
}
