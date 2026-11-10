using System;
using System.Collections;
using System.Collections.Generic;
using FirstSymbol.Vectra;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.UIToolkit;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace FirstSymbol.VectraPerf
{
    public sealed partial class PerfMain
    {
        const int WorldCount = 5000;

        // W1: 5000 shapes in world space (VectraRenderer, or SpriteRenderer for the reference).
        IEnumerator World()
        {
            PerfScene.MakeCamera(Root, true, 25f);
            var mem = Row("MEM_world", "5000 world shapes; m0 before, m1 on screen, m2 after destroying and collecting");
            yield return Collect();
            Mem(mem, "m0");
            var parent = new GameObject("World").transform;
            parent.SetParent(Root, false);
            var row = Row("W1", "5000 shapes 0.7 units on a 50-unit-high orthographic view (about 10 px each); first_* is the frame after creation");
            yield return Frames(row, "w1", 12, i =>
            {
                if (i != 0) return;
                var once = Measure(() =>
                {
                    for (int k = 0; k < WorldCount; k++)
                    {
                        Vector3 pos = new Vector3(-42.5f + (k % 100) * 0.85f, -21f + (k / 100) * 0.85f, 0f);
                        if (Side == "ref")
                        {
                            var go = new GameObject("W" + k, typeof(SpriteRenderer));
                            go.transform.SetParent(parent, false);
                            go.transform.localPosition = pos;
                            go.transform.localScale = Vector3.one * (0.7f / 0.64f);
                            var sr = go.GetComponent<SpriteRenderer>();
                            sr.sprite = PerfScene.SpriteOf(PerfScene.KindOf(k));
                            sr.color = PerfScene.Tint(k);
                        }
                        else
                        {
                            var go = new GameObject("W" + k, typeof(VectraRenderer));
                            go.transform.SetParent(parent, false);
                            go.transform.localPosition = pos;
                            var r = go.GetComponent<VectraRenderer>();
                            r.Size = new Vector2(0.7f, 0.7f);
                            PerfScene.StyleWorld(r, k);
                        }
                    }
                });
                PutOnce(row, "create", once);
            }, 0);
            yield return Frames(row, "w1s", 200, null);
            Mem(mem, "m1", true);
            yield return Collect();
            Mem(mem, "m1c");
            Destroy(parent.gameObject);
            yield return null;
            yield return null;
            yield return Collect();
            Mem(mem, "m2");
        }

        // U1: 1000 UI Toolkit elements.
        IEnumerator Uitk()
        {
            var mem = Row("MEM_uitk", "1000 elements; m0 before, m1 shown, m2 after removing and collecting");
            yield return Collect();
            Mem(mem, "m0");
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            settings.clearColor = true;
            settings.colorClearValue = new Color(0.08f, 0.09f, 0.11f, 1f);
            var go = new GameObject("PerfDocument");
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            go.SetActive(true);
            yield return Settle(5);
            var root = doc.rootVisualElement;
            var made = new List<VisualElement>(CanvasCount);
            var row = Row("U1", "1000 elements 20x20 (" + (Side == "ref" ? "VisualElement with background, border and border-radius; the star becomes a rounded square" : "VectraElement, the mix of the Canvas rows") + ")");
            yield return Frames(row, "u1", 12, i =>
            {
                if (i != 0) return;
                var once = Measure(() =>
                {
                    for (int k = 0; k < CanvasCount; k++)
                    {
                        Vector2 pos = GridPos(k, 50, 24f, 4f);
                        VisualElement e = Side == "ref" ? MakeRefElement(k) : MakeVectraElement(k);
                        e.style.position = Position.Absolute;
                        e.style.left = pos.x;
                        e.style.top = pos.y;
                        e.style.width = 20;
                        e.style.height = 20;
                        root.Add(e);
                        made.Add(e);
                    }
                });
                PutOnce(row, "create", once);
            }, 0);
            yield return Frames(row, "u1s", 300, null);
            Mem(mem, "m1", true);

            var change = Row("U1b", "size of all 1000 changes by 2 px every frame");
            yield return Frames(change, "u1r", 40, f =>
            {
                float s = 20f + (f & 1) * 2f;
                for (int k = 0; k < made.Count; k++)
                {
                    made[k].style.width = s;
                    made[k].style.height = s;
                }
            });

            root.Clear();
            made.Clear();
            Destroy(go);
            yield return null;
            yield return null;
            yield return Collect();
            Mem(mem, "m2");
        }

        static VisualElement MakeVectraElement(int k)
        {
            Color c = PerfScene.Tint(k);
            var e = new VectraElement();
            switch (k % 5)
            {
                case 0:
                    e.Shape = VectraElementShape.Rectangle;
                    e.CornerRadius = 6f;
                    e.Fill = c;
                    break;
                case 1:
                    e.Shape = VectraElementShape.Ellipse;
                    e.Fill = c;
                    break;
                case 2:
                    e.Shape = VectraElementShape.Star;
                    e.Fill = c;
                    break;
                case 3:
                    e.Shape = VectraElementShape.Rectangle;
                    e.CornerRadius = 4f;
                    e.Fill = Color.clear;
                    e.Stroke = c;
                    e.StrokeWidth = 2f;
                    break;
                default:
                    e.Shape = VectraElementShape.Rectangle;
                    e.CornerRadius = 6f;
                    e.Fills[0].paint = VectraPaint.Linear(c, Color.Lerp(c, Color.black, 0.6f), 90f);
                    e.SetShapeDirty();
                    break;
            }

            return e;
        }

        static VisualElement MakeRefElement(int k)
        {
            Color c = PerfScene.Tint(k);
            var e = new VisualElement();
            var kind = k % 5;
            Length radius = kind == 1 ? Length.Percent(50) : new Length(kind == 2 ? 3 : 6);
            e.style.borderTopLeftRadius = radius;
            e.style.borderTopRightRadius = radius;
            e.style.borderBottomLeftRadius = radius;
            e.style.borderBottomRightRadius = radius;
            if (kind == 3)
            {
                e.style.borderTopWidth = 2;
                e.style.borderBottomWidth = 2;
                e.style.borderLeftWidth = 2;
                e.style.borderRightWidth = 2;
                e.style.borderTopColor = c;
                e.style.borderBottomColor = c;
                e.style.borderLeftColor = c;
                e.style.borderRightColor = c;
            }
            else
            {
                e.style.backgroundColor = kind == 4 ? Color.Lerp(c, Color.black, 0.3f) : c;
            }

            return e;
        }

        // I3: what the first use of each feature costs: the frame in which it first appears (shader variants, materials, atlases, type init).
        IEnumerator Cold()
        {
            Canvas canvas = null;
            RectTransform container = null;
            // The control: a canvas and a camera, nothing on it.
            yield return ColdStep("canvas", () =>
            {
                canvas = PerfScene.MakeCanvas(Root);
                PerfScene.MakeCamera(Root, true);
                container = PerfScene.MakeContainer(canvas.transform, "Cold");
            });
            if (Side == "vg")
            {
                if (PerfBridge.Vg == null)
                {
                    R.error = "the vector graphics package is not in this player";
                    yield break;
                }

                yield return ColdStep("svg", () =>
                {
                    var sprite = PerfBridge.Vg.BuildSprite(Icons[0]);
                    for (int k = 0; k < 8; k++) PerfBridge.Vg.MakeImage(container, "V" + k, sprite, new Vector2(10 + k * 50, 10), new Vector2(40, 40));
                });
                yield break;
            }

            bool vec = Side == "vectra";
            yield return ColdStep("shape", () =>
            {
                if (vec) PerfScene.MakeVectra(container, 0, new Vector2(10, 10), new Vector2(40, 40));
                else PerfScene.MakeImage(container, "I", PerfScene.Kind.RoundRect, new Vector2(10, 10), new Vector2(40, 40), Color.white);
            });
            yield return ColdStep("shape_more", () =>
            {
                for (int k = 1; k < 5; k++)
                {
                    if (vec) PerfScene.MakeVectra(container, k, new Vector2(10 + k * 50, 10), new Vector2(40, 40));
                    else PerfScene.MakeImage(container, "I" + k, PerfScene.KindOf(k), new Vector2(10 + k * 50, 10), new Vector2(40, 40), Color.white);
                }
            });
            yield return ColdStep("shadow", () =>
            {
                var rt = PerfScene.Place(container, "Sh", new Vector2(10, 70), new Vector2(40, 40));
                if (vec)
                {
                    var s = rt.gameObject.AddComponent<VectraShape>();
                    s.SetGeometry<RectangleShape>();
                    s.SetFill(Color.white);
                    s.AddEffect<DropShadowEffect>();
                }
                else
                {
                    var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
                    img.sprite = PerfScene.SpriteOf(PerfScene.Kind.RoundRect);
                    img.type = UnityEngine.UI.Image.Type.Sliced;
                    rt.gameObject.AddComponent<Shadow>();
                }
            });
            if (vec)
            {
                yield return ColdStep("blur", () =>
                {
                    var rt = PerfScene.Place(container, "Bl", new Vector2(60, 70), new Vector2(40, 40));
                    var s = rt.gameObject.AddComponent<VectraShape>();
                    s.SetGeometry<RectangleShape>();
                    s.SetFill(Color.white);
                    s.AddEffect<LayerBlurEffect>();
                });
                yield return ColdStep("svg_browser", () => ColdSvg(container, SvgColorBlendingChoice.Browser, 130));
                yield return ColdStep("svg_linear", () => ColdSvg(container, SvgColorBlendingChoice.Linear, 190));
            }

            yield return ColdStep("mask", () =>
            {
                var window = PerfScene.Place(container, "Mask", new Vector2(10, 130), new Vector2(120, 60));
                if (vec)
                {
                    var s = window.gameObject.AddComponent<VectraShape>();
                    s.SetGeometry<RectangleShape>();
                    s.SetFill(Color.gray);
                    window.gameObject.AddComponent<VectraMask>();
                    PerfScene.MakeVectra(window, 0, new Vector2(10, 10), new Vector2(60, 60));
                }
                else
                {
                    var img = window.gameObject.AddComponent<UnityEngine.UI.Image>();
                    img.sprite = PerfScene.SpriteOf(PerfScene.Kind.RoundRect);
                    img.type = UnityEngine.UI.Image.Type.Sliced;
                    window.gameObject.AddComponent<Mask>();
                    PerfScene.MakeImage(window, "c", PerfScene.Kind.RoundRect, new Vector2(10, 10), new Vector2(60, 60), Color.white);
                }
            });
            if (vec)
            {
                yield return ColdStep("softmask", () =>
                {
                    var window = PerfScene.Place(container, "Soft", new Vector2(150, 130), new Vector2(120, 60));
                    var s = window.gameObject.AddComponent<VectraShape>();
                    s.SetGeometry<RectangleShape>();
                    s.SetFill(Color.gray);
                    window.gameObject.AddComponent<VectraMask>().Mode = VectraMaskMode.Soft;
                    PerfScene.MakeVectra(window, 0, new Vector2(10, 10), new Vector2(60, 60));
                });
                yield return ColdStep("boolean", () =>
                {
                    var rt = PerfScene.Place(container, "Bo", new Vector2(300, 130), new Vector2(60, 60));
                    var s = rt.gameObject.AddComponent<VectraShape>();
                    s.SetFill(Color.white);
                    var b = s.SetGeometry<BooleanShape>();
                    b.operands.Add(new BooleanOperand(new StarShape(), BooleanOp.Subtract, new Rect(0.25f, 0.25f, 0.5f, 0.5f)));
                });
                yield return ColdStep("bake", () =>
                {
                    var rt = PerfScene.Place(container, "Bk", new Vector2(380, 130), new Vector2(60, 60));
                    var s = rt.gameObject.AddComponent<VectraShape>();
                    s.SetFill(Color.white);
                    var opts = VectraSpriteOptions.Default;
                    opts.resolution = 128;
                    var sprite = VectraSpriteBaker.Bake(s, opts);
                    if (sprite != null) GC.KeepAlive(sprite);
                });
            }

            yield return ColdStep("world", () =>
            {
                var go = vec ? new GameObject("Wo", typeof(VectraRenderer)) : new GameObject("Wo", typeof(SpriteRenderer));
                go.transform.position = new Vector3(0, 0, 0);
                if (vec)
                {
                    var r = go.GetComponent<VectraRenderer>();
                    r.Size = new Vector2(3, 3);
                    PerfScene.StyleWorld(r, 0);
                }
                else
                {
                    var sr = go.GetComponent<SpriteRenderer>();
                    sr.sprite = PerfScene.SpriteOf(PerfScene.Kind.RoundRect);
                }
            });
            yield return ColdStep("uitk", () =>
            {
                var settings = ScriptableObject.CreateInstance<PanelSettings>();
                settings.scaleMode = PanelScaleMode.ConstantPixelSize;
                var go = new GameObject("ColdDocument");
                go.SetActive(false);
                var doc = go.AddComponent<UIDocument>();
                doc.panelSettings = settings;
                go.SetActive(true);
                var e = vec ? (VisualElement)MakeVectraElement(0) : MakeRefElement(0);
                e.style.width = 40;
                e.style.height = 40;
                doc.rootVisualElement.Add(e);
            });
        }

        static void ColdSvg(Transform container, SvgColorBlendingChoice blend, float y)
        {
            for (int k = 0; k < Icons.Length; k++)
            {
                var rt = PerfScene.Place(container, "Svg" + k, new Vector2(10 + k * 50, y), new Vector2(40, 40));
                var img = rt.gameObject.AddComponent<VectraSvgImage>();
                img.ColorBlending = blend;
                img.Document = VectraSvg.Parse(Icons[k]);
            }
        }

        IEnumerator ColdStep(string id, Action create)
        {
            var row = Row("I3_" + id);
            yield return Frames(row, "c", 4, i =>
            {
                if (i == 0) PutOnce(row, "create", Measure(create));
            }, 0);
        }
    }
}
