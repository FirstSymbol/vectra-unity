using System.Collections.Generic;
using System.IO;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>SVG pattern paints: Figma image fills (fill, tile) and general tiles.</summary>
    public class SvgPatternTests
    {
        const string Tiles =
            "<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100' viewBox='0 0 100 100'>" +
            "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='20' height='20'><rect width='10' height='10' fill='#ff0000'/></pattern></defs>" +
            "<rect width='100' height='100' fill='url(#p)'/></svg>";

        readonly List<Object> m_Destroy = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in m_Destroy) if (o != null) Object.DestroyImmediate(o);
            m_Destroy.Clear();
        }

        VectraSvgDocument Parse(string svg)
        {
            var d = SvgParser.Parse(svg);
            m_Destroy.Add(d);
            return d;
        }

        static string Corpus(string file)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VectraShape).Assembly);
            if (info == null) Assert.Ignore("Vectra is a folder under Assets in this project: the corpus is read from the package folder");
            return File.ReadAllText(Path.Combine(info.resolvedPath, "Tests", "FigmaCorpus~", file));
        }

        static Color32 Pixel(Texture2D t, float x, float yFromTop) => t.GetPixel((int)x, t.height - 1 - (int)yFromTop);

        [Test]
        public void ImageFill_IsATexturePaintOnTheImageItself()
        {
            var doc = Parse(Corpus("11_image_fill.svg"));
            var fill = doc.elements.Find(e => e.fill != null && e.fill.type == PaintType.Texture).fill;
            Assert.AreEqual(TextureFit.Tile, fill.textureFit);
            Assert.IsTrue(doc.patterns[fill.textureSource].IsImage, "a tile that is one whole image uses the image, not a drawn tile");
            // document (20,20)..(140,140) is the element box = the tile: texture v runs up
            var a = fill.customMapping.Apply(new float2(20, 20));
            var b = fill.customMapping.Apply(new float2(140, 140));
            Assert.AreEqual(0f, a.x, 1e-3f);
            Assert.AreEqual(1f, a.y, 1e-3f);
            Assert.AreEqual(1f, b.x, 1e-3f);
            Assert.AreEqual(0f, b.y, 1e-3f);
            Assert.IsFalse(doc.warnings.Exists(w => w.Contains("Pattern")), string.Join("|", doc.warnings));
        }

        [Test]
        public void TiledImageFill_RepeatsEverySixthOfTheBox()
        {
            var doc = Parse(Corpus("12_image_fill_tile.svg"));
            var fill = doc.elements.Find(e => e.fill != null && e.fill.type == PaintType.Texture).fill;
            Assert.IsTrue(doc.patterns[fill.textureSource].IsImage);
            var m = fill.customMapping;
            Assert.AreEqual(1f, m.Apply(new float2(40, 20)).x, 1e-3f, "one tile is 20 units wide");
            Assert.AreEqual(1f, m.Apply(new float2(20, 20)).y, 1e-3f);
        }

        [Test]
        public void GeneralTile_GetsItsSizeAndPixelDensity()
        {
            var doc = Parse(Tiles);
            var fill = doc.elements[0].fill;
            Assert.AreEqual(PaintType.Texture, fill.type);
            var data = doc.patterns[fill.textureSource];
            Assert.IsFalse(data.IsImage);
            Assert.AreEqual(new Vector2(20, 20), data.tileSize);
            Assert.AreEqual(80, data.pixelWidth);
            Assert.AreEqual(1, data.elements.Count);
        }

        [Test]
        public void PatternTransform_RotatesTheMapping()
        {
            var doc = Parse(Tiles.Replace("width='20'", "patternTransform='rotate(90)' width='20'"));
            var m = doc.elements[0].fill.customMapping;
            // tile x axis now points down: one tile along +y of the document is one tile in u
            Assert.AreEqual(1f, math.length(m.ApplyVector(new float2(0, 20))), 1e-3f);
            Assert.AreEqual(0f, m.ApplyVector(new float2(20, 0)).x, 1e-3f);
        }

        [Test]
        public void HrefInheritance_TakesContentAndSizeFromTheReferencedPattern()
        {
            const string svg =
                "<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='100' height='100'>" +
                "<defs><pattern id='base' patternUnits='userSpaceOnUse' width='25' height='25'><rect width='5' height='5'/></pattern>" +
                "<pattern id='p' xlink:href='#base'/></defs><rect width='100' height='100' fill='url(#p)'/></svg>";
            var doc = Parse(svg);
            Assert.AreEqual(new Vector2(25, 25), doc.patterns[doc.elements[0].fill.textureSource].tileSize);
        }

        [Test]
        public void PatternOnStroke_IsATexturePaint()
        {
            var doc = Parse(Tiles.Replace("fill='url(#p)'", "fill='none' stroke='url(#p)' stroke-width='10'"));
            Assert.AreEqual(PaintType.Texture, doc.elements[0].stroke.paint.type);
        }

        [Test]
        public void DifferentTextures_StartANewMesh()
        {
            var svg = Tiles.Replace("</defs>", "<pattern id='q' patternUnits='userSpaceOnUse' width='10' height='10'><circle cx='5' cy='5' r='4'/></pattern></defs>")
                .Replace("</svg>", "<rect width='50' height='50' fill='url(#q)'/></svg>");
            var doc = Parse(svg);
            var items = new List<SvgDrawItem>();
            SvgDrawOrder.Split(doc, false, false, items);
            Assert.AreEqual(2, items.Count);
        }

        [Test]
        public void TiledPattern_RendersRepeatedTiles()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            var doc = Parse(Tiles);
            var t = VectraRasterizer.Render(doc, 100, 100);
            m_Destroy.Add(t);
            foreach (var (x, y) in new[] { (5, 5), (25, 5), (65, 45), (85, 85) })
            {
                var c = Pixel(t, x, y);
                Assert.Greater(c.r, 200, $"red square at {x},{y}");
                Assert.Less(c.g, 60);
                Assert.AreEqual(255, c.a);
            }

            foreach (var (x, y) in new[] { (15, 5), (5, 15), (35, 35), (95, 95) })
                Assert.Less(Pixel(t, x, y).a, 10, $"empty part of the tile at {x},{y}");
        }

        [Test]
        public void ImageFill_RendersThePicture_AndTexturesGoWithTheDocument()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            var doc = Parse(Corpus("11_image_fill.svg"));
            var t = VectraRasterizer.Render(doc, 160, 160);
            m_Destroy.Add(t);
            var hexagon = Pixel(t, 52, 50);
            Assert.Greater(hexagon.g, hexagon.r + 40, "the teal hexagon of the picture");
            Assert.AreEqual(255, Pixel(t, 5, 5).r, "outside the box: the white background");

            var texture = doc.patternTextures[doc.elements.Find(e => e.fill.type == PaintType.Texture).fill.textureSource];
            Assert.IsTrue(texture != null);
            Object.DestroyImmediate(doc);
            Assert.IsTrue(texture == null, "the document destroys its textures");
        }

        [Test]
        public void Reparse_ReleasesTheOldTable()
        {
            var doc = Parse(Tiles);
            Assert.AreEqual(1, doc.patterns.Count);
            SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='5' height='5'/></svg>", doc);
            Assert.AreEqual(0, doc.patterns.Count);
        }
    }
}
