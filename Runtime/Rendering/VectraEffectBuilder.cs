using FirstSymbol.Vectra.Rendering;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>Where a custom effect is drawn in the layer stack.</summary>
    public enum VectraEffectOrder
    {
        BehindFill,
        AboveFill,
        AboveStrokes
    }

    /// <summary>
    /// Base class for user-defined effects. Override <see cref="Populate"/> and emit layers through the builder;
    /// they go through the same change tracking, batching and shader as the built-in effects. Subclasses must be
    /// [Serializable] with a parameterless constructor; they appear in the inspector's "Add effect" menu.
    /// <code>
    /// [Serializable] public class NeonRings : VectraCustomEffect
    /// {
    ///     public Color color = Color.cyan; public int rings = 3; public float gap = 6f;
    ///     public override string DisplayName => "Neon Rings";
    ///     public override bool NeedsDistanceField => true;
    ///     public override float Reach => rings * gap + 4f;
    ///     public override void Populate(VectraEffectBuilder b)
    ///     {
    ///         for (int i = 1; i &lt;= rings; i++) b.Band(color, 2f, i * gap);
    ///     }
    /// }
    /// </code>
    /// </summary>
    [System.Serializable]
    public abstract class VectraCustomEffect : VectraEffect
    {
        public virtual VectraEffectOrder Order => VectraEffectOrder.AboveStrokes;

        /// <summary>Path shapes bake a distance field when true (analytic shapes always have one).</summary>
        public virtual bool NeedsDistanceField => true;

        /// <summary>How far outside the outline the emitted layers reach (local units), for distance-field baking.</summary>
        public virtual float Reach => 0f;

        public abstract void Populate(VectraEffectBuilder builder);
    }

    /// <summary>Emits layers for a <see cref="VectraCustomEffect"/>. All sizes are in local units.</summary>
    public sealed class VectraEffectBuilder
    {
        readonly VectraShapeRenderer m_Renderer;
        internal bool analytic;

        internal VectraEffectBuilder(VectraShapeRenderer renderer) => m_Renderer = renderer;

        /// <summary>The rect the shape is built in.</summary>
        public Rect Rect => m_Renderer.BuilderFrame.rect;

        /// <summary>True for analytic shapes (exact distance); false for path shapes (baked distance field).</summary>
        public bool IsAnalytic => analytic;

        /// <summary>False for path shapes whose distance field could not be baked; layers are then skipped.</summary>
        public bool HasDistance => m_Renderer.BuilderHasDistance(analytic);

        /// <summary>The shape's interior painted with <paramref name="paint"/>, optionally blurred and offset.</summary>
        public void Fill(VectraPaint paint, float blur = 0f, Vector2 offset = default) => m_Renderer.BuilderFill(paint, blur, offset, analytic);

        public void Fill(Color color, float blur = 0f, Vector2 offset = default) => Fill(VectraPaint.Solid(color), blur, offset);

        /// <summary>A band of <paramref name="width"/> centred <paramref name="offset"/> units outside the outline (negative = inside).</summary>
        public void Band(VectraPaint paint, float width, float offset = 0f) => m_Renderer.BuilderBand(paint, width, offset, analytic);

        public void Band(Color color, float width, float offset = 0f) => Band(VectraPaint.Solid(color), width, offset);

        /// <summary>A soft copy of the silhouette (shadow); <paramref name="glow"/> uses the smoother glow falloff.</summary>
        public void Shadow(Color color, float blur, float spread = 0f, Vector2 offset = default, bool glow = false,
            LayerBlend blend = LayerBlend.Normal) => m_Renderer.BuilderShadow(color, blur, spread, offset, glow, blend, analytic);

        /// <summary>A shadow cast inwards from the outline.</summary>
        public void InnerShadow(Color color, Vector2 offset, float blur, float choke = 0f, LayerBlend blend = LayerBlend.Normal) =>
            m_Renderer.BuilderInnerShadow(color, offset, blur, choke, blend, analytic);
    }
}
