# Atrio

![The Atrio courtyard at midday](Documentation/Images/atrio-courtyard-day.jpg)

**Atrio** is a small, explorable Unity scene: a sunlit, plant-filled courtyard with a few resident cats, an orrery that controls the sun, and a flashlight for exploring after dark.

Atrio was first made for the **Unite 2026 Keynote and show-floor demos in Tokyo on November 17th**. We're sharing it so you can open it, play it, and take it apart to see how it works.

---

## What Atrio demonstrates

### New URP rendering technologies

The scene mainly exists to show new features in the Universal Render Pipeline (URP):

| Feature | What it does in Atrio |
|---|---|
| **Surface Cache Global Illumination (SCGI)** | Real-time global illumination with no baking. Light bouncing off the warm plaster walls and tiled floors updates as the sun moves, and the scene has no lightmaps at all. Even the fireflies add a soft glow to their surroundings. |
| **Screen Space Reflections (SSR)** | Reflections that follow the scene in real time on glossy surfaces such as the courtyard's floor tiles. |
| **Ground Truth Ambient Occlusion (GTAO)** | Physically based contact shadowing in the corners, under railings, and around plants and props, to ground objects in the space. |
| **Slope-Scale Depth Bias** | Shadow bias that scales with how steeply a surface faces the light (and with the soft-shadow filter size). It removes shadow acne on angled surfaces without the "floating" shadows a large constant bias produces. |

SCGI, SSR and GTAO are set up in the project's renderer and volume profiles (see [Exploring the project](#exploring-the-project)), so you can turn each one on and off and compare. Slope-Scale Depth Bias is a project-wide mode, under **Project Settings > Graphics > URP > Depth Bias Mode**.

### Building Blocks (early preview)

Atrio also includes an **early preview of upcoming Building Blocks components**. These are general-purpose, reusable gameplay pieces you can drop into your own projects:

- **First-Person Controls**: a complete first-person character with walking, sprinting, jumping and looking around, driven by the Input System.
- **Seamless-Animation Interactables**: interactions that blend the player's view and first-person arms into an animation and back out again without a cut. Atrio uses them for opening doors, flipping light switches, pressing the Orrery's buttons, taking out the flashlight and petting the cats.
- **Modular Touch-Input elements**: on-screen joysticks and buttons that can be combined freely, so the same scene plays well on touch devices.
- **Assisted input binding**, plus interaction prompts that show the right glyph for the device in use (keyboard, gamepad or touch), including gamepads whose face buttons are laid out differently.
- **…and more.**

Interactions are built as sequences of steps. The project's own custom steps are in `Assets/Scripts/BuildingBlocks_InteractableSteps` (for example *Animate Day Cycle*, *Pet Cat*, *Torch* and *Trigger Interactable*). They're a good starting point for writing your own.

> **Note:** Building Blocks is an early preview. Component names, APIs and workflows may change before release.

### Neural Rendering (early preview)

Neural Texture Compression (NTC) is available as part of the Neural Rendering package. NTC lets you encode texture sets in a more efficient representation, delivering on average 50% compression savings compared to BCn on URP materials, at the cost of an overhead when decoding materials per pixel.

Not all materials are compressed with NTC, so URP supports mixing traditional and NTC materials in the same scene.

The Atrio project contains both traditional and neural texture representations, and you can preview NTC on and off in the Editor.

Press `Tab` to open the Debug Panel, then press `N` or click the toggle to switch NTC on and off.

<p align="center">
  <img src="Documentation/Images/ntc-panel.jpg" alt="The Debug Panel with the NTC toggle on, showing GBuffer, GPU and CPU timings and FPS">
</p>

When you make a build, Unity only includes the texture sets needed for the selected NTC configuration. In Atrio, we saw 30% texture memory savings with NTC enabled.

For how to use NTC, train materials and create custom shaders that support NTC, see the Neural Rendering package documentation.

> **Note:** The Neural Rendering package is in early preview. Shaders, APIs, networks and workflows may change before release. NTC quality and performance is not final.

---

## Things to do in the scene

### Visit the cats!

Several cats live in the courtyard, lounging on stools, railings and balconies. Find them and walk up close. Some of them will happily accept a pet.

<p>
  <img src="Documentation/Images/atrio-cat-tiger.jpg" alt="A brown and white tabby cat on a painter's stool" width="49%">
  <img src="Documentation/Images/atrio-cat-patches.jpg" alt="A ginger tabby cat perched on the upper balcony railing" width="49%">
</p>

### Change the time of day with the Orrery

The Orrery stands on the upper balcony. Use it to move the sun across the sky and take the courtyard from midday to midnight and back. The sky, sun, moon, exposure, reflections and the courtyard lamps all change with it, and SCGI updates the bounced light as it goes. The lamps come on one by one rather than all at once.

![The Orrery on the upper balcony](Documentation/Images/atrio-orrery.jpg)

### Take out the flashlight and explore at night

Once it's dark, take out the flashlight and switch it on to explore the courtyard by torchlight. You can take it out or put it away from anywhere in the scene (see [Controls](#controls)).

<p>
  <img src="Documentation/Images/atrio-courtyard-night.jpg" alt="The courtyard at night with the lamps lit" width="49%">
  <img src="Documentation/Images/atrio-flashlight.jpg" alt="Lighting up the ginger cat on the balcony railing with the flashlight" width="49%">
</p>

### Look out for the courtyard's other residents

Birds flock and settle around the courtyard, butterflies flutter between the plants, and gusts of wind blow through the doorways. After dark, fireflies come out. Keep exploring at night, all the way to the top floor, to find out where they're going.

---

## Getting started

### Requirements

- **Unity 6.7** (`6000.7.0b8` or later). Install it from the Unity Hub.
- **Universal Render Pipeline** `17.7.0` (resolved automatically from the project manifest).
- **Neural Rendering** `1.0.0-exp.1` (resolved automatically from the project manifest).

### Open and play

1. Add the project folder in the Unity Hub and open it with the Unity version above. Package Manager downloads every dependency on first open. The Building Blocks packages are already included in the `Packages` folder.
2. Open the scene **`Assets/Scenes/Atrio.unity`**.
3. Press **Play**. You start just outside the courtyard's entrance door.

![The entrance door, where the player starts](Documentation/Images/atrio-entrance.jpg)

### Controls

| Action | Keyboard & mouse | Gamepad | Touch |
|---|---|---|---|
| Move | `W` `A` `S` `D` | Left stick | Left on-screen joystick |
| Look | Mouse | Right stick | Right on-screen joystick |
| Jump | `Space` | South button | — |
| Interact | `E` | West button | Action button |
| Take out / put away flashlight | `T` | East button | Flashlight button |
| Flashlight on / off (while it's out) | `F` | North button | Lightbulb button |
| Sprint | `Left Shift` | Left stick press | — |
| Slow-Walk | `Left Ctrl` | — | — |
| Release mouse cursor | `Esc` | — | — |
| Debug Panel | `Tab` | — | — |
| Toggle NTC on/off | `N` | — | — |

Gamepad buttons are named by their position among the four face buttons (North, South, East and West), not by the letter or symbol printed on them, because those differ between gamepads.

When you're close to something interactive, a prompt shows the button to press for your current device, drawn to match your gamepad's face-button layout.

---

## Built for many platforms

Atrio is built to run on many platforms, from desktop PCs to mobile devices with touch input. The project includes build profiles for **Windows**, **macOS**, **Linux** and **Android** in `Assets/Settings/Build Profiles`.

Its optimizations aren't hidden: they're plain settings and scripts in the project, so you can study them and reuse them in your own work.

> **Note:** The Neural Rendering package is only available on Windows (DX12) and macOS (Metal).

### Quality tiers

Each platform has a **High** and a **Low** quality level, and Atrio picks one automatically for the device it's running on:

| Quality level | Render scale (STP upscaling) | Main / additional light shadow maps | Soft shadows |
|---|---|---|---|
| `Desktop_High` | 1.0 | 4096 / 4096 | High |
| `Desktop_Low` | 0.7 | 2048 / 2048 | Medium |
| `Mobile_High` | 0.5 | 2048 / 1024 | Medium |
| `Mobile_Low` | 0.4 | 2048 / none | Low |

Each level has its own URP asset (`RPAsset_*`) and volume profile (`VolumeProfile_*`), and all four render with Deferred+ through the same renderer, `Renderer_Common`. `VolumeProfile_Mobile_High` is the project's default profile; the others override only what's different: the SCGI and SSR settings on every other level, plus GTAO on `Desktop_High` and Bloom, which is turned off, on `Mobile_Low`.

How the level is chosen (see `Assets/Scripts/QualityTiers` and `Assets/Settings/Resources/QualityTierSettings.asset`):

- **Before the first scene loads**, the device's GPU, model, memory and CPU are checked against a list of rules for that platform.
- **When no rule matches**, a short GPU benchmark runs on first launch and its result is saved, so hardware released later is still classified.
- **If nothing shows the device is fast enough**, the Low level is used.
- **To force a level**, launch a build with `-quality-tier high` or `-quality-tier low`.

### Other optimizations

- **Upscaling:** every quality level uses **Spatial-Temporal Post-Processing (STP)**. `Desktop_High` renders at full resolution and uses it for temporal anti-aliasing; the other levels render at 70%, 50% or 40% resolution and STP upscales the result.
- **Fully real-time lighting:** there's no lightmap baking (see `Assets/Settings/BakedLightingDisabledSettings.lighting`). SCGI provides the indirect light, which is what makes the live time-of-day change possible.
- **Only update what changes:** the sky and sun controllers (`SkyController`, `SkyLightController`) only write their settings while the time of day is actually changing, so SCGI isn't asked to refresh every frame.
- **Tuned shadows:** 2 cascades over a 28 m shadow distance, sized to the courtyard, with slope-scale depth bias to keep them clean. Objects that cover only a small part of the screen are left out of the shadow maps, with a stricter cutoff on the mobile levels. Decorative lights such as the wall lamps don't cast shadows at all.
- **Simplified shadow casters:** the roof, windows, doors, staircases and climbing plants cast shadows from separate low-detail, shadows-only meshes instead of their full geometry.
- **Optimized geometry:** repeated pieces such as the staircase balusters are instanced, and the roof sections use simplified geometry. The GPU Resident Drawer, with GPU occlusion culling, is on at every quality level, and on the mobile levels it also skips objects that cover less than 1% of the screen.
- **Scaled GTAO:** ambient occlusion uses the Medium preset (2 directions, 4 steps, half resolution) on every level except `Desktop_High`, which uses High (4 directions, 6 steps, full resolution).
- **Lean pipeline:** URP features the scene doesn't use, such as the depth and opaque textures, are turned off.
- **Fewer physics callbacks:** trigger stay events are turned off in the physics settings, since nothing in the scene uses them.
- **Frame pacing:** V-Sync is on at every quality level, so frames are paced to the display's refresh rate. Mobile platforms ignore V-Sync and default to 30 FPS, so a simple `SetTargetFPSTo60` component raises their cap to 60 FPS.

---

## Exploring the project

| Location | What's there |
|---|---|
| `Assets/Scenes/Atrio.unity` | The demo scene. |
| `Assets/Settings/` | URP assets and volume profiles for each quality level, the renderer they all share (`Renderer_Common`) with the SCGI, SSR, GTAO and Decal renderer features, the quality tier rules, and build profiles. |
| `Assets/FoundationalPrefabs/` | The gameplay prefab containing the player, interactions, characters and UI. |
| `Assets/Scripts/` | Time-of-day and sky control (`SkyLightController`, `SkyController`, `DayCycleLightController`, `DayCycleVFXController`), quality tier selection, flashlight, audio, bird flocking, and custom Building Blocks interaction steps. |
| `Assets/Shaders/` | Stylized sky, pond, candle flame, floating lantern, foliage wind, vertex-blended walls and other custom shaders. |
| `Assets/Environment/` | The courtyard's architecture, set dressing and props. |
| `Assets/Character/` | The cats, birds and first-person arms. |
| `Assets/Audio/` | Sound effects, ambience, the `Atrio` audio mixer and the audio prefabs. See the [Audio README](Assets/Audio/README.md) for how every sound is triggered and routed. |
| `Assets/Input/` | Input System action assets for the first-person controls. |
| `Assets/VFX/` | Fireflies, butterflies and moths, the Orrery glow, torch effects and wind trails, plus a `GIEmitter` shader that adds light to SCGI without being drawn itself. |
| `Assets/NTC/` | Contains utilities and custom shader graphs that work with NTC. |

In the scene, the hierarchy is split into top-level groups (`Systems`, `Lighting`, `Gameplay`, `Environment`, `Audio`, `VFX`) so you can find each part quickly.

### Audio

Footsteps change with the surface underfoot, the courtyard's ambience crossfades from day to dusk to night with the Orrery, and the space sounds more enclosed the lower you are in the atrio. For details on every sound, the scripts that play them and the mixer layout, see the [Audio README](Assets/Audio/README.md).

---

## Using Atrio's assets in your projects

You're free to use any of the assets in Atrio in your own projects, including the models, textures, materials, shaders, animations, audio, VFX and scripts.
