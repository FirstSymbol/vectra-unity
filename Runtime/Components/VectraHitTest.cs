using System;
using FirstSymbol.Vectra.Geometry;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>Exact point-in-shape tests with a small cache of the tessellated outline.</summary>
    public sealed class VectraHitTester : IDisposable
    {
        NativePath m_Path;
        Polylines m_Poly;
        NativeList<Trapezoid> m_Traps;
        PathBuilder m_Builder;
        int m_Key;

        public VectraHitTester()
        {
            m_Path = new NativePath(Allocator.Persistent, 32);
            m_Poly = new Polylines(Allocator.Persistent, 64);
            m_Traps = new NativeList<Trapezoid>(32, Allocator.Persistent);
            m_Builder = new PathBuilder(m_Path);
        }

        void Ensure(VectraShapeGeometry geometry, in ShapeFrame frame)
        {
            var h = new HashBuilder(geometry.ComputeHash());
            h.Add(frame.rect.x);
            h.Add(frame.rect.y);
            h.Add(frame.rect.width);
            h.Add(frame.rect.height);
            int key = h.Value | 1;
            if (key == m_Key) return;
            m_Key = key;
            m_Path.Clear();
            m_Poly.Clear();
            m_Traps.Clear();
            m_Builder.Reset(m_Path, Affine2.Identity);
            geometry.BuildPath(frame, m_Builder);
            float tol = math.max(frame.pixelSize, 1e-4f) * 0.5f;
            VectraGeometry.Flatten(m_Path, Affine2.Identity, tol, 0, false, ref m_Poly);
            VectraGeometry.Tessellate(m_Poly, geometry.FillRule, ref m_Traps);
        }

        public bool Contains(VectraShapeGeometry geometry, in ShapeFrame frame, float2 p, float reach)
        {
            if (geometry == null) return false;
            Ensure(geometry, frame);
            if (geometry.IsClosed && ScanlineTessellator.Contains(m_Traps.AsArray(), p)) return true;
            if (reach <= 0f) return false;
            float r2 = reach * reach;
            for (int ci = 0; ci < m_Poly.contours.Length; ci++)
            {
                var c = m_Poly.contours[ci];
                int segs = c.closed ? c.count : c.count - 1;
                for (int i = 0; i < segs; i++)
                {
                    float2 a = m_Poly.points[c.start + i];
                    float2 b = m_Poly.points[c.start + (i + 1) % c.count];
                    if (SdfBaker.DistanceSq(p, a, b) <= r2) return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            m_Path.Dispose();
            m_Poly.Dispose();
            if (m_Traps.IsCreated) m_Traps.Dispose();
        }
    }

    public static class VectraHitTest
    {
        static VectraHitTester s_Shared;

        /// <summary>Hit test with a shared tester (convenient for occasional queries).</summary>
        public static bool Contains(VectraShapeGeometry geometry, in ShapeFrame frame, Vector2 p, float reach)
        {
            s_Shared ??= new VectraHitTester();
            return s_Shared.Contains(geometry, frame, p, reach);
        }
    }
}
