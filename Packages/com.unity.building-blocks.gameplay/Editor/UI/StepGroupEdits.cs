using System.Collections.Generic;
using UnityEditor;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Structural edits to an interactable's serialized <c>List&lt;StepGroup&gt;</c>: add, delete, join,
    /// split and move steps, and add, delete and move groups. Edits go through the
    /// <see cref="SerializedProperty"/>; the caller applies them.
    /// </summary>
    /// <remarks>
    /// A group stays, empty, when its last step is deleted; an empty group is skipped when it plays. A step
    /// moved out of a group with no name takes that group with it, since the group only wrapped that step.
    /// </remarks>
    public static class StepGroupEdits
    {
        /// <summary>The steps array of group <paramref name="groupIndex"/>, or <c>null</c> when out of range.</summary>
        public static SerializedProperty StepsOf(SerializedProperty groups, int groupIndex)
        {
            if (groups == null || groupIndex < 0 || groupIndex >= groups.arraySize) return null;
            return groups.GetArrayElementAtIndex(groupIndex).FindPropertyRelative("Steps");
        }

        /// <summary>
        /// Removes one slot of a managed-reference array, clearing the reference first so the slot is
        /// actually removed.
        /// </summary>
        public static void RemoveElement(SerializedProperty array, int index)
        {
            if (array == null || index < 0 || index >= array.arraySize) return;

            SerializedProperty element = array.GetArrayElementAtIndex(index);
            if (element.propertyType == SerializedPropertyType.ManagedReference)
                element.managedReferenceValue = null;

            int before = array.arraySize;
            array.DeleteArrayElementAtIndex(index);
            if (array.arraySize == before)
                array.DeleteArrayElementAtIndex(index);
        }

        /// <summary>Whether group <paramref name="groupIndex"/> has a name the user typed.</summary>
        public static bool IsNamed(SerializedProperty groups, int groupIndex)
        {
            if (groups == null || groupIndex < 0 || groupIndex >= groups.arraySize) return false;
            SerializedProperty name = groups.GetArrayElementAtIndex(groupIndex).FindPropertyRelative("Name");
            return name != null && !string.IsNullOrWhiteSpace(name.stringValue);
        }

        /// <summary>
        /// Adds a group with no steps at <paramref name="groupIndex"/> (the end when out of range). Returns
        /// where it landed, or <c>-1</c> when <paramref name="groups"/> is <c>null</c>.
        /// </summary>
        public static int AddEmptyGroup(SerializedProperty groups, int groupIndex = -1)
        {
            if (groups == null) return -1;

            int at = groupIndex < 0 || groupIndex > groups.arraySize ? groups.arraySize : groupIndex;
            groups.InsertArrayElementAtIndex(at);
            groups.GetArrayElementAtIndex(at).managedReferenceValue = new StepGroup();
            return at;
        }

        /// <summary>
        /// Deletes the step at (<paramref name="groupIndex"/>, <paramref name="index"/>). Its group stays, empty
        /// when this was its last step. False when the slot is out of range.
        /// </summary>
        public static bool RemoveStep(SerializedProperty groups, int groupIndex, int index)
        {
            SerializedProperty steps = StepsOf(groups, groupIndex);
            if (steps == null || index < 0 || index >= steps.arraySize) return false;

            RemoveElement(steps, index);
            return true;
        }

        /// <summary>Deletes group <paramref name="groupIndex"/> and every step in it. False when out of range.</summary>
        public static bool RemoveGroup(SerializedProperty groups, int groupIndex)
        {
            if (groups == null || groupIndex < 0 || groupIndex >= groups.arraySize) return false;

            RemoveElement(groups, groupIndex);
            return true;
        }

        /// <summary>
        /// Moves group <paramref name="from"/> to index <paramref name="to"/>, steps and all. False when either
        /// index is out of range or nothing would change.
        /// </summary>
        public static bool MoveGroup(SerializedProperty groups, int from, int to)
        {
            if (groups == null || from == to) return false;
            if (from < 0 || from >= groups.arraySize || to < 0 || to >= groups.arraySize) return false;

            groups.MoveArrayElement(from, to);
            return true;
        }

        /// <summary>
        /// Where a group dragged from <paramref name="from"/> ends up when dropped on the seam before group
        /// <paramref name="slot"/> (the group count for after the last one), or <c>-1</c> when it stays put.
        /// </summary>
        public static int GroupIndexForSlot(int from, int slot)
        {
            int to = slot > from ? slot - 1 : slot;
            return to == from || to < 0 ? -1 : to;
        }

        /// <summary>Appends <paramref name="step"/> to group <paramref name="groupIndex"/>. False when the group does not exist.</summary>
        public static bool AddStep(SerializedProperty groups, int groupIndex, InteractionStep step)
        {
            SerializedProperty steps = StepsOf(groups, groupIndex);
            if (steps == null || step == null) return false;

            int index = steps.arraySize;
            steps.InsertArrayElementAtIndex(index);
            steps.GetArrayElementAtIndex(index).managedReferenceValue = step;
            return true;
        }

        /// <summary>Adds <paramref name="step"/> as a new group at <paramref name="groupIndex"/> (the end when out of range).</summary>
        public static bool AddStepAsNewGroup(SerializedProperty groups, InteractionStep step, int groupIndex = -1)
        {
            if (groups == null || step == null) return false;

            int at = groupIndex < 0 || groupIndex > groups.arraySize ? groups.arraySize : groupIndex;
            groups.InsertArrayElementAtIndex(at);
            groups.GetArrayElementAtIndex(at).managedReferenceValue = new StepGroup(step);
            return true;
        }

        /// <summary>
        /// Moves the step at (<paramref name="groupIndex"/>, <paramref name="index"/>) to the end of the group
        /// above, so it runs alongside it. An unnamed group left empty is removed. False when there is no
        /// group above, the slot is out of range, or the step's script is missing.
        /// </summary>
        public static bool JoinPreviousGroup(SerializedProperty groups, int groupIndex, int index)
        {
            if (groups == null || groupIndex <= 0) return false;
            SerializedProperty source = StepsOf(groups, groupIndex);
            SerializedProperty destination = StepsOf(groups, groupIndex - 1);
            if (source == null || destination == null || index < 0 || index >= source.arraySize) return false;

            var step = source.GetArrayElementAtIndex(index).managedReferenceValue as InteractionStep;
            if (step == null) return false;

            RemoveElement(source, index);
            int at = destination.arraySize;
            destination.InsertArrayElementAtIndex(at);
            destination.GetArrayElementAtIndex(at).managedReferenceValue = step;

            if (source.arraySize == 0 && !IsNamed(groups, groupIndex))
                RemoveElement(groups, groupIndex);

            return true;
        }

        /// <summary>
        /// Splits group <paramref name="groupIndex"/> at <paramref name="index"/> into a new group right after.
        /// False for the first step, an out-of-range slot, or a moved step with a missing script.
        /// </summary>
        public static bool SplitGroupAt(SerializedProperty groups, int groupIndex, int index)
        {
            SerializedProperty steps = StepsOf(groups, groupIndex);
            if (steps == null || index <= 0 || index >= steps.arraySize) return false;

            var moved = new List<InteractionStep>();
            for (int i = index; i < steps.arraySize; i++)
            {
                var step = steps.GetArrayElementAtIndex(i).managedReferenceValue as InteractionStep;
                if (step == null) return false;   // missing script: leave the group alone so its slot is kept
                moved.Add(step);
            }

            for (int i = steps.arraySize - 1; i >= index; i--)
                RemoveElement(steps, i);

            groups.InsertArrayElementAtIndex(groupIndex + 1);
            groups.GetArrayElementAtIndex(groupIndex + 1).managedReferenceValue = new StepGroup(moved.ToArray());
            return true;
        }

        /// <summary>
        /// Moves the step at (<paramref name="fromGroup"/>, <paramref name="fromIndex"/>), either as a new group
        /// before <paramref name="toGroup"/> (<paramref name="asNewGroup"/>) or into <paramref name="toGroup"/> at
        /// <paramref name="toIndex"/>. Targets are indices before the move. An unnamed group left empty is
        /// removed; a named one stays. False when nothing moved.
        /// </summary>
        public static bool MoveStep(SerializedProperty groups, int fromGroup, int fromIndex, int toGroup, int toIndex, bool asNewGroup)
        {
            SerializedProperty source = StepsOf(groups, fromGroup);
            if (source == null || fromIndex < 0 || fromIndex >= source.arraySize) return false;

            if (!asNewGroup && fromGroup == toGroup)
            {
                int to = toIndex > fromIndex ? toIndex - 1 : toIndex;
                if (to == fromIndex || to < 0 || to >= source.arraySize) return false;
                source.MoveArrayElement(fromIndex, to);
                return true;
            }

            var step = source.GetArrayElementAtIndex(fromIndex).managedReferenceValue as InteractionStep;
            if (step == null) return false;

            // A group's only step dropped on its own group's edge is already there, named or not.
            bool alone = source.arraySize == 1;
            if (asNewGroup && alone && (toGroup == fromGroup || toGroup == fromGroup + 1))
                return false;

            // The only step of an unnamed group takes the group along; a named group stays behind, empty.
            bool takesGroup = alone && !IsNamed(groups, fromGroup);

            RemoveElement(source, fromIndex);

            int insertAt = toGroup;
            if (takesGroup)
            {
                // The emptied source group goes; anything past it shifts up one.
                RemoveElement(groups, fromGroup);
                if (insertAt > fromGroup) insertAt--;
            }

            if (asNewGroup)
            {
                insertAt = insertAt < 0 || insertAt > groups.arraySize ? groups.arraySize : insertAt;
                groups.InsertArrayElementAtIndex(insertAt);
                groups.GetArrayElementAtIndex(insertAt).managedReferenceValue = new StepGroup(step);
                return true;
            }

            SerializedProperty destination = StepsOf(groups, insertAt);
            if (destination == null)
            {
                // The target vanished with the source group: keep the step as its own group at the end.
                groups.InsertArrayElementAtIndex(groups.arraySize);
                groups.GetArrayElementAtIndex(groups.arraySize - 1).managedReferenceValue = new StepGroup(step);
                return true;
            }

            int at = toIndex < 0 ? 0 : toIndex > destination.arraySize ? destination.arraySize : toIndex;
            destination.InsertArrayElementAtIndex(at);
            destination.GetArrayElementAtIndex(at).managedReferenceValue = step;
            return true;
        }
    }
}
