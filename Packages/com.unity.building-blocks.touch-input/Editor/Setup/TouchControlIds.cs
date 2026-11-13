using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// The rules for control ids. A new control gets the next free auto id of its kind (<c>Joystick1</c>), and its
    /// first action pick renames it after the action (<c>Move</c>, <c>Move2</c>).
    /// </summary>
    /// <remarks>
    /// An id the designer typed is never renamed. <see cref="Apply"/> names the GameObject after the id, but a
    /// Hierarchy rename leaves the id alone. Ids compare ignoring case and anything but letters, digits and
    /// underscores, because the Input System would give those the same control name.
    /// </remarks>
    static class TouchControlIds
    {
        public const string JoystickPrefix = "Joystick";
        public const string ButtonPrefix = "Button";

        static readonly string[] k_AutoPrefixes = { JoystickPrefix, ButtonPrefix };

        /// <summary>
        /// Whether <paramref name="id"/> looks like an auto id: a known prefix followed by digits only.
        /// A typed <c>Joystick3</c> counts as one too.
        /// </summary>
        public static bool IsAutoId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            foreach (string prefix in k_AutoPrefixes)
            {
                if (id.Length <= prefix.Length || !id.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                bool digits = true;
                for (int i = prefix.Length; i < id.Length && digits; i++)
                    digits = id[i] >= '0' && id[i] <= '9';

                if (digits)
                    return true;
            }

            return false;
        }

        /// <summary>The id's collision key: its letters, digits and underscores, lower-cased.</summary>
        public static string Key(string id)
        {
            if (string.IsNullOrEmpty(id))
                return string.Empty;

            var key = new StringBuilder(id.Length);
            foreach (char c in id)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    key.Append(char.ToLowerInvariant(c));
            }

            return key.ToString();
        }

        /// <summary>Turns an action name into an id by keeping letters, digits and underscores ("Unlock Cursor" becomes "UnlockCursor").</summary>
        public static string FromActionName(string actionName)
        {
            var id = new StringBuilder();
            if (!string.IsNullOrEmpty(actionName))
            {
                foreach (char c in actionName)
                {
                    if (char.IsLetterOrDigit(c) || c == '_')
                        id.Append(c);
                }
            }

            if (id.Length == 0)
                return "Control";

            if (char.IsDigit(id[0]))
                id.Insert(0, '_');

            return id.ToString();
        }

        /// <summary>
        /// Sets <paramref name="control"/>'s id and names its GameObject after it, as one Undo step.
        /// </summary>
        /// <remarks>It does not update bindings; callers use <c>TouchBindingAuthoring.RenameControl</c> for that.</remarks>
        public static void Apply(TouchControl control, string id, string undoName = "Rename Touch Control")
        {
            if (control == null)
                return;

            id ??= string.Empty;
            if (control.ActionId == id && control.gameObject.name == id)
                return;

            Undo.RecordObjects(new Object[] { control, control.gameObject }, undoName);
            control.ActionId = id;
            if (id.Length > 0)
                control.gameObject.name = id;
            EditorUtility.SetDirty(control);
            EditorUtility.SetDirty(control.gameObject);
        }

        /// <summary>Every control whose nearest rig is <paramref name="rig"/>, inactive ones included.</summary>
        public static List<TouchControl> ControlsOf(TouchRig rig)
        {
            var controls = new List<TouchControl>();
            if (rig == null)
                return controls;

            foreach (TouchControl control in rig.GetComponentsInChildren<TouchControl>(true))
            {
                if (control.GetComponentInParent<TouchRig>(true) == rig)
                    controls.Add(control);
            }

            return controls;
        }

        /// <summary>Whether a control of <paramref name="rig"/> other than <paramref name="except"/> already uses <paramref name="id"/>'s key.</summary>
        public static bool IsTaken(TouchRig rig, string id, TouchControl except = null)
        {
            string key = Key(id);
            foreach (TouchControl control in ControlsOf(rig))
            {
                if (control != except && Key(control.ActionId) == key)
                    return true;
            }

            return false;
        }

        /// <summary>The next free auto id for <paramref name="prefix"/> in <paramref name="rig"/>: <c>Joystick1</c>, <c>Joystick2</c>, …</summary>
        public static string NextAutoId(TouchRig rig, string prefix)
        {
            for (int n = 1; ; n++)
            {
                string candidate = prefix + n;
                if (!IsTaken(rig, candidate))
                    return candidate;
            }
        }

        /// <summary>
        /// Returns <paramref name="wanted"/> if no other control in the rig uses it, else <c>wanted2</c>, <c>wanted3</c>, and so on.
        /// <paramref name="self"/> is ignored, so a control keeps an id it already has.
        /// </summary>
        public static string Unique(TouchRig rig, string wanted, TouchControl self)
        {
            if (!IsTaken(rig, wanted, self))
                return wanted;

            for (int n = 2; ; n++)
            {
                string candidate = wanted + n;
                if (!IsTaken(rig, candidate, self))
                    return candidate;
            }
        }

        /// <summary>
        /// The id <paramref name="control"/> should have once it drives <paramref name="actionName"/>:
        /// its typed id if it has one, else the action's name made unique in the rig.
        /// </summary>
        public static string IdAfterPick(TouchControl control, string actionName)
        {
            if (control == null || !IsAutoId(control.ActionId))
                return control != null ? control.ActionId : string.Empty;

            return Unique(control.GetComponentInParent<TouchRig>(true), FromActionName(actionName), control);
        }
    }
}
