using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Shared inspector controls for the boids editors: ordered min/max range rows
/// and Animator parameter / state pickers that read the agents' controller.
/// </summary>
public static class BoidsEditorGUI
{
    private const float DashWidth = 14f;

    // -------------------------------------------------------------------------
    //  Min / max range
    // -------------------------------------------------------------------------

    /// <summary>
    /// Draws <paramref name="min"/> and <paramref name="max"/> on one row,
    /// clamped to <paramref name="floor"/> and kept ordered: raising min past max
    /// drags max up with it, and lowering max below min drags min down.
    /// </summary>
    public static void MinMaxRow(Rect rect, GUIContent label, SerializedProperty min, SerializedProperty max, float floor = 0f)
    {
        EditorGUI.BeginProperty(rect, label, min);
        EditorGUI.BeginProperty(rect, label, max);

        Rect fields = EditorGUI.PrefixLabel(rect, GUIUtility.GetControlID(FocusType.Passive), label);
        float fieldWidth = (fields.width - DashWidth) * 0.5f;
        Rect minRect  = new Rect(fields.x, fields.y, fieldWidth, fields.height);
        Rect dashRect = new Rect(minRect.xMax, fields.y, DashWidth, fields.height);
        Rect maxRect  = new Rect(dashRect.xMax, fields.y, fieldWidth, fields.height);

        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = min.hasMultipleDifferentValues;
        float newMin = EditorGUI.FloatField(minRect, min.floatValue);
        if (EditorGUI.EndChangeCheck())
        {
            min.floatValue = Mathf.Max(floor, newMin);
            if (max.floatValue < min.floatValue) max.floatValue = min.floatValue;
        }

        EditorGUI.LabelField(dashRect, "–", EditorStyles.centeredGreyMiniLabel);

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = max.hasMultipleDifferentValues;
        float newMax = EditorGUI.FloatField(maxRect, max.floatValue);
        if (EditorGUI.EndChangeCheck())
        {
            max.floatValue = Mathf.Max(floor, newMax);
            if (min.floatValue > max.floatValue) min.floatValue = max.floatValue;
        }

        EditorGUI.showMixedValue = false;
        EditorGUI.indentLevel = indent;

        EditorGUI.EndProperty();
        EditorGUI.EndProperty();
    }

    public static void MinMaxRow(GUIContent label, SerializedProperty min, SerializedProperty max, float floor = 0f)
    {
        MinMaxRow(EditorGUILayout.GetControlRect(), label, min, max, floor);
    }

    // -------------------------------------------------------------------------
    //  Animator pickers
    // -------------------------------------------------------------------------

    /// <summary>
    /// The AnimatorController used by the first agent that has one (looking
    /// through override controllers), or null.
    /// </summary>
    public static AnimatorController FindController(IList<Transform> agents)
    {
        if (agents == null) return null;

        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i] == null) continue;

            Animator animator = agents[i].GetComponentInChildren<Animator>(true);
            if (animator == null) continue;

            RuntimeAnimatorController rc = animator.runtimeAnimatorController;
            while (rc is AnimatorOverrideController overrideController)
                rc = overrideController.runtimeAnimatorController;

            if (rc is AnimatorController controller) return controller;
        }
        return null;
    }

    /// <summary>Popup of the controller's parameters of <paramref name="type"/> (text field without a controller).</summary>
    public static void ParameterPopup(SerializedProperty prop, AnimatorController controller, AnimatorControllerParameterType type)
    {
        if (controller == null)
        {
            EditorGUILayout.PropertyField(prop);
            return;
        }

        var names = new List<string>();
        foreach (AnimatorControllerParameter p in controller.parameters)
        {
            if (p.type == type) names.Add(p.name);
        }
        NamePopup(prop, names, type + " parameter");
    }

    /// <summary>Popup of every state name in the controller (text field without a controller).</summary>
    public static void StatePopup(SerializedProperty prop, AnimatorController controller)
    {
        if (controller == null)
        {
            EditorGUILayout.PropertyField(prop);
            return;
        }

        var names = new List<string>();
        foreach (AnimatorControllerLayer layer in controller.layers)
            CollectStateNames(layer.stateMachine, names);
        NamePopup(prop, names, "state");
    }

    /// <summary>
    /// Distinct positive values of <paramref name="intParameter"/> that some
    /// transition in the controller tests for with an Equals condition.
    /// </summary>
    public static SortedSet<int> FindEqualsThresholds(AnimatorController controller, string intParameter)
    {
        var values = new SortedSet<int>();
        if (controller == null) return values;

        foreach (AnimatorControllerLayer layer in controller.layers)
            CollectEqualsThresholds(layer.stateMachine, intParameter, values);
        return values;
    }

    // -------------------------------------------------------------------------
    //  Internals
    // -------------------------------------------------------------------------

    private static void NamePopup(SerializedProperty prop, List<string> names, string kind)
    {
        string current = prop.stringValue;
        int index = names.IndexOf(current);

        // Keep a missing value visible (and selected) rather than silently
        // replacing it, so a renamed parameter or state is easy to spot.
        bool missing = index < 0;
        var options = new List<GUIContent>();
        if (missing)
            options.Add(new GUIContent(string.IsNullOrEmpty(current) ? "(none)" : current + " (missing)"));
        foreach (string n in names) options.Add(new GUIContent(n));

        Rect rect = EditorGUILayout.GetControlRect();
        GUIContent label = EditorGUI.BeginProperty(rect, null, prop);

        EditorGUI.BeginChangeCheck();
        int picked = EditorGUI.Popup(rect, label, missing ? 0 : index, options.ToArray());
        if (EditorGUI.EndChangeCheck())
        {
            int nameIndex = missing ? picked - 1 : picked;
            if (nameIndex >= 0) prop.stringValue = names[nameIndex];
        }

        EditorGUI.EndProperty();

        if (missing)
            EditorGUILayout.HelpBox($"The agents' Animator Controller has no {kind} named \"{current}\".", MessageType.Warning);
    }

    private static void CollectStateNames(AnimatorStateMachine sm, List<string> names)
    {
        foreach (ChildAnimatorState s in sm.states)
        {
            if (!names.Contains(s.state.name)) names.Add(s.state.name);
        }
        foreach (ChildAnimatorStateMachine child in sm.stateMachines)
            CollectStateNames(child.stateMachine, names);
    }

    private static void CollectEqualsThresholds(AnimatorStateMachine sm, string parameter, SortedSet<int> values)
    {
        AddEqualsThresholds(sm.anyStateTransitions, parameter, values);
        foreach (ChildAnimatorState s in sm.states)
            AddEqualsThresholds(s.state.transitions, parameter, values);
        foreach (ChildAnimatorStateMachine child in sm.stateMachines)
            CollectEqualsThresholds(child.stateMachine, parameter, values);
    }

    private static void AddEqualsThresholds(AnimatorStateTransition[] transitions, string parameter, SortedSet<int> values)
    {
        foreach (AnimatorStateTransition t in transitions)
        {
            foreach (AnimatorCondition c in t.conditions)
            {
                if (c.parameter == parameter && c.mode == AnimatorConditionMode.Equals && c.threshold > 0f)
                    values.Add(Mathf.RoundToInt(c.threshold));
            }
        }
    }
}
