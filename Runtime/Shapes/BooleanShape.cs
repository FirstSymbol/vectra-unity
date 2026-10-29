using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>One operand of a <see cref="BooleanShape"/>.</summary>
    [Serializable]
    public class BooleanOperand
    {
        public bool enabled = true;
        public BooleanOp operation = BooleanOp.Union;
        [SerializeReference] public VectraShapeGeometry geometry = new EllipseShape();
        /// <summary>Frame of the operand. Normalized (0..1 of the owner rect) unless <see cref="absolute"/> is set.</summary>
        public Rect rect = new Rect(0f, 0f, 1f, 1f);
        public bool absolute;
        /// <summary>Extra transform applied to the operand (used by <see cref="VectraBooleanGroup"/> for child transforms).</summary>
        public Affine2 transform = Affine2.Identity;

        public BooleanOperand() { }

        public BooleanOperand(VectraShapeGeometry geometry, BooleanOp op, Rect normalizedRect)
        {
            this.geometry = geometry;
            operation = op;
            rect = normalizedRect;
        }

        public ShapeFrame Frame(in ShapeFrame owner)
        {
            if (absolute) return new ShapeFrame { rect = rect, pixelSize = owner.pixelSize };
            var r = owner.rect;
            return new ShapeFrame
            {
                rect = new Rect(r.xMin + rect.x * r.width, r.yMin + rect.y * r.height, rect.width * r.width, rect.height * r.height),
                pixelSize = owner.pixelSize
            };
        }

        public BooleanOperand Clone()
        {
            var o = (BooleanOperand)MemberwiseClone();
            o.geometry = geometry?.Clone();
            return o;
        }
    }

    /// <summary>
    /// Combines any number of shapes with union, subtract, intersect and exclude, evaluated in order. With a
    /// smoothness above zero the operations blend smoothly (rendered through a signed distance field).
    /// </summary>
    [Serializable]
    public sealed class BooleanShape : VectraShapeGeometry
    {
        public List<BooleanOperand> operands = new List<BooleanOperand>();
        /// <summary>Blend radius in local units for smooth booleans (0 = exact geometric booleans).</summary>
        [Min(0f)] public float smoothness;

        public override string DisplayName => "Boolean Group";

        public BooleanShape()
        {
            operands.Add(new BooleanOperand(new RectangleShape(), BooleanOp.Union, new Rect(0f, 0f, 0.7f, 0.7f)));
            operands.Add(new BooleanOperand(new EllipseShape(), BooleanOp.Union, new Rect(0.3f, 0.3f, 0.7f, 0.7f)));
        }

        public override VectraShapeGeometry Clone()
        {
            var b = (BooleanShape)MemberwiseClone();
            b.operands = new List<BooleanOperand>();
            foreach (var o in operands) b.operands.Add(o?.Clone());
            return b;
        }

        static PathBuilder s_Builder;

        /// <summary>Flattens every enabled operand into <paramref name="output"/> and fills the operand table.</summary>
        public void FlattenOperands(in ShapeFrame frame, float tolerance, ref Polylines output, List<Operand> ops)
        {
            ops.Clear();
            var path = new NativePath(Allocator.TempJob, 64);
            s_Builder ??= new PathBuilder(path);
            int index = 0;
            foreach (var o in operands)
            {
                if (o == null || !o.enabled || o.geometry == null || o.geometry == this) continue;
                path.Clear();
                s_Builder.Reset(path, Affine2.Translate(frame.Center) * Affine2.Rotate(math.radians(rotation)) *
                                      Affine2.Translate(-frame.Center) * o.transform);
                o.geometry.BuildPath(o.Frame(frame), s_Builder);
                VectraGeometry.Flatten(path, Affine2.Identity, tolerance, index, true, ref output);
                ops.Add(new Operand(o.geometry.FillRule, index == 0 ? BooleanOp.Union : o.operation));
                index++;
            }

            path.Dispose();
        }

        /// <summary>The exact (hard) boolean result as trapezoids.</summary>
        public void Tessellate(in ShapeFrame frame, float tolerance, ref NativeList<Trapezoid> traps)
        {
            var poly = new Polylines(Allocator.TempJob, 256);
            var ops = new List<Operand>();
            FlattenOperands(frame, tolerance, ref poly, ops);
            if (ops.Count > 0)
            {
                var arr = new NativeArray<Operand>(ops.ToArray(), Allocator.TempJob);
                VectraGeometry.Tessellate(poly, arr, ref traps);
                arr.Dispose();
            }

            poly.Dispose();
        }

        public override void BuildPath(in ShapeFrame frame, PathBuilder b)
        {
            // The result outline (outer contours counter-clockwise, holes clockwise).
            float tol = math.max(frame.pixelSize, 1e-4f) * 0.15f;
            var traps = new NativeList<Trapezoid>(256, Allocator.TempJob);
            Tessellate(frame, tol, ref traps);
            var outline = new Polylines(Allocator.TempJob, 256);
            VectraGeometry.BuildContours(traps.AsArray(), ref outline);
            for (int ci = 0; ci < outline.contours.Length; ci++)
            {
                var c = outline.contours[ci];
                b.MoveTo(outline.points[c.start]);
                for (int i = 1; i < c.count; i++) b.LineTo(outline.points[c.start + i]);
                b.Close();
            }

            outline.Dispose();
            traps.Dispose();
        }

        protected override void Hash(ref HashBuilder h)
        {
            h.Add(smoothness);
            foreach (var o in operands)
            {
                if (o == null || o.geometry == null || o.geometry == this) continue;
                h.Add(o.enabled);
                h.Add((int)o.operation);
                h.Add(o.geometry.ComputeHash());
                h.Add(o.rect.x);
                h.Add(o.rect.y);
                h.Add(o.rect.width);
                h.Add(o.rect.height);
                h.Add(o.absolute);
                h.Add(o.transform.GetHashCode());
            }
        }
    }
}
