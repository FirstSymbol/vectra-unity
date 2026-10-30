using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    static class VxBool
    {
        public static readonly string[] Names = { "Union", "Subtract", "Intersect", "Exclude" };
        public static readonly VxIcon.Kind[] Icons = { VxIcon.Kind.BoolUnion, VxIcon.Kind.BoolSubtract, VxIcon.Kind.BoolIntersect, VxIcon.Kind.BoolExclude };
        public static readonly string[] Tips =
        {
            "Add this shape to the result", "Cut this shape out of the result", "Keep only the overlap with the result",
            "Keep the parts that do not overlap"
        };

        /// <summary>Renders <paramref name="content"/> into the header image every time <paramref name="dirty"/> says so.</summary>
        public static void LivePreview(VisualElement owner, Image image, Func<VectraShapeContent> content, Func<Vector2> size)
        {
            Texture2D tex = null;
            int lastHash = 0;
            owner.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            });

            void Refresh()
            {
                var c = content();
                if (c?.ActiveGeometry == null) return;
                var sz = size();
                int h = c.ActiveGeometry.ComputeHash() ^ sz.GetHashCode();
                if (h == lastHash && tex != null) return;
                lastHash = h;
                try
                {
                    var t = VectraRasterizer.Render(c, sz, 168, 168);
                    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                    tex = t;
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    image.image = tex;
                }
                catch (Exception)
                {
                    image.image = null;
                }
            }

            owner.schedule.Execute(Refresh).Every(400);
        }
    }

    [CustomEditor(typeof(VectraBooleanGroup)), CanEditMultipleObjects]
    class VectraBooleanGroupEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var group = (VectraBooleanGroup)target;
            var so = serializedObject;
            var root = VxUI.Root();
            var header = VxUI.Header("Boolean Group", "Combines the child shapes into one", out var image, out _, out var chips);
            root.Add(header);
            VxBool.LivePreview(header, image,
                () => group == null ? null : group.TryGetComponent(out VectraShape s) ? s.Content : group.TryGetComponent(out VectraRenderer r) ? r.Content : null,
                () => group == null ? Vector2.one : group.TryGetComponent(out VectraShape s) ? s.rectTransform.rect.size : group.TryGetComponent(out VectraRenderer r) ? r.Size : Vector2.one);

            if (!group.TryGetComponent(out VectraShape _) && !group.TryGetComponent(out VectraRenderer _))
            {
                var warn = new VisualElement();
                warn.Add(VxUI.Text("The group draws its result with a Vectra Shape (UI) or Vectra Renderer (world) on the same object.", "vx-hint"));
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                row.Add(VxUI.Button("Add Vectra Shape (UI)", () => Undo.AddComponent<VectraShape>(group.gameObject)));
                row.Add(VxUI.Button("Add Vectra Renderer (World)", () => Undo.AddComponent<VectraRenderer>(group.gameObject)));
                warn.Add(row);
                root.Add(VxUI.TitledCard("vectra.bool.missing", VxIcon.Kind.Warning, "Renderer missing", warn));
            }

            var result = new VisualElement();
            var t = typeof(VectraBooleanGroup);
            var smooth = new Slider(0f, 64f) { showInputField = true };
            smooth.BindProperty(so.FindProperty("m_Smoothness"));
            result.Add(VxUI.Row("Smoothness", smooth, "Blend radius: 0 = exact geometric booleans, above 0 = smooth (metaball-like) blending"));
            result.Add(VxFields.For(so.FindProperty("m_HideOperands"), t, "Hide operands"));
            root.Add(VxUI.TitledCard("vectra.bool.result", VxIcon.Kind.BoolUnion, "Result", result));

            var operands = new VisualElement();
            root.Add(VxUI.TitledCard("vectra.bool.operands", VxIcon.Kind.Shape, "Operands", operands));
            int lastCount = -1;

            void Rebuild()
            {
                operands.Clear();
                var children = Operands(group);
                lastCount = children.Count;
                chips.Clear();
                chips.Add(VxUI.Chip($"{children.Count} operands", "accent"));
                chips.Add(so.FindProperty("m_Smoothness").floatValue > 0f ? VxUI.Chip("Smooth", "green") : VxUI.Chip("Exact", "green"));
                if (children.Count == 0)
                    operands.Add(VxUI.Text("Add child shapes below. Their order is the order of the operations.", "vx-empty"));
                for (int i = 0; i < children.Count; i++)
                    operands.Add(OperandRow(group, children[i], i, Rebuild));
                operands.Add(VxUI.SectionTitle("Add operand"));
                var tiles = VxUI.El("vx-grid");
                foreach (var g in new VectraShapeGeometry[] { new RectangleShape(), new EllipseShape(), new PolygonShape(), new StarShape(), new TriangleShape(), new PathShape() })
                {
                    var geom = g;
                    var tile = VxUI.El("vx-tile");
                    tile.Add(new VxShapeThumb(geom));
                    tile.Add(VxUI.Text(geom.DisplayName, "vx-tile__label"));
                    tile.RegisterCallback<ClickEvent>(_ =>
                    {
                        AddOperand(group, (VectraShapeGeometry)Activator.CreateInstance(geom.GetType()), children.Count == 0 ? BooleanOp.Union : BooleanOp.Subtract);
                        Rebuild();
                    });
                    tiles.Add(tile);
                }

                operands.Add(tiles);
            }

            Rebuild();
            operands.schedule.Execute(() =>
            {
                if (group != null && Operands(group).Count != lastCount) Rebuild();
            }).Every(500);
            root.Bind(so);
            return root;
        }

        static List<Transform> Operands(VectraBooleanGroup g)
        {
            var list = new List<Transform>();
            foreach (Transform c in g.transform)
                if (c.GetComponent<VectraShape>() != null || c.GetComponent<VectraRenderer>() != null) list.Add(c);
            return list;
        }

        static VisualElement OperandRow(VectraBooleanGroup group, Transform child, int index, Action rebuild)
        {
            var geometry = child.TryGetComponent(out VectraShape s) ? s.Geometry : child.GetComponent<VectraRenderer>().Geometry;
            var card = VxUI.El("vx-card");
            var row = VxUI.El("vx-card__header");
            var thumb = new VxShapeThumb(geometry) { style = { width = 22, height = 22, color = new Color(0.3f, 0.55f, 1f) } };
            row.Add(thumb);
            row.Add(VxUI.Text(child.name, "vx-card__title"));
            if (index == 0) row.Add(VxUI.Chip("Base"));
            else
            {
                var op = child.GetComponent<VectraBooleanOperand>();
                int current = op != null ? (int)op.operation : 0;
                var seg = new VxSegmented(null, VxBool.Icons, () => current, i =>
                {
                    var o = child.GetComponent<VectraBooleanOperand>();
                    if (o == null) o = Undo.AddComponent<VectraBooleanOperand>(child.gameObject);
                    Undo.RecordObject(o, "Boolean Operation");
                    o.operation = (BooleanOp)i;
                    current = i;
                    EditorUtility.SetDirty(o);
                    group.MarkDirty();
                }, VxBool.Names) { style = { flexGrow = 0, width = 112, marginRight = 4 } };
                row.Add(seg);
            }

            row.Add(VxUI.IconButton(VxIcon.Kind.Up, "Earlier", () =>
            {
                Undo.SetTransformParent(child, child.parent, "Reorder");
                child.SetSiblingIndex(Mathf.Max(0, child.GetSiblingIndex() - 1));
                group.MarkDirty();
                rebuild();
            }));
            row.Add(VxUI.IconButton(VxIcon.Kind.Down, "Later", () =>
            {
                Undo.SetTransformParent(child, child.parent, "Reorder");
                child.SetSiblingIndex(child.GetSiblingIndex() + 1);
                group.MarkDirty();
                rebuild();
            }));
            row.Add(VxUI.IconButton(VxIcon.Kind.Select, "Select to edit", () => Selection.activeGameObject = child.gameObject));
            row.Add(VxUI.IconButton(VxIcon.Kind.Trash, "Delete operand", () =>
            {
                Undo.DestroyObjectImmediate(child.gameObject);
                group.MarkDirty();
                rebuild();
            }, true));
            card.Add(row);
            return card;
        }

        static void AddOperand(VectraBooleanGroup group, VectraShapeGeometry geometry, BooleanOp op)
        {
            bool ui = group.GetComponent<VectraShape>() != null || group.GetComponent<RectTransform>() != null;
            GameObject go;
            if (ui)
            {
                go = new GameObject(geometry.DisplayName, typeof(RectTransform), typeof(CanvasRenderer), typeof(VectraShape));
                go.transform.SetParent(group.transform, false);
                var rt = (RectTransform)go.transform;
                var parent = group.GetComponent<RectTransform>();
                rt.sizeDelta = parent != null ? parent.rect.size * 0.6f : new Vector2(100, 100);
                rt.anchoredPosition = new Vector2(UnityEngine.Random.Range(-20f, 20f), UnityEngine.Random.Range(-20f, 20f));
                go.GetComponent<VectraShape>().Geometry = geometry;
            }
            else
            {
                go = new GameObject(geometry.DisplayName, typeof(VectraRenderer));
                go.transform.SetParent(group.transform, false);
                var r = go.GetComponent<VectraRenderer>();
                r.Geometry = geometry;
                var owner = group.GetComponent<VectraRenderer>();
                r.Size = owner != null ? owner.Size * 0.6f : Vector2.one * 0.6f;
            }

            Undo.RegisterCreatedObjectUndo(go, "Add Boolean Operand");
            var o = go.AddComponent<VectraBooleanOperand>();
            o.operation = op;
            group.MarkDirty();
        }
    }

    [CustomEditor(typeof(VectraBooleanOperand)), CanEditMultipleObjects]
    class VectraBooleanOperandEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var so = serializedObject;
            var root = VxUI.Root();
            var body = new VisualElement();
            var op = so.FindProperty("operation");
            var seg = VxSegmented.ForEnum(op, VxBool.Names, VxBool.Icons, VxBool.Tips);
            body.Add(VxUI.Row("Operation", seg));
            var hint = VxUI.Text("", "vx-hint");
            body.Add(hint);
            void Hint() => hint.text = VxBool.Tips[Mathf.Clamp(op.enumValueIndex, 0, 3)] + ". The first child of a group is always the base shape.";
            hint.TrackPropertyValue(op, _ => Hint());
            Hint();
            var group = ((Component)target).GetComponentInParent<VectraBooleanGroup>();
            if (group != null)
                body.Add(VxUI.Button("Select group", () => Selection.activeGameObject = group.gameObject));
            else
                body.Add(VxUI.Text("This object is not inside a Vectra Boolean Group.", "vx-hint"));
            root.Add(VxUI.TitledCard("vectra.operand", VxIcon.Kind.BoolUnion, "Boolean Operand", body));
            root.TrackSerializedObjectValue(so, _ =>
            {
                if (group != null) group.MarkDirty();
            });
            return root;
        }
    }

    [CustomEditor(typeof(VectraStyle))]
    class VectraStyleEditor : UnityEditor.Editor
    {
        VectraShapeContent m_Sample;

        public override VisualElement CreateInspectorGUI()
        {
            var style = (VectraStyle)target;
            var so = serializedObject;
            var root = VxUI.Root();
            var header = VxUI.Header(style.name, "Vectra Style · shared appearance", out var image, out _, out var chips);
            root.Add(header);
            m_Sample = new VectraShapeContent { geometry = new RectangleShape { cornerRadii = Vector4.one * 22f }, style = style };
            Texture2D tex = null;
            bool pending = true;
            header.TrackSerializedObjectValue(so, _ =>
            {
                pending = true;
                style.NotifyChanged();
            });
            header.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (tex != null) DestroyImmediate(tex);
            });
            header.schedule.Execute(() =>
            {
                chips.Clear();
                chips.Add(VxUI.Chip($"{style.fills.Count} fills"));
                chips.Add(VxUI.Chip($"{style.strokes.Count} strokes"));
                chips.Add(VxUI.Chip($"{style.effects.Count} effects"));
                if (!pending) return;
                pending = false;
                try
                {
                    var t = VectraRasterizer.Render(m_Sample, new Vector2(120, 120), 168, 168);
                    if (tex != null) DestroyImmediate(tex);
                    tex = t;
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    image.image = tex;
                }
                catch (Exception)
                {
                    image.image = null;
                }
            }).Every(400);

            root.Add(VxUI.Text("Every shape that references this style updates when it changes.", "vx-hint"));
            var tabs = VxUI.El("vx-tabs");
            var page = VxUI.El("vx-page");
            root.Add(tabs);
            root.Add(page);
            var defs = new (string name, VxIcon.Kind icon, Func<VisualElement> build)[]
            {
                ("Fill", VxIcon.Kind.Fill, () => VectraContentInspector.LayerList(so, so.FindProperty("fills"), false)),
                ("Stroke", VxIcon.Kind.Stroke, () => VectraContentInspector.LayerList(so, so.FindProperty("strokes"), true)),
                ("Effects", VxIcon.Kind.Effects, () => VectraContentInspector.EffectList(so, so.FindProperty("effects")))
            };
            var tabEls = new List<VisualElement>();

            void Show(int i)
            {
                SessionState.SetInt("vectra.style.tab", i);
                for (int k = 0; k < tabEls.Count; k++) tabEls[k].EnableInClassList("vx-tab--active", k == i);
                page.Clear();
                page.Add(defs[i].build());
            }

            for (int i = 0; i < defs.Length; i++)
            {
                int index = i;
                var tab = VxUI.El("vx-tab");
                tab.Add(new VxIcon(defs[i].icon, 13));
                tab.Add(VxUI.Text(defs[i].name, "vx-tab__label"));
                tab.RegisterCallback<ClickEvent>(_ => Show(index));
                tabEls.Add(tab);
                tabs.Add(tab);
            }

            Show(Mathf.Clamp(SessionState.GetInt("vectra.style.tab", 0), 0, defs.Length - 1));
            return root;
        }
    }
}
