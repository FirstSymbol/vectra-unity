using FirstSymbol.Vectra.Geometry;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FirstSymbol.Vectra.Samples
{
    /// <summary>
    /// World-space shapes rendered by VectraRenderer: a ring of rotating cards seen by a perspective camera,
    /// sorting order, a generated PolygonCollider2D and exact hit tests (click a card to flip its colour; the
    /// scene's camera needs a Physics2DRaycaster and an EventSystem, which the sample adds when missing).
    /// </summary>
    public class VectraWorldSample : MonoBehaviour
    {
        [SerializeField] int m_Count = 10;
        [SerializeField] float m_Radius = 4f;

        Transform m_Ring;
        VectraRenderer m_Sun;

        void Start()
        {
            var cam = Camera.main;
            if (cam != null && cam.GetComponent<Physics2DRaycaster>() == null) cam.gameObject.AddComponent<Physics2DRaycaster>();
            EnsureEventSystem();

            m_Ring = new GameObject("Ring").transform;
            m_Ring.SetParent(transform, false);
            for (int i = 0; i < m_Count; i++)
            {
                float a = i * Mathf.PI * 2f / m_Count;
                var card = Renderer<RectangleShape>(m_Ring, "Card " + i, new Vector3(Mathf.Cos(a) * m_Radius, 0f, Mathf.Sin(a) * m_Radius), new Vector2(1.4f, 2f));
                ((RectangleShape)card.Geometry).cornerRadii = Vector4.one * 0.18f;
                card.Content.fills[0].paint = VectraPaint.Linear(Color.HSVToRGB(i / (float)m_Count, 0.6f, 1f), Color.HSVToRGB(i / (float)m_Count + 0.08f, 0.8f, 0.7f), 90f);
                card.Content.strokes.Add(new StrokeLayer(Color.white, 0.04f) { alignment = StrokeAlignment.Inside });
                card.Content.effects.Add(new InnerGlowEffect { size = 0.15f, color = new Color(1f, 1f, 1f, 0.35f) });
                card.transform.rotation = Quaternion.LookRotation(card.transform.position);
                var icon = Renderer<StarShape>(card.transform, "Icon", new Vector3(0f, 0f, -0.01f), new Vector2(0.8f, 0.8f));
                ((StarShape)icon.Geometry).cornerRadius = 0.02f;
                icon.Content.fills[0].paint = VectraPaint.Solid(Color.white);
                icon.SortingOrder = 1;
                card.gameObject.AddComponent<ClickToRecolour>();
                card.SetDirty();
            }

            // A 2D "sun" with a glow and a collider generated from its outline.
            m_Sun = Renderer<StarShape>(transform, "Sun", new Vector3(0f, 2.6f, 0f), new Vector2(1.8f, 1.8f));
            var star = (StarShape)m_Sun.Geometry;
            star.points = 12;
            star.cornerRadius = 0.03f;
            m_Sun.Content.fills[0].paint = VectraPaint.Radial(new Color(1f, 0.95f, 0.6f), new Color(1f, 0.55f, 0.2f));
            m_Sun.Content.effects.Add(new OuterGlowEffect { size = 0.35f, color = new Color(1f, 0.7f, 0.2f, 0.8f) });
            m_Sun.gameObject.AddComponent<ClickToRecolour>();
            m_Sun.SetDirty();

            // A floor made of a stretched checker pattern.
            var floor = Renderer<EllipseShape>(transform, "Floor", new Vector3(0f, -1.2f, 0f), new Vector2(12f, 12f));
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.Content.fills[0].paint = new VectraPaint
            {
                type = PaintType.Checker, color = new Color(0.16f, 0.17f, 0.22f), secondaryColor = new Color(0.22f, 0.23f, 0.3f), patternSize = 0.6f
            };
            floor.SortingOrder = -10;
            floor.SetDirty();
        }

        void Update()
        {
            if (m_Ring != null) m_Ring.Rotate(0f, 12f * Time.deltaTime, 0f);
            if (m_Sun != null)
            {
                m_Sun.Geometry.rotation = Time.time * 20f;
                m_Sun.SetDirty();
            }
        }

        /// <summary>Adds an EventSystem with the input module of the active input handling (new Input System or legacy).</summary>
        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var module = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            go.AddComponent(module ?? typeof(StandaloneInputModule));
        }

        static VectraRenderer Renderer<T>(Transform parent, string name, Vector3 pos, Vector2 size) where T : VectraShapeGeometry, new()
        {
            var go = new GameObject(name, typeof(VectraRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var r = go.GetComponent<VectraRenderer>();
            r.SetGeometry<T>();
            r.Size = size;
            r.Content.fills.Clear();
            r.Content.fills.Add(new FillLayer(VectraPaint.Solid(Color.white)));
            return r;
        }

        /// <summary>Flips the fill colours when the exact outline is clicked (uses the generated collider).</summary>
        class ClickToRecolour : MonoBehaviour, IPointerClickHandler
        {
            void Start()
            {
                var r = GetComponent<VectraRenderer>();
                var col = gameObject.AddComponent<PolygonCollider2D>();
                var paths = VectraOutline.GetOutline(r.Geometry, r.Frame);
                col.pathCount = paths.Count;
                for (int i = 0; i < paths.Count; i++) col.SetPath(i, paths[i]);
            }

            public void OnPointerClick(PointerEventData e)
            {
                var r = GetComponent<VectraRenderer>();
                var p = r.Content.fills[0].paint;
                if (p.IsGradient && p.gradient.stops.Count > 1)
                    (p.gradient.stops[0], p.gradient.stops[p.gradient.stops.Count - 1]) =
                        (new VectraGradientStop(0f, p.gradient.stops[p.gradient.stops.Count - 1].color), new VectraGradientStop(1f, p.gradient.stops[0].color));
                else p.color = Color.HSVToRGB(Random.value, 0.6f, 1f);
                r.SetDirty();
            }
        }
    }
}
