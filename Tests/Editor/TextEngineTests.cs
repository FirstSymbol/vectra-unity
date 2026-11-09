using System.Linq;
using System.Text.RegularExpressions;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>Choosing a text engine: registry, Auto order, fallback, the project setting and the per-component override.</summary>
    public class TextEngineTests
    {
        // --- the registry ---------------------------------------------------------------------------------

        [Test]
        public void Legacy_IsAlwaysRegisteredAndUsable()
        {
            var legacy = VectraTextBackends.Get(VectraTextEngine.Legacy);
            Assert.IsNotNull(legacy);
            Assert.IsTrue(legacy.IsUsable(null, null));
            Assert.IsNull(legacy.FontAssetType);
            Assert.IsFalse(legacy.AcceptsFont(new Material(Shader.Find("Hidden/InternalErrorShader"))));
        }

        // 0 = not registered, 1 = registered but cannot draw, 2 = registered and can draw
        [TestCase(2, 2, VectraTextEngine.Lettera)]
        [TestCase(2, 0, VectraTextEngine.Lettera)]
        [TestCase(1, 2, VectraTextEngine.TextMeshPro)]
        [TestCase(1, 1, VectraTextEngine.Legacy)]
        [TestCase(0, 2, VectraTextEngine.TextMeshPro)]
        [TestCase(0, 1, VectraTextEngine.Legacy)]
        [TestCase(0, 0, VectraTextEngine.Legacy)]
        public void Auto_TakesLettera_ThenTextMeshPro_ThenLegacy(int lettera, int tmp, VectraTextEngine expected)
        {
            using (new RegistryScope(clear: true))
            {
                if (lettera > 0) VectraTextBackends.Register(new FakeBackend(VectraTextEngine.Lettera, usable: lettera == 2));
                if (tmp > 0) VectraTextBackends.Register(new FakeBackend(VectraTextEngine.TextMeshPro, usable: tmp == 2));
                var choice = VectraTextBackends.Resolve(VectraTextEngine.Auto, null, null, null);
                Assert.AreEqual(expected, choice.Used);
                Assert.IsFalse(choice.fellBack, "Auto never counts as a fallback");
            }
        }

        [Test]
        public void ANamedEngine_IsUsedWhenItCanDraw()
        {
            using (new RegistryScope(clear: true))
            {
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.Lettera));
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.TextMeshPro));
                foreach (var engine in new[] { VectraTextEngine.Lettera, VectraTextEngine.TextMeshPro, VectraTextEngine.Legacy })
                {
                    var choice = VectraTextBackends.Resolve(engine, null, null, null);
                    Assert.AreEqual(engine, choice.Used);
                    Assert.IsFalse(choice.fellBack);
                }
            }
        }

        [Test]
        public void ANamedEngine_ThatIsNotInstalled_FallsBackAlongTheAutoOrder()
        {
            using (new RegistryScope(clear: true))
            {
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.TextMeshPro));
                var choice = VectraTextBackends.Resolve(VectraTextEngine.Lettera, null, null, null);
                Assert.AreEqual(VectraTextEngine.TextMeshPro, choice.Used);
                Assert.IsTrue(choice.fellBack);
            }
        }

        [Test]
        public void TextMeshPro_WithoutAUsableFont_FallsBackToLegacy()
        {
            using (new RegistryScope(clear: true))
            {
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.TextMeshPro, usable: false));
                var choice = VectraTextBackends.Resolve(VectraTextEngine.TextMeshPro, null, null, null);
                Assert.AreEqual(VectraTextEngine.Legacy, choice.Used);
                Assert.IsTrue(choice.fellBack);
            }
        }

        [Test]
        public void None_DrawsNothing()
        {
            var choice = VectraTextBackends.Resolve(VectraTextEngine.None, null, null, null);
            Assert.IsNull(choice.backend);
            Assert.AreEqual(VectraTextEngine.None, choice.Used);
            Assert.IsFalse(choice.fellBack, "None was asked for");
        }

        [Test]
        public void ReplacingLegacy_AndUnregistering_RestoresTheBuiltInOne()
        {
            using (new RegistryScope())
            {
                var original = VectraTextBackends.Get(VectraTextEngine.Legacy);
                var fake = new FakeBackend(VectraTextEngine.Legacy);
                VectraTextBackends.Register(fake);
                Assert.AreSame(fake, VectraTextBackends.Get(VectraTextEngine.Legacy));
                VectraTextBackends.Unregister(VectraTextEngine.Legacy);
                Assert.AreSame(original, VectraTextBackends.Get(VectraTextEngine.Legacy));
            }
        }

        [Test]
        public void AutoAndNone_CannotBeRegistered()
        {
            using (new RegistryScope())
            {
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.Auto));
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.None));
                Assert.IsFalse(VectraTextBackends.IsRegistered(VectraTextEngine.Auto));
                Assert.IsFalse(VectraTextBackends.IsRegistered(VectraTextEngine.None));
            }
        }

        [Test]
        public void TheComponentFont_IsTheOneOfItsEngine()
        {
            var tmp = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "tmp" };
            var lettera = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "lettera" };
            try
            {
                Assert.AreSame(lettera, VectraTextBackends.FontFor(new FakeBackend(VectraTextEngine.Lettera), tmp, lettera));
                Assert.AreSame(tmp, VectraTextBackends.FontFor(new FakeBackend(VectraTextEngine.TextMeshPro), tmp, lettera));
                Assert.AreSame(tmp, VectraTextBackends.FontFor(null, tmp, lettera));
            }
            finally
            {
                Object.DestroyImmediate(tmp);
                Object.DestroyImmediate(lettera);
            }
        }

        // --- the project setting and the override ---------------------------------------------------------

        [TestCase(VectraTextEngineChoice.UseProjectSetting, VectraTextEngine.Legacy, VectraTextEngine.Legacy)]
        [TestCase(VectraTextEngineChoice.UseProjectSetting, VectraTextEngine.TextMeshPro, VectraTextEngine.TextMeshPro)]
        [TestCase(VectraTextEngineChoice.Auto, VectraTextEngine.Legacy, VectraTextEngine.Auto)]
        [TestCase(VectraTextEngineChoice.Lettera, VectraTextEngine.None, VectraTextEngine.Lettera)]
        [TestCase(VectraTextEngineChoice.TextMeshPro, VectraTextEngine.Lettera, VectraTextEngine.TextMeshPro)]
        [TestCase(VectraTextEngineChoice.Legacy, VectraTextEngine.Lettera, VectraTextEngine.Legacy)]
        [TestCase(VectraTextEngineChoice.None, VectraTextEngine.Lettera, VectraTextEngine.None)]
        public void Choice_FollowsTheProjectUnlessOverridden(VectraTextEngineChoice choice, VectraTextEngine project, VectraTextEngine expected) =>
            Assert.AreEqual(expected, VectraTextEngineChoices.Effective(choice, project));

        [Test]
        public void AnImage_FollowsTheProjectSetting_UntilItOverridesIt()
        {
            using (new RegistryScope(clear: true))
            using (var scope = new ImageScope())
            {
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.TextMeshPro));
                VectraRuntimeSettings.ProjectTextEngine = VectraTextEngine.TextMeshPro;
                var img = scope.Image();
                Assert.AreEqual(VectraTextEngine.TextMeshPro, img.RequestedTextEngine);
                Assert.AreEqual(VectraTextEngine.TextMeshPro, img.ResolveTextBackend().Used);

                VectraRuntimeSettings.ProjectTextEngine = VectraTextEngine.None;
                Assert.AreEqual(VectraTextEngine.None, img.RequestedTextEngine);
                img.TextEngine = VectraTextEngineChoice.Legacy;
                Assert.AreEqual(VectraTextEngine.Legacy, img.RequestedTextEngine);
                Assert.AreEqual(VectraTextEngine.Legacy, img.ResolveTextBackend().Used);
            }
        }

        static int CountMade(VectraSvgImage img, VectraTextEngine engine) =>
            img.transform.Cast<Transform>().Count(c => FakeBackend.Made(c.gameObject, engine));

        [Test]
        public void TwoImages_CanUseDifferentEnginesAtOnce()
        {
            using (new RegistryScope(clear: true))
            using (var scope = new ImageScope())
            {
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.TextMeshPro));
                VectraRuntimeSettings.ProjectTextEngine = VectraTextEngine.TextMeshPro;
                var follows = scope.Image();
                var legacy = scope.Image(engine: VectraTextEngineChoice.Legacy);
                Assert.IsTrue(ImageScope.Texts(follows).Count == 0 && CountMade(follows, VectraTextEngine.TextMeshPro) == 3, "follows the project");
                Assert.IsTrue(ImageScope.Texts(legacy).All(t => t.GetComponent<Text>() != null), "overrides it");
            }
        }

        // --- what an image builds -------------------------------------------------------------------------

        [Test]
        public void Legacy_BuildsOneTextPerSvgTextElement_WithTheRunsKept()
        {
            using (new RegistryScope())
            using (var scope = new ImageScope())
            {
                var img = scope.Image(engine: VectraTextEngineChoice.Legacy);
                var texts = ImageScope.Texts(img);
                Assert.AreEqual(3, texts.Count);
                var first = texts[0].GetComponent<Text>();
                StringAssert.Contains("<b><color=#FF8800FF>bold</color></b>", first.text);
                StringAssert.Contains("<i>", first.text);
                StringAssert.Contains("<size=36>big</size>", first.text, "36 document units at scale 1");
                StringAssert.StartsWith("Hello ", first.text);
            }
        }

        [Test]
        public void None_BuildsNoTextAndNoWarning()
        {
            using (new RegistryScope())
            using (var scope = new ImageScope())
            {
                var img = scope.Image(engine: VectraTextEngineChoice.None);
                Assert.AreEqual(0, ImageScope.Texts(img).Count);
                LogAssert.NoUnexpectedReceived();
            }
        }

        [Test]
        public void GenerateTextOff_BuildsNoTextAtAll()
        {
            using (new RegistryScope())
            using (var scope = new ImageScope())
            {
                var img = scope.Image(engine: VectraTextEngineChoice.Legacy);
                var so = new UnityEditor.SerializedObject(img);
                so.FindProperty("m_GenerateText").boolValue = false;
                so.ApplyModifiedProperties();
                img.RebuildGenerated();
                Assert.AreEqual(0, ImageScope.Texts(img).Count);
            }
        }

        [Test]
        public void AnEngineThatIsMissing_FallsBack_AndWarnsOncePerDocument()
        {
            using (new RegistryScope(clear: true))
            using (var scope = new ImageScope())
            {
                var img = scope.Image(engine: VectraTextEngineChoice.Lettera);
                LogAssert.Expect(LogType.Warning, new Regex("Lettera text engine is not available.*using Legacy"));
                img.RebuildGenerated();
                Assert.AreEqual(3, ImageScope.Texts(img).Count, "drawn by the fallback");
                img.RebuildGenerated();
                img.RebuildGenerated();
                LogAssert.NoUnexpectedReceived();
            }
        }

        [Test]
        public void WhenNoEngineCanDraw_TheTextIsSkipped_WithOneWarning()
        {
            using (new RegistryScope(clear: true))
            using (var scope = new ImageScope())
            {
                VectraTextBackends.Register(new FakeBackend(VectraTextEngine.Legacy, usable: false));
                LogAssert.Expect(LogType.Warning, new Regex("no text engine can draw"));
                var img = scope.Image(engine: VectraTextEngineChoice.Auto);
                Assert.AreEqual(0, ImageScope.Texts(img).Count);
                img.RebuildGenerated();
            }
        }

        [Test]
        public void TheBackendGetsPositionRotationSizeAndTint()
        {
            using (new RegistryScope(clear: true))
            using (var scope = new ImageScope())
            {
                var fake = new FakeBackend(VectraTextEngine.Lettera);
                VectraTextBackends.Register(fake);
                var img = scope.Image(engine: VectraTextEngineChoice.Lettera, size: 800f); // twice the document size
                img.color = new Color(1f, 0.5f, 0.25f, 0.5f);
                img.RebuildGenerated();
                Assert.AreEqual(2 * 3, fake.builds, "two rebuilds, three texts");
                var rotated = fake.last; // the last element: 'middle', rotated by -10 degrees in the SVG
                Assert.AreEqual(40f, rotated.fontSize, 0.01f, "20 document units at twice the size");
                Assert.AreEqual(10f, rotated.rotation, 0.05f, "SVG rotates clockwise, Unity counter-clockwise");
                Assert.AreEqual(0.5f, rotated.tint.g, 1e-4f);
                Assert.AreEqual(rotated.element.textColor.r * 1f, rotated.color.r, 1e-4f);
                Assert.AreEqual(rotated.element.textColor.a * 0.5f, rotated.color.a, 1e-4f);
                // 'middle' sits at x=200,y=170 in a 400x200 document: centre column, 70 below the centre; at 2x: 140 below
                Assert.AreEqual(0f, rotated.origin.x, 0.5f);
                Assert.AreEqual(-140f, rotated.origin.y, 0.5f);
            }
        }

        [Test]
        public void TheBackendGetsTheComponentFontOfItsEngine_AndTheFontMap()
        {
            using (new RegistryScope(clear: true))
            using (var scope = new ImageScope())
            {
                var fake = new FakeBackend(VectraTextEngine.Lettera);
                VectraTextBackends.Register(fake);
                var tmpFont = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "tmp font" };
                var letteraFont = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "lettera font" };
                var map = ScriptableObject.CreateInstance<VectraFontMap>();
                try
                {
                    var img = scope.Image(engine: VectraTextEngineChoice.Lettera);
                    img.FallbackFont = tmpFont;
                    img.LetteraFallbackFont = letteraFont;
                    img.FontMap = map;
                    img.RebuildGenerated();
                    Assert.AreSame(letteraFont, fake.last.fallbackFont);
                    Assert.AreSame(map, fake.last.fontMap);
                }
                finally
                {
                    Object.DestroyImmediate(tmpFont);
                    Object.DestroyImmediate(letteraFont);
                    Object.DestroyImmediate(map);
                }
            }
        }

        [Test]
        public void Expand_BuildsItsTextWithTheChosenBackend()
        {
            using (new RegistryScope(clear: true))
            using (var scope = new ImageScope())
            {
                var fake = new FakeBackend(VectraTextEngine.TextMeshPro);
                VectraTextBackends.Register(fake);
                var img = scope.Image(engine: VectraTextEngineChoice.TextMeshPro);
                int before = fake.builds;
                var root = VectraSvg.Expand(img);
                Assert.AreEqual(before + 3, fake.builds);
                Assert.AreEqual(3, root.Cast<Transform>().Count(c => FakeBackend.Made(c.gameObject, VectraTextEngine.TextMeshPro)));
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [Test]
        public void Expand_WithNoEngine_LeavesTheTextOut()
        {
            using (new RegistryScope())
            using (var scope = new ImageScope())
            {
                var img = scope.Image(engine: VectraTextEngineChoice.None);
                var root = VectraSvg.Expand(img);
                Assert.AreEqual(0, root.GetComponentsInChildren<Text>().Length);
                Object.DestroyImmediate(root.gameObject);
            }
        }

        // --- the legacy backend's placement --------------------------------------------------------------

        [TestCase("start", 0f, TextAnchor.LowerLeft)]
        [TestCase("middle", 0.5f, TextAnchor.LowerCenter)]
        [TestCase("end", 1f, TextAnchor.LowerRight)]
        public void Legacy_AnchorsTheBaselineOriginWithThePivot(string anchor, float pivotX, TextAnchor alignment)
        {
            using (new RegistryScope())
            using (var scope = new ImageScope())
            {
                var svg = $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 200'><text x='100' y='150' font-size='20' text-anchor='{anchor}'>Hxg</text></svg>";
                var img = scope.Image(svg, VectraTextEngineChoice.Legacy);
                var go = ImageScope.Texts(img).Single();
                var rt = (RectTransform)go.transform;
                var t = go.GetComponent<Text>();
                Assert.AreEqual(pivotX, rt.pivot.x, 1e-4f);
                Assert.AreEqual(0f, rt.pivot.y, 1e-4f);
                Assert.AreEqual(alignment, t.alignment);
                Assert.AreEqual(-100f, rt.anchoredPosition.x, 0.01f, "x=100 in a 400 wide rect is 100 left of the centre");
                Assert.AreEqual(-50f - 20f * 0.21f, rt.anchoredPosition.y, 0.01f, "y=150 in a 200 high rect is 50 below the centre, less the descent");
            }
        }

        [Test]
        public void Legacy_BoldAndItalic_BecomeTheFontStyle()
        {
            using (new RegistryScope())
            using (var scope = new ImageScope())
            {
                var svg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 200'><text x='1' y='50' font-weight='700' font-style='italic'>x</text></svg>";
                var img = scope.Image(svg, VectraTextEngineChoice.Legacy);
                var t = ImageScope.Texts(img).Single().GetComponent<Text>();
                Assert.AreEqual(FontStyle.BoldAndItalic, t.fontStyle);
                Assert.AreEqual("x", t.text, "no tags for what the font style already does");
            }
        }

        [Test]
        public void Legacy_UsesTheBuiltInFont_AndNeverCatchesRaycasts()
        {
            using (new RegistryScope())
            using (var scope = new ImageScope())
            {
                var img = scope.Image(engine: VectraTextEngineChoice.Legacy);
                foreach (var go in ImageScope.Texts(img))
                {
                    var t = go.GetComponent<Text>();
                    Assert.IsNotNull(t.font);
                    Assert.IsFalse(t.raycastTarget);
                    Assert.IsTrue(t.supportRichText);
                }
            }
        }

        [Test]
        public void ReadText_ReturnsWhatTheEngineWasGiven()
        {
            using (new RegistryScope())
            using (var scope = new ImageScope())
            {
                var img = scope.Image(engine: VectraTextEngineChoice.Legacy);
                var go = ImageScope.Texts(img)[0];
                Assert.AreEqual(go.GetComponent<Text>().text, VectraTextBackends.Get(VectraTextEngine.Legacy).ReadText(go));
                Assert.IsNull(VectraTextBackends.Get(VectraTextEngine.Legacy).ReadText(null));
            }
        }

        // --- the sandbox's cell of the matrix -------------------------------------------------------------

        [Test]
        public void TheRegistry_MatchesWhatTheProjectHas()
        {
            // What the project has: Lettera as a package or as a folder under Assets.
            bool lettera = !string.IsNullOrEmpty(UnityEditor.Compilation.CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName("FirstSymbol.Lettera"));
            Assert.AreEqual(lettera, VectraTextBackends.IsRegistered(VectraTextEngine.Lettera), lettera ? "Lettera is in the project" : "Lettera is not in the project");
#if VECTRA_NO_TMP
            Assert.IsFalse(VectraTextBackends.IsRegistered(VectraTextEngine.TextMeshPro), "the TextMesh Pro backend is switched off");
#else
            Assert.IsTrue(VectraTextBackends.IsRegistered(VectraTextEngine.TextMeshPro), "the TextMesh Pro backend is on");
#endif
            var auto = VectraTextBackends.Resolve(VectraTextEngine.Auto, null, null, null);
            var expected = VectraTextBackends.IsRegistered(VectraTextEngine.Lettera) ? VectraTextEngine.Lettera
                : VectraTextBackends.IsRegistered(VectraTextEngine.TextMeshPro) && VectraTextBackends.Get(VectraTextEngine.TextMeshPro).IsUsable(null, null) ? VectraTextEngine.TextMeshPro
                : VectraTextEngine.Legacy;
            Assert.AreEqual(expected, auto.Used);
            TestContext.WriteLine($"Auto resolves to {auto.Used} (Lettera {VectraTextBackends.IsRegistered(VectraTextEngine.Lettera)}, TMP backend {VectraTextBackends.IsRegistered(VectraTextEngine.TextMeshPro)})");
        }
    }
}
