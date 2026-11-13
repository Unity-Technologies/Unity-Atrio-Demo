using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Inspector for <see cref="AnimatorDriver"/>: a strip with a live line of what is written
    /// (speed, grounded, last jump or landing), then the fields. In Edit mode the strip says whether
    /// an Animator was found.
    /// </summary>
    [CustomEditor(typeof(AnimatorDriver)), CanEditMultipleObjects]
    sealed class AnimatorDriverInspector : UnityEditor.Editor
    {
        const long k_TickMs = 100;
        const int k_FlashTicks = 6;

        Label m_Live;
        int m_JumpFlash;
        int m_LandFlash;
        bool m_WasGrounded;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.AddToClassList("blocks-inspector");
            BuildingBlocksStyles.Apply(root);

            if (targets.Length == 1)
            {
                var strip = new VisualElement();
                strip.AddToClassList("blocks-effect-strip");

                var eyebrow = new Label("ANIMATOR DRIVER");
                eyebrow.AddToClassList("blocks-effect-strip__eyebrow");
                strip.Add(eyebrow);

                m_Live = new Label();
                m_Live.AddToClassList("blocks-effect-strip__neighbors");
                strip.Add(m_Live);

                root.Add(strip);

                Tick();
                root.schedule.Execute(Tick).Every(k_TickMs);
            }

            var fields = new VisualElement();
            InspectorElement.FillDefaultInspector(fields, serializedObject, this);
            root.Add(fields);

            var hint = new Label(
                "Each field names a parameter on your Animator Controller. Leave one blank to skip it. A name " +
                "the controller does not have is skipped and reported once in the console.");
            hint.AddToClassList("blocks-panel__hint");
            root.Add(hint);

            root.Bind(serializedObject);
            return root;
        }

        void Tick()
        {
            var driver = target as AnimatorDriver;
            if (driver == null || m_Live == null) return;

            Animator animator = driver.Animator != null ? driver.Animator : driver.GetComponentInChildren<Animator>();

            if (!Application.isPlaying)
            {
                if (animator == null)
                    m_Live.text = "No Animator under this character yet. Add one to a child, or assign it above.";
                else if (animator.runtimeAnimatorController == null)
                    m_Live.text = $"'{animator.name}' has no Animator Controller, so there is nothing to write to yet.";
                else
                    m_Live.text = $"Writes to '{animator.runtimeAnimatorController.name}' on '{animator.name}'. Enter Play mode to see the values.";

                return;
            }

            AnimatorDriver.Sample sample = driver.LastSample;

            if (sample.Jumped) m_JumpFlash = k_FlashTicks;
            if (sample.Landed) m_LandFlash = k_FlashTicks;
            if (m_WasGrounded && !sample.IsGrounded && sample.VerticalSpeed > 0f) m_JumpFlash = k_FlashTicks;
            if (!m_WasGrounded && sample.IsGrounded) m_LandFlash = k_FlashTicks;
            m_WasGrounded = sample.IsGrounded;

            string ground = sample.IsGrounded ? "on the ground" : sample.VerticalSpeed > 0f ? "rising" : "falling";
            string flash = m_JumpFlash > 0 ? " · jumped" : m_LandFlash > 0 ? " · landed" : string.Empty;
            if (m_JumpFlash > 0) m_JumpFlash--;
            if (m_LandFlash > 0) m_LandFlash--;

            m_Live.text = $"{sample.Speed:0.0} m/s · {ground}{flash}";
        }
    }
}
