using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Inspector for <see cref="TriggerVolume"/>: <b>Volume</b> (the box), <b>Detection</b> (who sets
    /// it off and how often), <b>Actions</b> (built-in and custom responses) and a read-only
    /// <b>Connected Interactables</b> list of Interactables whose Zone is this volume.
    /// </summary>
    /// <remarks>
    /// After firing shows only when Fires is "Once". The Detection and Actions summaries stay visible
    /// when their card is closed.
    /// </remarks>
    [CustomEditor(typeof(TriggerVolume))]
    [CanEditMultipleObjects]
    sealed class TriggerVolumeEditor : UnityEditor.Editor
    {
        const long k_TickMs = 100;
        const int k_ListenersEveryTicks = 30;
        const float k_NarrowWidth = 355f;
        const string k_Untagged = "Untagged";
        const string k_AnyTag = "Any tag";
        const string k_AddTag = "Add Tag...";
        // A literal "Untagged" (set only from code) means objects with no tag; shown apart from Any tag.
        const string k_LiteralUntagged = "Untagged (only objects with no tag)";
        const string k_EditHint = "drag handles in the scene";
        const string k_EditingHint = "handles are live: drag in the scene";

        /// <summary>The Detects choices, in the order shown: a button per <see cref="TriggerVolumeDetect"/>.</summary>
        static readonly (TriggerVolumeDetect mode, string label, string tooltip)[] k_Detects =
        {
            (TriggerVolumeDetect.Tag, "Tag", "Anything carrying a tag sets it off. The sample player is tagged Player."),
            (TriggerVolumeDetect.Object, "Object", "One GameObject, and anything under it, sets it off."),
            (TriggerVolumeDetect.Layers, "Layers", "Any collider on the chosen layers sets it off."),
        };

        /// <summary>The Fires choices, in the order shown.</summary>
        static readonly (TriggerVolumeFire mode, string label, string tooltip)[] k_Fires =
        {
            (TriggerVolumeFire.EveryTime, "Every time", "Fires every time something detected enters."),
            (TriggerVolumeFire.Once, "Once", "Fires the first time only; later entries do nothing."),
        };

        /// <summary>The After firing choices, in the order shown.</summary>
        static readonly (TriggerVolumeAfterFiring mode, string label, string tooltip)[] k_After =
        {
            (TriggerVolumeAfterFiring.Keep, "Keep active", "Stays on but ignores every later entry, until code calls Rearm()."),
            (TriggerVolumeAfterFiring.TurnOff, "Turn off", "Switches this component off once everyone has left. The box stays."),
            (TriggerVolumeAfterFiring.DestroyObject, "Destroy", "Destroys this GameObject, and everything on or under it, once everyone has left."),
        };

        // Shared across volumes so edit mode persists when selecting the next one.
        static bool s_EditBounds;

        readonly BoxBoundsHandle m_Handle = new BoxBoundsHandle();

        TriggerVolume m_Volume;
        SerializedObject m_Boxes;

        SerializedProperty m_Label;
        SerializedProperty m_Color;
        SerializedProperty m_Priority;
        SerializedProperty m_Detect;
        SerializedProperty m_Tag;
        SerializedProperty m_Object;
        SerializedProperty m_Layers;
        SerializedProperty m_ActiveWhileInside;
        SerializedProperty m_Fire;
        SerializedProperty m_AfterFiring;
        SerializedProperty m_OnTriggerEnter;
        SerializedProperty m_OnTriggerExit;

        InspectorCard m_VolumeCard;
        InspectorCard m_DetectionCard;
        InspectorCard m_LinkedCard;
        InspectorCard m_ActionsCard;

        Button m_EditButton;
        Label m_EditHint;

        SegmentedControl m_DetectsField;
        VisualElement m_ValueRow;
        Label m_ValueLabel;
        PopupField<string> m_TagField;
        ObjectField m_ObjectField;
        LayerMaskField m_LayersField;
        SegmentedControl m_FiresField;
        Fold m_AfterFold;
        SegmentedControl m_AfterField;
        Label m_DetectionSay;
        VisualElement m_WarnBox;

        VisualElement m_LinkedRows;
        Label m_LinkedNote;
        Label m_ActionsSay;

        // Who points here: rescanned by SyncListeners, read by the rows and the counts.
        readonly List<Interactable> m_Listeners = new List<Interactable>();
        bool m_DetectsPlayer = true;

        int m_ListenerSignature = -1;
        int m_Tick;

        // Scene state for the tag and player checks, refreshed when the hierarchy changes.
        string m_TaggedCheckedFor;
        bool m_TagExists = true;
        bool m_SceneDirty = true;

        void OnEnable()
        {
            m_Label = serializedObject.FindProperty("m_Label");
            m_Color = serializedObject.FindProperty("m_Color");
            m_Priority = serializedObject.FindProperty("m_Priority");
            m_Detect = serializedObject.FindProperty("m_Detect");
            m_Tag = serializedObject.FindProperty("m_Tag");
            m_Object = serializedObject.FindProperty("m_Object");
            m_Layers = serializedObject.FindProperty("m_Layers");
            m_ActiveWhileInside = serializedObject.FindProperty("m_ActiveWhileInside");
            m_Fire = serializedObject.FindProperty("m_Fire");
            m_AfterFiring = serializedObject.FindProperty("m_AfterFiring");
            m_OnTriggerEnter = serializedObject.FindProperty("m_OnTriggerEnter");
            m_OnTriggerExit = serializedObject.FindProperty("m_OnTriggerExit");

            // The editor outlives a detach (docking), so scene listeners live with the editor.
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.undoRedoPerformed += OnHierarchyChanged;
        }

        void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.undoRedoPerformed -= OnHierarchyChanged;
            ReleaseBoxes();
        }

        /// <summary>Unbinds the Size row before its serialized object is disposed.</summary>
        void ReleaseBoxes()
        {
            m_VolumeCard?.Q<Vector3Field>("size")?.Unbind();
            m_Boxes?.Dispose();
            m_Boxes = null;
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.AddToClassList("blocks-inspector");
            root.AddToClassList("blocks-tv");
            BuildingBlocksStyles.Apply(root);

            m_Volume = targets.Length == 1 ? target as TriggerVolume : null;

            root.Add(BuildVolumeCard());
            root.Add(BuildDetectionCard());
            root.Add(BuildActionsCard());
            if (m_Volume != null) root.Add(BuildInteractablesCard());

            root.Bind(serializedObject);
            BindSize();

            root.TrackSerializedObjectValue(serializedObject, _ => OnSerializedChanged());
            root.RegisterCallback<GeometryChangedEvent>(evt =>
                root.EnableInClassList("blocks-tv--narrow", evt.newRect.width > 0f && evt.newRect.width < k_NarrowWidth));

            SyncDetects();
            SyncFires();
            SyncListeners(force: true);
            Refresh();
            root.schedule.Execute(Tick).Every(k_TickMs);

            return root;
        }

        /// <summary>A field changed: refreshes sentences, warnings and summaries.</summary>
        void OnSerializedChanged()
        {
            SyncDetects();
            SyncFires();
            // Not forced: rows rebuild only when the zone name, listeners or Detects verdict change.
            SyncListeners(force: false);
            Refresh();
        }

        /// <summary>Something in the scene changed: who points here or who is tagged.</summary>
        void OnHierarchyChanged()
        {
            if (m_DetectionCard == null) return;
            m_SceneDirty = true;
            SyncListeners(force: false);
            RefreshWarnings();
            RefreshEventsSay();
        }

        VisualElement BuildVolumeCard()
        {
            m_VolumeCard = new InspectorCard("volume", "Volume").LeadingChevron().SetGloss("Where it happens: the area's shape and look.").Remember(target);

            // Edit
            m_EditButton = Buttons.Secondary("Edit Volume", ToggleEditBounds);
            m_EditButton.tooltip = "Drag the handles in the Scene view to resize the box. Stays on as you click from volume to volume.";
            SetIcon(m_EditButton, "EditCollider", "Grid.MoveTool");
            m_EditHint = Sub(k_EditHint);
            m_VolumeCard.Add(Row("Edit", m_EditButton, m_EditHint));
            SyncEditButton();

            // Size: the BoxCollider's size, bound after the volume's own fields (see BindSize).
            var size = new Vector3Field { name = "size", tooltip = "The box's size, in local units, the same value as on the Box Collider below. The card's summary shows the size in the scene, scale included." };
            m_VolumeCard.Add(Row("Size", size));

            var label = new TextField { bindingPath = "m_Label", isDelayed = false, tooltip = m_Label.tooltip };
            m_VolumeCard.Add(Row("Label", label));

            var color = new ColorField { bindingPath = "m_Color", tooltip = m_Color.tooltip };
            m_VolumeCard.Add(Row("Color", color));

            var priority = new IntegerField { bindingPath = "m_Priority", tooltip = m_Priority.tooltip };
            priority.AddToClassList("blocks-tv-narrow");
            m_VolumeCard.Add(Row("Priority", priority, Sub("when two volumes overlap, the higher one reports")));

            return m_VolumeCard;
        }

        /// <summary>
        /// The Size row edits the Box Collider, so it binds to its own serialized object after
        /// <c>root.Bind</c>.
        /// </summary>
        void BindSize()
        {
            var size = m_VolumeCard.Q<Vector3Field>("size");
            if (size == null) return;

            var boxes = new List<Object>();
            foreach (Object t in targets)
            {
                BoxCollider box = (t as TriggerVolume)?.Box;
                if (box != null) boxes.Add(box);
            }

            if (boxes.Count != targets.Length)
            {
                size.SetEnabled(false);
                size.tooltip = "No Box Collider on this object; the volume has no shape.";
                return;
            }

            m_Boxes?.Dispose();
            m_Boxes = new SerializedObject(boxes.ToArray());
            size.BindProperty(m_Boxes.FindProperty("m_Size"));
        }

        /// <summary>A small icon button at the end of a Connected Interactables row. Its click does not reach the row.</summary>
        static Button RowButton(string tooltip, System.Action onClick, params string[] icons)
        {
            var button = new Button(onClick) { tooltip = tooltip };
            button.AddToClassList("blocks-tv-irow__button");
            SetIcon(button, icons);
            button.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            return button;
        }

        static void SetIcon(Button button, params string[] icons)
        {
            Texture2D texture = EditorIcons.Find(icons);
            if (texture != null) button.iconImage = Background.FromTexture2D(texture);
        }

        void ToggleEditBounds()
        {
            s_EditBounds = !s_EditBounds;
            SyncEditButton();
            SceneView.RepaintAll();
        }

        void SyncEditButton()
        {
            m_EditButton.EnableInClassList("blocks-button--primary", s_EditBounds);
            m_EditButton.text = s_EditBounds ? "Editing Volume" : "Edit Volume";
            m_EditHint.text = s_EditBounds ? k_EditingHint : k_EditHint;
        }

        VisualElement BuildDetectionCard()
        {
            m_DetectionCard = new InspectorCard("detection", "Detection").LeadingChevron().SetGloss("Who sets off the actions below, and how often.").Remember(target);

            // Detects: the mode on one row, its value on the next, under the mode's own name.
            m_DetectsField = Segmented(k_Detects, m_Detect.tooltip);
            m_DetectsField.RegisterValueChangedCallback(evt => SetEnum(m_Detect, k_Detects, evt.newValue));
            m_DetectionCard.Add(Row("Detects", m_DetectsField));

            // The runtime stores "" for any tag, which TagField would show as Untagged. A plain popup shows
            // it as Any tag and keeps Add Tag...; a literal "Untagged" keeps its own name.
            m_TagField = new PopupField<string>(TagChoices(), k_Untagged,
                tag => tag == k_Untagged ? k_AnyTag : tag,
                tag => tag == k_Untagged ? k_AnyTag : tag)
            { tooltip = m_Tag.tooltip };
            // New tags appear the next time the popup opens.
            m_TagField.RegisterCallback<PointerDownEvent>(_ => m_TagField.choices = TagChoices(), TrickleDown.TrickleDown);
            m_TagField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == k_AddTag)
                {
                    m_TagField.SetValueWithoutNotify(evt.previousValue);
                    SettingsService.OpenProjectSettings("Project/Tags and Layers");
                    return;
                }

                m_Tag.stringValue = evt.newValue == k_Untagged ? string.Empty : evt.newValue;
                serializedObject.ApplyModifiedProperties();
            });
            m_TagField.TrackPropertyValue(m_Tag, _ => SyncTagField());
            SyncTagField();

            m_ObjectField = new ObjectField { bindingPath = "m_Object", objectType = typeof(GameObject), allowSceneObjects = true, tooltip = m_Object.tooltip };
            m_LayersField = new LayerMaskField { bindingPath = "m_Layers", tooltip = m_Layers.tooltip };

            m_ValueRow = Row("Tag", m_TagField, m_ObjectField, m_LayersField);
            m_ValueLabel = m_ValueRow.Q<Label>(className: "blocks-tv-field__label");
            m_DetectionCard.Add(m_ValueRow);

            // Fires, and After firing only when it fires once.
            m_FiresField = Segmented(k_Fires, m_Fire.tooltip);
            m_FiresField.RegisterValueChangedCallback(evt => SetEnum(m_Fire, k_Fires, evt.newValue));
            m_DetectionCard.Add(Row("Fires", m_FiresField));

            m_AfterFold = new Fold();
            m_AfterField = Segmented(k_After, m_AfterFiring.tooltip);
            m_AfterField.RegisterValueChangedCallback(evt => SetEnum(m_AfterFiring, k_After, evt.newValue));
            m_AfterFold.Add(Row("After firing", m_AfterField));
            m_DetectionCard.Add(m_AfterFold);

            m_WarnBox = new VisualElement();
            m_DetectionCard.Add(m_WarnBox);

            m_DetectionSay = SumBar();
            m_DetectionCard.AddFooter(m_DetectionSay);

            return m_DetectionCard;
        }

        VisualElement BuildInteractablesCard()
        {
            // Read only: set on each Interactable's Zone field.
            m_LinkedCard = new InspectorCard("interactables", "Connected Interactables").LeadingChevron().SetGloss("Interactables that start from this area.").Remember(target);
            var pill = new Label("Read only") { tooltip = "Each Interactable picks its volume on its own Starts card." };
            pill.AddToClassList("blocks-tv-pill");
            m_LinkedCard.AddAction(pill);

            m_LinkedRows = new VisualElement();
            m_LinkedRows.AddToClassList("blocks-tv-list");
            m_LinkedCard.Add(m_LinkedRows);

            m_LinkedNote = Note("Interactables in this scene that start from this volume. Change what they do on the Interactable's Starts card.");
            m_LinkedCard.Add(m_LinkedNote);
            return m_LinkedCard;
        }

        VisualElement BuildActionsCard()
        {
            m_ActionsCard = new InspectorCard("actions", "Actions").LeadingChevron().SetGloss("What happens when the Detection rule above fires.").Remember(target);

            // Built-in actions the volume runs itself, drawn like Unity events. Add new ones as drawers here.
            var builtIn = Group("Built-in Actions", "Common enter/exit setups.");
            builtIn.Add(BuiltInDrawer("Active While Inside", "Sets listed GameObjects active on enter and inactive on exit.", m_ActiveWhileInside));
            m_ActionsCard.Add(builtIn);

            var divider = new VisualElement();
            divider.AddToClassList("blocks-tv-divider");
            m_ActionsCard.Add(divider);

            // Custom: Unity's own event drawers, untouched.
            var custom = Group("Custom Actions", "Add your own actions to the standard enter and exit events.");
            custom.Add(EventDrawer(m_OnTriggerEnter));
            custom.Add(EventDrawer(m_OnTriggerExit));
            m_ActionsCard.Add(custom);

            m_ActionsSay = SumBar();
            m_ActionsCard.AddFooter(m_ActionsSay);
            return m_ActionsCard;
        }

        /// <summary>A group heading: small capitals title, one plain line under it saying what the group holds.</summary>
        internal static VisualElement Group(string title, string description)
        {
            var group = new VisualElement();
            group.AddToClassList("blocks-tv-group");
            var label = new Label(title.ToUpperInvariant());
            label.AddToClassList("blocks-tv-group__title");
            group.Add(label);
            var desc = new Label(description);
            desc.AddToClassList("blocks-tv-group__desc");
            group.Add(desc);
            return group;
        }

        internal static VisualElement EventDrawer(SerializedProperty property)
        {
            var field = new PropertyField(property);
            field.AddToClassList("blocks-tv-drawer");
            return field;
        }

        /// <summary>
        /// A built-in action drawn like a UnityEvent: header bar, bordered list bound to the array, + / −
        /// footer, and its description underneath.
        /// </summary>
        internal static VisualElement BuiltInDrawer(string title, string description, SerializedProperty array)
        {
            var drawer = new VisualElement();
            drawer.AddToClassList("blocks-tv-drawer");

            // Unity's own classes, so it matches the event drawers below.
            var container = new VisualElement();
            container.AddToClassList("unity-event__container");
            var header = new Label(title) { tooltip = array.tooltip };
            header.AddToClassList("unity-list-view__header");
            container.Add(header);

            var list = new ListView
            {
                bindingPath = array.propertyPath,
                showBorder = true,
                showAddRemoveFooter = true,
                showBoundCollectionSize = false,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                selectionType = SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                makeItem = () => new ObjectField { objectType = typeof(Object), allowSceneObjects = true },
            };
            list.Q<ScrollView>()?.AddToClassList("unity-event__list-view-scroll-view");
            container.Add(list);
            drawer.Add(container);

            var desc = new Label(description);
            desc.AddToClassList("blocks-tv-drawer__desc");
            drawer.Add(desc);
            return drawer;
        }

        internal static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? string.Empty : "s")}";

        /// <summary>A label on the fixed column, then the control(s) filling the rest.</summary>
        static VisualElement Row(string label, params VisualElement[] controls) => CardRows.Row(label, controls);

        static Label Sub(string text) => CardRows.Sub(text);

        /// <summary>The tinted bar closing a card: the sentence its settings add up to.</summary>
        static Label SumBar() => CardRows.SumBar();

        /// <summary>A small dim line under a list: what it shows and where to change it.</summary>
        static Label Note(string text)
        {
            var note = new Label(text);
            note.AddToClassList("blocks-tv-note");
            return note;
        }

        static VisualElement Warn(string text) => CardRows.Warn(text);

        static SegmentedControl Segmented<T>((T mode, string label, string tooltip)[] choices, string tooltip) => CardRows.Segmented(choices, tooltip);

        static int IndexOf<T>((T mode, string label, string tooltip)[] choices, T mode) where T : struct
        {
            for (int i = 0; i < choices.Length; i++)
                if (choices[i].mode.Equals(mode)) return i;
            return 0;
        }

        /// <summary>Writes the chosen button's enum value to the property, whatever order the buttons are shown in.</summary>
        void SetEnum<T>(SerializedProperty property, (T mode, string label, string tooltip)[] choices, int index) where T : struct, System.Enum
        {
            if (index < 0 || index >= choices.Length) return;
            int value = System.Convert.ToInt32(choices[index].mode);
            if (!property.hasMultipleDifferentValues && property.intValue == value) return;
            property.intValue = value;
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Lights the button for the property's value, or none for a mixed selection.</summary>
        static void SyncSegmented<T>(SegmentedControl field, SerializedProperty property, (T mode, string label, string tooltip)[] choices) where T : struct, System.Enum
        {
            int index = property.hasMultipleDifferentValues ? -1 : IndexOf(choices, (T)System.Enum.ToObject(typeof(T), property.intValue));
            if (field.value != index) field.SetValueWithoutNotify(index);
        }

        /// <summary>A scene name inside rich text: shown as typed, never read as a tag.</summary>
        static string Rich(string text) => CardRows.Rich(text);

        /// <summary>The project's tags, Untagged first (shown as Any tag), then Add Tag....</summary>
        static List<string> TagChoices()
        {
            var tags = new List<string>(UnityEditorInternal.InternalEditorUtility.tags);
            tags.Add(k_AddTag);
            return tags;
        }

        void SyncTagField()
        {
            if (m_TagField == null) return;
            m_TagField.showMixedValue = m_Tag.hasMultipleDifferentValues;
            string value = m_Tag.stringValue;
            m_TagField.SetValueWithoutNotify(string.IsNullOrEmpty(value) ? k_Untagged : value == k_Untagged ? k_LiteralUntagged : value);
        }

        void SyncDetects()
        {
            bool mixed = m_Detect.hasMultipleDifferentValues;
            var mode = (TriggerVolumeDetect)m_Detect.intValue;
            SyncSegmented(m_DetectsField, m_Detect, k_Detects);

            m_TagField.style.display = !mixed && mode == TriggerVolumeDetect.Tag ? DisplayStyle.Flex : DisplayStyle.None;
            m_ObjectField.style.display = !mixed && mode == TriggerVolumeDetect.Object ? DisplayStyle.Flex : DisplayStyle.None;
            m_LayersField.style.display = !mixed && mode == TriggerVolumeDetect.Layers ? DisplayStyle.Flex : DisplayStyle.None;
            m_ValueRow.style.display = mixed ? DisplayStyle.None : DisplayStyle.Flex;
            m_ValueLabel.text = mode == TriggerVolumeDetect.Object ? "Object" : mode == TriggerVolumeDetect.Layers ? "Layers" : "Tag";
        }

        void SyncFires()
        {
            bool mixed = m_Fire.hasMultipleDifferentValues;
            var fire = (TriggerVolumeFire)m_Fire.intValue;
            SyncSegmented(m_FiresField, m_Fire, k_Fires);
            SyncSegmented(m_AfterField, m_AfterFiring, k_After);
            m_AfterFold.SetOpen(!mixed && fire == TriggerVolumeFire.Once);
        }

        void Refresh()
        {
            RefreshDetectionSay();
            RefreshWarnings();
            RefreshEventsSay();
        }

        /// <summary>"Trigger Volume with label <b>Switch zone</b>", or its GameObject name when it has no label. Rich text.</summary>
        string VolumeText()
        {
            if (m_Volume == null) return "these volumes";
            return string.IsNullOrEmpty(m_Volume.Label)
                ? $"Trigger Volume <b>{Rich(m_Volume.name)}</b> (no label)"
                : $"Trigger Volume with label <b>{Rich(m_Volume.Label)}</b>";
        }

        void RefreshDetectionSay()
        {
            if (m_Detect.hasMultipleDifferentValues || m_Fire.hasMultipleDifferentValues)
            {
                m_DetectionSay.text = "These volumes are set up differently; a row with no button lit differs. Click a button to set them all the same.";
                return;
            }

            bool once = (TriggerVolumeFire)m_Fire.intValue == TriggerVolumeFire.Once;
            string text = $"Fires when {DetectsText()} enters {VolumeText()}";
            text += once ? $", once only: {AfterText()}." : ", every time.";
            m_DetectionSay.text = text;
        }

        /// <summary>What Once means afterwards, with its timing: After firing waits until the volume is empty.</summary>
        string AfterText()
        {
            switch ((TriggerVolumeAfterFiring)m_AfterFiring.intValue)
            {
                case TriggerVolumeAfterFiring.TurnOff: return "once everyone has left, the volume turns itself off";
                case TriggerVolumeAfterFiring.DestroyObject: return "once everyone has left, this GameObject is destroyed";
                default: return "after that it ignores every later entry";
            }
        }

        /// <summary>"anything tagged Player", "only Ball and what is under it", "anything on layers Player, NPC". Rich text.</summary>
        string DetectsText()
        {
            switch ((TriggerVolumeDetect)m_Detect.intValue)
            {
                case TriggerVolumeDetect.Object:
                {
                    var obj = m_Object.objectReferenceValue as GameObject;
                    return obj != null ? $"only <b>{Rich(obj.name)}</b> and what is under it" : "<b>nothing</b> (no object chosen)";
                }
                case TriggerVolumeDetect.Layers:
                {
                    int mask = m_Layers.intValue;
                    if (mask == 0) return "<b>nothing</b> (no layers selected)";
                    if (mask == ~0) return "anything, on any layer";
                    int count = CountBits(mask);
                    return $"anything on layer{(count > 1 ? "s" : string.Empty)} <b>{Rich(LayerNames(mask))}</b>";
                }
                default:
                {
                    string tag = m_Tag.stringValue;
                    return string.IsNullOrEmpty(tag) ? "anything" : $"anything tagged <b>{Rich(tag)}</b>";
                }
            }
        }

        /// <summary>The value alone, plain text, for a card summary or a warning: "Player", "Ball", "Player, NPC".</summary>
        string DetectsShort()
        {
            if (m_Detect.hasMultipleDifferentValues) return "different rules";
            switch ((TriggerVolumeDetect)m_Detect.intValue)
            {
                case TriggerVolumeDetect.Object:
                {
                    var obj = m_Object.objectReferenceValue as GameObject;
                    return obj != null ? obj.name : "None";
                }
                case TriggerVolumeDetect.Layers:
                {
                    int mask = m_Layers.intValue;
                    return mask == 0 ? "Nothing" : mask == ~0 ? "any layer" : LayerNames(mask);
                }
                default:
                {
                    string tag = m_Tag.stringValue;
                    return string.IsNullOrEmpty(tag) ? "anything" : tag;
                }
            }
        }

        static int CountBits(int mask)
        {
            int total = 0;
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) total++;
            return total;
        }

        static string LayerNames(int mask)
        {
            var names = new List<string>();
            for (int i = 0; i < 32 && names.Count < 4; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                string layer = LayerMask.LayerToName(i);
                names.Add(string.IsNullOrEmpty(layer) ? $"layer {i}" : layer);
            }

            int total = CountBits(mask);
            string text = string.Join(", ", names);
            return total > names.Count ? $"{text} and {total - names.Count} more" : text;
        }

        /// <summary>An Active While Inside entry by name: "Sign", "Lamp (Light)", or an asset with its type.</summary>
        internal static string TargetName(Object target) =>
            target is GameObject ? target.name
            : target is Component component ? $"{component.gameObject.name} ({target.GetType().Name})"
            : $"{target.name} ({target.GetType().Name})";

        /// <summary>
        /// One line counting every action in use ("Active While Inside: 5 GameObjects · Enter: 3 calls").
        /// The tooltip lists each object and call.
        /// </summary>
        void RefreshEventsSay()
        {
            if (m_ActionsSay == null) return;
            m_ActionsSay.tooltip = string.Empty;
            if (m_Volume == null)
            {
                m_ActionsSay.text = "Select one volume to read what it does.";
                return;
            }

            var shown = new List<string>();
            foreach (Object obj in m_Volume.ActiveWhileInside)
            {
                if (obj == null) continue;
                shown.Add(TargetName(obj));
            }

            var enter = new List<string>();
            var exit = new List<string>();
            AppendListeners(enter, m_Volume.TriggerEnter);
            AppendListeners(exit, m_Volume.TriggerExit);

            if (enter.Count == 0 && shown.Count == 0 && exit.Count == 0)
            {
                m_ActionsSay.text = "No actions yet. Add a GameObject to Active While Inside, or a call to a custom action.";
                return;
            }

            var parts = new List<string>(3);
            if (shown.Count > 0) parts.Add($"Active While Inside: <b>{Count(shown.Count, "GameObject")}</b>");
            if (enter.Count > 0) parts.Add($"Enter: <b>{Count(enter.Count, "call")}</b>");
            if (exit.Count > 0) parts.Add($"Exit: <b>{Count(exit.Count, "call")}</b>");
            m_ActionsSay.text = string.Join("  ·  ", parts);

            // Full list, one per line. Tooltips show plain text.
            var full = new List<string> { $"Waits for {Plain(WhoText())}." };
            foreach (string item in shown) full.Add($"While inside: {item}");
            foreach (string item in enter) full.Add($"On enter: {Plain(item)}");
            foreach (string item in exit) full.Add($"On exit: {Plain(item)}");
            m_ActionsSay.tooltip = string.Join("\n", full);
        }

        internal static string Plain(string rich) =>
            System.Text.RegularExpressions.Regex.Replace(rich, "<[^>]+>", string.Empty);

        /// <summary>Who Detects waits for, as the subject of a sentence: "<b>Player</b>", "anything on layers <b>NPC</b>". Rich text.</summary>
        string WhoText()
        {
            switch (m_Volume.Detect)
            {
                case TriggerVolumeDetect.Object:
                    return m_Volume.TargetObject != null ? $"<b>{Rich(m_Volume.TargetObject.name)}</b>" : "<b>nothing</b>";
                case TriggerVolumeDetect.Layers:
                {
                    int mask = m_Volume.Layers.value;
                    if (mask == 0) return "<b>nothing</b>";
                    if (mask == ~0) return "anything";
                    return $"anything on layer{(CountBits(mask) > 1 ? "s" : string.Empty)} <b>{Rich(LayerNames(mask))}</b>";
                }
                default:
                    return string.IsNullOrEmpty(m_Volume.Tag) ? "anything" : $"<b>{Rich(m_Volume.Tag)}</b>";
            }
        }

        /// <summary>"<b>Door</b>: Animator.Play", one per persistent call, "(muted)" when switched off.</summary>
        internal static void AppendListeners(List<string> items, UnityEventBase unityEvent)
        {
            int count = unityEvent.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
            {
                Object target = unityEvent.GetPersistentTarget(i);
                string method = unityEvent.GetPersistentMethodName(i) ?? string.Empty;
                if (method.StartsWith("set_")) method = method.Substring(4);
                string state = unityEvent.GetPersistentListenerState(i) == UnityEventCallState.Off ? " (muted)" : string.Empty;
                if (target == null)
                {
                    items.Add(string.IsNullOrEmpty(method) ? $"an empty call{state}" : $"<b>None</b>: {Rich(method)}{state}");
                    continue;
                }

                string who = target is Component component ? component.gameObject.name : target.name;
                string what = string.IsNullOrEmpty(method) ? $"{target.GetType().Name} (no function chosen)" : $"{target.GetType().Name}.{method}";
                items.Add($"<b>{Rich(who)}</b>: {Rich(what)}{state}");
            }
        }

        void RefreshWarnings()
        {
            if (m_WarnBox == null) return;
            m_WarnBox.Clear();
            if (m_Volume == null) return;

            switch (m_Volume.Detect)
            {
                case TriggerVolumeDetect.Tag:
                    if (string.IsNullOrEmpty(m_Volume.Tag)) break;
                    // A stored tag the project does not define draws blank in the popup.
                    if (System.Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, m_Volume.Tag) < 0)
                        m_WarnBox.Add(Warn($"The tag <b>{Rich(m_Volume.Tag)}</b> is not in this project's Tags and Layers, so this volume never fires."));
                    else if (!TagExistsInScene(m_Volume.Tag))
                        m_WarnBox.Add(Warn($"Nothing in this scene is tagged <b>{Rich(m_Volume.Tag)}</b>, so this volume never fires."));
                    break;
                case TriggerVolumeDetect.Object:
                    if (m_Volume.TargetObject == null)
                        m_WarnBox.Add(Warn("No object chosen, so this volume never fires."));
                    break;
                case TriggerVolumeDetect.Layers:
                    if (m_Volume.Layers.value == 0)
                        m_WarnBox.Add(Warn("No layers selected, so this volume never fires."));
                    break;
            }
        }

        /// <summary>
        /// Whether any GameObject in any loaded scene carries <paramref name="tag"/>. Rescans only when the
        /// hierarchy or the tag changed.
        /// </summary>
        bool TagExistsInScene(string tag)
        {
            if (!m_SceneDirty && tag == m_TaggedCheckedFor) return m_TagExists;
            m_TaggedCheckedFor = tag;
            m_SceneDirty = false;
            m_TagExists = false;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject rootObject in scene.GetRootGameObjects())
                {
                    foreach (Transform t in rootObject.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.gameObject.tag == tag) { m_TagExists = true; return true; }
                    }
                }
            }

            return false;
        }

        /// <summary>The interactables in the volume's scene whose Zone is this volume.</summary>
        List<Interactable> Listeners()
        {
            var found = new List<Interactable>();
            if (m_Volume == null) return found;

            foreach (Interactable interactable in Object.FindObjectsByType<Interactable>(FindObjectsInactive.Include))
            {
                if (interactable.Zone == m_Volume && interactable.gameObject.scene == m_Volume.gameObject.scene)
                    found.Add(interactable);
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }

        void SyncListeners(bool force)
        {
            if (m_LinkedRows == null || m_Volume == null) return;

            List<Interactable> listeners = Listeners();
            bool detectsPlayer = InteractionWiring.DetectsPlayer(m_Volume);
            string who = DetectsShort();

            // Cached so the sentence and counts do not rescan the scene.
            m_Listeners.Clear();
            m_Listeners.AddRange(listeners);
            m_DetectsPlayer = detectsPlayer;

            int signature = 17;
            signature = unchecked(signature * 31 + (detectsPlayer ? 1 : 0));
            signature = unchecked(signature * 31 + who.GetHashCode());
            foreach (Interactable interactable in listeners)
            {
#if UNITY_6000_4_OR_NEWER
                signature = unchecked(signature * 31 + interactable.GetEntityId().GetHashCode());
#else
                signature = unchecked(signature * 31 + interactable.GetInstanceID());
#endif
                signature = unchecked(signature * 31 + (int)interactable.PlayOn + (interactable.StopWhenPlayerLeaves ? 7 : 0) + (interactable.enabled ? 13 : 0));
                signature = unchecked(signature * 31 + (interactable.name != null ? interactable.name.GetHashCode() : 0));
            }

            if (!force && signature == m_ListenerSignature) return;
            m_ListenerSignature = signature;

            m_LinkedRows.Clear();
            m_LinkedNote.style.display = listeners.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (listeners.Count == 0)
            {
                var none = new Label("None yet. Pick this volume on an Interactable's Starts card, or use ⋮ › Add Interactable, connected.");
                none.AddToClassList("blocks-tv-empty");
                m_LinkedRows.Add(none);
                return;
            }

            foreach (Interactable chosen in listeners)
            {
                bool needsPlayer = chosen.PlayOn == InteractablePlayOn.Interact && !detectsPlayer;

                // Hierarchy-style row: icon, name, when it starts, Ping and Select. Read only; double-click selects.
                var row = new VisualElement { tooltip = $"Double-click to select {chosen.name}." };
                row.AddToClassList("blocks-tv-irow");
                if (needsPlayer) row.AddToClassList("blocks-tv-irow--warn");
                row.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.clickCount == 2) Selection.activeObject = chosen;
                });

                Texture icon = EditorGUIUtility.ObjectContent(chosen, typeof(Interactable)).image;
                if (icon != null)
                {
                    var image = new Image { image = icon, scaleMode = ScaleMode.ScaleToFit };
                    image.AddToClassList("blocks-tv-irow__icon");
                    row.Add(image);
                }

                var name = new Label(chosen.name);
                name.AddToClassList("blocks-tv-irow__name");
                row.Add(name);

                var verb = new Label(needsPlayer ? NeedsPlayerText(chosen) : WhenText(chosen));
                verb.AddToClassList("blocks-tv-irow__verb");
                if (needsPlayer)
                    verb.tooltip = $"{chosen.name} starts On press, and only the Player has a button, but this volume detects {who}. " +
                                   $"Set Detects to the Player, or change {chosen.name}'s Starts card.";
                row.Add(verb);
                row.Add(RowButton("Ping in Hierarchy", () => EditorGUIUtility.PingObject(chosen), "Search Icon", "ViewToolZoom"));
                row.Add(RowButton($"Select {chosen.name}", () => Selection.activeObject = chosen, "scenepicking_pickable_hover", "scenepicking_pickable"));
                m_LinkedRows.Add(row);
            }
        }

        /// <summary>"on press · needs the Player", and "· component off" when it is.</summary>
        static string NeedsPlayerText(Interactable interactable) =>
            "on press · needs the Player" + (interactable.enabled ? string.Empty : " · component off");

        /// <summary>"on press", "on enter · stops if it leaves", "not started from here", each "· component off" when it is.</summary>
        static string WhenText(Interactable interactable)
        {
            string when;
            switch (interactable.PlayOn)
            {
                case InteractablePlayOn.Interact: when = "on press"; break;
                case InteractablePlayOn.EnterZone: when = "on enter"; break;
                default: when = "not started from here"; break;
            }

            if (interactable.StopWhenPlayerLeaves && interactable.UsesZone) when += " · stops if it leaves";
            if (!interactable.enabled) when += " · component off";
            return when;
        }

        void Tick()
        {
            m_Tick++;
            if (m_Volume == null) return;

            if (m_Tick % k_ListenersEveryTicks == 0)
            {
                m_SceneDirty = true;
                SyncListeners(force: false);
                RefreshWarnings();
                RefreshEventsSay();
            }

        }

        void OnSceneGUI()
        {
            if (!s_EditBounds)
                return;

            var volume = (TriggerVolume)target;
            BoxCollider box = volume.Box;
            if (box == null)
                return;

            using (new Handles.DrawingScope(volume.Color, box.transform.localToWorldMatrix))
            {
                m_Handle.center = box.center;
                m_Handle.size = box.size;

                EditorGUI.BeginChangeCheck();
                m_Handle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(box, "Edit Trigger Volume");
                    box.center = m_Handle.center;
                    box.size = m_Handle.size;
                }
            }
        }
    }
}
