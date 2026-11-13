using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Custom inspector for <see cref="BoidsSwarm"/>: collapsible sections with
/// sub-groups, ordered min/max range rows, Animator parameter and state pickers
/// read from the agents' controller, and live state + controls in Play mode.
/// </summary>
[CustomEditor(typeof(BoidsSwarm))]
public class BoidsSwarmEditor : Editor
{
    private static readonly GUIContent DescentSpeedLabel = new GUIContent(
        "Descent Speed",
        "Descent speed (units/s): slows from the right value at the landing gate to the left value just above the resting point.");

    private static readonly GUIContent FlourishIntervalLabel = new GUIContent(
        "Flourish Interval",
        "Random seconds of idle loop between flourishes (min – max).");

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var swarm = (BoidsSwarm)target;

        if (Application.isPlaying) DrawRuntime(swarm);

        if (Section("Agents", true))
        {
            Prop("target");
            Prop("agents", true);

            if (swarm.target == null)
                EditorGUILayout.HelpBox("No Target: following agents have nothing to seek.", MessageType.Warning);

            int empty = CountEmptySlots(swarm.agents);
            if (empty > 0)
                EditorGUILayout.HelpBox($"{empty} empty agent slot(s); they are skipped.", MessageType.Info);
        }
        EndSection();

        if (Section("Flocking", true))
        {
            SubHeader("Separation");
            Prop("separationWeight", label: "Weight");
            Prop("separationRadius", label: "Radius");

            SubHeader("Alignment & Cohesion");
            Prop("alignmentWeight");
            Prop("cohesionWeight");
            Prop("neighborRadius");

            SubHeader("Target");
            Prop("targetWeight", label: "Weight");
            Prop("orbitRadius");
        }
        EndSection();

        if (Section("Flight", true))
        {
            Prop("maxSpeed");
            Prop("maxSteer");
            Prop("turnSpeed");

            SubHeader("Banking");
            SerializedProperty bankStrength = Prop("bankStrength", label: "Strength");
            using (new EditorGUI.DisabledScope(bankStrength.floatValue <= 0f))
            {
                Prop("maxBankAngle");
                Prop("bankSmoothing", label: "Smoothing");
                Prop("worldUpBias");
            }
        }
        EndSection();

        if (Section("Return & Landing", true))
        {
            SubHeader("Approach");
            Prop("returnSlowRadius", label: "Slow Radius");
            Prop("swingWidth");
            Prop("restTurnBoost", label: "Turn Boost");

            SubHeader("Landing Gate");
            Prop("landingApproachHeight", label: "Height");
            Prop("landingApproachDistance", label: "Distance Behind");
            Prop("descentEntryRadius", label: "Entry Radius");

            SubHeader("Descent & Landing");
            BoidsEditorGUI.MinMaxRow(DescentSpeedLabel,
                serializedObject.FindProperty("descentMinSpeed"),
                serializedObject.FindProperty("descentSpeed"), 0.01f);
            Prop("arriveRadius");
        }
        EndSection();

        if (Section("Animation", true))
        {
            AnimatorController controller = BoidsEditorGUI.FindController(swarm.agents);
            if (controller == null)
                EditorGUILayout.HelpBox("No Animator Controller found on the agents, so names can't be checked.", MessageType.Info);

            SubHeader("Parameters");
            BoidsEditorGUI.ParameterPopup(serializedObject.FindProperty("flyingParameter"), controller, AnimatorControllerParameterType.Bool);
            BoidsEditorGUI.ParameterPopup(serializedObject.FindProperty("flappingParameter"), controller, AnimatorControllerParameterType.Bool);
            BoidsEditorGUI.ParameterPopup(serializedObject.FindProperty("aboutToLandParameter"), controller, AnimatorControllerParameterType.Bool);

            SubHeader("Flapping");
            Prop("takeOffFlapDuration", label: "Take-off Duration");
            Prop("steepClimbAngle");
            Prop("steepClimbMinSpeed");
            Prop("steepClimbFlapDuration", label: "Steep Climb Duration");
            Prop("landingFlapDistance");
            Prop("landingFlapStopTime");
            Prop("landingFlyingStopTime");
            Prop("landingTriggerLeadTime", label: "About To Land Lead Time");

            SubHeader("Idle");
            BoidsEditorGUI.StatePopup(serializedObject.FindProperty("idleLoopState"), controller);
            SerializedProperty indexParam = serializedObject.FindProperty("idleIndexParameter");
            BoidsEditorGUI.ParameterPopup(indexParam, controller, AnimatorControllerParameterType.Int);
            SerializedProperty count = Prop("idleFlourishCount");
            using (new EditorGUI.DisabledScope(count.intValue <= 0))
            {
                BoidsEditorGUI.MinMaxRow(FlourishIntervalLabel,
                    serializedObject.FindProperty("idleFlourishIntervalMin"),
                    serializedObject.FindProperty("idleFlourishIntervalMax"));
            }
            DrawFlourishCountCheck(controller, indexParam.stringValue, count);
        }
        EndSection();

        serializedObject.ApplyModifiedProperties();
    }

    // -------------------------------------------------------------------------
    //  Sections
    // -------------------------------------------------------------------------

    private void DrawRuntime(BoidsSwarm swarm)
    {
        EditorGUILayout.LabelField("State", swarm.State.ToString(), EditorStyles.boldLabel);
        if (!Mathf.Approximately(swarm.MaxSteerMultiplier, 1f))
            EditorGUILayout.LabelField("Effective Max Steer", $"{swarm.EffectiveMaxSteer:0.##}  (× {swarm.MaxSteerMultiplier:0.##})");

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Wake"))         swarm.WakeSwarm();
            if (GUILayout.Button("Rest"))         swarm.RestSwarm();
            if (GUILayout.Button("Capture Rest")) swarm.CaptureRestState();
        }
        EditorGUILayout.Space();
    }

    // Keep the live state current in Play mode.
    public override bool RequiresConstantRepaint() => Application.isPlaying;

    /// <summary>
    /// Warns when the flourish count doesn't match the IdleIndex values the
    /// controller actually transitions on, and offers a one-click fix.
    /// </summary>
    private static void DrawFlourishCountCheck(AnimatorController controller, string indexParam, SerializedProperty count)
    {
        if (controller == null) return;

        SortedSet<int> values = BoidsEditorGUI.FindEqualsThresholds(controller, indexParam);
        if (values.Count == 0 || (values.Count == count.intValue && values.Max == count.intValue)) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.HelpBox(
                $"The controller has flourishes for {indexParam} = {string.Join(", ", values)}, but Idle Flourish Count is {count.intValue}.",
                MessageType.Warning);
            if (GUILayout.Button($"Use {values.Max}", GUILayout.Width(60f), GUILayout.ExpandHeight(true)))
                count.intValue = values.Max;
        }
    }

    // -------------------------------------------------------------------------
    //  Layout helpers
    // -------------------------------------------------------------------------

    // Plain foldouts in the header style: BeginFoldoutHeaderGroup can't contain
    // arrays (the agents list), which Unity reports as nested header groups.
    private static bool Section(string title, bool defaultOpen)
    {
        string key = "BoidsSwarmEditor." + title;
        EditorGUILayout.Space(2f);
        bool open = EditorGUILayout.Foldout(SessionState.GetBool(key, defaultOpen), title, true, EditorStyles.foldoutHeader);
        SessionState.SetBool(key, open);
        if (open) EditorGUI.indentLevel++;
        return open;
    }

    private static void EndSection()
    {
        EditorGUI.indentLevel = 0; // only raised for open sections
    }

    private static void SubHeader(string title)
    {
        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
    }

    private SerializedProperty Prop(string name, bool includeChildren = false, string label = null)
    {
        SerializedProperty prop = serializedObject.FindProperty(name);
        if (label == null)
            EditorGUILayout.PropertyField(prop, includeChildren);
        else
            EditorGUILayout.PropertyField(prop, new GUIContent(label, prop.tooltip), includeChildren);
        return prop;
    }

    private static int CountEmptySlots(Transform[] agents)
    {
        if (agents == null) return 0;
        int empty = 0;
        foreach (Transform a in agents)
        {
            if (a == null) empty++;
        }
        return empty;
    }
}
