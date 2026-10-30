using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Vectra.Samples
{
    /// <summary>
    /// Animation three ways: fluent tweens from code (drawing a checkmark, an ease comparison, a loading
    /// spinner, morphing), an AnimationClip keying VectraAnimatedProperties (the same works in the Animator
    /// and Timeline), and per-frame code driving dash offsets.
    /// </summary>
    public class VectraAnimationSample : MonoBehaviour
    {
        Font m_Font;
        VectraShape m_Marching;

        void Start()
        {
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 1. Draw a checkmark inside a circle, then pop the circle: tweens chained with OnComplete.
            var circle = Shape<EllipseShape>("Circle", new Vector2(-560, 200), new Vector2(170, 170));
            circle.Fills.Clear();
            circle.SetStroke(new Color(0.3f, 0.85f, 0.55f), 8f);
            circle.Strokes[0].cap = LineCap.Round;
            var check = Shape<CheckmarkShape>("Check", new Vector2(-560, 200), new Vector2(90, 90));
            check.Fills.Clear();
            check.SetStroke(new Color(0.3f, 0.85f, 0.55f), 10f);
            check.Strokes[0].cap = LineCap.Round;
            check.Strokes[0].join = LineJoin.Round;
            void Play()
            {
                circle.Fills.Clear();
                circle.Strokes[0].trimEnd = 0f;
                circle.SetShapeDirty();
                check.Strokes[0].trimEnd = 0f;
                check.SetShapeDirty();
                circle.TweenTrim(0f, 1f, 0.7f).SetEase(VectraEase.InOutCubic).OnComplete(() =>
                    check.TweenTrim(0f, 1f, 0.4f).SetEase(VectraEase.OutCubic).OnComplete(() =>
                    {
                        circle.Fills.Add(new FillLayer(new VectraPaint { color = new Color(0.3f, 0.85f, 0.55f, 0.25f), opacity = 0f }));
                        // An empty tween works as a delay before the loop starts again.
                        circle.TweenFillOpacity(1f, 0.3f).OnComplete(() => VectraTween.Create(circle, 1.2f, _ => { }).OnComplete(Play));
                    }));
            }
            Play();
            Label("Chained tweens", new Vector2(-560, 80));

            // 2. Ease comparison: the same move with different curves, looping back and forth.
            var eases = new[] { VectraEase.Linear, VectraEase.InOutSine, VectraEase.OutCubic, VectraEase.OutBack, VectraEase.OutElastic, VectraEase.OutBounce };
            for (int i = 0; i < eases.Length; i++)
            {
                var y = 290 - i * 36;
                var track = Shape<RectangleShape>("Track", new Vector2(-40, y), new Vector2(420, 4));
                track.SetFill(new Color(1f, 1f, 1f, 0.12f));
                var dot = Shape<EllipseShape>(eases[i] + " dot", new Vector2(-250, y), new Vector2(22, 22));
                dot.SetFill(Color.HSVToRGB(i / (float)eases.Length, 0.6f, 1f));
                var rt = dot.rectTransform;
                VectraTween.Float(dot, -250f, 170f, 1.4f, x => rt.anchoredPosition = new Vector2(x, y)).SetEase(eases[i]).SetLoops(-1, true);
                Label(eases[i].ToString(), new Vector2(-330, y), 140, TextAnchor.MiddleRight);
            }

            // 3. Spinner: rotation + trim breathing, looping forever.
            var spinner = Shape<ArcShape>("Spinner", new Vector2(420, 200), new Vector2(150, 150));
            var arc = (ArcShape)spinner.Geometry;
            arc.innerRadius = 0.82f;
            arc.sweepAngle = 270f;
            spinner.SetFill(VectraPaint.Linear(new Color(0.4f, 0.6f, 1f), new Color(0.8f, 0.4f, 1f), 0f));
            spinner.TweenRotation(-360f, 1.2f).SetEase(VectraEase.Linear).SetLoops(-1);
            VectraTween.Float(spinner, 40f, 300f, 0.9f, v =>
            {
                arc.sweepAngle = v;
                spinner.SetShapeDirty();
            }).SetEase(VectraEase.InOutSine).SetLoops(-1, true);
            Label("Rotation + sweep", new Vector2(420, 80));

            // 4. Morphing between two shapes.
            var morph = Shape<MorphShape>("Morph", new Vector2(-560, -200), new Vector2(160, 160));
            morph.SetFill(VectraPaint.Radial(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.4f, 0.3f)));
            morph.TweenMorph(1f, 1.2f).SetEase(VectraEase.InOutCubic).SetLoops(-1, true);
            Label("TweenMorph", new Vector2(-560, -320));

            // 5. An AnimationClip keying VectraAnimatedProperties (legacy Animation here; the same curves work in
            //    an Animator controller or a Timeline animation track).
            var keyed = Shape<StarShape>("Keyed", new Vector2(0, -200), new Vector2(170, 170));
            keyed.SetFill(new Color(1f, 0.8f, 0.3f));
            keyed.SetStroke(Color.white, 5f);
            keyed.AddEffect<FillAmountEffect>();
            var props = keyed.gameObject.AddComponent<VectraAnimatedProperties>();
            props.driveFillAmount = true;
            props.driveTrim = true;
            props.driveRotation = true;
            props.driveFillColor = true;
            var clip = new AnimationClip { legacy = true, wrapMode = WrapMode.PingPong, name = "Keyed shape" };
            var t = typeof(VectraAnimatedProperties);
            clip.SetCurve("", t, "fillAmount", AnimationCurve.EaseInOut(0f, 0f, 1.5f, 1f));
            clip.SetCurve("", t, "trimEnd", AnimationCurve.EaseInOut(0f, 0.1f, 1.5f, 1f));
            clip.SetCurve("", t, "rotation", AnimationCurve.EaseInOut(0f, 0f, 1.5f, 72f));
            clip.SetCurve("", t, "fillColor.r", AnimationCurve.Linear(0f, 1f, 1.5f, 0.4f));
            clip.SetCurve("", t, "fillColor.g", AnimationCurve.Linear(0f, 0.8f, 1.5f, 0.6f));
            clip.SetCurve("", t, "fillColor.b", AnimationCurve.Linear(0f, 0.3f, 1.5f, 1f));
            clip.SetCurve("", t, "fillColor.a", AnimationCurve.Constant(0f, 1.5f, 1f));
            var anim = keyed.gameObject.AddComponent<Animation>();
            anim.AddClip(clip, clip.name);
            anim.clip = clip;
            anim.Play();
            Label("AnimationClip → Animated Properties", new Vector2(0, -320), 420);

            // 6. Per-frame code: marching dashes.
            m_Marching = Shape<RectangleShape>("Marching ants", new Vector2(420, -200), new Vector2(220, 150));
            ((RectangleShape)m_Marching.Geometry).cornerRadii = Vector4.one * 20f;
            m_Marching.SetFill(new Color(1f, 1f, 1f, 0.05f));
            m_Marching.Strokes.Add(new StrokeLayer(Color.white, 3f) { dashed = true, dashPattern = new List<float> { 10f, 8f }, cap = LineCap.Round });
            Label("Dash offset from Update()", new Vector2(420, -320));
        }

        void Update()
        {
            if (m_Marching == null) return;
            m_Marching.Strokes[0].dashOffset = -Time.time * 40f;
            m_Marching.SetShapeDirty();
        }

        VectraShape Shape<T>(string name, Vector2 pos, Vector2 size) where T : VectraShapeGeometry, new()
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var s = go.GetComponent<VectraShape>();
            s.SetGeometry<T>();
            s.SetFill(Color.white);
            return s;
        }

        void Label(string text, Vector2 pos, float width = 300f, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width, 30);
            rt.anchoredPosition = pos;
            var t = go.GetComponent<Text>();
            t.font = m_Font;
            t.fontSize = 18;
            t.alignment = align;
            t.color = new Color(0.65f, 0.7f, 0.8f);
            t.text = text;
            t.raycastTarget = false;
        }
    }
}
