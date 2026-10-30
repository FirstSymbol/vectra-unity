using FirstSymbol.Vectra.Geometry;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

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

                    changed |= GradientHandles(target, content, frame);
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

                    Handles.color = new Color(0.3f, 0.7f, 1f);
                    EditorGUI.BeginChangeCheck();
                    var np = Handles.FreeMoveHandle(pos, Size(pos) * 1.4f, Vector3.zero, Handles.RectangleHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(target, "Move Path Point");
                        bp.position = p.FromLocal(frame, (Vector2)np, false);
                        changed = true;
                    }

                    // Ctrl/Cmd + click on a point deletes it.
                    if (e.type == EventType.MouseDown && e.button == 0 && EditorGUI.actionKey &&
                        HandleUtility.DistanceToCircle(pos, Size(pos) * 1.4f) < 1f && n > 2)
                    {
                        Undo.RecordObject(target, "Delete Path Point");
                        c.points.RemoveAt(i);
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
}
