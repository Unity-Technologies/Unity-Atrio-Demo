# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Added an **A-Right Gamepad** row to the Input Prompt Style, for platforms whose gamepads put A on the
  east face button. Left empty, it falls back to the *Gamepad* row. The Input Device Tracker reports
  an A-Right gamepad on those platforms, and in the Editor while one is the active build target. Input
  Prompt's *Preview* gets an *A-Right Gamepad* option for testing.
- Added the `InspectorLabel` attribute, which shows a field in the Inspector under a given label.

## [0.4.5] - 2026-10-23

Look to interact, the courtyard's ask after 0.4.4: an Interactable's button can work only while the
camera looks at it, so a light switch no longer answers E from across the room. Off by default; existing
scenes are unchanged.

### Added

- Added **Looking** to the Interactable's Starts card, under Button. With *Must look at it*, the button
  works only while the camera looks within *Max angle* (25° by default) of the *Look point*, with a
  few degrees of *Margin* once looking so it does not flicker. On *Anywhere*, a *Max distance* (2.5 m
  by default, 0 for any) means a switch needs no Trigger Volume. Looking away never cuts a run short,
  and `Play()` from code is not affected. Selected, the Interactable draws its look cone in the Scene
  view.

### Changed

- An Input Prompt whose *Show While Enabled* is an Interactable set to *Must look at it* shows only its
  dot, and ignores the button and taps, while the player is in the zone but looking elsewhere.

## [0.4.4] - 2026-10-19

The courtyard's asks after 0.4.3. Stairs: no more sideways drift climbing at an angle, no stutter
going down, no bounce against the railing. Lock Player can lock walking only. Re-released on
2026-10-19 without the Held Item Socket and Play Animation *Origin* of the first 0.4.4 tag: both were
demo-specific and belong in the consuming project.

### Added

- Added **Lock** to the Lock Player step: *Movement and look* (default, as before) or *Movement only*,
  which stops walking but leaves the player free to look around. A Move Player To or Look At step that
  turns or tilts the player takes the view while it runs, starting from where the player is looking, and
  hands it back when it ends; a Move Player To that only places the player leaves looking free.
  When control comes back the view stays where the player looked and the body turns to it. Needs the
  First Person Camera Rig, or another view that implements the new `IFreeLook`; without one the view
  stays locked too. While a Play Animation rides a *Point Of View*, the view is the clip's.

### Changed

- The playground's tower station (3) locks movement only, so you can look around while the tower lights.

### Fixed

- Fixed the character bouncing in place when walking into a railing at an angle on ramp stairs
  whose ramp and railing are one mesh collider. It now slides along the railing and keeps climbing.
- Fixed a vertical stutter walking down ramps and ramp stairs at walking speed: the character
  dropped only every other physics tick. It now follows the slope down every tick.
- Fixed the character turning sideways while climbing a steep ramp at an angle, which bounced the
  player between the railings of narrow ramp stairs. Aiming 10° off straight up a 42° ramp used to
  travel 67° off; it now travels 10° off. Head-on climbing speed is unchanged. Ramps within a few
  degrees of the slope limit can still drift a few degrees.

## [0.4.3] - 2026-10-08

An experimental Input Prompt: a screen-space prompt pinned to a point in the world, with a hint dot
from a distance and a glyph and label per device inside its zone. The playground's switch, door and
torch use it. Its fields and look may still change. A project on 0.4.2 upgrades by bumping the pin.

### Added

- Added the **Input Prompt** component, **experimental**: its fields and look may change in a later
  version without an upgrade step. A screen-space prompt pinned to a point in the world. A dot
  shows when the camera is within *Hint Distance* of the anchor; inside its zone a glyph and a short
  label show and the button fires *On Pressed*. It hides while the player's control is locked, and while
  the component in *Show While Enabled* is off. On an Interactable that starts *Volume, On press* with
  a zone, its ⋮ menu ▸ **Add Input Prompt (Experimental)** adds one that takes its zone and a copy of
  its button, starts it, and hides once it turns itself off; the Interactable is set to *Manual*.
- Added the **Input Prompt Style** asset: a glyph image or text, a label and *Hide* per device
  (keyboard and mouse, gamepad, touch), plus backgrounds, text colour, font and sizes. The glyph
  follows the device the player pressed last; *Preview* on the prompt forces one for testing. On
  touch, tapping the prompt counts as a press.
- Added the **World Anchor** component: keeps a UI element over a point in the world at a constant
  size on screen, for nametags and health bars too.
- The built-in prompt look and World Anchor need the uGUI package (`com.unity.ugui`). The package does
  not depend on it: without it, a prompt draws only through a *View Prefab* you assign.

### Changed

- The playground's switch, door and torch stations show Input Prompts with the courtyard art; their
  "Press E" signs are removed. Their Interactables start from the prompt and are set to *Manual*.

## [0.4.2] - 2026-10-08

Courtyard feedback on 0.4.1, and the 0.3.x legacy removed. Move Player To now also tilts the view by
default, step groups can be empty and dragged, and the Trigger Volume Group gains Active While Inside.
A project on 0.4.1 upgrades by bumping the pin; one still on 0.3.x upgrades to 0.4.1 and runs
**Building Blocks > Upgrade from 0.3.1** first.

### Added

- Added **Turn Body** and **Tilt View** to the **Move Player To** step. Both are on by default, so the
  player turns to the target's facing and the view tilts up or down to the target's tilt, eased with
  the move. The tilt stays within the camera rig's pitch limits, and the target's roll is ignored.
- Added **Empty group** to the Interactable's **+ Add step** menu: a group with no steps, to name now
  and fill later. A run skips an empty group without waiting a frame, and the Steps footer marks it
  "(empty)".
- Added a drag handle to each step group's header in the Interactable inspector. Drag it to move the
  whole group up or down; Undo puts it back.
- Added **Active While Inside** to the **Trigger Volume Group**. Objects in it switch on when
  something enters any member volume and off when the last occupant has left them all; walking
  between overlapping members of the same priority keeps them on; moving into a lower-priority
  member can switch them off and on for an instant. The group's inspector now has an **Actions** card like the Trigger Volume's: Built-in Actions, then
  Custom Actions with On Entered Any and On Exited All. Groups saved before load with an empty list
  and their events as wired.

### Changed

- Deleting the last step in a step group now keeps the group, empty, with its name. Dragging the only
  step out of a group you named also keeps the group; a group you never named is still removed.
- **Move Player To** steps saved before this version now also tilt the view to the target's tilt.
  Turn **Tilt View** off to keep the old behavior, and turn it off where a **Look At** in the same
  group tilts the view.
- Shortened the API documentation comments so each one states what a member does, its setup and
  its limits, without references to internal design documents.
- Changed the **Gravity** tooltip on Ground Locomotion and the **Target** tooltip on **Look At**
  to plain wording.

### Removed

Projects still on 0.3.x: upgrade to 0.4.1 and run **Building Blocks > Upgrade from 0.3.1** first.
The 0.4.0 notes said these would go in 0.5.0; they go now because the only project using the package
is already on 0.4.1.

- Removed the ten effect components (`WaitEffect`, `MoveToPoseEffect`, `LookAtEffect`,
  `PlayAnimationEffect`, `PlayTimelineEffect`, `SetAnimatorParameterEffect`, `SetActiveEffect`,
  `ParentToEffect`, `ReleaseControlEffect`, `UnityEventEffect`), `InteractionEffect`,
  `IInteractionEffect` and `Interactable.Effects`. Use the Interactable's steps.
- Removed `GroundLocomotionProcessorComponent`, `JumpProcessorComponent` and
  `YawRotationProcessorComponent`. Use the Character's processor list.
- Removed **Building Blocks > Upgrade from 0.3.1**, with the public `EffectToStepConverter`,
  `ProcessorComponentConverter` and the legacy members of `InteractionWiring`.
- Removed the hidden 0.3.x fields on the Interactable, Trigger Volume and Trigger Volume Group, and the
  load-time moves of `m_PlayOnce` and `m_Input`. A 0.3.x scene opened without upgrading first loses
  those settings.
- Removed the console warnings about 0.3.x components, and the old serialized names of the Trigger
  Volume's **On Trigger Enter** and **On Trigger Exit** (`m_OnEntered`, `m_OnExited`). A scene last
  saved on 0.3.x loses listeners stored under those names; open and save it on 0.4.1 first.
- Removed `MoveToPoseStep.BlendIntoNextClip`, a hidden field that did nothing.
- Removed the setup check's "Processors from before 0.4.0" error. A custom move processor component
  is still reported as not run.
- Removed unused public editor UI types and members: `Card`, `Heading`, `InspectorSection`,
  `ModuleCard`, `Callout.WithBullets` and `StepListView.StepCount`.
- A copy of the 0.2.0 sample still in `Assets/Samples/` now shows Missing Script components. Delete
  the folder; the playground ships in the package.

### Fixed

- Fixed setting `TriggerVolume.ActiveWhileInside` from code while someone was inside: the objects
  dropped from the list stayed on and the new ones stayed off until the next entry.
- Fixed a **Trigger Volume Group** staying occupied, without firing On Exited All, when the last
  collider inside it was destroyed.
- Fixed the Interactable's **Test** card warning that a run "didn't start" when the run only had
  instant steps or empty groups, so it started and finished at once.
- Fixed the **First Person Camera Rig** pointing the old way for the rest of a frame after **Look At**
  or **Move Player To** turned a locked player, so a camera ride that started in that frame started
  from the wrong direction.
- Fixed the Interactable inspector, its Steps lists and the Character's Move Processors list logging
  errors such as "SerializedObject of SerializedProperty has been Disposed" when the scene changed, an
  edit was undone, or a menu opened earlier was used after the inspector closed, for example on
  entering Play mode or selecting another object.

## [0.4.1] - 2026-09-29

### Fixed

- Fixed the **Button** on an Interactable so a binding added to it, such as a rebound key or an
  on-screen touch control, now starts the Interactable at runtime instead of only E and the west
  face button.

## [0.4.0] - 2026-09-25

To upgrade from 0.3.x, commit your project, then run **Building Blocks > Upgrade from 0.3.1**.
Interactables and characters do nothing until you do. The tool converts every scene and prefab under
`Assets`, saves them, and lists what it changed, what to check, and what to fix by hand.

- Rebinding done directly on **Player Input Source** or **First Person Camera Rig** resets to the
  default bindings. Select the character, click **Copy into project** in its **Setup** card, and
  rebind in the copied input asset.
- Processor values overridden on a scene instance of the **First Person Player** prefab reset to the
  prefab's values. The upgrade report lists each one so you can re-enter it.

### Added

- Added interaction steps. An **Interactable** now holds its whole sequence in its own inspector, as
  named groups of steps. Steps in a group start together, each after its own **Delay**, and the next
  group starts when the current one finishes.
- Added the **Lock Player** step, which takes the player's control for the rest of the run. New
  Interactables start with one.
- Added the **If stopped early** step list, which runs after a run is cut short and control returns,
  for example to shut a door or switch a light off.
- Added support for custom steps. Derive a `[Serializable]` class from `InteractionStep`, implement
  `Run` (and `Stop` if it changes anything), and it appears in the **+ Add step** menu.
  **Create > Building Blocks > Interaction Step** writes a starter file.
- Added the **Starts** card to the Interactable. It starts from its Trigger Volume (**On enter** or
  **On press**), **On enable**, **Manual**, or **Anywhere**, for a button that works wherever the
  player is. A summary line reads the result back, such as "Plays when the Player presses E in Torch
  zone."
- Added the **Repeats** card to the Interactable: plays **Every time** (with **Wait between**) or
  **Once**, then **Turn off**, **Hide object**, or **Destroy object**.
- Added the **Test** card to the Interactable. **Play** enters Play mode, moves the player into the
  Interactable's volume, and runs it.
- Added **Active While Inside** to the Trigger Volume, which switches GameObjects or components on
  when something enters and off when the last one leaves.
- Added **Fires: Once** to the Trigger Volume, with **After firing** set to **Keep active**,
  **Turn off**, or **Destroy**. Use `HasFired` and `Rearm()` from code.
- Added **Speed At Slope Limit** to the **Kinematic Mover** (default 40%). It sets walking speed on
  the steepest walkable ramp, up and down; flatter ramps blend toward full speed.
- Added support for custom move processors. Derive a `[Serializable]` class from `MoveProcessor` and
  it appears in the Character's **Add a processor** menu.
- Added custom buttons for move processors. Add a row under **Custom Buttons** on
  **Player Input Source**, declare an `InputSlot` field in the processor, and read
  `command.Pressed(slot)` or `command.Held(slot)`.
- Added `Character.GetProcessor<T>()`, `SetProcessorEnabled<T>(bool)`, and
  `EnableProcessor(name)` / `DisableProcessor(name)` for UnityEvents.
- Added **Building Blocks > Upgrade from 0.3.1**, which converts 0.3.x scenes and prefabs.
- Added setup checks for the input asset and custom buttons, each with a one-click fix.

### Changed

- Changed how an Interactable connects to a Trigger Volume. The Interactable now picks its volume on
  its **Starts** card, and nothing is wired on the volume. The Trigger Volume lists the Interactables
  that start from it in a read-only **Connected Interactables** card.
- Changed move processors from components into a list on the **Character**. The
  **Move Processors** list sets run order, on/off, and tuning in one place. Edits to
  `character.Processors` apply from the next tick.
- Changed the actions on **Player Input Source**, **First Person Camera Rig**, and the Interactable's
  **Button** to accept either an inline action or a reference to an action in your `.inputactions`
  asset. On-screen touch controls that drive the same asset work with no extra wiring.
- Changed **GameObject > Building Blocks > First Person Character** to copy the package's input asset
  to `Assets/BuildingBlocks/Input/FirstPersonControls.inputactions` on first use, so you edit
  bindings in a file you own.
- Changed the First Person Camera Rig to read mouse and gamepad stick through one **Look** action,
  with separate **Sensitivity** and **Stick Sensitivity**.
- Changed disabling an Interactable so it now stops a run in progress and releases the player.
- Renamed the Trigger Volume events to **On Trigger Enter** and **On Trigger Exit** (C#:
  `TriggerEntered`, `TriggerExited`). Existing wiring loads under the new names. The Trigger Volume
  Group's C# events are now `EnteredAny` and `ExitedAll`.
- Renamed the Play Animation **Camera** field to **Point Of View** (`PointOfView`). It accepts any
  GameObject the clip moves, so no Camera component is needed.
- Renamed **Move To Pose** to **Move Player To** in the step menu.
- Changed `MoveCommand` to carry 32 custom buttons in `PressedButtons` and `HeldButtons`. The struct
  is 8 bytes larger.
- Rebuilt the Trigger Volume inspector as four cards: **Volume**, **Detection**, **Actions**, and
  **Connected Interactables**.
- Reorganized the playground scene so each station is named for the component it teaches.

### Deprecated

- Deprecated the effect components (`InteractionEffect`, `IInteractionEffect`, and the ten shipped
  effects). They compile but no longer run, and are hidden from **Add Component**. The upgrade tool
  converts them to steps. They will be removed in 0.5.0.
- Deprecated `GroundLocomotionProcessorComponent`, `JumpProcessorComponent`, and
  `YawRotationProcessorComponent`. They no longer run, and the upgrade tool moves them into the
  Character's list. They will be removed in 0.5.0.

### Removed

- Removed `Interactable.Actor`. The player is whoever entered the volume or pressed the button,
  otherwise the object tagged Player. Use `Play(KinematicMover)` to pick one from code.
- Removed `Interactable.PlayOnce` and the **On Finished** / **On Interrupted** UnityEvents. Use the
  **Repeats** card, a **Unity Event** step in the last group, or the C# `Finished` and `Interrupted`
  events.
- Removed the Trigger Volume **On First Entered** / **On Last Exited** events and the C#
  `BecameOccupied` / `BecameEmpty` events. Use **On Trigger Enter** / **On Trigger Exit**.
- Removed support for `IMoveProcessor` components on a Character. Rewrite them as `MoveProcessor`
  classes and add them to the list.
- Removed `FirstPersonCameraRig.LookStickAction` and the **Look Stick** field. Use `LookAction`.
- Removed the Blocks Gallery window and the **?** button on Building Blocks inspectors. Create
  objects from **GameObject > Building Blocks**.

### Fixed

- Fixed ramp movement so uphill and downhill speeds now match. A 35° ramp was climbed at a third of
  walking speed; at the default **Speed At Slope Limit** both directions now run at about two
  thirds, so climbs feel faster and descents slower than in 0.3.1.
- Fixed Trigger Volumes so a character with several colliders now counts as one entrant, and a
  collider destroyed inside counts as having left.
- Fixed Building Blocks inspector cards so they now open and close instantly, like Unity foldouts.

## [0.3.1] - 2026-09-15

### Added

- Added a **Camera** field to the **Play Animation** effect. While the clip plays, the player's view
  follows a camera inside the animated object, then blends back to the head.
- Added **Camera options** to Play Animation: **Forward Axis**, **Up Axis**, the two blend times,
  and **Copy Field Of View**.
- Added `ICameraAnchor`, so a custom camera rig can support Play Animation's camera. The First Person
  Camera Rig implements it and adds `AttachTo`, `Release`, `IsAnchored`, `Anchor`, and
  `Advance(deltaTime)`.
- Added a setup check that warns when an Animator sits on or above the camera rig's camera.
- Added a camera shot to the playground door.

## [0.3.0] - 2026-09-14

### Added

- Added **Start with the step above** and **Delay** to every effect, so steps can overlap.
- Added five effects: **Look At**, **Set Animator Parameter**, **Set Active**, **Parent To**, and
  **Release Control**.
- Added **X**, **Y**, and **Z** toggles to **Move To Pose**. An axis that is off keeps the player's
  current value.
- Added **Speed**, **Blend In**, and **Blend Out** to **Play Animation**.
- Added **After Finish** to the Interactable: **Nothing**, **Disable Component**,
  **Deactivate Object**, or **Destroy Object**.
- Added `Interactable.ReleasePlayer()`, `HoldsPlayer`, `IsRunning`, `HasFinished`, `ElapsedOf`, and
  `RunningCount`.
- Added **Animator Driver**, which feeds your Animator Controller the character's speed, vertical
  speed, grounded state, and jump and land triggers.
- Added **GameObject > Building Blocks** menu items for the First Person Character, Trigger Volume,
  Trigger Volume Group, Interactable, and Interactable + Trigger Volume.
- Added **Make Interactable** and **Make Trigger Volume** to the Transform context menu, and
  **Add Trigger Volume, connected** / **Add Interactable, connected** to their component menus.
- Added custom icons for the Character, Trigger Volume, and Interactable.
- Added placeholder hands, a torch station, and more interaction examples to the playground.

### Changed

- Renamed **Interaction Sequence** to **Interactable**, including the `InteractablePlayOn` and
  `InteractableAfterFinish` enums. Scenes keep their references; code that names
  `InteractionSequence` needs renaming.
- Renamed Set Active's **Revert When Sequence Ends** to **Revert When Run Ends**.
- Renamed the trigger volumes the package creates from `Zone` to `Trigger Volume`.
- Moved the playground (player prefab, scene, input asset, animations) into the package under
  `Playground/`, replacing the First Person Character sample. After upgrading, delete
  `Assets/Samples/Building Blocks Gameplay/`. The playground uses URP materials and renders magenta
  in other render pipelines.
- Moved `FirstPersonCameraRig` into the package, in the `Unity.BuildingBlocks.Gameplay` namespace.

### Removed

- Removed the First Person Character sample. Its content now ships in the package.

### Fixed

- Fixed stepping so the player now walks onto kerbs, 0.25 m stairs, and ramp lips without launching
  or being blocked.
- Fixed ramps so the player no longer hops off the top or lifts off when stopping on one.
- Fixed stopping an Interactable so it now also stops its running effects.
- Fixed Interactables so an effect that throws is logged and the run continues.
- Fixed the character body so it no longer spins at low frame rates.
- Fixed `PlayerInputSource` so it no longer logs a `NullReferenceException` after a domain reload
  with Input System 1.20 or 6.x.

## [0.2.0] - 2026-09-10

### Added

- Added **Trigger Volume**, a box that detects the Player tag, a specific object, or layers, with
  priority for overlapping volumes and enter and exit events.
- Added **Trigger Volume Group**, which treats several volumes as one area.
- Added **Interaction Sequence**, which runs effects in order and can lock the player for the run.
  It plays manually, when enabled, or on a button press.
- Added the **Wait**, **Move To Pose**, **Play Animation**, **Play Timeline**, and **Unity Event**
  effects.
- Added `Character.Lock` and `Unlock`, which take and return the player's control without a visual
  snap, and `KinematicMoverExtensions.Teleport`.
- Added the **Blocks Gallery** window (**Building Blocks > Blocks Gallery**), and a **?** button on
  every Building Blocks inspector that opens its card.
- Added **Building Blocks > Report an Issue**, which opens a GitHub issue form with the package and
  editor versions filled in.
- Added **Eye Forward**, **Look Smoothing**, **Stick Sensitivity**, and `LookEnabled` to the sample
  camera rig.
- Added interaction stations to the sample playground, and an **Interact** action to its input
  asset.

### Changed

- Changed move processors so one whose enabled checkbox is off no longer runs.
- Added the `com.unity.modules.animation` and `com.unity.modules.director` dependencies.

### Removed

- Removed the **First Person Character Setup** window. Its checks and fixes are in the Character
  inspector's **Setup** card.
- Removed material fixing, including **Fix Materials for Active Render Pipeline**. Use your render
  pipeline's material converter.
- Removed the unused `Interactor`, `IInteractable`, `Interactable`, `PlayerControlLock`, and
  `FirstPersonInteractInput`.

## [0.1.1] - 2026-08-18

### Fixed

- Fixed compilation on Unity 6000.4 and newer, where `Object.GetInstanceID()` is obsolete.

## [0.1.0] - 2026-08-12

### Added

- Added `KinematicMover`, a kinematic capsule controller with collide-and-slide, ground snap, step
  offset, and slope limit.
- Added the `Character` component, which runs the mover every fixed update with the
  `GroundLocomotionProcessor`, `JumpProcessor`, and `YawRotationProcessor`.
- Added `PlayerInputSource`, which reads the Input System with default keyboard and mouse bindings.
- Added `MoveInterpolator`, which smooths visuals between fixed updates.
- Added the `IMoveProcessor`, `IMoveCommandSource`, and `ICollisionBackend` extension points.
- Added the Character inspector, with setup checks, one-click fixes, and processor ordering.
- Added the First Person Character sample: player prefab, camera rig, input asset, and playground
  scene.
- Added **Building Blocks > First Person Character Setup** and
  **Building Blocks > Fix Materials for Active Render Pipeline**.
