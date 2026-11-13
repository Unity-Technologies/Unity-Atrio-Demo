using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// An interactable's step groups, drawn from a serialized <c>List&lt;StepGroup&gt;</c>. Each group is a
    /// named box of step rows.
    /// </summary>
    /// <remarks>
    /// Drag rows by the handle to reorder, regroup or split; drag a group by the handle on its header to
    /// move it with its steps; click a row to open settings; ⋮ or right-click to duplicate, skip or delete.
    /// A group with no steps shows a hint and is skipped when it plays. In Play mode rows fill in as the
    /// run passes them.
    /// Edits go through the given <see cref="SerializedProperty"/> as one Undo step. The add menu lists
    /// every <see cref="InteractionStep"/> outside test assemblies that is public, top-level, concrete,
    /// not generic, not obsolete and has a public parameterless constructor. Each is named by its
    /// <see cref="InteractionStepAttribute"/> or class name minus <c>Step</c>; shipped steps come first.
    /// </remarks>
    public sealed class StepListView : VisualElement
    {
        const float k_DragThreshold = 4f;
        const long k_ChangePollMs = 500;
        const long k_LivePollMs = 50;
        const int k_SpinnerFrames = 12;
        const int k_EnterDelayMs = 20;

        /// <summary>A step row: where it lives and what draws it.</summary>
        sealed class Row
        {
            public VisualElement Root;
            public VisualElement Line;
            public VisualElement Body;
            public Image NodeIcon;
            public Label NodeLabel;
            public VisualElement Progress;
            public InteractionStep Step;
            public SerializedProperty Property;
            public int Group;
            public int Index;
            public StepState State = StepState.None;
        }

        /// <summary>A group: its box, its name field and its rows.</summary>
        sealed class GroupView
        {
            public VisualElement Root;
            public VisualElement Rows;
            public TextField Name;
            public int Index;
            public readonly List<Row> Steps = new List<Row>();
        }

        /// <summary>Where a dragged row would land: inside a group, or as a new group before one.</summary>
        struct DropTarget
        {
            public int Group;
            public int Index;
            public bool NewGroup;
        }

        readonly SerializedProperty m_Groups;
        readonly SerializedObject m_Object;
        readonly Interactable m_Interactable;
        readonly Action m_OnChanged;
        readonly string m_EmptyText;
        readonly VisualElement m_GroupHost;
        readonly List<GroupView> m_GroupViews = new List<GroupView>();
        readonly Texture2D[] m_Spinner = new Texture2D[k_SpinnerFrames];
        readonly HashSet<string> m_Open = new HashSet<string>();

        int m_LastHash;
        int m_LiveTick;
        bool m_Released;
        Type m_JustAdded;
        int m_FocusGroup = -1;

        // Drag state: a row (m_Pressed) or a whole group (m_PressedGroup), never both.
        Row m_Pressed;
        GroupView m_PressedGroup;
        int m_GroupSlot;
        bool m_Dragging;
        int m_PointerId = -1;
        Vector2 m_PressPosition;
        float m_DragOriginTop;
        float m_DragOriginLeft;
        DropTarget m_DropTarget;
        VisualElement m_DropLine;
        VisualElement m_Spacer;

        /// <summary>
        /// Creates the list over <paramref name="groups"/> on <paramref name="interactable"/>.
        /// </summary>
        /// <param name="groups">The serialized group list to draw and edit.</param>
        /// <param name="interactable">The interactable that owns it; read for live state and warnings.</param>
        /// <param name="emptyText">Shown when there are no groups.</param>
        /// <param name="onChanged">Called after any change made from here.</param>
        public StepListView(SerializedProperty groups, Interactable interactable, string emptyText, Action onChanged = null)
        {
            m_Groups = groups;
            m_Object = groups?.serializedObject;
            m_Interactable = interactable;
            m_EmptyText = emptyText;
            m_OnChanged = onChanged;

            AddToClassList("blocks-steps");
            AddToClassList("blocks-groups");
            AddToClassList("blocks-beats");

            m_GroupHost = new VisualElement();
            m_GroupHost.AddToClassList("blocks-groups__host");
            Add(m_GroupHost);

            var addStep = new Button { text = "+ Add step", tooltip = "A new group at the end, with the step in it, or an empty group to name now and fill later. Groups run top to bottom; drag a step into another group to run it alongside those steps, or between groups to give it its own." };
            addStep.AddToClassList("blocks-button--quiet");
            addStep.AddToClassList("blocks-groups__add");
            addStep.clicked += () => ShowAddMenu(addStep, -1);
            Add(addStep);

            if (m_Groups == null || m_Object == null)
            {
                var hint = new Label("(Multi-object editing is not supported)");
                hint.AddToClassList("blocks-text--dim");
                m_GroupHost.Add(hint);
                addStep.SetEnabled(false);
                return;
            }

            for (int i = 0; i < k_SpinnerFrames; i++)
                m_Spinner[i] = EditorIcons.Find($"WaitSpin{i:00}");

            // The list holds the pointer capture: the dragged row leaves the hierarchy and would lose it.
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);

            Undo.undoRedoPerformed += OnUndoRedo;
            RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);

            schedule.Execute(RebuildIfChanged).Every(k_ChangePollMs);
            schedule.Execute(UpdateLiveState).Every(k_LivePollMs);

            Rebuild();
        }

        /// <summary>
        /// Stops the list listening, refreshing and editing, and greys it out. Call it from the owning editor's
        /// <c>OnDisable</c>: Unity disposes the serialized object there, and may not detach the list first.
        /// </summary>
        internal void Release()
        {
            m_Released = true;
            CancelPress();
            Undo.undoRedoPerformed -= OnUndoRedo;
            SetEnabled(false);
            this.Unbind();
        }

        /// <summary>
        /// The name a step type shows in rows and menus: its <see cref="InteractionStepAttribute"/>, or
        /// the class name minus <c>Step</c>, spaced out.
        /// </summary>
        public static string DisplayName(Type type)
        {
            if (type == null) return "(missing)";

            var attribute = (InteractionStepAttribute)Attribute.GetCustomAttribute(type, typeof(InteractionStepAttribute), false);
            if (attribute != null && !string.IsNullOrEmpty(attribute.DisplayName)) return attribute.DisplayName;

            string name = type.Name;
            if (name.EndsWith("Step", StringComparison.Ordinal) && name.Length > 4) name = name.Substring(0, name.Length - 4);
            return ObjectNames.NicifyVariableName(name);
        }

        /// <summary>A 16 px editor icon for a step type; a script icon for a project's own.</summary>
        public static Texture2D Glyph(Type type)
        {
            if (type == typeof(LockPlayerStep)) return EditorIcons.Find("LockIcon-On", "LockIcon");
            if (type == typeof(WaitStep)) return EditorIcons.Find("WaitSpin00");
            if (type == typeof(MoveToPoseStep)) return EditorIcons.Find("MoveTool");
            if (type == typeof(LookAtStep)) return EditorIcons.Find("ViewToolOrbit");
            if (type == typeof(PlayAnimationStep)) return EditorIcons.Find("Animation.Play");
            if (type == typeof(PlayTimelineStep)) return EditorIcons.Find("UnityEditor.Timeline.TimelineWindow");
            if (type == typeof(SetAnimatorParameterStep)) return EditorIcons.Find("UnityEditor.AnimationWindow");
            if (type == typeof(SetActiveStep)) return EditorIcons.Find("ToggleUVOverlay");
            if (type == typeof(ParentToStep)) return EditorIcons.Find("UnityEditor.HierarchyWindow");
            if (type == typeof(ReleaseControlStep)) return EditorIcons.Find("LockIcon");
            if (type == typeof(UnityEventStep)) return EditorIcons.Find("Animation.EventMarker");
            return EditorIcons.Find("cs Script Icon");
        }

        /// <summary>Sort key for the add menu: shipped steps in a fixed order, then everything else.</summary>
        public static int Order(Type type)
        {
            if (type == typeof(LockPlayerStep)) return 0;
            if (type == typeof(WaitStep)) return 1;
            if (type == typeof(MoveToPoseStep)) return 2;
            if (type == typeof(LookAtStep)) return 3;
            if (type == typeof(PlayAnimationStep)) return 4;
            if (type == typeof(PlayTimelineStep)) return 5;
            if (type == typeof(SetAnimatorParameterStep)) return 6;
            if (type == typeof(SetActiveStep)) return 7;
            if (type == typeof(ParentToStep)) return 8;
            if (type == typeof(ReleaseControlStep)) return 9;
            if (type == typeof(UnityEventStep)) return 10;
            return 100;
        }

        /// <summary>Whether <paramref name="type"/> ships with the package rather than with the project.</summary>
        public static bool IsShipped(Type type) => type != null && type.Assembly == typeof(InteractionStep).Assembly;

        /// <summary>
        /// Every step type the add menu offers: concrete, public, parameterless constructor, non-test
        /// assembly. Shipped first, then the project's, each sorted by name.
        /// </summary>
        public static List<Type> OfferableTypes()
        {
            var types = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<InteractionStep>())
            {
                if (IsOfferable(type)) types.Add(type);
            }

            types.Sort((a, b) =>
            {
                int shipped = IsShipped(b).CompareTo(IsShipped(a));
                if (shipped != 0) return shipped;
                int order = Order(a).CompareTo(Order(b));
                return order != 0 ? order : string.CompareOrdinal(DisplayName(a), DisplayName(b));
            });

            return types;
        }

        static bool IsOfferable(Type type)
        {
            if (type == null || type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) return false;
            if (!type.IsVisible || type.IsNested) return false;
            if (type.GetConstructor(Type.EmptyTypes) == null) return false;
            if (type.IsDefined(typeof(ObsoleteAttribute), inherit: false)) return false;

            string assembly = type.Assembly.GetName().Name ?? string.Empty;
            return !assembly.EndsWith("Tests", StringComparison.Ordinal) &&
                   !assembly.Contains(".Tests.") &&
                   !assembly.EndsWith(".TestFramework", StringComparison.Ordinal);
        }

        /// <summary>
        /// One line about a step's settings, or <c>null</c>. <paramref name="warn"/> marks it unfinished.
        /// <paramref name="interactable"/> resolves defaults and may be <c>null</c>.
        /// </summary>
        public static string Summarize(InteractionStep step, Interactable interactable, out bool warn)
        {
            string summary = Settings(step, interactable, out warn);
            if (step == null || step.Delay <= 0f) return summary;

            string timing = $"after {step.Delay:0.##} s";
            return string.IsNullOrEmpty(summary) ? timing : $"{timing} · {summary}";
        }

        static string Settings(InteractionStep step, Interactable interactable, out bool warn)
        {
            warn = false;

            switch (step)
            {
                case LockPlayerStep lockPlayer:
                    return lockPlayer.Lock == PlayerLockScope.MovementOnly
                        ? "no walking until the run ends, or a Release Control · looking stays free"
                        : "no input until the run ends, or a Release Control";

                case WaitStep wait:
                    return $"{wait.Seconds:0.##} s";

                case MoveToPoseStep pose:
                {
                    if (pose.Target == null)
                    {
                        warn = true;
                        return "No target yet. Drop in the Transform to stand at";
                    }

                    // Only the departures from the default are worth the row's width.
                    string keeps = pose.TurnBody && pose.TiltView ? ""
                        : pose.TurnBody ? " · keeps view tilt"
                        : pose.TiltView ? " · keeps facing"
                        : " · keeps facing and tilt";
                    return pose.Duration <= 0f
                        ? $"snaps to {pose.Target.name}{keeps}"
                        : $"to {pose.Target.name} · {pose.Duration:0.##} s{keeps}";
                }

                case LookAtStep look:
                {
                    if (look.Target == null)
                    {
                        warn = true;
                        return "No target yet. Drop in what to look at";
                    }

                    string how = look.TurnBody && look.TiltView ? "body and view"
                        : look.TurnBody ? "body only"
                        : look.TiltView ? "view only" : "nothing to turn";
                    warn = !look.TurnBody && !look.TiltView;
                    return look.Duration <= 0f
                        ? $"snaps to {look.Target.name} · {how}"
                        : $"at {look.Target.name} · {look.Duration:0.##} s · {how}";
                }

                case PlayAnimationStep animation:
                {
                    if (animation.Clip == null)
                    {
                        warn = true;
                        return "No clip yet";
                    }

                    int bound = 0;
                    foreach (Animator animator in animation.Animators)
                    {
                        if (animator != null) bound++;
                    }

                    if (bound == 0)
                    {
                        warn = true;
                        return $"{animation.Clip.name} · no Animators yet";
                    }

                    string text = $"{animation.Clip.name} · {animation.Duration:0.#} s · {bound} {(bound == 1 ? "animator" : "animators")}";
                    if (Math.Abs(animation.Speed - 1f) > 0.001f) text += $" · ×{animation.Speed:0.##}";
                    if (animation.BlendIn > 0f || animation.BlendOut > 0f) text += " · fades";
                    if (animation.PointOfView != null) text += $" · view from {animation.PointOfView.name}";
                    return text;
                }

                case PlayTimelineStep timeline:
                {
                    PlayableDirector director = timeline.Director != null
                        ? timeline.Director
                        : interactable != null ? interactable.GetComponent<PlayableDirector>() : null;
                    if (director == null)
                    {
                        warn = true;
                        return "No Playable Director yet";
                    }

                    string what = director.playableAsset != null ? director.playableAsset.name : director.name;
                    return timeline.WaitUntilFinished ? $"{what} · waits for it" : $"{what} · fires and moves on";
                }

                case SetAnimatorParameterStep parameter:
                {
                    if (string.IsNullOrEmpty(parameter.Parameter))
                    {
                        warn = true;
                        return "No parameter yet";
                    }

                    string on = parameter.Animator != null ? parameter.Animator.name : "the player";
                    switch (parameter.Kind)
                    {
                        case AnimatorParameterKind.Trigger: return $"fires {parameter.Parameter} on {on}";
                        case AnimatorParameterKind.Bool: return $"{parameter.Parameter} = {(parameter.BoolValue ? "true" : "false")} on {on}";
                        case AnimatorParameterKind.Int: return Eased($"{parameter.Parameter} = {parameter.IntValue} on {on}", parameter.Duration);
                        default: return Eased($"{parameter.Parameter} = {parameter.FloatValue:0.##} on {on}", parameter.Duration);
                    }
                }

                case SetActiveStep active:
                {
                    int count = 0;
                    string first = null;
                    foreach (UnityEngine.Object target in active.Targets)
                    {
                        if (target == null) continue;
                        count++;
                        first = first ?? target.name;
                    }

                    if (count == 0)
                    {
                        warn = true;
                        return "Nothing to switch yet";
                    }

                    string verb = active.Mode == SetActiveMode.On ? "on" : active.Mode == SetActiveMode.Off ? "off" : "toggled";
                    string what = count == 1 ? first : $"{count} things";
                    return active.RevertWhenRunEnds ? $"{what} {verb} · back when the run ends" : $"{what} {verb}";
                }

                case ParentToStep parent:
                {
                    if (parent.Target == null)
                    {
                        warn = true;
                        return "No object to move yet";
                    }

                    string where = parent.NewParent != null ? parent.NewParent.name : "the scene root";
                    return parent.Placement == ParentToPlacement.SnapToParent
                        ? $"{parent.Target.name} onto {where}"
                        : $"{parent.Target.name} under {where}, where it stands";
                }

                case ReleaseControlStep _:
                {
                    if (interactable != null && !interactable.WouldLockPlayer)
                    {
                        warn = true;
                        return "No Lock Player step above, so there is nothing to release";
                    }

                    return "control back to the player";
                }

                case UnityEventStep unityEvent:
                {
                    int listeners = unityEvent.OnApplied.GetPersistentEventCount();
                    if (listeners == 0)
                    {
                        warn = true;
                        return "Nothing wired yet";
                    }

                    return listeners == 1 ? "1 call" : $"{listeners} calls";
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// The word that tells two steps of the same kind apart (clip, target, parameter), or <c>null</c>.
        /// </summary>
        public static string Noun(InteractionStep step, Interactable interactable)
        {
            switch (step)
            {
                case PlayAnimationStep animation: return animation.Clip != null ? animation.Clip.name : null;
                case MoveToPoseStep pose: return pose.Target != null ? pose.Target.name : null;
                case LookAtStep look: return look.Target != null ? look.Target.name : null;
                case WaitStep wait: return $"{wait.Seconds:0.##} s";
                case SetAnimatorParameterStep parameter: return string.IsNullOrEmpty(parameter.Parameter) ? null : parameter.Parameter;
                case ParentToStep parent: return parent.Target != null ? parent.Target.name : null;
                case PlayTimelineStep timeline:
                {
                    PlayableDirector director = timeline.Director != null
                        ? timeline.Director
                        : interactable != null ? interactable.GetComponent<PlayableDirector>() : null;
                    if (director == null) return null;
                    return director.playableAsset != null ? director.playableAsset.name : director.name;
                }

                case SetActiveStep active:
                {
                    string first = null;
                    int count = 0;
                    foreach (UnityEngine.Object target in active.Targets)
                    {
                        if (target == null) continue;
                        first = first ?? target.name;
                        count++;
                    }

                    return count == 0 ? null : count == 1 ? first : $"{first} +{count - 1}";
                }

                default: return null;
            }
        }

        static string Eased(string text, float seconds) => seconds > 0f ? $"{text} · over {seconds:0.##} s" : text;

        /// <summary>
        /// How long <paramref name="step"/> takes, delay included. False when open-ended or unknown.
        /// </summary>
        public static bool TryGetDuration(InteractionStep step, Interactable interactable, out float seconds)
        {
            seconds = step != null ? Mathf.Max(0f, step.Delay) : 0f;

            switch (step)
            {
                case WaitStep wait:
                    seconds += wait.Seconds;
                    return true;

                case MoveToPoseStep pose:
                    seconds += pose.Target != null ? pose.Duration : 0f;
                    return true;

                case LookAtStep look:
                    seconds += look.Target != null ? look.Duration : 0f;
                    return true;

                case PlayAnimationStep animation:
                    seconds += animation.Duration;
                    return true;

                case SetAnimatorParameterStep parameter:
                    seconds += parameter.Kind == AnimatorParameterKind.Float || parameter.Kind == AnimatorParameterKind.Int
                        ? parameter.Duration
                        : 0f;
                    return true;

                case LockPlayerStep _:
                case SetActiveStep _:
                case ParentToStep _:
                case ReleaseControlStep _:
                case UnityEventStep _:
                    return true;

                case PlayTimelineStep timeline:
                {
                    if (!timeline.WaitUntilFinished) return true;

                    PlayableDirector director = timeline.Director != null
                        ? timeline.Director
                        : interactable != null ? interactable.GetComponent<PlayableDirector>() : null;
                    if (director == null || director.playableAsset == null) return false;

                    seconds += (float)director.duration;
                    return true;
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// Total seconds for a list of groups (each lasts as long as its longest enabled step).
        /// <paramref name="allKnown"/> is false when any step is open-ended.
        /// </summary>
        public static float EstimateDuration(List<StepGroup> groups, Interactable interactable, out bool allKnown)
        {
            allKnown = true;
            float total = 0f;
            if (groups == null) return 0f;

            foreach (StepGroup group in groups)
            {
                if (group?.Steps == null) continue;

                float longest = 0f;
                float shortest = float.PositiveInfinity;
                foreach (InteractionStep step in group.Steps)
                {
                    if (step == null || !step.Enabled) continue;

                    if (TryGetDuration(step, interactable, out float seconds))
                    {
                        longest = Mathf.Max(longest, seconds);
                        shortest = Mathf.Min(shortest, seconds);
                    }
                    else
                    {
                        allKnown = false;
                    }
                }

                if (float.IsPositiveInfinity(shortest)) shortest = 0f;
                total += group.Advance == StepGroupAdvance.WhenAnyFinishes ? shortest : longest;
            }

            return total;
        }

        void OnUndoRedo()
        {
            if (m_Released) return;
            // A view never shown (e.g. in a test) never detaches, and its SerializedObject may be disposed.
            if (panel == null) return;
            if (m_Object == null || m_Object.targetObject == null) return;

            // An undo can take away the group whose name has the cursor (Empty group, then Ctrl+Z at once).
            // Drop the field's binding before letting go of focus, so its text is not written back, then
            // rebuild whatever the undo changed: the rebuild is what binds the fields again.
            if (HasFocusInside())
            {
                m_GroupHost.Unbind();
                (panel.focusController.focusedElement as Focusable)?.Blur();
                m_Object.Update();
                Rebuild();
                m_OnChanged?.Invoke();
                return;
            }

            m_Object.Update();
            RebuildIfChanged();
        }

        void RebuildIfChanged()
        {
            // A press holds the rows it started on: a rebuild under it would orphan them.
            if (m_Released || m_Dragging || m_Pressed != null || m_PressedGroup != null) return;
            if (m_Object == null || m_Object.targetObject == null) return;
            if (HasFocusInside()) return;

            int hash = ComputeHash();
            if (hash == m_LastHash) return;

            m_LastHash = hash;
            Rebuild();
            m_OnChanged?.Invoke();
        }

        bool HasFocusInside()
        {
            var focused = panel?.focusController?.focusedElement as VisualElement;
            while (focused != null)
            {
                if (focused == this) return true;
                focused = focused.parent;
            }

            return false;
        }

        List<StepGroup> Groups()
        {
            var list = new List<StepGroup>();
            if (m_Groups == null || !m_Groups.isArray) return list;

            for (int i = 0; i < m_Groups.arraySize; i++)
                list.Add(m_Groups.GetArrayElementAtIndex(i).managedReferenceValue as StepGroup);

            return list;
        }

        int ComputeHash()
        {
            int hash = 17;
            foreach (StepGroup group in Groups())
            {
                hash = unchecked(hash * 31 + (group == null ? 0 : 1));
                if (group == null) continue;

                hash = unchecked(hash * 31 + (group.Name != null ? group.Name.GetHashCode() : 0));
                hash = unchecked(hash * 31 + (int)group.Advance);
                if (group.Steps == null) continue;

                foreach (InteractionStep step in group.Steps)
                {
                    hash = unchecked(hash * 31 + (step != null ? step.GetType().GetHashCode() : 0));
                    if (step == null) continue;

                    hash = unchecked(hash * 31 + (step.Enabled ? 1 : 0));
                    string summary = Summarize(step, m_Interactable, out bool warn);
                    hash = unchecked(hash * 31 + (summary != null ? summary.GetHashCode() : 0) + (warn ? 1 : 0));
                }
            }

            return hash;
        }

        void Rebuild()
        {
            // Taken now so a refused or early-out rebuild does not carry it to a later one.
            int focusGroup = m_FocusGroup;
            m_FocusGroup = -1;
            if (m_Released || m_Dragging || m_Object == null) return;

            m_GroupHost.Clear();
            m_GroupViews.Clear();
            m_LastHash = ComputeHash();

            List<StepGroup> groups = Groups();
            if (groups.Count == 0)
            {
                var empty = new Label(m_EmptyText);
                empty.AddToClassList("blocks-steps__empty");
                empty.style.marginLeft = 1;
                m_GroupHost.Add(empty);
                return;
            }

            for (int g = 0; g < groups.Count; g++)
            {
                GroupView view = BuildGroup(g, groups[g], groups.Count);
                m_GroupViews.Add(view);
                m_GroupHost.Add(view.Root);
            }

            if (m_JustAdded != null)
            {
                Row last = null;
                foreach (GroupView group in m_GroupViews)
                {
                    foreach (Row row in group.Steps)
                    {
                        if (row.Step != null && row.Step.GetType() == m_JustAdded) last = row;
                    }
                }

                if (last != null)
                {
                    last.Root.AddToClassList("blocks-step--enter");
                    last.Root.schedule.Execute(() => last.Root.RemoveFromClassList("blocks-step--enter")).StartingIn(k_EnterDelayMs);
                }

                m_JustAdded = null;
            }

            if (focusGroup >= 0 && focusGroup < m_GroupViews.Count)
            {
                // A new empty group is there to be named: put the cursor in its name.
                TextField name = m_GroupViews[focusGroup].Name;
                if (name != null && name.enabledSelf) name.schedule.Execute(() => name.Focus());
            }

            UpdateLiveState();
        }

        GroupView BuildGroup(int index, StepGroup group, int count)
        {
            var view = new GroupView { Index = index };

            List<InteractionStep> steps = group?.Steps;
            int stepCount = steps != null ? steps.Count : 0;

            // Every group is a named box, even with one step.
            var root = new VisualElement();
            root.AddToClassList("blocks-group");
            root.AddToClassList("blocks-beat");
            view.Root = root;

            SerializedProperty groupProperty = m_Groups.GetArrayElementAtIndex(index);

            // Header: the drag handle, the name, the wait rule when steps overlap, the group menu.
            var header = new VisualElement();
            header.AddToClassList("blocks-beat__header");
            root.Add(header);

            VisualElement handle = BuildHandle();
            handle.pickingMode = PickingMode.Position;
            handle.tooltip = "Drag to move this group, steps and all";
            handle.AddToClassList("blocks-beat__handle");
            handle.RegisterCallback<PointerDownEvent>(evt => OnGroupPointerDown(evt, view));
            header.Add(handle);

            SerializedProperty nameProperty = groupProperty.FindPropertyRelative("Name");
            var name = new TextField { isDelayed = true, tooltip = "Name this group: \"Walk up\", \"Open door\". The names make the summary under the card." };
            name.AddToClassList("blocks-beat__name");
            view.Name = name;
            if (nameProperty != null)
            {
                name.BindProperty(nameProperty);
            }
            else
            {
                name.SetEnabled(false);
            }

            string placeholder = GroupName(group, index);
            name.RegisterCallback<FocusOutEvent>(_ => SyncNamePlaceholder(name, placeholder));
            name.RegisterValueChangedCallback(_ =>
            {
                SyncNamePlaceholder(name, placeholder);
                m_OnChanged?.Invoke();
            });
            SyncNamePlaceholder(name, placeholder);
            header.Add(name);

            if (stepCount >= 2 && index < count - 1)
            {
                bool isDefault = group.Advance == StepGroupAdvance.WhenAllFinish;
                header.EnableInClassList("blocks-beat__header--default-wait", isDefault);
                var waits = new Label("next waits for")
                {
                    tooltip = "The steps in this group start together. This says when the next group starts.",
                };
                waits.AddToClassList("blocks-beat__waits");
                header.Add(waits);

                SerializedProperty advanceProperty = groupProperty.FindPropertyRelative("Advance");
                var advance = new DropdownField(new List<string> { "all", "the first" }, 0)
                {
                    tooltip = "All: the next group waits until every step here has ended. The first: the next group starts " +
                              "as soon as one step here ends, and the rest keep running alongside it.",
                };
                advance.AddToClassList("blocks-beat__advance");
                if (advanceProperty != null)
                {
                    advance.index = Mathf.Clamp(advanceProperty.enumValueIndex, 0, 1);
                    advance.RegisterValueChangedCallback(_ =>
                    {
                        if (m_Released || advanceProperty.enumValueIndex == advance.index) return;
                        advanceProperty.enumValueIndex = advance.index;
                        m_Object.ApplyModifiedProperties();
                        m_LastHash = ComputeHash();
                        m_OnChanged?.Invoke();
                    });
                }

                header.Add(advance);
            }

            var more = new Button { text = "⋮", tooltip = "Add a step to this group, move it, delete it" };
            more.AddToClassList("blocks-beat__more");
            more.clicked += () => ShowGroupMenu(more, index, count);
            header.Add(more);

            header.AddManipulator(new ContextualMenuManipulator(evt => PopulateGroupMenu(evt, index, count)));

            // Rows.
            view.Rows = new VisualElement();
            view.Rows.AddToClassList("blocks-group__rows");
            root.Add(view.Rows);

            if (stepCount == 0)
            {
                var empty = new Label("Empty, so it is skipped. Drag a step here, or ⋮ › Add step to this group.");
                empty.AddToClassList("blocks-group__empty");
                view.Rows.Add(empty);
            }

            SerializedProperty stepsProperty = groupProperty.FindPropertyRelative("Steps");
            for (int i = 0; i < stepCount; i++)
            {
                SerializedProperty stepProperty = stepsProperty != null && i < stepsProperty.arraySize
                    ? stepsProperty.GetArrayElementAtIndex(i)
                    : null;
                Row row = BuildRow(steps[i], stepProperty, index, i);
                view.Steps.Add(row);
                view.Rows.Add(row.Root);
            }

            return view;
        }

        /// <summary>
        /// A group's display name: its own name, else derived from its steps ("Move Player To &amp; 1 more"),
        /// else "Group 2".
        /// </summary>
        public static string GroupName(StepGroup group, int index)
        {
            if (group != null && !string.IsNullOrWhiteSpace(group.Name)) return group.Name.Trim();

            InteractionStep first = null;
            int count = 0;
            if (group?.Steps != null)
            {
                foreach (InteractionStep step in group.Steps)
                {
                    if (step == null) continue;
                    first = first ?? step;
                    count++;
                }
            }

            if (first == null) return $"Group {index + 1}";
            string name = DisplayName(first.GetType());
            return count == 1 ? name : $"{name} & {count - 1} more";
        }

        static void SyncNamePlaceholder(TextField field, string placeholder)
        {
            bool empty = string.IsNullOrEmpty(field.value);
            field.EnableInClassList("blocks-beat__name--empty", empty);
#if UNITY_6000_0_OR_NEWER
            field.textEdition.placeholder = placeholder;
            field.textEdition.hidePlaceholderOnFocus = false;
#endif
            // The placeholder uses the field's text colour, so dim it inline for an unnamed group.
            TextElement text = field.Q<TextElement>();
            if (text != null)
                text.style.color = empty ? new StyleColor(EditorGUIUtility.isProSkin ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.45f, 0.45f, 0.45f)) : new StyleColor(StyleKeyword.Null);
        }

        Row BuildRow(InteractionStep step, SerializedProperty property, int groupIndex, int index)
        {
            var row = new Row { Step = step, Group = groupIndex, Index = index };

            var root = new VisualElement();
            root.AddToClassList("blocks-step-frame");
            row.Root = root;

            var line = new VisualElement();
            line.AddToClassList("blocks-step");
            root.Add(line);
            row.Line = line;

            Type type = step?.GetType();

            // The drag handle leads the row. A skipped step (Enabled off, from ⋮) reads dimmed.
            line.Add(BuildHandle());
            if (step != null && !step.Enabled) line.AddToClassList("blocks-step--muted");

            // Node: a small ring that fills in while the run passes.
            var node = new VisualElement();
            node.AddToClassList("blocks-step__node");
            node.AddToClassList("blocks-step__node--inline");
            row.NodeLabel = new Label((index + 1).ToString());
            row.NodeLabel.AddToClassList("blocks-step__node-label");
            row.NodeIcon = new Image { scaleMode = ScaleMode.ScaleToFit };
            row.NodeIcon.AddToClassList("blocks-step__node-icon");
            node.Add(row.NodeLabel);
            node.Add(row.NodeIcon);
            line.Add(node);

            // Glyph.
            Texture2D glyph = Glyph(type);
            if (glyph != null)
            {
                var image = new Image { image = glyph, scaleMode = ScaleMode.ScaleToFit };
                image.AddToClassList("blocks-step__glyph");
                line.Add(image);
            }

            // Name only; hover shows the settings line, and a mark flags a step not set up yet.
            var text = new VisualElement();
            text.AddToClassList("blocks-step__text");
            var name = new Label(step != null ? DisplayName(type) : "(missing step)");
            name.AddToClassList("blocks-step__name");
            text.Add(name);
            // The dim words after the name: what it acts on, then its delay. "DoorOpen · after 0.5 s".
            string noun = Noun(step, m_Interactable);
            if (step != null && step.Delay > 0f)
            {
                string after = $"after {step.Delay:0.##} s";
                noun = string.IsNullOrEmpty(noun) ? after : $"{noun} · {after}";
            }

            if (!string.IsNullOrEmpty(noun))
            {
                var what = new Label(noun);
                what.AddToClassList("blocks-step__noun");
                text.Add(what);
            }

            line.Add(text);
            line.AddToClassList("blocks-step--compact");

            bool warn = false;
            string summary = step != null
                ? Settings(step, m_Interactable, out warn)
                : "Its script was removed or renamed. Delete it from ⋮, or bring the script back.";
            if (step == null) warn = true;
            if (!string.IsNullOrEmpty(summary)) line.tooltip = summary;

            if (warn)
            {
                line.AddToClassList("blocks-step--warn");
                var mark = new Label("!") { tooltip = summary };
                mark.AddToClassList("blocks-step__warn-mark");
                line.Add(mark);
            }

            var more = new Button { text = "⋮", tooltip = "Duplicate, give it its own group, delete" };
            more.AddToClassList("blocks-step__more");
            more.clicked += () => ShowStepMenu(more, row);
            line.Add(more);


            var track = new VisualElement();
            track.AddToClassList("blocks-step__track");
            row.Progress = new VisualElement();
            row.Progress.AddToClassList("blocks-step__progress");
            track.Add(row.Progress);
            line.Add(track);

            // Settings under the row.
            row.Body = new VisualElement();
            row.Body.AddToClassList("blocks-step__body");
            root.Add(row.Body);

            string key = StepKey(property);
            bool open = key != null && m_Open.Contains(key);
            if (open) FillBody(row, property);
            row.Body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            line.EnableInClassList("blocks-step--open", open);

            row.Property = property;
            line.RegisterCallback<PointerDownEvent>(evt => OnPointerDown(evt, row));
            line.AddManipulator(new ContextualMenuManipulator(evt => PopulateStepMenu(evt, row)));

            return row;
        }

        /// <summary>Two columns of three dots: the drag handle at the start of a row.</summary>
        static VisualElement BuildHandle()
        {
            var handle = new VisualElement { pickingMode = PickingMode.Ignore };
            handle.AddToClassList("blocks-handle");
            for (int c = 0; c < 2; c++)
            {
                var column = new VisualElement { pickingMode = PickingMode.Ignore };
                column.AddToClassList("blocks-handle__column");
                for (int d = 0; d < 3; d++)
                {
                    var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                    dot.AddToClassList("blocks-handle__dot");
                    column.Add(dot);
                }

                handle.Add(column);
            }

            return handle;
        }

        /// <summary>The step's settings: Delay first, then every other serialized field the step declares. Enabled is the checkbox on the row.</summary>
        void FillBody(Row row, SerializedProperty property)
        {
            row.Body.Clear();
            if (property == null || row.Step == null) return;

            SerializedProperty delay = property.FindPropertyRelative("Delay");
            if (delay != null)
            {
                var field = new PropertyField(delay, "Delay (seconds)")
                {
                    tooltip = "Seconds after the group starts before this step runs. The other steps in the group are already going by then.",
                };
                field.BindProperty(delay);
                row.Body.Add(field);
            }

            int fields = 0;
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                if (child.name == "Enabled" || child.name == "Delay") continue;

                var field = new PropertyField(child.Copy());
                field.BindProperty(child.Copy());
                row.Body.Add(field);
                fields++;
            }

            if (row.Step is PlayAnimationStep)
                AddPointOfViewHint(row.Body, property);

            if (fields == 0 && delay == null)
            {
                var none = new Label("Nothing to set up.");
                none.AddToClassList("blocks-panel__hint");
                none.style.marginTop = 0;
                row.Body.Add(none);
            }
        }

        /// <summary>
        /// Warns under Play Animation when Copy Field Of View is on but the Point Of View has no Camera.
        /// </summary>
        static void AddPointOfViewHint(VisualElement body, SerializedProperty property)
        {
            SerializedProperty pointOfView = property.FindPropertyRelative("m_PointOfView");
            SerializedProperty copy = property.FindPropertyRelative("m_CopyFieldOfView");
            if (pointOfView == null || copy == null) return;

            var hint = new Label();
            hint.AddToClassList("blocks-panel__hint");
            body.Add(hint);

            void Refresh()
            {
                var target = pointOfView.objectReferenceValue as GameObject;
                bool show = target != null && copy.boolValue && target.GetComponent<Camera>() == null;
                hint.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show)
                    hint.text = $"{target.name} has no Camera, so the field of view stays the player's. " +
                                "Add a Camera to it (switched off) only if the clip animates field of view.";
            }

            Refresh();
            hint.TrackPropertyValue(pointOfView, _ => Refresh());
            hint.TrackPropertyValue(copy, _ => Refresh());
        }

        /// <summary>A key for a step's open state that survives rebuilds: the object plus the step's reference id.</summary>
        string StepKey(SerializedProperty property)
        {
            if (property == null || m_Object == null || m_Object.targetObject == null) return null;

            long id;
            try
            {
                id = property.managedReferenceId;
            }
            catch (Exception)
            {
                return null;
            }

#if UNITY_6000_4_OR_NEWER
            return $"{m_Object.targetObject.GetEntityId()}:{id}";
#else
            return $"{m_Object.targetObject.GetInstanceID()}:{id}";
#endif
        }

        void ToggleOpen(Row row, SerializedProperty property)
        {
            string key = StepKey(property);
            if (key == null || row.Step == null) return;

            bool open = !m_Open.Contains(key);
            if (open)
            {
                m_Open.Add(key);
                FillBody(row, property);
            }
            else
            {
                m_Open.Remove(key);
                row.Body.Clear();
            }

            row.Body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            row.Line.EnableInClassList("blocks-step--open", open);
        }

        void ShowAddMenu(VisualElement anchor, int groupIndex)
        {
            var menu = new GenericMenu();
            List<Type> types = OfferableTypes();

            bool separated = false;
            bool anyShipped = false;
            foreach (Type type in types)
            {
                bool shipped = IsShipped(type);
                if (shipped) anyShipped = true;
                if (!shipped && anyShipped && !separated)
                {
                    menu.AddSeparator(string.Empty);
                    separated = true;
                }

                Type chosen = type;
                menu.AddItem(new GUIContent(DisplayName(type)), false, () => AddStep(groupIndex, chosen));
            }

            if (types.Count == 0)
                menu.AddDisabledItem(new GUIContent("No step scripts found"));

            menu.AddSeparator(string.Empty);
            if (groupIndex < 0)
                menu.AddItem(new GUIContent("Empty group"), false, AddEmptyGroup);
            menu.AddItem(new GUIContent("New step script..."), false, InteractionStepTemplate.CreateFromMenu);

            menu.DropDown(anchor.worldBound);
        }

        void ShowStepMenu(VisualElement anchor, Row row)
        {
            var menu = new GenericMenu();
            bool live = row.Step != null;
            AddMenuItem(menu, "Duplicate", live, () => DuplicateStep(row.Group, row.Index));
            AddMenuItem(menu, "Give it its own group", live && CanSplitOut(row), () => SplitOut(row.Group, row.Index));
            AddMenuItem(menu, live && !row.Step.Enabled ? "Stop skipping" : "Skip this step", live, () => ToggleSkip(row.Group, row.Index));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Delete"), false, () => RemoveStep(row.Group, row.Index));
            menu.DropDown(anchor.worldBound);
        }

        void ShowGroupMenu(VisualElement anchor, int index, int count)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add step to this group"), false, () => ShowAddMenu(anchor, index));
            menu.AddSeparator(string.Empty);
            AddMenuItem(menu, "Move group up", index > 0, () => MoveGroup(index, index - 1));
            AddMenuItem(menu, "Move group down", index < count - 1, () => MoveGroup(index, index + 1));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(DeleteGroupLabel(index)), false, () => RemoveGroup(index));
            menu.DropDown(anchor.worldBound);
        }

        string DeleteGroupLabel(int index)
        {
            int steps = index >= 0 && index < m_GroupViews.Count ? m_GroupViews[index].Steps.Count : 0;
            return steps <= 1 ? "Delete group" : $"Delete group ({steps} steps)";
        }

        static void AddMenuItem(GenericMenu menu, string text, bool enabled, GenericMenu.MenuFunction action)
        {
            if (enabled) menu.AddItem(new GUIContent(text), false, action);
            else menu.AddDisabledItem(new GUIContent(text));
        }

        bool CanSplitOut(Row row) => row.Group >= 0 && row.Group < m_GroupViews.Count && m_GroupViews[row.Group].Steps.Count > 1;

        void PopulateStepMenu(ContextualMenuPopulateEvent evt, Row row)
        {
            bool live = row.Step != null;
            evt.menu.AppendAction("Duplicate", _ => DuplicateStep(row.Group, row.Index),
                live ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Give it its own group", _ => SplitOut(row.Group, row.Index),
                live && CanSplitOut(row) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction(live && !row.Step.Enabled ? "Stop skipping" : "Skip this step", _ => ToggleSkip(row.Group, row.Index),
                live ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Delete", _ => RemoveStep(row.Group, row.Index));
        }

        void PopulateGroupMenu(ContextualMenuPopulateEvent evt, int index, int count)
        {
            evt.menu.AppendAction("Move group up", _ => MoveGroup(index, index - 1),
                index > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Move group down", _ => MoveGroup(index, index + 1),
                index < count - 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction(DeleteGroupLabel(index), _ => RemoveGroup(index));
        }

        SerializedProperty StepsOf(int groupIndex) => StepGroupEdits.StepsOf(m_Groups, groupIndex);

        void Commit(string undoName)
        {
            m_Object.ApplyModifiedProperties();
            if (m_Object.targetObject != null) Undo.SetCurrentGroupName(undoName);
            m_Object.Update();
            Rebuild();
            m_OnChanged?.Invoke();
        }

        void RemoveGroup(int index)
        {
            if (m_Released) return;
            m_Object.Update();
            if (!StepGroupEdits.RemoveGroup(m_Groups, index)) return;
            Commit("Delete group");
        }

        void MoveGroup(int from, int to)
        {
            if (m_Released) return;
            m_Object.Update();
            if (!StepGroupEdits.MoveGroup(m_Groups, from, to))
            {
                Rebuild();
                return;
            }

            Commit("Move group");
        }

        /// <summary>Adds a group with no steps at the end and puts the cursor in its name.</summary>
        void AddEmptyGroup()
        {
            if (m_Released) return;
            m_Object.Update();
            int at = StepGroupEdits.AddEmptyGroup(m_Groups);
            if (at < 0) return;

            m_FocusGroup = at;
            Commit("Add empty group");
        }

        /// <summary>Adds a step to group <paramref name="groupIndex"/>, or as a new group at the end for <c>-1</c>.</summary>
        void AddStep(int groupIndex, Type type)
        {
            if (m_Released) return;
            m_Object.Update();

            InteractionStep step;
            try
            {
                step = (InteractionStep)Activator.CreateInstance(type);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, m_Object.targetObject);
                return;
            }

            bool added = groupIndex < 0
                ? StepGroupEdits.AddStepAsNewGroup(m_Groups, step)
                : StepGroupEdits.AddStep(m_Groups, groupIndex, step);
            if (!added) return;

            m_JustAdded = type;
            Commit($"Add {DisplayName(type)} step");
        }

        void RemoveStep(int groupIndex, int index)
        {
            if (m_Released) return;
            m_Object.Update();
            if (!StepGroupEdits.RemoveStep(m_Groups, groupIndex, index)) return;
            Commit("Delete step");
        }

        void DuplicateStep(int groupIndex, int index)
        {
            if (m_Released) return;
            m_Object.Update();
            SerializedProperty steps = StepsOf(groupIndex);
            if (steps == null || index < 0 || index >= steps.arraySize) return;

            var source = steps.GetArrayElementAtIndex(index).managedReferenceValue as InteractionStep;
            if (source == null) return;

            // A deep copy of the serialized fields; scene references come along as references.
            var copy = (InteractionStep)JsonUtility.FromJson(JsonUtility.ToJson(source), source.GetType());
            if (copy == null) return;

            steps.InsertArrayElementAtIndex(index + 1);
            steps.GetArrayElementAtIndex(index + 1).managedReferenceValue = copy;
            Commit("Duplicate step");
        }

        /// <summary>Skips a step (it keeps its place, dimmed) or brings it back.</summary>
        void ToggleSkip(int groupIndex, int index)
        {
            if (m_Released) return;
            m_Object.Update();
            SerializedProperty steps = StepsOf(groupIndex);
            if (steps == null || index < 0 || index >= steps.arraySize) return;

            SerializedProperty enabled = steps.GetArrayElementAtIndex(index).FindPropertyRelative("Enabled");
            if (enabled == null) return;
            enabled.boolValue = !enabled.boolValue;
            Commit(enabled.boolValue ? "Stop skipping step" : "Skip step");
        }

        /// <summary>Takes a step out of its group into a new group of its own, right after it.</summary>
        void SplitOut(int groupIndex, int index)
        {
            if (m_Released) return;
            m_Object.Update();
            if (!StepGroupEdits.MoveStep(m_Groups, groupIndex, index, groupIndex + 1, 0, asNewGroup: true))
            {
                Rebuild();
                return;
            }

            Commit("Give step its own group");
        }

        /// <summary>Moves a step to <paramref name="target"/>, expressed in the list as it stands before the move.</summary>
        void MoveStep(int fromGroup, int fromIndex, DropTarget target)
        {
            if (m_Released) return;
            m_Object.Update();
            if (!StepGroupEdits.MoveStep(m_Groups, fromGroup, fromIndex, target.Group, target.Index, target.NewGroup))
            {
                Rebuild();
                return;
            }

            Commit(target.NewGroup ? "Move step to its own group" : fromGroup == target.Group ? "Reorder step" : "Move step to another group");
        }

        void UpdateLiveState()
        {
            if (m_Released) return;
            m_LiveTick++;
            bool live = Application.isPlaying && m_Interactable != null && m_Interactable.IsPlaying;

            foreach (GroupView group in m_GroupViews)
            {
                foreach (Row row in group.Steps)
                {
                    StepState state = StepState.None;
                    float progress = -1f;

                    if (live && row.Step != null)
                    {
                        if (m_Interactable.IsRunning(row.Step))
                        {
                            state = StepState.Running;
                            if (TryGetDuration(row.Step, m_Interactable, out float total) && total > 0f)
                                progress = m_Interactable.ElapsedOf(row.Step) / total;
                        }
                        else if (m_Interactable.HasFinished(row.Step))
                        {
                            state = StepState.Done;
                        }
                        else
                        {
                            state = StepState.Pending;
                        }
                    }

                    if (state != row.State)
                    {
                        row.State = state;
                        row.Line.EnableInClassList("blocks-step--pending", state == StepState.Pending);
                        row.Line.EnableInClassList("blocks-step--running", state == StepState.Running);
                        row.Line.EnableInClassList("blocks-step--done", state == StepState.Done);
                        if (state != StepState.Running) row.Progress.style.width = 0;
                    }

                    if (state != StepState.Running) continue;

                    row.Progress.style.width = progress < 0f ? 0 : Length.Percent(Mathf.Clamp01(progress) * 100f);
                    Texture2D frame = m_Spinner[(m_LiveTick / 2) % k_SpinnerFrames];
                    if (frame != null) row.NodeIcon.image = frame;
                }
            }
        }

        static bool IsControl(VisualElement element, VisualElement stopAt)
        {
            while (element != null && element != stopAt)
            {
                if (element is Toggle || element is Button || element is TextField || element is DropdownField) return true;
                element = element.parent;
            }

            return false;
        }

        void OnPointerDown(PointerDownEvent evt, Row row)
        {
            if (m_Released || evt.button != 0 || m_Pressed != null || m_PressedGroup != null) return;
            if (IsControl(evt.target as VisualElement, row.Line)) return;

            m_Pressed = row;
            m_PointerId = evt.pointerId;
            m_PressPosition = evt.position;
            m_Dragging = false;
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnGroupPointerDown(PointerDownEvent evt, GroupView view)
        {
            if (m_Released || evt.button != 0 || m_Pressed != null || m_PressedGroup != null) return;

            m_PressedGroup = view;
            m_PointerId = evt.pointerId;
            m_PressPosition = evt.position;
            m_Dragging = false;
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (m_PressedGroup != null)
            {
                OnGroupPointerMove(evt);
                return;
            }

            Row row = m_Pressed;
            if (row == null || evt.pointerId != m_PointerId || !this.HasPointerCapture(m_PointerId)) return;

            Vector2 delta = (Vector2)evt.position - m_PressPosition;
            if (!m_Dragging)
            {
                if (Mathf.Abs(delta.y) < k_DragThreshold) return;
                BeginDrag(row);
                if (!m_Dragging) return;
            }

            UpdateDrag(evt.position);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (m_PressedGroup != null)
            {
                if (evt.pointerId != m_PointerId) return;

                GroupView view = m_PressedGroup;
                bool groupWasDragging = m_Dragging;
                m_PressedGroup = null;
                if (this.HasPointerCapture(m_PointerId))
                    this.ReleasePointer(m_PointerId);
                m_PointerId = -1;

                if (groupWasDragging) EndGroupDrag(view, apply: true);
                return;
            }

            Row row = m_Pressed;
            if (row == null || evt.pointerId != m_PointerId) return;

            bool wasDragging = m_Dragging;
            m_Pressed = null;
            if (this.HasPointerCapture(m_PointerId))
                this.ReleasePointer(m_PointerId);
            m_PointerId = -1;

            if (!wasDragging)
            {
                ToggleOpen(row, row.Property);
                return;
            }

            EndDrag(row, apply: true);
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (m_PressedGroup != null)
            {
                if (evt.target != this) return;

                GroupView view = m_PressedGroup;
                m_PressedGroup = null;
                m_PointerId = -1;
                if (m_Dragging) EndGroupDrag(view, apply: false);
                return;
            }

            Row row = m_Pressed;
            if (row == null || evt.target != this) return;

            m_Pressed = null;
            m_PointerId = -1;
            if (m_Dragging) EndDrag(row, apply: false);
        }

        void BeginDrag(Row row)
        {
            if (row.Root.parent == null || row.Root.panel != panel)
            {
                CancelPress();
                Rebuild();
                return;
            }

            m_Dragging = true;

            // The row's frame floats over the whole list; a spacer holds its slot open.
            Rect frame = row.Root.worldBound;
            Rect mine = worldBound;
            m_DragOriginTop = frame.y - mine.y;
            m_DragOriginLeft = frame.x - mine.x;

            m_Spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Spacer.style.height = frame.height;
            row.Root.parent.Insert(row.Root.parent.IndexOf(row.Root), m_Spacer);

            // A group whose only step is dragged stays on screen, so dropping the step back where it was
            // finds its own group and changes nothing.
            row.Root.RemoveFromHierarchy();
            Add(row.Root);
            row.Root.AddToClassList("blocks-step-frame--dragging");
            row.Line.AddToClassList("blocks-step--dragging");
            row.Root.style.position = Position.Absolute;
            row.Root.style.left = m_DragOriginLeft;
            row.Root.style.width = frame.width;
            row.Root.style.top = m_DragOriginTop;
            row.Root.pickingMode = PickingMode.Ignore;

            m_DropLine = new VisualElement { pickingMode = PickingMode.Ignore };
            m_DropLine.AddToClassList("blocks-drop-line");
            var newGroup = new Label("New group") { pickingMode = PickingMode.Ignore };
            newGroup.AddToClassList("blocks-drop-line__label");
            m_DropLine.Add(newGroup);
            Add(m_DropLine);

            m_DropTarget = new DropTarget { Group = row.Group, Index = row.Index };
            PlaceDropLine(row);
        }

        void UpdateDrag(Vector2 pointer)
        {
            Row row = m_Pressed;
            if (row == null) return;

            row.Root.style.top = m_DragOriginTop + (pointer.y - m_PressPosition.y);
            m_DropTarget = FindDropTarget(pointer.y, row);
            PlaceDropLine(row);
        }

        /// <summary>
        /// The drop slot nearest the pointer: between groups it becomes a new group; between rows it joins
        /// that group.
        /// </summary>
        DropTarget FindDropTarget(float y, Row dragged)
        {
            var best = new DropTarget { Group = dragged.Group, Index = dragged.Index };
            float bestDistance = float.PositiveInfinity;
            int lastVisible = -1;

            for (int g = 0; g < m_GroupViews.Count; g++)
            {
                GroupView group = m_GroupViews[g];
                if (group.Root.style.display.value == DisplayStyle.None) continue;
                Rect root = group.Root.worldBound;

                // A new group before this one.
                Consider(g, 0, true, root.yMin, y, ref best, ref bestDistance);
                lastVisible = g;

                // A group whose only step is the one being dragged has nothing to join.
                if (group.Steps.Count == 1 && group.Steps[0] == dragged) continue;

                Rect rowsBound = group.Rows.worldBound;
                Consider(g, 0, false, rowsBound.yMin + 2f, y, ref best, ref bestDistance);

                for (int i = 0; i < group.Steps.Count; i++)
                {
                    Row other = group.Steps[i];
                    if (other == dragged) continue;

                    Rect bound = other.Root.worldBound;
                    Consider(g, i + 1, false, bound.yMax, y, ref best, ref bestDistance);
                }
            }

            // A new group after the last one showing.
            if (lastVisible >= 0)
                Consider(lastVisible + 1, 0, true, m_GroupViews[lastVisible].Root.worldBound.yMax, y, ref best, ref bestDistance);

            return best;
        }

        static void Consider(int group, int index, bool newGroup, float edge, float y, ref DropTarget best, ref float bestDistance)
        {
            float distance = Mathf.Abs(edge - y);
            if (distance >= bestDistance) return;
            bestDistance = distance;
            best = new DropTarget { Group = group, Index = index, NewGroup = newGroup };
        }

        void PlaceDropLine(Row dragged)
        {
            if (m_DropLine == null) return;

            Rect mine = worldBound;
            foreach (GroupView view in m_GroupViews)
                view.Root.EnableInClassList("blocks-beat--drop", !m_DropTarget.NewGroup && view.Index == m_DropTarget.Group);

            if (m_DropTarget.NewGroup)
            {
                // Between groups: a labelled line in the gap, where the new group will appear.
                GroupView before = null, after = null;
                for (int g = m_DropTarget.Group; g < m_GroupViews.Count && before == null; g++)
                    if (m_GroupViews[g].Root.style.display.value != DisplayStyle.None) before = m_GroupViews[g];
                for (int g = Mathf.Min(m_DropTarget.Group, m_GroupViews.Count) - 1; g >= 0 && after == null; g--)
                    if (m_GroupViews[g].Root.style.display.value != DisplayStyle.None) after = m_GroupViews[g];
                float seam = before != null && after != null
                    ? (after.Root.worldBound.yMax + before.Root.worldBound.yMin) * 0.5f
                    : before != null ? before.Root.worldBound.yMin - 3f
                    : after != null ? after.Root.worldBound.yMax + 3f
                    : mine.yMin;
                Rect host = m_GroupHost.worldBound;
                m_DropLine.EnableInClassList("blocks-drop-line--group", true);
                m_DropLine.style.display = DisplayStyle.Flex;
                m_DropLine.style.left = host.xMin - mine.xMin;
                m_DropLine.style.width = host.width;
                m_DropLine.style.top = seam - mine.yMin - 1;
                return;
            }

            m_DropLine.EnableInClassList("blocks-drop-line--group", false);
            GroupView group = m_DropTarget.Group >= 0 && m_DropTarget.Group < m_GroupViews.Count ? m_GroupViews[m_DropTarget.Group] : null;
            if (group == null)
            {
                m_DropLine.style.display = DisplayStyle.None;
                return;
            }

            Rect rows = group.Rows.worldBound;
            float y = rows.yMin;
            if (m_DropTarget.Index > 0 && m_DropTarget.Index - 1 < group.Steps.Count)
            {
                // Under the row before the slot; the dragged row's slot is held by the spacer.
                Row above = group.Steps[m_DropTarget.Index - 1];
                y = above == dragged && m_Spacer != null ? m_Spacer.worldBound.yMax : above.Root.worldBound.yMax;
            }

            m_DropLine.style.display = DisplayStyle.Flex;
            m_DropLine.style.left = rows.xMin - mine.xMin + 8;
            m_DropLine.style.width = rows.width - 16;
            m_DropLine.style.top = y - mine.yMin - 1;
        }

        void EndDrag(Row row, bool apply)
        {
            m_Dragging = false;
            DropTarget target = m_DropTarget;

            m_DropLine?.RemoveFromHierarchy();
            m_DropLine = null;
            foreach (GroupView view in m_GroupViews) view.Root.RemoveFromClassList("blocks-beat--drop");
            m_Spacer?.RemoveFromHierarchy();
            m_Spacer = null;

            row.Root.RemoveFromHierarchy();

            // A drop where the step already is moves nothing; MoveStep says so and the list just redraws.
            if (apply)
                MoveStep(row.Group, row.Index, target);
            else
                Rebuild();
        }

        void OnGroupPointerMove(PointerMoveEvent evt)
        {
            GroupView view = m_PressedGroup;
            if (evt.pointerId != m_PointerId || !this.HasPointerCapture(m_PointerId)) return;

            float dy = evt.position.y - m_PressPosition.y;
            if (!m_Dragging)
            {
                if (Mathf.Abs(dy) < k_DragThreshold) return;
                BeginGroupDrag(view);
                if (!m_Dragging) return;
            }

            view.Root.style.top = m_DragOriginTop + dy;
            m_GroupSlot = FindGroupSlot(m_DragOriginTop + dy, view);
            PlaceGroupDropLine(view);
        }

        void BeginGroupDrag(GroupView view)
        {
            if (view.Root.parent != m_GroupHost)
            {
                CancelPress();
                Rebuild();
                return;
            }

            m_Dragging = true;

            // The group floats over the list; a spacer of its height holds its place.
            Rect frame = view.Root.worldBound;
            Rect mine = worldBound;
            m_DragOriginTop = frame.y - mine.y;
            m_DragOriginLeft = frame.x - mine.x;

            m_Spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Spacer.style.height = frame.height;
            m_Spacer.style.marginBottom = view.Root.resolvedStyle.marginBottom;
            m_GroupHost.Insert(m_GroupHost.IndexOf(view.Root), m_Spacer);

            view.Root.RemoveFromHierarchy();
            Add(view.Root);
            view.Root.AddToClassList("blocks-beat--dragging");
            view.Root.style.position = Position.Absolute;
            view.Root.style.left = m_DragOriginLeft;
            view.Root.style.width = frame.width;
            view.Root.style.top = m_DragOriginTop;
            view.Root.pickingMode = PickingMode.Ignore;

            m_DropLine = new VisualElement { pickingMode = PickingMode.Ignore };
            m_DropLine.AddToClassList("blocks-drop-line");
            m_DropLine.AddToClassList("blocks-drop-line--group");
            Add(m_DropLine);

            m_GroupSlot = view.Index;
            PlaceGroupDropLine(view);
        }

        /// <summary>Top edge of group <paramref name="g"/>; the dragged group's place is its spacer.</summary>
        float GroupTop(int g, GroupView dragged) =>
            g == dragged.Index && m_Spacer != null ? m_Spacer.worldBound.yMin : m_GroupViews[g].Root.worldBound.yMin;

        float GroupBottom(int g, GroupView dragged) =>
            g == dragged.Index && m_Spacer != null ? m_Spacer.worldBound.yMax : m_GroupViews[g].Root.worldBound.yMax;

        /// <summary>The seam above group <paramref name="slot"/>, or below the last group for the count.</summary>
        float GroupSeam(int slot, GroupView dragged)
        {
            int count = m_GroupViews.Count;
            if (slot <= 0) return GroupTop(0, dragged);
            if (slot >= count) return GroupBottom(count - 1, dragged);
            return (GroupBottom(slot - 1, dragged) + GroupTop(slot, dragged)) * 0.5f;
        }

        /// <summary>
        /// Where the floating group would drop, as a seam index into the list before the move. It passes a
        /// neighbour once its leading edge crosses that neighbour's middle, the same distance up or down.
        /// </summary>
        int FindGroupSlot(float floatingTop, GroupView dragged)
        {
            Rect home = m_Spacer != null ? m_Spacer.worldBound : dragged.Root.worldBound;
            float top = worldBound.yMin + floatingTop;
            float bottom = top + home.height;

            int to = dragged.Index;
            for (int g = dragged.Index + 1; g < m_GroupViews.Count; g++)
            {
                if (bottom > m_GroupViews[g].Root.worldBound.center.y) to = g;
            }

            for (int g = dragged.Index - 1; g >= 0; g--)
            {
                if (top < m_GroupViews[g].Root.worldBound.center.y) to = g;
            }

            return to > dragged.Index ? to + 1 : to;
        }

        /// <summary>Ends a press or drag without applying it: lets go of the pointer and removes the drag's helpers.</summary>
        void CancelPress()
        {
            // State first: letting go of the pointer sends a capture-out, which must find nothing to end.
            Row row = m_Pressed;
            GroupView group = m_PressedGroup;
            int pointerId = m_PointerId;
            m_Pressed = null;
            m_PressedGroup = null;
            m_PointerId = -1;
            m_Dragging = false;
            if (pointerId >= 0 && this.HasPointerCapture(pointerId))
                this.ReleasePointer(pointerId);

            m_DropLine?.RemoveFromHierarchy();
            m_DropLine = null;
            m_Spacer?.RemoveFromHierarchy();
            m_Spacer = null;
            if (row != null && row.Root.parent == this) row.Root.RemoveFromHierarchy();
            if (group != null && group.Root.parent == this) group.Root.RemoveFromHierarchy();
        }

        void PlaceGroupDropLine(GroupView dragged)
        {
            if (m_DropLine == null) return;

            bool moves = StepGroupEdits.GroupIndexForSlot(dragged.Index, m_GroupSlot) >= 0;
            m_DropLine.style.display = moves ? DisplayStyle.Flex : DisplayStyle.None;
            if (!moves) return;

            Rect mine = worldBound;
            Rect host = m_GroupHost.worldBound;
            float seam = GroupSeam(m_GroupSlot, dragged);
            if (m_GroupSlot <= 0) seam -= 3f;
            else if (m_GroupSlot >= m_GroupViews.Count) seam += 3f;

            m_DropLine.style.left = host.xMin - mine.xMin;
            m_DropLine.style.width = host.width;
            m_DropLine.style.top = seam - mine.yMin - 1;
        }

        void EndGroupDrag(GroupView view, bool apply)
        {
            m_Dragging = false;
            int to = StepGroupEdits.GroupIndexForSlot(view.Index, m_GroupSlot);

            m_DropLine?.RemoveFromHierarchy();
            m_DropLine = null;
            m_Spacer?.RemoveFromHierarchy();
            m_Spacer = null;
            view.Root.RemoveFromHierarchy();

            if (apply && to >= 0)
                MoveGroup(view.Index, to);
            else
                Rebuild();
        }
    }
}
