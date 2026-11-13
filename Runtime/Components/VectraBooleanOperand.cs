using FirstSymbol.Vectra.Geometry;
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

        // A child that gains or loses an operand is noticed by its group in the same frame.
        void OnEnable() => FirstSymbol.Vectra.Rendering.VectraWatch.Epoch++;
        void OnDisable() => FirstSymbol.Vectra.Rendering.VectraWatch.Epoch++;

        void OnValidate()
        {
            var g = GetComponentInParent<VectraBooleanGroup>();
            if (g != null) g.MarkDirty();
        }
    }
}
