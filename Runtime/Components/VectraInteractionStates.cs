using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Vectra
{
    public enum VectraInteractionState
    {
        Normal,
        Hover,
        Pressed,
        Selected,
        Disabled
    }

    /// <summary>Appearance of one interaction state. Toggles decide which properties the state changes.</summary>
    [Serializable]
    public class VectraStateLook
    {
        [Tooltip("Multiplies the shape colour (Graphic tint).")]
        [ColorUsage(true, true)] public Color tint = Color.white;
        [Min(0f)] public float scale = 1f;
        public bool overrideFill;
        [ColorUsage(true, true)] public Color fill = Color.white;
        public bool overrideStroke;
        [ColorUsage(true, true)] public Color stroke = Color.white;
        [Tooltip("Multiplies the alpha of shadows and glows.")]
        [Min(0f)] public float effectStrength = 1f;

        public VectraStateLook Clone() => (VectraStateLook)MemberwiseClone();
    }

    /// <summary>
    /// Hover, press, focus (selection) and disabled looks for a <see cref="VectraShape"/> with editable transitions.
    /// Works on its own (pointer and selection events) or next to a Selectable, whose interactable flag drives the
    /// Disabled state. Fill and stroke overrides change the first fill and stroke layer of the local appearance
    /// (shapes that use a shared style only get tint, scale and effect strength).
    /// </summary>
    // Runs in play mode only: it modifies colours and scale, which must not leak into saved scenes.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VectraShape))]
    [AddComponentMenu("Vectra/Vectra Interaction States")]
    public class VectraInteractionStates : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
        IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public VectraStateLook normal = new VectraStateLook();
        public VectraStateLook hover = new VectraStateLook { tint = new Color(1.08f, 1.08f, 1.08f, 1f), scale = 1.03f };
        public VectraStateLook pressed = new VectraStateLook { tint = new Color(0.85f, 0.85f, 0.85f, 1f), scale = 0.97f };
        public VectraStateLook selected = new VectraStateLook { overrideStroke = true, stroke = new Color(0.35f, 0.6f, 1f), effectStrength = 1.4f };
        public VectraStateLook disabled = new VectraStateLook { tint = new Color(0.6f, 0.6f, 0.6f, 0.5f) };

        [Tooltip("Transition duration in seconds.")]
        [Min(0f)] public float duration = 0.12f;
        public VectraEase ease = VectraEase.OutCubic;
        [Tooltip("Use unscaled time (transitions keep running while the game is paused).")]
        public bool unscaledTime = true;

        VectraShape m_Shape;
        Selectable m_Selectable;
        bool m_Hover, m_Pressed, m_Selected;
        VectraInteractionState m_State;
        VectraStateLook m_From, m_Current, m_To;
        float m_T = 1f;
        Color m_BaseColor;
        Vector3 m_BaseScale;
        Color[] m_BaseEffectAlpha = Array.Empty<Color>();
        bool m_Captured;

        public VectraInteractionState State => m_State;

        /// <summary>Raised when the state changes (old, new).</summary>
        public event Action<VectraInteractionState, VectraInteractionState> StateChanged;

        VectraStateLook Look(VectraInteractionState s) => s switch
        {
            VectraInteractionState.Hover => hover,
            VectraInteractionState.Pressed => pressed,
            VectraInteractionState.Selected => selected,
            VectraInteractionState.Disabled => disabled,
            _ => normal
        };

        void OnEnable()
        {
            m_Shape = GetComponent<VectraShape>();
            TryGetComponent(out m_Selectable);
            Capture();
            m_State = Evaluate();
            m_Current = Look(m_State).Clone();
            m_To = Look(m_State);
            m_T = 1f;
            Apply(m_Current);
        }

        void OnDisable()
        {
            if (m_Captured && m_Shape != null) Apply(normal);
        }

        /// <summary>Remembers the shape's current colour, scale and effect colours as the base the looks modify.</summary>
        public void Capture()
        {
            if (m_Shape == null) m_Shape = GetComponent<VectraShape>();
            m_BaseColor = m_Shape.color;
            m_BaseScale = transform.localScale;
            var fx = m_Shape.Content.ActiveEffects;
            m_BaseEffectAlpha = new Color[fx.Count];
            for (int i = 0; i < fx.Count; i++) m_BaseEffectAlpha[i] = EffectColor(fx[i]) ?? Color.clear;
            if (normal.overrideFill == false && m_Shape.Fills.Count > 0 && m_Shape.Fills[0]?.paint != null) normal.fill = m_Shape.Fills[0].paint.color;
            if (normal.overrideStroke == false && m_Shape.Strokes.Count > 0 && m_Shape.Strokes[0]?.paint != null) normal.stroke = m_Shape.Strokes[0].paint.color;
            m_Captured = true;
        }

        VectraInteractionState Evaluate()
        {
            if (m_Selectable != null && !m_Selectable.IsInteractable()) return VectraInteractionState.Disabled;
            if (m_Pressed) return VectraInteractionState.Pressed;
            if (m_Hover) return VectraInteractionState.Hover;
            if (m_Selected) return VectraInteractionState.Selected;
            return VectraInteractionState.Normal;
        }

        /// <summary>Forces a state (for previews or custom input); pointer events keep updating it afterwards.</summary>
        public void SetState(VectraInteractionState state, bool instant = false)
        {
            var old = m_State;
            m_State = state;
            m_From = m_Current?.Clone() ?? Look(state).Clone();
            m_To = Look(state);
            m_T = instant || duration <= 0f ? 1f : 0f;
            if (m_T >= 1f)
            {
                m_Current = m_To.Clone();
                Apply(m_Current);
            }

            if (old != state) StateChanged?.Invoke(old, state);
        }

        void Refresh()
        {
            var s = Evaluate();
            if (s != m_State) SetState(s);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            m_Hover = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData e)
        {
            m_Hover = false;
            m_Pressed = false;
            Refresh();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            m_Pressed = true;
            Refresh();
        }

        public void OnPointerUp(PointerEventData e)
        {
            m_Pressed = false;
            Refresh();
        }

        public void OnSelect(BaseEventData e)
        {
            m_Selected = true;
            Refresh();
        }

        public void OnDeselect(BaseEventData e)
        {
            m_Selected = false;
            Refresh();
        }

        void Update()
        {
            if (m_Selectable != null) Refresh();
            if (m_T >= 1f || m_From == null) return;
            m_T = Mathf.Min(1f, m_T + (unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime) / Mathf.Max(duration, 1e-4f));
            float k = VectraTween.Evaluate(ease, m_T);
            m_Current ??= new VectraStateLook();
            m_Current.tint = Color.LerpUnclamped(m_From.tint, m_To.tint, k);
            m_Current.scale = Mathf.LerpUnclamped(m_From.scale, m_To.scale, k);
            m_Current.fill = Color.LerpUnclamped(m_From.overrideFill ? m_From.fill : normal.fill, m_To.overrideFill ? m_To.fill : normal.fill, k);
            m_Current.stroke = Color.LerpUnclamped(m_From.overrideStroke ? m_From.stroke : normal.stroke, m_To.overrideStroke ? m_To.stroke : normal.stroke, k);
            m_Current.overrideFill = m_From.overrideFill || m_To.overrideFill;
            m_Current.overrideStroke = m_From.overrideStroke || m_To.overrideStroke;
            m_Current.effectStrength = Mathf.LerpUnclamped(m_From.effectStrength, m_To.effectStrength, k);
            Apply(m_Current);
        }

        void Apply(VectraStateLook look)
        {
            if (m_Shape == null || look == null || !m_Captured) return;
            m_Shape.color = m_BaseColor * look.tint;
            transform.localScale = m_BaseScale * look.scale;
            var c = m_Shape.Content;
            bool local = c.style == null;
            if (local && look.overrideFill && c.fills.Count > 0 && c.fills[0]?.paint != null) c.fills[0].paint.color = look.fill;
            if (local && look.overrideStroke && c.strokes.Count > 0 && c.strokes[0]?.paint != null) c.strokes[0].paint.color = look.stroke;
            var fx = c.ActiveEffects;
            for (int i = 0; i < fx.Count && i < m_BaseEffectAlpha.Length && local; i++)
            {
                var b = m_BaseEffectAlpha[i];
                SetEffectColor(fx[i], new Color(b.r, b.g, b.b, b.a * look.effectStrength));
            }

            m_Shape.SetShapeDirty();
        }

        static Color? EffectColor(VectraEffect e) => e switch
        {
            DropShadowEffect d => d.color,
            OuterGlowEffect g => g.color,
            InnerShadowEffect s => s.color,
            InnerGlowEffect ig => ig.color,
            OutlineEffect o => o.color,
            _ => null
        };

        static void SetEffectColor(VectraEffect e, Color c)
        {
            switch (e)
            {
                case DropShadowEffect d: d.color = c; break;
                case OuterGlowEffect g: g.color = c; break;
                case InnerShadowEffect s: s.color = c; break;
                case InnerGlowEffect ig: ig.color = c; break;
                case OutlineEffect o: o.color = c; break;
            }
        }
    }
}
