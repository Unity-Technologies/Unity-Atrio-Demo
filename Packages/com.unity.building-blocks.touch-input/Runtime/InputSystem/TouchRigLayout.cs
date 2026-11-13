using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.Utilities;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// Turns a rig's controls into the <c>TouchRig</c> device layout: one device control per rig control, named
    /// after its <see cref="TouchControl.ActionId"/>, in hierarchy order.
    /// </summary>
    /// <remarks>
    /// Planning is separate from building so problems are reported once, by the planner. The Input System may
    /// call the builder many times.
    /// </remarks>
    internal static class TouchRigLayout
    {
        public const string LayoutName = "TouchRig";
        public const string DisplayName = "Touch Rig";
        public static readonly FourCC Format = new FourCC('T', 'R', 'I', 'G');

        public const int StickSizeInBytes = 8;
        public const int ButtonSizeInBytes = 4;

        /// <summary>A device's state cannot be empty, so a rig with no controls still gets this many bytes.</summary>
        public const int MinimumSizeInBytes = 4;

        public enum Kind
        {
            /// <summary>A <see cref="TouchJoystick"/>: layout <c>Stick</c>, <c>VEC2</c>.</summary>
            Stick,

            /// <summary>A <see cref="TouchButton"/>, or any other control, bridged as its <c>IsPressed</c>: layout <c>Button</c>, <c>FLT</c>.</summary>
            Button,
        }

        public readonly struct Entry
        {
            public readonly TouchControl Control;
            public readonly string Name;
            public readonly string DisplayName;
            public readonly Kind Kind;
            public readonly int ByteOffset;
            public readonly int SizeInBytes;

            public Entry(TouchControl control, string name, string displayName, Kind kind, int byteOffset)
            {
                Control = control;
                Name = name;
                DisplayName = displayName;
                Kind = kind;
                ByteOffset = byteOffset;
                SizeInBytes = SizeOf(kind);
            }
        }

        public static int SizeOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.Stick: return StickSizeInBytes;
                default: return ButtonSizeInBytes;
            }
        }

        public static Kind KindOf(TouchControl control)
        {
            switch (control)
            {
                case TouchJoystick _: return Kind.Stick;
                default: return Kind.Button;
            }
        }

        /// <summary>
        /// The control name for an <see cref="TouchControl.ActionId"/>, or empty when nothing is left. Keeps
        /// <c>[A-Za-z0-9_]</c> in camel case: <c>"Look Camera"</c> becomes <c>"lookCamera"</c>.
        /// </summary>
        public static string ControlName(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
                return "";

            var name = new StringBuilder(actionId.Length);
            bool upperNext = false;
            foreach (char c in actionId)
            {
                bool valid = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                if (!valid)
                {
                    upperNext = name.Length > 0;
                    continue;
                }

                if (name.Length == 0)
                    name.Append(char.ToLowerInvariant(c));
                else
                    name.Append(upperNext ? char.ToUpperInvariant(c) : c);

                upperNext = false;
            }

            return name.ToString();
        }

        /// <summary>
        /// One entry per control that can be bridged, in order, with packed byte offsets. A control with an empty
        /// id, or a name already taken (ignoring case), is skipped and described in <paramref name="problems"/>.
        /// </summary>
        public static Entry[] Plan(IReadOnlyList<TouchControl> controls, List<string> problems)
        {
            var entries = new List<Entry>(controls.Count);
            var taken = new Dictionary<string, TouchControl>(StringComparer.OrdinalIgnoreCase);
            int offset = 0;

            for (int i = 0; i < controls.Count; i++)
            {
                TouchControl control = controls[i];
                if (control == null)
                    continue;

                string id = control.ActionId;
                string name = ControlName(id);

                if (name.Length == 0)
                {
                    problems?.Add(string.IsNullOrEmpty(id)
                        ? $"'{control.name}' has no ActionId, so it has no control on <{LayoutName}>. Give it one."
                        : $"'{control.name}': ActionId \"{id}\" has no letter, digit or underscore to name a control after, so it has no control on <{LayoutName}>.");
                    continue;
                }

                if (taken.TryGetValue(name, out TouchControl first))
                {
                    problems?.Add(
                        $"'{first.name}' (\"{first.ActionId}\") and '{control.name}' (\"{id}\") both become <{LayoutName}>/{name}; " +
                        $"'{control.name}' is left off the device. Give one a different ActionId.");
                    continue;
                }

                taken.Add(name, control);
                var entry = new Entry(control, name, id, KindOf(control), offset);
                entries.Add(entry);
                offset += entry.SizeInBytes;
            }

            return entries.ToArray();
        }

        /// <summary>Bytes the entries occupy, before the <see cref="MinimumSizeInBytes"/> floor.</summary>
        public static int TotalSize(IReadOnlyList<Entry> entries)
        {
            int total = 0;
            for (int i = 0; i < entries.Count; i++)
                total += entries[i].SizeInBytes;
            return total;
        }

        /// <summary>The device layout for these entries.</summary>
        public static InputControlLayout Build(IReadOnlyList<Entry> entries)
        {
            var builder = new InputControlLayout.Builder()
                .WithName(LayoutName)
                .WithDisplayName(DisplayName)
                .WithType<TouchRigDevice>()
                .WithFormat(Format)
                .WithSizeInBytes(Math.Max(MinimumSizeInBytes, TotalSize(entries)));

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                InputControlLayout.Builder.ControlBuilder control = builder.AddControl(entry.Name)
                    .WithDisplayName(entry.DisplayName)
                    .WithByteOffset((uint)entry.ByteOffset);

                switch (entry.Kind)
                {
                    case Kind.Stick:
                        control.WithLayout("Stick").WithFormat("VEC2");
                        break;

                    case Kind.Button:
                        control.WithLayout("Button").WithFormat("FLT");
                        break;
                }
            }

            return builder.Build();
        }

        /// <summary>The device layout for these controls. Problems are dropped; call <see cref="Plan"/> to see them.</summary>
        public static InputControlLayout Build(IReadOnlyList<TouchControl> controls) => Build(Plan(controls, null));

        /// <summary>
        /// Every binding path the entries offer, in order: <c>&lt;TouchRig&gt;/move</c> for a stick or button.
        /// </summary>
        public static void BindingPaths(IReadOnlyList<Entry> entries, List<string> into)
        {
            into.Clear();
            for (int i = 0; i < entries.Count; i++)
                into.Add(PathPrefix + entries[i].Name);
        }

        public const string PathPrefix = "<" + LayoutName + ">/";

        /// <summary>
        /// Whether an action can take a control's value. A stick fits non-Button actions expecting a Vector2, Stick,
        /// Dpad or Delta; a button fits Button actions, or actions expecting a Button, Axis or Analog.
        /// </summary>
        /// <remarks>The Editor's Mappings card and <see cref="TouchRebinding.Map"/> both use this rule.</remarks>
        public static bool Accepts(UnityEngine.InputSystem.InputActionType type, string expectedControlType, bool givesVector2)
        {
            string expected = expectedControlType ?? string.Empty;
            if (givesVector2)
                return type != UnityEngine.InputSystem.InputActionType.Button && IsOneOf(expected, "Vector2", "Stick", "Dpad", "Delta");

            return type == UnityEngine.InputSystem.InputActionType.Button || IsOneOf(expected, "Button", "Axis", "Analog");
        }

        static bool IsOneOf(string value, params string[] options)
        {
            foreach (string option in options)
            {
                if (string.Equals(value, option, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
