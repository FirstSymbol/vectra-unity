using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using FirstSymbol.Vectra.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// Turns the direct child shapes (<see cref="VectraShape"/> or <see cref="VectraRenderer"/>) into the operands of
    /// a boolean shape drawn by the component on this object. Children stay editable (move, rotate, resize, change
    /// their geometry) but are not drawn themselves; the order of the children is the order of the operations.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Vectra/Vectra Boolean Group")]
    public class VectraBooleanGroup : MonoBehaviour, IVectraTicked
    {
        [Tooltip("Blend radius for smooth booleans (0 = exact).")]
        [SerializeField, Min(0f)] float m_Smoothness;
        [Tooltip("Hide the operand shapes (they still define the result).")]
        [SerializeField] bool m_HideOperands = true;

        int m_LastHash;
        bool m_Dirty = true;
        // Operand objects are reused between syncs (the owner's boolean shape holds them), so a sync does not allocate.
        readonly List<BooleanOperand> m_Operands = new List<BooleanOperand>();
        int m_OperandCount;

        struct ChildRefs
        {
            public Transform transform;
            public VectraShape shape;
            public VectraRenderer renderer;
            public VectraBooleanOperand operand;
        }

        // The components of the children, found once instead of three lookups per child per frame. The list is made again when the children
        // change or when a Vectra component is enabled or disabled somewhere.
        readonly List<ChildRefs> m_Children = new List<ChildRefs>();
        bool m_ChildrenKnown;
        int m_SeenEpoch;
        int m_WatchSlot = -1;
        bool m_Always;

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

        void OnEnable()
        {
            m_Dirty = true;
            m_ChildrenKnown = false;
            m_Always = false;
            VectraWatch.Add(this, transform, VectraWatch.GroupOther, false);
        }

        void OnValidate()
        {
            m_Dirty = true;
            m_ChildrenKnown = false;
        }

        void OnTransformChildrenChanged()
        {
            m_Dirty = true;
            m_ChildrenKnown = false;
        }

        void OnDisable()
        {
            VectraWatch.Remove(this);
            // Show the operands again when the group stops combining them.
            for (int i = 0; i < transform.childCount; i++) SetHidden(transform.GetChild(i), false);
        }

        int IVectraTicked.WatchSlot
        {
            get => m_WatchSlot;
            set => m_WatchSlot = value;
        }

        bool IVectraTicked.WatchPoll() => false;

        // The old LateUpdate: the children (matrices, rects, geometry) are compared every frame, so the group asks to be ticked every frame.
        bool IVectraTicked.WatchTick()
        {
            if (!m_Always)
            {
                m_Always = true;
                VectraWatch.SetAlways(this, true);
            }

            int hash = ComputeChildrenHash();
            if (!m_Dirty && hash == m_LastHash) return false;
            m_Dirty = false;
            m_LastHash = hash;
            Sync();
            return false;
        }

        void RefreshChildren()
        {
            m_Children.Clear();
            var self = transform;
            // Indexed loop: enumerating a Transform allocates.
            for (int ci = 0; ci < self.childCount; ci++)
            {
                var child = self.GetChild(ci);
                var refs = new ChildRefs { transform = child };
                if (child.TryGetComponent(out VectraShape s)) refs.shape = s;
                else if (child.TryGetComponent(out VectraRenderer vr)) refs.renderer = vr;
                if (child.TryGetComponent(out VectraBooleanOperand op)) refs.operand = op;
                m_Children.Add(refs);
            }

            m_ChildrenKnown = true;
            m_SeenEpoch = VectraWatch.Epoch;
        }

        int ComputeChildrenHash()
        {
            // Any Vectra component enabled or disabled anywhere (a shape or an operand added to a child) makes the group look again.
            if (m_ChildrenKnown && m_SeenEpoch != VectraWatch.Epoch) m_ChildrenKnown = false;
            if (!m_ChildrenKnown) RefreshChildren();
            var h = new HashBuilder(17);
            h.Add(m_Smoothness);
            h.Add(m_HideOperands);
            var self = transform;
            var toLocal = self.worldToLocalMatrix;
            for (int ci = 0; ci < m_Children.Count; ci++)
            {
                var refs = m_Children[ci];
                var child = refs.transform;
                if (child == null)
                {
                    // A child was destroyed: look again next frame.
                    m_ChildrenKnown = false;
                    m_Dirty = true;
                    continue;
                }

                if (!child.gameObject.activeSelf) continue;
                var m = toLocal * child.localToWorldMatrix;
                h.Add(m.m00); h.Add(m.m01); h.Add(m.m03);
                h.Add(m.m10); h.Add(m.m11); h.Add(m.m13);
                if (refs.shape != null)
                {
                    var r = refs.shape.rectTransform.rect;
                    h.Add(r.x); h.Add(r.y); h.Add(r.width); h.Add(r.height);
                    h.Add(refs.shape.Geometry?.ComputeHash() ?? 0);
                }
                else if (refs.renderer != null)
                {
                    var r = refs.renderer.LocalRect;
                    h.Add(r.x); h.Add(r.y); h.Add(r.width); h.Add(r.height);
                    h.Add(refs.renderer.Geometry?.ComputeHash() ?? 0);
                }

                if (refs.operand != null) h.Add((int)refs.operand.operation);
            }

            return h.Value;
        }

        /// <summary>Rebuilds the operand list from the children and pushes it to the owner shape.</summary>
        public void Sync()
        {
            m_OperandCount = 0;
            // Indexed loop: enumerating a Transform allocates.
            for (int ci = 0; ci < transform.childCount; ci++)
            {
                var child = transform.GetChild(ci);
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
                if (m_OperandCount == m_Operands.Count) m_Operands.Add(new BooleanOperand());
                var operand = m_Operands[m_OperandCount++];
                operand.enabled = true;
                operand.geometry = geometry;
                operand.operation = op;
                operand.rect = rect;
                operand.absolute = true;
                operand.transform = new Affine2(m.m00, m.m10, m.m01, m.m11, m.m03, m.m13);
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
            for (int i = 0; i < m_OperandCount; i++) target.operands.Add(m_Operands[i]);
            for (int i = m_OperandCount; i < m_Operands.Count; i++) m_Operands[i].geometry = null;
        }

        static void SetHidden(Transform child, bool hidden)
        {
            if (child.TryGetComponent(out VectraShape s)) s.canvasRenderer.cull = hidden;
            else if (child.TryGetComponent(out VectraRenderer r) && r.TryGetComponent(out MeshRenderer mr)) mr.enabled = !hidden;
        }
    }
}
