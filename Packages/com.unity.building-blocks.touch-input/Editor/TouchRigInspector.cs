using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// Inspector for <see cref="TouchRig"/>. It has three cards: Rig check, Show on, and Mappings.
    /// </summary>
    /// <remarks>
    /// Rig check lists <see cref="TouchRigDoctor"/>'s findings, each with a fix. Show on picks where and when the
    /// controls appear. Mappings (<see cref="TouchMappingsView"/>) shows which control drives each input action.
    ///
    /// Without the Input System package, the Mappings card lists the controls and the C# line that reads them.
    /// </remarks>
    [CustomEditor(typeof(TouchRig))]
    sealed class TouchRigInspector : UnityEditor.Editor
    {
        const long k_TickMs = 100;
        const int k_BackstopTicks = 50;
        const int k_RefreshEveryTicks = 5;

        TouchRig m_Rig;
        VisualElement m_Root;
        InspectorCard m_CheckCard;
        Label m_CheckSay;
        int m_ErrorsShown;
        Button m_FixAll;
        InspectorCard m_ShowCard;
        Label m_ShowSay;
        InspectorCard m_MappingsCard;
#if BB_TOUCH_HAS_INPUTSYSTEM
        TouchMappingsView m_Mappings;
#endif

        readonly List<TouchSetupCheck> m_Checks = new List<TouchSetupCheck>();
        List<TouchControl> m_Controls = new List<TouchControl>();
        string m_Key;
        int m_Tick;
        int m_SeenVersion = -1;

        public override VisualElement CreateInspectorGUI()
        {
            m_Root = new VisualElement();
            m_Root.AddToClassList("blocks-inspector");
            BuildingBlocksStyles.Apply(m_Root);

            m_Rig = target as TouchRig;
            if (m_Rig == null)
                return m_Root;

            m_CheckCard = new InspectorCard("check", "Rig check", defaultOpen: false).LeadingChevron()
                .SetGloss("What stops touches reaching your game.").Remember(target);
            m_FixAll = Buttons.Quiet("Fix all", FixAll);
            m_FixAll.tooltip = "Run every fix below that needs no choice.";
            m_CheckCard.AddAction(m_FixAll);
            m_CheckSay = new Label();
            m_CheckSay.AddToClassList("blocks-touch-sumbar");
            m_CheckCard.AddFooter(m_CheckSay);
            m_Root.Add(m_CheckCard);
            m_Root.Add(BuildShowCard());

            m_MappingsCard = new InspectorCard("mappings", "Mappings").LeadingChevron()
                .SetGloss("Which control drives each action the scene reads.").Remember(target);
            Button add = Buttons.Quiet("+ Control", null);
            add.clicked += () => ShowAddControlMenu(add);
            add.tooltip = "Add a joystick or a button to this rig.";
            m_MappingsCard.AddAction(add);
#if BB_TOUCH_HAS_INPUTSYSTEM
            m_Mappings = new TouchMappingsView(m_Rig, m_MappingsCard, () => Refresh(force: true));
            m_MappingsCard.Add(m_Mappings);
#else
            m_MappingsCard.Add(TouchInspectorKit.Hint("Mappings need the Input System package. Without it, gameplay reads each control from C#: rig.Get<TouchJoystick>(\"Move\")."));
#endif
            m_Root.Add(m_MappingsCard);

            Refresh(force: true);
            m_Root.schedule.Execute(Tick).Every(k_TickMs);
            Undo.undoRedoPerformed += OnUndoRedo;
            m_Root.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);
            return m_Root;
        }

        void OnUndoRedo()
        {
#if BB_TOUCH_HAS_INPUTSYSTEM
            m_Mappings?.Refresh(force: true);
#endif
            Refresh(force: true);
        }

        InspectorCard BuildShowCard()
        {
            m_ShowCard = new InspectorCard("show", "Show on").LeadingChevron()
                .SetGloss("Which platforms show these controls, and when.").Remember(target);
            SerializedProperty showOn = serializedObject.FindProperty("m_ShowOn");
            SerializedProperty showWhen = serializedObject.FindProperty("m_ShowWhen");
            m_ShowCard.Add(TouchInspectorKit.Field(showOn, "Platforms"));
            m_ShowCard.Add(TouchInspectorKit.Field(showWhen, "When"));
            m_ShowSay = TouchInspectorKit.Sub(string.Empty);
            m_ShowCard.Add(m_ShowSay);
            UpdateShowSay(); // and every tick: the fields and the When inputs both move
            return m_ShowCard;
        }

        /// <summary>Updates the card's summary and its "here" line. Runs every tick, since the inputs can change in Play.</summary>
        void UpdateShowSay()
        {
            if (m_Rig == null || m_ShowSay == null)
                return;

            TouchPlatforms here = TouchPlatform.Current;
            bool shown = m_Rig.IsShown;
            string platforms = ShowOnSummary(m_Rig.ShowOn);
            string when = m_Rig.ShowWhen switch
            {
                TouchShowWhen.TouchscreenPresent => "with a touchscreen",
                TouchShowWhen.LastInputWasTouch => "after a touch",
                _ => "always",
            };
            m_ShowCard.SetSummary($"{platforms} · {when}", warn: m_Rig.ShowOn == 0);

            string reason = (m_Rig.ShowOn & here) == 0 ? $"{Name(here)} is not ticked."
                : m_Rig.ShowWhen == TouchShowWhen.TouchscreenPresent && !shown ? "No touchscreen here."
                : m_Rig.ShowWhen == TouchShowWhen.LastInputWasTouch && !shown ? "The last input was not a touch."
                : string.Empty;
            string say = shown ? $"Shown here ({Name(here)})." : $"Hidden here ({Name(here)}). {reason}";
            if (m_ShowSay.text != say)
                m_ShowSay.text = say;
        }

        internal static string ShowOnSummary(TouchPlatforms showOn)
        {
            if (showOn == 0) return "nowhere";
            int count = 0, all = 0;
            foreach (TouchPlatforms p in (TouchPlatforms[])System.Enum.GetValues(typeof(TouchPlatforms)))
            {
                all++;
                if ((showOn & p) != 0) count++;
            }

            return count == all ? "all platforms" : count == 1 ? "1 platform" : $"{count} platforms";
        }

        static string Name(TouchPlatforms platform) => platform switch
        {
            TouchPlatforms.IOS => "iOS",
            TouchPlatforms.WebMobile => "Web, mobile browser",
            TouchPlatforms.WebDesktop => "Web, desktop browser",
            _ => platform.ToString(),
        };

        void ShowAddControlMenu(VisualElement anchor)
        {
            var menu = new GenericMenu();
            GameObject context = m_Rig.gameObject;
            menu.AddItem(new GUIContent("Joystick"), false, () => Spawned(TouchRigSpawner.CreateJoystick(context)));
            menu.AddItem(new GUIContent("Button"), false, () => Spawned(TouchRigSpawner.CreateButton(context)));
            menu.DropDown(anchor.worldBound);
        }

        void Spawned(GameObject created)
        {
#if BB_TOUCH_HAS_INPUTSYSTEM
            m_Mappings?.Refresh(force: true);
#endif
            Refresh(force: true);
            if (created != null)
                EditorGUIUtility.PingObject(created);
        }

        void FixAll()
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fix Touch Rig");

            // Each pass re-inspects: one fix (a bridge, a Touch scheme) can change the next finding.
            for (int pass = 0; pass < 8; pass++)
            {
                bool applied = false;
                foreach (TouchSetupCheck check in Problems(TouchRigDoctor.Inspect(m_Rig)))
                {
                    if (check.Fixes.Count != 1 || check.OffersActionPicker)
                        continue;

                    check.Fix();
                    applied = true;
                    break;
                }

                if (!applied)
                    break;
            }

            Undo.CollapseUndoOperations(group);
            Refresh(force: true);
        }

        void Tick()
        {
            if (m_Rig == null)
                return;

            m_Tick++;
            if (Application.isPlaying)
                UpdateLive();
            UpdateShowSay();

            if (ShouldRefresh())
                Refresh(force: false);
        }

        // At most twice a second, and only after a change. The doctor walks every control and binding.
        bool ShouldRefresh() =>
            m_Tick % k_RefreshEveryTicks == 0 && (TouchEditorChanges.Version != m_SeenVersion || m_Tick % k_BackstopTicks == 0);

        void Refresh(bool force)
        {
            if (m_Rig == null)
                return;

            m_SeenVersion = TouchEditorChanges.Version;
            m_Controls = TouchControlIds.ControlsOf(m_Rig);
            m_Checks.Clear();
            m_Checks.AddRange(Problems(TouchRigDoctor.Inspect(m_Rig)));

            var key = new StringBuilder();
            key.Append(Application.isPlaying);
            foreach (TouchSetupCheck check in m_Checks)
                key.Append('|').Append(check.Id).Append(check.Title);
            foreach (TouchControl control in m_Controls)
                key.Append('|').Append(control.GetInstanceIDSafe()).Append(control.ActionId).Append(IsMapped(control));

            string k = key.ToString();
            if (!force && k == m_Key)
                return;
            m_Key = k;

            RebuildCheck();
            UpdateCheckSay();
        }

        bool IsMapped(TouchControl control)
        {
#if BB_TOUCH_HAS_INPUTSYSTEM
            return m_Mappings != null && m_Mappings.IsMapped(control);
#else
            return false;
#endif
        }

        // The Mappings card writes each mapping into its action's own asset, so the doctor's
        // single-asset findings (drives nothing, no asset, several assets) do not apply here.
        static List<TouchSetupCheck> Problems(List<TouchSetupCheck> all) => all.FindAll(c =>
            c.Id != TouchRigDoctor.IdDrivesNothing && c.Id != TouchRigDoctor.IdNoAsset && c.Id != TouchRigDoctor.IdSeveralAssets);

        int UnmappedCount() => m_Controls.FindAll(c => !IsMapped(c)).Count;

        void RebuildCheck()
        {
            m_CheckCard.Clear();
            int fixable = 0;
            foreach (TouchSetupCheck check in m_Checks)
            {
                m_CheckCard.Add(new CheckRow(check, () => Refresh(force: true)));
                if (check.Fixes.Count == 1 && !check.OffersActionPicker)
                    fixable++;
            }

#if BB_TOUCH_HAS_INPUTSYSTEM
            int unmapped = UnmappedCount();
            if (unmapped > 0)
            {
                m_CheckCard.Add(new CheckRow(new TouchSetupCheck(TouchRigDoctor.IdDrivesNothing,
                    unmapped == 1 ? "1 control drives no action yet." : $"{unmapped} controls drive no action yet.",
                    "Fine if you read them in code. To map one, use Mappings.",
                    CheckSeverity.Info)));
            }
#endif

            if (m_Checks.Count == 0)
                m_CheckCard.Add(new CheckRow(new TouchSetupCheck("ok", "Nothing to fix.", null, CheckSeverity.Ok)));

            m_FixAll.style.display = fixable > 1 ? DisplayStyle.Flex : DisplayStyle.None;

            // A new error opens the card: a closed card with a red line under it is easy to miss.
            int errors = m_Checks.FindAll(c => c.Severity == CheckSeverity.Error).Count;
            if (errors > m_ErrorsShown)
                m_CheckCard.SetOpen(true);
            m_ErrorsShown = errors;
        }

        /// <summary>Updates the header count and the line under the card from the same findings as the rows.</summary>
        void UpdateCheckSay()
        {
            if (Application.isPlaying)
            {
                UpdateLive();
                return;
            }

            CheckSeverity worst = TouchRigDoctor.WorstSeverity(m_Checks);
            int total = m_Controls.Count;
            int unmapped = UnmappedCount();

            string say;
            if (m_Checks.Count > 0)
            {
                string first = m_Checks.Find(c => c.Severity == worst).Title;
                say = m_Checks.Count == 1 ? first : $"{first} And {m_Checks.Count - 1} more.";
                m_CheckCard.SetSummary(worst == CheckSeverity.Error ? $"{m_Checks.Count} to fix" : $"{m_Checks.Count} to check", warn: true);
            }
            else
            {
                string controls = total == 1 ? "1 control" : $"{total} controls";
                if (total == 0) say = "No controls yet. + Add mapping makes one.";
#if BB_TOUCH_HAS_INPUTSYSTEM
                else if (unmapped == 0) say = $"{controls}, all mapped.";
                else if (unmapped < total) say = $"{controls}. {total - unmapped} mapped, {unmapped} read in code.";
#endif
                else say = $"{controls}, read in code.";
                m_CheckCard.SetSummary("All good");
            }

            SetSay(say, m_Checks.Count == 0 ? null : worst == CheckSeverity.Error ? "error" : "warn");
        }

        void SetSay(string text, string tint)
        {
            if (m_CheckSay.text != text) m_CheckSay.text = text;
            m_CheckSay.EnableInClassList("blocks-touch-sumbar--warn", tint == "warn");
            m_CheckSay.EnableInClassList("blocks-touch-sumbar--error", tint == "error");
        }

        void UpdateLive()
        {
            int fingers = 0;
            foreach (TouchControl control in m_Controls)
            {
                if (control != null && control.IsPressed)
                    fingers++;
            }

            m_CheckCard.SetSummary("Playing");
            SetSay(fingers == 1 ? "1 finger down." : $"{fingers} fingers down.", null);
        }
    }

    static class TouchObjectExtensions
    {
        /// <summary>A per-object number for change keys that compiles on every supported editor.</summary>
        public static int GetInstanceIDSafe(this Object o) => o == null ? 0 : o.GetHashCode();
    }
}
