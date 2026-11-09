using System.Linq;
using FirstSymbol.Lettera;
using FirstSymbol.Vectra.Svg;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.TextEngines.Tests
{
    /// <summary>The Lettera backend, under the same define constraint as the assembly it tests.</summary>
    public class LetteraBackendTests
    {
        const string Svg =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 200'>" +
            "<text x='20' y='60' font-size='24' fill='#ffffff' font-family='Inter, sans-serif'>Hello <tspan font-weight='bold' fill='#ff8800'>bold</tspan> and " +
            "<tspan font-style='italic' font-size='36'>big</tspan></text>" +
            "<text x='380' y='120' font-size='20' text-anchor='end' font-weight='bold'>right</text>" +
            "<text x='200' y='170' font-size='20' text-anchor='middle'>mid &lt;x&gt; *s*</text>" +
            "<text x='50' y='100' font-size='40'>HHHH</text></svg>";

        IVectraTextBackend m_Backend;
        GameObject m_Root;
        VectraSvgDocument m_Doc;
        LetteraFontStack m_Stack, m_BoldStack;

        [SetUp]
        public void SetUp()
        {
            m_Backend = VectraTextBackends.Get(VectraTextEngine.Lettera);
            m_Root = new GameObject("lettera tests", typeof(Canvas));
            m_Doc = SvgParser.Parse(Svg);
            m_Stack = ScriptableObject.CreateInstance<LetteraFontStack>();
            m_BoldStack = ScriptableObject.CreateInstance<LetteraFontStack>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Root);
            Object.DestroyImmediate(m_Doc);
            Object.DestroyImmediate(m_Stack);
            Object.DestroyImmediate(m_BoldStack);
        }

        VectraTextSpec Spec(int element, VectraFontMap map = null, Object fallback = null)
        {
            var e = m_Doc.elements.Where(x => x.isText).ElementAt(element);
            return new VectraTextSpec
            {
                element = e, fontSize = e.fontSize, tint = Color.white, color = e.textColor,
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
        public void ItIsRegistered_AndKnowsItsFontTypes()
        {
            Assert.IsNotNull(m_Backend);
            Assert.AreEqual(VectraTextEngine.Lettera, m_Backend.Engine);
            Assert.IsTrue(m_Backend.IsUsable(null, null));
            Assert.IsTrue(m_Backend.AcceptsFont(m_Stack), "a font stack");
            Assert.IsFalse(m_Backend.AcceptsFont(null));
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            Assert.IsFalse(m_Backend.AcceptsFont(mat));
            Object.DestroyImmediate(mat);
        }

        [Test]
        public void Build_MakesALetteraText_WithTheRunsAsMarkup()
        {
            var go = Child();
            m_Backend.Build(go, Spec(0));
            var t = go.GetComponent<LetteraText>();
            Assert.IsNotNull(t);
            Assert.AreEqual("Hello <b><color=#FF8800FF>bold</color></b> and <i><size=150%>big</size></i>", t.Text);
            Assert.AreEqual(t.Text, m_Backend.ReadText(go));
            Assert.AreEqual(24f, t.FontSize, 1e-3f);
            Assert.IsFalse(t.WordWrap);
            Assert.IsFalse(t.raycastTarget);
            Assert.AreEqual(Color.white.r, t.color.r, 1e-3f);
        }

        [Test]
        public void Build_EscapesMarkupThatIsLiteralText()
        {
            var go = Child();
            m_Backend.Build(go, Spec(2));
            Assert.AreEqual("mid \\<x> \\*s\\*", go.GetComponent<LetteraText>().Text);
            Assert.AreEqual("mid <x> *s*", go.GetComponent<LetteraText>().CleanText, "Lettera shows the characters as written");
        }

        [Test]
        public void Build_BoldElement_GetsABoldTag_UnlessTheMapHasABoldFace()
        {
            var go = Child();
            m_Backend.Build(go, Spec(1));
            Assert.AreEqual("<b>right</b>", go.GetComponent<LetteraText>().Text);

            var map = ScriptableObject.CreateInstance<VectraFontMap>();
            try
            {
                var e = m_Doc.elements.Where(x => x.isText).ElementAt(1);
                e.fontFamily = "Inter";
                map.entries.Add(new VectraFontMap.Entry { family = "Inter", lettera = m_Stack, letteraBold = m_BoldStack });
                var go2 = Child();
                m_Backend.Build(go2, Spec(1, map));
                var t = go2.GetComponent<LetteraText>();
                Assert.AreEqual("right", t.Text, "the face is already bold");
                Assert.AreSame(m_BoldStack, t.FontStack);
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void Build_TakesTheFontFromTheMap_ThenTheComponentFallback()
        {
            var map = ScriptableObject.CreateInstance<VectraFontMap>();
            try
            {
                map.entries.Add(new VectraFontMap.Entry { family = "Inter", lettera = m_Stack });
                var go = Child();
                m_Backend.Build(go, Spec(0, map, m_BoldStack));
                Assert.AreSame(m_Stack, go.GetComponent<LetteraText>().FontStack, "by family, from the map");

                var go2 = Child();
                m_Backend.Build(go2, Spec(2, map, m_BoldStack));
                Assert.AreSame(m_BoldStack, go2.GetComponent<LetteraText>().FontStack, "no family matches: the fallback");

                var go3 = Child();
                m_Backend.Build(go3, Spec(2, null, null));
                Assert.IsNull(go3.GetComponent<LetteraText>().FontStack);
                Assert.IsNull(go3.GetComponent<LetteraText>().Font, "Lettera's own default applies");
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        [TestCase(0, 0f, LetteraHorizontalAlignment.Left)]
        [TestCase(1, 1f, LetteraHorizontalAlignment.Right)]
        [TestCase(2, 0.5f, LetteraHorizontalAlignment.Center)]
        public void Build_PutsThePivotOnTheAnchor(int element, float pivotX, LetteraHorizontalAlignment alignment)
        {
            var go = Child();
            var spec = Spec(element);
            spec.rotation = 7f;
            m_Backend.Build(go, spec);
            var rt = (RectTransform)go.transform;
            var t = go.GetComponent<LetteraText>();
            Assert.AreEqual(pivotX, rt.pivot.x, 1e-4f);
            Assert.AreEqual(alignment, t.HorizontalAlignment);
            Assert.AreEqual(LetteraVerticalAlignment.Top, t.VerticalAlignment);
            Assert.AreEqual(10f, rt.anchoredPosition.x, 1e-3f);
            Assert.AreEqual(7f, rt.localEulerAngles.z, 1e-3f);
        }

        [Test]
        public void Build_PutsTheFirstBaselineOnTheOrigin()
        {
            var go = Child();
            m_Backend.Build(go, Spec(3));
            var rt = (RectTransform)go.transform;
            var t = go.GetComponent<LetteraText>();
            t.ForceRebuild();
            Assert.AreEqual(1, t.Lines.Count);
            // the pivot is the top edge; the baseline is a line of the layout below it. The rendered position is checked in TextRenderTests.
            Assert.AreEqual(-20f, rt.anchoredPosition.y + t.Lines[0].Baseline, 1e-3f);
            Assert.AreEqual(1f, rt.pivot.y, 1e-4f);
            Assert.Less(t.Lines[0].Baseline, 0f, "below the pivot");
            Assert.Greater(-t.Lines[0].Baseline, 40f * 0.5f, "about the ascent of the font");
        }

        [Test]
        public void Build_CalledTwice_UpdatesTheSameComponent()
        {
            var go = Child();
            m_Backend.Build(go, Spec(0));
            m_Backend.Build(go, Spec(1));
            Assert.AreEqual(1, go.GetComponents<LetteraText>().Length);
            StringAssert.Contains("right", go.GetComponent<LetteraText>().Text);
        }

        [Test]
        public void AnImage_UsingLettera_BuildsLetteraChildren_AndAutoPicksLettera()
        {
            var canvasGo = new GameObject("c", typeof(Canvas));
            try
            {
                var go = new GameObject("img", typeof(RectTransform));
                go.transform.SetParent(canvasGo.transform, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(400, 200);
                var img = go.AddComponent<VectraSvgImage>();
                img.Document = m_Doc;
                img.TextEngine = VectraTextEngineChoice.Auto;
                Assert.AreEqual(VectraTextEngine.Lettera, img.ResolveTextBackend().Used);
                img.RebuildGenerated();
                var texts = img.GetComponentsInChildren<LetteraText>();
                Assert.AreEqual(4, texts.Length);
                StringAssert.Contains("<b>", texts[0].Text);
                var choice = VectraTextBackends.Resolve(VectraTextEngine.Auto, null, null, null);
                Assert.AreEqual(VectraTextEngine.Lettera, choice.Used);
                Assert.IsFalse(choice.fellBack);
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }

        [Test]
        public void Expand_UsesLetteraWhenChosen()
        {
            var canvasGo = new GameObject("c", typeof(Canvas));
            try
            {
                var go = new GameObject("img", typeof(RectTransform));
                go.transform.SetParent(canvasGo.transform, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(400, 200);
                var img = go.AddComponent<VectraSvgImage>();
                img.Document = m_Doc;
                img.TextEngine = VectraTextEngineChoice.Lettera;
                var root = VectraSvg.Expand(img);
                Assert.AreEqual(4, root.GetComponentsInChildren<LetteraText>().Length);
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }

        [Test]
        public void ALetteraFontInTheMap_IsKeptByValidate_AndATextMeshProOneIsNot()
        {
            var map = ScriptableObject.CreateInstance<VectraFontMap>();
            var wrong = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "not a font" };
            try
            {
                map.letteraFallback = m_Stack;
                map.entries.Add(new VectraFontMap.Entry { family = "x", lettera = m_Stack, letteraBold = wrong });
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not a Lettera font"));
                Assert.AreEqual(1, map.Validate());
                Assert.AreSame(m_Stack, map.letteraFallback);
                Assert.AreSame(m_Stack, map.entries[0].lettera);
                Assert.IsNull(map.entries[0].letteraBold);
            }
            finally
            {
                Object.DestroyImmediate(map);
                Object.DestroyImmediate(wrong);
            }
        }
    }
}
