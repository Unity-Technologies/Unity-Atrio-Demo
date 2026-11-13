#if BB_TOUCH_HAS_INPUTSYSTEM
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// The body of the Touch Rig's Mappings card. Each control is a named box listing the actions it drives.
    /// </summary>
    /// <remarks>
    /// Actions added with + Add mapping but not yet given a control sit in a "Choose a control" box at the top.
    /// Renaming a box renames the control's id and moves every binding with it.
    /// </remarks>
    sealed class TouchMappingsView : VisualElement
    {
        const long k_PollMs = 250;

        readonly TouchRig m_Rig;
        readonly InspectorCard m_Card;
        readonly Action m_OnChanged;
        readonly VisualElement m_Host;
        readonly Label m_Note;
        readonly Button m_Add;
        readonly HashSet<string> m_Open = new HashSet<string>();

        List<TouchInputSource> m_Sources = new List<TouchInputSource>();
        List<TouchControl> m_Controls = new List<TouchControl>();
        int m_SeenVersion = -1;
        string m_Key;

        /// <summary>The number of control-to-action mappings drawn.</summary>
        public int MappingCount { get; private set; }

        /// <summary>The number of actions waiting for a control.</summary>
        public int PendingCount { get; private set; }

        /// <summary>Whether <paramref name="control"/> drives at least one scene action.</summary>
        public bool IsMapped(TouchControl control) => m_Sources.Exists(s => s.IsMappedTo(control));

        public TouchMappingsView(TouchRig rig, InspectorCard card, Action onChanged)
        {
            m_Rig = rig;
            m_Card = card;
            m_OnChanged = onChanged;

            AddToClassList("blocks-steps");
            AddToClassList("blocks-groups");
            AddToClassList("blocks-beats");
            AddToClassList("blocks-mappings");

            m_Host = new VisualElement();
            m_Host.AddToClassList("blocks-groups__host");
            Add(m_Host);

            m_Add = new Button { text = "+ Add mapping", tooltip = "Pick an action the scene reads, then choose the control that drives it." };
            m_Add.AddToClassList("blocks-button--quiet");
            m_Add.AddToClassList("blocks-groups__add");
            m_Add.clicked += () => ShowSourceMenu(m_Add, null);
            Add(m_Add);

            m_Note = new Label();
            m_Note.AddToClassList("blocks-touch-note");
            m_Note.style.display = DisplayStyle.None;
            Add(m_Note);

            schedule.Execute(Poll).Every(k_PollMs);
            Refresh(force: true);
        }

        void Poll()
        {
            if (m_Rig == null || TouchEditorChanges.Version == m_SeenVersion)
                return;
            Refresh(force: false);
        }

        /// <summary>Rescans the scene and rebuilds the rows if anything changed, or always with <paramref name="force"/>.</summary>
        public void Refresh(bool force)
        {
            if (m_Rig == null)
                return;

            m_SeenVersion = TouchEditorChanges.Version;
            m_Sources = TouchMappings.Scan();
            m_Controls = TouchControlIds.ControlsOf(m_Rig);

            var key = new StringBuilder();
            key.Append(Application.isPlaying);
            foreach (TouchInputSource source in m_Sources)
                key.Append('|').Append(source.Key).Append(source.Name).Append(string.Join(",", source.ControlNames));
            foreach (TouchControl control in m_Controls)
                key.Append('|').Append(control.GetInstanceIDSafe()).Append(control.ActionId);
            key.Append('|').Append(string.Join(",", TouchMappings.Pending(m_Rig)));

            string k = key.ToString();
            if (!force && k == m_Key)
                return;
            m_Key = k;
            Rebuild();
        }

        void Rebuild()
        {
            m_Host.Clear();
            bool playing = Application.isPlaying;
            m_Add.SetEnabled(!playing);

            // Pending actions have no control yet. Drop the ones mapped elsewhere or gone from the scene.
            var pending = new List<TouchInputSource>();
            foreach (string sourceKey in TouchMappings.Pending(m_Rig))
            {
                TouchInputSource source = TouchMappings.Find(m_Sources, sourceKey);
                if (source == null || MappedInRig(source))
                    TouchMappings.SetPending(m_Rig, sourceKey, false);
                else
                    pending.Add(source);
            }

            PendingCount = pending.Count;
            if (pending.Count > 0)
            {
                VisualElement box = Box(null, "Choose a control", pending.Count == 1 ? "1 action" : $"{pending.Count} actions", out VisualElement rows);
                box.AddToClassList("blocks-beat--pending");
                foreach (TouchInputSource source in pending)
                    rows.Add(PendingRow(source));
                m_Host.Add(box);
            }

            // One box per control, its actions as rows.
            MappingCount = 0;
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TouchControl control in m_Controls)
            {
                string name = TouchBindingAuthoring.ControlName(control);
                if (name.Length > 0)
                    known.Add(name);

                VisualElement box = ControlBox(control, out VisualElement rows);
                int count = 0;
                foreach (TouchInputSource source in m_Sources)
                {
                    if (!source.IsMappedTo(control))
                        continue;
                    rows.Add(MappingRow(source, control, name));
                    count++;
                }

                MappingCount += count;
                if (count == 0)
                {
                    var empty = new Label("Drives nothing yet. ⋮ › Map an action, or + Add mapping.");
                    empty.AddToClassList("blocks-group__empty");
                    rows.Add(empty);
                }

                m_Host.Add(box);
            }

            // Bindings to a control this rig does not have: another rig's, or a deleted one.
            var orphans = new List<(TouchInputSource source, string name)>();
            foreach (TouchInputSource source in m_Sources)
            {
                foreach (string name in source.ControlNames)
                {
                    if (!known.Contains(name))
                        orphans.Add((source, name));
                }
            }

            if (orphans.Count > 0)
            {
                VisualElement box = Box(null, "Not in this rig", "another rig or a deleted control", out VisualElement rows);
                box.AddToClassList("blocks-beat--orphan");
                foreach ((TouchInputSource source, string name) in orphans)
                    rows.Add(MappingRow(source, null, name));
                m_Host.Add(box);
            }

            if (m_Controls.Count == 0 && pending.Count == 0)
            {
                var empty = new Label("No controls yet. + Add mapping picks an action and makes a control for it.");
                empty.AddToClassList("blocks-steps__empty");
                m_Host.Add(empty);
            }

            if (m_Sources.Count == 0)
            {
                var none = new Label("Nothing in the open scenes reads an input action yet.");
                none.AddToClassList("blocks-panel__hint");
                m_Host.Add(none);
            }

            UpdateSummary();
        }

        bool MappedInRig(TouchInputSource source)
        {
            foreach (TouchControl control in m_Controls)
            {
                if (source.IsMappedTo(control))
                    return true;
            }

            return false;
        }

        void UpdateSummary()
        {
            if (Application.isPlaying)
            {
                m_Card.SetSummary($"{MappingCount} mapped");
                return;
            }

            string text = MappingCount == 1 ? "1 mapping" : $"{MappingCount} mappings";
            if (PendingCount > 0)
                text += $" · {PendingCount} to choose";
            m_Card.SetSummary(text, warn: PendingCount > 0);
        }

        /// <summary>A step-group box with a fixed title. <paramref name="rows"/> receives the rows.</summary>
        static VisualElement Box(VisualElement glyph, string title, string meta, out VisualElement rows)
        {
            var root = new VisualElement();
            root.AddToClassList("blocks-group");
            root.AddToClassList("blocks-beat");

            var header = new VisualElement();
            header.AddToClassList("blocks-beat__header");
            if (glyph != null)
                header.Add(glyph);

            var name = new Label(title);
            name.AddToClassList("blocks-mapping__title");
            header.Add(name);

            if (!string.IsNullOrEmpty(meta))
            {
                var dim = new Label(meta);
                dim.AddToClassList("blocks-beat__waits");
                dim.AddToClassList("blocks-mapping__meta");
                header.Add(dim);
            }

            root.Add(header);
            rows = new VisualElement();
            rows.AddToClassList("blocks-group__rows");
            root.Add(rows);
            return root;
        }

        VisualElement ControlBox(TouchControl control, out VisualElement rows)
        {
            var root = new VisualElement();
            root.AddToClassList("blocks-group");
            root.AddToClassList("blocks-beat");
            root.AddToClassList("blocks-mapping-control");

            var header = new VisualElement();
            header.AddToClassList("blocks-beat__header");

            VisualElement icon = TouchInspectorKit.Icon(control);
            icon.AddToClassList("blocks-mapping__control-icon");
            header.Add(icon);

            // The id is edited in place. Renaming it moves every mapping with it.
            var name = new TextField { isDelayed = true, value = control.ActionId, tooltip = "The control's id: what rig.Get<T>(\"id\") finds and the last part of its <TouchRig>/ binding path. Renaming moves every mapping with it." };
            name.AddToClassList("blocks-beat__name");
            name.SetEnabled(!Application.isPlaying);
            name.RegisterValueChangedCallback(evt =>
            {
                AuthoringResult result = TouchMappings.Rename(control, evt.newValue);
                if (!result.Ok)
                    name.SetValueWithoutNotify(control.ActionId);
                Changed(result);
            });
            header.Add(name);

            var kind = new Label(TouchInspectorKit.KindName(control));
            kind.AddToClassList("blocks-beat__waits");
            kind.AddToClassList("blocks-mapping__meta");
            header.Add(kind);

            if (Application.isPlaying)
            {
                var live = new Label(TouchInspectorKit.LiveValue(control));
                live.AddToClassList("blocks-mapping__live");
                live.schedule.Execute(() =>
                {
                    if (control == null) return;
                    string text = TouchInspectorKit.LiveValue(control);
                    if (live.text != text) live.text = text;
                    live.EnableInClassList("blocks-mapping__live--on", control.IsPressed);
                }).Every(100);
                header.Add(live);
            }

            var select = new Button(() => Select(control)) { text = "→", tooltip = $"Select {control.name}" };
            select.AddToClassList("blocks-beat__more");
            header.Add(select);

            var more = new Button { text = "⋮", tooltip = "Map an action to this control, select it" };
            more.AddToClassList("blocks-beat__more");
            more.clicked += () => ShowControlMenu(more, control);
            header.Add(more);
            header.AddManipulator(new ContextualMenuManipulator(evt => AddControlItems(Adder(evt.menu), control)));

            root.Add(header);
            rows = new VisualElement();
            rows.AddToClassList("blocks-group__rows");
            root.Add(rows);
            return root;
        }

        /// <summary>A row for <paramref name="source"/> in <paramref name="control"/>'s box (<c>null</c> for an orphan binding).</summary>
        VisualElement MappingRow(TouchInputSource source, TouchControl control, string controlName)
        {
            string rowKey = source.Key + "→" + controlName;
            VisualElement frame = RowFrame(source, out VisualElement line, out VisualElement text);

            if (control == null)
            {
                var noun = new Label("<TouchRig>/" + controlName);
                noun.AddToClassList("blocks-step__noun");
                text.Add(noun);
                line.AddToClassList("blocks-step--warn");
            }

            if (source.ReadOnlyReason != null)
                AddWarn(line, source.ReadOnlyReason);

            var more = new Button { text = "⋮", tooltip = "Move to another control, remove" };
            more.AddToClassList("blocks-step__more");
            more.clicked += () =>
            {
                var menu = new GenericMenu();
                AddRowItems(Adder(menu), source, control, controlName);
                menu.DropDown(more.worldBound);
            };
            line.Add(more);
            line.AddManipulator(new ContextualMenuManipulator(evt => AddRowItems(Adder(evt.menu), source, control, controlName)));

            VisualElement body = new VisualElement();
            body.AddToClassList("blocks-step__body");
            frame.Add(body);

            bool open = m_Open.Contains(rowKey);
            line.EnableInClassList("blocks-step--open", open);
            body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
                FillBody(body, source, controlName);

            line.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button)
                    return;
                bool nowOpen = !m_Open.Contains(rowKey);
                if (nowOpen) m_Open.Add(rowKey); else m_Open.Remove(rowKey);
                line.EnableInClassList("blocks-step--open", nowOpen);
                body.style.display = nowOpen ? DisplayStyle.Flex : DisplayStyle.None;
                if (nowOpen)
                    FillBody(body, source, controlName);
            });

            return frame;
        }

        VisualElement PendingRow(TouchInputSource source)
        {
            VisualElement frame = RowFrame(source, out VisualElement line, out _);
            line.AddToClassList("blocks-step--pending-choice");

            var choose = new Button { text = "Choose control ▾", tooltip = source.TakesStick ? "Pick a joystick for this action, or make a new one." : "Pick a button for this action, or make a new one." };
            choose.AddToClassList("blocks-mapping__choose");
            choose.clicked += () => ShowChooseMenu(choose, source);
            choose.SetEnabled(!Application.isPlaying && source.ReadOnlyReason == null);
            line.Add(choose);

            if (source.ReadOnlyReason != null)
                AddWarn(line, source.ReadOnlyReason);

            var remove = new Button(() =>
            {
                TouchMappings.SetPending(m_Rig, source.Key, false);
                Refresh(force: true);
            }) { text = "✕", tooltip = "Take it off the list. Nothing was written yet." };
            remove.AddToClassList("blocks-step__more");
            line.Add(remove);
            return frame;
        }

        /// <summary>The shell every mapping row shares: frame, line, glyph, name and location.</summary>
        static VisualElement RowFrame(TouchInputSource source, out VisualElement line, out VisualElement text)
        {
            var frame = new VisualElement();
            frame.AddToClassList("blocks-step-frame");

            line = new VisualElement();
            line.AddToClassList("blocks-step");
            line.AddToClassList("blocks-step--compact");
            line.AddToClassList("blocks-mapping-row");
            frame.Add(line);

            Texture glyph = Glyph(source);
            if (glyph != null)
            {
                var image = new Image { image = glyph, scaleMode = ScaleMode.ScaleToFit };
                image.AddToClassList("blocks-step__glyph");
                line.Add(image);
            }

            text = new VisualElement();
            text.AddToClassList("blocks-step__text");
            var name = new Label(source.Name);
            name.AddToClassList("blocks-step__name");
            text.Add(name);

            var where = new Label(Where(source));
            where.AddToClassList("blocks-step__noun");
            text.Add(where);
            line.Add(text);

            line.tooltip = $"{source.Name} · {(source.IsInline ? "inline on " : "in ")}{source.Where}";
            return frame;
        }

        static string Where(TouchInputSource source)
        {
            if (source.IsInline)
                return source.Where;

            string readers = source.Readers.Count == 0 ? string.Empty
                : source.Readers.Count == 1 ? " · " + source.Readers[0].gameObject.name
                : $" · {source.Readers[0].gameObject.name} +{source.Readers.Count - 1}";
            return source.Where + readers;
        }

        static void AddWarn(VisualElement line, string reason)
        {
            line.AddToClassList("blocks-step--warn");
            var mark = new Label("!") { tooltip = reason };
            mark.AddToClassList("blocks-step__warn-mark");
            line.Add(mark);
        }

        static Texture Glyph(TouchInputSource source)
        {
            if (source.IsInline)
                return source.Owner != null ? EditorGUIUtility.ObjectContent(source.Owner, source.Owner.GetType()).image : null;
            return source.Asset != null ? AssetPreview.GetMiniThumbnail(source.Asset) : null;
        }

        /// <summary>The details under an open row: where the action is, who reads it, and its binding.</summary>
        void FillBody(VisualElement body, TouchInputSource source, string controlName)
        {
            body.Clear();
            if (source.IsInline)
            {
                body.Add(TouchInspectorKit.Row("Inline on", TouchInspectorKit.Value(source.Where, () => Ping(source.Owner), "Ping the component whose field holds this action")));
            }
            else
            {
                body.Add(TouchInspectorKit.Row("Asset", TouchInspectorKit.Value(source.Where, () => Ping(source.Asset), AssetDatabase.GetAssetPath(source.Asset))));
                foreach (Component reader in source.Readers)
                {
                    if (reader == null) continue;
                    Component r = reader;
                    body.Add(TouchInspectorKit.Row("Read by", TouchInspectorKit.Value(r.gameObject.name + " · " + ObjectNames.NicifyVariableName(r.GetType().Name), () => Ping(r), "Ping it")));
                }
            }

            body.Add(TouchInspectorKit.Row("Binding", TouchInspectorKit.Value(TouchBindingAuthoring.PathFor(controlName))));
        }

        /// <summary>Adds one menu item. A path of <c>"-"</c> adds a separator.</summary>
        delegate void MenuAdd(string path, bool enabled, Action action);

        static MenuAdd Adder(GenericMenu menu) => (path, enabled, action) =>
        {
            if (path == "-") menu.AddSeparator(string.Empty);
            else if (enabled) menu.AddItem(new GUIContent(path), false, () => action());
            else menu.AddDisabledItem(new GUIContent(path));
        };

        static MenuAdd Adder(DropdownMenu menu) => (path, enabled, action) =>
        {
            if (path == "-") menu.AppendSeparator();
            else menu.AppendAction(path, _ => action(), enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        };

        void ShowSourceMenu(VisualElement anchor, TouchControl forControl)
        {
            Refresh(force: false);
            var menu = new GenericMenu();
            AddSourceItems(Adder(menu), string.Empty, forControl);
            menu.DropDown(anchor.worldBound);
        }

        /// <summary>
        /// Adds every action the scene reads under <paramref name="prefix"/>, grouped by asset and map.
        /// With <paramref name="forControl"/>, lists only actions it can drive and maps the pick straight to it.
        /// </summary>
        void AddSourceItems(MenuAdd add, string prefix, TouchControl forControl)
        {
            if (m_Sources.Count == 0)
            {
                add(prefix + "Nothing in the open scenes reads an input action", false, null);
                return;
            }

            List<string> pending = TouchMappings.Pending(m_Rig);
            foreach (TouchInputSource source in m_Sources)
            {
                string path = prefix + (source.IsInline
                    ? "Scene objects/" + Escape(source.Owner != null ? source.Owner.gameObject.name : "?") + "/" + Escape(source.Name)
                    : Escape(source.Asset.name) + "/" + Escape(source.Where.Substring(source.Asset.name.Length + 3)) + "/" + Escape(source.Name));

                string mappedTo = MappedNames(source);
                if (mappedTo.Length > 0)
                    path += $"   ({mappedTo})";

                bool usable = forControl != null ? source.Takes(forControl) : source.TakesStick || source.TakesButton;
                bool already = forControl != null ? source.IsMappedTo(forControl) : pending.Contains(source.Key);
                bool enabled = usable && !already && source.ReadOnlyReason == null && !Application.isPlaying;

                TouchInputSource s = source;
                add(path, enabled, () =>
                {
                    if (forControl != null)
                    {
                        Changed(TouchMappings.Map(s, forControl));
                        return;
                    }

                    TouchMappings.SetPending(m_Rig, s.Key, true);
                    Refresh(force: true);
                });
            }
        }

        string MappedNames(TouchInputSource source)
        {
            var names = new List<string>();
            foreach (TouchControl control in m_Controls)
            {
                if (source.IsMappedTo(control))
                    names.Add(control.ActionId);
            }

            return string.Join(", ", names);
        }

        /// <summary>A pending action's control picker: every control that can drive it, then a new one.</summary>
        void ShowChooseMenu(VisualElement anchor, TouchInputSource source)
        {
            var menu = new GenericMenu();
            foreach (TouchControl control in m_Controls)
            {
                TouchControl c = control;
                var label = new GUIContent($"{control.ActionId}   ({TouchInspectorKit.KindName(control)})");
                if (source.Takes(control))
                    menu.AddItem(label, false, () => Changed(TouchMappings.Map(source, c)));
                else
                    menu.AddDisabledItem(label);
            }

            if (m_Controls.Count > 0)
                menu.AddSeparator(string.Empty);

            if (source.TakesStick)
                menu.AddItem(new GUIContent("New Joystick"), false, () => MapToNew(source, TouchRigSpawner.CreateJoystick));
            if (source.TakesButton)
                menu.AddItem(new GUIContent("New Button"), false, () => MapToNew(source, TouchRigSpawner.CreateButton));

            menu.DropDown(anchor.worldBound);
        }

        void MapToNew(TouchInputSource source, Func<GameObject, GameObject> create)
        {
            GameObject created = create(m_Rig.gameObject);
            TouchControl control = created != null ? created.GetComponent<TouchControl>() : null;
            if (control == null)
                return;

            // A fresh auto id takes the action's name ("Joystick1" becomes "Move"), kept unique in the rig.
            string id = TouchControlIds.Unique(m_Rig, TouchControlIds.FromActionName(source.Name), control);
            if (TouchControlIds.Key(id).Length > 0)
                TouchControlIds.Apply(control, id, "Create Touch Control");

            Changed(TouchMappings.Map(source, control));
            EditorGUIUtility.PingObject(created);
        }

        void ShowControlMenu(VisualElement anchor, TouchControl control)
        {
            Refresh(force: false);
            var menu = new GenericMenu();
            AddControlItems(Adder(menu), control);
            menu.DropDown(anchor.worldBound);
        }

        void AddControlItems(MenuAdd add, TouchControl control)
        {
            AddSourceItems(add, "Map an action/", control);
            add("-", true, null);
            add("Select control", true, () => Select(control));
        }

        void AddRowItems(MenuAdd add, TouchInputSource source, TouchControl control, string controlName)
        {
            bool editable = !Application.isPlaying && source.ReadOnlyReason == null;
            foreach (TouchControl other in m_Controls)
            {
                TouchControl target = other;
                add("Move to/" + other.ActionId, editable && other != control && source.Takes(other) && !source.IsMappedTo(other),
                    () => Changed(TouchMappings.Move(source, controlName, target)));
            }

            foreach (TouchControl other in m_Controls)
            {
                TouchControl target = other;
                add("Also map to/" + other.ActionId, editable && other != control && source.Takes(other) && !source.IsMappedTo(other),
                    () => Changed(TouchMappings.Map(source, target)));
            }

            add("-", true, null);
            Object ping = TouchMappings.PingTarget(source);
            if (ping != null)
                add(source.IsInline ? "Ping the object" : "Ping the asset", true, () => Ping(ping));
            if (control != null)
                add("Select control", true, () => Select(control));

            add("-", true, null);
            add("Remove mapping", editable, () => Changed(TouchMappings.Unmap(source, controlName)));
        }

        static string Escape(string text) => (text ?? string.Empty).Replace("/", "∕");

        void Changed(AuthoringResult result)
        {
            m_Note.text = result.Message ?? string.Empty;
            m_Note.style.display = string.IsNullOrEmpty(result.Message) ? DisplayStyle.None : DisplayStyle.Flex;
            Refresh(force: true);
            m_OnChanged?.Invoke();
        }

        static void Ping(Object target)
        {
            if (target != null)
                EditorGUIUtility.PingObject(target);
        }

        static void Select(TouchControl control)
        {
            if (control == null)
                return;
            Selection.activeObject = control.gameObject;
            EditorGUIUtility.PingObject(control.gameObject);
        }
    }
}
#endif
