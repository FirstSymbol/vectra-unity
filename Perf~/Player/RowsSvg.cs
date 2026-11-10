using System;
using System.Collections;
using System.Collections.Generic;
using FirstSymbol.Vectra;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.VectraPerf
{
    public sealed partial class PerfMain
    {
        const string IconHead = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' width='24' height='24'>";

        // Eight icons of the kind an app ships: filled paths, strokes, a dash, a gradient, opacity.
        public static readonly string[] Icons =
        {
            IconHead + "<path d='M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z' fill='#333'/></svg>",
            IconHead + "<path d='M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z' fill='#e8a317'/></svg>",
            IconHead + "<path d='M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z' fill='#d32f2f'/></svg>",
            IconHead + "<path d='M5 13l4 4L19 7' fill='none' stroke='#2a7' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round'/></svg>",
            IconHead + "<circle cx='10' cy='10' r='6' fill='none' stroke='#333' stroke-width='2'/><path d='M15 15l6 6' stroke='#333' stroke-width='2' stroke-linecap='round'/></svg>",
            IconHead + "<rect x='3' y='5' width='18' height='2' rx='1' fill='#333'/><rect x='3' y='11' width='18' height='2' rx='1' fill='#333'/><rect x='3' y='17' width='18' height='2' rx='1' fill='#333'/></svg>",
            IconHead + "<defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='#ff8800'/><stop offset='1' stop-color='#cc0044'/></linearGradient></defs><path d='M8 5v14l11-7z' fill='url(#g)'/></svg>",
            IconHead + "<circle cx='12' cy='12' r='9' fill='none' stroke='#555' stroke-width='2' stroke-dasharray='3 2'/><circle cx='12' cy='12' r='4' fill='#555' fill-opacity='0.6'/></svg>",
        };

        public sealed class CorpusDoc
        {
            public string name, text;
        }

        public static List<CorpusDoc> Corpus()
        {
            var list = new List<CorpusDoc>();
            var assets = Resources.LoadAll<TextAsset>("PerfCorpus");
            Array.Sort(assets, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var a in assets) list.Add(new CorpusDoc { name = a.name, text = a.text });
            return list;
        }

        List<string> SvgVariants()
        {
            var v = new List<string>();
            if (Side != "vectra") v.Add("-");
            else if (Variants.Length > 0) v.AddRange(Variants);
            else v.AddRange(new[] { "browser", "linear" });
            if (Reverse) v.Reverse();
            return v;
        }

        // The icon a thing shows: a Vectra document, or a picture for the references.
        sealed class IconSet
        {
            public VectraSvgDocument[] docs;
            public Sprite[] sprites;
        }

        IconSet MakeIcons()
        {
            var set = new IconSet();
            if (Side == "vectra")
            {
                set.docs = new VectraSvgDocument[Icons.Length];
                for (int i = 0; i < Icons.Length; i++) set.docs[i] = VectraSvg.Parse(Icons[i]);
            }
            else if (Side == "vg")
            {
                set.sprites = new Sprite[Icons.Length];
                for (int i = 0; i < Icons.Length; i++) set.sprites[i] = PerfBridge.Vg.BuildSprite(Icons[i]);
            }

            return set;
        }

        Graphic MakeIcon(IconSet set, Transform parent, int k, Vector2 pos, Vector2 size, string blend)
        {
            int icon = k % Icons.Length;
            if (Side == "ref") return PerfScene.MakeImage(parent, "I" + k, PerfScene.Kind.RoundRect, pos, size, PerfScene.Tint(k));
            if (Side == "vg") return PerfBridge.Vg.MakeImage(parent, "I" + k, set.sprites[icon], pos, size);
            var rt = PerfScene.Place(parent, "I" + k, pos, size);
            var img = rt.gameObject.AddComponent<VectraSvgImage>();
            img.ColorBlending = blend == "linear" ? SvgColorBlendingChoice.Linear : SvgColorBlendingChoice.Browser;
            img.Document = set.docs[icon];
            return img;
        }

        // V8: 300 icons on screen. In a Linear project the Browser blend draws each picture through a gamma scope into a texture.
        // V9: a scroll list of 500 icons moving one step every frame.
        IEnumerator SvgIcons()
        {
            var canvas = PerfScene.MakeCanvas(Root);
            PerfScene.MakeCamera(Root, true);
            yield return Collect();
            if (Side == "vg" && PerfBridge.Vg == null)
            {
                R.error = "the vector graphics package is not in this player";
                yield break;
            }

            foreach (string blend in SvgVariants())
            {
                string tag = blend == "-" ? "" : "_" + blend;
                var set = MakeIcons();
                yield return Collect();
                var row = Row("V8" + tag, "300 icons 40x40 " + (Side == "vectra" ? "VectraSvgImage, colour blending " + blend : Side == "vg" ? "SVGImage with a sprite per icon (com.unity.vectorgraphics)" : "uGUI Image with one raster sprite (a floor, not an SVG)"));
                Mem(row, "pre");
                var container = PerfScene.MakeContainer(canvas.transform, "Icons");
                yield return Frames(row, "v8", 12, i =>
                {
                    if (i != 0) return;
                    var once = Measure(() =>
                    {
                        for (int k = 0; k < 300; k++) MakeIcon(set, container, k, GridPos(k, 30, 42f, 4f), new Vector2(40, 40), blend);
                    });
                    PutOnce(row, "create", once);
                }, 0);
                yield return Frames(row, "v8s", 200, null);
                Mem(row, "post", true);
                yield return Clear(container);

                // V9
                var list = Row("V9" + tag, "500 icons 48x48 in a 640x600 RectMask2D window, content scrolled 13 px per frame");
                var window = PerfScene.Place(canvas.transform, "Window", new Vector2(100, 60), new Vector2(640, 600));
                window.gameObject.AddComponent<RectMask2D>();
                var content = PerfScene.Place(window, "Content", Vector2.zero, new Vector2(640, 500 * 52));
                yield return Frames(list, "v9", 12, i =>
                {
                    if (i != 0) return;
                    var once = Measure(() =>
                    {
                        for (int k = 0; k < 500; k++) MakeIcon(set, content, k, new Vector2(8, 4 + k * 52), new Vector2(48, 48), blend);
                    });
                    PutOnce(list, "create", once);
                }, 0);
                yield return Frames(list, "v9s", 300, f => content.anchoredPosition = new Vector2(0, f * 13f));
                yield return Clear(window);
                if (set.docs != null) foreach (var d in set.docs) if (d != null) Destroy(d);
                if (set.sprites != null) foreach (var s in set.sprites) if (s != null) { Destroy(s.texture); Destroy(s); }
                yield return Collect();
                Mem(row, "rel");
            }
        }

        // I2: parse time and allocation per document. S1: sprite generation.
        IEnumerator Parse()
        {
            var docs = Corpus();
            var row = Row("I2", "parse of each corpus document, median of 15 after 3 warm-ups; d<N>_ms / d<N>_alloc_b by sorted name");
            var names = new List<string>();
            double total = 0, worst = 0, totalAlloc = 0;
            bool gotAlloc = false;
            string worstName = "";
            int failed = 0;
            for (int n = 0; n < docs.Count; n++)
            {
                var d = docs[n];
                names.Add(n + "=" + d.name);
                var times = new List<double>();
                bool ok = true;
                try
                {
                    for (int w = 0; w < 3; w++) ParseOnce(d.text);
                    for (int it = 0; it < 15; it++)
                    {
                        var o = Measure(() => ParseOnce(d.text));
                        times.Add(o.ms);
                    }
                }
                catch (Exception e)
                {
                    ok = false;
                    failed++;
                    row.note += " d" + n + " failed: " + e.GetType().Name;
                }

                if (!ok) continue;
                double ms = Median(times);
                row.Put("d" + n + "_ms", ms);
                total += ms;
                // Allocation: ten parses in one frame, read from that frame's counter (development players only).
                yield return null;
                for (int k = 0; k < 10; k++) ParseOnce(d.text);
                yield return null;
                long frameAlloc = FrameAlloc();
                if (frameAlloc >= 0)
                {
                    row.Put("d" + n + "_alloc_b", frameAlloc / 10.0);
                    totalAlloc += frameAlloc / 10.0;
                    gotAlloc = true;
                }
                if (ms > worst)
                {
                    worst = ms;
                    worstName = d.name;
                }

                yield return null;
            }

            row.Put("total_ms", total);
            if (gotAlloc) row.Put("total_alloc_b", totalAlloc);
            row.Put("worst_ms", worst);
            row.Put("docs", docs.Count);
            row.Put("failed", failed);
            row.note += " worst=" + worstName + " | " + string.Join(",", names);

            var icons = Row("I2_icons", "parse of the eight icons of V8, median of 50");
            double iconTotal = 0;
            double iconAlloc = 0;
            bool iconAllocKnown = false;
            for (int n = 0; n < Icons.Length; n++)
            {
                for (int w = 0; w < 3; w++) ParseOnce(Icons[n]);
                var times = new List<double>();
                for (int it = 0; it < 50; it++)
                {
                    var o = Measure(() => ParseOnce(Icons[n]));
                    times.Add(o.ms);
                }

                iconTotal += Median(times);
                yield return null;
                for (int k = 0; k < 20; k++) ParseOnce(Icons[n]);
                yield return null;
                long frameAlloc = FrameAlloc();
                if (frameAlloc >= 0)
                {
                    iconAlloc += frameAlloc / 20.0;
                    iconAllocKnown = true;
                }
            }

            icons.Put("total_ms", iconTotal);
            if (iconAllocKnown) icons.Put("total_alloc_b", iconAlloc);
            yield return null;
        }

        void ParseOnce(string text)
        {
            if (Side == "vg")
            {
                PerfBridge.Vg.Parse(text);
                return;
            }

            var doc = VectraSvg.Parse(text);
            if (doc != null) Destroy(doc);
        }

        IEnumerator Sprites()
        {
            var docs = Corpus();
            var canvas = PerfScene.MakeCanvas(Root);
            PerfScene.MakeCamera(Root, true);
            yield return Collect();
            var row = Row("S1", "a sprite of each corpus document at 256 px; d<N>_cold_ms is the first call, d<N>_ms the median of 4 more");
            double cold = 0, warm = 0, warmAlloc = 0, worst = 0;
            string worstName = "";
            int failed = 0;
            for (int n = 0; n < docs.Count; n++)
            {
                var d = docs[n];
                VectraSvgDocument doc = null;
                try
                {
                    if (Side == "vectra") doc = VectraSvg.Parse(d.text);
                    var times = new List<double>();
                    var allocs = new List<double>();
                    double first = 0;
                    for (int it = 0; it < 5; it++)
                    {
                        Sprite sprite = null;
                        Texture2D tex = null;
                        var o = Measure(() =>
                        {
                            if (Side == "vectra")
                            {
                                var opts = VectraSpriteOptions.Default;
                                opts.resolution = 256;
                                sprite = VectraSpriteBaker.Bake(doc, opts);
                            }
                            else
                            {
                                sprite = PerfBridge.Vg.BuildSprite(d.text);
                                if (sprite != null) tex = PerfBridge.Vg.Render(sprite, 256, 256);
                            }
                        });
                        if (sprite == null) throw new InvalidOperationException("no sprite");
                        if (it == 0) first = o.ms;
                        else
                        {
                            times.Add(o.ms);
                            if (o.alloc >= 0) allocs.Add(o.alloc);
                        }

                        if (sprite.texture != null) Destroy(sprite.texture);
                        Destroy(sprite);
                        if (tex != null) Destroy(tex);
                    }

                    double ms = Median(times);
                    row.Put("d" + n + "_cold_ms", first);
                    row.Put("d" + n + "_ms", ms);
                    if (allocs.Count > 0) row.Put("d" + n + "_alloc_b", Median(allocs));
                    cold += first;
                    warm += ms;
                    if (allocs.Count > 0) warmAlloc += Median(allocs);
                    if (ms > worst)
                    {
                        worst = ms;
                        worstName = d.name;
                    }
                }
                catch (Exception e)
                {
                    failed++;
                    row.note += " d" + n + " " + d.name + " failed: " + e.GetType().Name + " " + e.Message;
                }
                finally
                {
                    if (doc != null) Destroy(doc);
                }

                yield return null;
            }

            row.Put("cold_total_ms", cold);
            row.Put("total_ms", warm);
            if (warmAlloc > 0) row.Put("total_alloc_b", warmAlloc);
            row.Put("worst_ms", worst);
            row.Put("docs", docs.Count);
            row.Put("failed", failed);
            row.note += " worst=" + worstName;

            // Shapes: 100 baked rounded shapes of the mix (Vectra only: a uGUI Image has nothing to bake).
            if (Side == "vectra")
            {
                var shapes = Row("S1_shapes", "VectraSpriteBaker.Bake of 100 shapes of the mix at 128 px (median per shape over all, 2 rounds)");
                var container = PerfScene.MakeContainer(canvas.transform, "Bake");
                var made = new List<VectraShape>();
                for (int k = 0; k < 100; k++) made.Add(PerfScene.MakeVectra(container, k, GridPos(k, 20, 64f, 14f), new Vector2(56, 56)));
                yield return Settle(3);
                var times = new List<double>();
                var allocs = new List<double>();
                for (int round = 0; round < 2; round++)
                for (int k = 0; k < 100; k++)
                {
                    Sprite sprite = null;
                    var opts = VectraSpriteOptions.Default;
                    opts.resolution = 128;
                    var o = Measure(() => sprite = VectraSpriteBaker.Bake(made[k], opts));
                    if (round == 1)
                    {
                        times.Add(o.ms);
                        if (o.alloc >= 0) allocs.Add(o.alloc);
                    }

                    if (sprite != null)
                    {
                        Destroy(sprite.texture);
                        Destroy(sprite);
                    }
                }

                shapes.Put("median_ms", Median(times));
                shapes.Put("p95_ms", Pct(times, 0.95));
                if (allocs.Count > 0) shapes.Put("alloc_median_b", Median(allocs));
                yield return Clear(container);
            }
        }
    }
}
