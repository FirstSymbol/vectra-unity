using System;
using FirstSymbol.Vectra.Geometry;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>Per-primitive constants of one layer.</summary>
    public struct LayerConstants
    {
        public float header;
        public float s0;
        public float4 s1;
        public float4 s2;
        public float paintV;
        public float paintParam;
        public Color32 color;
    }

    [BurstCompile]
    struct TrapezoidVerticesJob : IJob
    {
        [ReadOnly] public NativeArray<Trapezoid> traps;
        public LayerConstants constants;
        public Affine2 toPaint;
        public float2 origin;
        public float margin;
        public NativeList<VectraVertex> vertices;
        public NativeList<int> indices;

        public void Execute()
        {
            int baseVertex = vertices.Length;
            vertices.Capacity = math.max(vertices.Capacity, baseVertex + traps.Length * 4);
            indices.Capacity = math.max(indices.Capacity, indices.Length + traps.Length * 6);
            float maxExpand = margin * 8f;

            for (int i = 0; i < traps.Length; i++)
            {
                var t = traps[i];
                // Zero-height slivers (left by slab splits at edge crossings) carry no area but would get a wide,
                // nearly horizontal anti-aliasing fringe.
                if (t.yt - t.yb < margin * 1e-3f) continue;
                bool left = (t.flags & TrapezoidEdges.Left) != 0;
                bool right = (t.flags & TrapezoidEdges.Right) != 0;
                bool top = (t.flags & TrapezoidEdges.Top) != 0;
                bool bottom = (t.flags & TrapezoidEdges.Bottom) != 0;

                float2 lb = new float2(t.xlb, t.yb) - origin, lt = new float2(t.xlt, t.yt) - origin;
                float2 rb = new float2(t.xrb, t.yb) - origin, rt = new float2(t.xrt, t.yt) - origin;

                float angleL = 0f, cL = VectraEncoding.Inside, exL = 0f;
                if (left)
                {
                    float2 d = math.normalizesafe(lt - lb, new float2(0, 1));
                    float2 n = new float2(-d.y, d.x);
                    angleL = math.atan2(n.y, n.x);
                    cL = -math.dot(n, lb);
                    exL = math.min(margin / math.max(math.abs(n.x), 0.125f), maxExpand);
                }

                float angleR = 0f, cR = VectraEncoding.Inside, exR = 0f;
                if (right)
                {
                    float2 d = math.normalizesafe(rt - rb, new float2(0, 1));
                    float2 n = new float2(d.y, -d.x);
                    angleR = math.atan2(n.y, n.x);
                    cR = -math.dot(n, rb);
                    exR = math.min(margin / math.max(math.abs(n.x), 0.125f), maxExpand);
                }

                float exT = top ? margin : 0f;
                float exB = bottom ? margin : 0f;

                var c = constants;
                c.s1 = new float4(angleL, cL, angleR, cR);
                c.s2.x = lt.y;
                c.s2.y = lb.y;
                // Style bits 0/1 of the header flag boundary top/bottom sides (path headers carry style 0).
                c.header = constants.header + ((top ? 1 : 0) | (bottom ? 2 : 0)) * 1048576f;

                int v0 = vertices.Length;
                Add(lb + new float2(-exL, -exB), c);
                Add(rb + new float2(exR, -exB), c);
                Add(rt + new float2(exR, exT), c);
                Add(lt + new float2(-exL, exT), c);
                indices.Add(v0);
                indices.Add(v0 + 2);
                indices.Add(v0 + 1);
                indices.Add(v0);
                indices.Add(v0 + 3);
                indices.Add(v0 + 2);
            }
        }

        void Add(float2 p, in LayerConstants c)
        {
            float2 local = p + origin;
            float2 g = toPaint.Apply(local);
            vertices.Add(new VectraVertex
            {
                position = new float3(local, 0f),
                color = c.color,
                uv0 = new float4(p, c.header, c.s0),
                uv1 = c.s1,
                uv2 = c.s2,
                uv3 = new float4(g, c.paintV, c.paintParam)
            });
        }
    }

    /// <summary>
    /// Accumulates the vertices of every layer of a shape and uploads them to a <see cref="Mesh"/> in one go.
    /// </summary>
    public sealed class VectraMeshBuffer : IDisposable
    {
        public NativeList<VectraVertex> vertices;
        public NativeList<int> indices;
        public Bounds2 bounds;

        public VectraMeshBuffer()
        {
            vertices = new NativeList<VectraVertex>(64, Allocator.Persistent);
            indices = new NativeList<int>(96, Allocator.Persistent);
            bounds = Bounds2.Empty;
        }

        public int VertexCount => vertices.IsCreated ? vertices.Length : 0;
        public int TriangleCount => indices.IsCreated ? indices.Length / 3 : 0;

        public void Clear()
        {
            vertices.Clear();
            indices.Clear();
            bounds = Bounds2.Empty;
        }

        /// <summary>
        /// Adds a quad given by its corners in shape space. <paramref name="shapeToLocal"/> places it in local space.
        /// The shape-space position written per vertex is <c>corner - pOffset</c>.
        /// </summary>
        public void AddQuad(float2 pMin, float2 pMax, in Affine2 shapeToLocal, float2 pOffset, in LayerConstants c,
            in Affine2 toPaint)
        {
            int v0 = vertices.Length;
            AddVertex(new float2(pMin.x, pMin.y), shapeToLocal, pOffset, c, toPaint);
            AddVertex(new float2(pMax.x, pMin.y), shapeToLocal, pOffset, c, toPaint);
            AddVertex(new float2(pMax.x, pMax.y), shapeToLocal, pOffset, c, toPaint);
            AddVertex(new float2(pMin.x, pMax.y), shapeToLocal, pOffset, c, toPaint);
            indices.Add(v0);
            indices.Add(v0 + 2);
            indices.Add(v0 + 1);
            indices.Add(v0);
            indices.Add(v0 + 3);
            indices.Add(v0 + 2);
        }

        void AddVertex(float2 corner, in Affine2 shapeToLocal, float2 pOffset, in LayerConstants c, in Affine2 toPaint)
        {
            float2 local = shapeToLocal.Apply(corner + pOffset);
            bounds.Encapsulate(local);
            vertices.Add(new VectraVertex
            {
                position = new float3(local, 0f),
                color = c.color,
                uv0 = new float4(corner, c.header, c.s0),
                uv1 = c.s1,
                uv2 = c.s2,
                uv3 = new float4(toPaint.Apply(local), c.paintV, c.paintParam)
            });
        }

        /// <summary>Adds path trapezoids (local coordinates) with analytic edge anti-aliasing.</summary>
        public void AddTrapezoids(NativeArray<Trapezoid> traps, float2 origin, float margin, in LayerConstants c,
            in Affine2 toPaint)
        {
            if (traps.Length == 0) return;
            new TrapezoidVerticesJob
            {
                traps = traps,
                constants = c,
                toPaint = toPaint,
                origin = origin,
                margin = margin,
                vertices = vertices,
                indices = indices
            }.Run();
            var b = VectraGeometry.Bounds(traps);
            bounds.Encapsulate(b.Expanded(margin * 8f));
        }

        /// <summary>
        /// Uploads the buffer to <paramref name="mesh"/> using the standard vertex channels (position, colour,
        /// uv0-uv3) so that uGUI canvas batching reads it exactly like any other UI mesh.
        /// </summary>
        public void Upload(Mesh mesh)
        {
            if (mesh == null) return;
            int vc = vertices.Length;
            int ic = indices.Length;
            mesh.Clear();
            if (vc == 0 || ic == 0) return;

            var pos = new NativeArray<Vector3>(vc, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var col = new NativeArray<Color32>(vc, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var uv0 = new NativeArray<Vector4>(vc, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var uv1 = new NativeArray<Vector4>(vc, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var uv2 = new NativeArray<Vector4>(vc, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var uv3 = new NativeArray<Vector4>(vc, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < vc; i++)
            {
                var v = vertices[i];
                // Never hand non-finite data to the GPU.
                if (!math.all(math.isfinite(v.position))) v.position = float3.zero;
                pos[i] = v.position;
                col[i] = v.color;
                uv0[i] = Finite(v.uv0);
                uv1[i] = Finite(v.uv1);
                uv2[i] = Finite(v.uv2);
                uv3[i] = Finite(v.uv3);
            }

            mesh.indexFormat = vc > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(pos);
            mesh.SetColors(col);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetUVs(3, uv3);
            mesh.SetIndices(indices.AsArray(), MeshTopology.Triangles, 0, false);
            var b = bounds.IsValid ? bounds : new Bounds2 { min = float2.zero, max = float2.zero };
            mesh.bounds = new Bounds(new Vector3(b.Center.x, b.Center.y, 0f), new Vector3(b.Size.x, b.Size.y, 0.01f));

            pos.Dispose();
            col.Dispose();
            uv0.Dispose();
            uv1.Dispose();
            uv2.Dispose();
            uv3.Dispose();
        }

        static Vector4 Finite(float4 v)
        {
            bool4 ok = math.isfinite(v);
            return math.select(float4.zero, v, ok);
        }

        public void Dispose()
        {
            if (vertices.IsCreated) vertices.Dispose();
            if (indices.IsCreated) indices.Dispose();
        }
    }
}
