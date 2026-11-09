using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class AutoDefineAndPromptTests
    {
        [Test]
        public void ADefineIsAddedWhenWantedAndMissing()
        {
            Assert.AreEqual(VectraAutoDefines.Step.Add, VectraAutoDefines.Plan(true, false, false));
            Assert.AreEqual(VectraAutoDefines.Step.Add, VectraAutoDefines.Plan(true, false, true), "the user took it away but the bridge needs it");
        }

        [Test]
        public void ADefineTheUserSetIsNeverTouched()
        {
            Assert.AreEqual(VectraAutoDefines.Step.None, VectraAutoDefines.Plan(true, true, false));
            Assert.AreEqual(VectraAutoDefines.Step.None, VectraAutoDefines.Plan(false, true, false), "set by hand, so it stays when Lettera goes");
            Assert.AreEqual(VectraAutoDefines.Step.None, VectraAutoDefines.Plan(false, false, false));
        }

        [Test]
        public void OnlyWhatTheToolAddedIsTakenAway()
        {
            Assert.AreEqual(VectraAutoDefines.Step.Remove, VectraAutoDefines.Plan(false, true, true));
            Assert.AreEqual(VectraAutoDefines.Step.Forget, VectraAutoDefines.Plan(false, false, true));
            Assert.AreEqual(VectraAutoDefines.Step.None, VectraAutoDefines.Plan(true, true, true));
        }

        [Test]
        public void ALetteraFolderUnderAssetsIsWantedFromVersionOne()
        {
            Assert.IsTrue(VectraAutoDefines.Wanted("Assets/Plugins/Lettera/Runtime/FirstSymbol.Lettera.asmdef", _ => "1.1.0"));
            Assert.IsTrue(VectraAutoDefines.Wanted("Assets/L/Runtime/FirstSymbol.Lettera.asmdef", _ => "1.0.0-rc.4"));
            Assert.IsTrue(VectraAutoDefines.Wanted("Assets/L/Runtime/FirstSymbol.Lettera.asmdef", _ => null), "version unknown");
            Assert.IsFalse(VectraAutoDefines.Wanted("Assets/L/Runtime/FirstSymbol.Lettera.asmdef", _ => "0.9.0"));
            Assert.IsFalse(VectraAutoDefines.Wanted("Packages/com.firstsymbol.lettera/Runtime/FirstSymbol.Lettera.asmdef", _ => "1.1.0"), "a package sets the symbol itself");
            Assert.IsFalse(VectraAutoDefines.Wanted(null, null));
        }

        [Test]
        public void ThePromptNeverShowsInBatchModeOrWhenSuppressedOrAnswered()
        {
            Assert.IsTrue(VectraSvgImporterPrompt.ShouldAsk(false, false, false, false, false));
            Assert.IsFalse(VectraSvgImporterPrompt.ShouldAsk(true, false, false, false, false), "batch mode");
            Assert.IsFalse(VectraSvgImporterPrompt.ShouldAsk(false, true, false, false, false), "suppressed");
            Assert.IsFalse(VectraSvgImporterPrompt.ShouldAsk(false, false, true, false, false), "answered");
            Assert.IsFalse(VectraSvgImporterPrompt.ShouldAsk(false, false, false, true, false), "the importer is already on");
            Assert.IsFalse(VectraSvgImporterPrompt.ShouldAsk(false, false, false, false, true), "asked to wait");
        }

        [UnityEngine.TestTools.UnityTest]
        [Category("Screenshots")]
        public System.Collections.IEnumerator ThePromptWindowLooksRight()
        {
            string dir = Environment.GetEnvironmentVariable("FSHUB_SHOTS");
            if (string.IsNullOrEmpty(dir)) { Assert.Ignore("FSHUB_SHOTS is not set"); yield break; }
            VectraSvgImporterPrompt.Open();
            var window = Resources.FindObjectsOfTypeAll<VectraSvgImporterPrompt>().First();
            window.position = new Rect(60, 60, 470, 300);
            for (int i = 0; i < 6; i++)
            {
                window.Repaint();
                yield return null;
            }
            MethodInfo capture = typeof(UnityEditorInternal.InternalEditorUtility).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).First(m => m.Name == "CaptureEditorWindow");
            var rt = new RenderTexture(470, 300, 0, RenderTextureFormat.ARGB32);
            capture.Invoke(null, new object[] { window, rt });
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(470, 300, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 470, 300), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, (Environment.GetEnvironmentVariable("FSHUB_TAG") ?? "shot") + "_vectra_prompt.png"), tex.EncodeToPNG());
            window.Close();
            Assert.IsFalse(VectraProjectSettings.instance.SvgImporterAnswered, "closing the window is not an answer");
        }
    }
}
