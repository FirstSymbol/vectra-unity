using System.IO;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>The SVG importer end to end: a file in the project, the Vectra override, what comes out.</summary>
    public class ImporterTests
    {
        const string Folder = "Assets/__VectraImporterTest";
        const string Svg =
            "<svg xmlns='http://www.w3.org/2000/svg' width='80' height='40' viewBox='0 0 80 40'>" +
            "<defs><marker id='m' markerWidth='4' markerHeight='4'><rect width='4' height='4'/></marker></defs>" +
            "<rect width='80' height='40' fill='#336699'/>" +
            "<line x1='10' y1='20' x2='70' y2='20' stroke='#fff' stroke-width='2' marker-end='url(#m)'/>" +
            "<text x='5' y='15' font-size='10'>Hi</text></svg>";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(Folder);
            Directory.CreateDirectory(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.Refresh();
        }

        static string Import(string name, string text, VectraSvgImporter.OutputMode mode)
        {
            string path = Folder + "/" + name + ".svg";
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SetImporterOverride<VectraSvgImporter>(path);
            var importer = (VectraSvgImporter)AssetImporter.GetAtPath(path);
            Assert.IsNotNull(importer, "the Vectra importer took the file");
            importer.mode = mode;
            importer.SaveAndReimport();
            return path;
        }

        [Test]
        public void AFile_ImportsAsADocument_WithItsElements()
        {
            string path = Import("doc", Svg, VectraSvgImporter.OutputMode.Document);
            var doc = AssetDatabase.LoadAssetAtPath<VectraSvgDocument>(path);
            Assert.IsNotNull(doc, "the main object is a VectraSvgDocument");
            Assert.AreEqual(new Vector2(80, 40), doc.size);
            // the background, the line, its end marker and the text
            Assert.AreEqual(4, doc.elements.Count);
            Assert.IsTrue(doc.elements[3].isText);
            Assert.AreEqual(0, doc.warnings.Count, string.Join(" | ", doc.warnings));
        }

        [Test]
        public void TheDocument_IsSavedWithTheAsset_NotRebuiltEveryTime()
        {
            string path = Import("twice", Svg, VectraSvgImporter.OutputMode.Document);
            var first = AssetDatabase.LoadAssetAtPath<VectraSvgDocument>(path);
            Assert.AreEqual(4, first.elements.Count);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var again = AssetDatabase.LoadAssetAtPath<VectraSvgDocument>(path);
            Assert.AreEqual(4, again.elements.Count);
        }

        [Test]
        public void ImportFailed_SeesAVectraSvgThatCameOutPlain_AndOnlyThat()
        {
            string good = Import("good", Svg, VectraSvgImporter.OutputMode.Document);
            Assert.IsFalse(VectraSvgImporterPostprocessor.ImportFailed(good), "a working import");
            string other = Folder + "/other.svg";
            File.WriteAllText(other, Svg);
            AssetDatabase.ImportAsset(other, ImportAssetOptions.ForceSynchronousImport);
            Assert.IsFalse(VectraSvgImporterPostprocessor.ImportFailed(other), "not Vectra's importer");
            Assert.IsFalse(VectraSvgImporterPostprocessor.ImportFailed(Folder + "/missing.svg"));
            Assert.IsFalse(VectraSvgImporterPostprocessor.ImportFailed(Folder + "/x.png"));
        }

        [Test]
        public void TextureMode_BakesAPicture()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("raster import needs a graphics device");
            string path = Import("tex", Svg, VectraSvgImporter.OutputMode.Texture);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            // The importer falls back to the document when it cannot rasterize; batch-mode import workers have no GPU to draw with.
            if (tex == null && AssetDatabase.LoadAssetAtPath<VectraSvgDocument>(path) != null) Assert.Ignore("this editor imported the file as a document: no GPU in the import worker");
            Assert.IsNotNull(tex);
            Assert.Greater(tex.width, 0);
            Assert.Greater(tex.height, 0);
            Assert.AreEqual(2f, (float)tex.width / tex.height, 0.05f, "an 80 x 40 file keeps its aspect");
        }
    }
}
