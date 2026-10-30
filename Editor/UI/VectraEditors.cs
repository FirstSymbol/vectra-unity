using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>Gradient editor: preview bar (click to add a stop), draggable stops, stop colour/position/midpoint, presets.</summary>
    public class VxGradientEditor : VisualElement
    {
        readonly SerializedProperty m_Gradient, m_Stops;
        readonly VisualElement m_Bar, m_Markers, m_StopPanel;
        int m_Selected;
        int m_Drag = -1;

        public VxGradientEditor(SerializedProperty gradient)
        {
            m_Gradient = gradient;
            m_Stops = gradient.FindPropertyRelative("stops");

            var top = VxUI.El("vx-row");
            top.Add(VxUI.Text("Gradient", "vx-row__label"));
            var interp = VxSegmented.ForEnum(gradient.FindPropertyRelative("interpolation"), new[] { "sRGB", "Linear", "OKLab", "Step" },
                tooltips: new[] { "Interpolate authored colours (like design tools)", "Interpolate in linear light", "Perceptually uniform", "Hard steps" },
                changed: Redraw);
            top.Add(interp);
            top.Add(VxUI.IconButton(VxIcon.Kind.More, "Presets and tools", ShowMenu));
            Add(top);

            m_Bar = VxUI.El("vx-gradient-bar");
            m_Bar.tooltip = "Click to add a colour stop";
            m_Bar.generateVisualContent += DrawBar;
            m_Bar.RegisterCallback<PointerDownEvent>(AddStopAt);
            Add(m_Bar);

            m_Markers = VxUI.El("vx-gradient-markers");
            m_Markers.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (m_Drag < 0 || !m_Markers.HasPointerCapture(e.pointerId)) return;
                float t = Mathf.Clamp01(e.localPosition.x / Mathf.Max(m_Markers.layout.width, 1f));
                m_Stops.serializedObject.Update();
                m_Stops.GetArrayElementAtIndex(m_Drag).FindPropertyRelative("position").floatValue = t;
                m_Stops.serializedObject.ApplyModifiedProperties();
            });
            m_Markers.RegisterCallback<PointerUpEvent>(e =>
            {
                m_Drag = -1;
                m_Markers.ReleasePointer(e.pointerId);
            });
            Add(m_Markers);

            m_StopPanel = new VisualElement();
            Add(m_StopPanel);

            this.TrackPropertyValue(m_Stops, _ => Redraw());
            Redraw();
        }

        VectraGradient Current()
        {
            var g = new VectraGradient { interpolation = (GradientInterpolation)m_Gradient.FindPropertyRelative("interpolation").enumValueIndex };
            for (int i = 0; i < m_Stops.arraySize; i++)
            {
                var s = m_Stops.GetArrayElementAtIndex(i);
                g.stops.Add(new VectraGradientStop(s.FindPropertyRelative("position").floatValue,
                    s.FindPropertyRelative("color").colorValue, s.FindPropertyRelative("midpoint").floatValue));
            }

            return g;
        }

        void Redraw()
        {
            if (m_Stops.serializedObject.targetObject == null) return;
            m_Stops.serializedObject.UpdateIfRequiredOrScript();
            m_Bar.MarkDirtyRepaint();
            m_Selected = Mathf.Clamp(m_Selected, 0, Mathf.Max(0, m_Stops.arraySize - 1));
            m_Markers.Clear();
            for (int i = 0; i < m_Stops.arraySize; i++)
            {
                int index = i;
                var s = m_Stops.GetArrayElementAtIndex(i);
                var c = s.FindPropertyRelative("color").colorValue;
                var m = VxUI.El("vx-stop" + (i == m_Selected ? " vx-stop--selected" : ""));
                m.style.left = Length.Percent(s.FindPropertyRelative("position").floatValue * 100f);
                m.style.backgroundColor = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
                m.tooltip = "Drag to move · double-click to delete";
                m.RegisterCallback<PointerDownEvent>(e =>
                {
                    e.StopPropagation();
                    if (e.clickCount == 2 && m_Stops.arraySize > 2)
                    {
                        m_Stops.serializedObject.Update();
                        m_Stops.DeleteArrayElementAtIndex(index);
                        m_Stops.serializedObject.ApplyModifiedProperties();
                        return;
                    }

                    m_Selected = index;
                    m_Drag = index;
                    m_Markers.CapturePointer(e.pointerId);
                    Redraw();
                });
                m_Markers.Add(m);
            }

            BuildStopPanel();
        }

        void BuildStopPanel()
        {
            m_StopPanel.Clear();
            if (m_Stops.arraySize == 0) return;
            var s = m_Stops.GetArrayElementAtIndex(m_Selected);
            var row = VxUI.El("vx-row");
            row.Add(VxUI.Text($"Stop {m_Selected + 1}/{m_Stops.arraySize}", "vx-row__label"));
            var color = new ColorField { hdr = true, showAlpha = true, style = { flexGrow = 1 } };
            color.BindProperty(s.FindPropertyRelative("color"));
            row.Add(color);
            var pos = new FloatField { style = { width = 44, marginLeft = 4 }, tooltip = "Position (%)" };
            pos.SetValueWithoutNotify(Mathf.Round(s.FindPropertyRelative("position").floatValue * 1000f) / 10f);
            pos.RegisterValueChangedCallback(e =>
            {
                s.serializedObject.Update();
                s.FindPropertyRelative("position").floatValue = Mathf.Clamp01(e.newValue / 100f);
                s.serializedObject.ApplyModifiedProperties();
            });
            row.Add(pos);
            row.Add(new Label("%") { style = { marginLeft = 2, opacity = 0.6f } });
            var del = VxUI.IconButton(VxIcon.Kind.Trash, "Remove stop", () =>
            {
                if (m_Stops.arraySize <= 2) return;
                m_Stops.serializedObject.Update();
                m_Stops.DeleteArrayElementAtIndex(m_Selected);
                m_Stops.serializedObject.ApplyModifiedProperties();
            }, true);
            del.SetEnabled(m_Stops.arraySize > 2);
            row.Add(del);
            m_StopPanel.Add(row);
            if (m_Selected < m_Stops.arraySize - 1)
            {
                var mid = new Slider(0.01f, 0.99f) { showInputField = true };
                mid.BindProperty(s.FindPropertyRelative("midpoint"));
                m_StopPanel.Add(VxUI.Row("Midpoint", mid, "Where the blend to the next stop reaches 50%"));
            }
        }

        void DrawBar(MeshGenerationContext mgc)
        {
            var r = m_Bar.contentRect;
            if (r.width <= 0) return;
            var p = mgc.painter2D;
            const float cell = 6f;
            for (int y = 0; y * cell < r.height; y++)
            for (int x = 0; x * cell < r.width; x++)
            {
                p.fillColor = (x + y) % 2 == 0 ? new Color(0.78f, 0.78f, 0.78f) : new Color(0.55f, 0.55f, 0.55f);
                Quad(p, x * cell, y * cell, Mathf.Min((x + 1) * cell, r.width), Mathf.Min((y + 1) * cell, r.height));
            }

            var g = Current();
            int n = Mathf.Clamp((int)r.width / 3, 8, 128);
            for (int i = 0; i < n; i++)
            {
                var c = g.Evaluate((i + 0.5f) / n);
                p.fillColor = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a));
                Quad(p, r.width * i / n, 0, r.width * (i + 1) / n + 0.6f, r.height);
            }
        }

        static void Quad(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y0));
            p.LineTo(new Vector2(x1, y1));
            p.LineTo(new Vector2(x0, y1));
            p.ClosePath();
            p.Fill();
        }

        void AddStopAt(PointerDownEvent e)
        {
            float t = Mathf.Clamp01(e.localPosition.x / Mathf.Max(m_Bar.layout.width, 1f));
            var c = Current().Evaluate(t);
            m_Stops.serializedObject.Update();
            int i = m_Stops.arraySize;
            m_Stops.InsertArrayElementAtIndex(i);
            var s = m_Stops.GetArrayElementAtIndex(i);
            s.FindPropertyRelative("position").floatValue = t;
            s.FindPropertyRelative("color").colorValue = c;
            s.FindPropertyRelative("midpoint").floatValue = 0.5f;
            m_Selected = i;
            m_Stops.serializedObject.ApplyModifiedProperties();
        }

        void ShowMenu()
        {
            var menu = new GenericMenu();
            void Preset(string name, params Color[] colors) => menu.AddItem(new GUIContent("Presets/" + name), false, () => SetStops(colors));
            Preset("White → Black", Color.white, Color.black);
            Preset("Sunset", new Color(1f, 0.37f, 0.33f), new Color(1f, 0.75f, 0.3f));
            Preset("Ocean", new Color(0.1f, 0.3f, 0.8f), new Color(0.3f, 0.9f, 0.95f));
            Preset("Purple Haze", new Color(0.4f, 0.2f, 0.9f), new Color(0.95f, 0.35f, 0.7f));
            Preset("Mint", new Color(0.2f, 0.85f, 0.6f), new Color(0.75f, 1f, 0.6f));
            Preset("Fire", new Color(0.5f, 0f, 0f), new Color(1f, 0.3f, 0f), new Color(1f, 0.85f, 0.2f));
            Preset("Rainbow", Color.red, new Color(1, 1, 0), Color.green, Color.cyan, Color.blue, Color.magenta, Color.red);
            Preset("Fade Out", Color.white, new Color(1, 1, 1, 0));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Reverse"), false, () =>
            {
                m_Stops.serializedObject.Update();
                for (int i = 0; i < m_Stops.arraySize; i++)
                {
                    var p = m_Stops.GetArrayElementAtIndex(i).FindPropertyRelative("position");
                    p.floatValue = 1f - p.floatValue;
                }

                m_Stops.serializedObject.ApplyModifiedProperties();
            });
            menu.AddItem(new GUIContent("Distribute Evenly"), false, () =>
            {
                m_Stops.serializedObject.Update();
                for (int i = 0; i < m_Stops.arraySize; i++)
                    m_Stops.GetArrayElementAtIndex(i).FindPropertyRelative("position").floatValue = m_Stops.arraySize > 1 ? i / (m_Stops.arraySize - 1f) : 0f;
                m_Stops.serializedObject.ApplyModifiedProperties();
            });
            menu.ShowAsContext();
        }

        void SetStops(Color[] colors)
        {
            m_Stops.serializedObject.Update();
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

    /// <summary>Paint editor: type tiles followed by the settings of the selected type.</summary>
    public class VxPaintEditor : VisualElement
    {
        static readonly PaintType[] s_Types =
        {
            PaintType.Solid, PaintType.LinearGradient, PaintType.RadialGradient, PaintType.AngularGradient,
            PaintType.DiamondGradient, PaintType.Freeform, PaintType.Texture, PaintType.Stripes, PaintType.Checker,
            PaintType.Dots, PaintType.Grid, PaintType.Noise
        };

        readonly SerializedProperty m_Paint;
        readonly VisualElement m_Tiles, m_Body;
        readonly Label m_Name;
        readonly bool m_ShowBlend;

        public VxPaintEditor(SerializedProperty paint, bool showBlend = true)
        {
            m_Paint = paint;
            m_ShowBlend = showBlend;
            m_Tiles = VxUI.El("vx-paint-types");
            Add(m_Tiles);
            m_Name = VxUI.Text("", "vx-paint-name");
            m_Body = new VisualElement();
            Add(m_Body);
            foreach (var t in s_Types)
            {
                var type = t;
                var tile = VxUI.El("vx-paint-type");
                tile.tooltip = VxPaint.NameOf(t);
                tile.Add(new VxIcon(VxPaint.IconOf(t), 16));
                tile.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    m_Paint.serializedObject.Update();
                    m_Paint.FindPropertyRelative("type").enumValueIndex = (int)type;
                    m_Paint.serializedObject.ApplyModifiedProperties();
                    Rebuild();
                });
                tile.userData = type;
                m_Tiles.Add(tile);
            }

            Rebuild();
        }

        PaintType Type => (PaintType)m_Paint.FindPropertyRelative("type").enumValueIndex;

        SerializedProperty P(string name) => m_Paint.FindPropertyRelative(name);
        VisualElement F(string name, string label = null) => VxFields.For(P(name), typeof(VectraPaint), label);

        void Rebuild()
        {
            m_Paint.serializedObject.UpdateIfRequiredOrScript();
            var type = Type;
            foreach (var t in m_Tiles.Children()) t.EnableInClassList("vx-paint-type--active", (PaintType)t.userData == type);
            m_Name.text = VxPaint.NameOf(type);
            m_Body.Clear();
            switch (type)
            {
                case PaintType.Solid:
                    m_Body.Add(F("color", "Color"));
                    break;
                case PaintType.LinearGradient:
                case PaintType.RadialGradient:
                case PaintType.AngularGradient:
                case PaintType.DiamondGradient:
                    m_Body.Add(new VxGradientEditor(P("gradient")));
                    if (P("useCustomMapping").boolValue)
                    {
                        m_Body.Add(VxUI.Text("This gradient uses an imported (SVG) transform. Turn it off to edit the handles.", "vx-hint"));
                        m_Body.Add(F("useCustomMapping", "Imported Mapping"));
                        break;
                    }

                    if (type == PaintType.LinearGradient) m_Body.Add(VxUI.Row("Angle", LinearAngle(), "Direction of the gradient"));
                    else
                    {
                        m_Body.Add(F("gradientStart", "Center"));
                        m_Body.Add(F("gradientEnd", "Radius Handle"));
                        var aspect = new Slider(0.05f, 4f) { showInputField = true };
                        aspect.BindProperty(P("gradientAspect"));
                        m_Body.Add(VxUI.Row("Aspect", aspect, "Ratio of the second axis to the first"));
                        if (type == PaintType.RadialGradient) m_Body.Add(F("focalPoint", "Focal Point"));
                    }

                    m_Body.Add(VxUI.Row("Spread", VxSegmented.ForEnum(P("spread"), new[] { "Pad", "Repeat", "Reflect" }), "What happens outside the gradient range"));
                    if (type == PaintType.LinearGradient)
                    {
                        var handles = new Foldout { text = "Handles (normalized)", value = false };
                        handles.Add(F("gradientStart", "Start"));
                        handles.Add(F("gradientEnd", "End"));
                        m_Body.Add(handles);
                    }

                    break;
                case PaintType.Texture:
                    m_Body.Add(F("texture", "Texture"));
                    m_Body.Add(F("color", "Tint"));
                    m_Body.Add(VxUI.Row("Fit", VxSegmented.ForEnum(P("textureFit"))));
                    m_Body.Add(F("textureScale", "Scale"));
                    m_Body.Add(F("textureOffset", "Offset"));
                    m_Body.Add(F("textureRotation", "Rotation"));
                    m_Body.Add(F("textureUvRect", "UV Rect"));
                    break;
                case PaintType.Freeform:
                    m_Body.Add(new VxFreeformPoints(P("freeformPoints")));
                    break;
                default:
                    m_Body.Add(F("color", "Color A"));
                    m_Body.Add(F("secondaryColor", "Color B"));
                    var size = new Slider(1f, 128f) { showInputField = true };
                    size.BindProperty(P("patternSize"));
                    m_Body.Add(VxUI.Row("Cell Size", size, "Size of one pattern cell in local units"));
                    if (type != PaintType.Noise && type != PaintType.Checker) m_Body.Add(F("patternRatio", "Ratio"));
                    if (type != PaintType.Noise) m_Body.Add(F("patternRotation", "Rotation"));
                    m_Body.Add(F("patternOffset", "Offset"));
                    break;
            }

            m_Body.Add(F("opacity", "Opacity"));
            if (m_ShowBlend)
                m_Body.Add(VxUI.Row("Blend", VxSegmented.ForEnum(P("blend"), new[] { "Normal", "Additive" }, new[] { VxIcon.Kind.BlendNormal, VxIcon.Kind.BlendAdd }), "How this layer mixes with the layers below it"));
        }

        /// <summary>Angle of a linear gradient, derived from and written back to its normalized handles.</summary>
        VisualElement LinearAngle()
        {
            var start = P("gradientStart");
            var end = P("gradientEnd");
            float Get()
            {
                var d = end.vector2Value - start.vector2Value;
                return Mathf.Round(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }

            void Set(float deg)
            {
                m_Paint.serializedObject.Update();
                var c = (start.vector2Value + end.vector2Value) * 0.5f;
                float half = Mathf.Max((end.vector2Value - start.vector2Value).magnitude * 0.5f, 0.05f);
                var d = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * half;
                start.vector2Value = c - d;
                end.vector2Value = c + d;
                m_Paint.serializedObject.ApplyModifiedProperties();
            }

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var dial = new VxDial(Get, Set);
            row.Add(dial);
            var field = new FloatField { value = Get(), style = { flexGrow = 1 } };
            field.RegisterValueChangedCallback(e => Set(e.newValue));
            row.Add(field);
            row.Add(VxUI.IconButton(VxIcon.Kind.Reset, "Reverse direction", () => Set(Get() + 180f)));
            row.TrackPropertyValue(end, _ =>
            {
                field.SetValueWithoutNotify(Get());
                dial.MarkDirtyRepaint();
            });
            row.TrackPropertyValue(start, _ =>
            {
                field.SetValueWithoutNotify(Get());
                dial.MarkDirtyRepaint();
            });
            return row;
        }
    }

    /// <summary>Editor of the colour points of a freeform gradient.</summary>
    public class VxFreeformPoints : VisualElement
    {
        readonly SerializedProperty m_List;
        int m_Size = -1;

        public VxFreeformPoints(SerializedProperty list)
        {
            m_List = list;
            Rebuild();
            schedule.Execute(() =>
            {
                if (m_List.serializedObject.targetObject == null) return;
                m_List.serializedObject.UpdateIfRequiredOrScript();
                if (m_List.arraySize != m_Size) Rebuild();
            }).Every(300);
        }

        void Rebuild()
        {
            Clear();
            m_List.serializedObject.UpdateIfRequiredOrScript();
            m_Size = m_List.arraySize;
            Add(VxUI.Text("Colour points blend by distance; positions are normalized (0..1) in the shape bounds.", "vx-hint"));
            for (int i = 0; i < m_List.arraySize; i++)
            {
                int index = i;
                var e = m_List.GetArrayElementAtIndex(i);
                var row = VxUI.El("vx-row");
                var color = new ColorField { hdr = true, style = { width = 60 } };
                color.BindProperty(e.FindPropertyRelative("color"));
                row.Add(color);
                var pos = new Vector2Field { style = { flexGrow = 1, marginLeft = 4 } };
                pos.BindProperty(e.FindPropertyRelative("position"));
                row.Add(pos);
                var fall = new FloatField { style = { width = 38, marginLeft = 4 }, tooltip = "Falloff (sharpness)" };
                fall.BindProperty(e.FindPropertyRelative("falloff"));
                row.Add(fall);
                row.Add(VxUI.IconButton(VxIcon.Kind.Trash, "Remove point", () =>
                {
                    m_List.serializedObject.Update();
                    m_List.DeleteArrayElementAtIndex(index);
                    m_List.serializedObject.ApplyModifiedProperties();
                    Rebuild();
                }, true));
                Add(row);
            }

            Add(VxUI.AddButton("Add colour point", () =>
            {
                m_List.serializedObject.Update();
                int i = m_List.arraySize;
                m_List.InsertArrayElementAtIndex(i);
                var e = m_List.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("position").vector2Value = new Vector2(UnityEngine.Random.value, UnityEngine.Random.value);
                e.FindPropertyRelative("color").colorValue = Color.HSVToRGB(UnityEngine.Random.value, 0.7f, 1f);
                e.FindPropertyRelative("falloff").floatValue = 2f;
                m_List.serializedObject.ApplyModifiedProperties();
                Rebuild();
            }));
        }
    }

    /// <summary>Visual corner radius editor: a rectangle diagram with a field at each corner and a link toggle.</summary>
    public class VxCornerRadius : VisualElement
    {
        const string LinkKey = "vectra.corners.linked";
        readonly SerializedProperty m_Radii;
        readonly VisualElement m_Frame;
        readonly FloatField[] m_Fields = new FloatField[4];

        public VxCornerRadius(SerializedProperty radii)
        {
            m_Radii = radii;
            AddToClassList("vx-corners");
            m_Frame = VxUI.El("vx-corners__frame");
            m_Frame.generateVisualContent += DrawFrame;
            Add(m_Frame);

            // x = top-left, y = top-right, z = bottom-right, w = bottom-left
            var positions = new[] { (0f, 0f, true, true), (0f, 0f, false, true), (0f, 0f, false, false), (0f, 0f, true, false) };
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var f = new FloatField { tooltip = new[] { "Top left", "Top right", "Bottom right", "Bottom left" }[i] };
                f.AddToClassList("vx-corners__field");
                bool left = positions[i].Item3, topSide = positions[i].Item4;
                if (left) f.style.left = 0;
                else f.style.right = 0;
                f.style.top = topSide ? 4 : 66;
                f.RegisterValueChangedCallback(e => Set(index, Mathf.Max(0f, e.newValue)));
                m_Fields[i] = f;
                Add(f);
            }

            bool linked = SessionState.GetBool(LinkKey, true);
            VisualElement link = null;
            link = VxUI.IconButton(linked ? VxIcon.Kind.Link : VxIcon.Kind.Unlink, "Edit all corners together", () =>
            {
                bool v = !SessionState.GetBool(LinkKey, true);
                SessionState.SetBool(LinkKey, v);
                link.Q<VxIcon>().SetKind(v ? VxIcon.Kind.Link : VxIcon.Kind.Unlink);
            });
            link.AddToClassList("vx-corners__link");
            Add(link);

            this.TrackPropertyValue(radii, _ => Refresh());
            Refresh();
        }

        void Set(int i, float v)
        {
            m_Radii.serializedObject.Update();
            var r = m_Radii.vector4Value;
            if (SessionState.GetBool(LinkKey, true)) r = new Vector4(v, v, v, v);
            else r[i] = v;
            m_Radii.vector4Value = r;
            m_Radii.serializedObject.ApplyModifiedProperties();
        }

        void Refresh()
        {
            var r = m_Radii.vector4Value;
            for (int i = 0; i < 4; i++) m_Fields[i].SetValueWithoutNotify(r[i]);
            m_Frame.MarkDirtyRepaint();
        }

        void DrawFrame(MeshGenerationContext mgc)
        {
            var rect = m_Frame.contentRect;
            var r = m_Radii.vector4Value;
            float max = Mathf.Max(Mathf.Max(r.x, r.y), Mathf.Max(Mathf.Max(r.z, r.w), 1f));
            float scale = rect.width * 0.5f / Mathf.Max(max * 1.6f, 1f);
            float tl = Mathf.Min(r.x * scale, rect.width * 0.5f), tr = Mathf.Min(r.y * scale, rect.width * 0.5f);
            float br = Mathf.Min(r.z * scale, rect.width * 0.5f), bl = Mathf.Min(r.w * scale, rect.width * 0.5f);
            var p = mgc.painter2D;
            var col = m_Frame.resolvedStyle.color;
            p.strokeColor = new Color(col.r, col.g, col.b, 0.8f);
            p.fillColor = new Color(0.3f, 0.55f, 1f, 0.18f);
            p.lineWidth = 1.5f;
            float w = rect.width, h = rect.height;
            p.BeginPath();
            p.MoveTo(new Vector2(tl, 0));
            p.LineTo(new Vector2(w - tr, 0));
            p.ArcTo(new Vector2(w, 0), new Vector2(w, tr), tr);
            p.LineTo(new Vector2(w, h - br));
            p.ArcTo(new Vector2(w, h), new Vector2(w - br, h), br);
            p.LineTo(new Vector2(bl, h));
            p.ArcTo(new Vector2(0, h), new Vector2(0, h - bl), bl);
            p.LineTo(new Vector2(0, tl));
            p.ArcTo(new Vector2(0, 0), new Vector2(tl, 0), tl);
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }
    }

    [CustomPropertyDrawer(typeof(VectraPaint))]
    class VectraPaintDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = VxUI.Root();
            root.Add(new VxPaintEditor(property));
            return root;
        }
    }

    [CustomPropertyDrawer(typeof(VectraGradient))]
    class VectraGradientDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = VxUI.Root();
            root.Add(new VxGradientEditor(property));
            return root;
        }
    }
}
