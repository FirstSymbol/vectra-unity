using System;
using Unity.Collections;
using Unity.Mathematics;

namespace FirstSymbol.Vectra.Geometry
{
    /// <summary>How the winding number of a point decides whether it is inside a filled area.</summary>
    public enum FillRule : byte
    {
        NonZero,
        EvenOdd,
        /// <summary>Inside where the winding is positive (counter-clockwise contours in a y-up space).</summary>
        Positive,
        /// <summary>Inside where the winding is negative (clockwise contours in a y-up space).</summary>
        Negative
    }

    /// <summary>Boolean operation that combines an operand with the result of the previous operands.</summary>
    public enum BooleanOp : byte
    {
        Union,
        Subtract,
        Intersect,
        Exclude
    }

    public enum LineCap : byte
    {
        Butt,
        Round,
        Square
    }

    public enum LineJoin : byte
    {
        Miter,
        Round,
        Bevel
    }

    public enum PathVerb : byte
    {
        MoveTo,
        LineTo,
        QuadTo,
        CubicTo,
        Close
    }

    /// <summary>A run of points in <see cref="Polylines.points"/>.</summary>
    public struct Contour
    {
        public int start;
        public int count;
        public bool closed;
        /// <summary>Index of the boolean operand this contour belongs to.</summary>
        public int operand;
    }

    /// <summary>One boolean operand: its fill rule and how it combines with the operands before it.</summary>
    public struct Operand
    {
        public FillRule rule;
        public BooleanOp op;

        public Operand(FillRule rule, BooleanOp op = BooleanOp.Union)
        {
            this.rule = rule;
            this.op = op;
        }
    }

    [Flags]
    public enum TrapezoidEdges : byte
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8
    }

    /// <summary>
    /// A horizontal-band trapezoid produced by <see cref="ScanlineTessellator"/>. The flags tell which sides
    /// lie on the real boundary of the filled area (and therefore need anti-aliasing).
    /// </summary>
    public struct Trapezoid
    {
        public float yb, yt;
        public float xlb, xlt;
        public float xrb, xrt;
        public TrapezoidEdges flags;

        public float2 BottomLeft => new float2(xlb, yb);
        public float2 BottomRight => new float2(xrb, yb);
        public float2 TopLeft => new float2(xlt, yt);
        public float2 TopRight => new float2(xrt, yt);

        public float Area => 0.5f * ((xrb - xlb) + (xrt - xlt)) * (yt - yb);
    }

    /// <summary>A line segment, used for boundaries and distance fields.</summary>
    public struct Segment
    {
        public float2 a, b;

        public Segment(float2 a, float2 b)
        {
            this.a = a;
            this.b = b;
        }
    }

    /// <summary>A path made of verbs and control points, stored in native memory so that jobs can read it.</summary>
    public struct NativePath : IDisposable
    {
        public NativeList<byte> verbs;
        public NativeList<float2> points;

        public NativePath(Allocator allocator, int capacity = 32)
        {
            verbs = new NativeList<byte>(capacity, allocator);
            points = new NativeList<float2>(capacity * 3, allocator);
        }

        public bool IsCreated => verbs.IsCreated;
        public bool IsEmpty => verbs.Length == 0;

        public void Clear()
        {
            verbs.Clear();
            points.Clear();
        }

        public void MoveTo(float2 p)
        {
            verbs.Add((byte)PathVerb.MoveTo);
            points.Add(p);
        }

        public void LineTo(float2 p)
        {
            verbs.Add((byte)PathVerb.LineTo);
            points.Add(p);
        }

        public void QuadTo(float2 c, float2 p)
        {
            verbs.Add((byte)PathVerb.QuadTo);
            points.Add(c);
            points.Add(p);
        }

        public void CubicTo(float2 c1, float2 c2, float2 p)
        {
            verbs.Add((byte)PathVerb.CubicTo);
            points.Add(c1);
            points.Add(c2);
            points.Add(p);
        }

        public void Close()
        {
            verbs.Add((byte)PathVerb.Close);
        }

        public void Append(in NativePath other)
        {
            verbs.AddRange(other.verbs.AsArray());
            points.AddRange(other.points.AsArray());
        }

        public void Dispose()
        {
            if (verbs.IsCreated) verbs.Dispose();
            if (points.IsCreated) points.Dispose();
        }
    }

    /// <summary>Flattened contours (polylines). Each contour has an operand index for boolean operations.</summary>
    public struct Polylines : IDisposable
    {
        public NativeList<float2> points;
        public NativeList<Contour> contours;

        int m_OpenStart;
        int m_OpenOperand;

        public Polylines(Allocator allocator, int capacity = 64)
        {
            points = new NativeList<float2>(capacity, allocator);
            contours = new NativeList<Contour>(8, allocator);
            m_OpenStart = -1;
            m_OpenOperand = 0;
        }

        public bool IsCreated => points.IsCreated;
        public bool IsEmpty => contours.Length == 0;

        public void Clear()
        {
            points.Clear();
            contours.Clear();
            m_OpenStart = -1;
        }

        public void BeginContour(int operand = 0)
        {
            if (m_OpenStart >= 0) EndContour(false);
            m_OpenStart = points.Length;
            m_OpenOperand = operand;
        }

        public void AddPoint(float2 p)
        {
            if (m_OpenStart < 0) BeginContour(m_OpenOperand);
            int n = points.Length - m_OpenStart;
            if (n > 0 && math.all(points[points.Length - 1] == p)) return;
            points.Add(p);
        }

        public void EndContour(bool closed)
        {
            if (m_OpenStart < 0) return;
            int count = points.Length - m_OpenStart;
            if (closed && count > 1 && math.all(points[m_OpenStart] == points[points.Length - 1]))
            {
                points.RemoveAt(points.Length - 1);
                count--;
            }

            if (count > 0)
                contours.Add(new Contour { start = m_OpenStart, count = count, closed = closed, operand = m_OpenOperand });
            else
                points.ResizeUninitialized(m_OpenStart);
            m_OpenStart = -1;
        }

        /// <summary>Appends every contour of <paramref name="other"/>, optionally re-assigning the operand.</summary>
        public void Append(in Polylines other, int operandOverride = -1)
        {
            int baseIndex = points.Length;
            points.AddRange(other.points.AsArray());
            for (int i = 0; i < other.contours.Length; i++)
            {
                var c = other.contours[i];
                c.start += baseIndex;
                if (operandOverride >= 0) c.operand = operandOverride;
                contours.Add(c);
            }
        }

        public void Dispose()
        {
            if (points.IsCreated) points.Dispose();
            if (contours.IsCreated) contours.Dispose();
        }
    }

    /// <summary>Axis-aligned bounds helper.</summary>
    public struct Bounds2
    {
        public float2 min, max;

        public static Bounds2 Empty => new Bounds2 { min = new float2(float.MaxValue), max = new float2(float.MinValue) };

        public bool IsValid => min.x <= max.x && min.y <= max.y;
        public float2 Size => IsValid ? max - min : float2.zero;
        public float2 Center => (min + max) * 0.5f;

        public void Encapsulate(float2 p)
        {
            min = math.min(min, p);
            max = math.max(max, p);
        }

        public void Encapsulate(Bounds2 b)
        {
            if (!b.IsValid) return;
            min = math.min(min, b.min);
            max = math.max(max, b.max);
        }

        public Bounds2 Expanded(float amount) => new Bounds2 { min = min - amount, max = max + amount };
    }
}
