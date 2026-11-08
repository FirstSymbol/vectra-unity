using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// Reusable appearance (fills, strokes, effects), the equivalent of a CSS class or a Figma style. Every shape
    /// that references it updates when it changes.
    /// </summary>
    [CreateAssetMenu(menuName = "Vectra/Style", fileName = "VectraStyle", order = 20)]
    public class VectraStyle : ScriptableObject
    {
        public List<FillLayer> fills = new List<FillLayer> { new FillLayer() };
        public List<StrokeLayer> strokes = new List<StrokeLayer>();
        [SerializeReference] public List<VectraEffect> effects = new List<VectraEffect>();

        /// <summary>Raised when the style is edited (inspector or <see cref="NotifyChanged"/>).</summary>
        public static event Action<VectraStyle> Changed;

        public void NotifyChanged() => Changed?.Invoke(this);

        void OnValidate() => NotifyChanged();
    }
}
