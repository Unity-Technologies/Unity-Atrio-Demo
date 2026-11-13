# Atrio Audio

This is a reference for every sound in Atrio: what triggers it, which script plays it, where it lives in the project, and how it's routed through the mixer.

Paths are relative to the project folder (`courtyard_unity_project/`). The audio scripts are in `Assets/Scripts/Audio`, the sounds, mixer and audio prefabs are in this folder (`Assets/Audio`), and the scene is `Assets/Scenes/Atrio.unity`. Most gameplay audio is set up in `Assets/FoundationalPrefabs/Gameplay.prefab`; the ambience, emitters and mixer control are in `Assets/Audio/Audio.prefab`.

---

## Overview

| Sound | Triggered by | Code | Lives on |
|---|---|---|---|
| Light switch click | Light Switch interaction step, 1.6 s in | `LightSwitchAudio.LightSwitchOn` / `LightSwitchOff` | `LightSwitchAudio.prefab`, nested in `LightSwitch_Interactable.prefab` (×7 in `Gameplay.prefab`) |
| Lamp buzz | Light switch on, after 0.1 s | `LightSwitchAudio.LightSwitchOn` | AudioSource on `WallLamp.prefab` (linked per switch in the scene) |
| Footsteps and landings | Distance walked / time in the air | `FootstepAudio.Update` | `FirstPersonPlayer.prefab` → Visual → Footsteps |
| Entrance door | Animation event | `DoorAudio.DoorOpenAudioEvent` | Entrance door in `Gameplay.prefab` |
| Orrery button | Animation event | `OrreryPressButtonAudio.AudioOrreryPressButton` | `Gameplay.prefab` → Interactions → Orrery → Interaction |
| Flashlight on / off | `ToggleTorchLight.LightStateChanged` | `TorchAudio.OnLightStateChanged` | `Torch.prefab` root |
| Bird wing flaps | Animation events on the Flapping clip | `BirdAnimationAudio.BirdFlapAudio` | `Boid.prefab` root (every bird in the flock) |
| Cat meow | Animation event and a random timer | `CatAnimationAudio.CatMeowAudio`, `AudioEmitter` | `Cat.prefab` / nested `CatAudio.prefab` |
| Cat purr | Play On Awake, looping | `CatAudio.Update` (ducks it under meows) | `CatAudio.prefab` → CatPurrr |
| Louder purr while petted | Animation events | `CatAnimationAudio.CatFussedAudioStart` / `Stop` | `Cat.prefab` root |
| Day, dusk and night ambience | Play On Awake, looping; mixer volume follows the time of day | `TimeOfDay.Update` | `Audio.prefab` → TimeOfDayManager |
| Birdsong and dog barks | Random bursts from random positions | `AudioEmitter.Update` | `Audio.prefab` → BirdEmitter 1–4, DogEmitter |
| Atrio reverb and filters | Camera height inside the atrio | `Reverb.Update` | `Audio.prefab` → ReverbManager |
| Master fade-in | Scene start, once the ambience is streaming | `MasterVolumeFade.Update` | `Audio.prefab` |

---

## Interactions

### Light switches and lamp buzz

Each light switch clicks when the player's hand reaches it, and the wall lamp it controls starts to buzz a moment later.

| | |
|---|---|
| **Script** | `Assets/Scripts/Audio/LightSwitchAudio.cs`: `LightSwitchOn()`, `LightSwitchOff()` |
| **Called from** | The **Light Switch** step in the `Interactable_Switch` sequence of `Assets/FoundationalPrefabs/LightSwitch_Interactable.prefab`. The step (`Assets/Scripts/BuildingBlocks_InteractableSteps/LightSwitchStep.cs`) raises its **On Switch On** or **On Switch Off** event after **Switch Delay** (1.6 s), which lines the click up with the arm animation. |
| **Lives on** | `Assets/Audio/LightSwitchAudio.prefab`, nested inside `LightSwitch_Interactable.prefab`, with `LightSwitchAudio` and an AudioSource. There are seven switches, `LightSwitch_01`–`07`, under `LightSwitches` in `Gameplay.prefab`. |
| **Plays** | **Click:** `Assets/Audio/LightSwitch/LightSwitchOnOff.asset` on the switch's own AudioSource, at `LightSwitchVolume` (0.1).<br>**Buzz:** the AudioSource on the linked lamp (`Assets/Environment/ElectricalLighting/Prefabs/WallLamp.prefab`, resource `LightOnBuzz.asset`), at `LightBulbVolume` (0.2). It starts `LightBulbDelay` (0.1 s) after the click and stops when the switch is turned off. |
| **Mixer** | Atrio Dry (both) |

> **Note:** Each switch's `LightGameObject` (the lamp that buzzes) is set as a scene override in `Atrio.unity`. The lamps (`WallLamp_001`–`007`) are scene objects, so the link can't be stored in `Gameplay.prefab`.

### Entrance door

The entrance door creaks as it swings open.

| | |
|---|---|
| **Script** | `Assets/Scripts/Audio/DoorAudio.cs`: `DoorOpenAudioEvent()` |
| **Called from** | Animation event `DoorOpenAudioEvent` on the **DoorOpen** clip in `Assets/Animations/EntranceDoor.fbx`, at normalized time **0.280** (about frame 98 of 0–350). Edit it in the FBX's import settings, under **Animation > Events**. |
| **Lives on** | The `EntranceDoor` prefab instance in `Gameplay.prefab`. `DoorAudio` and its AudioSource are added to that instance rather than to `EntranceDoor.prefab` itself, on the same GameObject as the Animator. |
| **Plays** | `Assets/Audio/Door/DoorOpen.asset` (volume 0.2) |
| **Mixer** | Atrio Dry |

### Orrery button press

The Orrery's button clicks when the player's finger presses it.

| | |
|---|---|
| **Script** | `Assets/Scripts/Audio/OrreryPressButtonAudio.cs`: `AudioOrreryPressButton()` |
| **Called from** | Animation event `AudioOrreryPressButton` on the **OrreryActivate** clip in `Assets/Animations/OrreryActivate.fbx`, at normalized time **0.482** (about frame 159 of 0–330). |
| **Lives on** | `Gameplay.prefab` → Interactions → Orrery → **Interaction**, which has the receiver and the AudioSource. A second receiver, with no AudioSource assigned, sits on the first-person player, because both Animators play this clip and Unity logs an error if an event has no receiver. |
| **Plays** | `Assets/Audio/Orrery/Orrery.wav` (volume 0.1) |
| **Mixer** | Atrio Dry |

### Flashlight on / off

The flashlight clicks whenever its light actually turns on or off.

| | |
|---|---|
| **Script** | `Assets/Scripts/Audio/TorchAudio.cs`: `OnLightStateChanged()` |
| **Triggered by** | `ToggleTorchLight.SetLightState()` (`Assets/Scripts/ToggleTorchLight.cs`), which raises `LightStateChanged` only when the state changes. In `Gameplay.prefab`, `Interactable_equip_torch` turns the light on in its **Turn Light On** group (after the grab animation), `Interactable_reset_torch` turns it off, and `Interactable_turn_on_off` toggles it. Putting the flashlight away while it's already off plays nothing. |
| **Lives on** | The root of `Assets/Environment/Torch/Prefabs/Torch.prefab`, with its own AudioSource. It finds `ToggleTorchLight` in its children (added to the flashlight instance in `Gameplay.prefab`). The sound follows the flashlight, so it comes from the player's hand while held. |
| **Plays** | `Assets/Audio/Torch/TorchOnOff.wav` (volume 0.534, max distance 4 m) |
| **Mixer** | Master |

---

## Footsteps and landings

The first-person player has no animated feet, so footsteps are driven by distance travelled rather than by animation events.

| | |
|---|---|
| **Script** | `Assets/Scripts/Audio/FootstepAudio.cs`: `Update()`, `FindSurface()`, `Play()` |
| **Triggered by** | Each frame it reads the position and velocity of the parent `KinematicMover`. While grounded and moving at `MinSpeed` (0.5 m/s) or faster, it adds up the distance travelled, height included so stairs count. Every `StrideLength` (0.7 m) × the surface's `StrideMultiplier` it plays a step. Touching down after at least `MinAirTimeForLand` (0.3 s) in the air plays a landing. |
| **Surface lookup** | A short raycast down from the feet (`GroundCheckDistance`, 0.5 m). If the collider it hits, or one of its parents, has a `FootstepSurface` component (`Assets/Scripts/Audio/FootstepSurface.cs`), that surface type is used; anything else uses `DefaultSurfaceType` (Stone). Results are cached per collider. |
| **Lives on** | `Assets/Character/FirstPersonArms/Prefabs/FirstPersonPlayer.prefab` → Visual → **Footsteps**. It has two AudioSources, and steps alternate between them so one step's tail isn't cut off by the next. |
| **Volume** | The surface's `Volume` × `FootstepMasterVolume` (0.8 in `FirstPersonPlayer.prefab`, overridden to 0.7 in `Gameplay.prefab`). |
| **Mixer** | Atrio Dry |

Each surface type (`Assets/Scripts/Audio/FootstepSurfaceType.cs`) sets its step and landing sounds, its volume and its stride multiplier:

| Surface | Asset | Sounds | Volume | Stride multiplier | Used on |
|---|---|---|---|---|---|
| Stone | `Footsteps/FootstepSurface_Stone.asset` | `Footstep_Stone.asset` (10 variations) | 0.085 | 3.25 | Everything that isn't tagged |
| Wood | `Footsteps/FootstepSurface_Wood.asset` | `Footstep_Wood.asset` (10 variations) | 0.112 | 1.25 | `Assets/Environment/Stairs/Prefabs/Staircase_002.prefab` |
| Water | `Footsteps/FootstepSurface_Water.asset` | `Footstep_Water.asset` (10 variations) | 0.1 | 3.25 | `Assets/Environment/WaterPlane/WaterPlane.prefab` |

> **Tip:** To find floors that need tagging, tick **Log Surfaces** on the `FootstepAudio` component. Each step then logs the collider it hit and draws the ray in the Scene view.

---

## Creatures

### Bird wing flaps

Every bird in the flock plays its own wing flaps, in time with its animation.

| | |
|---|---|
| **Script** | `Assets/Scripts/Audio/BirdAnimationAudio.cs`: `BirdFlapAudio()` |
| **Called from** | Ten `BirdFlapAudio` animation events, about every 0.1 of normalized time, on the **Flapping** clip in `Assets/Character/Bird/Animation/Bird_Flapping.fbx`. |
| **Lives on** | The root of `Assets/Character/Bird/Prefabs/Boid.prefab`, next to the Animator. |
| **Plays** | `Assets/Audio/BirdWingFlap/BirdWingFlap.asset` (volume 0.068, max distance 3 m, pitch randomized between 0.8 and 1.0) |
| **Mixer** | Master |

### Cat meows

Cats meow both from their idle animation and at random.

| | |
|---|---|
| **From animation** | Animation event `CatMeowAudio` on the **Standing_Idle** clip in `Assets/Animations/Cat_StandingIdle.fbx`, at normalized time **0.465** (about frame 372 of 0–800), calls `CatAnimationAudio.CatMeowAudio()` (`Assets/Scripts/Audio/CatAnimationAudio.cs`) on the root of `Assets/Character/Cat/Prefabs/Cat.prefab`. Only cats playing the standing idle meow this way; the laying, sitting and rail idles have no event. |
| **At random** | An `AudioEmitter` on `CatAudio.prefab` → **CatMeow** plays one meow every 5–10 s. |
| **Plays** | `Assets/Audio/Cat/CatMeow.asset` (15 variations, pitch randomized between 0.8 and 1.2), through the CatMeow AudioSource in `Assets/Audio/CatAudio.prefab`, which is nested in `Cat.prefab`. |
| **Mixer** | Atrio Dry |

### Cat purring

Every cat purrs constantly, goes quiet while it meows, and purrs louder while you pet it.

| | |
|---|---|
| **Script** | `Assets/Scripts/Audio/CatAudio.cs`: `Update()`, `SetFussed()` |
| **Lives on** | `Assets/Audio/CatAudio.prefab` → **CatPurrr**, playing `Assets/Audio/Cat/CatPurr.wav` on a loop from Play On Awake. `CatAudio.Update()` mutes the purr while a meow is playing. |
| **Petting** | Animation events `CatFussedAudioStart` (normalized time **0.455**) and `CatFussedAudioStop` (**0.812**) on the **Pet** clip in `Assets/Animations/PetCat.fbx` call `CatAnimationAudio`, which calls `CatAudio.SetFussed()` on that cat. Events only reach the Animator playing the clip, so only the cat being petted gets louder. An empty receiver (`Assets/Scripts/Audio/PlayerArmsAnimationEvents.cs`) sits on the first-person player, which plays the same clip. |
| **Levels** | Fades between `PurrVolume` (0.3) and `FussedPurrVolume` (0.6) over `FussedFadeTime` (0.3 s). `PurrMasterVolume` (1) scales both. |
| **Mixer** | Atrio Dry |

---

## Ambience

### Time-of-day ambience

Day, dusk and night ambience loops crossfade as the time of day changes.

| | |
|---|---|
| **Script** | `Assets/Scripts/Audio/TimeOfDay.cs`: `Update()`, `SetGroupVolume()` |
| **How it works** | All the loops play all the time. Each frame, `TimeOfDay` reads the hour from `SkyLightController` (`Assets/Scripts/SkyLightController.cs`) and samples `Assets/Audio/DayCycleAmbience.anim` at that hour to get `Day`, `Dusk` and `Night` weights. It scales them (Day ×0.6, Dusk ×0.6, Night ×1), converts them to dB and writes them to the exposed mixer parameters `Day_Volume`, `Dusk_Volume` and `Night_Volume`. |
| **Lives on** | `Assets/Audio/Audio.prefab` → **TimeOfDayManager**. Its `SkylightController` reference is a scene override. |
| **Sources** | `Audio.prefab` → AmbientAudioObject1–4, each with a Day, DawnDusk and Night AudioSource:<br>**Day:** `DayAmbience.wav`, `CityTraffic_001`–`003.wav` → Ambience_Day_TimeOfDay<br>**Dusk:** `Dusk_001.wav`, `Dusk_002.wav` → Ambience_Dusk_TimeOfDay<br>**Night:** `Night.wav` → Ambience_Night_TimeOfDay<br>All are in `Assets/Audio/Ambience`. |

> **Note:** `AmbientAudioObject` (`Assets/Scripts/Audio/AmbientAudioObject.cs`) only holds data and doesn't play anything.

### Birdsong and dog barks

Birds and dogs call out in short bursts from positions that move around the courtyard.

| | |
|---|---|
| **Scripts** | `Assets/Scripts/Audio/AudioObject.cs` ties the parts together and moves the emitter each frame.<br>`Assets/Scripts/Audio/AudioEmitter.cs` plays the bursts and sets the mixer group.<br>`Assets/Scripts/Audio/AudioEmitterAngleDistance.cs` moves the emitter to a new random angle and distance around `Origin` after each burst. |
| **How it works** | `AudioEmitter` picks a number of sounds (`TriggerSampleCountMinMax`), a gap between them and a pitch, then plays the burst. After `MinMaxDelayBetweenEmitterReset` it picks again and the emitter moves to a new position. The count is a random integer that excludes the maximum, so a range of 1–4 plays 1–3 sounds. |
| **Birds** | `Audio.prefab` → BirdEmitter, BirdEmitter 2, 3 and 4, playing `Assets/Audio/Birds/Bird_001`–`003.asset`: 1–3 chirps, 1–2 s apart, pitch 0.9–1.2, a new burst every 2–8 s, 5–30 m from (20, 24, −10). Mixer: Birds_Day. |
| **Dogs** | `Audio.prefab` → DogEmitter, playing `Assets/Audio/Dog/DogBark.asset`: 1–3 barks, 0.25–2 s apart, pitch 0.9–1.2, a new burst every 4–10 s, 30–100 m away, volume 0.25. Mixer: Dogs. |

### Atrio reverb and filters

The courtyard sounds more enclosed and the outside world more muffled the lower you are in the atrio.

| | |
|---|---|
| **Script** | `Assets/Scripts/Audio/Reverb.cs`: `Update()` |
| **Lives on** | `Assets/Audio/Audio.prefab` → **ReverbManager**, together with the BoxCollider that defines the atrio. |
| **How it works** | It tracks the camera tagged `MainCamera`. Inside the box, the camera's height from Y = 0 up to the top of the box sets the mixer parameters below. The reverb send fades linearly; the filter cutoffs follow an eased (t⁴) curve and are smoothed over time. |

| Parameter | At ground level → top of the atrio | Outside the atrio |
|---|---|---|
| `AtrioReverbSendLevel` | 0 dB → −80 dB | −80 dB |
| `AtrioAmbienceLowPassFilter` | 3000 → 6000 Hz | 6000 Hz |
| `AtrioAmbienceHighPassFilter` | 300 → 100 Hz | 100 Hz |
| `InsideAtrio_DayAmbienceLPF` | 300 → 22000 Hz (muffles the day ambience down in the atrio) | 22000 Hz |
| `Dogs_LowPassCutoff` | 0 → 22000 Hz (not smoothed) | 22000 Hz |

### Master volume fade-in

All audio fades in when the scene starts, instead of popping in.

| | |
|---|---|
| **Script** | `Assets/Scripts/Audio/MasterVolumeFade.cs`: `Awake()`, `Update()` |
| **Lives on** | `Assets/Audio/Audio.prefab` |
| **How it works** | Holds the exposed `MasterVolume` parameter at −80 dB from `Awake` (it runs before other scripts). Once the streamed day ambience has started playing, it fades up to the level set in the mixer over `fadeDuration` (3 s), using unscaled time. |

---

## Mixer layout

The mixer is `Assets/Audio/Atrio.mixer`.

```
Master                        ← flashlight, bird wing flaps
└── MasterVolume              ← faded in by MasterVolumeFade
    ├── Atrio Wet
    └── Atrio Dry             ← light switches, lamps, footsteps, door, Orrery, cat meows and purrs
        └── Ambience_Master
            ├── Reverb
            ├── Dogs
            ├── Ambience_Dusk_Master
            │   └── Ambience_Dusk_TimeOfDay
            ├── Ambience_Night_TimeOfDay
            │   └── JetPlane
            └── Day_TimeOfDay
                ├── Birds_Day
                └── Ambience_Day_Master
                    ├── Ambience_Day_TimeOfDay
                    └── Plane
```

| Exposed parameter | Set by |
|---|---|
| `MasterVolume` | `MasterVolumeFade` |
| `Day_Volume`, `Dusk_Volume`, `Night_Volume` | `TimeOfDay` |
| `AtrioReverbSendLevel`, `AtrioAmbienceLowPassFilter`, `AtrioAmbienceHighPassFilter`, `InsideAtrio_DayAmbienceLPF`, `Dogs_LowPassCutoff` | `Reverb` |

> **Note:** Some group levels are set to +12 dB on purpose, because the source samples are quiet.

---

## Working with animation events

Animation events only reach scripts on the **same GameObject as the Animator** playing the clip. When a clip is reused by another Animator (for example, the first-person player also plays the Orrery and cat-petting clips), give that Animator a receiver too, even an empty one, or Unity logs a "has no receiver" error.

---

## Known issues

- **BirdEmitter 4 is silent.** Its `AudioEmitter` has no **Audio Content** assigned, so it never plays. Assign `Assets/Audio/Birds/Bird_004/Bird_004.asset` to restore the fourth bird.
