using System.Linq;
using FirstSymbol.Vectra.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor.Tests
{
    public class RuntimeSettingsTests
    {
        int m_Max;
        int m_Project;

        [SetUp]
        public void SetUp()
        {
            m_Max = VectraSdfAtlas.MaxSize;
            m_Project = VectraProjectSettings.instance.sdfAtlasMaxSize;
            VectraBuildSettings.Remove();
        }

        [TearDown]
        public void TearDown()
        {
            VectraBuildSettings.Remove();
            VectraProjectSettings.instance.sdfAtlasMaxSize = m_Project;
            VectraSdfAtlas.MaxSize = m_Max;
        }

        [TestCase(0, 512)]
        [TestCase(700, 512)]
        [TestCase(3000, 2048)]
        [TestCase(4096, 4096)]
        [TestCase(99999, 8192)]
        public void AtlasSize_IsClampedToAPowerOfTwo(int given, int expected) =>
            Assert.AreEqual(expected, VectraRuntimeSettings.NormalizeAtlasSize(given));

        [Test]
        public void TheAssetAppliesItsSizeWhenItLoads()
        {
            var s = VectraRuntimeSettings.Create(4096);
            try
            {
                Assert.AreEqual(4096, VectraSdfAtlas.MaxSize);
                VectraSdfAtlas.MaxSize = 2048;
                s.Apply();
                Assert.AreEqual(4096, VectraSdfAtlas.MaxSize);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void ABuild_CarriesANonDefaultSizeToThePlayer()
        {
            VectraProjectSettings.instance.sdfAtlasMaxSize = 4096;
            Assert.IsTrue(VectraBuildSettings.Install());
            var found = PlayerSettings.GetPreloadedAssets().OfType<VectraRuntimeSettings>().ToArray();
            Assert.AreEqual(1, found.Length);
            Assert.AreEqual(4096, found[0].SdfAtlasMaxSize);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<VectraRuntimeSettings>(VectraBuildSettings.AssetPath));

            VectraBuildSettings.Remove();
            Assert.IsFalse(PlayerSettings.GetPreloadedAssets().OfType<VectraRuntimeSettings>().Any(), "preloaded asset after the build");
            Assert.IsFalse(AssetDatabase.IsValidFolder(VectraBuildSettings.Folder), "temporary folder after the build");
        }

        [Test]
        public void ADefaultSize_AddsNothingToTheBuild()
        {
            VectraProjectSettings.instance.sdfAtlasMaxSize = VectraRuntimeSettings.DefaultAtlasSize;
            Assert.IsFalse(VectraBuildSettings.Install());
            Assert.IsFalse(PlayerSettings.GetPreloadedAssets().OfType<VectraRuntimeSettings>().Any());
            Assert.IsFalse(AssetDatabase.IsValidFolder(VectraBuildSettings.Folder));
        }
    }
}
