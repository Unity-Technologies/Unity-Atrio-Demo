# Building Blocks Gameplay

A gameplay framework package: a kinematic character-movement core (`KinematicMover`) that
separates simulation from visuals and is networking-ready by design, a `Character` layer whose
processor list is the movement recipe, and an `Interactions` system built from trigger volumes and
steps.

`KinematicMover` is driven by a per-tick command struct, keeps its complete state in a
snapshotable struct, interpolates visuals separately from the simulation, and is extended
through processors instead of subclassing.

## Status

Early preview. The v0 movement core is implemented and playable: collide-and-slide,
overlap recovery, ground probing with a slope limit, ramp-tolerant step offset, ground snap,
and a `Character` layer carrying gravity/locomotion, jump with coyote time, yaw rotation, an
Input System command source, and tick interpolation. The package's `Playground/` folder assembles
all of it into a prefab you add from *GameObject ▸ Building Blocks* and drive. The
playground content is authored for URP.

The `Interactions` assembly starts at a zone: a `TriggerVolume` fires enter/exit events; an
`Interactable` runs groups of steps (Lock Player, Move Player To, Look At, Play Animation, Play
Timeline, Wait, Set Active, Set Animator Parameter, Parent To, Release Control, Unity Event), each
group starting together and the next when the current one is done; a step you write yourself appears
in the same menu (see `interaction-steps.md`). This project uses it for the door and its other interactions.

Note: `KinematicMover` applies no forces of its own — gravity and locomotion
live in processors (`MoveProcessor`, serialized in the `Character`'s own list since 0.4.0) shipped
in the `Character` assembly. A mover with no processors registered will not fall. This keeps the mover usable for non-character movers
(props, NPC animals, zero-g).

## The Player layer (required)

**The right-click menu's First Person Character does this for you**, and the
`Character` inspector's Setup card offers it as a Fix otherwise — either names the first free user
layer `Player` and moves the character's whole hierarchy onto it. To do it by hand: name layer 8 `Player` in *Edit ▸ Project
Settings ▸ Tags and Layers*, User Layer 8. The prefab is authored on layer index 8; if you already
use 8 for something else, name a different free layer and move the prefab's whole hierarchy onto it
instead.

Why it matters:

This exists because the mover has **physical presence**. It carries a real, enabled `CapsuleCollider`
and a kinematic `Rigidbody` so that `Physics.ComputePenetration` has a shape to work with — overlap
recovery is unreachable without one — and so other rigidbodies collide with the character and trigger
volumes report it (a deliberate design choice).

The cost is that the capsule **blocks everyone's queries, including your own**. A first-person
interaction ray cast from this character's camera starts inside its own capsule and hits it. So do
your enemy line-of-sight checks, your cursor picking, and anything else that raycasts from or at the
player. Put the character on its own layer and exclude that layer from those queries:

```csharp
// Interaction ray from the player's camera, ignoring the player's own capsule.
int mask = ~LayerMask.GetMask("Player");
if (Physics.Raycast(camera.position, camera.forward, out var hit, 3f, mask)) { … }
```

This is an onboarding cost, not an accident — the alternative (a disabled or hidden proxy collider)
does not work, and a collider parked on a layer that collides with nothing gives up the presence the
character needs.

The mover's own `Collision Mask` is left at *Everything*, so two players still block each other.
Narrow it on the prefab if you want a character that walks through its own kind.

## Prefab anatomy

```
FirstPersonPlayer         layer: Player — see above; tag: Player — what a Trigger Volume detects by default
├─ KinematicMover         the sim: capsule, ticks, owns the root transform
├─ CapsuleCollider        required; the mover writes its dimensions, do not edit them by hand
├─ Rigidbody              required; forced kinematic, no gravity, no interpolation
├─ MoveInterpolator       smooths Visual/ between fixed ticks
├─ PlayerInputSource      the IMoveCommandSource — swap it for AI or a cutscene
├─ Character              the host loop: reads a command, ticks the mover. Lock / Unlock take control away and give it back
│    Processors            Ground Locomotion (accel/decel, sprint, air control, gravity) → Jump (impulse, coyote time) → Yaw Rotation (facing from MoveCommand.Yaw)
├─ AnimatorDriver         feeds the hands' Animator Controller: Speed, VerticalSpeed, IsGrounded, Jumped, Landed
├─ Visual/                interpolated. Your model, Animator and hands go here.
│  ├─ Body                a debug capsule, disabled — enable it to see yourself from outside
│  └─ Hands               a placeholder block with an Animator and Playground/Animations/PlaygroundHands.controller: it bobs as you
│                         walk, dips when you land, lifts when an interactable sets Holding. Replace it with your rig, keep the wiring
└─ CameraRig/             SIBLING of Visual/, not a child of it. FirstPersonCameraRig, a package component
   └─ Camera              carries pitch, and nothing else
```
