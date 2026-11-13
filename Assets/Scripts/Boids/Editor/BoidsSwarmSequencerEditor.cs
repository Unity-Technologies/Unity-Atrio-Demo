using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Custom editor for <see cref="BoidsSwarmSequencer"/>.
///
/// Inspector: each sequence is a reorderable list of collapsible events.  An
/// event's retarget interval is a toggle plus an ordered min/max range, and the
/// last event of each sequence shows how it is treated (the wake sequence's
/// runs until stopped; the rest sequence's applies entry changes only) instead
/// of the settings it ignores.  New events get usable defaults.
///
/// Scene view: draws each expanded event's Random Target Area as a scalable,
/// movable — but never rotatable — box handle in world space, using
/// <see cref="BoxBoundsHandle"/>.
/// </summary>
[CustomEditor(typeof(BoidsSwarmSequencer))]
public class BoidsSwarmSequencerEditor : Editor
{
    private enum LastEvent { Holds, RunsUntilStopped, EntryOnly }

    private static readonly GUIContent RetargetLabel = new GUIContent(
        "Retarget",
        "Move the target to a new random point in the area on a random interval while the event runs.  Off = placed once on entry only.");

    private static readonly GUIContent IntervalLabel = new GUIContent(
        "Interval",
        "Random seconds between retargets (min – max).");

    private static readonly GUIContent DurationLabel = new GUIContent(
        "Duration",
        "How long (seconds) to remain in this event before advancing to the next.");

    private ReorderableList _wakeList;
    private ReorderableList _restList;

    // -------------------------------------------------------------------------
    //  Inspector
    // -------------------------------------------------------------------------

    private void OnEnable()
    {
        _wakeList = CreateList("wakeSequence", LastEvent.RunsUntilStopped);
        _restList = CreateList("restSequence", LastEvent.EntryOnly);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var sequencer = (BoidsSwarmSequencer)target;

        if (Application.isPlaying) DrawRuntime(sequencer);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("swarm"));
        if (sequencer.swarm == null)
            EditorGUILayout.HelpBox("Assign a swarm to drive.", MessageType.Warning);
        else if (sequencer.swarm.target == null)
            EditorGUILayout.HelpBox("The swarm has no Target, so events can't move it.", MessageType.Warning);

        EditorGUILayout.Space();
        DrawList(_wakeList);
        DrawList(_restList);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Hotkey", EditorStyles.boldLabel);
        SerializedProperty enabled = serializedObject.FindProperty("enableToggleHotkey");
        EditorGUILayout.PropertyField(enabled, new GUIContent("Toggle Hotkey", enabled.tooltip));
        if (enabled.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleKey"), new GUIContent("Key"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("requireShift"));
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    public override bool RequiresConstantRepaint() => Application.isPlaying;

    private static void DrawRuntime(BoidsSwarmSequencer sequencer)
    {
        EditorGUILayout.LabelField("Sequence", sequencer.IsPlaying ? "Playing" : "Stopped", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Play Wake")) sequencer.PlayWakeSequence();
            if (GUILayout.Button("Play Rest")) sequencer.PlayRestSequence();
            using (new EditorGUI.DisabledScope(!sequencer.IsPlaying))
            {
                if (GUILayout.Button("Stop")) sequencer.StopSequence();
            }
        }
        EditorGUILayout.Space();
    }

    /// <summary>
    /// Foldout + list.  The foldout is the list property's own isExpanded, which
    /// the scene view also uses to show or hide that sequence's handles.
    /// </summary>
    private static void DrawList(ReorderableList list)
    {
        SerializedProperty prop = list.serializedProperty;
        prop.isExpanded = EditorGUILayout.Foldout(prop.isExpanded,
            $"{prop.displayName}  ({prop.arraySize})", true, EditorStyles.foldoutHeader);
        if (prop.isExpanded)
            list.DoLayoutList();
    }

    private ReorderableList CreateList(string propertyName, LastEvent lastMode)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        var list = new ReorderableList(serializedObject, prop, true, false, true, true);

        list.elementHeightCallback = i =>
            EventHeight(prop.GetArrayElementAtIndex(i), LastMode(prop, i, lastMode));

        list.drawElementCallback = (rect, i, active, focused) =>
        {
            rect.y += 2f;
            rect.xMin += 10f; // room for the foldout arrow beside the drag handle
            DrawEvent(rect, prop.GetArrayElementAtIndex(i), i, LastMode(prop, i, lastMode));
        };

        list.onAddCallback = l =>
        {
            int index = prop.arraySize;
            prop.arraySize++; // duplicates the previous event, if there is one
            SerializedProperty evt = prop.GetArrayElementAtIndex(index);
            if (index == 0) ResetEvent(evt);
            evt.isExpanded = true;
            l.index = index;
        };

        return list;
    }

    private static LastEvent LastMode(SerializedProperty list, int index, LastEvent lastMode)
    {
        return index == list.arraySize - 1 ? lastMode : LastEvent.Holds;
    }

    // -------------------------------------------------------------------------
    //  Event drawing
    // -------------------------------------------------------------------------

    private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

    private static float EventHeight(SerializedProperty evt, LastEvent mode)
    {
        float h = Line + 4f; // foldout
        if (!evt.isExpanded) return h;

        h += Line * 3; // name, state change, steer multiplier
        h += EditorGUI.GetPropertyHeight(evt.FindPropertyRelative("randomTargetArea")) + EditorGUIUtility.standardVerticalSpacing;

        if (mode == LastEvent.EntryOnly)
            return h + Line * 2; // note

        h += Line; // duration (or "runs until stopped")
        h += Line; // retarget toggle
        if (evt.FindPropertyRelative("targetUpdateIntervalMax").floatValue > 0f)
            h += Line; // interval
        return h;
    }

    private static void DrawEvent(Rect rect, SerializedProperty evt, int index, LastEvent mode)
    {
        SerializedProperty name  = evt.FindPropertyRelative("name");
        SerializedProperty state = evt.FindPropertyRelative("stateChange");

        Rect row = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
        string stateTag = state.enumValueIndex == 0 ? "" : $"  [{state.enumDisplayNames[state.enumValueIndex]}]";
        evt.isExpanded = EditorGUI.Foldout(row, evt.isExpanded, $"{index}: {name.stringValue}{stateTag}", true);
        if (!evt.isExpanded) return;

        row.y += Line;
        EditorGUI.PropertyField(row, name);
        row.y += Line;
        EditorGUI.PropertyField(row, state);

        row.y += Line;
        switch (mode)
        {
            case LastEvent.RunsUntilStopped:
                EditorGUI.LabelField(row, DurationLabel, new GUIContent("Runs until stopped (last wake event)"));
                break;
            case LastEvent.Holds:
                EditorGUI.PropertyField(row, evt.FindPropertyRelative("eventDuration"), DurationLabel);
                break;
            case LastEvent.EntryOnly:
                row.y -= Line; // no duration row
                break;
        }

        row.y += Line;
        EditorGUI.PropertyField(row, evt.FindPropertyRelative("maxSteerMultiplier"));

        SerializedProperty area = evt.FindPropertyRelative("randomTargetArea");
        row.y += Line;
        Rect areaRect = new Rect(row.x, row.y, row.width, EditorGUI.GetPropertyHeight(area));
        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(areaRect, area, true);
        if (EditorGUI.EndChangeCheck())
        {
            // Bounds fields accept negative sizes; keep the area valid.
            Bounds b = area.boundsValue;
            b.size = new Vector3(Mathf.Abs(b.size.x), Mathf.Abs(b.size.y), Mathf.Abs(b.size.z));
            area.boundsValue = b;
        }
        row.y += areaRect.height + EditorGUIUtility.standardVerticalSpacing;

        if (mode == LastEvent.EntryOnly)
        {
            Rect note = new Rect(row.x, row.y, row.width, Line * 2 - EditorGUIUtility.standardVerticalSpacing);
            EditorGUI.HelpBox(note, "Last rest event: applies its state change, steer and target placement, then the sequence ends.", MessageType.None);
            return;
        }

        SerializedProperty min = evt.FindPropertyRelative("targetUpdateIntervalMin");
        SerializedProperty max = evt.FindPropertyRelative("targetUpdateIntervalMax");

        // Retargeting is "on" whenever max > 0; the toggle sets a usable range or clears it.
        EditorGUI.BeginChangeCheck();
        bool retarget = EditorGUI.Toggle(row, RetargetLabel, max.floatValue > 0f);
        if (EditorGUI.EndChangeCheck())
        {
            if (retarget)
            {
                float start = min.floatValue > 0f ? min.floatValue : 1f;
                min.floatValue = start;
                max.floatValue = start;
            }
            else
            {
                min.floatValue = 0f;
                max.floatValue = 0f;
            }
        }

        if (max.floatValue > 0f)
        {
            row.y += Line;
            EditorGUI.indentLevel++;
            BoidsEditorGUI.MinMaxRow(row, IntervalLabel, min, max);
            EditorGUI.indentLevel--;
        }
    }

    /// <summary>
    /// Field defaults for the first event added to an empty list (Unity would
    /// otherwise zero it, leaving no steering and no target area).  The area
    /// starts around the swarm's target, or this object, so its handle is in view.
    /// </summary>
    private void ResetEvent(SerializedProperty evt)
    {
        var sequencer = (BoidsSwarmSequencer)target;
        Vector3 centre = sequencer.swarm != null && sequencer.swarm.target != null
            ? sequencer.swarm.target.position
            : sequencer.transform.position;

        evt.FindPropertyRelative("name").stringValue                    = "Event";
        evt.FindPropertyRelative("stateChange").enumValueIndex          = 0;
        evt.FindPropertyRelative("eventDuration").floatValue            = 3f;
        evt.FindPropertyRelative("maxSteerMultiplier").floatValue       = 1f;
        evt.FindPropertyRelative("randomTargetArea").boundsValue        = new Bounds(centre, Vector3.one * 5f);
        evt.FindPropertyRelative("targetUpdateIntervalMin").floatValue  = 1f;
        evt.FindPropertyRelative("targetUpdateIntervalMax").floatValue  = 1f;
    }

    // -------------------------------------------------------------------------
    //  Scene view
    // -------------------------------------------------------------------------

    // Reused handles (one per drawn box, grown on demand).  BoxBoundsHandle is
    // axis-aligned and offers no rotation controls, satisfying the "cannot be
    // rotated" requirement.
    private readonly List<BoxBoundsHandle> _handles = new List<BoxBoundsHandle>();
    private int _handleCursor;

    private static readonly Color WakeColor = new Color(0.3f, 0.8f, 1f, 1f);
    private static readonly Color RestColor = new Color(1f, 0.8f, 0.3f, 1f);

    private void OnSceneGUI()
    {
        serializedObject.Update();

        // Ensure boxes are edited in world space (identity matrix = no rotation).
        Matrix4x4 prev = Handles.matrix;
        Handles.matrix = Matrix4x4.identity;

        _handleCursor = 0;
        DrawSequenceHandles("wakeSequence", "Wake", WakeColor);
        DrawSequenceHandles("restSequence", "Rest", RestColor);

        Handles.matrix = prev;

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSequenceHandles(string listPropertyName, string label, Color color)
    {
        SerializedProperty list = serializedObject.FindProperty(listPropertyName);
        if (list == null || !list.isArray) return;

        // Hide all of this sequence's handles when the sequence list foldout
        // itself is collapsed in the inspector.
        if (!list.isExpanded) return;

        for (int i = 0; i < list.arraySize; i++)
        {
            SerializedProperty evt  = list.GetArrayElementAtIndex(i);
            if (evt == null) continue;

            // Only show the bounds handle when this event's foldout is expanded
            // (its details are visible) in the inspector.
            if (!evt.isExpanded) continue;

            SerializedProperty area = evt.FindPropertyRelative("randomTargetArea");
            SerializedProperty nameProp = evt.FindPropertyRelative("name");
            if (area == null) continue;

            Bounds b = area.boundsValue;

            BoxBoundsHandle handle = NextHandle();
            handle.center = b.center;
            handle.size   = b.size;
            handle.wireframeColor = color;
            handle.handleColor    = color;

            EditorGUI.BeginChangeCheck();
            handle.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Edit Random Target Area");
                b.center = handle.center;
                b.size   = handle.size;
                area.boundsValue = b;
            }

            // Label at the box centre.
            string evtName = nameProp != null ? nameProp.stringValue : string.Empty;
            Handles.color = color;
            Handles.Label(
                b.center + Vector3.up * (b.extents.y + 0.25f),
                $"{label} [{i}] {evtName}");
        }
    }

    private BoxBoundsHandle NextHandle()
    {
        while (_handleCursor >= _handles.Count)
            _handles.Add(new BoxBoundsHandle());
        return _handles[_handleCursor++];
    }
}
