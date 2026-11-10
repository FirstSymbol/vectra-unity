using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.VectraPerf
{
    public sealed partial class PerfMain
    {
        const int CanvasCount = 1000;

        static Vector2 GridPos(int i, int cols, float step, float pad) => new Vector2(pad + (i % cols) * step, pad + (i / cols) * step);

        Graphic MakeCanvasItem(Transform parent, int i, Vector2 pos, Vector2 size)
        {
            if (Side == "ref") return PerfScene.MakeImage(parent, "I" + i, PerfScene.KindOf(i), pos, size, PerfScene.Tint(i));
            return PerfScene.MakeVectra(parent, i, pos, size);
        }

        // V1 V2 V3 V4 V5 V11 and the memory rows of a Canvas with a thousand shapes (the mix of PerfScene.Style).
        IEnumerator Canvas1000()
        {
            var canvas = PerfScene.MakeCanvas(Root);
            PerfScene.MakeCamera(Root, true);
            var mem = Row("MEM_canvas", "1000 shapes of the mix; m0 before, m1 with them on screen, m2 after they are destroyed and collected");
            yield return Collect();
            Mem(mem, "m0");
            var container = PerfScene.MakeContainer(canvas.transform, "Items");
            var graphics = new List<Graphic>(CanvasCount);
            var bases = new Vector2[CanvasCount];

            // V1: create 1000 and the first frame that draws them.
            var v1 = Row("V1", "create 1000 in one frame; first_* is that frame (Canvas rebuild and the first draw)");
            yield return Frames(v1, "v1", 12, i =>
            {
                if (i != 0) return;
                var once = Measure(() =>
                {
                    for (int k = 0; k < CanvasCount; k++)
                    {
                        bases[k] = GridPos(k, 50, 24f, 4f);
                        graphics.Add(MakeCanvasItem(container, k, bases[k], new Vector2(20, 20)));
                    }
                });
                PutOnce(v1, "create", once);
            }, 0);

            // V4: a frame of 1000 that do not change.
            yield return Frames(Row("V4", "1000 unchanging shapes, steady frames"), "v4", 300, null);
            Mem(mem, "m1", true);

            // V3: colour of 100 per frame.
            yield return Frames(Row("V3", "colour of 100 shapes changes every frame"), "v3", 200, f =>
            {
                for (int k = 0; k < 100; k++) graphics[k].color = Color.HSVToRGB((k * 0.01f + f * 0.02f + 2f) % 1f, 0.6f, 0.9f);
            });

            // V5: 200 tweened (position, rotation, scale), as an animation drives them.
            yield return Frames(Row("V5", "200 shapes move, rotate and pulse every frame"), "v5", 200, f =>
            {
                float t = f * 0.05f;
                for (int k = 0; k < 200; k++)
                {
                    var rt = graphics[k].rectTransform;
                    rt.anchoredPosition = new Vector2(bases[k].x + Mathf.Sin(t + k) * 6f, -bases[k].y + Mathf.Cos(t + k) * 6f);
                    rt.localRotation = Quaternion.Euler(0f, 0f, t * 30f + k);
                    rt.localScale = Vector3.one * (1f + 0.1f * Mathf.Sin(t * 2f + k));
                }
            });
            for (int k = 0; k < 200; k++)
            {
                var rt = graphics[k].rectTransform;
                rt.anchoredPosition = new Vector2(bases[k].x, -bases[k].y);
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            }

            yield return Settle(5);

            // V2: rebuild 1000: the size of every shape changes.
            yield return Frames(Row("V2", "size of all 1000 changes by 2 px every frame (a full rebuild of each)"), "v2", 40, f =>
            {
                float s = 20f + (f & 1) * 2f;
                var size = new Vector2(s, s);
                for (int k = 0; k < CanvasCount; k++) graphics[k].rectTransform.sizeDelta = size;
            });
            for (int k = 0; k < CanvasCount; k++) graphics[k].rectTransform.sizeDelta = new Vector2(20, 20);
            yield return Settle(5);

            // V11: deactivate and reactivate the 1000.
            yield return Toggle(Row("V11", "the parent of 1000 is deactivated, then reactivated; off_* and on_* are the call and the frame after it"), container.gameObject, 12);

            yield return Collect();
            Mem(mem, "m1c");
            Destroy(canvas.gameObject);
            graphics.Clear();
            yield return null;
            yield return null;
            yield return Collect();
            Mem(mem, "m2");
        }

        IEnumerator Toggle(PerfRow row, GameObject go, int rounds)
        {
            var offCall = new List<double>();
            var offSpan = new List<double>();
            var offAlloc = new List<double>();
            var onCall = new List<double>();
            var onSpan = new List<double>();
            var onAlloc = new List<double>();
            for (int r = -2; r < rounds; r++)
            {
                long t0 = Stopwatch.GetTimestamp();
                long a0 = AllocNow();
                go.SetActive(false);
                double call = Ms(Stopwatch.GetTimestamp() - t0);
                yield return s_EofPublic;
                double span = Ms(Stopwatch.GetTimestamp() - t0);
                long al = a0 < 0 ? -1 : AllocNow() - a0;
                yield return null;
                if (al < 0) al = FrameAlloc();
                if (r >= 0)
                {
                    offCall.Add(call);
                    offSpan.Add(span);
                    if (al >= 0) offAlloc.Add(al);
                }

                yield return null;
                t0 = Stopwatch.GetTimestamp();
                a0 = AllocNow();
                go.SetActive(true);
                call = Ms(Stopwatch.GetTimestamp() - t0);
                yield return s_EofPublic;
                span = Ms(Stopwatch.GetTimestamp() - t0);
                al = a0 < 0 ? -1 : AllocNow() - a0;
                yield return null;
                if (al < 0) al = FrameAlloc();
                if (r >= 0)
                {
                    onCall.Add(call);
                    onSpan.Add(span);
                    if (al >= 0) onAlloc.Add(al);
                }

                yield return null;
            }

            row.Put("off_call_ms", Median(offCall));
            row.Put("off_span_ms", Median(offSpan));
            if (offAlloc.Count > 0) row.Put("off_alloc_b", Median(offAlloc));
            row.Put("on_call_ms", Median(onCall));
            row.Put("on_span_ms", Median(onSpan));
            if (onAlloc.Count > 0) row.Put("on_alloc_b", Median(onAlloc));
            row.Put("on_span_max_ms", Pct(onSpan, 1.0));
        }

        static readonly WaitForEndOfFrame s_EofPublic = new WaitForEndOfFrame();
    }
}
