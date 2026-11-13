using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// The character's move processors in run order, drawn from its serialized
    /// <c>List&lt;MoveProcessor&gt;</c>. Click a row to open its settings, drag to reorder, right-click
    /// for more; the add row opens a palette of every processor class.
    /// </summary>
    /// <remarks>
    /// Edits go through the given <see cref="SerializedProperty"/> as one Undo step. Order is run order:
    /// moving <see cref="JumpProcessor"/> above <see cref="GroundLocomotionProcessor"/> changes the tick the
    /// jump lands on. Custom <see cref="MoveProcessor"/> classes appear automatically (via
    /// <see cref="TypeCache"/>); a shipped processor already in the list is dimmed.
    /// </remarks>
    public sealed class ProcessorListView : VisualElement
    {
        const float k_DragThreshold = 4f;
        const long k_ChangePollMs = 500;
        const int k_EnterDelayMs = 20;

        sealed class Row
        {
            public VisualElement Root;
            public VisualElement Line;
            public VisualElement Body;
            public MoveProcessor Processor;
            public int Index;
        }

        readonly SerializedProperty m_List;
        readonly SerializedObject m_Object;
        readonly Character m_Host;
        readonly Action m_OnChanged;
        readonly VisualElement m_Rows;
        readonly VisualElement m_AddRow;
        readonly VisualElement m_PaletteHost;
        readonly List<Row> m_RowViews = new List<Row>();
        readonly HashSet<string> m_Open = new HashSet<string>();

        int m_LastHash;
        Type m_JustAdded;
        bool m_PaletteOpen;
        bool m_Released;

        // Drag state.
        Row m_Pressed;
        bool m_Dragging;
        int m_PointerId = -1;
        Vector2 m_PressPosition;
        float m_DragOriginTop;
        int m_DropIndex;
        VisualElement m_DropLine;
        VisualElement m_Spacer;

        /// <summary>
        /// Creates the list over <paramref name="processors"/> on <paramref name="host"/>.
        /// <paramref name="onChanged"/> runs after any add, remove, reorder or checkbox change.
        /// </summary>
        public ProcessorListView(SerializedProperty processors, Character host, Action onChanged = null)
        {
            m_List = processors;
            m_Object = processors?.serializedObject;
            m_Host = host;
            m_OnChanged = onChanged;

            AddToClassList("blocks-steps");
            AddToClassList("blocks-groups");

            m_Rows = new VisualElement();
            m_Rows.AddToClassList("blocks-steps__rows");
            Add(m_Rows);

            m_AddRow = BuildAddRow();
            Add(m_AddRow);

            m_PaletteHost = new VisualElement();
            Add(m_PaletteHost);

            if (m_List == null || m_Object == null || m_Host == null)
            {
                var hint = new Label("(Multi-object editing is not supported)");
                hint.AddToClassList("blocks-text--dim");
                m_Rows.Add(hint);
                m_AddRow.SetEnabled(false);
                return;
            }

            // Hooked on attach so a re-attached list (rebuilt inspector, re-docked tab) still redraws on Undo.
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed -= OnUndoRedo;
                if (!m_Released) Undo.undoRedoPerformed += OnUndoRedo;
            });
            RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);
            schedule.Execute(RebuildIfChanged).Every(k_ChangePollMs);

            Rebuild();
        }

        /// <summary>
        /// Stops the list listening, refreshing and editing, and greys it out. Call it from the owning editor's
        /// <c>OnDisable</c>: Unity disposes the serialized object there, and may not detach the list first.
        /// </summary>
        internal void Release()
        {
            m_Released = true;
            Undo.undoRedoPerformed -= OnUndoRedo;
            SetEnabled(false);
            this.Unbind();
        }

        /// <summary>How many processors the list shows, on or off.</summary>
        public int Count => m_Host != null ? m_Host.Processors.Count : 0;

        /// <summary>How many of them are ticked.</summary>
        public int ActiveCount
        {
            get
            {
                if (m_Host == null) return 0;
                int active = 0;
                foreach (MoveProcessor processor in m_Host.Processors)
                    if (processor != null && processor.Enabled) active++;
                return active;
            }
        }

        /// <summary>Whether any row has its settings closed.</summary>
        public bool AnyClosed
        {
            get
            {
                for (int i = 0; i < m_RowViews.Count; i++)
                {
                    string key = KeyOf(i);
                    if (key != null && m_RowViews[i].Processor != null && !m_Open.Contains(key)) return true;
                }

                return false;
            }
        }

        /// <summary>Opens or closes every row's settings.</summary>
        public void SetAllOpen(bool open)
        {
            for (int i = 0; i < m_RowViews.Count; i++)
            {
                string key = KeyOf(i);
                if (key == null || m_RowViews[i].Processor == null) continue;
                if (open) m_Open.Add(key);
                else m_Open.Remove(key);
            }

            Rebuild();
        }

        /// <summary>A type's row name: its nicified class name minus "Processor".</summary>
        public static string DisplayName(Type type)
        {
            if (type == null) return "(missing)";
            string name = ObjectNames.NicifyVariableName(type.Name);
            return name.EndsWith(" Processor", StringComparison.Ordinal) ? name.Substring(0, name.Length - 10) : name;
        }

        /// <summary>One line about a processor's tuning, or <c>null</c>.</summary>
        public static string Summarize(MoveProcessor processor, Character host, out bool warn)
        {
            warn = false;

            switch (processor)
            {
                case GroundLocomotionProcessor locomotion:
                    return $"walk {locomotion.MaxSpeed:0.#} m/s · sprint ×{locomotion.SprintMultiplier:0.#} · gravity {locomotion.Gravity:0.#}";

                case JumpProcessor jump:
                {
                    var locomotion = host != null ? host.GetProcessor<GroundLocomotionProcessor>() : null;
                    float gravity = locomotion != null ? Mathf.Abs(locomotion.Gravity) : 9.81f;
                    string height = gravity > 0f ? $" · about {jump.JumpSpeed * jump.JumpSpeed / (2f * gravity):0.0} m high" : string.Empty;
                    return $"{jump.JumpSpeed:0.#} m/s{height} · coyote {jump.CoyoteTime:0.##} s";
                }

                case YawRotationProcessor yaw:
                    return yaw.TurnRate <= 0f ? "turns instantly" : $"turns {yaw.TurnRate:0}°/s";

                default:
                    return null;
            }
        }

        static string Describe(Type type)
        {
            if (type == typeof(GroundLocomotionProcessor)) return "Walking and gravity: speed, acceleration, sprint, and air control.";
            if (type == typeof(JumpProcessor)) return "A jump with coyote time.";
            if (type == typeof(YawRotationProcessor)) return "Turns the character to face where the input points.";
            return null;
        }

        static bool IsShipped(Type type) =>
            type == typeof(GroundLocomotionProcessor) || type == typeof(JumpProcessor) || type == typeof(YawRotationProcessor);

        static int Order(Type type)
        {
            if (type == typeof(GroundLocomotionProcessor)) return 0;
            if (type == typeof(JumpProcessor)) return 1;
            if (type == typeof(YawRotationProcessor)) return 2;
            return 100;
        }

        void OnUndoRedo()
        {
            if (m_Released || m_Object == null || m_Object.targetObject == null) return;
            m_Object.Update();
            Rebuild();
            m_OnChanged?.Invoke();
        }

        void RebuildIfChanged()
        {
            if (m_Released || m_Host == null || m_Dragging) return;

            int hash = ComputeHash();
            if (hash == m_LastHash) return;

            m_Object.Update();
            Rebuild();
            m_OnChanged?.Invoke();
        }

        // Order-sensitive so a reorder redraws. Every public field is hashed by value, so tuning edits
        // redraw at the next poll without rebuilding summary strings.
        int ComputeHash()
        {
            int hash = 17;
            foreach (MoveProcessor processor in m_Host.Processors)
            {
                if (processor == null)
                {
                    hash = unchecked(hash * 31);
                    continue;
                }

                hash = unchecked(hash * 31 + processor.GetHashCode());
                hash = unchecked(hash * 31 + (processor.Enabled ? 1 : 0));
                foreach (System.Reflection.FieldInfo field in FieldsOf(processor.GetType()))
                {
                    object value = field.GetValue(processor);
                    hash = unchecked(hash * 31 + (value != null ? value.GetHashCode() : 0));
                }
            }

            return hash;
        }

        static readonly Dictionary<Type, System.Reflection.FieldInfo[]> s_Fields = new Dictionary<Type, System.Reflection.FieldInfo[]>();

        /// <summary>The serialized (public, non-static) fields of a processor type, cached per type.</summary>
        static System.Reflection.FieldInfo[] FieldsOf(Type type)
        {
            if (!s_Fields.TryGetValue(type, out System.Reflection.FieldInfo[] fields))
            {
                fields = type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                s_Fields[type] = fields;
            }

            return fields;
        }

        void Rebuild()
        {
            if (m_Dragging || m_Host == null) return;

            m_Rows.Clear();
            m_RowViews.Clear();
            m_LastHash = ComputeHash();

            int count = Mathf.Min(m_List.arraySize, m_Host.Processors.Count);
            if (count == 0)
            {
                var empty = new Label("No processors: this character applies no forces at all. It will not fall.");
                empty.AddToClassList("blocks-steps__empty");
                m_Rows.Add(empty);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Row row = BuildRow(i, count);
                m_RowViews.Add(row);
                m_Rows.Add(row.Root);
            }

            if (m_JustAdded != null)
            {
                Row last = m_RowViews[m_RowViews.Count - 1];
                if (last.Processor != null && last.Processor.GetType() == m_JustAdded)
                {
                    last.Line.AddToClassList("blocks-step--enter");
                    last.Line.schedule.Execute(() => last.Line.RemoveFromClassList("blocks-step--enter"))
                        .StartingIn(k_EnterDelayMs);
                }

                m_JustAdded = null;
            }
        }

        Row BuildRow(int index, int count)
        {
            SerializedProperty property = m_List.GetArrayElementAtIndex(index);
            MoveProcessor processor = m_Host.Processors[index];
            var row = new Row { Processor = processor, Index = index };

            var root = new VisualElement();
            root.AddToClassList("blocks-step-frame");
            row.Root = root;

            var line = new VisualElement
            {
                tooltip = "Click to show or hide its settings · drag to reorder · right-click for more",
            };
            line.AddToClassList("blocks-step");
            root.Add(line);
            row.Line = line;

            // Checkbox on the left.
            SerializedProperty enabled = property.FindPropertyRelative(nameof(MoveProcessor.Enabled));
            var toggle = new Toggle { tooltip = "Untick to stop this processor running. It keeps its place in the list." };
            toggle.AddToClassList("blocks-step__enable");
            toggle.AddToClassList("blocks-step__enable--left");
            if (enabled != null)
            {
                toggle.BindProperty(enabled);
                toggle.RegisterValueChangedCallback(evt =>
                {
                    line.EnableInClassList("blocks-step--muted", !evt.newValue);
                    m_LastHash = ComputeHash();
                    m_OnChanged?.Invoke();
                });
            }
            else
            {
                toggle.SetEnabled(false);
            }

            line.Add(toggle);
            if (processor != null && !processor.Enabled) line.AddToClassList("blocks-step--muted");

            // Node with the run-order number.
            var node = new VisualElement { tooltip = "Run order. Processors run top to bottom every tick." };
            node.AddToClassList("blocks-step__node");
            node.AddToClassList("blocks-step__node--inline");
            var number = new Label((index + 1).ToString());
            number.AddToClassList("blocks-step__node-label");
            node.Add(number);
            line.Add(node);

            // Name + summary.
            var text = new VisualElement();
            text.AddToClassList("blocks-step__text");
            var name = new Label(processor != null ? DisplayName(processor.GetType()) : "(missing processor: its script was removed or renamed)");
            name.AddToClassList("blocks-step__name");
            text.Add(name);

            bool warn = true;
            string summary = processor != null
                ? Summarize(processor, m_Host, out warn)
                : "Right-click to remove it, or bring the script back.";

            if (string.IsNullOrEmpty(summary))
            {
                line.AddToClassList("blocks-step--compact");
            }
            else
            {
                var summaryLabel = new Label(summary);
                summaryLabel.AddToClassList("blocks-step__summary");
                if (warn) summaryLabel.AddToClassList("blocks-step__summary--warn");
                text.Add(summaryLabel);
            }

            line.Add(text);

            var remove = new Button(() => RemoveProcessor(index)) { text = "✕", tooltip = "Remove this processor (Undo brings it back)" };
            remove.AddToClassList("blocks-step__remove");
            line.Add(remove);

            line.Add(BuildGrip());

            // Settings under the row.
            row.Body = new VisualElement();
            row.Body.AddToClassList("blocks-step__body");
            root.Add(row.Body);

            string key = KeyOf(index);
            bool open = key != null && processor != null && m_Open.Contains(key);
            if (open) FillBody(row, property);
            row.Body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            line.EnableInClassList("blocks-step--open", open);

            line.RegisterCallback<PointerDownEvent>(evt => OnPointerDown(evt, row));
            line.RegisterCallback<PointerMoveEvent>(evt => OnPointerMove(evt, row));
            line.RegisterCallback<PointerUpEvent>(evt => OnPointerUp(evt, row));
            line.RegisterCallback<PointerCaptureOutEvent>(evt => OnPointerCaptureOut(evt, row));
            line.AddManipulator(new ContextualMenuManipulator(evt => PopulateMenu(evt, row, count)));

            return row;
        }

        static VisualElement BuildGrip()
        {
            var grip = new VisualElement { pickingMode = PickingMode.Ignore };
            grip.AddToClassList("blocks-grip");
            for (int i = 0; i < 6; i++)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("blocks-grip__dot");
                grip.Add(dot);
            }

            return grip;
        }

        /// <summary>Every serialized field the processor declares; Enabled is the checkbox on the row.</summary>
        void FillBody(Row row, SerializedProperty property)
        {
            row.Body.Clear();

            int fields = 0;
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                if (child.name == nameof(MoveProcessor.Enabled)) continue;

                var field = new PropertyField();
                field.BindProperty(child.Copy());
                row.Body.Add(field);
                fields++;
            }

            if (fields == 0)
            {
                var none = new Label("Nothing to tune.");
                none.AddToClassList("blocks-panel__hint");
                none.style.marginTop = 0;
                row.Body.Add(none);
            }
        }

        /// <summary>A key for a row's open state that survives rebuilds: the object plus the reference id.</summary>
        string KeyOf(int index)
        {
            if (m_Object == null || m_Object.targetObject == null || index < 0 || index >= m_List.arraySize) return null;

            long id;
            try
            {
                id = m_List.GetArrayElementAtIndex(index).managedReferenceId;
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

        void ToggleOpen(Row row)
        {
            string key = KeyOf(row.Index);
            if (key == null || row.Processor == null) return;

            bool open = !m_Open.Contains(key);
            if (open)
            {
                m_Open.Add(key);
                FillBody(row, m_List.GetArrayElementAtIndex(row.Index));
            }
            else
            {
                m_Open.Remove(key);
                row.Body.Clear();
            }

            row.Body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            row.Line.EnableInClassList("blocks-step--open", open);
            m_OnChanged?.Invoke();
        }

        void PopulateMenu(ContextualMenuPopulateEvent evt, Row row, int count)
        {
            evt.menu.AppendAction("Move up", _ => MoveTo(row.Index, row.Index - 1),
                row.Index > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Move down", _ => MoveTo(row.Index, row.Index + 1),
                row.Index < count - 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Duplicate", _ => Duplicate(row.Index),
                row.Processor != null && !IsShipped(row.Processor.GetType())
                    ? DropdownMenuAction.Status.Normal
                    : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Remove processor", _ => RemoveProcessor(row.Index));
        }

        void Commit(string undoName)
        {
            m_Object.ApplyModifiedProperties();
            if (m_Object.targetObject != null) Undo.SetCurrentGroupName(undoName);
            m_Object.Update();
            Rebuild();
            m_OnChanged?.Invoke();
        }

        void AddEntry(Type type)
        {
            if (m_Released) return;
            MoveProcessor processor;
            try
            {
                processor = (MoveProcessor)Activator.CreateInstance(type);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, m_Object.targetObject);
                return;
            }

            m_Object.Update();
            int index = m_List.arraySize;
            m_List.InsertArrayElementAtIndex(index);
            m_List.GetArrayElementAtIndex(index).managedReferenceValue = processor;
            m_JustAdded = type;
            Commit($"Add {DisplayName(type)} processor");
        }

        void RemoveProcessor(int index)
        {
            if (m_Released) return;
            m_Object.Update();
            if (index < 0 || index >= m_List.arraySize) return;

            // Deleting a slot that still holds a reference can clear it instead of removing it.
            SerializedProperty element = m_List.GetArrayElementAtIndex(index);
            element.managedReferenceValue = null;
            int before = m_List.arraySize;
            m_List.DeleteArrayElementAtIndex(index);
            if (m_List.arraySize == before)
                m_List.DeleteArrayElementAtIndex(index);

            Commit("Remove processor");
        }

        void MoveTo(int from, int to)
        {
            if (m_Released) return;
            m_Object.Update();
            if (from == to || to < 0 || to >= m_List.arraySize) return;

            m_List.MoveArrayElement(from, to);
            Commit("Reorder processors");
        }

        void Duplicate(int index)
        {
            if (m_Released) return;
            m_Object.Update();
            if (index < 0 || index >= m_Host.Processors.Count) return;

            MoveProcessor source = m_Host.Processors[index];
            if (source == null) return;

            var copy = (MoveProcessor)JsonUtility.FromJson(JsonUtility.ToJson(source), source.GetType());
            if (copy == null) return;

            m_List.InsertArrayElementAtIndex(index + 1);
            m_List.GetArrayElementAtIndex(index + 1).managedReferenceValue = copy;
            Commit("Duplicate processor");
        }

        static bool IsControl(VisualElement element, VisualElement stopAt)
        {
            while (element != null && element != stopAt)
            {
                if (element is Toggle || element is Button) return true;
                element = element.parent;
            }

            return false;
        }

        void OnPointerDown(PointerDownEvent evt, Row row)
        {
            if (evt.button != 0 || m_Pressed != null) return;
            if (IsControl(evt.target as VisualElement, row.Line)) return;

            m_Pressed = row;
            m_PointerId = evt.pointerId;
            m_PressPosition = evt.position;
            m_Dragging = false;
            row.Line.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt, Row row)
        {
            if (m_Pressed != row || !row.Line.HasPointerCapture(m_PointerId)) return;

            float dy = evt.position.y - m_PressPosition.y;
            if (!m_Dragging)
            {
                if (Mathf.Abs(dy) < k_DragThreshold || m_RowViews.Count < 2) return;
                BeginDrag(row);
            }

            UpdateDrag(evt.position.y);
        }

        void OnPointerUp(PointerUpEvent evt, Row row)
        {
            if (m_Pressed != row) return;

            if (row.Line.HasPointerCapture(m_PointerId))
                row.Line.ReleasePointer(m_PointerId);

            bool wasDragging = m_Dragging;
            m_Pressed = null;
            m_PointerId = -1;

            if (!wasDragging)
            {
                ToggleOpen(row);
                return;
            }

            EndDrag(row, apply: true);
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt, Row row)
        {
            // Lost the pointer mid-drag (focus change, window moved): put everything back.
            if (m_Pressed != row) return;

            m_Pressed = null;
            m_PointerId = -1;
            if (m_Dragging) EndDrag(row, apply: false);
        }

        void BeginDrag(Row row)
        {
            m_Dragging = true;
            ClosePalette();

            // The row's frame floats over the list; a spacer holds its slot open.
            Rect frame = row.Root.worldBound;
            Rect mine = worldBound;
            m_DragOriginTop = frame.y - mine.y;

            m_Spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Spacer.style.height = frame.height;
            m_Rows.Insert(m_Rows.IndexOf(row.Root), m_Spacer);

            row.Root.RemoveFromHierarchy();
            Add(row.Root);
            row.Root.AddToClassList("blocks-step-frame--dragging");
            row.Line.AddToClassList("blocks-step--dragging");
            row.Root.style.position = Position.Absolute;
            row.Root.style.left = frame.x - mine.x;
            row.Root.style.width = frame.width;
            row.Root.style.top = m_DragOriginTop;
            row.Root.pickingMode = PickingMode.Ignore;

            m_DropLine = new VisualElement { pickingMode = PickingMode.Ignore };
            m_DropLine.AddToClassList("blocks-drop-line");
            Add(m_DropLine);

            m_DropIndex = row.Index;
            PlaceDropLine(row);
        }

        void UpdateDrag(float pointerY)
        {
            Row row = m_Pressed;
            if (row == null) return;

            row.Root.style.top = m_DragOriginTop + (pointerY - m_PressPosition.y);
            m_DropIndex = FindDropIndex(pointerY, row);
            PlaceDropLine(row);
        }

        /// <summary>The gap nearest the pointer: above each other row, or below the last.</summary>
        int FindDropIndex(float y, Row dragged)
        {
            int best = dragged.Index;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < m_RowViews.Count; i++)
            {
                Row other = m_RowViews[i];
                Rect bound = other == dragged && m_Spacer != null ? m_Spacer.worldBound : other.Root.worldBound;
                Consider(i, bound.yMin, y, ref best, ref bestDistance);
                Consider(i + 1, bound.yMax, y, ref best, ref bestDistance);
            }

            return best;
        }

        static void Consider(int index, float edge, float y, ref int best, ref float bestDistance)
        {
            float distance = Mathf.Abs(edge - y);
            if (distance >= bestDistance) return;
            bestDistance = distance;
            best = index;
        }

        void PlaceDropLine(Row dragged)
        {
            if (m_DropLine == null) return;

            Rect mine = worldBound;
            Rect rows = m_Rows.worldBound;
            float y;
            if (m_DropIndex < m_RowViews.Count)
            {
                Row at = m_RowViews[m_DropIndex];
                y = at == dragged && m_Spacer != null ? m_Spacer.worldBound.yMin : at.Root.worldBound.yMin;
            }
            else
            {
                Row last = m_RowViews[m_RowViews.Count - 1];
                y = last == dragged && m_Spacer != null ? m_Spacer.worldBound.yMax : last.Root.worldBound.yMax;
            }

            m_DropLine.style.display = DisplayStyle.Flex;
            m_DropLine.style.left = rows.xMin - mine.xMin + 30;
            m_DropLine.style.width = rows.width - 34;
            m_DropLine.style.top = y - mine.yMin - 1;
        }

        void EndDrag(Row row, bool apply)
        {
            m_Dragging = false;
            int target = m_DropIndex;

            m_DropLine?.RemoveFromHierarchy();
            m_DropLine = null;
            m_Spacer?.RemoveFromHierarchy();
            m_Spacer = null;
            row.Root.RemoveFromHierarchy();

            // The drop index counts gaps in the list as it was; the slot below itself is no move.
            bool moved = apply && target != row.Index && target != row.Index + 1;
            if (moved)
            {
                int to = target > row.Index ? target - 1 : target;
                MoveTo(row.Index, to);
            }
            else
            {
                Rebuild();
            }
        }

        VisualElement BuildAddRow()
        {
            var row = new VisualElement { tooltip = "Add a processor at the end" };
            row.AddToClassList("blocks-step-add");

            var rail = new VisualElement();
            rail.AddToClassList("blocks-step-add__rail");
            var node = new VisualElement();
            node.AddToClassList("blocks-step-add__node");
            // Two bars rather than a "+" glyph, which sits off-centre in a 16px circle.
            node.Add(Bar("blocks-step-add__bar--h"));
            node.Add(Bar("blocks-step-add__bar--v"));
            rail.Add(node);
            row.Add(rail);

            var label = new Label("Add a processor");
            label.AddToClassList("blocks-step-add__label");
            row.Add(label);

            row.RegisterCallback<ClickEvent>(_ => TogglePalette());
            return row;
        }

        static VisualElement Bar(string modifier)
        {
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("blocks-step-add__bar");
            bar.AddToClassList(modifier);
            return bar;
        }

        void TogglePalette()
        {
            if (m_PaletteOpen) ClosePalette();
            else OpenPalette();
        }

        void ClosePalette()
        {
            if (!m_PaletteOpen) return;
            m_PaletteOpen = false;
            m_AddRow.RemoveFromClassList("blocks-step-add--open");
            m_PaletteHost.Clear();
        }

        void OpenPalette()
        {
            m_PaletteOpen = true;
            m_AddRow.AddToClassList("blocks-step-add--open");
            m_PaletteHost.Clear();

            var present = new HashSet<Type>();
            foreach (MoveProcessor processor in m_Host.Processors)
                if (processor != null) present.Add(processor.GetType());

            List<Type> types = OfferableTypes();
            if (types.Count == 0)
            {
                var none = new Label("(no MoveProcessor classes found)");
                none.AddToClassList("blocks-palette__none");
                m_PaletteHost.Add(none);
                return;
            }

            var palette = new VisualElement();
            palette.AddToClassList("blocks-palette");

            for (int i = 0; i < types.Count; i++)
            {
                Type type = types[i];

                // A shipped processor already present is dimmed, not hidden, so the palette stays complete.
                bool blocked = present.Contains(type) && IsShipped(type);
                VisualElement chip = BuildChip(type, blocked);
                chip.AddToClassList("blocks-chip--enter");
                palette.Add(chip);

                int delay = k_EnterDelayMs + i * 28;
                chip.schedule.Execute(() => chip.RemoveFromClassList("blocks-chip--enter")).StartingIn(delay);
            }

            m_PaletteHost.Add(palette);
        }

        VisualElement BuildChip(Type type, bool blocked)
        {
            var chip = new VisualElement();
            chip.AddToClassList("blocks-chip");

            var title = new Label(DisplayName(type));
            title.AddToClassList("blocks-chip__title");
            chip.Add(title);

            string description = Describe(type);
            if (!string.IsNullOrEmpty(description))
            {
                var desc = new Label(description);
                desc.AddToClassList("blocks-chip__desc");
                chip.Add(desc);
            }

            if (blocked)
            {
                chip.AddToClassList("blocks-chip--disabled");
                chip.tooltip = "Already on this character; only one is allowed.";
            }
            else
            {
                chip.RegisterCallback<ClickEvent>(_ =>
                {
                    AddEntry(type);
                    ClosePalette();
                });
            }

            return chip;
        }

        /// <summary>
        /// Concrete, public, top-level <see cref="MoveProcessor"/> classes with a parameterless constructor
        /// from non-test assemblies, shipped ones first. Test doubles are nested fixture classes and would
        /// otherwise show as "Fixture/Name".
        /// </summary>
        internal static List<Type> OfferableTypes()
        {
            var types = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<MoveProcessor>())
            {
                if (type.IsAbstract || !type.IsVisible || type.IsNested) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                string assembly = type.Assembly.GetName().Name ?? string.Empty;
                if (assembly.EndsWith("Tests", StringComparison.Ordinal) ||
                    assembly.Contains(".Tests.") ||
                    assembly.EndsWith(".TestFramework", StringComparison.Ordinal))
                {
                    continue;
                }

                types.Add(type);
            }

            types.Sort((a, b) =>
            {
                int order = Order(a).CompareTo(Order(b));
                return order != 0 ? order : string.CompareOrdinal(a.Name, b.Name);
            });

            return types;
        }
    }
}
