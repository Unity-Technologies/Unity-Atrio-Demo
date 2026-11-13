# Changelog

Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). This is the release note the
consuming teams read, so every tag gets an entry.

## [Unreleased]

## [0.2.1] - 2026-10-07

Workflow fixes and a first run on a phone. No runtime behaviour changes: the code docs and tooltips are rewritten, and
the repo's check and release scripts no longer report success when a step fails.

### Behaviour to know

- First device run: the dev scene on an iPhone 14 Pro Max (IL2CPP, development build). The joystick and button work.
  Hot reload works: a pushed edit hid the joystick, spawned arrow buttons, and spawned and mapped a button at runtime.
  A rig set to Last input was touch hides when a paired gamepad is pressed and shows again on a touch.
- On iOS the Pipeline runtime server starts but cannot write its port file, so the `touch_*` runtime commands were not
  reached on the phone. Android is untried.

### Changed

- XML docs and inspector tooltips are shorter and plainer, and no longer point at internal design records.

## [0.2.0] - 2026-10-06

Tune on the device: change how the controls feel, which actions they drive and which controls exist while the game
runs, and keep the result. With Unity's Pipeline package the same is one `unity command` away, for a person at a
terminal or an agent, in the Editor or a development build. Agents can also build a layout in the Editor with typed
commands. A rig now picks the platforms it shows on.

### Behaviour to know

- Nothing here has run on a phone yet; persistent-data writes, the runtime command server and hot reload on a device
  are unverified. Compiled and tested on 6000.5 with Input System 1.20 and Pipeline 0.7.0-exp.1, not on the 6000.3
  floor.
- Pipeline is experimental; its commands can change under this package. Without it nothing here changes.
- A device rebuild (a control added, destroyed or renamed in Play) lets go of held buttons; a held joystick stays held.

### Added

- **Show on, per rig**: `TouchRig.ShowOn` picks the platforms (`TouchPlatforms` flags: iOS, Android, Web
  mobile, Web desktop, Desktop, Console, Editor; Device Simulator aware) and `TouchRig.ShowWhen` the moment (Always,
  Touchscreen present, Last input was touch). The Touch Rig inspector has a Show on card with a "Shown here" line.
  While `IsShown` is false the views fade out, take no touches and let go of a held control; the controls stay
  registered. `TouchInputHints` (Core) holds whether a touchscreen is present and whether the last input was touch;
  `TouchInputHintsReporter` in the Input System assembly fills it in. `TouchPlatform.Current` / `From` and
  `TouchRig.ShouldShow` expose the rule to code. Defaults keep
  every rig showing as before.

- **`TouchTuning`** (Core): `Export(rig)` writes the rig's Feel settings as JSON keyed by control id
  (joystick origin mode, dead zone, saturation, recenter; buttons by id and type); `Apply(rig, json)` writes them
  back. A setting the JSON leaves out keeps its value; an unknown id, a wrong type or text that is not JSON is
  reported in the returned `TouchTuningResult` and logged once, never thrown. `Save` / `Load` use
  `<persistentDataPath>/TouchTuning/<scene>.<rig>.json` (`DefaultPath`) or a path you give; `Saved` fires after a
  save. The JSON carries a `version`.
- **Saving in Play keeps the values** (Editor, `TouchTuningKeeper`): tuning saved with `TouchTuning.Save` during Play
  is applied onto the same rig in the scene when Play stops (undoable; save the scene to keep it). Unsaved changes
  are discarded as usual.
- **`TouchRig.Find(actionId)`**: the non-generic twin of `Get<T>`, for hot-reloaded code that cannot call a generic
  method. `rig.Find("Move") as TouchJoystick`.
- **The bridge's device follows the rig while the game runs**: add, destroy or rename a control and the
  `TouchRig` device is rebuilt at the next Input System update, once however many changes there were. No manual
  `TouchInputBridge.Rebuild()`. A rebuild cancels held buttons first, so that finger must touch again and no button
  action stays pressed or fires twice; a held joystick stays held and keeps driving Move. New `TouchRig.ControlsChanged` event (Core).
- **`TouchRebinding`** (InputSystem assembly): `Rebind(action, "Pet")` points an action's
  `<TouchRig>/…` binding at another control as an Input System override; `Reset(action)` removes it. `Save` / `Load`
  keep the asset's overrides (`SaveBindingOverridesAsJson` / `LoadBindingOverridesFromJson`) in
  `<persistentDataPath>/TouchRebinding/<asset>.json` or a path you give. Problems come back in a `TouchRebindResult`,
  never thrown. `Export` / `Apply` do the same with text instead of a file. No UI.

- **CLI commands** (optional `Pipeline` assembly): with `com.unity.pipeline` installed, `unity command
  touch_rig_get` lists rigs and controls (id, type, enabled, value, tuning, binding path); `touch_tune Move deadZone 0.4`
  sets one Feel setting, `touch_tune --tuning <json>` applies tuning JSON; `touch_rebind --action Player/Interact
  --control Pet` rebinds (or `--reset true`); `touch_save` saves tuning and overrides and returns the text, so values
  tuned on a phone can be applied in the Editor. They run in the Editor and in development builds with the Pipeline
  runtime server on. A request that cannot be done fails with one sentence and changes nothing. Without Pipeline, or in
  a release build that does not set `ENABLE_RUNTIME_PIPELINE`, the assembly is not compiled. Not yet run on a device.

- **Mapping at runtime** (`TouchRebinding.Map` / `Unmap`): `Map(interact, "Jump")` makes the Jump
  button drive Interact while the game runs, by adding a `<TouchRig>/jump` binding in memory (enabled actions too).
  A joystick on a Button action, or a button on a Vector2 action, is refused and reported. `Unmap` removes only what
  `Map` added, never a binding of the asset. `Save` / `Load` keep the added mappings next to the overrides in the same
  file (a `touchMappings` list) and add them back with the same binding ids, never twice; files saved earlier still
  load. `RuntimeMappings(action)` lists them. In the Editor they are removed when Play stops. The asset file is never written: the Mappings card, outside Play, is how a mapping becomes part of the asset.
- **CLI `touch_map` / `touch_unmap`** (`unity command touch_map Interact Jump`); `touch_save` saves runtime mappings
  and `touch_rig_get` lists them per control (`mappedAtRuntime`). An action two assets both have is named
  `Asset:Map/Action`.

- **Authoring commands for agents** (optional `Editor.Pipeline` assembly): with `com.unity.pipeline`
  installed, `touch_scene_get` lists what a layout needs (the actions the scene reads with their kind and readers, the
  rigs' controls with placement, the canvas, the Rig check); `touch_add --kind joystick --action Player/Move --anchor
  bottom_left --x 200 --y 220` builds the menu's control and maps it; `touch_place`, `touch_rename`, `touch_remove`,
  `touch_assign` (with `--from` to move a mapping), `touch_unassign` and `touch_check --fix` do what the inspector
  does. Edit mode only, one Undo step each; a mapping in an action asset is not undone by Ctrl+Z, as on the Mappings
  card. The menu and inspectors are unchanged.

### Changed

- **The runtime command assembly also references `Unity.Pipeline.Attributes`**, where Pipeline 0.8 moves
  `[CliCommand]`.
- **Commands no longer treat two assets' same-named actions as one** (`touch_rebind`): with project-wide actions and
  another asset both holding `Player/Jump`, the command now asks for `Asset:Map/Action` instead of changing (or failing
  on) both.
- **Hidden controls stay on the `TouchRig` device**: a disabled control, or one on an inactive
  GameObject, is now on the device and publishes rest, so showing a button never changes the device and its binding
  works at once. The C# API is unchanged: `Find` / `Get<T>` still return `null` for a hidden control. Two controls with
  the same id under one rig now collide on the device even if only one is shown.

### Fixed

- **The Device Simulator counts as a phone for a view's own Show on** (`TouchControlView`): it read the real platform,
  so a mobile-only view stayed hidden in the Simulator.

## [0.1.0] - 2026-09-29

The component library: add a Joystick or Button from the menu, map it to the actions your game reads on the Touch
Rig's Mappings card, tune how it feels, press Play. No binding path typed, no action asset opened by hand, no code.
The rig's Rig check card says what is missing, each finding with a Fix. Nothing here has been run on a phone;
compiled and tested on 6000.6 with Input System 1.20, not on the 6000.3 floor. The 0.0.3 work ships under this
number; 0.0.3 was never tagged.

### Added

- **Touch Rig inspector** (UI Toolkit cards in the gameplay package's style, light and dark), two cards:
  - **Rig check**: every finding with its fix, and Fix all.
  - **Mappings**: every action the open scenes read (`InputActionReference`, `InputActionProperty` and `InputAction`
    fields, every action of a `PlayerInput`'s asset; input modules and the bridge skipped). **+ Add mapping** picks
    an action, then a control of the right kind, or New Joystick / New Button (named after the action: `Move`,
    deduped in the rig). One box per control; its rows are the actions it drives. Rename a control in its box and
    every mapping moves with it. Disabled in Play.
- **Control inspector** (Joystick, Button): one line naming what the control drives with a link to the rig, then
  **Feel** (the control's own fields; origin as a segmented control) and **Look** (the view's fields: sprites,
  tints, knob travel, idle and active opacity, fade, show on).
- **`TouchMappings`** (editor): scans the open scenes for the actions they read and maps, unmaps and renames. A
  mapping is one `<TouchRig>/<id>` binding on that action, written into the action's own asset (re-imported, not
  undoable) or into the component's inline action (undoable). Nothing is stored on the control.
- **`TouchBindingAuthoring`** (editor): the asset writer under it. It only ever creates, moves, re-processes,
  renames or deletes `<TouchRig>/…` bindings, adds a `Touch` scheme once when the asset has schemes and none is
  named Touch, and refuses read-only assets (inside an immutable package).
- **`TouchRigDoctor`** (editor): duplicate or empty ids, missing EventSystem or the wrong input module, a rig
  without a bridge, a control with no view or outside a rig, a control past the safe area, an ambiguous or
  read-only asset, a binding outside the Touch scheme, a Touch scheme that does not list `<TouchRig>`, and the
  traps of Unity 6's default actions: an action a touch control drives that also reads `<Pointer>/delta` in the
  Touch scheme (dragging the stick would also turn the camera), and an action that reads
  `<Touchscreen>/primaryTouch/tap` (any tap fires it). Each with a fix labelled with exactly what it changes.
- **Default skin**: a joystick ring with chevrons and a dark well, a knob with a soft shadow, a button disc and
  rim, in `Runtime/UGUI/Skin/` (redrawn by `Editor/Skin/TouchSkinGenerator`). White parts take the tint.
- Views: `IdleAlpha` / `ActiveAlpha` / `FadeSeconds` (a joystick idles at 40%), `ShowOn` (Always or mobile
  platforms only), `TouchButtonView.PressedScale` (0.92), `TouchJoystickView.KnobTravel` (0.7). Applied
  through a `CanvasGroup` added only when needed.
- `TouchInputBridge.Actions`: editor-only; the asset the Doctor's binding checks read. Never read in Play.

### Changed

- **Mappings are made on the rig, starting from the action**. Many to many: a control
  may drive several actions, and an action may be driven by several controls.
- **A control's GameObject is named after its id**, and follows it: spawning (`Joystick1`), New Joystick from a
  mapping (`Move`), a rename on the rig, a doctor fix. One way only: renaming the object in the Hierarchy leaves
  the id (and so the bindings) alone, and the next id change renames it back.
- **Control ids are auto-generated and no longer assumed to be action names**. New controls
  get `Joystick1`, `Button1`. The **Twin-Stick Layout's controls start unmapped** like any other.
- Menu order: Joystick, Button, Twin-Stick Layout. The new control is selected.
- Existing joystick views now fade to 40% when idle (the new `IdleAlpha` default). Set Idle opacity to 1 in
  the Look card to keep the old look.
- The view and bridge inspectors shrink to one line pointing at the Look card / the Touch Rig inspector.

### Removed

- **Gesture Area and its bridge control**: `TouchGestureArea`, `GestureEvent`, `GestureKind`,
  `GestureMask`, `TouchGestureAreaView`, `TouchGestureControl`, `TouchRigDevice.Gesture(id)` and the Gesture
  Area menu item. The package ships the joystick and the button while their workflows are polished; areas come
  back later. A scene that used one shows a missing script: delete the component, or stay on 0.0.2.
- **The Touch Rig menu item.** An empty rig had no use of its own: adding a Joystick or Button creates the rig
  when there is none above it, and Twin-Stick Layout always makes a new one.
  `Add Component ▸ Building Blocks ▸ Touch Input ▸ Touch Rig` still exists.
- The Touch Rig inspector's device card (binding paths with Copy buttons).

### Behaviour to know

- **Response has no UI for now** (sensitivity and invert Y for sticks, Press / Hold / Tap for buttons). The
  authoring code is kept and comes back on the mapping row (open decisions). Feel (dead zone, origin,
  saturation) lives on the control and applies to the C# API and the Input System alike.
- **Several touch controls may drive one action**, each with its own binding. On a Value action the Input System
  keeps the stronger input while both are touched; it does not add them.
- The Doctor's binding checks (Touch scheme, pointer traps) still read one resolved asset: the bridge's Actions
  field, else the one asset the scene's components reference, else project-wide actions.
- The first write to an action asset re-serializes the file in the Input System's current format (for example
  a `"priority": 0` on each action), as its own editor does on save. Asset writes are not on Unity's Undo stack;
  inline-action writes and id renames are.
- Mappings cannot change in Play (the actions are enabled); the Mappings card is read-only there.

## [0.0.2] - 2026-09-22

The Input System bridge: bind an `InputAction` to `<TouchRig>/move` and an on-screen joystick drives it,
with no code. Nothing here has been run on a phone; compiled and tested on 6000.6 with Input System
1.20, not on the 6000.3 floor.

### Added

- `Unity.BuildingBlocks.TouchInput.InputSystem`, a runtime assembly compiled only when
  `com.unity.inputsystem` is installed (`BB_TOUCH_HAS_INPUTSYSTEM`); the package still compiles and
  tests without it.
- `TouchInputBridge`: on a `TouchRig`, adds a `TouchRigDevice` whose layout (`TouchRig`) is generated
  from the rig's enabled controls, and queues one state event per Input System update. The binding
  path is the ActionId: `Move` → `<TouchRig>/move`, `Look Camera` → `<TouchRig>/lookCamera`. A
  joystick is a `Stick`, a button a `Button`, a gesture area a `TouchGestureControl`
  (`<TouchRig>/gesture/tap`, `/swipe`, `/dragDelta`). Empty and colliding ids are skipped with one
  warning each. One bridge at a time; `Rebuild()` picks up controls enabled later. Never a gamepad.
- `TouchRigDevice` (`current`, `Stick(id)`, `Button(id)`, `Gesture(id)`, `BindingPathFor(id)`) and
  `TouchGestureControl` (`dragDelta`, `tap`, `swipe`).
- Editor: every rig the Touch Input menu creates carries a `TouchInputBridge` when the Input System is
  installed and active. The bridge's inspector shows the device status and every binding path with a
  Copy button, and warns about ids left off the device; each control's inspector shows its binding path.

### Behaviour to know

- **One frame of latency.** uGUI feeds the controls after the Input System's update, so an action sees
  a value one frame after `rig.Get<T>(id)` does. The C# API stays the lower-latency path.
- A button press that begins and ends between two updates still reads 1 for one update, and a tap or
  swipe reads 1 for exactly one update, so quick gestures reach Button-type actions.
- The binding picker in the Input Actions editor lists `<TouchRig>` only while the device exists (in
  Play); paste the path from the bridge's inspector.

### API check

Built against `InputControlLayout.Builder`, `InputSystem.RegisterLayoutBuilder`,
`StateEvent.From(InputDevice, out InputEventPtr, Allocator)`,
`InputControlExtensions.WriteValueIntoEvent` and `InputEventPtr.time`, all verified present and safe
to call in Input System 1.20. No fallback taken. The bridge assembly enables `unsafe` code for one
override (`TouchGestureControl.ReadUnprocessedValueFromState`), which the Input System requires of any
custom control.

### Fixed

- Two CS0618 warnings on 6000.6 (`FindFirstObjectByType` → `FindAnyObjectByType`) in the control
  inspector and a PlayMode test.

## [0.0.1] - 2026-09-21

First testable cut. Nothing here has been run on a phone; compiled and tested on 6000.6, not on the
6000.3 floor.

### Added

- Core: `TouchControl` (one owner pointer, `Feed(in PointerSample)`, `Cancel()`, `ActionId`,
  `Changed`), `TouchJoystick` (Fixed / Floating / Floating With Leash origin, dead zone, saturation,
  radial clamp, recenter on release), `TouchButton` (`Pressed` / `Released`, `HoldSeconds` from
  sample times), `TouchGestureArea` (up to four fingers; `Tapped`, `Swiped`, `Dragged` with a
  `GestureEvent` struct, masked by `GestureMask`), `TouchRig` (registry: `Get<T>(actionId)`,
  `CancelAll`, duplicate ids reported once), `PointerSample`, `JoystickMath`.
- UGUI: `TouchControlView` base (pointer down / drag / up → samples, drag threshold off, cancels the
  control on disable, focus loss and pause), `TouchJoystickView` (base + knob, measures radius and
  rest position from its rect, `HideWhenIdle`), `TouchButtonView` (tint), `TouchGestureAreaView`
  (invisible raycast target, optional debug label), `TouchSafeArea` (fits `Screen.safeArea`).
- Editor: `GameObject ▸ Building Blocks ▸ Touch Input ▸ Touch Rig / Joystick / Button / Gesture Area /
  Twin-Stick Layout`, one Undo step each, through a single `TouchRigSpawner`; creates Canvas (Screen
  Space Overlay, 1080×1920 scale-with-screen-size, match 0.5), EventSystem (Input System UI module
  when that package is installed and active, else Standalone) and a safe-area root when missing.
  `TouchControlInspector` for every control: fields, setup warnings (no view, no rig above, Canvas
  without EventSystem) and a live read-out in Play.

### Known gaps

- `TouchGestureArea` recognizes tap, swipe and drag only; no pinch or twist.
- No layout profiles, no Input System bridge, no UI Toolkit view.
