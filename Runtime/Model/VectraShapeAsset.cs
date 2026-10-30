using System;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// Shared geometry: shapes that reference this asset (<see cref="VectraShapeContent.shapeAsset"/>) draw its shape
    /// and update when it changes. Together with <see cref="VectraStyle"/> it forms a shape library: one asset per
    /// icon or primitive preset, reused across instances.
    /// </summary>
    [CreateAssetMenu(menuName = "Vectra/Shape", fileName = "VectraShape", order = 19)]
    public class VectraShapeAsset : ScriptableObject
    {
        [SerializeReference] public VectraShapeGeometry geometry = new RectangleShape();
        [Tooltip("Optional appearance applied by the shape library when the asset is placed in a scene.")]
        public VectraStyle defaultStyle;

        /// <summary>Raised when the asset is edited (inspector or <see cref="NotifyChanged"/>).</summary>
        public static event Action<VectraShapeAsset> Changed;

        public void NotifyChanged() => Changed?.Invoke(this);

        void OnValidate() => NotifyChanged();
    }
}
