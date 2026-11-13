using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Inspector for <see cref="Interactable"/>: a Test card (<see cref="InteractableTestRun"/>), then
    /// Starts, Steps ("When it plays" / "If stopped early", see <see cref="StepListView"/>) and Repeats
    /// cards, each with a live summary.
    /// </summary>
    /// <remarks>
    /// Steps are serialized data on the interactable, so their settings open under their row.
    /// </remarks>
    [CustomEditor(typeof(Interactable))]
    sealed class InteractableInspector : UnityEditor.Editor
    {
        const long k_TickMs = 66;
        // How often the player check reruns, to catch a character added, removed or retagged.
        const long k_PlayerCheckMs = 3000;
        const string k_TestGloss = "Test this interactable in Play mode.";

        /// <summary>Tooltip on the "If stopped early" title.</summary>
        public const string StoppedEarlyHelp =
            "Runs when a run is cut short: it leaves the volume (Starts ▸ If it leaves: Stop), Stop() is called, or it is switched off.";

        /// <summary>Shown in the Steps card when a Move Player To or Look At has no Lock Player before it.</summary>
        public const string NoLockWarning =
            "Move Player To and Look At move the player, but no Lock Player step comes before them. The player " +
            "keeps control and the movement will fight it. Add a Lock Player step above.";

        /// <summary>Starts on: what starts it. Volume stands for both volume modes; Plays picks between them.</summary>
        enum StartsOn { Volume, Anywhere, Enable, Manual }

        static readonly (StartsOn mode, string label, string tooltip)[] k_StartsOn =
        {
            (StartsOn.Volume, "Volume", "A Trigger Volume starts it: something it detects walks in, or presses the button while inside."),
            (StartsOn.Anywhere, "Anywhere", "A button starts it, wherever the player is. For something they carry: a torch, a radio."),
            (StartsOn.Enable, "On enable", "It plays the moment this component turns on: at scene start, or when a UnityEvent switches it on."),
            (StartsOn.Manual, "Manual", "Nothing starts it on its own. Your script, a UI button or a UnityEvent calls Play()."),
        };

        static readonly (InteractablePlayOn mode, string label, string tooltip)[] k_Plays =
        {
            (InteractablePlayOn.EnterZone, "On enter", "Plays the moment something the volume detects walks in."),
            (InteractablePlayOn.Interact, "On press", "Plays when the button is pressed while something the volume detects is inside."),
        };

        static readonly (bool look, string label, string tooltip)[] k_Look =
        {
            (false, "Not needed", "The button works wherever the player looks."),
            (true, "Must look at it", "The button works only while the camera looks toward the Look point."),
        };

        static readonly (bool stop, string label, string tooltip)[] k_Leaves =
        {
            (false, "Keep playing", "Walking out mid-run does nothing; the run finishes."),
            (true, "Stop", "Walking out mid-run cuts the run short, and the \"If stopped early\" steps run."),
        };

        /// <summary>Repeats › Plays: every time, or once. Every time is <see cref="InteractableAfterFinish.Nothing"/>.</summary>
        static readonly (bool once, string label, string tooltip)[] k_Repeats =
        {
            (false, "Every time", "It can play again after each run, once the wait between runs is over."),
            (true, "Once", "After one full run it takes itself out of play. A run cut short does not count."),
        };

        /// <summary>Repeats › Then: what a Once interactable does to itself after its full run.</summary>
        static readonly (InteractableAfterFinish mode, string label, string tooltip)[] k_Then =
        {
            (InteractableAfterFinish.DisableComponent, "Turn off", "Switches this component off. Tick it again to re-arm."),
            (InteractableAfterFinish.DeactivateObject, "Hide object", "Deactivates the GameObject it sits on, steps and all."),
            (InteractableAfterFinish.DestroyObject, "Destroy object", "Removes the GameObject for good. For an intro that can never play again."),
        };

        Interactable m_Interactable;
        VisualElement m_Root;

        InspectorCard m_TestCard;
        Button m_TestButton;
        bool m_TestShowsStop;

        InspectorCard m_StartsCard;
        InspectorCard m_StepsCard;
        InspectorCard m_RepeatsCard;

        SegmentedControl m_StartsOnField;
        SegmentedControl m_PlaysField;
        VisualElement m_PressNoteRow;
        Label m_PressNote;
        SegmentedControl m_LeavesField;
        SegmentedControl m_LookField;
        Fold m_LookFold;
        Fold m_LookDetails;
        Fold m_PromptFold;
        Label m_PromptNote;
        Fold m_ReachFold;
        Fold m_VolumeFold;
        Fold m_AnywhereFold;
        Fold m_VolumeDetails;
        Fold m_ButtonFold;
        Fold m_InlineKeysFold;
        VisualElement m_NoVolumeWarn;
        Label m_NoVolumeText;
        Button m_AddVolume;
        Label m_Detects;
        Button m_DetectsLink;
        Button m_ButtonMode;
        ObjectField m_ButtonReference;
        Label m_ButtonKeys;
        Label m_StartsSay;
        VisualElement m_NoLock;
        Label m_StepsSay;
        StepListView m_Steps;
        StepListView m_StoppedSteps;
        SegmentedControl m_RepeatsField;
        Fold m_EveryTimeFold;
        FloatField m_WaitField;
        Fold m_OnceFold;
        SegmentedControl m_ThenField;
        Label m_RepeatsSay;

        SerializedProperty m_PlayOn;
        SerializedProperty m_Zone;
        SerializedProperty m_Button;
        SerializedProperty m_ButtonUseReference;
        SerializedProperty m_StopWhenPlayerLeaves;
        SerializedProperty m_RequireLook;
        SerializedProperty m_LookPoint;
        SerializedProperty m_MaxLookAngle;
        SerializedProperty m_LookMargin;
        SerializedProperty m_MaxLookDistance;
        SerializedProperty m_Cooldown;
        SerializedProperty m_AfterFinish;
        SerializedProperty m_Groups;
        SerializedProperty m_OnInterruptedGroups;

        bool m_ZoneDetectsPlayer = true;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            m_Root = root;
            root.AddToClassList("blocks-inspector");
            BuildingBlocksStyles.Apply(root);

            if (targets.Length > 1)
            {
                root.Add(new Callout("MULTI-OBJECT EDITING", "Select a single interactable to edit it.", CalloutTone.Warning));
                return root;
            }

            m_Interactable = (Interactable)target;
            ReleaseLists();
            FindProperties();

            root.Add(BuildTestCard());
            root.Add(BuildStartsCard());
            root.Add(BuildStepsCard());
            root.Add(BuildRepeatsCard());

            root.Bind(serializedObject);
            root.TrackSerializedObjectValue(serializedObject, _ => OnSerializedChanged());

            SyncStarts();
            SyncRepeats();
            RefreshSummaries();
            Tick();
            root.schedule.Execute(Tick).Every(k_TickMs);
            root.schedule.Execute(RecheckPlayer).Every(k_PlayerCheckMs);

            return root;
        }

        // Unity can destroy the editor (play mode, selection, recompile) without detaching its UI, and can
        // build the UI more than once, so scene listeners follow the editor, not the UI.
        void OnEnable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.undoRedoPerformed -= OnHierarchyChanged;
            Undo.undoRedoPerformed += OnHierarchyChanged;
        }

        void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.undoRedoPerformed -= OnHierarchyChanged;
            ReleaseLists();
            // The serialized object is disposed after this. Grey the UI out and unbind it; the null check
            // stops the scheduled refreshes and every control and menu item opened before.
            m_Root?.SetEnabled(false);
            m_Root?.Unbind();
            m_Interactable = null;
        }

        void ReleaseLists()
        {
            m_Steps?.Release();
            m_StoppedSteps?.Release();
        }

        void FindProperties()
        {
            m_PlayOn = serializedObject.FindProperty("m_PlayOn");
            m_Zone = serializedObject.FindProperty("m_Zone");
            m_Button = serializedObject.FindProperty("m_Button");
            m_ButtonUseReference = m_Button.FindPropertyRelative("m_UseReference");
            m_StopWhenPlayerLeaves = serializedObject.FindProperty("m_StopWhenPlayerLeaves");
            m_RequireLook = serializedObject.FindProperty("m_RequireLook");
            m_LookPoint = serializedObject.FindProperty("m_LookPoint");
            m_MaxLookAngle = serializedObject.FindProperty("m_MaxLookAngle");
            m_LookMargin = serializedObject.FindProperty("m_LookMargin");
            m_MaxLookDistance = serializedObject.FindProperty("m_MaxLookDistance");
            m_Cooldown = serializedObject.FindProperty("m_Cooldown");
            m_AfterFinish = serializedObject.FindProperty("m_AfterFinish");
            m_Groups = serializedObject.FindProperty("m_Groups");
            m_OnInterruptedGroups = serializedObject.FindProperty("m_OnInterruptedGroups");
        }

        void OnSerializedChanged()
        {
            if (m_Interactable == null) return;
            SyncStarts();
            SyncRepeats();
            RefreshSummaries();
        }

        void OnHierarchyChanged()
        {
            if (m_Interactable == null) return;
            // The Repeats bar names the object; Starts names the Input Prompt that presses it.
            SyncRepeats();
            SyncStarts();
        }

        VisualElement BuildTestCard()
        {
            m_TestCard = new InspectorCard("test", "Test") { Collapsible = false };
            m_TestCard.AddToClassList("blocks-test");
            m_TestCard.SetGloss(k_TestGloss);

            m_TestButton = Buttons.Primary("Play", OnTestClicked);
            m_TestButton.AddToClassList("blocks-test__button");
            SetIcon(m_TestButton, "PlayButton");
            m_TestCard.AddAction(m_TestButton);

            return m_TestCard;
        }

        void OnTestClicked()
        {
            if (m_Interactable == null) return;
            if (Application.isPlaying && m_Interactable.IsPlaying)
                m_Interactable.Stop();
            else
                InteractableTestRun.Request(m_Interactable);
        }

        static void SetIcon(Button button, string icon)
        {
            Texture2D texture = EditorIcons.Find(icon);
            if (texture != null) button.iconImage = Background.FromTexture2D(texture);
        }

        VisualElement BuildStartsCard()
        {
            // One question per row, rows only once they apply, and a summary bar that stays visible closed.
            m_StartsCard = new InspectorCard("starts", "Starts").LeadingChevron().SetGloss("What makes it play.").Remember(target);
            m_StartsCard.AddToClassList("blocks-starts");

            m_StartsOnField = CardRows.Segmented(k_StartsOn, m_PlayOn.tooltip);
            m_StartsOnField.RegisterValueChangedCallback(evt => SetStartsOn(evt.newValue));
            m_StartsCard.Add(CardRows.Row("Starts on", m_StartsOnField));

            // Button: only the Button rows, moved here from under Plays by SyncStarts.
            m_AnywhereFold = new Fold();
            m_StartsCard.Add(m_AnywhereFold);

            // Manual with an Input Prompt pressing it: the prompt owns the button, this still owns Looking.
            m_PromptFold = new Fold();
            m_PromptNote = new Label { enableRichText = true };
            m_PromptNote.AddToClassList("blocks-tv-sub");
            m_PromptNote.AddToClassList("blocks-starts__note");
            m_PromptFold.Add(CardRows.Row(string.Empty, m_PromptNote));
            m_StartsCard.Add(m_PromptFold);
            BuildLookRows();

            m_VolumeFold = new Fold();
            var volume = new ObjectField { objectType = typeof(TriggerVolume), allowSceneObjects = true, tooltip = m_Zone.tooltip };
            volume.BindProperty(m_Zone);
            m_AddVolume = Buttons.Quiet("+ Add", () =>
            {
                if (m_Interactable == null) return;
                Interactable interactable = m_Interactable;
                BlockSpawner.AddTriggerVolumeTo(interactable);
                // Keep the selection on the interactable while it is being set up.
                Selection.activeGameObject = interactable.gameObject;
                serializedObject.Update();
                OnSerializedChanged();
            });
            m_AddVolume.tooltip = "A new Trigger Volume beside this object, picked here. Size it afterwards with Edit Volume.";
            m_AddVolume.style.marginLeft = 4;
            m_VolumeFold.Add(CardRows.Row("Volume", volume, m_AddVolume));

            m_NoVolumeWarn = CardRows.Warn(string.Empty);
            m_NoVolumeText = m_NoVolumeWarn.Q<Label>(className: "blocks-tv-warn__text");
            m_VolumeFold.Add(m_NoVolumeWarn);

            m_VolumeDetails = new Fold();

            // Who counts is set on the volume; shown here for the sentence below.
            m_Detects = new Label();
            m_Detects.AddToClassList("blocks-starts__detects");
            m_DetectsLink = Buttons.Link(string.Empty, () =>
            {
                if (m_Interactable != null && m_Interactable.Zone != null) Selection.activeObject = m_Interactable.Zone;
            });
            m_DetectsLink.tooltip = "Who counts is set on the volume's Detection card. Opens the volume.";
            m_DetectsLink.AddToClassList("blocks-starts__link");
            m_VolumeDetails.Add(CardRows.Row("Detects", m_Detects, m_DetectsLink));

            m_PlaysField = CardRows.Segmented(k_Plays, "Walking in starts it, or pressing the button while inside.");
            m_PlaysField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue < 0 || evt.newValue >= k_Plays.Length) return;
                SetPlayOn(k_Plays[evt.newValue].mode);
            });
            m_VolumeDetails.Add(CardRows.Row("Plays", m_PlaysField));

            // On press is greyed out when the volume does not detect the player; only the player has a button.
            m_PressNote = new Label { enableRichText = true };
            m_PressNote.AddToClassList("blocks-tv-sub");
            m_PressNote.AddToClassList("blocks-starts__note");
            m_PressNoteRow = CardRows.Row(string.Empty, m_PressNote);
            m_VolumeDetails.Add(m_PressNoteRow);

            m_ButtonFold = new Fold();
            BuildButtonRows(m_ButtonFold);
            m_VolumeDetails.Add(m_ButtonFold);

            m_LeavesField = CardRows.Segmented(k_Leaves, m_StopWhenPlayerLeaves.tooltip);
            m_LeavesField.RegisterValueChangedCallback(evt =>
            {
                if (m_Interactable == null || evt.newValue < 0 || evt.newValue >= k_Leaves.Length) return;
                bool stop = k_Leaves[evt.newValue].stop;
                if (m_StopWhenPlayerLeaves.boolValue == stop) return;
                m_StopWhenPlayerLeaves.boolValue = stop;
                serializedObject.ApplyModifiedProperties();
                OnSerializedChanged();
            });
            m_VolumeDetails.Add(CardRows.Row("If it leaves", m_LeavesField));

            m_VolumeFold.Add(m_VolumeDetails);
            m_StartsCard.Add(m_VolumeFold);

            m_StartsSay = CardRows.SumBar();
            m_StartsSay.enableRichText = true;
            m_StartsCard.AddFooter(m_StartsSay);

            return m_StartsCard;
        }

        /// <summary>
        /// The Button row: ⋮ switches between an input asset reference and an inline action. Its keys show
        /// underneath; an inline action's editor opens below.
        /// </summary>
        void BuildButtonRows(VisualElement parent)
        {
            m_ButtonMode = new Button(ShowButtonModeMenu) { text = "⋮", tooltip = "Use Reference: an action in your input asset, which on-screen touch controls also drive. Use Action: keys set here." };
            m_ButtonMode.AddToClassList("blocks-starts__mode");

            m_ButtonReference = new ObjectField { objectType = typeof(UnityEngine.InputSystem.InputActionReference), allowSceneObjects = false, tooltip = m_Button.tooltip };
            m_ButtonReference.BindProperty(m_Button.FindPropertyRelative("m_Reference"));

            var inlineName = new Label("Keys set here");
            inlineName.AddToClassList("blocks-starts__inline");

            parent.Add(CardRows.Row("Button", m_ButtonMode, m_ButtonReference, inlineName));

            m_ButtonKeys = new Label();
            m_ButtonKeys.AddToClassList("blocks-tv-sub");
            m_ButtonKeys.AddToClassList("blocks-starts__keys");
            parent.Add(CardRows.Row(string.Empty, m_ButtonKeys));

            m_InlineKeysFold = new Fold();
            m_InlineKeysFold.AddToClassList("blocks-starts__keys-editor");
            m_InlineKeysFold.Add(new PropertyField(m_Button.FindPropertyRelative("m_Action"), "Keys") { tooltip = m_Button.tooltip });
            parent.Add(m_InlineKeysFold);
        }

        /// <summary>Looking: whether the button needs the camera on the look point. SyncStarts moves it under the Button rows.</summary>
        void BuildLookRows()
        {
            m_LookFold = new Fold();
            VisualElement parent = m_LookFold;
            m_LookField = CardRows.Segmented(k_Look, m_RequireLook.tooltip);
            m_LookField.RegisterValueChangedCallback(evt => SetRequireLook(evt.newValue));
            parent.Add(CardRows.Row("Looking", m_LookField));

            m_LookDetails = new Fold();
            var point = new ObjectField { objectType = typeof(Transform), allowSceneObjects = true, tooltip = m_LookPoint.tooltip };
            point.BindProperty(m_LookPoint);
            m_LookDetails.Add(CardRows.Row("Look point", point, CardRows.Sub("empty: this object")));
            m_LookDetails.Add(CardRows.Row("Max angle", ClampedFloat(m_MaxLookAngle, 1f, 90f), CardRows.Sub("degrees from the centre of the view")));
            m_LookDetails.Add(CardRows.Row("Margin", ClampedFloat(m_LookMargin, 0f, 15f), CardRows.Sub("degrees more before it lets go")));

            m_ReachFold = new Fold();
            m_ReachFold.Add(CardRows.Row("Max distance", ClampedFloat(m_MaxLookDistance, 0f, float.MaxValue), CardRows.Sub("metres · 0 is any distance")));
            m_LookDetails.Add(m_ReachFold);
            parent.Add(m_LookDetails);
        }

        FloatField ClampedFloat(SerializedProperty property, float min, float max)
        {
            var field = new FloatField { tooltip = property.tooltip };
            field.AddToClassList("blocks-repeats__wait");
            field.BindProperty(property);
            field.RegisterValueChangedCallback(evt =>
            {
                if (m_Interactable == null) return;
                float clamped = Mathf.Clamp(evt.newValue, min, max);
                if (clamped == evt.newValue) return;
                property.floatValue = clamped;
                serializedObject.ApplyModifiedProperties();
            });
            return field;
        }

        /// <summary>A click on Looking's button <paramref name="index"/>. Internal for tests: a detached element sends no change events.</summary>
        internal void SetRequireLook(int index)
        {
            if (m_Interactable == null || index < 0 || index >= k_Look.Length) return;
            bool look = k_Look[index].look;
            if (m_RequireLook.boolValue == look) return;
            m_RequireLook.boolValue = look;
            serializedObject.ApplyModifiedProperties();
            OnSerializedChanged();
        }

        void ShowButtonModeMenu()
        {
            if (m_Interactable == null) return;
            var menu = new GenericMenu();
            bool reference = m_ButtonUseReference.boolValue;
            menu.AddItem(new GUIContent("Use Reference"), reference, () => SetButtonUsesReference(true));
            menu.AddItem(new GUIContent("Use Action"), !reference, () => SetButtonUsesReference(false));
            menu.DropDown(m_ButtonMode.worldBound);
        }

        void SetButtonUsesReference(bool reference)
        {
            if (m_Interactable == null) return;
            serializedObject.Update();
            if (m_ButtonUseReference.boolValue == reference) return;
            m_ButtonUseReference.boolValue = reference;
            serializedObject.ApplyModifiedProperties();
            OnSerializedChanged();
        }

        /// <summary>A click on Starts on's button <paramref name="index"/>. Internal for tests: a detached element sends no change events.</summary>
        internal void SetStartsOn(int index)
        {
            if (m_Interactable == null || index < 0 || index >= k_StartsOn.Length) return;
            switch (k_StartsOn[index].mode)
            {
                case StartsOn.Volume:
                    // Keep the current volume mode, else pick the simpler one.
                    if (!m_Interactable.UsesZone) SetPlayOn(InteractablePlayOn.EnterZone);
                    break;
                // Clear the Zone so the volume stops listing this interactable.
                case StartsOn.Anywhere: SetPlayOn(InteractablePlayOn.InteractAnywhere, clearZone: true); break;
                case StartsOn.Enable: SetPlayOn(InteractablePlayOn.Enable); break;
                default: SetPlayOn(InteractablePlayOn.Manual); break;
            }
        }

        void SetPlayOn(InteractablePlayOn mode, bool clearZone = false)
        {
            if (m_Interactable == null) return;
            if (m_PlayOn.intValue == (int)mode) return;
            m_PlayOn.intValue = (int)mode;
            if (clearZone) m_Zone.objectReferenceValue = null;
            serializedObject.ApplyModifiedProperties();
            OnSerializedChanged();
        }

        static StartsOn StartsOnOf(InteractablePlayOn mode)
        {
            switch (mode)
            {
                case InteractablePlayOn.EnterZone:
                case InteractablePlayOn.Interact: return StartsOn.Volume;
                case InteractablePlayOn.InteractAnywhere: return StartsOn.Anywhere;
                case InteractablePlayOn.Enable: return StartsOn.Enable;
                default: return StartsOn.Manual;
            }
        }

        static int IndexOf<T>((T mode, string label, string tooltip)[] choices, T mode)
        {
            for (int i = 0; i < choices.Length; i++)
                if (EqualityComparer<T>.Default.Equals(choices[i].mode, mode)) return i;
            return -1;
        }

        /// <summary>Reruns the player check and resyncs the card only when the answer changed.</summary>
        void RecheckPlayer()
        {
            if (m_Interactable == null) return;
            var zone = m_Zone.objectReferenceValue as TriggerVolume;
            if (zone == null || InteractionWiring.DetectsPlayer(zone) == m_ZoneDetectsPlayer) return;
            SyncStarts();
        }

        void SyncStarts()
        {
            var mode = (InteractablePlayOn)m_PlayOn.intValue;
            var zone = m_Zone.objectReferenceValue as TriggerVolume;
            m_ZoneDetectsPlayer = zone == null || InteractionWiring.DetectsPlayer(zone);
            bool usesZone = mode == InteractablePlayOn.EnterZone || mode == InteractablePlayOn.Interact;
            bool press = mode == InteractablePlayOn.Interact;
            bool anywhere = mode == InteractablePlayOn.InteractAnywhere;
            bool reference = m_ButtonUseReference.boolValue;

            m_StartsOnField.SetValueWithoutNotify(IndexOf(k_StartsOn, StartsOnOf(mode)));
            m_PlaysField.SetValueWithoutNotify(IndexOf(k_Plays, mode));
            m_LeavesField.SetValueWithoutNotify(IndexOf(k_Leaves, m_StopWhenPlayerLeaves.boolValue));

            m_VolumeFold.SetOpen(usesZone);
            m_VolumeDetails.SetOpen(zone != null);
            m_AddVolume.style.display = zone != null ? DisplayStyle.None : DisplayStyle.Flex;
            m_NoVolumeWarn.style.display = zone == null ? DisplayStyle.Flex : DisplayStyle.None;
            // Button with no volume works anywhere; supported from code but not offered here.
            m_NoVolumeText.text = press
                ? $"No volume picked, so {FirstKeyText()} plays this anywhere. Pick the volume it should work in, or choose Anywhere above."
                : "Pick the Trigger Volume that starts this, or press + Add.";

            if (zone != null)
            {
                m_Detects.text = DetectsShort(zone);
                m_DetectsLink.text = $"set on {zone.name} ›";
            }

            // On press is greyed out while the volume does not detect the player. If already chosen it
            // stays lit with an amber note; it is never switched automatically.
            int pressIndex = IndexOf(k_Plays, InteractablePlayOn.Interact);
            string who = zone != null ? DetectsText(zone) ?? "nothing" : null;
            m_PlaysField.SetChoiceEnabled(pressIndex, m_ZoneDetectsPlayer,
                zone != null ? $"Only the Player has a button, and {zone.name} detects {CardRows.Plain(who)}." : null);
            m_PressNoteRow.style.display = m_ZoneDetectsPlayer ? DisplayStyle.None : DisplayStyle.Flex;
            m_PressNote.EnableInClassList("blocks-starts__note--warn", press && !m_ZoneDetectsPlayer);
            if (zone != null && !m_ZoneDetectsPlayer)
            {
                string where = $"<b>{CardRows.Rich(zone.name)}</b>";
                m_PressNote.text = press
                    ? $"On press needs the Player inside, and {where} detects {who}. Choose On enter, or set Detects on {where} to the Player."
                    : $"On press needs the Player inside. {where} detects {who}.";
            }

            // One set of Button rows: under Plays for a volume, straight under Starts on for Button.
            VisualElement buttonHome = anywhere ? m_AnywhereFold : m_VolumeDetails;
            if (m_ButtonFold.parent != buttonHome.contentContainer)
            {
                m_ButtonFold.RemoveFromHierarchy();
                if (anywhere) m_AnywhereFold.Add(m_ButtonFold);
                else m_VolumeDetails.Insert(m_VolumeDetails.IndexOf(m_PressNoteRow) + 1, m_ButtonFold);
                m_ButtonFold.Bind(serializedObject);
            }

            m_AnywhereFold.SetOpen(anywhere);
            m_ButtonFold.SetOpen(press || anywhere);
            m_ButtonReference.style.display = reference ? DisplayStyle.Flex : DisplayStyle.None;
            m_ButtonReference.parent.Q<Label>(className: "blocks-starts__inline").style.display = reference ? DisplayStyle.None : DisplayStyle.Flex;
            m_InlineKeysFold.SetOpen((press || anywhere) && !reference);
            m_ButtonKeys.text = ButtonKeysText(reference);

            // Looking follows the Button rows; under Manual it shows once an Input Prompt reads it, or it is on.
            bool manual = mode == InteractablePlayOn.Manual;
            InputPrompt prompt = manual ? InteractionWiring.PromptReading(m_Interactable) : null;
            bool viaPrompt = prompt != null || (manual && m_RequireLook.boolValue);
            VisualElement lookHome = anywhere ? m_AnywhereFold : manual ? m_PromptFold : m_VolumeDetails;
            if (m_LookFold.parent != lookHome.contentContainer)
            {
                m_LookFold.RemoveFromHierarchy();
                if (lookHome == m_VolumeDetails) m_VolumeDetails.Insert(m_VolumeDetails.IndexOf(m_ButtonFold) + 1, m_LookFold);
                else lookHome.Add(m_LookFold);
                m_LookFold.Bind(serializedObject);
            }

            m_PromptFold.SetOpen(manual && viaPrompt);
            string promptName = prompt != null ? $"<b>{CardRows.Rich(prompt.name)}</b>" : null;
            m_PromptNote.text = prompt == null
                ? "No Input Prompt reads this, so Looking does nothing yet."
                : InteractionWiring.PromptPlays(prompt, m_Interactable)
                    ? $"The Input Prompt on {promptName} presses this, and reads Looking."
                    : $"The Input Prompt on {promptName} reads Looking, but its On Pressed does not call Play here.";
            m_LookFold.SetOpen(press || anywhere || viaPrompt);
            m_LookField.SetValueWithoutNotify(IndexOf(k_Look, m_RequireLook.boolValue));
            m_LookDetails.SetOpen(m_RequireLook.boolValue);
            m_ReachFold.SetOpen(anywhere);

            m_StartsSay.text = StartsSentence(mode, zone);
        }

        /// <summary>
        /// Who, what and where in one line, from the Starts answers and the volume's Detection. A second
        /// line appears only for a non-default leave behaviour. Rich text.
        /// </summary>
        string StartsSentence(InteractablePlayOn mode, TriggerVolume zone)
        {
            switch (mode)
            {
                case InteractablePlayOn.Enable:
                    return "Plays when this turns on, like at scene start.";
                case InteractablePlayOn.Manual:
                    InputPrompt prompt = InteractionWiring.PromptReading(m_Interactable);
                    // Show While Enabled only says the prompt reads this; only On Pressed calling Play starts it.
                    if (!InteractionWiring.PromptPlays(prompt, m_Interactable)) return "Plays only when something calls <b>Play()</b>.";
                    return $"Plays when its <b>Input Prompt</b> on <b>{CardRows.Rich(prompt.name)}</b> is pressed" +
                           (m_RequireLook.boolValue ? $"{LookClause()}." : ".");
            }

            string key = $"<b>{CardRows.Rich(FirstKeyText())}</b>";
            // Only the player has a button, so there is no volume Detection to describe.
            if (mode == InteractablePlayOn.InteractAnywhere)
            {
                if (!m_RequireLook.boolValue)
                    return $"Plays when the <b>Player</b> presses {key}, anywhere.";
                float reach = m_MaxLookDistance.floatValue;
                return reach > 0f
                    ? $"Plays when the <b>Player</b> presses {key} within <b>{reach:0.##} m</b>{LookClause()}."
                    : $"Plays when the <b>Player</b> presses {key}{LookClause()}, from any distance.";
            }

            if (zone == null)
                return mode == InteractablePlayOn.Interact
                    ? $"Press {key} anywhere to play.\nPick a volume to limit where."
                    : "Pick a Trigger Volume to start this.";

            string where = $"<b>{CardRows.Rich(zone.name)}</b>";
            string who = DetectsText(zone);
            if (who == null) return $"{where} detects nothing, so this never plays.";

            if (mode == InteractablePlayOn.Interact && !m_ZoneDetectsPlayer)
                return $"{where} doesn't detect the Player, so pressing {key} never plays this.";

            string text = mode == InteractablePlayOn.Interact
                ? $"Plays when {who} presses {key} in {where}{(m_RequireLook.boolValue ? LookClause() : string.Empty)}."
                : $"Plays when {who} walks into {where}.";
            if (m_StopWhenPlayerLeaves.boolValue) text += "\nWalking out stops it.";
            if (!zone.isActiveAndEnabled) text += $"\n{where} is off, so this won't play yet.";
            return text;
        }

        /// <summary>" while looking at it", or at the named look point. Rich text.</summary>
        string LookClause()
        {
            var point = m_LookPoint.objectReferenceValue as Transform;
            return point != null && point != m_Interactable.transform
                ? $" while looking at <b>{CardRows.Rich(point.name)}</b>"
                : " while looking at it";
        }

        /// <summary>
        /// The volume's Detection in words ("the Player", "anything tagged Enemy"), or null when it detects
        /// nothing. Rich text.
        /// </summary>
        static string DetectsText(TriggerVolume zone)
        {
            switch (zone.Detect)
            {
                case TriggerVolumeDetect.Object:
                    return zone.TargetObject != null ? $"<b>{CardRows.Rich(zone.TargetObject.name)}</b>" : null;
                case TriggerVolumeDetect.Layers:
                    int mask = zone.Layers.value;
                    if (mask == 0) return null;
                    if (mask == ~0) return "anything";
                    bool one = (mask & (mask - 1)) == 0;
                    return $"anything on the <b>{CardRows.Rich(LayerText(mask))}</b> {(one ? "layer" : "layers")}";
                default:
                    if (string.IsNullOrEmpty(zone.Tag)) return "anyone";
                    return zone.Tag == "Player" ? "the <b>Player</b>" : $"anything tagged <b>{CardRows.Rich(zone.Tag)}</b>";
            }
        }

        /// <summary>"Tag: Player", "Object: Ball", "Layers: Player, NPC".</summary>
        static string DetectsShort(TriggerVolume zone)
        {
            switch (zone.Detect)
            {
                case TriggerVolumeDetect.Object: return "Object: " + (zone.TargetObject != null ? zone.TargetObject.name : "None");
                case TriggerVolumeDetect.Layers: return "Layers: " + (zone.Layers.value == 0 ? "Nothing" : LayerText(zone.Layers.value));
                default: return "Tag: " + (string.IsNullOrEmpty(zone.Tag) ? "Any" : zone.Tag);
            }
        }

        static string LayerText(int mask)
        {
            if (mask == ~0) return "any layer";
            var names = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                string layer = LayerMask.LayerToName(i);
                names.Add(string.IsNullOrEmpty(layer) ? $"layer {i}" : layer);
            }

            return string.Join(", ", names);
        }

        /// <summary>The line under Button: the keys it answers to, and where they come from.</summary>
        string ButtonKeysText(bool reference)
        {
            if (reference)
            {
                var asset = m_Button.FindPropertyRelative("m_Reference").objectReferenceValue as UnityEngine.InputSystem.InputActionReference;
                if (asset == null) return "Drag in an action from your input asset, such as Player/Interact.";
                string from = asset.asset != null ? $" · from {asset.asset.name}" : string.Empty;
                return BindingsText(" · ") + from;
            }

            // The keys are in the list below; say only what inline cannot do.
            return "On-screen touch controls need ⋮ › Use Reference.";
        }

        VisualElement BuildStepsCard()
        {
            m_StepsCard = new InspectorCard("steps", "Steps").LeadingChevron().Remember(target);
            m_StepsCard.AddToClassList("blocks-steps-card");

            m_StepsCard.Add(GroupTitle("When it plays", null));

            m_NoLock = CardRows.Warn(NoLockWarning);
            m_NoLock.style.marginTop = 0;
            m_NoLock.style.marginBottom = 6;
            m_NoLock.style.display = DisplayStyle.None;
            m_StepsCard.Add(m_NoLock);

            m_Steps = new StepListView(m_Groups, m_Interactable,
                "Nothing happens yet. Press + Add step: a wait, a move, an animation, a switch, an event.",
                RefreshSummaries);
            m_StepsCard.Add(m_Steps);

            var divider = new VisualElement();
            divider.AddToClassList("blocks-tv-divider");
            m_StepsCard.Add(divider);

            var stoppedTitle = GroupTitle("If stopped early", null);
            stoppedTitle.tooltip = StoppedEarlyHelp;
            m_StepsCard.Add(stoppedTitle);
            m_StoppedSteps = new StepListView(m_OnInterruptedGroups, m_Interactable,
                "Nothing extra happens. Typical: close the door, hide the prompt.",
                RefreshSummaries);
            m_StoppedSteps.AddToClassList("blocks-steps--quiet-empty");
            m_StepsCard.Add(m_StoppedSteps);

            m_StepsSay = CardRows.SumBar();
            m_StepsCard.AddFooter(m_StepsSay);

            return m_StepsCard;
        }

        /// <summary>A small-caps list title and, when given, one dim line under it.</summary>
        static VisualElement GroupTitle(string title, string description)
        {
            var group = new VisualElement();
            group.AddToClassList("blocks-tv-group");
            var label = new Label(title.ToUpperInvariant());
            label.AddToClassList("blocks-tv-group__title");
            group.Add(label);
            if (string.IsNullOrEmpty(description))
            {
                label.style.marginBottom = 6;
                return group;
            }

            var desc = new Label(description);
            desc.AddToClassList("blocks-tv-group__desc");
            group.Add(desc);
            return group;
        }

        VisualElement BuildRepeatsCard()
        {
            // Same layout as Starts.
            m_RepeatsCard = new InspectorCard("after", "Repeats").LeadingChevron().SetGloss("Whether it can play again.").Remember(target);

            m_RepeatsField = CardRows.Segmented(k_Repeats, m_AfterFinish.tooltip);
            m_RepeatsField.RegisterValueChangedCallback(evt =>
            {
                if (m_Interactable == null || evt.newValue < 0 || evt.newValue >= k_Repeats.Length) return;
                bool once = k_Repeats[evt.newValue].once;
                if (IsOnce((InteractableAfterFinish)m_AfterFinish.intValue) == once) return;
                m_AfterFinish.intValue = (int)(once ? InteractableAfterFinish.DisableComponent : InteractableAfterFinish.Nothing);
                // A Once interactable only waits after a cut-short run, so the hidden wait is cleared.
                if (once) m_Cooldown.floatValue = 0f;
                serializedObject.ApplyModifiedProperties();
                OnSerializedChanged();
            });
            m_RepeatsCard.Add(CardRows.Row("Plays", m_RepeatsField));

            m_EveryTimeFold = new Fold();
            m_WaitField = new FloatField { tooltip = m_Cooldown.tooltip };
            m_WaitField.AddToClassList("blocks-repeats__wait");
            m_WaitField.BindProperty(m_Cooldown);
            m_WaitField.RegisterValueChangedCallback(evt =>
            {
                if (m_Interactable == null || evt.newValue >= 0f) return;
                m_Cooldown.floatValue = 0f;
                serializedObject.ApplyModifiedProperties();
            });
            m_EveryTimeFold.Add(CardRows.Row("Wait between", m_WaitField, CardRows.Sub("seconds")));
            m_RepeatsCard.Add(m_EveryTimeFold);

            m_OnceFold = new Fold();
            m_ThenField = CardRows.Segmented(k_Then, m_AfterFinish.tooltip);
            m_ThenField.RegisterValueChangedCallback(evt =>
            {
                if (m_Interactable == null || evt.newValue < 0 || evt.newValue >= k_Then.Length) return;
                int value = (int)k_Then[evt.newValue].mode;
                if (m_AfterFinish.intValue == value) return;
                m_AfterFinish.intValue = value;
                serializedObject.ApplyModifiedProperties();
                OnSerializedChanged();
            });
            m_OnceFold.Add(CardRows.Row("Then", m_ThenField));
            m_RepeatsCard.Add(m_OnceFold);

            m_RepeatsSay = CardRows.SumBar();
            m_RepeatsSay.enableRichText = true;
            m_RepeatsCard.AddFooter(m_RepeatsSay);

            return m_RepeatsCard;
        }

        static bool IsOnce(InteractableAfterFinish mode) => mode != InteractableAfterFinish.Nothing;

        void SyncRepeats()
        {
            var mode = (InteractableAfterFinish)m_AfterFinish.intValue;
            bool once = IsOnce(mode);

            m_RepeatsField.SetValueWithoutNotify(IndexOf(k_Repeats, once));
            m_EveryTimeFold.SetOpen(!once);
            m_OnceFold.SetOpen(once);
            if (once) m_ThenField.SetValueWithoutNotify(IndexOf(k_Then, mode));

            m_RepeatsSay.text = RepeatsSentence(mode, m_Cooldown.floatValue, m_StopWhenPlayerLeaves.boolValue, m_Interactable.name);
        }

        /// <summary>The Repeats bar: what Plays, Then and the wait add up to. Rich text.</summary>
        public static string RepeatsSentence(InteractableAfterFinish mode, float wait, bool stopsOnLeave, string objectName)
        {
            string who = $"<b>{CardRows.Rich(objectName)}</b>";
            string text;
            switch (mode)
            {
                case InteractableAfterFinish.DisableComponent: text = "Plays once, then switches off. Tick the component to re-arm."; break;
                case InteractableAfterFinish.DeactivateObject: text = $"Plays once, then hides {who}, steps and all."; break;
                case InteractableAfterFinish.DestroyObject: text = $"Plays once, then deletes {who} for good."; break;
                default:
                    return wait > 0f
                        ? $"Can play again {wait:0.##} s after each run."
                        : "Plays again as soon as it finishes.";
            }

            // Only a full run counts; a run cut short (leaving the volume, or Stop) leaves it in play.
            if (stopsOnLeave) text += " A run cut short can try again.";
            return text;
        }

        void RefreshSummaries()
        {
            if (m_Interactable == null || m_StartsCard == null) return;

            m_StepsCard.SetGloss(StepsSummary(m_Interactable.Groups, "step", out bool _));

            m_StepsSay.text = StepsSentence(m_Interactable.Groups, m_Interactable.InterruptedGroups);

            m_NoLock.style.display = NeedsLock(m_Interactable.Groups) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The footer: both lists in one line. "Lock player → Open door. If cut short: Close door."</summary>
        public static string StepsSentence(List<StepGroup> groups, List<StepGroup> stopped)
        {
            string cut = stopped == null || stopped.Count == 0 ? "nothing extra." : GroupsSentence(stopped) + ".";
            string plays = groups == null || groups.Count == 0 ? "Nothing happens yet." : GroupsSentence(groups) + ".";
            return $"{plays} If cut short: {cut}";
        }

        /// <summary>
        /// The groups in order, by name: "Lock player → Walk up → Open door". A group with no steps reads
        /// "Open door (empty)".
        /// </summary>
        public static string GroupsSentence(List<StepGroup> groups)
        {
            if (groups == null || groups.Count == 0) return "Nothing happens yet.";

            var names = new List<string>(groups.Count);
            for (int i = 0; i < groups.Count; i++)
            {
                string name = StepListView.GroupName(groups[i], i);
                bool empty = groups[i]?.Steps == null || groups[i].Steps.Count == 0;
                names.Add(empty ? $"{name} (empty)" : name);
            }

            return string.Join(" → ", names);
        }

        static int CountSteps(List<StepGroup> groups)
        {
            int count = 0;
            if (groups == null) return 0;
            foreach (StepGroup group in groups)
            {
                if (group?.Steps == null) continue;
                foreach (InteractionStep step in group.Steps)
                {
                    if (step != null) count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Whether a Move Player To or Look At step runs with no enabled Lock Player step before it in list order.
        /// </summary>
        public static bool NeedsLock(List<StepGroup> groups)
        {
            if (groups == null) return false;

            bool locked = false;
            foreach (StepGroup group in groups)
            {
                if (group?.Steps == null) continue;
                foreach (InteractionStep step in group.Steps)
                {
                    if (step == null || !step.Enabled) continue;
                    if (step is LockPlayerStep) locked = true;
                    else if ((step is MoveToPoseStep || step is LookAtStep) && !locked) return true;
                }
            }

            return false;
        }

        string StepsSummary(List<StepGroup> groups, string noun, out bool warn)
        {
            warn = false;
            int total = CountSteps(groups);
            if (total == 0)
            {
                warn = true;
                return "nothing happens yet";
            }

            int active = 0;
            foreach (StepGroup group in groups)
            {
                if (group?.Steps == null) continue;
                foreach (InteractionStep step in group.Steps)
                {
                    if (step != null && step.Enabled) active++;
                }
            }

            string count = active == total
                ? $"{total} {(total == 1 ? noun : noun + "s")}"
                : $"{active} of {total} {noun}s on";
            if (active == 0) return count;

            float seconds = StepListView.EstimateDuration(groups, m_Interactable, out bool allKnown);
            if (allKnown && seconds < 0.05f) return $"{count} · instant";
            if (!allKnown && seconds < 0.05f) return count;
            return allKnown ? $"{count} · about {seconds:0.#} s" : $"{count} · at least {seconds:0.#} s";
        }

        /// <summary>The keys the button answers to, from its action (inline or referenced): "E or Gamepad West button".</summary>
        string BindingsText(string separator = " or ")
        {
            UnityEngine.InputSystem.InputAction action = m_Interactable.InputAction;
            if (action == null) return "the button";

            var parts = new List<string>();
            foreach (UnityEngine.InputSystem.InputBinding binding in action.bindings)
            {
                if (binding.isComposite || binding.isPartOfComposite || string.IsNullOrEmpty(binding.effectivePath)) continue;
                string text = HumanizeBinding(binding.effectivePath);
                if (!parts.Contains(text)) parts.Add(text);
            }

            return parts.Count == 0 ? "the button (no keys bound)" : string.Join(separator, parts);
        }

        /// <summary>The first key the button answers to ("E"); the Button row lists the rest.</summary>
        string FirstKeyText()
        {
            UnityEngine.InputSystem.InputAction action = m_Interactable.InputAction;
            if (action == null) return "the button";
            foreach (UnityEngine.InputSystem.InputBinding binding in action.bindings)
            {
                if (binding.isComposite || binding.isPartOfComposite || string.IsNullOrEmpty(binding.effectivePath)) continue;
                return HumanizeBinding(binding.effectivePath);
            }

            return "the button";
        }

        /// <summary>"&lt;Keyboard&gt;/e" → "E"; "&lt;Gamepad&gt;/buttonWest" → "Gamepad West button".</summary>
        static string HumanizeBinding(string path)
        {
            string device = null;
            string control = path;

            int close = path.IndexOf('>');
            if (path.StartsWith("<") && close > 0)
            {
                device = path.Substring(1, close - 1);
                control = path.Substring(close + 1).TrimStart('/');
            }

            int slash = control.LastIndexOf('/');
            if (slash >= 0) control = control.Substring(slash + 1);

            var words = new StringBuilder();
            foreach (char c in control)
            {
                if (char.IsUpper(c) && words.Length > 0) words.Append(' ');
                words.Append(c);
            }

            string text = words.ToString();
            if (text.StartsWith("button ")) text = text.Substring(7) + " button";
            if (text.Length == 1) text = text.ToUpperInvariant();
            else if (text.Length > 1) text = char.ToUpperInvariant(text[0]) + text.Substring(1);

            return device == null || device == "Keyboard" ? text : $"{device} {text}";
        }

        void Tick()
        {
            if (m_Interactable == null) return;

            bool playing = Application.isPlaying;
            bool running = playing && m_Interactable.IsPlaying;
            bool warn = false;
            string gloss;

            if (!playing)
            {
                gloss = k_TestGloss;
            }
            else if (running)
            {
                List<StepGroup> groups = m_Interactable.IsInterrupting ? m_Interactable.InterruptedGroups : m_Interactable.Groups;
                int group = Mathf.Clamp(m_Interactable.CurrentGroupIndex, 0, Mathf.Max(groups.Count - 1, 0));
                string name = groups.Count > 0 ? StepListView.GroupName(groups[group], group) : "";
                gloss = m_Interactable.IsInterrupting
                    ? $"Stopped early · {name} · {m_Interactable.Elapsed:0.0} s"
                    : $"Playing {name} · group {group + 1} of {groups.Count} · {m_Interactable.Elapsed:0.0} s";
            }
            else if (!m_Interactable.gameObject.activeInHierarchy && m_Interactable.gameObject.activeSelf)
            {
                warn = true;
                gloss = "A parent GameObject is inactive, so this can't play.";
            }
            else if (m_Interactable.CooldownRemaining > 0f)
            {
                gloss = $"Cooling down · {m_Interactable.CooldownRemaining:0.0} s";
            }
            else if (!m_Interactable.enabled || !m_Interactable.gameObject.activeSelf)
            {
                gloss = "Off. Play switches it on for the test.";
            }
            else if (m_Interactable.UsesZone && m_Interactable.Zone == null && m_Interactable.PlayOn == InteractablePlayOn.EnterZone)
            {
                warn = true;
                gloss = "No Trigger Volume picked, so only Play here starts it.";
            }
            else if (m_Interactable.UsesZone && m_Interactable.Zone != null && !m_Interactable.Zone.isActiveAndEnabled)
            {
                warn = true;
                gloss = $"{m_Interactable.Zone.name} is off, so only Play here starts it.";
            }
            else if ((m_Interactable.PlayOn == InteractablePlayOn.InteractAnywhere
                      || (m_Interactable.PlayOn == InteractablePlayOn.Interact && m_Interactable.Zone != null && m_Interactable.IsPlayerInZone))
                     && !m_Interactable.IsLookedAt)
            {
                gloss = m_Interactable.PlayOn == InteractablePlayOn.InteractAnywhere && m_Interactable.MaxLookDistance > 0f
                    ? $"Look at it within {m_Interactable.MaxLookDistance:0.##} m to arm."
                    : "In range · look at it to arm.";
            }
            else if (m_Interactable.PlayOn == InteractablePlayOn.InteractAnywhere
                     || (m_Interactable.PlayOn == InteractablePlayOn.Interact && m_Interactable.Zone != null && m_Interactable.IsPlayerInZone))
            {
                gloss = $"Armed · press {BindingsText()} to play.";
            }
            else
            {
                gloss = "Ready · " + ArmedText();
            }

            // The look gizmo's colour follows the player.
            if (playing && m_Interactable.RequireLook)
                SceneView.RepaintAll();

            m_TestCard.SetGloss(gloss);
            m_TestCard.tooltip = playing ? gloss : string.Empty;
            m_TestCard.EnableInClassList("blocks-test--warn", warn);
            m_TestCard.EnableInClassList("blocks-test--live", running);

            bool canTest = InteractableTestRun.CanTest(m_Interactable, out string reason);
            bool cooling = playing && !running && m_Interactable.CooldownRemaining > 0f;
            m_TestButton.SetEnabled(canTest && !cooling);

            if (running != m_TestShowsStop)
            {
                m_TestShowsStop = running;
                m_TestButton.text = running ? "Stop" : "Play";
                m_TestButton.EnableInClassList("blocks-button--primary", !running);
                SetIcon(m_TestButton, running ? "PreMatQuad" : "PlayButton");
            }

            m_TestButton.tooltip = running
                ? "Cut the run short. The player gets control back and the \"If stopped early\" steps run."
                : reason ?? (playing
                    ? "Moves the player into the volume and runs it now, skipping Starts. Switches the component on if it's off."
                    : "Enters Play mode, moves the player into the volume and runs it, skipping Starts. Switches the component on if it's off.");
        }

        string ArmedText()
        {
            switch (m_Interactable.PlayOn)
            {
                case InteractablePlayOn.Enable: return "Plays again the next time it is enabled.";
                case InteractablePlayOn.Interact:
                    return m_Interactable.Zone != null
                        ? $"Press {BindingsText()} in {m_Interactable.Zone.name} to play."
                        : $"Press {BindingsText()} to play, anywhere.";
                case InteractablePlayOn.EnterZone: return $"Plays when something {m_Interactable.Zone.name} detects walks in.";
                default: return "Waiting for something to call Play().";
            }
        }
    }
}
