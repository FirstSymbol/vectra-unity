using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.TextEngines;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.Editor.Tests
{
    /// <summary>A text backend that only records what it was asked to draw.</summary>
    sealed class FakeBackend : IVectraTextBackend
    {
        readonly Func<Object, bool> m_Accepts;
        public bool usable;
        public int builds;
        public VectraTextSpec last;

        public FakeBackend(VectraTextEngine engine, bool usable = true, Func<Object, bool> accepts = null)
        {
            Engine = engine;
            this.usable = usable;
            m_Accepts = accepts ?? (_ => true);
        }

        public static string NameOf(VectraTextEngine engine) => "Fake " + engine;
        public static bool Made(GameObject go, VectraTextEngine engine) => go.name == NameOf(engine);

        public VectraTextEngine Engine { get; }
        public Type FontAssetType => typeof(Object);
        public bool AcceptsFont(Object font) => m_Accepts(font);
        public bool IsUsable(VectraFontMap map, Object fallbackFont) => usable;

        public void Build(GameObject go, in VectraTextSpec spec)
        {
            builds++;
            last = spec;
            go.name = NameOf(Engine); // an Editor-folder script cannot be attached, so the name is the mark
        }

        public bool supportsWorld = true;
        public int worldBuilds;
        public bool SupportsWorld => supportsWorld;

        public void BuildWorld(GameObject go, in VectraTextSpec spec)
        {
            worldBuilds++;
            last = spec;
            go.name = NameOf(Engine);
        }

        public string ReadText(GameObject go) => last.element != null ? last.element.text : null;
    }

    /// <summary>Saves the backend registry and the project engine, and puts them back on dispose.</summary>
    sealed class RegistryScope : IDisposable
    {
        static readonly VectraTextEngine[] k_All =
            { VectraTextEngine.Lettera, VectraTextEngine.TextMeshPro, VectraTextEngine.Legacy };

        readonly IVectraTextBackend[] m_Saved = new IVectraTextBackend[k_All.Length];
        readonly VectraTextEngine m_Project;

        public RegistryScope(bool clear = false)
        {
            for (int i = 0; i < k_All.Length; i++) m_Saved[i] = VectraTextBackends.Get(k_All[i]);
            m_Project = FirstSymbol.Vectra.Rendering.VectraRuntimeSettings.ProjectTextEngine;
            if (clear)
            {
                VectraTextBackends.Unregister(VectraTextEngine.Lettera);
                VectraTextBackends.Unregister(VectraTextEngine.TextMeshPro);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < k_All.Length; i++)
            {
                if (m_Saved[i] != null) VectraTextBackends.Register(m_Saved[i]);
                else VectraTextBackends.Unregister(k_All[i]);
            }

            FirstSymbol.Vectra.Rendering.VectraRuntimeSettings.ProjectTextEngine = m_Project;
        }
    }

    /// <summary>Builds SVG images on a canvas and cleans them up.</summary>
    sealed class ImageScope : IDisposable
    {
        public const string Svg =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 200' width='400' height='200'>" +
            "<rect width='400' height='200' fill='#223'/>" +
            "<text x='20' y='60' font-size='24' fill='#ffffff'>Hello <tspan font-weight='bold' fill='#ff8800'>bold</tspan> and " +
            "<tspan font-style='italic' font-size='36'>big</tspan></text>" +
            "<text x='380' y='120' font-size='20' text-anchor='end'>right</text>" +
            "<text x='200' y='170' font-size='20' text-anchor='middle' transform='rotate(-10 200 170)'>middle</text></svg>";

        readonly List<Object> m_Objects = new List<Object>();
        public readonly Canvas canvas;

        public ImageScope()
        {
            var go = new GameObject("Vectra test canvas", typeof(Canvas));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            m_Objects.Add(go);
        }

        public VectraSvgImage Image(string svg = Svg, VectraTextEngineChoice engine = VectraTextEngineChoice.UseProjectSetting, float size = 400f)
        {
            var go = new GameObject("Svg", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(size, size * 0.5f);
            var img = go.AddComponent<VectraSvgImage>();
            var doc = SvgParser.Parse(svg);
            m_Objects.Add(doc);
            img.TextEngine = engine;
            img.Document = doc;
            img.RebuildGenerated();
            return img;
        }

        /// <summary>The generated text objects of an image, in document order.</summary>
        public static List<GameObject> Texts(VectraSvgImage img)
        {
            var list = new List<GameObject>();
            foreach (Transform c in img.transform)
                if (c.name == "SVG Text") list.Add(c.gameObject);
            return list;
        }

        public void Dispose()
        {
            foreach (var o in m_Objects)
                if (o != null) Object.DestroyImmediate(o);
        }
    }
}
