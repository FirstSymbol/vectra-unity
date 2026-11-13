using System.IO;
using System.Text.RegularExpressions;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class WatchEditModeTests
    {
        [Test]
        public void Registrations_SurviveTheStartOfEnterPlayModeWithoutReloads()
        {
            var go = new GameObject("Watched", typeof(VectraRenderer));
            try
            {
                VectraWatch.Tick();
                int count = VectraWatch.Count;
                Assert.GreaterOrEqual(count, 1);
                VectraWatch.SimulateSubsystemRegistration();
                Assert.AreEqual(count, VectraWatch.Count, "nothing is reset: the objects are not enabled again");
                go.GetComponent<VectraRenderer>().SetDirty();
                VectraWatch.Tick();
                Assert.GreaterOrEqual(VectraWatch.LastTicked, 1, "a dirtied renderer is still ticked");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheDriverComesBack_WhenItsSceneWasClosed()
        {
            var go = new GameObject("Watched", typeof(VectraRenderer));
            try
            {
                Assert.IsTrue(VectraWatch.HasDriver);
                VectraWatch.DestroyDriverForTests();
                Assert.IsFalse(VectraWatch.HasDriver);
                VectraWatch.EditorEnsureDriver(); // what the editor update does
                Assert.IsTrue(VectraWatch.HasDriver, "the editor update brings it back");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ShaderGradientScale_MatchesTheAtlasMaxHeight()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VectraGradientAtlas).Assembly);
            string path = Path.Combine(info.resolvedPath, "Runtime", "Resources", "Vectra", "VectraCore.hlsl");
            Assert.IsNotNull(path, "VectraCore.hlsl found");
            var m = Regex.Match(File.ReadAllText(path), @"v \* \((\d+)\.0 \* _VectraGradients_TexelSize\.y\)");
            Assert.IsTrue(m.Success, "GradientV found");
            Assert.AreEqual(VectraGradientAtlas.MaxHeight, int.Parse(m.Groups[1].Value));
        }
    }
}
