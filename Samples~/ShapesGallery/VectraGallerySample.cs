using System.Collections.Generic;
using FirstSymbol.Vectra;
using FirstSymbol.Vectra.Geometry;
using UnityEngine;

namespace FirstSymbol.Vectra.Samples
{
    /// <summary>
    /// Builds a gallery of Vectra shapes under this RectTransform at startup and animates a few of them
    /// (trim path, marching dashes, fill amount, morphing and a rotating gradient) to show runtime control.
    /// </summary>
    public class VectraGallerySample : MonoBehaviour
    {
        [SerializeField] float m_Cell = 200f;
        [SerializeField] float m_Size = 150f;
        [SerializeField] int m_Columns = 8;

        VectraShape m_Trim, m_Dash, m_Progress, m_Morph, m_Spin;

        void Start()
        {
            var builders = new List<System.Action<VectraShape>>
            {
                s => { s.SetGeometry<RectangleShape>().cornerRadii = Vector4.one * 24; s.SetFill(VectraPaint.Linear(new Color(0.36f, 0.6f, 1f), new Color(0.65f, 0.3f, 1f), -45)); s.AddEffect<DropShadowEffect>().color = new Color(0, 0, 0, 0.6f); },
                s => { s.SetGeometry<EllipseShape>(); s.SetFill(VectraPaint.Radial(new Color(1f, 0.9f, 0.4f), new Color(1f, 0.35f, 0.2f))); s.SetStroke(Color.white, 4); },
                s => { var p = s.SetGeometry<PolygonShape>(); p.sides = 6; p.cornerRadius = 12; s.SetFill(new Color(0.95f, 0.45f, 0.5f)); s.AddEffect<InnerShadowEffect>(); },
                s => { s.SetGeometry<StarShape>().cornerRadius = 4; s.SetFill(new Color(1f, 0.8f, 0.2f)); s.AddEffect<OuterGlowEffect>(); },
                s => { s.SetGeometry<HeartShape>(); s.SetFill(new Color(1f, 0.3f, 0.45f)); s.AddEffect<BevelEffect>(); },
                s => { s.SetGeometry<GearShape>(); s.SetFill(new Color(0.75f, 0.78f, 0.85f)); s.AddEffect<DropShadowEffect>(); },
                s => { s.SetGeometry<SquircleShape>(); s.SetFill(new VectraPaint { type = PaintType.AngularGradient, gradient = Rainbow(), gradientStart = new Vector2(0.5f, 0.5f), gradientEnd = new Vector2(1f, 0.5f) }); m_Spin = s; },
                s => { s.SetGeometry<RhombusShape>().cornerRadius = 8; s.SetFill(new VectraPaint { type = PaintType.Stripes, color = new Color(1f, 0.6f, 0.2f), secondaryColor = new Color(0.2f, 0.1f, 0.05f), patternSize = 14, patternRotation = 45 }); },
                s => { var b = s.SetGeometry<BooleanShape>(); b.operands[1].operation = BooleanOp.Subtract; s.SetFill(new Color(0.4f, 0.7f, 1f)); s.SetStroke(Color.white, 3); },
                s => { var b = s.SetGeometry<BooleanShape>(); b.smoothness = 30; b.operands[0].geometry = new EllipseShape(); s.SetFill(new Color(0.5f, 1f, 0.7f)); },
                s => { s.SetGeometry<MorphShape>(); s.SetFill(new Color(1f, 0.6f, 0.3f)); m_Morph = s; },
                s => { s.SetGeometry<SpiralShape>(); s.Fills.Clear(); s.Strokes.Add(new StrokeLayer(new Color(0.4f, 1f, 0.9f), 6) { cap = LineCap.Round }); m_Trim = s; },
                s => { var r = s.SetGeometry<RectangleShape>(); r.cornerRadii = Vector4.one * 30; r.cornerStyle = CornerStyle.Squircle; s.SetFill(new Color(0.15f, 0.15f, 0.2f)); s.Strokes.Add(new StrokeLayer(Color.white, 4) { dashed = true, dashPattern = new List<float> { 12, 8 }, cap = LineCap.Round }); m_Dash = s; },
                s => { var a = s.SetGeometry<ArcShape>(); a.innerRadius = 0.75f; a.sweepAngle = 360; s.SetFill(new Color(0.3f, 1f, 0.6f)); m_Progress = s; s.AddEffect<FillAmountEffect>(); },
                s => { s.SetGeometry<SpeechBubbleShape>(); s.SetFill(Color.white); s.AddEffect<DropShadowEffect>(); },
                s => { s.SetGeometry<EllipseShape>(); s.SetFill(new VectraPaint { type = PaintType.Freeform, freeformPoints = new List<FreeformPoint> { new FreeformPoint(new Vector2(0.1f, 0.1f), Color.red), new FreeformPoint(new Vector2(0.9f, 0.2f), Color.yellow), new FreeformPoint(new Vector2(0.5f, 0.9f), Color.blue) } }); },
            };

            int rows = Mathf.CeilToInt(builders.Count / (float)m_Columns);
            for (int i = 0; i < builders.Count; i++)
            {
                var go = new GameObject("Shape " + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
                go.transform.SetParent(transform, false);
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(m_Size, m_Size);
                rt.anchoredPosition = new Vector2((i % m_Columns - (m_Columns - 1) * 0.5f) * m_Cell, ((rows - 1) * 0.5f - i / m_Columns) * m_Cell);
                var shape = go.GetComponent<VectraShape>();
                builders[i](shape);
                shape.SetShapeDirty();
            }
        }

        void Update()
        {
            float t = Time.time;
            if (m_Trim != null)
            {
                var s = m_Trim.Strokes[0];
                s.trimEnd = Mathf.PingPong(t * 0.5f, 1f);
                m_Trim.SetShapeDirty();
            }

            if (m_Dash != null)
            {
                m_Dash.Strokes[0].dashOffset = -t * 30f;
                m_Dash.SetShapeDirty();
            }

            if (m_Progress != null)
            {
                m_Progress.GetEffect<FillAmountEffect>().amount = Mathf.Repeat(t * 0.25f, 1f);
                m_Progress.SetShapeDirty();
            }

            if (m_Morph != null)
            {
                ((MorphShape)m_Morph.Geometry).t = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(t * 0.4f, 1f));
                m_Morph.SetShapeDirty();
            }

            if (m_Spin != null)
            {
                float a = t;
                var p = m_Spin.Fills[0].paint;
                p.gradientEnd = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
                m_Spin.SetShapeDirty();
            }
        }

        static VectraGradient Rainbow()
        {
            var g = new VectraGradient { interpolation = GradientInterpolation.OkLab };
            g.stops.Add(new VectraGradientStop(0f, Color.red));
            g.stops.Add(new VectraGradientStop(0.33f, Color.green));
            g.stops.Add(new VectraGradientStop(0.66f, Color.blue));
            g.stops.Add(new VectraGradientStop(1f, Color.red));
            return g;
        }
    }
}
