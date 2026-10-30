using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.Rendering;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// The serialized description of a shape shared by <see cref="VectraShape"/> (Canvas) and
    /// <see cref="VectraRenderer"/> (world): geometry plus the appearance stack (fills, strokes, effects), which can
    /// come from a shared <see cref="VectraStyle"/>.
    /// </summary>
    [Serializable]
    public class VectraShapeContent
    {
        [SerializeReference] public VectraShapeGeometry geometry = new RectangleShape();
        /// <summary>Shared geometry; overrides <see cref="geometry"/> when set.</summary>
        public VectraShapeAsset shapeAsset;
        public VectraStyle style;
        public List<FillLayer> fills = new List<FillLayer> { new FillLayer() };
        public List<StrokeLayer> strokes = new List<StrokeLayer>();
        [SerializeReference] public List<VectraEffect> effects = new List<VectraEffect>();
        public ShapeBlendMode blendMode = ShapeBlendMode.Normal;
        public VectraQuality quality = VectraQuality.High;
        /// <summary>Tessellate even when an analytic distance function exists (for debugging or exact outlines).</summary>
        public bool forcePath;

        public VectraShapeGeometry ActiveGeometry => shapeAsset != null && shapeAsset.geometry != null ? shapeAsset.geometry : geometry;
        public List<FillLayer> ActiveFills => style != null ? style.fills : fills;
        public List<StrokeLayer> ActiveStrokes => style != null ? style.strokes : strokes;
        public List<VectraEffect> ActiveEffects => style != null ? style.effects : effects;

        public VectraBuildInput ToInput(in ShapeFrame frame, Color tint, bool world) => new VectraBuildInput
        {
            geometry = ActiveGeometry,
            fills = ActiveFills,
            strokes = ActiveStrokes,
            effects = ActiveEffects,
            frame = frame,
            tint = tint,
            worldSpace = world,
            quality = quality,
            forcePath = forcePath
        };

        /// <summary>Copies the shared geometry into this content so it can be edited per instance.</summary>
        public void DetachShape()
        {
            if (shapeAsset == null) return;
            geometry = shapeAsset.geometry?.Clone();
            shapeAsset = null;
        }

        /// <summary>Copies the local appearance stack from the style so it can be edited per instance.</summary>
        public void DetachStyle()
        {
            if (style == null) return;
            fills = new List<FillLayer>();
            foreach (var f in style.fills) fills.Add(f?.Clone());
            strokes = new List<StrokeLayer>();
            foreach (var s in style.strokes) strokes.Add(s?.Clone());
            effects = new List<VectraEffect>();
            foreach (var e in style.effects) effects.Add(e?.Clone());
            style = null;
        }

        public VectraShapeContent Clone()
        {
            var c = (VectraShapeContent)MemberwiseClone();
            c.geometry = geometry?.Clone();
            c.fills = new List<FillLayer>();
            foreach (var f in fills) c.fills.Add(f?.Clone());
            c.strokes = new List<StrokeLayer>();
            foreach (var s in strokes) c.strokes.Add(s?.Clone());
            c.effects = new List<VectraEffect>();
            foreach (var e in effects) c.effects.Add(e?.Clone());
            return c;
        }
    }

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
