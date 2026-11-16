using UnityEngine;
using UnityEngine.UI;

namespace NTC.Editor
{
    /// <summary>
    /// ON/OFF switch animation (color fade + knob slide) for the debug panel, ported from Fantasy Kingdom's
    /// ButtonAnimation. A plain class rather than a MonoBehaviour because editor-assembly components can't be
    /// attached to GameObjects; <see cref="NTCDebugPanel"/> ticks it.
    /// </summary>
    sealed class NTCToggleSwitch
    {
        const float k_KnobOnX = 18f;
        const float k_KnobOffX = -18f;
        const float k_AnimDuration = 0.25f;
        static readonly Color k_OnColor = new Color(0.392f, 0.847f, 0.322f, 1f);
        static readonly Color k_OffColor = new Color(0.5725f, 0.5725f, 0.5725f, 1f);

        readonly Image m_Background;
        readonly Graphic m_OnText;
        readonly Graphic m_OffText;
        readonly RectTransform m_Knob;

        // 0 = fully OFF, 1 = fully ON.
        float m_OnAmount;
        float m_Target;

        public NTCToggleSwitch(Image background, Graphic onText, Graphic offText, RectTransform knob)
        {
            m_Background = background;
            m_OnText = onText;
            m_OffText = offText;
            m_Knob = knob;
        }

        public void SetImmediate(bool on)
        {
            m_Target = m_OnAmount = on ? 1f : 0f;
            Apply();
        }

        public void Animate(bool on) => m_Target = on ? 1f : 0f;

        public void Tick(float deltaTime)
        {
            if (m_OnAmount == m_Target)
                return;
            m_OnAmount = Mathf.MoveTowards(m_OnAmount, m_Target, deltaTime / k_AnimDuration);
            Apply();
        }

        void Apply()
        {
            m_Background.color = Color.Lerp(k_OffColor, k_OnColor, m_OnAmount);
            SetAlpha(m_OnText, m_OnAmount);
            SetAlpha(m_OffText, 1f - m_OnAmount);
            Vector2 knobPos = m_Knob.anchoredPosition;
            knobPos.x = Mathf.Lerp(k_KnobOffX, k_KnobOnX, m_OnAmount);
            m_Knob.anchoredPosition = knobPos;
        }

        static void SetAlpha(Graphic graphic, float alpha)
        {
            Color c = graphic.color;
            c.a = alpha;
            graphic.color = c;
        }
    }
}
