using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class SvgMarkerTests
    {
        // A marker whose tip is on x = 10 and whose base is on x = 0: a 10 x 10 arrow pointing along +x, ref point at the base middle.
        const string ArrowDefs =
            "<defs><marker id='a' viewBox='0 0 10 10' refX='0' refY='5' markerWidth='5' markerHeight='5' orient='auto'>" +
            "<path d='M0 0 L10 5 L0 10 Z' fill='red'/></marker></defs>";

        static VectraSvgDocument Parse(string body, string attrs = "")
        {
            var doc = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200' " + attrs + ">" + body + "</svg>");
            Cleanup.Add(doc);
            return doc;
        }

        static readonly List<Object> Cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in Cleanup)
                if (o != null) Object.DestroyImmediate(o);
            Cleanup.Clear();
        }

        static Bounds2 BoundsOf(SvgElementData e) => e.path.ControlBounds();

        [Test]
        public void MarkerEnd_SitsAtTheEndOfALine_AndPointsAlongIt()
        {
            // stroke-width 2 and a 5 unit viewport over a 10 unit viewBox: one viewBox unit is one user unit.
            var doc = Parse(ArrowDefs + "<line x1='20' y1='50' x2='100' y2='50' stroke='black' stroke-width='2' marker-end='url(#a)'/>");
            Assert.AreEqual(2, doc.elements.Count, string.Join(" | ", doc.warnings));
            var b = BoundsOf(doc.elements[1]);
            Assert.AreEqual(100f, b.min.x, 0.01f, "the base is on the line end");
            Assert.AreEqual(110f, b.max.x, 0.01f, "the tip is 10 units further along");
            Assert.AreEqual(45f, b.min.y, 0.01f);
            Assert.AreEqual(55f, b.max.y, 0.01f);
        }

        [Test]
        public void Orient_AutoFollowsTheDirectionOfTheLine()
        {
            var doc = Parse(ArrowDefs + "<line x1='50' y1='20' x2='50' y2='100' stroke='black' stroke-width='2' marker-end='url(#a)'/>");
            var b = BoundsOf(doc.elements[1]);
            Assert.AreEqual(110f, b.max.y, 0.01f, "the tip points down the page");
            Assert.AreEqual(100f, b.min.y, 0.01f);
            Assert.AreEqual(45f, b.min.x, 0.01f);
            Assert.AreEqual(55f, b.max.x, 0.01f);
        }

        [Test]
        public void Orient_AutoStartReverseTurnsTheStartMarkerAround()
        {
            const string defs = "<defs><marker id='r' viewBox='0 0 10 10' refX='0' refY='5' markerWidth='5' markerHeight='5' orient='auto-start-reverse'>" +
                                "<path d='M0 0 L10 5 L0 10 Z'/></marker></defs>";
            var doc = Parse(defs + "<line x1='50' y1='50' x2='150' y2='50' stroke='black' stroke-width='2' marker-start='url(#r)' marker-end='url(#r)'/>");
            Assert.AreEqual(3, doc.elements.Count);
            var start = BoundsOf(doc.elements[1]);
            var end = BoundsOf(doc.elements[2]);
            Assert.AreEqual(40f, start.min.x, 0.01f, "the start arrow points backwards, away from the line");
            Assert.AreEqual(50f, start.max.x, 0.01f);
            Assert.AreEqual(150f, end.min.x, 0.01f);
            Assert.AreEqual(160f, end.max.x, 0.01f);
        }

        [Test]
        public void Orient_FixedAngle()
        {
            const string defs = "<defs><marker id='f' viewBox='0 0 10 10' refX='0' refY='5' markerWidth='5' markerHeight='5' orient='90'>" +
                                "<path d='M0 0 L10 5 L0 10 Z'/></marker></defs>";
            var doc = Parse(defs + "<line x1='50' y1='50' x2='150' y2='50' stroke='black' stroke-width='2' marker-end='url(#f)'/>");
            var b = BoundsOf(doc.elements[1]);
            Assert.AreEqual(60f, b.max.y, 0.01f, "90 degrees turns the arrow to point down the page whatever the line does");
            Assert.AreEqual(145f, b.min.x, 0.01f);
            Assert.AreEqual(155f, b.max.x, 0.01f);
        }

        [Test]
        public void MarkerUnits_StrokeWidthScalesTheMarker_UserSpaceOnUseDoesNot()
        {
            const string scaled = "<defs><marker id='s' viewBox='0 0 10 10' refX='0' refY='5' markerWidth='10' markerHeight='10'>" +
                                  "<path d='M0 0 L10 5 L0 10 Z'/></marker></defs>";
            const string fixedSize = "<defs><marker id='s' viewBox='0 0 10 10' refX='0' refY='5' markerWidth='10' markerHeight='10' markerUnits='userSpaceOnUse'>" +
                                     "<path d='M0 0 L10 5 L0 10 Z'/></marker></defs>";
            const string line = "<line x1='10' y1='50' x2='100' y2='50' stroke='black' stroke-width='4' marker-end='url(#s)'/>";
            var a = Parse(scaled + line);
            var b = Parse(fixedSize + line);
            Assert.AreEqual(40f, BoundsOf(a.elements[1]).Size.x, 0.01f, "stroke width 4 times the 10 unit marker");
            Assert.AreEqual(10f, BoundsOf(b.elements[1]).Size.x, 0.01f, "user units: 10");
        }

        [Test]
        public void RefPoint_MovesTheMarkerOntoTheVertex()
        {
            const string defs = "<defs><marker id='c' viewBox='0 0 10 10' refX='5' refY='5' markerWidth='5' markerHeight='5'>" +
                                "<circle cx='5' cy='5' r='4'/></marker></defs>";
            var doc = Parse(defs + "<line x1='10' y1='50' x2='100' y2='50' stroke='black' stroke-width='2' marker-end='url(#c)'/>");
            var b = BoundsOf(doc.elements[1]);
            Assert.AreEqual(100f, (b.min.x + b.max.x) * 0.5f, 0.3f, "the circle is centred on the end of the line");
            Assert.AreEqual(50f, (b.min.y + b.max.y) * 0.5f, 0.3f);
        }

        [Test]
        public void StartMidAndEnd_OnAPolyline()
        {
            const string defs = "<defs><marker id='d' viewBox='0 0 4 4' refX='2' refY='2' markerWidth='4' markerHeight='4'><rect width='4' height='4'/></marker></defs>";
            var doc = Parse(defs + "<polyline points='10,10 60,10 60,60 110,60' fill='none' stroke='black' marker-start='url(#d)' marker-mid='url(#d)' marker-end='url(#d)'/>");
            // the polyline, then one marker at each of the 4 vertices
            Assert.AreEqual(5, doc.elements.Count);
            var centres = doc.elements.Skip(1).Select(e => (BoundsOf(e).min + BoundsOf(e).max) * 0.5f).ToList();
            Assert.That(centres.Any(c => math.distance(c, new float2(10, 10)) < 0.2f));
            Assert.That(centres.Any(c => math.distance(c, new float2(60, 10)) < 0.2f));
            Assert.That(centres.Any(c => math.distance(c, new float2(60, 60)) < 0.2f));
            Assert.That(centres.Any(c => math.distance(c, new float2(110, 60)) < 0.2f));
        }

        [Test]
        public void OnlyMid_SkipsTheFirstAndTheLastVertex()
        {
            const string defs = "<defs><marker id='m' viewBox='0 0 4 4' refX='2' refY='2' markerWidth='4' markerHeight='4'><rect width='4' height='4'/></marker></defs>";
            var doc = Parse(defs + "<polyline points='10,10 60,10 60,60 110,60' fill='none' stroke='black' marker-mid='url(#m)'/>");
            Assert.AreEqual(3, doc.elements.Count, "the polyline and its two inner vertices");
        }

        [Test]
        public void Polygon_StartAndEndMeetAtTheFirstVertex()
        {
            const string defs = "<defs><marker id='p' viewBox='0 0 4 4' refX='2' refY='2' markerWidth='4' markerHeight='4'><rect width='4' height='4'/></marker></defs>";
            var doc = Parse(defs + "<polygon points='10,10 60,10 60,60' fill='none' stroke='black' marker-start='url(#p)' marker-end='url(#p)'/>");
            Assert.AreEqual(3, doc.elements.Count);
            var a = (BoundsOf(doc.elements[1]).min + BoundsOf(doc.elements[1]).max) * 0.5f;
            var b = (BoundsOf(doc.elements[2]).min + BoundsOf(doc.elements[2]).max) * 0.5f;
            Assert.AreEqual(0f, math.distance(a, new float2(10, 10)), 0.2f);
            Assert.AreEqual(0f, math.distance(b, new float2(10, 10)), 0.2f);
        }

        [Test]
        public void MarkerShorthand_AndInheritanceFromAGroup()
        {
            const string defs = "<defs><marker id='g' viewBox='0 0 4 4' refX='2' refY='2' markerWidth='4' markerHeight='4'><rect width='4' height='4'/></marker></defs>";
            var viaStyle = Parse(defs + "<path d='M10 10 L50 10' stroke='black' style='marker: url(#g)'/>");
            Assert.AreEqual(1 + 2, viaStyle.elements.Count, "start, mid (none on a single segment), end");
            var viaGroup = Parse(defs + "<g marker-end='url(#g)'><path d='M10 10 L50 10' stroke='black'/></g>");
            Assert.AreEqual(2, viaGroup.elements.Count);
        }

        [Test]
        public void ShapesOtherThanPathLineAndPoly_DrawNoMarkers()
        {
            const string defs = "<defs><marker id='x' markerWidth='4' markerHeight='4'><rect width='4' height='4'/></marker></defs>";
            var doc = Parse(defs + "<rect x='10' y='10' width='50' height='50' marker-end='url(#x)'/>");
            Assert.AreEqual(1, doc.elements.Count);
        }

        [Test]
        public void MarkerContent_IsClippedToItsViewport_UnlessOverflowIsVisible()
        {
            const string wide = "<path d='M-20 0 L30 5 L-20 10 Z'/>";
            var clipped = Parse("<defs><marker id='o' viewBox='0 0 10 10' markerWidth='5' markerHeight='5'>" + wide + "</marker></defs>" +
                                "<line x1='10' y1='50' x2='100' y2='50' stroke='black' marker-end='url(#o)'/>");
            Assert.Greater(clipped.elements[1].clips.Count, 0, "overflow is hidden by default");
            var open = Parse("<defs><marker id='o' viewBox='0 0 10 10' markerWidth='5' markerHeight='5' overflow='visible'>" + wide + "</marker></defs>" +
                             "<line x1='10' y1='50' x2='100' y2='50' stroke='black' marker-end='url(#o)'/>");
            Assert.AreEqual(0, open.elements[1].clips.Count);
        }

        [Test]
        public void MissingMarker_IsReported()
        {
            var doc = Parse("<line x1='10' y1='50' x2='100' y2='50' stroke='black' marker-end='url(#nope)'/>");
            Assert.AreEqual(1, doc.elements.Count);
            Assert.That(doc.warnings.Any(w => w.Contains("marker") && w.Contains("not found")), string.Join(" | ", doc.warnings));
        }

        [Test]
        public void AMarkerThatUsesItself_DoesNotRecurse()
        {
            var doc = Parse("<defs><marker id='self' markerWidth='4' markerHeight='4'><path d='M0 0 L4 4' stroke='black' marker-end='url(#self)'/></marker></defs>" +
                            "<path d='M10 10 L50 10' stroke='black' marker-end='url(#self)'/>");
            Assert.Less(doc.elements.Count, 10);
        }

        [Test]
        public void MarkersFollowTheTransformOfTheElement()
        {
            var doc = Parse(ArrowDefs + "<g transform='translate(100 0) scale(2)'><line x1='0' y1='20' x2='20' y2='20' stroke='black' stroke-width='2' marker-end='url(#a)'/></g>");
            var b = BoundsOf(doc.elements[1]);
            Assert.AreEqual(140f, b.min.x, 0.01f, "end of the line at x = 20 * 2 + 100");
            Assert.AreEqual(160f, b.max.x, 0.01f, "marker size doubles too");
        }

        [Test]
        public void ManyMarkers_StayInsideTheBudget()
        {
            var points = string.Join(" ", Enumerable.Range(0, 400).Select(i => $"{i},{i % 7}"));
            var opts = new SvgParseOptions { maxUseExpansions = 50 };
            var doc = SvgParser.Parse("<svg viewBox='0 0 400 10'><defs><marker id='b' markerWidth='1' markerHeight='1'><rect width='1' height='1'/></marker></defs>" +
                                      $"<polyline points='{points}' fill='none' stroke='black' marker-mid='url(#b)'/></svg>", opts);
            Cleanup.Add(doc);
            Assert.LessOrEqual(doc.elements.Count, 52);
            Assert.That(doc.warnings.Any(w => w.Contains("limit")), string.Join(" | ", doc.warnings));
        }

        [Test]
        public void Vertices_OfAClosedTriangle()
        {
            var path = new NativePath(Allocator.Temp);
            var b = new PathBuilder(path);
            Assert.IsTrue(SvgPathParser.Parse("M0 0 L10 0 L10 10 Z", b));
            var list = new List<SvgParser.MarkerVertex>();
            SvgParser.CollectVertices(path, list);
            path.Dispose();
            Assert.AreEqual(4, list.Count, "three corners and the closing vertex");
            Assert.AreEqual(0f, math.distance(list[3].position, list[0].position), 1e-4f);
            // The first vertex: arrives along the closing segment (towards -x, -y), leaves along +x: the bisector points up-left of +x... check it is neither of the two.
            float a0 = SvgParser.MarkerAngle(list[0]);
            Assert.Greater(Mathf.Abs(a0), 0.1f);
            // The middle vertex at (10, 0) turns from +x to +y: 45 degrees.
            Assert.AreEqual(Mathf.PI / 4f, SvgParser.MarkerAngle(list[1]), 1e-3f);
        }
    }
}
