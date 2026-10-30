using FirstSymbol.Vectra.Geometry;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>
    /// Scene view editing of Vectra shapes: Bezier path points and tangents (midpoint "+" inserts a point,
    /// Ctrl/Cmd+click deletes), polyline points, rectangle corner radii, arc angles and gradient handles of the
    /// first gradient fill.
    /// </summary>
    abstract class VectraShapeToolBase : EditorTool
    {
        static GUIContent s_Icon;

        public override GUIContent toolbarIcon => s_Icon ??= new GUIContent(EditorGUIUtility.IconContent("EditCollider").image, "Edit Vectra Shape");

        // Tool options (edited by the scene view overlay).
        internal static bool snap;
        internal static float snapStep = 5f;
        internal static bool showGradient = true;
        internal static bool showEffects = true;
        internal static int selectedContour = -1, selectedPoint = -1;
        internal static Object selectedTarget;
        internal static System.Action selectionChanged;

        internal static Vector2 Snap(Vector2 p) => snap && snapStep > 0f ? new Vector2(Mathf.Round(p.x / snapStep) * snapStep, Mathf.Round(p.y / snapStep) * snapStep) : p;

        static void Select(Object target, int contour, int point)
        {
            if (selectedTarget == target && selectedContour == contour && selectedPoint == point) return;
            selectedTarget = target;
            selectedContour = contour;
            selectedPoint = point;
            selectionChanged?.Invoke();
        }

        /// <summary>The selected Bezier point of the active tool, if any.</summary>
        internal static bool TryGetSelectedPoint(out PathShape path, out BezierContour contour, out int index)
        {
            path = null;
            contour = null;
            index = selectedPoint;
            var content = (selectedTarget as IVectraShape)?.Content;
            if (!(content?.geometry is PathShape p) || selectedContour < 0 || selectedContour >= p.contours.Count) return false;
            var c = p.contours[selectedContour];
            if (c?.points == null || index < 0 || index >= c.points.Count) return false;
            path = p;
            contour = c;
            return true;
        }

        protected abstract bool TryGet(Object target, out Transform t, out Rect rect, out VectraShapeContent content);
        protected abstract void MarkDirty(Object target);

        public override void OnToolGUI(EditorWindow window)
        {
            foreach (var target in targets)
            {
                if (!TryGet(target, out var t, out var rect, out var content) || content == null) continue;
                var frame = new ShapeFrame { rect = rect, pixelSize = 1f };
                using (new Handles.DrawingScope(t.localToWorldMatrix))
                {
                    bool changed = false;
                    switch (content.geometry)
                    {
                        case PathShape p: changed |= PathHandles(target, p, frame); break;
                        case PolylineShape pl: changed |= PolylineHandles(target, pl, frame); break;
                        case RectangleShape r: changed |= RectHandles(target, r, frame); break;
                        case ArcShape a: changed |= ArcHandles(target, a, frame); break;
                    }

                    if (showGradient) changed |= GradientHandles(target, content, frame);
                    if (showEffects) changed |= EffectHandles(target, content, frame);
                    if (changed) MarkDirty(target);
                }
            }
        }

        static float Size(Vector3 p) => HandleUtility.GetHandleSize(p) * 0.06f;

        static bool PathHandles(Object target, PathShape p, ShapeFrame frame)
        {
            bool changed = false;
            var e = Event.current;
            for (int ci = 0; ci < p.contours.Count; ci++)
            {
                var c = p.contours[ci];
                if (c?.points == null) continue;
                int n = c.points.Count;
                for (int i = 0; i < n; i++)
                {
                    var bp = c.points[i];
                    Vector3 pos = (Vector2)(Vector3)ToV(p.ToLocal(frame, bp.position, false));
                    Vector3 inH = pos + (Vector3)ToV(p.ToLocal(frame, bp.inTangent, true));
                    Vector3 outH = pos + (Vector3)ToV(p.ToLocal(frame, bp.outTangent, true));

                    Handles.color = new Color(1f, 1f, 1f, 0.5f);
                    if (bp.inTangent != Vector2.zero) Handles.DrawLine(pos, inH);
                    if (bp.outTangent != Vector2.zero) Handles.DrawLine(pos, outH);

                    bool isSelected = selectedTarget == target && selectedContour == ci && selectedPoint == i;
                    // A plain click on a point selects it (the overlay edits its mode).
                    if (e.type == EventType.MouseDown && e.button == 0 && !EditorGUI.actionKey &&
                        HandleUtility.DistanceToCircle(pos, Size(pos) * 1.4f) < 1f)
                        Select(target, ci, i);

                    Handles.color = isSelected ? new Color(1f, 0.95f, 0.3f) : new Color(0.3f, 0.7f, 1f);
                    EditorGUI.BeginChangeCheck();
                    var np = Handles.FreeMoveHandle(pos, Size(pos) * (isSelected ? 1.8f : 1.4f), Vector3.zero, Handles.RectangleHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(target, "Move Path Point");
                        bp.position = p.FromLocal(frame, Snap((Vector2)np), false);
                        Select(target, ci, i);
                        changed = true;
                    }

                    // Ctrl/Cmd + click on a point deletes it.
                    if (e.type == EventType.MouseDown && e.button == 0 && EditorGUI.actionKey &&
                        HandleUtility.DistanceToCircle(pos, Size(pos) * 1.4f) < 1f && n > 2)
                    {
                        Undo.RecordObject(target, "Delete Path Point");
                        c.points.RemoveAt(i);
                        Select(null, -1, -1);
                        e.Use();
                        return true;
                    }

                    Handles.color = new Color(1f, 0.8f, 0.3f);
                    if (bp.inTangent != Vector2.zero || bp.mode != BezierPointMode.Corner)
                    {
                        EditorGUI.BeginChangeCheck();
                        var ni = Handles.FreeMoveHandle(inH, Size(inH), Vector3.zero, Handles.CircleHandleCap);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(target, "Move Tangent");
                            bp.inTangent = p.FromLocal(frame, (Vector2)(ni - pos), true);
                            bp.outTangent = Mirror(bp.mode, bp.inTangent, bp.outTangent);
                            changed = true;
                        }
                    }

                    if (bp.outTangent != Vector2.zero || bp.mode != BezierPointMode.Corner)
                    {
                        EditorGUI.BeginChangeCheck();
                        var no = Handles.FreeMoveHandle(outH, Size(outH), Vector3.zero, Handles.CircleHandleCap);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(target, "Move Tangent");
                            bp.outTangent = p.FromLocal(frame, (Vector2)(no - pos), true);
                            bp.inTangent = Mirror(bp.mode, bp.outTangent, bp.inTangent);
                            changed = true;
                        }
                    }

                    c.points[i] = bp;
                }

                // Midpoint "+" handles insert a point.
                int segs = c.closed ? n : n - 1;
                for (int i = 0; i < segs; i++)
                {
                    var a = c.points[i];
                    var b = c.points[(i + 1) % n];
                    Vector2 pa = ToV(p.ToLocal(frame, a.position, false)), pb = ToV(p.ToLocal(frame, b.position, false));
                    Vector2 ca = pa + ToV(p.ToLocal(frame, a.outTangent, true)), cb = pb + ToV(p.ToLocal(frame, b.inTangent, true));
                    Vector2 mid = 0.125f * pa + 0.375f * ca + 0.375f * cb + 0.125f * pb;
                    Handles.color = new Color(0.4f, 1f, 0.5f, 0.8f);
                    if (Handles.Button(mid, Quaternion.identity, Size(mid) * 0.8f, Size(mid), Handles.DotHandleCap))
                    {
                        Undo.RecordObject(target, "Insert Path Point");
                        c.points.Insert(i + 1, new BezierPoint(p.FromLocal(frame, mid, false)));
                        return true;
                    }
                }
            }

            return changed;
        }

        static Vector2 Mirror(BezierPointMode mode, Vector2 moved, Vector2 other)
        {
            if (mode == BezierPointMode.Symmetric) return -moved;
            if (mode == BezierPointMode.Smooth) return -moved.normalized * other.magnitude;
            return other;
        }

        static Vector2 ToV(Unity.Mathematics.float2 v) => new Vector2(v.x, v.y);

        static bool PolylineHandles(Object target, PolylineShape pl, ShapeFrame frame)
        {
            bool changed = false;
            for (int i = 0; i < pl.points.Count; i++)
            {
                Vector3 pos = pl.coordinates == PathCoordinates.Local ? (Vector3)pl.points[i] : (Vector3)ToV(frame.FromNormalized(pl.points[i]));
                Handles.color = new Color(0.3f, 0.7f, 1f);
                EditorGUI.BeginChangeCheck();
                var np = Handles.FreeMoveHandle(pos, Size(pos) * 1.4f, Vector3.zero, Handles.RectangleHandleCap);
                if (!EditorGUI.EndChangeCheck()) continue;
                Undo.RecordObject(target, "Move Point");
                var size = frame.Size;
                np = Snap((Vector2)np);
                pl.points[i] = pl.coordinates == PathCoordinates.Local
                    ? (Vector2)np
                    : new Vector2((np.x - frame.rect.xMin) / Mathf.Max(size.x, 1e-6f), (np.y - frame.rect.yMin) / Mathf.Max(size.y, 1e-6f));
                changed = true;
            }

            return changed;
        }

        static bool RectHandles(Object target, RectangleShape r, ShapeFrame frame)
        {
            bool changed = false;
            var rect = frame.rect;
            float max = Mathf.Min(rect.width, rect.height) * 0.5f;
            // x = top-left, y = top-right, z = bottom-right, w = bottom-left
            Vector2[] corners = { new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMin, rect.yMin) };
            Vector2[] inward = { new Vector2(1, -1), new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1) };
            for (int i = 0; i < 4; i++)
            {
                float radius = r.cornerRadii[i];
                // The handle sits at the centre of the corner arc.
                Vector3 pos = corners[i] + inward[i] * Mathf.Max(radius, max * 0.08f);
                Handles.color = new Color(1f, 0.55f, 0.2f);
                EditorGUI.BeginChangeCheck();
                var np = Handles.Slider2D(pos, Vector3.forward, Vector3.right, Vector3.up, Size(pos), Handles.DotHandleCap, 0f);
                if (!EditorGUI.EndChangeCheck()) continue;
                Undo.RecordObject(target, "Corner Radius");
                Vector2 d = (Vector2)np - corners[i];
                float nr = Mathf.Clamp(Vector2.Dot(d, inward[i]) * 0.5f, 0f, max);
                var radii = r.cornerRadii;
                if (Event.current.shift) radii[i] = nr;
                else radii = new Vector4(nr, nr, nr, nr);
                r.cornerRadii = radii;
                changed = true;
            }

            return changed;
        }

        static bool ArcHandles(Object target, ArcShape a, ShapeFrame frame)
        {
            bool changed = false;
            Vector2 c = ToV(frame.Center);
            float radius = frame.MinHalf;
            float rot = a.rotation * Mathf.Deg2Rad;
            Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad + rot), Mathf.Sin(deg * Mathf.Deg2Rad + rot));
            Handles.color = new Color(0.4f, 1f, 0.6f);
            Vector3 sp = c + Dir(a.startAngle) * radius;
            Vector3 ep = c + Dir(a.startAngle + a.sweepAngle) * radius;
            EditorGUI.BeginChangeCheck();
            var ns = Handles.Slider2D(sp, Vector3.forward, Vector3.right, Vector3.up, Size(sp), Handles.DotHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Arc Start");
                Vector2 d = (Vector2)ns - c;
                a.startAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - a.rotation;
                changed = true;
            }

            EditorGUI.BeginChangeCheck();
            var ne = Handles.Slider2D(ep, Vector3.forward, Vector3.right, Vector3.up, Size(ep), Handles.DotHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Arc Sweep");
                Vector2 d = (Vector2)ne - c;
                float end = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - a.rotation;
                a.sweepAngle = Mathf.Repeat(end - a.startAngle, 360f);
                changed = true;
            }

            return changed;
        }

        static bool GradientHandles(Object target, VectraShapeContent content, ShapeFrame frame)
        {
            var fills = content.ActiveFills;
            if (fills == null) return false;
            foreach (var f in fills)
            {
                if (f == null || !f.enabled || f.paint == null || !f.paint.IsGradient || f.paint.useCustomMapping) continue;
                var paint = f.paint;
                Vector3 s = ToV(frame.FromNormalized(paint.gradientStart));
                Vector3 e = ToV(frame.FromNormalized(paint.gradientEnd));
                Handles.color = new Color(1f, 1f, 1f, 0.7f);
                Handles.DrawDottedLine(s, e, 3f);
                bool changed = false;
                Handles.color = Color.white;
                EditorGUI.BeginChangeCheck();
                var ns = Handles.Slider2D(s, Vector3.forward, Vector3.right, Vector3.up, Size(s) * 1.3f, Handles.CircleHandleCap, 0f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Gradient Start");
                    paint.gradientStart = Normalize(frame, ns);
                    changed = true;
                }

                EditorGUI.BeginChangeCheck();
                var ne = Handles.Slider2D(e, Vector3.forward, Vector3.right, Vector3.up, Size(e) * 1.3f, Handles.RectangleHandleCap, 0f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Gradient End");
                    paint.gradientEnd = Normalize(frame, ne);
                    changed = true;
                }

                return changed;
            }

            return false;
        }

        /// <summary>Offset handle of the first drop shadow: drag it to move the shadow.</summary>
        static bool EffectHandles(Object target, VectraShapeContent content, ShapeFrame frame)
        {
            var effects = content.ActiveEffects;
            if (effects == null) return false;
            foreach (var fx in effects)
            {
                if (!(fx is DropShadowEffect ds) || !ds.enabled) continue;
                Vector3 c = ToV(frame.Center);
                Vector3 h = c + (Vector3)ds.offset;
                Handles.color = new Color(0.6f, 0.6f, 0.65f, 0.8f);
                Handles.DrawDottedLine(c, h, 2f);
                EditorGUI.BeginChangeCheck();
                var nh = Handles.Slider2D(h, Vector3.forward, Vector3.right, Vector3.up, Size(h) * 1.2f, Handles.CircleHandleCap, 0f);
                if (!EditorGUI.EndChangeCheck()) return false;
                Undo.RecordObject(target, "Shadow Offset");
                ds.offset = Snap((Vector2)(nh - c));
                return true;
            }

            return false;
        }

        static Vector2 Normalize(ShapeFrame frame, Vector3 p) =>
            new Vector2((p.x - frame.rect.xMin) / Mathf.Max(frame.rect.width, 1e-6f), (p.y - frame.rect.yMin) / Mathf.Max(frame.rect.height, 1e-6f));
    }

    [EditorTool("Edit Vectra Shape", typeof(VectraShape))]
    class VectraShapeTool : VectraShapeToolBase
    {
        protected override bool TryGet(Object target, out Transform t, out Rect rect, out VectraShapeContent content)
        {
            var s = target as VectraShape;
            t = s != null ? s.transform : null;
            rect = s != null ? s.rectTransform.rect : default;
            content = s != null ? s.Content : null;
            return s != null;
        }

        protected override void MarkDirty(Object target) => ((VectraShape)target).SetShapeDirty();
    }

    [EditorTool("Edit Vectra Shape", typeof(VectraRenderer))]
    class VectraRendererTool : VectraShapeToolBase
    {
        protected override bool TryGet(Object target, out Transform t, out Rect rect, out VectraShapeContent content)
        {
            var r = target as VectraRenderer;
            t = r != null ? r.transform : null;
            rect = r != null ? r.LocalRect : default;
            content = r != null ? r.Content : null;
            return r != null;
        }

        protected override void MarkDirty(Object target) => ((VectraRenderer)target).SetDirty();
    }

    /// <summary>Scene view overlay shown while the Vectra shape tool is active: point modes, snapping, handle toggles.</summary>
    [Overlay(typeof(SceneView), "vectra-shape-tool", "Vectra Shape")]
    class VectraShapeToolOverlay : Overlay, ITransientOverlay
    {
        public bool visible
        {
            get
            {
                var t = ToolManager.activeToolType;
                return t != null && typeof(VectraShapeToolBase).IsAssignableFrom(t);
            }
        }

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement { style = { minWidth = 210, paddingLeft = 4, paddingRight = 4, paddingTop = 2, paddingBottom = 4 } };

            var pointLabel = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 2 } };
            root.Add(pointLabel);
            var modes = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var buttons = new Button[3];
            string[] names = { "Corner", "Smooth", "Symmetric" };
            for (int m = 0; m < 3; m++)
            {
                var mode = (BezierPointMode)m;
                buttons[m] = new Button(() => SetMode(mode)) { text = names[m], tooltip = "Tangent mode of the selected point", style = { flexGrow = 1 } };
                modes.Add(buttons[m]);
            }

            root.Add(modes);

            void Refresh()
            {
                bool has = VectraShapeToolBase.TryGetSelectedPoint(out _, out var contour, out int index);
                pointLabel.text = has ? $"Point {index + 1} of {contour.points.Count}" : "Click a path point to select it";
                for (int m = 0; m < 3; m++)
                {
                    buttons[m].SetEnabled(has);
                    bool active = has && (int)contour.points[index].mode == m;
                    buttons[m].style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
                    buttons[m].style.backgroundColor = active ? new Color(0.25f, 0.45f, 0.8f, 0.6f) : new StyleColor(StyleKeyword.Null);
                }
            }

            VectraShapeToolBase.selectionChanged += Refresh;
            root.RegisterCallback<DetachFromPanelEvent>(_ => VectraShapeToolBase.selectionChanged -= Refresh);
            root.schedule.Execute(Refresh).Every(250);

            var snapRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6, alignItems = Align.Center } };
            var snapToggle = new Toggle("Snap") { value = VectraShapeToolBase.snap, tooltip = "Round dragged points to a grid (local units)" };
            snapToggle.labelElement.style.minWidth = 40;
            snapToggle.RegisterValueChangedCallback(e => VectraShapeToolBase.snap = e.newValue);
            var step = new FloatField { value = VectraShapeToolBase.snapStep, style = { width = 50 } };
            step.RegisterValueChangedCallback(e => VectraShapeToolBase.snapStep = Mathf.Max(0.01f, e.newValue));
            snapRow.Add(snapToggle);
            snapRow.Add(step);
            root.Add(snapRow);

            var grad = new Toggle("Gradient handles") { value = VectraShapeToolBase.showGradient };
            grad.RegisterValueChangedCallback(e =>
            {
                VectraShapeToolBase.showGradient = e.newValue;
                SceneView.RepaintAll();
            });
            root.Add(grad);
            var fx = new Toggle("Shadow handle") { value = VectraShapeToolBase.showEffects };
            fx.RegisterValueChangedCallback(e =>
            {
                VectraShapeToolBase.showEffects = e.newValue;
                SceneView.RepaintAll();
            });
            root.Add(fx);
            root.Add(new Label("+ on a segment inserts a point · Ctrl/Cmd+click deletes · Shift on a corner edits one radius")
                { style = { whiteSpace = WhiteSpace.Normal, fontSize = 10, opacity = 0.7f, marginTop = 4, maxWidth = 220 } });
            Refresh();
            return root;
        }

        static void SetMode(BezierPointMode mode)
        {
            if (!VectraShapeToolBase.TryGetSelectedPoint(out _, out var contour, out int index)) return;
            var target = VectraShapeToolBase.selectedTarget;
            Undo.RecordObject(target, "Point Mode");
            var bp = contour.points[index];
            bp.mode = mode;
            // Make the tangents consistent with the new mode right away.
            if (mode != BezierPointMode.Corner)
            {
                var dir = (bp.outTangent - bp.inTangent).normalized;
                if (dir == Vector2.zero) dir = Vector2.right;
                float lo = bp.outTangent.magnitude, li = bp.inTangent.magnitude;
                if (lo + li < 1e-4f) lo = li = 0.1f;
                if (mode == BezierPointMode.Symmetric) lo = li = (lo + li) * 0.5f;
                bp.outTangent = dir * lo;
                bp.inTangent = -dir * li;
            }

            contour.points[index] = bp;
            (target as IVectraShape)?.SetShapeDirty();
            EditorUtility.SetDirty(target);
            VectraShapeToolBase.selectionChanged?.Invoke();
            SceneView.RepaintAll();
        }
    }}
