using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Binds every <see cref="InputSlot"/> field on a processor to a bit of the command source's
    /// <see cref="IMoveCommandLayout"/>. <see cref="Character"/> calls this when its list or source
    /// changes; a host that drives a <see cref="KinematicMover"/> directly calls it itself.
    /// </summary>
    /// <remarks>
    /// Fields, private and inherited included, are found by reflection and cached per type. Call at
    /// registration, never per tick.
    /// </remarks>
    public static class InputSlotBinder
    {
        static readonly Dictionary<Type, FieldInfo[]> s_SlotFields = new Dictionary<Type, FieldInfo[]>();
        static readonly List<FieldInfo> s_Scratch = new List<FieldInfo>();

        /// <summary>
        /// The <see cref="InputSlot"/> fields <paramref name="type"/> declares or inherits, private
        /// ones included. Cached; an empty array for a type with none.
        /// </summary>
        public static FieldInfo[] SlotFields(Type type)
        {
            if (type == null)
                return Array.Empty<FieldInfo>();

            if (s_SlotFields.TryGetValue(type, out FieldInfo[] cached))
                return cached;

            s_Scratch.Clear();
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo[] declared = t.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                for (int i = 0; i < declared.Length; i++)
                {
                    if (declared[i].FieldType == typeof(InputSlot))
                        s_Scratch.Add(declared[i]);
                }
            }

            FieldInfo[] result = s_Scratch.Count == 0 ? Array.Empty<FieldInfo>() : s_Scratch.ToArray();
            s_SlotFields[type] = result;
            return result;
        }

        /// <summary>
        /// Binds every slot on <paramref name="processor"/> against <paramref name="source"/>: a name
        /// its <see cref="IMoveCommandLayout"/> knows gets its bit, anything else becomes unbound. A
        /// source that is not a layout logs one warning if any slot is named; a missing button logs one
        /// warning per slot, attributed to <paramref name="context"/>. Returns how many slots asked for
        /// a button the source does not have.
        /// </summary>
        public static int BindToSource(object processor, IMoveCommandSource source, UnityEngine.Object context = null)
        {
            if (processor == null)
                return 0;

            var layout = source as IMoveCommandLayout;
            if (layout != null || source == null)
                return Bind(processor, layout, context);

            FieldInfo[] fields = SlotFields(processor.GetType());
            if (fields.Length == 0)
                return 0;

            int named = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                var slot = (InputSlot)fields[i].GetValue(processor);
                if (!string.IsNullOrWhiteSpace(slot.Name))
                    named++;

                fields[i].SetValue(processor, slot.Bound(InputSlot.Unbound));
            }

            if (named > 0)
            {
                Debug.LogWarning(
                    $"{(context != null ? context.name + ": " : string.Empty)}{processor.GetType().Name} reads named " +
                    $"buttons, but the command source ({source.GetType().Name}) does not implement IMoveCommandLayout, " +
                    "so no named input can bind. Implement IMoveCommandLayout on the source (Player Input Source does).", context);
            }

            return named;
        }

        /// <summary>
        /// Binds every slot on <paramref name="processor"/>: a name the <paramref name="layout"/>
        /// knows gets its bit, anything else becomes unbound. A <c>null</c> layout unbinds silently; a
        /// missing button logs one warning per slot, attributed to <paramref name="context"/>. Returns
        /// how many slots asked for a button the layout does not have.
        /// </summary>
        public static int Bind(object processor, IMoveCommandLayout layout, UnityEngine.Object context = null)
        {
            if (processor == null)
                return 0;

            FieldInfo[] fields = SlotFields(processor.GetType());
            int unresolved = 0;

            for (int i = 0; i < fields.Length; i++)
            {
                var slot = (InputSlot)fields[i].GetValue(processor);
                int bit = InputSlot.Unbound;

                if (layout != null && !string.IsNullOrWhiteSpace(slot.Name))
                {
                    bit = layout.FindButton(slot.Name);
                    if (bit == InputSlot.Unbound)
                    {
                        unresolved++;
                        Debug.LogWarning(
                            $"{(context != null ? context.name + ": " : string.Empty)}{processor.GetType().Name} reads " +
                            $"the button '{slot.Name}', but the command source has no custom button called that. Add a " +
                            "row of that name under Player Input Source ▸ Custom Buttons.", context);
                    }
                }

                fields[i].SetValue(processor, slot.Bound(bit));
            }

            return unresolved;
        }
    }
}
