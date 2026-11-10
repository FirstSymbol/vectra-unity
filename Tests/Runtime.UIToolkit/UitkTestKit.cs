using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit.Tests
{
    /// <summary>A runtime UIDocument rendered into a render texture, and a way to read its pixels.</summary>
    sealed class UitkHarness : IDisposable
    {
        public readonly GameObject go;
        public readonly UIDocument document;
        public readonly PanelSettings settings;
        public readonly RenderTexture target;
        public readonly int width, height;
        Color32[] m_Pixels;

        public UitkHarness(int w = 256, int h = 256, float scale = 1f)
        {
            width = w;
            height = h;
            target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.HideAndDontSave };
            target.Create();
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.targetTexture = target;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = scale;
            settings.clearColor = true;
            settings.colorClearValue = new Color(0f, 0f, 0f, 1f);
            go = new GameObject("Vectra UITK test");
            go.SetActive(false);
            document = go.AddComponent<UIDocument>();
            document.panelSettings = settings;
            go.SetActive(true);
            Root.style.flexGrow = 1;
        }

        public VisualElement Root => document.rootVisualElement;

        /// <summary>Puts <paramref name="e"/> at a fixed place of the panel.</summary>
        public T Place<T>(T e, float x, float y, float w, float h) where T : VisualElement
        {
            e.style.position = Position.Absolute;
            e.style.left = x;
            e.style.top = y;
            e.style.width = w;
            e.style.height = h;
            Root.Add(e);
            return e;
        }

        /// <summary>Lets the panel lay out, run its scheduled work and repaint.</summary>
        public IEnumerator Frames(int n = 4)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        /// <summary>Reads the target; pixel (0, 0) is the top-left of the panel.</summary>
        public void Read()
        {
            var prev = RenderTexture.active;
            RenderTexture.active = target;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply(false);
            m_Pixels = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            RenderTexture.active = prev;
        }

        public Color32 Px(int x, int y)
        {
            if (m_Pixels == null) Read();
            return m_Pixels[(height - 1 - y) * width + x];
        }

        /// <summary>Saves the target as a PNG (to look at when a pixel check fails).</summary>
        public string Dump(string name)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = target;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vu_dump");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, name + ".png");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return path;
        }

        public void Invalidate() => m_Pixels = null;

        public static bool Near(Color32 c, int r, int g, int b, int tol = 6) =>
            Math.Abs(c.r - r) <= tol && Math.Abs(c.g - g) <= tol && Math.Abs(c.b - b) <= tol;

        public static string Hex(Color32 c) => $"({c.r},{c.g},{c.b},{c.a})";

        public void Dispose()
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
