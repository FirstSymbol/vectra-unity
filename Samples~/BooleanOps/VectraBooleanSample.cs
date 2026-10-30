using FirstSymbol.Vectra.Geometry;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Vectra.Samples
{
    /// <summary>
    /// Boolean operations: one VectraBooleanGroup per operation (its children are the operands and move at
    /// runtime), a smooth union of orbiting blobs, and a shape built from a BooleanShape in code.
    /// </summary>
    public class VectraBooleanSample : MonoBehaviour
    {
        readonly RectTransform[] m_Movers = new RectTransform[4];
        readonly RectTransform[] m_Blobs = new RectTransform[4];
        VectraBooleanGroup[] m_Groups = new VectraBooleanGroup[4];
        VectraBooleanGroup m_Smooth;
        Font m_Font;

        void Start()
        {
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var ops = new[] { BooleanOp.Union, BooleanOp.Subtract, BooleanOp.Intersect, BooleanOp.Exclude };
            var colors = new[] { new Color(0.35f, 0.6f, 1f), new Color(1f, 0.45f, 0.45f), new Color(0.4f, 0.9f, 0.6f), new Color(1f, 0.75f, 0.3f) };
            for (int i = 0; i < 4; i++)
            {
                var pos = new Vector2(-540 + i * 360, 180);
                var group = Shape<RectangleShape>(transform, ops[i] + " group", pos, new Vector2(260, 260));
                group.SetFill(colors[i]);
                group.SetStroke(Color.white, 3f, StrokeAlignment.Inside);
                group.AddEffect<DropShadowEffect>().blur = 14f;
                m_Groups[i] = group.gameObject.AddComponent<VectraBooleanGroup>();

                var a = Shape<RectangleShape>(group.transform, "Base", new Vector2(-30, 0), new Vector2(150, 150));
                ((RectangleShape)a.Geometry).cornerRadii = Vector4.one * 24f;
                var b = Shape<EllipseShape>(group.transform, "Operand", new Vector2(40, 30), new Vector2(140, 140));
                b.gameObject.AddComponent<VectraBooleanOperand>().operation = ops[i];
                m_Movers[i] = b.rectTransform;
                m_Groups[i].Sync();
                Label(ops[i].ToString(), pos + new Vector2(0, -165));
            }

            // Smooth union: metaballs from a group with smoothness.
            var smooth = Shape<RectangleShape>(transform, "Smooth group", new Vector2(-300, -220), new Vector2(380, 280));
            smooth.SetFill(VectraPaint.Linear(new Color(0.55f, 0.35f, 1f), new Color(0.2f, 0.8f, 1f), 30f));
            smooth.AddEffect<InnerGlowEffect>().color = new Color(1f, 1f, 1f, 0.5f);
            m_Smooth = smooth.gameObject.AddComponent<VectraBooleanGroup>();
            m_Smooth.Smoothness = 40f;
            for (int i = 0; i < 4; i++)
            {
                var blob = Shape<EllipseShape>(smooth.transform, "Blob " + i, Vector2.zero, new Vector2(100, 100) * (1f - i * 0.12f));
                blob.gameObject.AddComponent<VectraBooleanOperand>();
                m_Blobs[i] = blob.rectTransform;
            }

            m_Smooth.Sync();
            Label("Smooth union (metaballs)", new Vector2(-300, -380));

            // A boolean defined entirely in code: a ring with a notch and a crossbar.
            var code = Shape<BooleanShape>(transform, "BooleanShape in code", new Vector2(300, -220), new Vector2(260, 260));
            var shape = (BooleanShape)code.Geometry;
            shape.operands.Clear();
            shape.operands.Add(new BooleanOperand(new EllipseShape(), BooleanOp.Union, new Rect(0f, 0f, 1f, 1f)));
            shape.operands.Add(new BooleanOperand(new EllipseShape(), BooleanOp.Subtract, new Rect(0.22f, 0.22f, 0.56f, 0.56f)));
            shape.operands.Add(new BooleanOperand(new RectangleShape(), BooleanOp.Union, new Rect(0.05f, 0.44f, 0.9f, 0.12f)));
            shape.operands.Add(new BooleanOperand(new TriangleShape(), BooleanOp.Subtract, new Rect(0.4f, 0.75f, 0.2f, 0.3f)));
            code.SetFill(new Color(0.95f, 0.95f, 1f));
            code.SetStroke(new Color(0.2f, 0.5f, 1f), 4f, StrokeAlignment.Outside);
            code.SetShapeDirty();
            Label("BooleanShape built in code", new Vector2(300, -380));
        }

        void Update()
        {
            float t = Time.time;
            for (int i = 0; i < 4; i++)
            {
                if (m_Movers[i] == null) continue;
                m_Movers[i].anchoredPosition = new Vector2(40f + Mathf.Sin(t * 0.9f + i) * 45f, 30f + Mathf.Cos(t * 0.7f + i) * 35f);
            }

            for (int i = 0; i < 4; i++)
            {
                if (m_Blobs[i] == null) continue;
                float a = t * (0.6f + i * 0.25f) + i * 1.7f;
                m_Blobs[i].anchoredPosition = new Vector2(Mathf.Cos(a) * (60f + i * 18f), Mathf.Sin(a * 1.3f) * (40f + i * 10f));
            }
        }

        static VectraShape Shape<T>(Transform parent, string name, Vector2 pos, Vector2 size) where T : VectraShapeGeometry, new()
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var s = go.GetComponent<VectraShape>();
            s.SetGeometry<T>();
            s.SetFill(Color.white);
            return s;
        }

        void Label(string text, Vector2 pos)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(360, 30);
            rt.anchoredPosition = pos;
            var t = go.GetComponent<Text>();
            t.font = m_Font;
            t.fontSize = 20;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(0.65f, 0.7f, 0.8f);
            t.text = text;
            t.raycastTarget = false;
        }
    }
}
