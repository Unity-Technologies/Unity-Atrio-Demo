# Building Blocks Touch Input

On-screen touch controls for mobile games: a `TouchRig` holding `TouchJoystick` and `TouchButton`
controls, drawn by uGUI views, read by gameplay through a C# API.

## Status

Early preview, 0.2.1. The controls work in the Game view with a mouse and in the Device
Simulator with one finger, and they ran on an iPhone by hand. Android is untried.

## How it fits together

A control is a component that holds state and math. It never reads input itself: a view (uGUI
today) converts pointer events into `PointerSample`s and calls `Feed`. The control owns one pointer
at a time; a second finger on the same control is ignored until the first lifts. Gameplay reads
`rig.Get<TouchJoystick>("Move").Value` in `Update`, or subscribes to the control's events.

Sizes are pixels on the way in and normalized on the way out. Physical sizing (millimetres,
`Screen.dpi`) and the safe area are handled by the uGUI layer.

## Map an action to a control

1. Select the **Touch Rig** (or make one with **GameObject ▸ Building Blocks ▸ Touch Input ▸ Joystick**; a
   Canvas, a Safe Area, an EventSystem and, with the Input System, a Touch Input Bridge are created if the scene
   has none). Controls already work from C#: `rig.Get<TouchJoystick>("Joystick1").Value`.
2. In the rig's **Mappings** card, **+ Add mapping** lists every action the open scenes read, grouped by asset
   and map (inline actions under their object), for example **Player / Move**.
3. The action waits in a **Choose a control** box. **Choose control ▾** offers every control of the right kind
   (a stick for a Vector2 action, a button for a press) and **New Joystick** / **New Button**. A new control
   takes the action's name as its id (`Move`).
4. **Play** and drag: the action performs. With the gameplay package's first-person player, a Joystick on
   Player / Move walks, with no code.

What Mappings reads: `InputActionReference`, `InputActionProperty` (by reference or inline) and `InputAction`
fields on the open scenes' components, and every action of an `InputActionAsset` field (`PlayerInput`). Input
modules and the bridge are skipped.

**The binding is the mapping.** A mapping is one `<TouchRig>/<id>` binding on the action, written into the
action's own asset (in its Touch scheme; one is added if the asset has schemes and none is called Touch;
re-imported, not undoable) or into the component's inline action (undoable). Nothing on the control remembers
the action. The action's other bindings (keyboard, gamepad) are never changed. An asset inside a registry or
git package is read-only: the Rig check offers to duplicate it into `Assets/` (point your player at the copy
too).

Mappings shows one box per control; its rows are the actions it drives. A control can drive several actions
and an action can be driven by several controls. A row's **⋮** moves it to another control, maps it to one
more, or removes it. Rename a control on the rig (the box's name): every mapping moves with it. A control's
own inspector only says what it drives, with a link back to the rig.

**Rig check** says in one line whether the rig works and, open, lists what to fix: duplicate or empty ids, a
missing EventSystem or the wrong input module, a rig without a bridge, a control with no view or outside the
safe area, a read-only asset, and two traps in Unity 6's default actions: Look reads `<Pointer>/delta` in the
Touch scheme (dragging a stick would also turn the camera) and Attack reads `<Touchscreen>/primaryTouch/tap`
(any tap fires it). Each finding has a button that says exactly what it changes; **Fix all** runs the ones that
need no choice. The tool never edits anything but `<TouchRig>/…` bindings on its own. These binding checks
still read one resolved asset (the bridge's **Actions** field, else the one asset the scene references, else
project-wide actions).

Binding paths are the id with anything but letters, digits and underscores dropped and the first letter
lower-cased: `Move` → `<TouchRig>/move`. A joystick is a `Stick` (Vector2, magnitude ≤ 1, its dead zone
applied); a button a `Button`. Each mapping row's details show its path, for binding by hand.

Two things to know. Values reach actions **one frame after** `rig.Get<T>(id)` reads them, because uGUI feeds
the controls after the Input System has updated; read the C# API where that frame matters. And there is **one
bridge at a time**: a second enabled bridge warns and disables itself.

## Change controls while the game runs

The `TouchRig` device follows the rig. Add, destroy or rename a control in Play and the device is rebuilt at the next
update, once per frame however many changes there were. A rebuild resets every control of the rig: a finger that was
down must lift and touch again. Hidden controls (a disabled component or GameObject) stay on the device and read at
rest, so showing and hiding a button never rebuilds anything. To show a **Pet** button near the cat: put it under the
rig hidden, map Interact to it on the Mappings card, and enable its GameObject when the player is close. The action
works on the first press. From C#, `rig.Find("Pet")` returns `null` while it is hidden.

To change which control drives an action, use the Input System's binding overrides through a small helper (no UI;
build your own):

```csharp
TouchRebindResult r = TouchRebinding.Rebind(actions.FindAction("Player/Interact"), "Pet");
TouchRebinding.Save(actions);   // <persistentDataPath>/TouchRebinding/<asset>.json
TouchRebinding.Load(actions);   // next session, before the player reads input
```

Only `<TouchRig>/…` bindings change; the asset is not edited. The file holds every override on that asset, in the
Input System's own format. Bad text or a missing file is reported in the result, never thrown.

### Map an action while the game runs

A rebind moves a touch binding the action already has. To make a control drive an action that has none (or a second
control on one that has), map it:

```csharp
InputAction interact = actions.FindAction("Player/Interact");
TouchRebindResult r = TouchRebinding.Map(interact, "Jump");   // adds <TouchRig>/jump, in memory
TouchRebinding.Unmap(interact, "Jump");                       // removes what Map added, nothing else
string[] added = TouchRebinding.RuntimeMappings(interact);    // { "<TouchRig>/jump" }
```

- The action may be enabled; the new control drives it at once. The binding takes the group of the asset's Touch
  scheme, so a `PlayerInput` on that scheme reads it.
- The control's value must fit: a joystick needs a Vector2 action, a button a Button action (or a Value action
  expecting Button, Axis or Analog). A mismatch fails, changes nothing, and says so in the result.
- `Unmap` never removes a binding the asset has; it fails and tells you to rebind it or use the Mappings card.
- **Saving keeps mappings.** `Save` writes them in a `touchMappings` list next to the overrides, in the same file;
  `Load` adds them again with the same binding ids (overrides on them come back too) and never adds one twice. A file
  saved before mappings existed still loads. Loading a file replaces both the overrides and the added mappings.
- The asset file is never written. In the Editor, a mapping made in Play is gone when Play stops. To make it part of
  the asset, map it on the Touch Rig's Mappings card outside Play; the card stays read-only in Play.

## Feel

**Feel** (the control's own fields: origin, dead zone, full-at, recenter) decides how the finger's movement
becomes the value. It is the same for the C# API and the Input System. Per-mapping response (sensitivity,
invert, a button's Press / Hold / Tap) has no UI in 0.1.0; add processors or interactions on the binding by
hand in the action asset. Several touch controls on one action: while both are touched the Input System keeps
the stronger input; it does not add them.

## Save and load tuning

Tune a joystick while the game runs, then keep the values. `TouchTuning.Export(rig)` gives the rig's Feel settings
as JSON keyed by control id; `TouchTuning.Apply(rig, json)` puts them back and returns what it could not use (an id
the rig does not have, a value of the wrong type) instead of throwing. Settings the JSON leaves out are not changed.

```csharp
TouchTuning.Save(rig);                       // <persistentDataPath>/TouchTuning/<scene>.<rig>.json
TouchTuningResult r = TouchTuning.Load(rig); // r.Problems lists anything skipped
```

In the Editor, saving during Play also writes the saved values into the scene when Play stops (undoable; save the
scene). Values you change in Play but do not save are discarded, as usual. On a device nothing loads by itself: call
`Load` where your game starts. Placement and looks are not part of tuning.

From hot-reloaded code, which cannot call generic methods, look a control up with `rig.Find("Move") as TouchJoystick`.

## Drive the rig from the command line

With Unity's Pipeline package (`com.unity.pipeline`, experimental) in the project, six commands drive a rig from a
terminal or an agent through the `unity` CLI. They run in the Editor (edit mode or Play) and in a development build.
Without Pipeline, or in a release build (unless it sets `ENABLE_RUNTIME_PIPELINE`), they do not exist and nothing else
changes.

| Command | Arguments, in order | Does |
|---|---|---|
| `touch_rig_get` | `rig` | Lists rigs and their controls: id, type, enabled, value, tuning, binding path |
| `touch_tune` | `control`, `setting`, `value`, `tuning`, `rig` | Sets one Feel setting (`originMode`, `deadZone`, `saturation`, `recenterOnRelease`) and/or applies tuning JSON |
| `touch_rebind` | `action`, `control`, `from`, `reset` | Points an action's `<TouchRig>/…` binding at another control (an override), or removes it |
| `touch_map` | `action`, `control` | Makes a control drive an action at runtime (adds a `<TouchRig>/…` binding; `TouchRebinding.Map`) |
| `touch_unmap` | `action`, `control` | Removes a mapping `touch_map` added (never one the asset has) |
| `touch_save` | `rig`, `rebinds` | Saves tuning (and overrides and runtime mappings) to persistent data and returns the text written |

```bash
unity command touch_rig_get
unity command touch_tune Move deadZone 0.4          # or --control Move --setting deadZone --value 0.4
unity command touch_tune --tuning "$(cat tuning.json)"
unity command touch_rebind --action Player/Interact --control Pet
unity command touch_map Interact Jump               # the Jump button now also fires Interact
unity command touch_unmap Interact Jump
unity command touch_save
```

An action is `Map/Action`, or just `Action` when one map has it. When two assets both have it (project-wide actions
and a `PlayerInput`'s own asset, say), name the asset too: `TouchTest:Player/Interact`. `touch_rig_get` lists, per
control, the actions mapped to it at runtime (`mappedAtRuntime`). In edit mode `touch_map` works on the asset's
in-memory copy only, and says so; use the Mappings card to keep a mapping.

`rig` can be left out when only one rig has the control. A request that cannot be done (no such control, a setting
the control lacks, `deadZone lots`) fails with one sentence saying what to change; the rig is not touched. The JSON
argument is `--tuning`, because the CLI keeps `--json` for its own output.

To bring values tuned on a phone into the project: run `touch_save` on the phone, copy the `tuning[].json` text it
returns, and apply it in the Editor with `touch_tune --tuning`. In edit mode that marks the scene dirty; save it.

**On a phone (untried: on iOS the server starts but cannot write its port file yet).** The Pipeline server runs in a
Player only when:

1. *Project Settings ▸ Pipeline ▸ Runtime ▸ Enable In Builds* is on, and the build is a **Development Build**.
2. The phone's port reaches the laptop. The server listens on the phone's loopback only (ports 7900-7949, with a
   token). Android: `adb forward tcp:<port> tcp:<port>`. iOS: `iproxy <port> <port>` (libimobiledevice).
3. The CLI knows the port and token: pull the `.unity-pipeline-runtime-port` file from the app's persistent data
   (Android: `adb pull /sdcard/Android/data/<bundle id>/files/.unity-pipeline-runtime-port <dir>/`), then
   `unity command --runtime-path <dir> touch_rig_get`.

Commands run on the main thread, one at a time. The command assembly is kept by the linker
(`[Preserve]`, `AlwaysLinkAssembly`); stripping on IL2CPP is not verified yet.

## Build a layout from the command line (or an agent)

With Pipeline installed, eight more commands build controls in the Editor, outside Play. They call what the menu and
the Touch Rig inspector call, so the result is the same objects and bindings you would have made by hand, and you keep
editing it in the inspector.

| Command | Arguments, in order | Does |
|---|---|---|
| `touch_scene_get` | `rig` | The actions the scene reads (`stick` or `button`, who reads them, which controls drive them), each rig's controls with placement, the canvas size, the Rig check |
| `touch_add` | `kind`, `id`, `action`, `anchor`, `x`, `y`, `size`, `rig` | Adds a joystick or button (Canvas, EventSystem and rig too when missing); with `action`, maps it and names it after the action |
| `touch_place` | `control`, `anchor`, `x`, `y`, `size`, `rig` | Moves or resizes a control; what you leave out is kept |
| `touch_rename` | `control`, `id`, `rig` | Renames a control; its mappings move with it |
| `touch_remove` | `control`, `rig`, `keep_mappings` | Deletes a control and its mappings |
| `touch_assign` | `action`, `control`, `from`, `rig` | Maps a control to an action, kept in the asset (or inline action); `from` moves the mapping from another control |
| `touch_unassign` | `action`, `control`, `rig` | Removes that mapping |
| `touch_check` | `rig`, `fix` | The Rig check; `--fix true` runs the fixes that need no choice |

```bash
unity command touch_scene_get
unity command touch_add --kind joystick --action TouchTest:Player/Move --anchor bottom_left --x 200 --y 220
unity command touch_add --kind button --action TouchTest:Player/Jump --anchor bottom_right --x 150 --y 260
unity command touch_place Jump --y 420
unity command touch_assign --action TouchTest:Player/Interact --control Jump
unity command touch_check --fix true
```

Placement is in canvas units (the Canvas Scaler's reference is 1080×1920): an anchor (`bottom_left`, `bottom`,
`bottom_right`, `left`, `center`, `right`, `top_left`, `top`, `top_right`) and the distance from it to the control's
centre, measured into the screen. On a centred axis, + is right or up. A joystick is 220 by default, a button 150.
Overlaps and controls outside the safe area come back as warnings.

Each command is one Undo step. A mapping written into an action asset is saved to the asset and is not undone by
Ctrl+Z (the Mappings card behaves the same); a mapping on a component's inline action is. In Play they refuse and name
the runtime command to use instead (`touch_map`, `touch_tune`).

## Show on

The Touch Rig's **Show on** card picks where its controls show.

- **Platforms**: a dropdown like a layer mask. Tick iOS, Android, Web (mobile or desktop browser), Desktop, Console,
  Editor. Everything by default. The Device Simulator's phone counts as that phone, not as Editor.
- **When**: Always; Touchscreen present (a laptop without one shows nothing); Last input was touch (a gamepad or
  keyboard press hides the controls, the next touch brings them back; in the Editor a mouse click counts as a touch).
- The line under the fields says whether the controls show here, and why not.

Hidden controls fade out and take no touches; a finger that was down is let go. They still work from C# and stay on
the Input System device. A view's own **Show on** (Look card) still applies too.

## Look

The Look card edits the view: the default skin's sprites (white, so the tint recolours them), knob travel,
idle and active opacity with a short fade (a joystick rests at 40%), a button's pressed tint and scale, and
**Show on**: Always (the default, so the Game view shows controls while you build) or mobile platforms only.
