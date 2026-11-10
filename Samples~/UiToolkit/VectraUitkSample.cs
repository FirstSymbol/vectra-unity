using FirstSymbol.Vectra.Svg;
using FirstSymbol.Vectra.UIToolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Samples
{
    /// <summary>
    /// Adds what UXML cannot say to the sample panel: a gradient that turns every frame (a redraw per frame, no allocation),
    /// a star with effects built in code, and an SVG document drawn by a VectraSvgElement.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class VectraUitkSample : MonoBehaviour
    {
        const string Logo =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 100'>" +
            "<rect x='4' y='4' width='192' height='92' rx='20' fill='#1d2540' stroke='#5b8cff' stroke-width='4'/>" +
            "<circle cx='50' cy='50' r='26' fill='#ffcc00'/>" +
            "<path d='M90 70 L120 30 L150 70 Z' fill='#50dca0'/>" +
            "<text x='100' y='90' font-size='16' text-anchor='middle' fill='#ffffff'>Vectra</text></svg>";

        VectraElement m_Spinner;
        VectraSvgDocument m_Document;
        float m_Angle;

        void Start()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            m_Spinner = root.Q<VectraElement>("spinner");
            if (m_Spinner != null)
            {
                m_Spinner.Fills[0].paint = VectraPaint.Linear(new Color(1f, 0.45f, 0.2f), new Color(0.65f, 0.3f, 1f), 0f);
                m_Spinner.AddEffect<OuterGlowEffect>().size = 10f;
            }

            var row = root.Q("svg-row");
            if (row == null) return;

            var star = new VectraElement { Shape = VectraElementShape.Star, Fill = new Color(1f, 0.8f, 0.2f), StrokeWidth = 3f, Stroke = Color.white };
            star.style.width = 110;
            star.style.height = 110;
            star.SetGeometry<StarShape>().points = 7;
            star.AddEffect<DropShadowEffect>().blur = 12f;
            row.Add(star);

            m_Document = SvgParser.Parse(Logo);
            var logo = new VectraSvgElement { Document = m_Document };
            logo.style.width = 240;
            logo.style.height = 120;
            row.Add(logo);
        }

        void Update()
        {
            if (m_Spinner == null) return;
            m_Angle = (m_Angle + 90f * Time.deltaTime) % 360f;
            m_Spinner.Fills[0].paint.SetLinearAngle(m_Angle);
            m_Spinner.SetShapeDirty();
        }

        void OnDestroy()
        {
            if (m_Document != null) Destroy(m_Document);
        }
    }
}
