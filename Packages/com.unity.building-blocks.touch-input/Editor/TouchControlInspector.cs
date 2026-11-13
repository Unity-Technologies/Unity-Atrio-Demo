using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// Inspector for every <see cref="TouchControl"/>. It shows the control and its view together:
    /// Feel holds the control's fields, Look holds the view's.
    /// </summary>
    /// <remarks>
    /// What the control drives is shown read-only. Mappings are edited on the Touch Rig's Mappings card.
    /// </remarks>
    [CustomEditor(typeof(TouchControl), true)]
    [CanEditMultipleObjects]
    sealed class TouchControlInspector : UnityEditor.Editor
    {
        const long k_TickMs = 500;

        TouchControl m_Control;
        TouchControlView m_View;
        SerializedObject m_ViewObject;

        VisualElement m_Root;
        InspectorCard m_FeelCard;
        InspectorCard m_LookCard;
        Label m_Drives;
        int m_SeenVersion = -1;

        public override VisualElement CreateInspectorGUI()
        {
            m_Root = new VisualElement();
            m_Root.AddToClassList("blocks-inspector");
            BuildingBlocksStyles.Apply(m_Root);

            m_Control = target as TouchControl;
            if (m_Control == null)
                return m_Root;

            if (targets.Length > 1)
            {
                m_Root.Add(new Callout("SEVERAL CONTROLS", "Select one control to see its cards. Shared fields below.", CalloutTone.Default));
                InspectorElement.FillDefaultInspector(m_Root, serializedObject, this);
                return m_Root;
            }

            m_View = m_Control.GetComponent<TouchControlView>();
            m_ViewObject = m_View != null ? new SerializedObject(m_View) : null;

            m_Root.Add(BuildDrivesLine());
            m_Root.Add(BuildFeelCard());
            m_Root.Add(BuildLookCard());

            RefreshSummaries();
            RefreshDrives();
            m_Root.schedule.Execute(() =>
            {
                if (m_Control != null && TouchEditorChanges.Version != m_SeenVersion)
                    RefreshDrives();
            }).Every(k_TickMs);
            Undo.undoRedoPerformed += OnUndoRedo;
            m_Root.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);
            return m_Root;
        }

        void OnUndoRedo()
        {
            if (m_Control == null)
                return;

            serializedObject.Update();
            m_ViewObject?.Update();
            RefreshSummaries();
            RefreshDrives();
        }

        VisualElement BuildDrivesLine()
        {
            var line = new VisualElement();
            line.AddToClassList("blocks-touch-drives");

            m_Drives = new Label();
            m_Drives.AddToClassList("blocks-touch-drives__text");
            line.Add(m_Drives);

            TouchRig rig = m_Control.GetComponentInParent<TouchRig>(true);
            if (rig != null)
            {
                Button edit = Buttons.Link("Edit on Touch Rig", () =>
                {
                    Selection.activeObject = rig.gameObject;
                    EditorGUIUtility.PingObject(rig.gameObject);
                });
                edit.tooltip = "Mappings are made on the rig: its Mappings card lists every action the scene reads.";
                edit.AddToClassList("blocks-touch-drives__edit");
                line.Add(edit);
            }

            return line;
        }

        void RefreshDrives()
        {
            m_SeenVersion = TouchEditorChanges.Version;
            string id = string.IsNullOrEmpty(m_Control.ActionId) ? "(no id)" : m_Control.ActionId;
#if BB_TOUCH_HAS_INPUTSYSTEM
            var names = new List<string>();
            foreach (TouchInputSource source in TouchMappings.Scan())
            {
                if (source.IsMappedTo(m_Control))
                    names.Add(source.Name);
            }

            m_Drives.text = names.Count == 0 ? $"{id} drives no action yet." : $"{id} drives {string.Join(", ", names)}.";
            m_Drives.EnableInClassList("blocks-touch-drives__text--none", names.Count == 0);
#else
            m_Drives.text = $"{id} · read from C#: {TouchInspectorKit.CSharpLine(m_Control)}";
#endif
        }

        VisualElement BuildFeelCard()
        {
            m_FeelCard = new InspectorCard("feel", "Feel", defaultOpen: false).Remember(target);
            m_FeelCard.SetGloss("the finger");

            switch (m_Control)
            {
                case TouchJoystick _:
                    SerializedProperty origin = serializedObject.FindProperty("m_OriginMode");
                    var segmented = new SegmentedControl(new List<(string, string)>
                    {
                        ("Fixed", "The centre stays where the stick is drawn; a finger landing off-centre deflects it at once."),
                        ("Floating", "The centre is wherever the finger lands, so movement always starts from zero."),
                        ("Leash", "Floating, and the centre is dragged along when the finger leaves the rim."),
                    })
                    { tooltip = origin.tooltip };
                    segmented.SetValueWithoutNotify(origin.enumValueIndex);
                    segmented.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue < 0) return;
                        serializedObject.Update();
                        origin.enumValueIndex = evt.newValue;
                        serializedObject.ApplyModifiedProperties();
                        RefreshSummaries();
                    });
                    segmented.TrackPropertyValue(origin, p => segmented.SetValueWithoutNotify(p.enumValueIndex));
                    m_FeelCard.Add(TouchInspectorKit.Row("Origin", segmented));
                    m_FeelCard.Add(TouchInspectorKit.Row("Dead zone", TouchInspectorKit.Slider(serializedObject.FindProperty("m_DeadZone"), 0f, 0.5f, serializedObject.FindProperty("m_DeadZone").tooltip)));
                    m_FeelCard.Add(TouchInspectorKit.Row("Full at", TouchInspectorKit.Slider(serializedObject.FindProperty("m_Saturation"), 0.3f, 1f, serializedObject.FindProperty("m_Saturation").tooltip)));
                    m_FeelCard.Add(TouchInspectorKit.Field(serializedObject.FindProperty("m_RecenterOnRelease"), "Recenter"));
                    m_FeelCard.Add(TouchInspectorKit.Hint("Dead zone and full-at are fractions of the stick's radius. Same feel from C# and the Input System."));
                    break;

                case TouchButton _:
                    m_FeelCard.Add(TouchInspectorKit.Hint("Pressed while a finger is on it, even if the finger slides off. Pressed and Released fire once each, even for a tap shorter than a frame."));
                    break;

                default:
                    SerializedProperty it = serializedObject.GetIterator();
                    for (bool enter = true; it.NextVisible(enter); enter = false)
                    {
                        if (it.name == "m_Script" || it.name == "m_ActionId")
                            continue;
                        m_FeelCard.Add(TouchInspectorKit.Field(it.Copy(), it.displayName));
                    }
                    break;
            }

            Track(m_FeelCard, serializedObject);
            return m_FeelCard;
        }

        VisualElement BuildLookCard()
        {
            m_LookCard = new InspectorCard("look", "Look", defaultOpen: false).Remember(target);
            m_LookCard.SetGloss("what the player sees");

            if (m_ViewObject == null)
            {
                m_LookCard.Add(TouchInspectorKit.Hint("No view on this GameObject: nothing draws this control and nothing feeds it from the screen."));
                return m_LookCard;
            }

            SerializedObject v = m_ViewObject;
            switch (m_View)
            {
                case TouchJoystickView joystickView:
                    AddImageRows(joystickView.Base, "Ring");
                    AddImageRows(joystickView.Knob, "Knob");
                    m_LookCard.Add(TouchInspectorKit.Row("Knob travel", TouchInspectorKit.Slider(v.FindProperty("m_KnobTravel"), 0.2f, 1f, v.FindProperty("m_KnobTravel").tooltip)));
                    AddAlphaRows(v);
                    m_LookCard.Add(TouchInspectorKit.Field(v.FindProperty("m_HideWhenIdle"), "Hide when idle"));
                    break;

                case TouchButtonView buttonView:
                    AddImageRows(buttonView.Target, "Disc", tint: false);
                    m_LookCard.Add(TouchInspectorKit.Field(v.FindProperty("m_NormalColor"), "Tint"));
                    m_LookCard.Add(TouchInspectorKit.Field(v.FindProperty("m_PressedColor"), "Pressed tint"));
                    m_LookCard.Add(TouchInspectorKit.Row("Pressed scale", TouchInspectorKit.Slider(v.FindProperty("m_PressedScale"), 0.5f, 1.2f, v.FindProperty("m_PressedScale").tooltip)));
                    AddAlphaRows(v);
                    break;

            }

            m_LookCard.Add(TouchInspectorKit.Field(v.FindProperty("m_ShowOn"), "Show on"));
            Track(m_LookCard, v);
            return m_LookCard;
        }

        void AddImageRows(Image image, string label, bool tint = true)
        {
            if (image == null)
                return;

            var imageObject = new SerializedObject(image);
            m_LookCard.Add(TouchInspectorKit.ObjectRow(imageObject.FindProperty("m_Sprite"), label, typeof(Sprite), $"The sprite of '{image.name}'. The default skin's sprites are white, so tint recolours them."));
            if (tint)
                m_LookCard.Add(TouchInspectorKit.Field(imageObject.FindProperty("m_Color"), label + " tint"));
            Track(m_LookCard, imageObject);
        }

        /// <summary>Refreshes the summaries when <paramref name="tracked"/> changes. Each tracker element tracks one object.</summary>
        void Track(VisualElement parent, SerializedObject tracked)
        {
            var tracker = new VisualElement { name = "tracker" };
            tracker.style.display = DisplayStyle.None;
            parent.hierarchy.Add(tracker);
            tracker.TrackSerializedObjectValue(tracked, _ => RefreshSummaries());
        }

        void AddAlphaRows(SerializedObject v)
        {
            m_LookCard.Add(TouchInspectorKit.Row("Idle opacity", TouchInspectorKit.Slider(v.FindProperty("m_IdleAlpha"), 0f, 1f, v.FindProperty("m_IdleAlpha").tooltip)));
            m_LookCard.Add(TouchInspectorKit.Row("Active opacity", TouchInspectorKit.Slider(v.FindProperty("m_ActiveAlpha"), 0f, 1f, v.FindProperty("m_ActiveAlpha").tooltip)));
            m_LookCard.Add(TouchInspectorKit.Field(v.FindProperty("m_FadeSeconds"), "Fade (s)"));
        }

        void RefreshSummaries()
        {
            if (m_Control == null)
                return;

            switch (m_Control)
            {
                case TouchJoystick joystick:
                    string origin = joystick.OriginMode == JoystickOriginMode.FloatingWithLeash ? "leash" : joystick.OriginMode.ToString().ToLowerInvariant();
                    m_FeelCard.SetSummary($"{origin} · dead zone {joystick.DeadZone:0.##}");
                    break;
                case TouchButton _:
                    m_FeelCard.SetSummary("pressed while touched");
                    break;
                default:
                    m_FeelCard.SetSummary(string.Empty);
                    break;
            }

            if (m_View == null)
            {
                m_LookCard.SetSummary("no view", warn: true);
                return;
            }

            var parts = new List<string>();
            parts.Add(UsesDefaultSkin() ? "default skin" : "custom skin");
            if (m_View.IdleAlpha < 0.999f)
                parts.Add($"idle {m_View.IdleAlpha * 100f:0}%");
            if (m_View.ShowOn == TouchVisibility.MobilePlatformsOnly)
                parts.Add("mobile only");
            m_LookCard.SetSummary(string.Join(" · ", parts));
        }

        bool UsesDefaultSkin()
        {
            Image image = m_View switch
            {
                TouchJoystickView j => j.Base,
                TouchButtonView b => b.Target,
                _ => null,
            };

            if (image == null || image.sprite == null)
                return false;

            return TouchSkin.IsDefault(image.sprite);
        }
    }
}
