using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>Boolean operation of a child shape inside a <see cref="VectraBooleanGroup"/>.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Vectra/Vectra Boolean Operand")]
    public class VectraBooleanOperand : MonoBehaviour
    {
        public BooleanOp operation = BooleanOp.Union;

        void OnValidate()
        {
            var g = GetComponentInParent<VectraBooleanGroup>();
            if (g != null) g.MarkDirty();
        }
    }

    /// <summary>
    /// Turns the direct child shapes (<see cref="VectraShape"/> or <see cref="VectraRenderer"/>) into the operands of
    /// a boolean shape drawn by the component on this object. Children stay editable (move, rotate, resize, change
    /// their geometry) but are not drawn themselves; the order of the children is the order of the operations.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Vectra/Vectra Boolean Group")]
    public class VectraBooleanGroup : MonoBehaviour
    {
        [Tooltip("Blend radius for smooth booleans (0 = exact).")]
        [SerializeField, Min(0f)] float m_Smoothness;
        [Tooltip("Hide the operand shapes (they still define the result).")]
        [SerializeField] bool m_HideOperands = true;

        int m_LastHash;
        bool m_Dirty = true;
        readonly List<BooleanOperand> m_Operands = new List<BooleanOperand>();

        public float Smoothness
        {
            get => m_Smoothness;
            set
            {
                m_Smoothness = Mathf.Max(0f, value);
                m_Dirty = true;
            }
        }

        public void MarkDirty() => m_Dirty = true;

        void OnEnable() => m_Dirty = true;
        void OnValidate() => m_Dirty = true;
        void OnTransformChildrenChanged() => m_Dirty = true;

        void OnDisable()
        {
            // Show the operands again when the group stops combining them.
            foreach (Transform child in transform) SetHidden(child, false);
        }

        void LateUpdate()
        {
            int hash = ComputeChildrenHash();
            if (!m_Dirty && hash == m_LastHash) return;
            m_Dirty = false;
            m_LastHash = hash;
            Sync();
        }

        int ComputeChildrenHash()
        {
            var h = new HashBuilder(17);
            h.Add(m_Smoothness);
            h.Add(m_HideOperands);
            foreach (Transform child in transform)
            {
                if (!child.gameObject.activeSelf) continue;
                var m = transform.worldToLocalMatrix * child.localToWorldMatrix;
                h.Add(m.m00); h.Add(m.m01); h.Add(m.m03);
                h.Add(m.m10); h.Add(m.m11); h.Add(m.m13);
                if (child.TryGetComponent(out VectraShape s))
                {
                    var r = s.rectTransform.rect;
                    h.Add(r.x); h.Add(r.y); h.Add(r.width); h.Add(r.height);
                    h.Add(s.Geometry?.ComputeHash() ?? 0);
                }
                else if (child.TryGetComponent(out VectraRenderer vr))
                {
                    var r = vr.LocalRect;
                    h.Add(r.x); h.Add(r.y); h.Add(r.width); h.Add(r.height);
                    h.Add(vr.Geometry?.ComputeHash() ?? 0);
                }

                if (child.TryGetComponent(out VectraBooleanOperand op)) h.Add((int)op.operation);
            }

            return h.Value;
        }

        /// <summary>Rebuilds the operand list from the children and pushes it to the owner shape.</summary>
        public void Sync()
        {
            m_Operands.Clear();
            foreach (Transform child in transform)
            {
                if (!child.gameObject.activeSelf) continue;
                VectraShapeGeometry geometry = null;
                Rect rect = default;
                if (child.TryGetComponent(out VectraShape s))
                {
                    geometry = s.Geometry;
                    rect = s.rectTransform.rect;
                }
                else if (child.TryGetComponent(out VectraRenderer vr))
                {
                    geometry = vr.Geometry;
                    rect = vr.LocalRect;
                }

                if (geometry == null || geometry is BooleanShape) continue;
                var m = transform.worldToLocalMatrix * child.localToWorldMatrix;
                var op = child.TryGetComponent(out VectraBooleanOperand o) ? o.operation : BooleanOp.Union;
                m_Operands.Add(new BooleanOperand
                {
                    geometry = geometry,
                    operation = op,
                    rect = rect,
                    absolute = true,
                    transform = new Affine2(m.m00, m.m10, m.m01, m.m11, m.m03, m.m13)
                });
                SetHidden(child, m_HideOperands);
            }

            BooleanShape target = null;
            if (TryGetComponent(out VectraShape owner))
            {
                target = owner.Geometry as BooleanShape ?? owner.SetGeometry<BooleanShape>();
                Apply(target);
                owner.SetShapeDirty();
            }
            else if (TryGetComponent(out VectraRenderer ownerR))
            {
                target = ownerR.Geometry as BooleanShape ?? ownerR.SetGeometry<BooleanShape>();
                Apply(target);
                ownerR.SetDirty();
            }
        }

        void Apply(BooleanShape target)
        {
            target.smoothness = m_Smoothness;
            target.operands.Clear();
            target.operands.AddRange(m_Operands);
        }

        static void SetHidden(Transform child, bool hidden)
        {
            if (child.TryGetComponent(out VectraShape s)) s.canvasRenderer.cull = hidden;
            else if (child.TryGetComponent(out VectraRenderer r) && r.TryGetComponent(out MeshRenderer mr)) mr.enabled = !hidden;
        }
    }
}
