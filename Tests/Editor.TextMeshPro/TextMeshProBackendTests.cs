using System.Linq;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.TextEngines.Tests
{
    /// <summary>The TextMesh Pro backend, under the same define constraints as the assembly it tests.</summary>
    public class TextMeshProBackendTests
    {
        const string Svg =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 200'>" +
            "<text x='20' y='60' font-size='24' fill='#ffffff' font-family='Inter, sans-serif'>Hello <tspan font-weight='bold' fill='#ff8800'>bold</tspan> and " +
            "<tspan font-style='italic' font-size='36'>big</tspan></text>" +
            "<text x='380' y='120' font-size='20' text-anchor='end' font-weight='bold'>right</text>" +
            "<text x='200' y='170' font-size='20' text-anchor='middle' transform='rotate(-10 200 170)'>mid &lt;x&gt;</text></svg>";

        IVectraTextBackend m_Backend;
        TMP_FontAsset m_Font;
        TMP_FontAsset m_BoldFont;
        GameObject m_Root;
        VectraSvgDocument m_Doc;
        bool m_IgnoreFailing;

        [SetUp]
        public void SetUp()
        {
            m_IgnoreFailing = LogAssert.ignoreFailingMessages;
            // without TMP Essential Resources the component complains about its missing default font; that is not what is under test
            LogAssert.ignoreFailingMessages = true;
            m_Backend = VectraTextBackends.Get(VectraTextEngine.TextMeshPro);
            m_Root = new GameObject("tmp tests", typeof(RectTransform));
            // building a font needs TMP's settings asset; in a project without TMP Essential Resources the tests that need a font are ignored
            try
            {
                m_Font = TMP_FontAsset.CreateFontAsset("Arial", "Regular", 90);
                m_BoldFont = TMP_FontAsset.CreateFontAsset("Arial", "Bold", 90);
            }
            catch (System.Exception)
            {
                m_Font = m_BoldFont = null;
            }

            m_Doc = SvgParser.Parse(Svg);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = m_IgnoreFailing;
            Object.DestroyImmediate(m_Root);
            foreach (var o in new Object[] { m_Font, m_BoldFont, m_Doc })
                if (o != null) Object.DestroyImmediate(o);
        }

        VectraTextSpec Spec(int element, VectraFontMap map = null, Object fallback = null, float fontScale = 1f)
        {
            var e = m_Doc.elements.Where(x => x.isText).ElementAt(element);
            return new VectraTextSpec
            {
                element = e, fontSize = e.fontSize * fontScale, tint = Color.white, color = e.textColor,
                origin = new Vector2(10f, -20f), rotation = 0f, fontMap = map, fallbackFont = fallback
            };
        }

        GameObject Child()
        {
            var go = new GameObject("t", typeof(RectTransform));
            go.transform.SetParent(m_Root.transform, false);
            return go;
        }

        [Test]
        public void ItIsRegistered_AndKnowsItsFontType()
        {
            Assert.IsNotNull(m_Backend);
            Assert.AreEqual(VectraTextEngine.TextMeshPro, m_Backend.Engine);
            Assert.AreEqual(typeof(TMP_FontAsset), m_Backend.FontAssetType);
        }

        [Test]
        public void ItAcceptsOnlyTextMeshProFonts()
        {
            if (m_Font == null) Assert.Ignore("No system font to build a TextMesh Pro font from");
            Assert.IsTrue(m_Backend.AcceptsFont(m_Font));
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            Assert.IsFalse(m_Backend.AcceptsFont(mat));
            Assert.IsFalse(m_Backend.AcceptsFont(null));
            Object.DestroyImmediate(mat);
        }

        [Test]
        public void ItIsUsable_WithAFallbackFont_AFontInTheMap_OrTheDefaultFont()
        {
            if (m_Font == null) Assert.Ignore("No system font to build a TextMesh Pro font from");
            var map = ScriptableObject.CreateInstance<VectraFontMap>();
            try
            {
                Assert.IsTrue(m_Backend.IsUsable(null, m_Font));
                Assert.IsFalse(m_Backend.IsUsable(map, null) && !map.HasFonts(VectraTextEngine.TextMeshPro) && !HasDefault(), "an empty map without a default font");
                map.fallback = m_Font;
                Assert.IsTrue(m_Backend.IsUsable(map, null));
                Assert.AreEqual(HasDefault(), m_Backend.IsUsable(null, null), "the default font decides when nothing else is given");
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        static bool HasDefault() => Resources.Load<ScriptableObject>("TMP Settings") != null && TMP_Settings.defaultFontAsset != null;

        [Test]
        public void Build_MakesATextMeshProObject_WithTheRunsAsRichText()
        {
            if (m_Font == null) Assert.Ignore("No system font to build a TextMesh Pro font from");
            var go = Child();
            m_Backend.Build(go, Spec(0, null, m_Font));
            var t = go.GetComponent<TextMeshProUGUI>();
            Assert.IsNotNull(t);
            StringAssert.StartsWith("Hello ", t.text);
            StringAssert.Contains("<b><color=#FF8800FF>bold</color></b>", t.text);
            StringAssert.Contains("<size=150%>", t.text, "36 against 24");
            StringAssert.Contains("<i>", t.text);
            Assert.AreEqual(t.text, m_Backend.ReadText(go));
            Assert.AreSame(m_Font, t.font);
            Assert.AreEqual(24f, t.fontSize, 1e-3f);
            Assert.IsFalse(t.raycastTarget);
        }

        [Test]
        public void Build_ProtectsALiteralLessThan()
        {
            if (m_Font == null) Assert.Ignore("No system font to build a TextMesh Pro font from");
            var go = Child();
            m_Backend.Build(go, Spec(2, null, m_Font));
            StringAssert.Contains("<noparse>mid <x></noparse>", go.GetComponent<TextMeshProUGUI>().text);
        }

        [TestCase(0, 0f, TextAlignmentOptions.BaselineLeft)]
        [TestCase(1, 1f, TextAlignmentOptions.BaselineRight)]
        [TestCase(2, 0.5f, TextAlignmentOptions.Baseline)]
        public void Build_AnchorsTheBaselineOrigin(int element, float pivotX, TextAlignmentOptions alignment)
        {
            if (m_Font == null) Assert.Ignore("No system font to build a TextMesh Pro font from");
            var go = Child();
            var spec = Spec(element, null, m_Font);
            spec.rotation = 7f;
            m_Backend.Build(go, spec);
            var rt = (RectTransform)go.transform;
            var t = go.GetComponent<TextMeshProUGUI>();
            Assert.AreEqual(pivotX, rt.pivot.x, 1e-4f);
            Assert.AreEqual(0.5f, rt.pivot.y, 1e-4f, "the Baseline alignment keeps the baseline on the middle of the box");
            Assert.AreEqual(alignment, t.alignment);
            Assert.AreEqual(10f, rt.anchoredPosition.x, 1e-3f);
            Assert.AreEqual(-20f, rt.anchoredPosition.y, 1e-3f, "the origin itself");
            Assert.AreEqual(7f, rt.localEulerAngles.z, 1e-3f);
        }

        [Test]
        public void Build_BoldComesFromTheFontStyle_UnlessTheMapHasABoldFace()
        {
            if (m_Font == null || m_BoldFont == null) Assert.Ignore("No system font to build a TextMesh Pro font from");
            var map = ScriptableObject.CreateInstance<VectraFontMap>();
            try
            {
                var go = Child();
                m_Backend.Build(go, Spec(1, map, m_Font));
                Assert.IsTrue((go.GetComponent<TextMeshProUGUI>().fontStyle & FontStyles.Bold) != 0, "synthesized");

                map.entries.Add(new VectraFontMap.Entry { family = "sans-serif", regular = m_Font, bold = m_BoldFont });
                var e = m_Doc.elements.Where(x => x.isText).ElementAt(1);
                e.fontFamily = "sans-serif";
                var go2 = Child();
                m_Backend.Build(go2, Spec(1, map, m_Font));
                var t2 = go2.GetComponent<TextMeshProUGUI>();
                Assert.AreSame(m_BoldFont, t2.font);
                Assert.IsFalse((t2.fontStyle & FontStyles.Bold) != 0, "the face is already bold");
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void Build_TakesTheFontFromTheMap_ByFamily()
        {
            if (m_Font == null) Assert.Ignore("No system font to build a TextMesh Pro font from");
            var map = ScriptableObject.CreateInstance<VectraFontMap>();
            try
            {
                map.entries.Add(new VectraFontMap.Entry { family = "Inter", regular = m_Font });
                var go = Child();
                m_Backend.Build(go, Spec(0, map, null));
                Assert.AreSame(m_Font, go.GetComponent<TextMeshProUGUI>().font);
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void Build_CalledTwice_UpdatesTheSameComponent()
        {
            if (m_Font == null) Assert.Ignore("No system font to build a TextMesh Pro font from");
            var go = Child();
            m_Backend.Build(go, Spec(0, null, m_Font));
            m_Backend.Build(go, Spec(1, null, m_Font));
            Assert.AreEqual(1, go.GetComponents<TextMeshProUGUI>().Length);
            StringAssert.Contains("right", go.GetComponent<TextMeshProUGUI>().text);
        }

        [Test]
        public void AnImage_UsingTextMeshPro_BuildsTextMeshProChildren()
        {
            if (m_Font == null) Assert.Ignore("No system font to build a TextMesh Pro font from");
            var canvasGo = new GameObject("c", typeof(Canvas));
            try
            {
                var go = new GameObject("img", typeof(RectTransform));
                go.transform.SetParent(canvasGo.transform, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(400, 200);
                var img = go.AddComponent<VectraSvgImage>();
                img.Document = m_Doc;
                img.TextEngine = VectraTextEngineChoice.TextMeshPro;
                img.FallbackFont = m_Font;
                img.RebuildGenerated();
                var texts = img.GetComponentsInChildren<TextMeshProUGUI>();
                Assert.AreEqual(3, texts.Length);
                Assert.AreEqual(VectraTextEngine.TextMeshPro, img.ResolveTextBackend().Used);
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }

        [Test]
        public void AutoPicksTextMeshPro_WhenLetteraIsAbsent_AndAFontResolves()
        {
            if (VectraTextBackends.IsRegistered(VectraTextEngine.Lettera)) Assert.Ignore("Lettera is installed here, so Auto takes it");
            if (m_Font == null) Assert.Ignore("No system font to build a TextMesh Pro font from");
            var choice = VectraTextBackends.Resolve(VectraTextEngine.Auto, null, m_Font, null);
            Assert.AreEqual(VectraTextEngine.TextMeshPro, choice.Used);
            Assert.IsFalse(choice.fellBack);
        }

        [Test]
        public void WithoutAnyFont_AndWithoutTheDefaultFont_AutoUsesLegacy()
        {
            if (VectraTextBackends.IsRegistered(VectraTextEngine.Lettera)) Assert.Ignore("Lettera is installed here, so Auto takes it");
            if (HasDefault()) Assert.Ignore("TMP Essential Resources are imported here, so TextMesh Pro has a default font");
            var choice = VectraTextBackends.Resolve(VectraTextEngine.Auto, null, null, null);
            Assert.AreEqual(VectraTextEngine.Legacy, choice.Used);
        }
    }
}
