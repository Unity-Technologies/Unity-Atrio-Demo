# Interaction steps

An **Interactable** is a list of things that happen in order: the player walks to a mark, a door
opens, a light comes on, control comes back. Each of those is a **step**. This page shows how to
make something interactive with the shipped steps, how the list is timed, and how to add a step of
your own.

## Make something interactive

1. Right-click in the Hierarchy ▸ **Building Blocks ▸ Interactable + Trigger Volume**. You get an
   `Interaction` object with a **Trigger Volume** (the zone the player has to stand in) and an
   **Interactable** (what happens), already connected: the volume is the Interactable's **Zone** and
   its **Starts** reads *When the player presses Interact*, so E works inside the zone. Right-click a
   door first and the pair nests under it.
2. Select the Trigger Volume, press **Edit Volume** and drag the handles over the spot where the
   player should stand. Drop the "Press E" sign into **Active While Inside** and it shows only while
   someone stands there.
3. Select the Interactable. It starts with **Lock Player**, which takes the player's control for the
   run. Press **+ Add step** and pick what should happen: **Move Player To** to walk the player to a
   mark, **Look At** to turn them toward the door, **Play Animation** to play the door's clip, **Set
   Active** to switch a light, **Unity Event** to call anything else. Each new step runs after the one
   above; right-click a row and pick **Run alongside the step above** to start them together, which
   draws the two as a **Group**. **Release Control** gives control back early; without it the player is
   freed when the run ends.
4. Press Play, walk onto the pad, press E.

*Starts* has four sentences: *When Play() is called* (your code, a button, a UnityEvent), *When the
player enters the zone*, *When the player presses Interact* (inside the Zone, or anywhere when it is
empty) and *When this component is enabled*. **Stop when the player leaves** cuts the run short when
they walk out mid-run. **Repeats** decides whether it can play again: *Every time* (with a wait
between runs) or *Once*, and then whether it turns off, hides or destroys its object.

Something that should react to the player without a sequence at all — a lamp that lights while you
are near — needs only the Trigger Volume: drop the lamp into **Active While Inside**, or wire **On
Trigger Enter** / **On Trigger Exit** straight to it. A zone that should only ever fire once (an intro
trigger) sets **Fire** to *Once* and picks what happens **After Firing**. And a run that only animates
the world (a switch, a bell) does not need Lock Player; delete it.

## Groups

- A group is a box of steps that **start at the same moment**.
- Each step can hold a **Delay** first, so a Release Control with Delay 1.6 beside a Play Animation
  hands control back 1.6 s into the clip.
- What comes after the group starts when the group is done: **when all finish** (the default) or
  **when any finishes**, which lets the slower steps keep running alongside what comes next.
- Every group is a box you can name: "walk to the mark while the door opens". An unnamed group takes
  its name from its steps.
- Drag a step by its ⋮⋮ handle to reorder it, into another group to run it alongside, or between
  groups to give it a group of its own. Drag a group by the handle on its header to move it, steps
  and all. Undo puts either back.
- **+ Add step ▸ Empty group** adds a named placeholder to fill later. Deleting a group's last step
  keeps the group, empty. An empty group is skipped when it plays. Dragging the only step out of an
  unnamed group removes that group; a named group stays.
- **⋮ ▸ Skip this step** turns a step off without losing its place.

## Write your own step

**Create ▸ Building Blocks ▸ Interaction Step** in the Project window writes this file for you; give
it the name of the thing it does. Save it, and the step appears in the **+ Add step** menu on every
Interactable.

```csharp
using System;
using System.Collections;
using UnityEngine;
using Unity.BuildingBlocks.Gameplay;

/// <summary>Rings a bell: plays a sound at the bell and waits for it to finish.</summary>
[Serializable]
// The name shown in the + Add step menu.
[InteractionStep("Ring Bell")]
public sealed class RingBellStep : InteractionStep
{
    // Fields show in the Interactable. [SceneObject] marks things that live in the scene
    // (a Transform, a Light); harmless to forget today.
    [SerializeField, SceneObject]
    [Tooltip("The bell's AudioSource.")]
    AudioSource m_Bell;

    [SerializeField, Min(0f)]
    [Tooltip("Seconds to hold the run after the sound starts.")]
    float m_HoldFor = 1f;

    public override IEnumerator Run(Interaction interaction)
    {
        // Run is a coroutine: yield return interaction.Wait(1f) waits one second, yield break ends the step.
        if (m_Bell == null)
            yield break;                       // nothing to do: skip

        m_Bell.Play();
        yield return interaction.Wait(m_HoldFor);
    }

    public override void Stop(Interaction interaction)
    {
        if (m_Bell != null)
            m_Bell.Stop();                     // the run was cut short: put things back
    }
}
```

What each part does:

- **`Run`** is the step. Return when you are done; `yield return null` waits a frame, and
  `yield return interaction.Wait(seconds)` waits that long on the interactable's clock. Instant work
  can `yield break` right away.
- **`Stop`** is called only if the run is cut short while this step is going. Undo whatever `Run`
  changed and had not yet put back. Leave it out if there is nothing to undo.
- **`interaction`** hands you the run: `interaction.Player` (the character, which can be empty when
  the scene has none), `interaction.Interactable`, `interaction.Elapsed`.
- Fields marked `[SerializeField]` (or public) show in the step's row, exactly as on a component.
- **`[InteractionStep("Ring Bell")]`** is the name the **+ Add step** menu shows; leave it off and the
  class name minus `Step` is used.
- **`[SceneObject]`** marks a field that points at something in the scene (a Transform, an Animator, an
  AudioSource) rather than at an asset; it changes nothing today, and it is what will let steps be saved
  as reusable assets later without you touching the code.

Two things a step is not: it is not a component (no `gameObject`, no `OnDisable`; everything it needs
comes through `interaction`), and it never uses Unity's `WaitForSeconds` — use `interaction.Wait`.

## Stopping early

Tick **Stop when the player leaves** and walk out of the zone, call **Stop** on the Interactable (a
button in Play mode, a UnityEvent, or `interactable.Stop()` from code), or switch the component or its
object off. Every step that had begun gets its `Stop`, in the order they started, control comes back
to the player, then the **If stopped early** steps run: a door that shuts, a light that goes off. In
code, `Interrupted` fires instead of `Finished`.
