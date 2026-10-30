using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class SvgTests
    {
        [TestCase("#f00", 1f, 0f, 0f, 1f)]
        [TestCase("#00ff0080", 0f, 1f, 0f, 0.502f)]
        [TestCase("rgb(0, 0, 255)", 0f, 0f, 1f, 1f)]
        [TestCase("rgba(255,255,255,0.5)", 1f, 1f, 1f, 0.5f)]
        [TestCase("hsl(120, 100%, 50%)", 0f, 1f, 0f, 1f)]
        [TestCase("cornflowerblue", 0.392f, 0.584f, 0.929f, 1f)]
        [TestCase("transparent", 0f, 0f, 0f, 0f)]
        public void Colors(string s, float r, float g, float b, float a)
        {
            Assert.IsTrue(SvgColor.TryParse(s, out var c));
            Assert.AreEqual(r, c.r, 0.01f);
            Assert.AreEqual(g, c.g, 0.01f);
            Assert.AreEqual(b, c.b, 0.01f);
            Assert.AreEqual(a, c.a, 0.01f);
        }

        [Test]
        public void Transforms_Compose()
        {
            var m = SvgParser.ParseTransform("translate(10, 20) scale(2) rotate(90)");
            var p = m.Apply(new float2(1, 0));
            Assert.AreEqual(10f, p.x, 1e-4f);
            Assert.AreEqual(22f, p.y, 1e-4f);
        }

        [Test]
        public void PathData_AllCommands()
        {
            var path = new NativePath(Allocator.Temp);
            var b = new PathBuilder(path);
            Assert.IsTrue(SvgPathParser.Parse("M10 10 h20 v20 H10 Z m5,5 l5-5 c1 1 2 2 3 3 s4 4 5 5 q1 1 2 2 t3 3 a5 5 0 1 0 10 0 z", b));
            Assert.Greater(path.verbs.Length, 12);
            Assert.IsTrue(SvgPathParser.Parse("M1.5.5L-1-2", b), "compact numbers");
            path.Dispose();
        }

        [Test]
        public void Css_SpecificityAndInheritance()
        {
            const string svg = @"<svg viewBox='0 0 10 10'><style>rect{fill:red} .a{fill:green} #b{fill:blue}</style>
                <g fill='yellow'><rect class='a' width='1' height='1'/><rect id='b' class='a' width='1' height='1'/>
                <rect width='1' height='1' style='fill:#fff'/><circle r='1'/></g></svg>";
            var doc = SvgParser.Parse(svg);
            Assert.AreEqual(4, doc.elements.Count);
            Assert.AreEqual(new Color(0f, 128f / 255f, 0f), doc.elements[0].fill.color); // CSS green is #008000
            Assert.AreEqual(Color.blue, doc.elements[1].fill.color);
            Assert.AreEqual(Color.white, doc.elements[2].fill.color);
            Assert.AreEqual(new Color(1f, 1f, 0f), doc.elements[3].fill.color); // inherited from the group
            Object.DestroyImmediate(doc);
        }

        [Test]
        public void Gradients_UseAndClip()
        {
            const string svg = @"<svg width='100' height='50' viewBox='0 0 200 100'>
                <defs><linearGradient id='g'><stop offset='0' stop-color='#000'/><stop offset='1' stop-color='#fff'/></linearGradient>
                <radialGradient id='r' href='#g' fx='0.2'/><clipPath id='c'><rect width='50' height='50'/></clipPath>
                <symbol id='s' viewBox='0 0 10 10'><circle cx='5' cy='5' r='5'/></symbol></defs>
                <rect width='10' height='10' fill='url(#g)'/><rect width='10' height='10' fill='url(#r)' clip-path='url(#c)'/>
                <use href='#s' x='20' width='20' height='20' fill='red'/><text x='1' y='9'>Hi</text></svg>";
            var doc = SvgParser.Parse(svg);
            Assert.AreEqual(new Rect(0, 0, 200, 100), doc.viewBox);
            Assert.AreEqual(new Vector2(100, 50), doc.size);
            Assert.AreEqual(PaintType.LinearGradient, doc.elements[0].fill.type);
            Assert.AreEqual(2, doc.elements[0].fill.gradient.stops.Count);
            Assert.AreEqual(PaintType.RadialGradient, doc.elements[1].fill.type);
            Assert.AreEqual(1, doc.elements[1].clips.Count);
            Assert.AreEqual(Color.red, doc.elements[2].fill.color);
            Assert.IsTrue(doc.elements[3].isText);
            Assert.AreEqual("Hi", doc.elements[3].text);
            Object.DestroyImmediate(doc);
        }

        [Test]
        public void Writer_RoundTrip()
        {
            var content = new VectraShapeContent();
            content.strokes.Add(new StrokeLayer(Color.black, 2) { dashed = true });
            string svg = SvgWriter.Write(content, new Rect(-50, -50, 100, 100));
            var doc = SvgParser.Parse(svg);
            Assert.AreEqual(2, doc.elements.Count);
            Assert.IsTrue(doc.elements[1].hasStroke);
            Assert.IsTrue(doc.elements[1].stroke.dashed);
            Object.DestroyImmediate(doc);
        }
    }
}
