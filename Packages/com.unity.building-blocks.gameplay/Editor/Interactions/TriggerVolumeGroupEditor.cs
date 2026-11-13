using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Inspector for <see cref="TriggerVolumeGroup"/>: a strip with the member count and whether
    /// anyone is inside, then <b>Volumes</b> and <b>Actions</b>, the second laid out like a Trigger Volume's.
    /// </summary>
    [CustomEditor(typeof(TriggerVolumeGroup))]
    sealed class TriggerVolumeGroupEditor : UnityEditor.Editor
    {
        const long k_TickMs = 100;

        TriggerVolumeGroup m_Group;
        VisualElement m_Strip;
        VisualElement m_Dot;
        Label m_Title;
        Label m_Sub;
        string m_StripState = string.Empty;
        InspectorCard m_VolumesCard;
        InspectorCard m_ActionsCard;
        Label m_ActionsSay;
        VisualElement m_Root;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            m_Root = root;
            root.AddToClassList("blocks-inspector");
            BuildingBlocksStyles.Apply(root);

            if (targets.Length > 1)
            {
                root.Add(new Callout("MULTI-OBJECT EDITING", "Select a single group to edit it.", CalloutTone.Warning));
                return root;
            }

            m_Group = (TriggerVolumeGroup)target;

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
            root.Add(m_Strip);

            m_VolumesCard = new InspectorCard("volumes", "Volumes").LeadingChevron().SetGloss("The Trigger Volumes that count as one area.").Remember(target);
            m_VolumesCard.Add(new PropertyField(serializedObject.FindProperty("m_Volumes")));
            var hint = new Label("Reset on the component collects every Trigger Volume under this object.");
            hint.AddToClassList("blocks-panel__hint");
            m_VolumesCard.Add(hint);
            root.Add(m_VolumesCard);

            root.Add(BuildActionsCard());

            root.Bind(serializedObject);
            root.TrackSerializedObjectValue(serializedObject, _ => RefreshSummaries());
            RefreshSummaries();
            Tick();
            root.schedule.Execute(Tick).Every(k_TickMs);
            return root;
        }

        void OnDisable()
        {
            // Unity disposes the serialized object after this and may not detach the UI first. Grey it out
            // and unbind it; the null check stops the scheduled refreshes.
            m_Root?.SetEnabled(false);
            m_Root?.Unbind();
            m_Group = null;
        }

        VisualElement BuildActionsCard()
        {
            m_ActionsCard = new InspectorCard("actions", "Actions").LeadingChevron().SetGloss("What happens as someone enters or leaves the area.").Remember(target);

            var builtIn = TriggerVolumeEditor.Group("Built-in Actions", "Common enter/exit setups.");
            builtIn.Add(TriggerVolumeEditor.BuiltInDrawer("Active While Inside",
                "Sets listed GameObjects active while any member is occupied.",
                serializedObject.FindProperty("m_ActiveWhileInside")));
            m_ActionsCard.Add(builtIn);

            var divider = new VisualElement();
            divider.AddToClassList("blocks-tv-divider");
            m_ActionsCard.Add(divider);

            var custom = TriggerVolumeEditor.Group("Custom Actions",
                "Add your own actions. Walking between overlapping members fires neither.");
            custom.Add(TriggerVolumeEditor.EventDrawer(serializedObject.FindProperty("m_OnEnteredAny")));
            custom.Add(TriggerVolumeEditor.EventDrawer(serializedObject.FindProperty("m_OnExitedAll")));
            m_ActionsCard.Add(custom);

            m_ActionsSay = CardRows.SumBar();
            m_ActionsCard.AddFooter(m_ActionsSay);
            return m_ActionsCard;
        }

        /// <summary>
        /// One line counting every action in use ("Active While Inside: 2 GameObjects · Entered Any: 1 call").
        /// The tooltip lists each object and call.
        /// </summary>
        internal static string ActionsLine(TriggerVolumeGroup group, out string tooltip)
        {
            var shown = new List<string>();
            foreach (Object obj in group.ActiveWhileInside)
            {
                if (obj == null) continue;
                shown.Add(TriggerVolumeEditor.TargetName(obj));
            }

            var enter = new List<string>();
            var exit = new List<string>();
            TriggerVolumeEditor.AppendListeners(enter, group.OnEnteredAny);
            TriggerVolumeEditor.AppendListeners(exit, group.OnExitedAll);

            tooltip = string.Empty;
            if (shown.Count == 0 && enter.Count == 0 && exit.Count == 0)
                return "No actions yet. Add a GameObject to Active While Inside, or a call to a custom action.";

            var parts = new List<string>(3);
            if (shown.Count > 0) parts.Add($"Active While Inside: <b>{TriggerVolumeEditor.Count(shown.Count, "GameObject")}</b>");
            if (enter.Count > 0) parts.Add($"Entered Any: <b>{TriggerVolumeEditor.Count(enter.Count, "call")}</b>");
            if (exit.Count > 0) parts.Add($"Exited All: <b>{TriggerVolumeEditor.Count(exit.Count, "call")}</b>");

            var full = new List<string>();
            foreach (string item in shown) full.Add($"While inside: {item}");
            foreach (string item in enter) full.Add($"On entering any member: {TriggerVolumeEditor.Plain(item)}");
            foreach (string item in exit) full.Add($"On leaving all members: {TriggerVolumeEditor.Plain(item)}");
            tooltip = string.Join("\n", full);
            return string.Join("  ·  ", parts);
        }

        void RefreshSummaries()
        {
            if (m_Group == null) return;

            int count = m_Group.Volumes.Count;
            int missing = 0;
            foreach (TriggerVolume volume in m_Group.Volumes) if (volume == null) missing++;

            string members = count == 0 ? "none yet" : count == 1 ? "1 volume" : $"{count} volumes";
            if (missing > 0) members += $" · {missing} empty {(missing == 1 ? "slot" : "slots")}";
            m_VolumesCard.SetSummary(members, count == 0 || missing > 0);

            m_ActionsSay.text = ActionsLine(m_Group, out string tooltip);
            m_ActionsSay.tooltip = tooltip;
        }

        void Tick()
        {
            if (m_Group == null) return;

            string state, title, sub;
            int count = m_Group.Volumes.Count;

            if (!Application.isPlaying)
            {
                state = count == 0 ? "blocked" : string.Empty;
                title = count == 0 ? "No volumes" : count == 1 ? "1 volume" : $"{count} volumes as one";
                sub = count == 0 ? "Add Trigger Volumes to the list, or press Reset to collect the children." : "Anything inside any of them counts as inside the group.";
            }
            else if (!m_Group.isActiveAndEnabled)
            {
                state = "blocked"; title = "Off"; sub = "Disabled: the union is not tracked.";
            }
            else if (m_Group.IsOccupied)
            {
                state = "live"; title = "Occupied"; sub = "Someone is inside at least one member volume.";
            }
            else
            {
                state = "ready"; title = "Empty"; sub = "Waiting for something to enter any member.";
            }

            if (state != m_StripState)
            {
                m_Strip.RemoveFromClassList("blocks-transport--ready");
                m_Strip.RemoveFromClassList("blocks-transport--live");
                m_Strip.RemoveFromClassList("blocks-transport--blocked");
                if (state.Length > 0) m_Strip.AddToClassList("blocks-transport--" + state);
                m_StripState = state;
            }

            if (m_Title.text != title) m_Title.text = title;
            if (m_Sub.text != sub) m_Sub.text = sub;
        }
    }
}
