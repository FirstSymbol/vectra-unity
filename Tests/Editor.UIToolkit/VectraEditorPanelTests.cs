using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit.Tests
{
    /// <summary>UXML, the UI Builder entry and an editor window panel.</summary>
    public class VectraEditorPanelTests
    {
        const string Dir = "Assets/VectraUitkEditorTmp";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        }

        [Test]
        public void ElementsAreUxmlElementsSoUiBuilderListsThem()
        {
            Assert.IsTrue(typeof(VectraElement).IsDefined(typeof(UxmlElementAttribute), false));
            Assert.IsTrue(typeof(VectraSvgElement).IsDefined(typeof(UxmlElementAttribute), false));
            Assert.IsTrue(typeof(VectraElement).IsPublic && typeof(VectraSvgElement).IsPublic);
        }

        [Test]
        public void UxmlAttributesReachTheElement()
        {
            Directory.CreateDirectory(Dir);
            const string path = Dir + "/t.uxml";
            File.WriteAllText(path,
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:vf=\"FirstSymbol.Vectra.UIToolkit\">" +
                "<vf:VectraElement name=\"a\" shape=\"Star\" sides=\"7\" fill=\"#ffcc00\" stroke=\"#112233\" stroke-width=\"3\" corner-radius=\"2\" />" +
                "<vf:VectraSvgElement name=\"b\" preserve-aspect=\"false\" quality=\"Medium\" generate-text=\"false\" />" +
                "</ui:UXML>");
            AssetDatabase.ImportAsset(path);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.IsNotNull(tree, "the UXML imported");
            var root = tree.Instantiate();
            var a = root.Q<VectraElement>("a");
            var b = root.Q<VectraSvgElement>("b");
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.AreEqual(VectraElementShape.Star, a.Shape);
            Assert.AreEqual(7, a.Sides);
            Assert.AreEqual(3f, a.StrokeWidth);
            Assert.AreEqual(2f, a.CornerRadius);
            Assert.AreEqual(1f, a.Fill.a);
            Assert.AreEqual(0xff / 255f, a.Fill.r, 1e-3f);
            Assert.AreEqual(0x11 / 255f, a.Stroke.r, 1e-3f);
            Assert.IsFalse(b.PreserveAspect);
            Assert.IsFalse(b.GenerateText);
            Assert.AreEqual(Rendering.VectraQuality.Medium, b.Quality);
        }

        [Test]
        public void ShapeAttributeKeepsCustomGeometry()
        {
            var e = new VectraElement();
            e.SetGeometry<SpiralShape>();
            Assert.AreEqual(VectraElementShape.Custom, e.Shape);
            e.Shape = VectraElementShape.Custom;
            Assert.IsInstanceOf<SpiralShape>(e.Geometry, "Custom does not replace code geometry");
            e.Shape = VectraElementShape.Ellipse;
            Assert.IsInstanceOf<EllipseShape>(e.Geometry);
        }

        public class TestWindow : EditorWindow
        {
            public VectraElement shape;

            void CreateGUI()
            {
                shape = new VectraElement { Fill = Color.red, CornerRadius = 0 };
                shape.style.position = Position.Absolute;
                shape.style.left = 10;
                shape.style.top = 10;
                shape.style.width = 60;
                shape.style.height = 60;
                rootVisualElement.Add(shape);
            }
        }

        [UnityTest]
        public IEnumerator DrawsInAnEditorWindowPanel()
        {
            var w = EditorWindow.CreateWindow<TestWindow>();
            w.position = new Rect(-3000, -3000, 200, 200);
            w.Show();
            for (int i = 0; i < 20 && (w.shape == null || w.shape.RasterCount == 0); i++)
            {
                w.Repaint();
                yield return null;
            }

            try
            {
                Assert.IsNotNull(w.shape);
                Assert.GreaterOrEqual(w.shape.RasterCount, 1, "the editor panel ran the scheduled rasterization");
                Assert.IsNotNull(w.shape.SurfaceTexture);
                Assert.GreaterOrEqual(w.shape.SurfaceSize.x, 60);
            }
            finally
            {
                w.Close();
            }
        }
    }
}
