using System;
using System.Collections.Generic;
using System.Reflection;
using FirstSymbol.Vectra.Geometry;
using UILineCap = UnityEngine.UIElements.LineCap;
using UILineJoin = UnityEngine.UIElements.LineJoin;
using Unity.Collections;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>Building blocks of the Vectra inspector (styled with VectraInspector.uss).</summary>
    public static class VxUI
    {
        static StyleSheet s_Sheet;

        public static StyleSheet Sheet
        {
            get
            {
                if (s_Sheet != null) return s_Sheet;
                foreach (var guid in AssetDatabase.FindAssets("VectraInspector t:StyleSheet"))
                {
                    s_Sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
                    if (s_Sheet != null) break;
                }

                return s_Sheet;
            }
        }

        public static VisualElement Root()
        {
            var root = new VisualElement();
            if (Sheet != null) root.styleSheets.Add(Sheet);
            root.AddToClassList("vx-root");
            if (!EditorGUIUtility.isProSkin) root.AddToClassList("vx--light");
            return root;
        }

        public static VisualElement El(string cls, params VisualElement[] children)
        {
            var e = new VisualElement();
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) e.AddToClassList(c);
            foreach (var c in children) e.Add(c);
            return e;
        }

        public static Label Text(string text, string cls)
        {
            var l = new Label(text);
            foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            return l;
        }

        /// <summary>Label + field row.</summary>
        public static VisualElement Row(string label, VisualElement field, string tooltip = null)
        {
            var row = El("vx-row");
            var l = Text(label, "vx-row__label");
            if (!string.IsNullOrEmpty(tooltip)) l.tooltip = tooltip;
            row.Add(l);
            field.AddToClassList("vx-row__field");
            row.Add(field);
            if (!string.IsNullOrEmpty(tooltip)) row.tooltip = tooltip;
            return row;
        }

        public static VisualElement IconButton(VxIcon.Kind icon, string tooltip, Action onClick, bool danger = false)
        {
            var b = El("vx-icon-btn" + (danger ? " vx-icon-btn--danger" : ""));
            b.tooltip = tooltip;
            b.Add(new VxIcon(icon, 14));
            b.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                onClick();
            });
            return b;
        }

        public static VisualElement AddButton(string label, Action onClick)
        {
            var b = El("vx-add-btn");
            b.Add(new VxIcon(VxIcon.Kind.Plus, 12));
            b.Add(Text(label, "vx-add-btn__label"));
            b.RegisterCallback<ClickEvent>(_ => onClick());
            return b;
        }

        public static VisualElement Button(string label, Action onClick, string tooltip = null)
        {
            var b = Text(label, "vx-button");
            b.tooltip = tooltip;
            b.RegisterCallback<ClickEvent>(_ => onClick());
            return b;
        }

        public static Label Chip(string text, string variant = null)
        {
            var c = Text(text, "vx-chip");
            if (variant != null) c.AddToClassList("vx-chip--" + variant);
            return c;
        }

        public static Label SectionTitle(string text) => Text(text.ToUpperInvariant(), "vx-section-title");

        /// <summary>Card with a clickable header (fold state kept per session) and a body.</summary>
        public static VisualElement Card(string key, VisualElement header, VisualElement body, bool defaultOpen = true)
        {
            var card = El("vx-card");
            header.AddToClassList("vx-card__header");
            card.Add(header);
            body.AddToClassList("vx-card__body");
            card.Add(body);
            bool open = SessionState.GetBool(key, defaultOpen);
            body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            var chevron = new VxIcon(open ? VxIcon.Kind.ChevronDown : VxIcon.Kind.ChevronRight, 10);
            header.Insert(0, chevron);
            header.RegisterCallback<ClickEvent>(_ =>
            {
                open = !open;
                SessionState.SetBool(key, open);
                body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                chevron.SetKind(open ? VxIcon.Kind.ChevronDown : VxIcon.Kind.ChevronRight);
            });
            return card;
        }

        /// <summary>Shows <paramref name="el"/> only while <paramref name="condition"/> holds for <paramref name="p"/>.</summary>
        public static T VisibleWhen<T>(this T el, SerializedProperty p, Func<SerializedProperty, bool> condition) where T : VisualElement
        {
            void Apply() => el.style.display = condition(p) ? DisplayStyle.Flex : DisplayStyle.None;
            el.TrackPropertyValue(p, _ => Apply());
            Apply();
            return el;
        }

        public static string Nice(string name) => ObjectNames.NicifyVariableName(name.StartsWith("m_") ? name.Substring(2) : name);

        public static FieldInfo FieldOf(Type owner, string name)
        {
            for (var t = owner; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }

            return null;
        }
    }

    /// <summary>Animated on/off switch bound to a bool property.</summary>
    public class VxSwitch : VisualElement
    {
        readonly Func<bool> m_Get;

        public VxSwitch(SerializedProperty p) : this(() => p.boolValue, v =>
        {
            p.serializedObject.Update();
            p.boolValue = v;
            p.serializedObject.ApplyModifiedProperties();
        })
        {
            this.TrackPropertyValue(p, _ => Refresh());
        }

        public VxSwitch(Func<bool> get, Action<bool> set)
        {
            m_Get = get;
            AddToClassList("vx-switch");
            Add(VxUI.El("vx-switch__knob"));
            RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                set(!m_Get());
                Refresh();
            });
            Refresh();
        }

        public void Refresh() => EnableInClassList("vx-switch--on", m_Get());
    }

    /// <summary>Row of mutually exclusive buttons (text and/or icons).</summary>
    public class VxSegmented : VisualElement
    {
        readonly List<VisualElement> m_Items = new List<VisualElement>();
        readonly Func<int> m_Get;

        public VxSegmented(string[] labels, VxIcon.Kind[] icons, Func<int> get, Action<int> set, string[] tooltips = null)
        {
            m_Get = get;
            AddToClassList("vx-segmented");
            int n = labels?.Length ?? icons.Length;
            for (int i = 0; i < n; i++)
            {
                int index = i;
                var item = VxUI.El("vx-seg");
                if (icons != null && i < icons.Length) item.Add(new VxIcon(icons[i], 14));
                if (labels != null && !string.IsNullOrEmpty(labels[i])) item.Add(VxUI.Text(labels[i], "vx-seg__label"));
                if (tooltips != null && i < tooltips.Length) item.tooltip = tooltips[i];
                item.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    set(index);
                    Refresh();
                });
                m_Items.Add(item);
                Add(item);
            }

            Refresh();
        }

        /// <summary>Segmented control bound to an enum property.</summary>
        public static VxSegmented ForEnum(SerializedProperty p, string[] labels = null, VxIcon.Kind[] icons = null, string[] tooltips = null, Action changed = null)
        {
            labels ??= icons == null ? Array.ConvertAll(p.enumDisplayNames, s => s) : null;
            var seg = new VxSegmented(labels, icons, () => p.enumValueIndex, i =>
            {
                p.serializedObject.Update();
                p.enumValueIndex = i;
                p.serializedObject.ApplyModifiedProperties();
                changed?.Invoke();
            }, tooltips ?? p.enumDisplayNames);
            seg.TrackPropertyValue(p, _ => seg.Refresh());
            return seg;
        }

        public void Refresh()
        {
            int v = m_Get();
            for (int i = 0; i < m_Items.Count; i++) m_Items[i].EnableInClassList("vx-seg--active", i == v);
        }
    }

    /// <summary>Resolution-independent icons drawn with Painter2D in the current text colour.</summary>
    public class VxIcon : VisualElement
    {
        public enum Kind
        {
            Shape, Fill, Stroke, Effects, Settings, Style,
            Eye, EyeOff, Plus, Trash, Up, Down, Duplicate, More, Link, Unlink, ChevronDown, ChevronRight, Reset,
            AlignInside, AlignCenter, AlignOutside, CapButt, CapRound, CapSquare, JoinMiter, JoinRound, JoinBevel,
            PaintSolid, PaintLinear, PaintRadial, PaintAngular, PaintDiamond, PaintTexture, PaintStripes, PaintChecker,
            PaintDots, PaintGrid, PaintNoise, PaintFreeform,
            FxShadow, FxInnerShadow, FxGlow, FxInnerGlow, FxBevel, FxBlur, FxGrain, FxAdjust, FxFillAmount, FxDissolve,
            BlendNormal, BlendAdd, BlendMultiply, BlendScreen
        }

        Kind m_Kind;

        public VxIcon(Kind kind, float size = 14)
        {
            m_Kind = kind;
            style.width = size;
            style.height = size;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void SetKind(Kind k)
        {
            m_Kind = k;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            float s = Mathf.Min(r.width, r.height);
            if (s <= 0) return;
            var c = resolvedStyle.color;
            var p = mgc.painter2D;
            p.strokeColor = c;
            p.fillColor = c;
            p.lineWidth = Mathf.Max(1.2f, s * 0.1f);
            p.lineCap = UILineCap.Round;
            p.lineJoin = UILineJoin.Round;
            Vector2 o = new Vector2((r.width - s) * 0.5f, (r.height - s) * 0.5f);
            Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);
            float R(float v) => v * s;

            void Line(float x0, float y0, float x1, float y1)
            {
                p.BeginPath();
                p.MoveTo(P(x0, y0));
                p.LineTo(P(x1, y1));
                p.Stroke();
            }

            void Poly(bool fill, params float[] xy)
            {
                p.BeginPath();
                p.MoveTo(P(xy[0], xy[1]));
                for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
                p.ClosePath();
                if (fill) p.Fill();
                else p.Stroke();
            }

            void Circle(float x, float y, float rad, bool fill)
            {
                p.BeginPath();
                p.Arc(P(x, y), R(rad), 0f, 360f);
                p.ClosePath();
                if (fill) p.Fill();
                else p.Stroke();
            }

            void Rect(float x0, float y0, float x1, float y1, bool fill) => Poly(fill, x0, y0, x1, y0, x1, y1, x0, y1);

            void Alpha(float a)
            {
                var cc = c;
                cc.a *= a;
                p.fillColor = cc;
                p.strokeColor = cc;
            }

            switch (m_Kind)
            {
                case Kind.Shape:
                    Circle(0.38f, 0.4f, 0.24f, true);
                    Alpha(0.6f);
                    Rect(0.42f, 0.42f, 0.88f, 0.88f, true);
                    break;
                case Kind.Fill:
                    Rect(0.15f, 0.15f, 0.85f, 0.85f, true);
                    break;
                case Kind.Stroke:
                    p.lineWidth = s * 0.14f;
                    Rect(0.2f, 0.2f, 0.8f, 0.8f, false);
                    break;
                case Kind.Effects:
                    Poly(true, 0.5f, 0.08f, 0.6f, 0.4f, 0.92f, 0.5f, 0.6f, 0.6f, 0.5f, 0.92f, 0.4f, 0.6f, 0.08f, 0.5f, 0.4f, 0.4f);
                    break;
                case Kind.Settings:
                    Circle(0.5f, 0.5f, 0.15f, false);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        Line(0.5f + Mathf.Cos(a) * 0.27f, 0.5f + Mathf.Sin(a) * 0.27f, 0.5f + Mathf.Cos(a) * 0.4f, 0.5f + Mathf.Sin(a) * 0.4f);
                    }

                    break;
                case Kind.Style:
                    Circle(0.32f, 0.35f, 0.17f, true);
                    Alpha(0.7f);
                    Circle(0.66f, 0.38f, 0.17f, true);
                    Alpha(0.45f);
                    Circle(0.5f, 0.68f, 0.17f, true);
                    break;
                case Kind.Eye:
                case Kind.EyeOff:
                    p.BeginPath();
                    p.MoveTo(P(0.06f, 0.5f));
                    p.BezierCurveTo(P(0.3f, 0.15f), P(0.7f, 0.15f), P(0.94f, 0.5f));
                    p.BezierCurveTo(P(0.7f, 0.85f), P(0.3f, 0.85f), P(0.06f, 0.5f));
                    p.Stroke();
                    Circle(0.5f, 0.5f, 0.13f, true);
                    if (m_Kind == Kind.EyeOff) Line(0.15f, 0.85f, 0.85f, 0.15f);
                    break;
                case Kind.Plus:
                    Line(0.5f, 0.15f, 0.5f, 0.85f);
                    Line(0.15f, 0.5f, 0.85f, 0.5f);
                    break;
                case Kind.Trash:
                    Line(0.15f, 0.25f, 0.85f, 0.25f);
                    Line(0.38f, 0.12f, 0.62f, 0.12f);
                    Poly(false, 0.25f, 0.25f, 0.3f, 0.88f, 0.7f, 0.88f, 0.75f, 0.25f);
                    break;
                case Kind.Up:
                    Poly(false, 0.2f, 0.65f, 0.5f, 0.32f, 0.8f, 0.65f);
                    break;
                case Kind.Down:
                    Poly(false, 0.2f, 0.35f, 0.5f, 0.68f, 0.8f, 0.35f);
                    break;
                case Kind.ChevronDown:
                    p.BeginPath();
                    p.MoveTo(P(0.2f, 0.35f));
                    p.LineTo(P(0.5f, 0.68f));
                    p.LineTo(P(0.8f, 0.35f));
                    p.Stroke();
                    break;
                case Kind.ChevronRight:
                    p.BeginPath();
                    p.MoveTo(P(0.35f, 0.2f));
                    p.LineTo(P(0.68f, 0.5f));
                    p.LineTo(P(0.35f, 0.8f));
                    p.Stroke();
                    break;
                case Kind.Duplicate:
                    Rect(0.12f, 0.12f, 0.62f, 0.62f, false);
                    Rect(0.38f, 0.38f, 0.88f, 0.88f, false);
                    break;
                case Kind.More:
                    Circle(0.2f, 0.5f, 0.08f, true);
                    Circle(0.5f, 0.5f, 0.08f, true);
                    Circle(0.8f, 0.5f, 0.08f, true);
                    break;
                case Kind.Link:
                case Kind.Unlink:
                    p.BeginPath();
                    p.Arc(P(0.32f, 0.5f), R(0.18f), 90f, 270f);
                    p.Stroke();
                    p.BeginPath();
                    p.Arc(P(0.68f, 0.5f), R(0.18f), -90f, 90f);
                    p.Stroke();
                    if (m_Kind == Kind.Link) Line(0.32f, 0.5f, 0.68f, 0.5f);
                    else
                    {
                        Line(0.32f, 0.32f, 0.42f, 0.32f);
                        Line(0.32f, 0.68f, 0.42f, 0.68f);
                        Line(0.58f, 0.32f, 0.68f, 0.32f);
                        Line(0.58f, 0.68f, 0.68f, 0.68f);
                    }

                    break;
                case Kind.Reset:
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.5f), R(0.32f), -60f, 220f);
                    p.Stroke();
                    Poly(true, 0.66f, 0.12f, 0.82f, 0.3f, 0.6f, 0.34f);
                    break;
                case Kind.AlignInside:
                case Kind.AlignCenter:
                case Kind.AlignOutside:
                {
                    Alpha(0.35f);
                    Rect(0.22f, 0.22f, 0.78f, 0.78f, true);
                    Alpha(1f);
                    float d = m_Kind == Kind.AlignInside ? 0.07f : m_Kind == Kind.AlignCenter ? 0f : -0.07f;
                    p.lineWidth = s * 0.12f;
                    Rect(0.22f + d, 0.22f + d, 0.78f - d, 0.78f - d, false);
                    break;
                }
                case Kind.CapButt:
                case Kind.CapRound:
                case Kind.CapSquare:
                {
                    p.lineWidth = s * 0.34f;
                    p.lineCap = m_Kind == Kind.CapRound ? UILineCap.Round : UILineCap.Butt;
                    float end = m_Kind == Kind.CapSquare ? 0.8f : 0.62f;
                    Alpha(0.85f);
                    Line(0.05f, 0.5f, m_Kind == Kind.CapRound ? 0.62f : end, 0.5f);
                    Alpha(1f);
                    p.lineWidth = Mathf.Max(1f, s * 0.06f);
                    Line(0.62f, 0.2f, 0.62f, 0.8f);
                    break;
                }
                case Kind.JoinMiter:
                case Kind.JoinRound:
                case Kind.JoinBevel:
                    p.lineWidth = s * 0.26f;
                    p.lineCap = UILineCap.Butt;
                    p.lineJoin = m_Kind == Kind.JoinMiter ? UILineJoin.Miter : m_Kind == Kind.JoinRound ? UILineJoin.Round : UILineJoin.Bevel;
                    p.BeginPath();
                    p.MoveTo(P(0.2f, 0.9f));
                    p.LineTo(P(0.25f, 0.25f));
                    p.LineTo(P(0.9f, 0.25f));
                    p.Stroke();
                    break;
                case Kind.PaintSolid:
                    Circle(0.5f, 0.5f, 0.32f, true);
                    break;
                case Kind.PaintLinear:
                    for (int i = 0; i < 5; i++)
                    {
                        Alpha(1f - i * 0.2f);
                        Rect(0.15f + i * 0.14f, 0.2f, 0.29f + i * 0.14f, 0.8f, true);
                    }

                    break;
                case Kind.PaintRadial:
                    for (int i = 0; i < 4; i++)
                    {
                        Alpha(0.25f + i * 0.25f);
                        Circle(0.5f, 0.5f, 0.38f - i * 0.09f, true);
                    }

                    break;
                case Kind.PaintAngular:
                    for (int i = 0; i < 6; i++)
                    {
                        Alpha(0.2f + i * 0.15f);
                        p.BeginPath();
                        p.MoveTo(P(0.5f, 0.5f));
                        p.Arc(P(0.5f, 0.5f), R(0.36f), i * 60f, (i + 1) * 60f);
                        p.ClosePath();
                        p.Fill();
                    }

                    break;
                case Kind.PaintDiamond:
                    for (int i = 0; i < 3; i++)
                    {
                        Alpha(0.3f + i * 0.35f);
                        float d = 0.38f - i * 0.12f;
                        Poly(true, 0.5f, 0.5f - d, 0.5f + d, 0.5f, 0.5f, 0.5f + d, 0.5f - d, 0.5f);
                    }

                    break;
                case Kind.PaintTexture:
                    Rect(0.12f, 0.18f, 0.88f, 0.82f, false);
                    Poly(true, 0.18f, 0.76f, 0.42f, 0.45f, 0.58f, 0.62f, 0.7f, 0.5f, 0.82f, 0.76f);
                    Circle(0.68f, 0.34f, 0.07f, true);
                    break;
                case Kind.PaintStripes:
                    for (int i = 0; i < 4; i++) Line(0.1f + i * 0.25f, 0.9f, 0.4f + i * 0.25f, 0.1f);
                    break;
                case Kind.PaintChecker:
                    Rect(0.15f, 0.15f, 0.5f, 0.5f, true);
                    Rect(0.5f, 0.5f, 0.85f, 0.85f, true);
                    Alpha(0.3f);
                    Rect(0.5f, 0.15f, 0.85f, 0.5f, true);
                    Rect(0.15f, 0.5f, 0.5f, 0.85f, true);
                    break;
                case Kind.PaintDots:
                    for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                        Circle(0.22f + x * 0.28f, 0.22f + y * 0.28f, 0.08f, true);
                    break;
                case Kind.PaintGrid:
                    p.lineWidth = Mathf.Max(1f, s * 0.07f);
                    for (int i = 0; i < 4; i++)
                    {
                        Line(0.15f + i * 0.233f, 0.15f, 0.15f + i * 0.233f, 0.85f);
                        Line(0.15f, 0.15f + i * 0.233f, 0.85f, 0.15f + i * 0.233f);
                    }

                    break;
                case Kind.PaintNoise:
                {
                    var rnd = new System.Random(7);
                    for (int i = 0; i < 18; i++)
                    {
                        Alpha(0.3f + (float)rnd.NextDouble() * 0.7f);
                        float x = 0.12f + (float)rnd.NextDouble() * 0.68f, y = 0.12f + (float)rnd.NextDouble() * 0.68f;
                        Rect(x, y, x + 0.09f, y + 0.09f, true);
                    }

                    break;
                }
                case Kind.PaintFreeform:
                    Alpha(0.9f);
                    Circle(0.35f, 0.38f, 0.2f, true);
                    Alpha(0.6f);
                    Circle(0.65f, 0.42f, 0.2f, true);
                    Alpha(0.35f);
                    Circle(0.5f, 0.66f, 0.2f, true);
                    break;
                case Kind.FxShadow:
                    Alpha(0.35f);
                    Rect(0.3f, 0.3f, 0.86f, 0.86f, true);
                    Alpha(1f);
                    Rect(0.14f, 0.14f, 0.7f, 0.7f, true);
                    break;
                case Kind.FxInnerShadow:
                    Rect(0.15f, 0.15f, 0.85f, 0.85f, false);
                    Alpha(0.45f);
                    Poly(true, 0.15f, 0.15f, 0.85f, 0.15f, 0.85f, 0.3f, 0.3f, 0.3f, 0.3f, 0.85f, 0.15f, 0.85f);
                    break;
                case Kind.FxGlow:
                    Alpha(0.2f);
                    Circle(0.5f, 0.5f, 0.45f, true);
                    Alpha(0.45f);
                    Circle(0.5f, 0.5f, 0.33f, true);
                    Alpha(1f);
                    Circle(0.5f, 0.5f, 0.2f, true);
                    break;
                case Kind.FxInnerGlow:
                    Circle(0.5f, 0.5f, 0.38f, false);
                    Alpha(0.45f);
                    Circle(0.5f, 0.5f, 0.28f, false);
                    break;
                case Kind.FxBevel:
                    Poly(true, 0.12f, 0.12f, 0.88f, 0.12f, 0.72f, 0.28f, 0.28f, 0.28f, 0.28f, 0.72f, 0.12f, 0.88f);
                    Alpha(0.35f);
                    Poly(true, 0.88f, 0.12f, 0.88f, 0.88f, 0.12f, 0.88f, 0.28f, 0.72f, 0.72f, 0.72f, 0.72f, 0.28f);
                    break;
                case Kind.FxBlur:
                    for (int i = 0; i < 3; i++)
                    {
                        Alpha(0.25f + i * 0.3f);
                        Circle(0.5f, 0.5f, 0.42f - i * 0.11f, true);
                    }

                    break;
                case Kind.FxGrain:
                {
                    var rnd = new System.Random(3);
                    for (int i = 0; i < 26; i++)
                        Circle(0.12f + (float)rnd.NextDouble() * 0.76f, 0.12f + (float)rnd.NextDouble() * 0.76f, 0.035f, true);
                    break;
                }
                case Kind.FxAdjust:
                    Line(0.15f, 0.3f, 0.85f, 0.3f);
                    Line(0.15f, 0.7f, 0.85f, 0.7f);
                    Circle(0.35f, 0.3f, 0.1f, true);
                    Circle(0.65f, 0.7f, 0.1f, true);
                    break;
                case Kind.FxFillAmount:
                    Circle(0.5f, 0.5f, 0.38f, false);
                    p.BeginPath();
                    p.MoveTo(P(0.5f, 0.5f));
                    p.Arc(P(0.5f, 0.5f), R(0.38f), -90f, 150f);
                    p.ClosePath();
                    p.Fill();
                    break;
                case Kind.FxDissolve:
                {
                    var rnd = new System.Random(11);
                    for (int i = 0; i < 14; i++)
                    {
                        float x = 0.15f + (float)rnd.NextDouble() * 0.6f, y = 0.15f + (float)rnd.NextDouble() * 0.6f;
                        Rect(x, y, x + 0.13f, y + 0.13f, true);
                    }

                    break;
                }
                case Kind.BlendNormal:
                    Circle(0.4f, 0.5f, 0.26f, true);
                    Circle(0.62f, 0.5f, 0.26f, false);
                    break;
                case Kind.BlendAdd:
                case Kind.BlendScreen:
                    Alpha(0.55f);
                    Circle(0.38f, 0.5f, 0.26f, true);
                    Circle(0.62f, 0.5f, 0.26f, true);
                    if (m_Kind == Kind.BlendAdd)
                    {
                        Alpha(1f);
                        Line(0.5f, 0.38f, 0.5f, 0.62f);
                    }

                    break;
                case Kind.BlendMultiply:
                    Circle(0.38f, 0.5f, 0.26f, false);
                    Circle(0.62f, 0.5f, 0.26f, false);
                    Circle(0.5f, 0.5f, 0.11f, true);
                    break;
            }
        }
    }

    /// <summary>Thumbnail of a shape drawn from its real outline.</summary>
    public class VxShapeThumb : VisualElement
    {
        readonly List<Vector2[]> m_Contours = new List<Vector2[]>();
        bool m_Closed = true;
        Geometry.FillRule m_Rule;

        public VxShapeThumb(VectraShapeGeometry geometry)
        {
            AddToClassList("vx-tile__thumb");
            pickingMode = PickingMode.Ignore;
            SetGeometry(geometry);
            generateVisualContent += Draw;
        }

        public void SetGeometry(VectraShapeGeometry g)
        {
            m_Contours.Clear();
            if (g == null) return;
            m_Closed = g.IsClosed;
            m_Rule = g.FillRule;
            try
            {
                var frame = new ShapeFrame { rect = new Rect(-50, -50, 100, 100), pixelSize = 0.5f };
                var path = new NativePath(Allocator.TempJob, 32);
                var poly = new Polylines(Allocator.TempJob, 128);
                g.BuildPath(frame, new PathBuilder(path));
                VectraGeometry.Flatten(path, Affine2.Identity, 0.6f, 0, false, ref poly);
                for (int ci = 0; ci < poly.contours.Length; ci++)
                {
                    var c = poly.contours[ci];
                    var arr = new Vector2[c.count];
                    for (int i = 0; i < c.count; i++) arr[i] = poly.points[c.start + i];
                    m_Contours.Add(arr);
                }

                path.Dispose();
                poly.Dispose();
            }
            catch (Exception)
            {
                m_Contours.Clear();
            }

            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            if (m_Contours.Count == 0) return;
            var r = contentRect;
            float s = Mathf.Min(r.width, r.height) / 104f;
            Vector2 c = r.center;
            Vector2 Map(Vector2 v) => c + new Vector2(v.x, -v.y) * s;
            var p = mgc.painter2D;
            var col = resolvedStyle.color;
            p.BeginPath();
            foreach (var contour in m_Contours)
            {
                if (contour.Length < 2) continue;
                p.MoveTo(Map(contour[0]));
                for (int i = 1; i < contour.Length; i++) p.LineTo(Map(contour[i]));
                if (m_Closed) p.ClosePath();
            }

            if (m_Closed)
            {
                p.fillColor = new Color(col.r, col.g, col.b, col.a * 0.85f);
                p.Fill(m_Rule == Geometry.FillRule.EvenOdd ? UnityEngine.UIElements.FillRule.OddEven : UnityEngine.UIElements.FillRule.NonZero);
            }
            else
            {
                p.strokeColor = col;
                p.lineWidth = Mathf.Max(1.5f, 6f * s);
                p.lineCap = UnityEngine.UIElements.LineCap.Round;
                p.lineJoin = UnityEngine.UIElements.LineJoin.Round;
                p.Stroke();
            }
        }
    }

    /// <summary>Small preview of a paint (colour, gradient strip, texture or pattern icon) over a checkerboard.</summary>
    public class VxSwatch : VisualElement
    {
        VectraPaint m_Paint;
        readonly VxIcon m_Icon;

        public VxSwatch()
        {
            AddToClassList("vx-swatch");
            m_Icon = new VxIcon(VxIcon.Kind.PaintSolid, 16) { style = { position = Position.Absolute, left = 2, top = 2 } };
            Add(m_Icon);
            generateVisualContent += Draw;
        }

        public void SetPaint(VectraPaint paint)
        {
            m_Paint = paint;
            bool pattern = paint != null && paint.type >= PaintType.Stripes && paint.type <= PaintType.Noise;
            m_Icon.style.display = pattern ? DisplayStyle.Flex : DisplayStyle.None;
            if (pattern)
            {
                m_Icon.SetKind(VxPaint.IconOf(paint.type));
                m_Icon.style.color = paint.secondaryColor;
                style.backgroundColor = paint.color;
            }
            else style.backgroundColor = StyleKeyword.Null;

            style.backgroundImage = paint != null && paint.type == PaintType.Texture && paint.texture is Texture2D t ? new StyleBackground(t) : new StyleBackground();
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width <= 0) return;
            var p = mgc.painter2D;
            // Checkerboard for transparency.
            float cell = 5f;
            for (int y = 0; y * cell < r.height; y++)
            for (int x = 0; x * cell < r.width; x++)
            {
                p.fillColor = (x + y) % 2 == 0 ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.5f, 0.5f, 0.5f);
                p.BeginPath();
                p.MoveTo(new Vector2(x * cell, y * cell));
                p.LineTo(new Vector2(Mathf.Min((x + 1) * cell, r.width), y * cell));
                p.LineTo(new Vector2(Mathf.Min((x + 1) * cell, r.width), Mathf.Min((y + 1) * cell, r.height)));
                p.LineTo(new Vector2(x * cell, Mathf.Min((y + 1) * cell, r.height)));
                p.ClosePath();
                p.Fill();
            }

            if (m_Paint == null) return;
            if (m_Paint.type == PaintType.Solid)
            {
                var c = m_Paint.color;
                c.a *= m_Paint.opacity;
                Fill(p, 0, r.width, r.height, Clamp(c));
                return;
            }

            if (m_Paint.IsGradient && m_Paint.gradient != null)
            {
                int n = 12;
                for (int i = 0; i < n; i++)
                {
                    var c = m_Paint.gradient.Evaluate((i + 0.5f) / n);
                    c.a *= m_Paint.opacity;
                    Fill(p, r.width * i / n, r.width * (i + 1) / n + 0.5f, r.height, Clamp(c));
                }
            }
            else if (m_Paint.type == PaintType.Freeform && m_Paint.freeformPoints != null && m_Paint.freeformPoints.Count > 0)
            {
                int n = m_Paint.freeformPoints.Count;
                for (int i = 0; i < n; i++) Fill(p, r.width * i / n, r.width * (i + 1) / n + 0.5f, r.height, Clamp(m_Paint.freeformPoints[i].color));
            }
        }

        static Color Clamp(Color c) => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a));

        static void Fill(Painter2D p, float x0, float x1, float h, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, 0));
            p.LineTo(new Vector2(x1, 0));
            p.LineTo(new Vector2(x1, h));
            p.LineTo(new Vector2(x0, h));
            p.ClosePath();
            p.Fill();
        }
    }

    /// <summary>Round dial showing an angle in degrees (counter-clockwise from +X); drag to change, Shift snaps to 15°.</summary>
    public class VxDial : VisualElement
    {
        readonly Func<float> m_Get;
        readonly Action<float> m_Set;

        public VxDial(Func<float> get, Action<float> set)
        {
            m_Get = get;
            m_Set = set;
            AddToClassList("vx-dial");
            tooltip = "Drag to rotate (Shift snaps to 15°)";
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(e =>
            {
                this.CapturePointer(e.pointerId);
                SetFrom(e.localPosition, e.shiftKey);
                e.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (this.HasPointerCapture(e.pointerId)) SetFrom(e.localPosition, e.shiftKey);
            });
            RegisterCallback<PointerUpEvent>(e => this.ReleasePointer(e.pointerId));
        }

        void SetFrom(Vector3 local, bool snap)
        {
            var c = contentRect.center;
            float a = Mathf.Atan2(-(local.y - c.y), local.x - c.x) * Mathf.Rad2Deg;
            a = snap ? Mathf.Round(a / 15f) * 15f : Mathf.Round(a);
            m_Set(a);
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            var p = mgc.painter2D;
            var col = resolvedStyle.color;
            float rad = Mathf.Min(r.width, r.height) * 0.5f - 1.5f;
            p.fillColor = new Color(0, 0, 0, 0.18f);
            p.BeginPath();
            p.Arc(r.center, rad, 0, 360);
            p.Fill();
            p.strokeColor = new Color(col.r, col.g, col.b, 0.45f);
            p.lineWidth = 1.2f;
            p.BeginPath();
            p.Arc(r.center, rad, 0, 360);
            p.Stroke();
            float a = m_Get() * Mathf.Deg2Rad;
            var tip = r.center + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * (rad - 1f);
            p.strokeColor = new Color(0.3f, 0.55f, 1f);
            p.lineWidth = 2f;
            p.lineCap = UILineCap.Round;
            p.BeginPath();
            p.MoveTo(r.center);
            p.LineTo(tip);
            p.Stroke();
            p.fillColor = new Color(0.3f, 0.55f, 1f);
            p.BeginPath();
            p.Arc(tip, 2.2f, 0, 360);
            p.Fill();
        }
    }

    /// <summary>Angle input bound to a float property: dial + numeric field.</summary>
    public class VxAngleField : VisualElement
    {
        public VxAngleField(SerializedProperty p)
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            var dial = new VxDial(() => p.floatValue, v =>
            {
                p.serializedObject.Update();
                p.floatValue = v;
                p.serializedObject.ApplyModifiedProperties();
            });
            Add(dial);
            var f = new FloatField { style = { flexGrow = 1 } };
            f.BindProperty(p);
            Add(f);
            this.TrackPropertyValue(p, _ => dial.MarkDirtyRepaint());
        }
    }

    /// <summary>Creates the best control for a serialized field based on its type and attributes.</summary>
    public static class VxFields
    {
        public static VisualElement For(SerializedProperty p, Type owner, string labelOverride = null)
        {
            var fi = owner != null ? VxUI.FieldOf(owner, p.name) : null;
            string label = labelOverride ?? (string.IsNullOrEmpty(p.displayName) ? VxUI.Nice(p.name) : p.displayName);
            string tooltip = fi?.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? p.tooltip;
            var range = fi?.GetCustomAttribute<RangeAttribute>();
            var min = fi?.GetCustomAttribute<MinAttribute>();
            bool angle = p.propertyType == SerializedPropertyType.Float &&
                         (p.name.IndexOf("angle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          p.name.IndexOf("rotation", StringComparison.OrdinalIgnoreCase) >= 0) &&
                         (range == null || range.max - range.min >= 359f);
            VisualElement field;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float when angle:
                    field = new VxAngleField(p);
                    break;
                case SerializedPropertyType.Float when range != null:
                {
                    var s = new Slider(range.min, range.max) { showInputField = true };
                    s.BindProperty(p);
                    field = s;
                    break;
                }
                case SerializedPropertyType.Float when min != null:
                {
                    // Soft range: the slider covers a comfortable range around the current value, the field accepts any value.
                    float v = Mathf.Abs(p.floatValue);
                    float soft = v <= 0f ? 16f : Mathf.Pow(2f, Mathf.Ceil(Mathf.Log(Mathf.Max(v * 4f, 1e-3f), 2f)));
                    var s = new Slider(min.min, Mathf.Max(soft, min.min + 1e-3f)) { showInputField = true };
                    s.BindProperty(p);
                    s.RegisterValueChangedCallback(e =>
                    {
                        if (e.newValue < min.min) s.value = min.min;
                    });
                    field = s;
                    break;
                }
                case SerializedPropertyType.Float:
                {
                    var f = new FloatField();
                    f.BindProperty(p);
                    field = f;
                    break;
                }
                case SerializedPropertyType.Integer when range != null:
                {
                    var s = new SliderInt((int)range.min, (int)range.max) { showInputField = true };
                    s.BindProperty(p);
                    field = s;
                    break;
                }
                case SerializedPropertyType.Integer:
                {
                    var f = new IntegerField();
                    f.BindProperty(p);
                    field = f;
                    break;
                }
                case SerializedPropertyType.Boolean:
                    field = VxUI.El("", new VxSwitch(p));
                    break;
                case SerializedPropertyType.Enum when p.enumDisplayNames.Length <= 4 && TotalLength(p.enumDisplayNames) <= 30:
                    field = VxSegmented.ForEnum(p);
                    break;
                case SerializedPropertyType.Enum when fi != null:
                {
                    var e = new EnumField((Enum)Enum.ToObject(fi.FieldType, 0));
                    e.BindProperty(p);
                    field = e;
                    break;
                }
                case SerializedPropertyType.Color:
                {
                    var hdr = fi?.GetCustomAttribute<ColorUsageAttribute>()?.hdr ?? false;
                    var c = new ColorField { hdr = hdr, showAlpha = true };
                    c.BindProperty(p);
                    field = c;
                    break;
                }
                case SerializedPropertyType.Vector2:
                {
                    var v = new Vector2Field();
                    v.BindProperty(p);
                    field = v;
                    break;
                }
                case SerializedPropertyType.Vector4:
                {
                    var v = new Vector4Field();
                    v.BindProperty(p);
                    field = v;
                    break;
                }
                case SerializedPropertyType.String:
                {
                    var t = new TextField { multiline = fi?.GetCustomAttribute<TextAreaAttribute>() != null };
                    t.BindProperty(p);
                    if (t.multiline) t.style.whiteSpace = WhiteSpace.Normal;
                    field = t;
                    break;
                }
                case SerializedPropertyType.ObjectReference:
                {
                    var o = new ObjectField { objectType = fi?.FieldType ?? typeof(UnityEngine.Object), allowSceneObjects = false };
                    o.BindProperty(p);
                    field = o;
                    break;
                }
                case SerializedPropertyType.AnimationCurve:
                {
                    var c = new CurveField();
                    c.BindProperty(p);
                    field = c;
                    break;
                }
                default:
                {
                    var pf = new PropertyField(p, "");
                    pf.Bind(p.serializedObject);
                    return VxUI.Row(label, pf, tooltip);
                }
            }

            return VxUI.Row(label, field, tooltip);
        }

        static int TotalLength(string[] names)
        {
            int n = 0;
            foreach (var s in names) n += s.Length;
            return n;
        }

        /// <summary>Adds a control for every visible child of <paramref name="prop"/>.</summary>
        public static void AddChildren(VisualElement parent, SerializedProperty prop, Type owner, params string[] skip)
        {
            var it = prop.Copy();
            var end = it.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                if (Array.IndexOf(skip, it.name) >= 0) continue;
                parent.Add(For(it.Copy(), owner));
            }
        }
    }

    /// <summary>Paint helpers shared by editors.</summary>
    public static class VxPaint
    {
        public static VxIcon.Kind IconOf(PaintType t)
        {
            switch (t)
            {
                case PaintType.LinearGradient: return VxIcon.Kind.PaintLinear;
                case PaintType.RadialGradient: return VxIcon.Kind.PaintRadial;
                case PaintType.AngularGradient: return VxIcon.Kind.PaintAngular;
                case PaintType.DiamondGradient: return VxIcon.Kind.PaintDiamond;
                case PaintType.Texture: return VxIcon.Kind.PaintTexture;
                case PaintType.Stripes: return VxIcon.Kind.PaintStripes;
                case PaintType.Checker: return VxIcon.Kind.PaintChecker;
                case PaintType.Dots: return VxIcon.Kind.PaintDots;
                case PaintType.Grid: return VxIcon.Kind.PaintGrid;
                case PaintType.Noise: return VxIcon.Kind.PaintNoise;
                case PaintType.Freeform: return VxIcon.Kind.PaintFreeform;
                default: return VxIcon.Kind.PaintSolid;
            }
        }

        public static string NameOf(PaintType t)
        {
            switch (t)
            {
                case PaintType.LinearGradient: return "Linear gradient";
                case PaintType.RadialGradient: return "Radial gradient";
                case PaintType.AngularGradient: return "Angular gradient";
                case PaintType.DiamondGradient: return "Diamond gradient";
                case PaintType.Freeform: return "Freeform gradient";
                default: return ObjectNames.NicifyVariableName(t.ToString());
            }
        }
    }
}
