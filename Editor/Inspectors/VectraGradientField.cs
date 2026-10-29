using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>
    /// Gradient editor bound to a serialized <see cref="VectraGradient"/>: unlimited stops (click the bar to add,
    /// drag markers to move, Delete/× to remove), per-stop HDR colour, midpoint, interpolation space and presets.
    /// </summary>
    public class VectraGradientField : VisualElement
    {
        readonly SerializedProperty m_Gradient;
        readonly SerializedProperty m_Stops;
        readonly VisualElement m_Bar;
        readonly VisualElement m_Markers;
        readonly VisualElement m_StopEditor;
        Texture2D m_Preview;
        int m_Selected;
        int m_Dragging = -1;

        public VectraGradientField(SerializedProperty gradient)
        {
            m_Gradient = gradient;
            m_Stops = gradient.FindPropertyRelative("stops");
            style.marginTop = 2;
            style.marginBottom = 4;

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            header.Add(new Label("Gradient") { style = { width = 120 } });
            var interp = new PropertyField(gradient.FindPropertyRelative("interpolation"), "") { style = { flexGrow = 1 } };
            interp.RegisterValueChangeCallback(_ => Refresh());
            header.Add(interp);
            var presets = new Button(ShowPresets) { text = "▾", tooltip = "Presets and tools" };
            header.Add(presets);
            Add(header);

            m_Bar = new VisualElement
            {
                style =
                {
                    height = 22, marginTop = 4, borderTopLeftRadius = 3, borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3, borderBottomRightRadius = 3, overflow = Overflow.Hidden,
                    borderLeftWidth = 1, borderRightWidth = 1, borderTopWidth = 1, borderBottomWidth = 1,
                    borderLeftColor = new Color(0, 0, 0, 0.5f), borderRightColor = new Color(0, 0, 0, 0.5f),
                    borderTopColor = new Color(0, 0, 0, 0.5f), borderBottomColor = new Color(0, 0, 0, 0.5f)
                },
                tooltip = "Click to add a stop"
            };
            m_Bar.RegisterCallback<PointerDownEvent>(OnBarDown);
            Add(m_Bar);

            m_Markers = new VisualElement { style = { height = 14, position = Position.Relative } };
            m_Markers.RegisterCallback<PointerMoveEvent>(OnDrag);
            m_Markers.RegisterCallback<PointerUpEvent>(e =>
            {
                m_Dragging = -1;
                m_Markers.ReleasePointer(e.pointerId);
            });
            Add(m_Markers);

            m_StopEditor = new VisualElement();
            Add(m_StopEditor);

            this.TrackPropertyValue(m_Stops, _ => Refresh());
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (m_Preview != null) Object.DestroyImmediate(m_Preview);
            });
            Refresh();
        }

        VectraGradient Current()
        {
            var g = new VectraGradient();
            g.interpolation = (GradientInterpolation)m_Gradient.FindPropertyRelative("interpolation").enumValueIndex;
            for (int i = 0; i < m_Stops.arraySize; i++)
            {
                var s = m_Stops.GetArrayElementAtIndex(i);
                g.stops.Add(new VectraGradientStop(s.FindPropertyRelative("position").floatValue,
                    s.FindPropertyRelative("color").colorValue, s.FindPropertyRelative("midpoint").floatValue));
            }

            return g;
        }

        void Refresh()
        {
            if (m_Stops.serializedObject.targetObject == null) return;
            m_Stops.serializedObject.UpdateIfRequiredOrScript();
            var g = Current();
            const int w = 256;
            if (m_Preview == null)
                m_Preview = new Texture2D(w, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * 2];
            for (int x = 0; x < w; x++)
            {
                var c = g.Evaluate(x / (w - 1f));
                // Checker behind translucent colours.
                var bg = ((x / 8) % 2 == 0) ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.55f, 0.55f, 0.55f);
                var o = Color.Lerp(bg, new Color(c.r, c.g, c.b), c.a);
                o.a = 1f;
                px[x] = px[x + w] = o;
            }

            m_Preview.SetPixels(px);
            m_Preview.Apply();
            m_Bar.style.backgroundImage = new StyleBackground(m_Preview);

            m_Markers.Clear();
            m_Selected = Mathf.Clamp(m_Selected, 0, Mathf.Max(0, m_Stops.arraySize - 1));
            for (int i = 0; i < m_Stops.arraySize; i++)
            {
                int index = i;
                var s = m_Stops.GetArrayElementAtIndex(i);
                var color = s.FindPropertyRelative("color").colorValue;
                var marker = new VisualElement
                {
                    style =
                    {
                        position = Position.Absolute, width = 10, height = 12, marginLeft = -5,
                        left = Length.Percent(s.FindPropertyRelative("position").floatValue * 100f),
                        backgroundColor = new Color(color.r, color.g, color.b, 1f),
                        borderLeftWidth = 2, borderRightWidth = 2, borderTopWidth = 2, borderBottomWidth = 2,
                        borderTopLeftRadius = 2, borderTopRightRadius = 2, borderBottomLeftRadius = 2, borderBottomRightRadius = 2
                    }
                };
                var border = i == m_Selected ? new Color(1f, 0.75f, 0.2f) : new Color(0.1f, 0.1f, 0.1f);
                marker.style.borderLeftColor = marker.style.borderRightColor = marker.style.borderTopColor = marker.style.borderBottomColor = border;
                marker.RegisterCallback<PointerDownEvent>(e =>
                {
                    m_Selected = index;
                    m_Dragging = index;
                    m_Markers.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    Refresh();
                });
                m_Markers.Add(marker);
            }

            BuildStopEditor();
        }

        void BuildStopEditor()
        {
            m_StopEditor.Clear();
            if (m_Stops.arraySize == 0) return;
            var s = m_Stops.GetArrayElementAtIndex(m_Selected);
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var color = new ColorField { value = s.FindPropertyRelative("color").colorValue, hdr = true, showAlpha = true, style = { flexGrow = 1 } };
            color.RegisterValueChangedCallback(e =>
            {
                s.FindPropertyRelative("color").colorValue = e.newValue;
                m_Stops.serializedObject.ApplyModifiedProperties();
            });
            row.Add(color);
            var pos = new FloatField { value = s.FindPropertyRelative("position").floatValue * 100f, style = { width = 54 }, tooltip = "Position (%)" };
            pos.RegisterValueChangedCallback(e =>
            {
                s.FindPropertyRelative("position").floatValue = Mathf.Clamp01(e.newValue / 100f);
                m_Stops.serializedObject.ApplyModifiedProperties();
            });
            row.Add(pos);
            row.Add(new Label("%"));
            var mid = new Slider(0.01f, 0.99f) { value = s.FindPropertyRelative("midpoint").floatValue, style = { width = 70 }, tooltip = "Midpoint to the next stop" };
            mid.RegisterValueChangedCallback(e =>
            {
                s.FindPropertyRelative("midpoint").floatValue = e.newValue;
                m_Stops.serializedObject.ApplyModifiedProperties();
            });
            row.Add(mid);
            var del = new Button(() =>
            {
                if (m_Stops.arraySize <= 2) return;
                m_Stops.DeleteArrayElementAtIndex(m_Selected);
                m_Selected = Mathf.Max(0, m_Selected - 1);
                m_Stops.serializedObject.ApplyModifiedProperties();
            }) { text = "×", tooltip = "Remove stop" };
            del.SetEnabled(m_Stops.arraySize > 2);
            row.Add(del);
            m_StopEditor.Add(row);
        }

        void OnBarDown(PointerDownEvent e)
        {
            float t = Mathf.Clamp01(e.localPosition.x / Mathf.Max(m_Bar.layout.width, 1f));
            var c = Current().Evaluate(t);
            int i = m_Stops.arraySize;
            m_Stops.InsertArrayElementAtIndex(i);
            var s = m_Stops.GetArrayElementAtIndex(i);
            s.FindPropertyRelative("position").floatValue = t;
            s.FindPropertyRelative("color").colorValue = c;
            s.FindPropertyRelative("midpoint").floatValue = 0.5f;
            m_Selected = i;
            m_Stops.serializedObject.ApplyModifiedProperties();
        }

        void OnDrag(PointerMoveEvent e)
        {
            if (m_Dragging < 0 || m_Dragging >= m_Stops.arraySize) return;
            float t = Mathf.Clamp01(e.localPosition.x / Mathf.Max(m_Markers.layout.width, 1f));
            m_Stops.GetArrayElementAtIndex(m_Dragging).FindPropertyRelative("position").floatValue = t;
            m_Stops.serializedObject.ApplyModifiedProperties();
        }

        void ShowPresets()
        {
            var menu = new GenericMenu();
            void Preset(string name, params Color[] colors) => menu.AddItem(new GUIContent("Presets/" + name), false, () => SetStops(colors));
            Preset("White → Black", Color.white, Color.black);
            Preset("Sunset", new Color(1f, 0.37f, 0.33f), new Color(1f, 0.75f, 0.3f));
            Preset("Ocean", new Color(0.1f, 0.3f, 0.8f), new Color(0.3f, 0.9f, 0.95f));
            Preset("Purple Haze", new Color(0.4f, 0.2f, 0.9f), new Color(0.95f, 0.35f, 0.7f));
            Preset("Rainbow", Color.red, Color.yellow, Color.green, Color.cyan, Color.blue, Color.magenta, Color.red);
            Preset("Fade Out", Color.white, new Color(1, 1, 1, 0));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Reverse"), false, () =>
            {
                for (int i = 0; i < m_Stops.arraySize; i++)
                {
                    var p = m_Stops.GetArrayElementAtIndex(i).FindPropertyRelative("position");
                    p.floatValue = 1f - p.floatValue;
                }

                m_Stops.serializedObject.ApplyModifiedProperties();
            });
            menu.AddItem(new GUIContent("Distribute Evenly"), false, () =>
            {
                for (int i = 0; i < m_Stops.arraySize; i++)
                    m_Stops.GetArrayElementAtIndex(i).FindPropertyRelative("position").floatValue = m_Stops.arraySize > 1 ? i / (m_Stops.arraySize - 1f) : 0f;
                m_Stops.serializedObject.ApplyModifiedProperties();
            });
            menu.ShowAsContext();
        }

        void SetStops(Color[] colors)
        {
            m_Stops.ClearArray();
            for (int i = 0; i < colors.Length; i++)
            {
                m_Stops.InsertArrayElementAtIndex(i);
                var s = m_Stops.GetArrayElementAtIndex(i);
                s.FindPropertyRelative("position").floatValue = colors.Length > 1 ? i / (colors.Length - 1f) : 0f;
                s.FindPropertyRelative("color").colorValue = colors[i];
                s.FindPropertyRelative("midpoint").floatValue = 0.5f;
            }

            m_Selected = 0;
            m_Stops.serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomPropertyDrawer(typeof(VectraGradient))]
    class VectraGradientDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => new VectraGradientField(property);
    }
}
