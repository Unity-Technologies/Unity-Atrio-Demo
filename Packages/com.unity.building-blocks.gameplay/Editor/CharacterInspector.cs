using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// The character's setup in one inspector: a status strip (setup problems, or live state in Play
    /// mode), then cards for the mover, every processor in execution order, and the command source.
    /// </summary>
    /// <remarks>
    /// Mover and command source cards only summarise and fold their own inspectors below; processors
    /// are data in this character's list, so their settings open here.
    /// </remarks>
    [CustomEditor(typeof(Character))]
    sealed class CharacterInspector : UnityEditor.Editor
    {
        const long k_TickMs = 100;
        const int k_DoctorEveryTicks = 10;
        const int k_PulseEveryTicks = 5;

        Character m_Host;
        GameObject m_Go;

        VisualElement m_Strip;
        VisualElement m_Dot;
        Label m_Title;
        Label m_Sub;
        Button m_LockButton;
        string m_StripState = string.Empty;
        int m_StripKey;

        InspectorCard m_SetupCard;
        InspectorCard m_MoverCard;
        InspectorCard m_ProcessorsCard;
        InspectorCard m_SourceCard;
        ProcessorListView m_List;
        VisualElement m_Root;
        Button m_FoldAll;

        readonly List<CharacterSetupCheck> m_Problems = new List<CharacterSetupCheck>();
        string m_ProblemsKey;
        int m_Tick;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            m_Root = root;
            root.AddToClassList("blocks-inspector");
            BuildingBlocksStyles.Apply(root);

            m_List?.Release();
            m_Host = target as Character;
            if (m_Host == null) return root;

            if (serializedObject.targetObjects.Length > 1)
            {
                root.Add(new Callout(
                    "MULTI-OBJECT EDITING",
                    "Select a single character to see its setup.",
                    CalloutTone.Warning));
                return root;
            }

            m_Go = m_Host.gameObject;

            root.Add(BuildStrip());
            root.Add(BuildSetupCard());
            root.Add(BuildMoverCard());
            root.Add(BuildProcessorsCard());
            root.Add(BuildSourceCard());

            var footnote = new Label(
                "Click a processor to tune it here. The mover's and the command source's settings live on " +
                "their own components below. Click their card to show or hide them.");
            footnote.AddToClassList("blocks-footnote");
            root.Add(footnote);

            RefreshSetup();
            RefreshSummaries();
            Tick();
            root.schedule.Execute(Tick).Every(k_TickMs);

            return root;
        }

        VisualElement BuildStrip()
        {
            m_Strip = new VisualElement();
            m_Strip.AddToClassList("blocks-transport");

            m_Dot = new VisualElement();
            m_Dot.AddToClassList("blocks-transport__dot");
            m_Strip.Add(m_Dot);

            var text = new VisualElement();
            text.AddToClassList("blocks-transport__text");
            m_Title = new Label();
            m_Title.AddToClassList("blocks-transport__title");
            m_Sub = new Label();
            m_Sub.AddToClassList("blocks-transport__sub");
            text.Add(m_Title);
            text.Add(m_Sub);
            m_Strip.Add(text);

            var buttons = new VisualElement();
            buttons.AddToClassList("blocks-transport__buttons");
            m_LockButton = Buttons.Secondary("Lock", ToggleLock);
            m_LockButton.AddToClassList("blocks-transport__button");
            m_LockButton.tooltip = "Take the player's control the way an interactable does, and give it back. Play mode only.";
            buttons.Add(m_LockButton);
            m_Strip.Add(buttons);

            return m_Strip;
        }

        void ToggleLock()
        {
            if (m_Host == null || !Application.isPlaying) return;
            if (m_Host.IsLocked) m_Host.Unlock();
            else m_Host.Lock();
        }

        void OnDisable()
        {
            // Unity disposes the serialized object after this and may not detach the UI first.
            m_List?.Release();
            m_Root?.SetEnabled(false);
            m_Root?.Unbind();
        }

        VisualElement BuildSetupCard()
        {
            m_SetupCard = new InspectorCard("setup", "Setup", defaultOpen: true).Remember(target);
            return m_SetupCard;
        }

        VisualElement BuildMoverCard()
        {
            m_MoverCard = new InspectorCard("mover", "Kinematic Mover").Remember(target)
                .Clickable("Click to show or hide the mover's settings below");
            m_MoverCard.HeaderClicked = () =>
            {
                var mover = m_Go.GetComponent<KinematicMover>();
                if (mover != null) InspectorFolds.Toggle(mover);
            };
            return m_MoverCard;
        }

        VisualElement BuildProcessorsCard()
        {
            m_ProcessorsCard = new InspectorCard("processors", "Move Processors").Remember(target);

            m_FoldAll = Buttons.Quiet("Show settings", ToggleAllFolds);
            m_FoldAll.tooltip = "Open or close every processor's settings.";
            m_ProcessorsCard.AddAction(m_FoldAll);

            m_List = new ProcessorListView(serializedObject.FindProperty("m_Processors"), m_Host, OnProcessorsChanged);
            m_ProcessorsCard.Add(m_List);
            return m_ProcessorsCard;
        }

        VisualElement BuildSourceCard()
        {
            m_SourceCard = new InspectorCard("source", "Command Source").Remember(target)
                .Clickable("Click to show or hide the source's settings below");
            m_SourceCard.HeaderClicked = () =>
            {
                Component source = FirstSource();
                if (source != null) InspectorFolds.Toggle(source);
            };
            return m_SourceCard;
        }

        void OnProcessorsChanged()
        {
            RefreshSetup();
            RefreshSummaries();
        }

        /// <summary>
        /// The setup doctor's findings, shown only when there are any.
        /// </summary>
        void RefreshSetup()
        {
            if (m_Go == null || m_SetupCard == null) return;

            m_Problems.Clear();
            var key = new System.Text.StringBuilder();
            foreach (CharacterSetupCheck check in CharacterSetupDoctor.Inspect(m_Go))
            {
                if (check.Severity == CheckSeverity.Ok) continue;
                m_Problems.Add(check);
                key.Append(check.Severity).Append('|').Append(check.Title).Append(';');
            }

            if (key.ToString() == m_ProblemsKey && m_SetupCard.childCount > 0 == m_Problems.Count > 0)
                return;
            m_ProblemsKey = key.ToString();

            m_SetupCard.Clear();
            m_SetupCard.style.display = m_Problems.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (m_Problems.Count == 0) return;

            CheckSeverity worst = CharacterSetupDoctor.WorstSeverity(m_Problems);
            m_SetupCard.SetSummary(worst == CheckSeverity.Error
                ? $"{m_Problems.Count} to fix"
                : $"{m_Problems.Count} to know about", warn: true);

            foreach (CharacterSetupCheck check in m_Problems)
                m_SetupCard.Add(new CheckRow(check, OnProcessorsChanged));
        }

        void RefreshSummaries()
        {
            if (m_Go == null) return;

            var mover = m_Go.GetComponent<KinematicMover>();
            if (mover != null)
            {
                m_MoverCard.style.display = DisplayStyle.Flex;
                m_MoverCard.SetSummary(
                    $"capsule {mover.Height:0.##} × {mover.Radius:0.##} m · slope {mover.SlopeLimit:0}° at {mover.SpeedAtSlopeLimit * 100f:0}% speed · step {mover.StepOffset:0.##} m");
            }
            else
            {
                m_MoverCard.style.display = DisplayStyle.None;
            }

            int total = m_List.Count;
            int active = m_List.ActiveCount;

            if (total == 0)
                m_ProcessorsCard.SetSummary("none: this character will not fall", warn: true);
            else if (active == total)
                m_ProcessorsCard.SetSummary($"{total}, in execution order");
            else
                m_ProcessorsCard.SetSummary($"{active} of {total} on, in execution order", warn: active == 0);

            Component source = FirstSource();
            int sources = CountSources();
            if (source == null)
            {
                m_SourceCard.style.display = DisplayStyle.None;
            }
            else
            {
                m_SourceCard.style.display = DisplayStyle.Flex;
                m_SourceCard.SetSummary(sources > 1
                    ? $"{ObjectNames.NicifyVariableName(source.GetType().Name)} · {sources} present, the first one drives"
                    : $"{ObjectNames.NicifyVariableName(source.GetType().Name)} drives the character");
            }

            UpdateFoldAll();
        }

        Component FirstSource()
        {
            var sources = new List<IMoveCommandSource>();
            m_Go.GetComponents(sources);
            return sources.Count > 0 ? sources[0] as Component : null;
        }

        int CountSources()
        {
            var sources = new List<IMoveCommandSource>();
            m_Go.GetComponents(sources);
            return sources.Count;
        }

        void Tick()
        {
            if (m_Host == null || m_Go == null) return;
            m_Tick++;

            bool playing = Application.isPlaying;
            m_LockButton.SetEnabled(playing);
            m_LockButton.text = playing && m_Host.IsLocked ? "Unlock" : "Lock";

            if (playing && m_StripState == "live" && m_Tick % k_PulseEveryTicks == 0)
                m_Dot.ToggleInClassList("blocks-pulse");

            if (!playing && m_Tick % k_DoctorEveryTicks == 0)
            {
                RefreshSetup();
                UpdateFoldAll();
            }

            // Rebuild the strip text only when the folded state key changes.
            int key = StripKey(playing);
            if (key == m_StripKey && m_StripState.Length > 0) return;
            m_StripKey = key;

            string state;
            string title;
            string sub;

            if (playing)
            {
                KinematicMover mover = m_Host.Mover;
                if (m_Host.IsLocked)
                {
                    state = "blocked";
                    title = "Locked";
                    sub = "Control is held by an interactable or by the button here. No input, no simulation.";
                }
                else if (mover == null)
                {
                    state = "blocked";
                    title = "No mover";
                    sub = "Nothing to simulate.";
                }
                else
                {
                    MoveState s = mover.GetState();
                    Vector3 v = s.Velocity;
                    float horizontal = new Vector3(v.x, 0f, v.z).magnitude;
                    state = "live";

                    if (s.MovementMode >= MoveState.ModeCustomStart)
                    {
                        title = $"Custom mode {s.MovementMode}";
                        sub = $"{horizontal:0.0} m/s";
                    }
                    else if (s.IsGrounded)
                    {
                        title = horizontal < 0.05f ? "Standing" : "Moving";
                        sub = horizontal < 0.05f ? "Grounded, not moving." : $"{horizontal:0.0} m/s along the ground";
                    }
                    else
                    {
                        title = v.y > 0.05f ? "Rising" : "Falling";
                        sub = $"{horizontal:0.0} m/s across · {Mathf.Abs(v.y):0.0} m/s {(v.y > 0.05f ? "up" : "down")} · off the ground {s.TimeSinceGrounded:0.0} s";
                    }
                }
            }
            else if (m_Problems.Count > 0)
            {
                state = "blocked";
                CheckSeverity worst = CharacterSetupDoctor.WorstSeverity(m_Problems);
                title = worst == CheckSeverity.Error ? "Needs setup" : "Worth a look";
                sub = m_Problems[0].Title + (m_Problems.Count > 1 ? $" · {m_Problems.Count - 1} more below" : string.Empty);
            }
            else
            {
                state = "ready";
                title = "Ready";
                int processors = m_List != null ? m_List.Count : 0;
                Component source = FirstSource();
                sub = $"{processors} {(processors == 1 ? "processor" : "processors")} · " +
                      (source != null ? $"{ObjectNames.NicifyVariableName(source.GetType().Name)} drives it" : "nothing drives it yet");
            }

            if (state != m_StripState)
            {
                m_Strip.RemoveFromClassList("blocks-transport--ready");
                m_Strip.RemoveFromClassList("blocks-transport--live");
                m_Strip.RemoveFromClassList("blocks-transport--blocked");
                if (state.Length > 0) m_Strip.AddToClassList("blocks-transport--" + state);
                if (state != "live") m_Dot.RemoveFromClassList("blocks-pulse");
                m_StripState = state;
            }

            if (m_Title.text != title) m_Title.text = title;
            if (m_Sub.text != sub) m_Sub.text = sub;
        }

        /// <summary>
        /// Everything the strip's text depends on, folded into one value.
        /// </summary>
        int StripKey(bool playing)
        {
            int key = playing ? 1 : 2;
            if (playing)
            {
                KinematicMover mover = m_Host.Mover;
                key = unchecked(key * 31 + (m_Host.IsLocked ? 1 : 0));
                key = unchecked(key * 31 + (mover == null ? 1 : 0));
                if (mover != null && !m_Host.IsLocked)
                {
                    MoveState s = mover.GetState();
                    Vector3 v = s.Velocity;
                    key = unchecked(key * 31 + s.MovementMode);
                    key = unchecked(key * 31 + (s.IsGrounded ? 1 : 0));
                    key = unchecked(key * 31 + Mathf.RoundToInt(new Vector3(v.x, 0f, v.z).magnitude * 10f));
                    key = unchecked(key * 31 + Mathf.RoundToInt(v.y * 10f));
                    key = unchecked(key * 31 + (s.IsGrounded ? 0 : Mathf.RoundToInt(s.TimeSinceGrounded * 10f)));
                }

                return key;
            }

            key = unchecked(key * 31 + m_Problems.Count);
            if (m_Problems.Count > 0)
            {
                key = unchecked(key * 31 + (int)CharacterSetupDoctor.WorstSeverity(m_Problems));
                key = unchecked(key * 31 + (m_Problems[0].Title != null ? m_Problems[0].Title.GetHashCode() : 0));
                return key;
            }

            key = unchecked(key * 31 + (m_List != null ? m_List.Count : 0));
            Component source = FirstSource();
            key = unchecked(key * 31 + (source != null ? source.GetType().GetHashCode() : 0));
            return key;
        }

        void UpdateFoldAll()
        {
            if (m_FoldAll == null || m_List == null) return;

            m_FoldAll.style.display = m_List.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            m_FoldAll.text = m_List.AnyClosed ? "Show settings" : "Hide settings";
        }

        void ToggleAllFolds()
        {
            m_List.SetAllOpen(m_List.AnyClosed);
            UpdateFoldAll();
        }
    }
}
