using System.Collections;
using System.Collections.Generic;
using FirstSymbol.Vectra;
using FirstSymbol.Vectra.Geometry;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.VectraPerf
{
    public sealed partial class PerfMain
    {
        // V6: 100 shapes with a shadow, a glow or a blur (GPU), and V7: 200 shapes under a mask.
        IEnumerator Fx()
        {
            var canvas = PerfScene.MakeCanvas(Root);
            PerfScene.MakeCamera(Root, true);
            yield return Collect();
            string[] effects = { "shadow", "glow", "blur" };
            for (int e = 0; e < effects.Length; e++)
            {
                string kind = effects[e];
                if (Side == "ref" && kind != "shadow") continue;
                var container = PerfScene.MakeContainer(canvas.transform, "Fx");
                var row = Row("V6" + (char)('a' + e), "100 shapes 56x56 with " + (Side == "ref" ? "an Image and the uGUI Shadow component (no blur: the closest uGUI has)" : kind));
                yield return Frames(row, "fx", 12, i =>
                {
                    if (i != 0) return;
                    var once = Measure(() =>
                    {
                        for (int k = 0; k < 100; k++)
                        {
                            Vector2 pos = GridPos(k, 20, 64f, 14f);
                            if (Side == "ref")
                            {
                                var img = PerfScene.MakeImage(container, "F" + k, PerfScene.Kind.RoundRect, pos, new Vector2(56, 56), PerfScene.Tint(k));
                                var sh = img.gameObject.AddComponent<Shadow>();
                                sh.effectDistance = new Vector2(4, -4);
                                sh.effectColor = new Color(0, 0, 0, 0.35f);
                                continue;
                            }

                            var rt = PerfScene.Place(container, "F" + k, pos, new Vector2(56, 56));
                            var s = rt.gameObject.AddComponent<VectraShape>();
                            s.SetGeometry<RectangleShape>().cornerRadii = new Vector4(12, 12, 12, 12);
                            s.SetFill(PerfScene.Tint(k));
                            if (kind == "shadow") s.AddEffect<DropShadowEffect>();
                            else if (kind == "glow") s.AddEffect<OuterGlowEffect>();
                            else s.AddEffect<LayerBlurEffect>();
                        }
                    });
                    PutOnce(row, "create", once);
                }, 0);
                yield return Frames(row, "fx_steady", 200, null);
                Mem(row, "m1");
                yield return Clear(container);
            }

            // V7: masks.
            string[] masks = { "stencil", "soft", "rect" };
            for (int mIndex = 0; mIndex < masks.Length; mIndex++)
            {
                string mode = masks[mIndex];
                if (Side == "ref" && mode == "soft") continue;
                var container = PerfScene.MakeContainer(canvas.transform, "Masked");
                var row = Row("V7" + (char)('a' + mIndex), "200 shapes 60x60 in a 800x440 window, " + mode + (Side == "ref" ? " (uGUI Mask / RectMask2D, Images)" : " mask"));
                yield return Frames(row, "mask", 12, i =>
                {
                    if (i != 0) return;
                    var once = Measure(() =>
                    {
                        var window = PerfScene.Place(container, "Window", new Vector2(100, 100), new Vector2(800, 440));
                        if (mode == "rect")
                        {
                            window.gameObject.AddComponent<RectMask2D>();
                        }
                        else if (Side == "ref")
                        {
                            var back = window.gameObject.AddComponent<Image>();
                            back.sprite = PerfScene.SpriteOf(PerfScene.Kind.RoundRect);
                            back.type = Image.Type.Sliced;
                            back.color = new Color(0.2f, 0.2f, 0.25f);
                            window.gameObject.AddComponent<Mask>();
                        }
                        else
                        {
                            var back = window.gameObject.AddComponent<VectraShape>();
                            back.SetGeometry<RectangleShape>().cornerRadii = new Vector4(40, 40, 40, 40);
                            back.SetFill(new Color(0.2f, 0.2f, 0.25f));
                            var mask = window.gameObject.AddComponent<VectraMask>();
                            mask.Mode = mode == "soft" ? VectraMaskMode.Soft : VectraMaskMode.Stencil;
                            mask.Softness = 8f;
                        }

                        for (int k = 0; k < 200; k++)
                        {
                            Vector2 pos = new Vector2(-20f + (k % 16) * 56f, -20f + (k / 16) * 56f);
                            if (Side == "ref") PerfScene.MakeImage(window, "M" + k, PerfScene.KindOf(k), pos, new Vector2(48, 48), PerfScene.Tint(k));
                            else PerfScene.MakeVectra(window, k, pos, new Vector2(48, 48));
                        }
                    });
                    PutOnce(row, "create", once);
                }, 0);
                yield return Frames(row, "mask_steady", 200, null);
                yield return Clear(container);
            }
        }

        // V10: boolean operations. The geometry is built on the CPU every time a shape is rebuilt.
        IEnumerator Booleans()
        {
            var canvas = PerfScene.MakeCanvas(Root);
            PerfScene.MakeCamera(Root, true);
            yield return Collect();

            for (int variant = 0; variant < 3; variant++)
            {
                string name = variant == 0 ? "exact" : variant == 1 ? "smooth" : "group";
                var container = PerfScene.MakeContainer(canvas.transform, "Bool");
                var shapes = new List<RectTransform>();
                var row = Row("V10" + (char)('a' + variant),
                    variant == 0 ? "100 shapes: union of a rounded rectangle and an ellipse, minus a star, exact geometry"
                    : variant == 1 ? "100 shapes: the same three operands, smooth blend 6"
                    : "50 VectraBooleanGroup objects with three child shapes (the children are the operands)");
                int count = variant == 2 ? 50 : 100;
                yield return Frames(row, "b", 12, i =>
                {
                    if (i != 0) return;
                    var once = Measure(() =>
                    {
                        for (int k = 0; k < count; k++)
                        {
                            Vector2 pos = GridPos(k, 20, 64f, 14f);
                            var rt = PerfScene.Place(container, "B" + k, pos, new Vector2(56, 56));
                            shapes.Add(rt);
                            var s = rt.gameObject.AddComponent<VectraShape>();
                            s.SetFill(PerfScene.Tint(k));
                            if (variant < 2)
                            {
                                var b = s.SetGeometry<BooleanShape>();
                                b.smoothness = variant == 1 ? 6f : 0f;
                                b.operands.Clear();
                                b.operands.Add(new BooleanOperand(new RectangleShape { cornerRadii = new Vector4(10, 10, 10, 10) }, BooleanOp.Union, new Rect(0f, 0f, 0.7f, 0.7f)));
                                b.operands.Add(new BooleanOperand(new EllipseShape(), BooleanOp.Union, new Rect(0.3f, 0.3f, 0.7f, 0.7f)));
                                b.operands.Add(new BooleanOperand(new StarShape(), BooleanOp.Subtract, new Rect(0.25f, 0.25f, 0.5f, 0.5f)));
                            }
                            else
                            {
                                rt.gameObject.AddComponent<VectraBooleanGroup>();
                                var r = PerfScene.Place(rt, "A", new Vector2(0, 0), new Vector2(40, 40));
                                r.gameObject.AddComponent<VectraShape>().SetGeometry<RectangleShape>();
                                var e1 = PerfScene.Place(rt, "B", new Vector2(16, 16), new Vector2(40, 40));
                                e1.gameObject.AddComponent<VectraShape>().SetGeometry<EllipseShape>();
                                var st = PerfScene.Place(rt, "C", new Vector2(14, 14), new Vector2(28, 28));
                                st.gameObject.AddComponent<VectraShape>().SetGeometry<StarShape>();
                            }
                        }
                    });
                    PutOnce(row, "create", once);
                }, 0);
                // Rebuild: the shapes change size every frame (variant 2: the children do).
                yield return Frames(row, "rebuild", 40, f =>
                {
                    float s = 56f + (f & 1) * 2f;
                    for (int k = 0; k < shapes.Count; k++)
                    {
                        if (variant < 2) shapes[k].sizeDelta = new Vector2(s, s);
                        else
                            for (int c = 0; c < 3; c++)
                            {
                                var child = (RectTransform)shapes[k].GetChild(c);
                                child.sizeDelta = new Vector2(40 + (f & 1) * 2, 40 + (f & 1) * 2);
                            }
                    }
                });
                yield return Frames(row, "steady", 100, null);
                yield return Clear(container);
            }
        }
    }
}
