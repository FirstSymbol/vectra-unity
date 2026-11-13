using System.Collections;
using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Tests
{
    /// <summary>Identical composited SVG instances (an icon repeated in a list) share one picture, counted by users.</summary>
    public class SvgPictureSharingPlayModeTests
    {
        const string Icon = "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'><g opacity='0.5'><rect x='2' y='2' width='20' height='20' rx='4' fill='#d32f2f'/></g></svg>";
        const string Other = "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'><g opacity='0.5'><circle cx='12' cy='12' r='9' fill='#2a7'/></g></svg>";

        readonly List<Object> m_Owned = new List<Object>();
        GameObject m_Canvas;

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) Assert.Ignore("composited pictures exist only in Linear projects: a Gamma project draws with no gamma scope");
            SvgPictures.DestroyAll();
            m_Canvas = new GameObject("Sharing canvas", typeof(Canvas));
            m_Canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            m_Owned.Add(m_Canvas);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in m_Owned)
                if (o != null) Object.DestroyImmediate(o);
            m_Owned.Clear();
        }

        VectraSvgDocument Parse(string svg)
        {
            var doc = SvgParser.Parse(svg);
            m_Owned.Add(doc);
            return doc;
        }

        VectraSvgImage MakeImage(VectraSvgDocument doc, float size = 40f, string name = "Icon")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(m_Canvas.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);
            var image = go.AddComponent<VectraSvgImage>();
            image.Document = doc;
            return image;
        }

        static RenderTexture PictureOf(VectraSvgImage image) => image.GetComponentInChildren<VectraSvgComposite>()?.Texture;

        static IEnumerator Settle()
        {
            for (int i = 0; i < 3; i++)
            {
                Canvas.ForceUpdateCanvases();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator IdenticalInstances_ShareOneTextureAndOneRender()
        {
            var doc = Parse(Icon);
            int steps = SvgCompositor.StepsRendered;
            var images = new List<VectraSvgImage>();
            for (int i = 0; i < 30; i++) images.Add(MakeImage(doc));
            yield return Settle();
            var first = PictureOf(images[0]);
            Assert.IsNotNull(first, "drawn");
            foreach (var image in images) Assert.AreSame(first, PictureOf(image), "one texture");
            Assert.AreEqual(1, SvgPictures.Count);
            Assert.AreEqual(1, SvgCompositor.StepsRendered - steps, "one composite render for 30 instances");
        }

        [UnityTest]
        public IEnumerator DifferentSizeOverridesOrDocument_GetTheirOwnPicture()
        {
            var doc = Parse(Icon);
            var a = MakeImage(doc);
            var sized = MakeImage(doc, 64f);
            var overridden = MakeImage(doc);
            overridden.Overrides.Add(new SvgOverride { selector = "rect", overrideFill = true, fill = Color.blue });
            var other = MakeImage(Parse(Other));
            var same = MakeImage(doc);
            yield return Settle();
            Assert.AreSame(PictureOf(a), PictureOf(same));
            Assert.AreNotSame(PictureOf(a), PictureOf(sized));
            Assert.AreNotSame(PictureOf(a), PictureOf(overridden));
            Assert.AreNotSame(PictureOf(a), PictureOf(other));
            Assert.AreEqual(4, SvgPictures.Count);
        }

        [UnityTest]
        public IEnumerator PictureOutlivesItsFirstUser_AndGoesWithTheLast()
        {
            var doc = Parse(Icon);
            var a = MakeImage(doc);
            var b = MakeImage(doc);
            yield return Settle();
            var texture = PictureOf(a);
            Assert.AreSame(texture, PictureOf(b));
            Object.DestroyImmediate(a.gameObject);
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(texture != null && texture.IsCreated(), "the other instance still shows it");
            Assert.AreEqual(1, SvgPictures.Count);
            Object.DestroyImmediate(b.gameObject);
            Assert.AreEqual(0, SvgPictures.Count, "no user left");
            yield return null;
            Assert.IsTrue(texture == null, "the render texture was destroyed with the last user");
        }

        [UnityTest]
        public IEnumerator ResizingOneInstance_LeavesTheOthersPictureAlone()
        {
            var doc = Parse(Icon);
            var a = MakeImage(doc);
            var b = MakeImage(doc);
            yield return Settle();
            var shared = PictureOf(a);
            ((RectTransform)b.transform).sizeDelta = new Vector2(80f, 80f);
            yield return Settle();
            Assert.AreSame(shared, PictureOf(a));
            Assert.AreNotSame(shared, PictureOf(b));
            Assert.AreEqual(2, SvgPictures.Count);
            ((RectTransform)b.transform).sizeDelta = new Vector2(40f, 40f);
            yield return Settle();
            Assert.AreSame(shared, PictureOf(b), "back to the shared picture");
            Assert.AreEqual(1, SvgPictures.Count, "the 80 px picture was released");
        }

        [UnityTest]
        public IEnumerator ChangingTheOverrides_SwitchesPicturesAndKeepsTheRest()
        {
            var doc = Parse(Icon);
            var a = MakeImage(doc);
            var b = MakeImage(doc);
            yield return Settle();
            var shared = PictureOf(a);
            b.Overrides.Add(new SvgOverride { selector = "rect", hide = true });
            b.SetAllDirty();
            b.RebuildGenerated();
            yield return Settle();
            Assert.AreSame(shared, PictureOf(a));
            Assert.AreNotSame(shared, PictureOf(b));
            b.Overrides.Clear();
            b.SetAllDirty();
            b.RebuildGenerated();
            yield return Settle();
            Assert.AreSame(shared, PictureOf(b));
            Assert.AreEqual(1, SvgPictures.Count);
        }

        [UnityTest]
        public IEnumerator DisablingAndEnabling_ReleasesAndTakesThePictureAgain()
        {
            var doc = Parse(Icon);
            var a = MakeImage(doc);
            var b = MakeImage(doc);
            yield return Settle();
            a.gameObject.SetActive(false);
            yield return Settle();
            Assert.IsNotNull(PictureOf(b));
            Assert.AreEqual(1, SvgPictures.Count);
            b.gameObject.SetActive(false);
            yield return Settle();
            Assert.AreEqual(0, SvgPictures.Count, "everything is gone while both are off");
            a.gameObject.SetActive(true);
            b.gameObject.SetActive(true);
            yield return Settle();
            Assert.AreSame(PictureOf(a), PictureOf(b));
            Assert.AreEqual(1, SvgPictures.Count);
        }

        [UnityTest]
        public IEnumerator ALostTexture_IsDrawnAgainForEveryoneSharingIt()
        {
            var doc = Parse(Icon);
            var a = MakeImage(doc);
            var b = MakeImage(doc);
            yield return Settle();
            var texture = PictureOf(a);
            texture.Release(); // what a device reset does: the object stays, its contents are gone
            Assert.IsFalse(texture.IsCreated());
            a.SetAllDirty();
            yield return Settle();
            var again = PictureOf(a);
            Assert.IsTrue(again != null && again.IsCreated(), "drawn again");
            Assert.AreSame(again, PictureOf(b), "still shared");
        }
    }
}
