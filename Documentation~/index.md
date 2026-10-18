# Sound System

Sound System is a reusable Unity package for playing clips through project-defined audio buses. It provides a bounded `AudioSource` pool, per-playback controls, bus volume controls in percentages or decibels, and optional local volume preferences. Your project owns its mixer, bus assets, clips, scenes, and user interface.

![Phillipe 2AM Sound System package overview](images/SoundSystemOverview.png)

## Contents

- [Requirements and installation](#requirements)
- [Basic Audio Setup sample](#try-the-sample)
- [Configure a project](#set-up-the-package)
- [Play 2D and 3D audio](#play-audio)
- [Control playback and volume](#control-playback-and-volume)
- [Pool, timing, and lifecycle behavior](#playback-behavior)
- [API reference](#api-overview)
- [Troubleshooting](#troubleshooting)
- [Performance and maintenance](#maintenance-and-performance)

## Requirements

- Unity 2023.1 or later.
- Unity's built-in Audio module.
- The optional sample also uses Unity's built-in IMGUI and Physics 3D modules.

The package declares Unity 2023.1 as its minimum. The [performance guide](performance.md) lists the exact environment and limits for its measurements; those measurements are not a substitute for checking your target platforms.

## Install

In Unity, open **Window > Package Manager**, select **+ > Add package from Git URL**, and enter:

```text
https://github.com/phillipeaam/unity-sound-system.git#main
```

For a stable project, replace `main` with a reviewed release tag or full commit hash. Unity records the resolved commit in `packages-lock.json`.

## Try the sample

The **Basic Audio Setup** sample includes a playable scene, its own mixer and bus assets, and controls for `TryPlay` calls, per-call settings, individual playback control, pool replacement, bus volume, and 3D attenuation. Its runtime `AudioListener` is attached to the gold player marker; the camera is positioned closer to the 3D field.

In Package Manager, select **Sound System** and import the sample from **Samples**. Unity places it in `Assets/Samples/Sound System/<package-version>/Basic Audio Setup`. Open `BasicAudioSetup.unity` and enter Play mode to try it.

The sample uses generated tones. `Music` and `SFX` are example bus names; your project can use its own names and UI.

## Set up the package

### 1. Create an audio mixer

Create an `AudioMixer` and add a group for each channel you need. To control a bus's volume through the package, expose that group's volume parameter and note its exact name. Each bus group must belong to this mixer.

### 2. Create bus assets

In the Project window, choose **Assets > Create > Phillipe 2AM > Sound System > Bus Definition**. Create one asset per channel and set its:

| Setting | Purpose |
|---|---|
| Stable Preference ID | Identifies saved volume for this bus. Keep it unique within the configuration and unchanged to preserve preferences. Buses with the same ID share a saved volume key. |
| Mixer Group | Routes clips to a group in the configured mixer. |
| Exposed Volume Parameter | Optional. Required if you want to change this bus's volume through the package. |
| Default Volume dB | Bus baseline used when no saved volume exists and as the 100% reference for percentage controls; values are clamped to -80 to +20 dB. |
| Min/Max Distance | Default 3D attenuation range; initially 1 and 500. Minimum must be non-negative and maximum greater than minimum. |
| Rolloff Mode | Linear or logarithmic distance falloff. Custom curves are not supported. |

Bus names and the number of buses are up to your project.

### 3. Create the configuration asset

Choose **Assets > Create > Phillipe 2AM > Sound System > Audio System Configuration**. Assign the mixer and add each bus asset once. Then review these settings:

| Setting | Default | Purpose |
|---|---:|---|
| Initial Pool Size | 4 | Sources created when the manager starts. Set to 0 for on-demand creation. |
| Maximum Pool Size | 32 | Maximum occupied playback slots. At capacity, the oldest non-Idle playback from the same bus and loop category can be replaced; otherwise the request is rejected. |
| Fade Duration | 0.5 s | Default duration for pause, resume, and stop fades. Set to 0 for immediate changes. |
| Playback Start Timeout | 5 s | How long a request can wait to start after its scheduled time. Minimum is 0.1 s. |
| Persist Volume Preferences | On | Saves bus volumes locally between sessions. |
| Preference Key Prefix | `Phillipe2AM.SoundSystem` | Default prefix used for saved volume keys. Keep it stable to retain preferences. |
| Pause Playback On Application Suspend | On | Pauses only this package's sources in the background; resumes only those paused by this setting. |

These are starting values, not requirements. Choose pool sizes and timing to suit your project.

### 4. Add the manager

Add one `AudioManager` component to a root GameObject and assign the configuration. Persistence is enabled by default, so the manager must be on a root object. Disable persistence if it should not survive scene changes.

The manager is available through `AudioManager.Instance`. It disables itself and logs an error if no configuration is assigned. Duplicate managers are destroyed.

## Play audio

Keep clip and bus references in your own components. The simplest call plays a 2D clip:

```csharp
using Phillipe2AM.SoundSystem;
using Phillipe2AM.SoundSystem.Configuration;
using UnityEngine;

public sealed class AudioCue : MonoBehaviour
{
    [SerializeField] private AudioClip _clip;
    [SerializeField] private AudioBusDefinition _bus;

    public void Play()
    {
        AudioManager manager = AudioManager.Instance;
        if (manager == null || !manager.TryPlay(_clip, _bus, out _))
        {
            Debug.LogWarning("The audio cue could not be played.", this);
        }
    }
}
```

In a component with references to the clips and buses below, choose between a fixed world position and a moving target:

```csharp
using Phillipe2AM.SoundSystem;
using Phillipe2AM.SoundSystem.Configuration;
using Phillipe2AM.SoundSystem.Playback;
using UnityEngine;

// Plays at this position; it does not follow the object.
AudioManager.Instance?.TryPlayAtPosition(_explosionClip, _sfxBus, transform.position, out _);

// vehicleTransform makes the source follow the vehicle. loop: true repeats the engine clip.
AudioPlaybackOptions loopingEngineOptions = new(loop: true);
AudioManager.Instance?.TryPlay(_engineLoop, _vehicleBus, vehicleTransform, out _, loopingEngineOptions);

// The same options apply to 2D requests.
AudioPlaybackOptions laserOptions = new(loop: true);
AudioManager.Instance?.TryPlay(_laserClip, _sfxBus, out _, laserOptions);
```

The target overload and `TryPlayAtPosition` use full 3D spatialization with the bus's distance and rolloff settings. A null target or non-finite position is rejected; spatial calls never fall back to 2D. Followed audio is not parented to the target. If the target is destroyed, playback continues at its last valid position. `TryPlay(clip, bus, out handle)` is the 2D form.

Every playback entry point is named `TryPlay` or `TryPlayAtPosition`. Each returns whether the request was accepted and an `AudioPlaybackHandle` for that playback; use `out _` when individual control is unnecessary. The handle exposes `State`, `IsValid`, `IsPlaying`, `Stop`, `Pause`, and `Resume`. It becomes invalid after the sound ends, is stopped or replaced, or its manager is destroyed.

```csharp
using Phillipe2AM.SoundSystem;
using Phillipe2AM.SoundSystem.Playback;
using UnityEngine.Audio;

AudioPlaybackOptions options = new AudioPlaybackOptions(
    loop: true,
    volume: 0.7f,
    pitch: 1.15f,
    minDistance: 2f,
    maxDistance: 35f,
    rolloffMode: AudioRolloffMode.Linear);

AudioManager manager = AudioManager.Instance;
if (manager != null && manager.TryPlay(_laserClip, _sfxBus, transform, out AudioPlaybackHandle laser, options))
{
    // Later, control just this playback.
    laser.Pause();
    laser.Resume();
    laser.Stop();
}
```

Playback is one-shot by default. Set `loop: true` in `AudioPlaybackOptions` to repeat that playback until stopped. Options are checked before the pool allocates or replaces a source. Per-call volume must be finite and between 0 and 1; pitch must be finite and between 0.1 and 3. Omitted 3D settings inherit from the bus. Spatial overrides are valid only for fixed-position and followed-target calls. Linear rolloff reaches silence at Max Distance; Logarithmic does not use Max Distance as a hard cutoff.

Each `TryPlay` method returns `false` if the manager, clip, configured bus, or pool slot is unavailable and provides a default invalid handle. Negative finite delays become zero; non-finite delays are rejected.

## Control playback and volume

The manager can stop, pause, or resume playback by bus or clip. These methods return the number of matching playback slots:

```csharp
AudioManager.Instance?.StopBus(_musicBus);
AudioManager.Instance?.PauseClip(_clip);
AudioManager.Instance?.ResumeBus(_musicBus);
```

For a user-facing slider, use bus volume percentage. `100%` keeps the bus's configured default level, `0%` silences it, and values in between reduce the level relative to that default. The percentage uses a logarithmic gain mapping; for example, `50%` is about 6 dB below the bus default. Finite inputs are clamped to 0–100, while NaN and infinity are rejected. The getter returns 0% at the mixer floor and otherwise converts the effective mixer level back to a percentage relative to the default, capped at 100%. If a saved dB value is above a newly lowered default, that saved level remains active while the getter reports 100%; setting 100% applies and saves the new default.

```csharp
AudioManager.Instance?.SetBusVolumePercent(_musicBus, slider.value);
float volumePercent = AudioManager.Instance.GetBusVolumePercent(_musicBus);
```

Bus percentage controls change the bus level. The per-call `volume` option remains a separate 0–1 multiplier for one playback. For precise mixer work, the absolute decibel API is also available; configure a slider to use a range of -80 to +20:

```csharp
AudioManager.Instance?.SetBusVolume(_musicBus, slider.value);
float volumeDb = AudioManager.Instance.GetBusVolume(_musicBus);
```

Volume changes require an exposed parameter on the bus. Both bus-volume APIs address the same exposed mixer parameter and preference. When preference saving is enabled, the applied mixer level is stored in `PlayerPrefs` using `<Preference Key Prefix>.<Stable Preference ID>.VolumeDb`. The default prefix is `Phillipe2AM.SoundSystem`; choose a different app-specific prefix if needed. Keep the bus IDs and preference prefix stable after initialization. Preferences store effective decibels, so both volume APIs restore the same level. Two buses with the same ID share a saved preference key.

## Playback behavior

- The pool reuses sources and never grows beyond **Maximum Pool Size**. At capacity, it replaces the oldest non-Idle request only when the bus and effective loop/one-shot category match. Scheduled, paused, and fading requests count as occupied. Otherwise it rejects the request and preserves other playback.
- Completed sources are cleared and reused. The package does not unload or destroy clip assets; your project owns their loading and lifetime.
- Looping clips continue until stopped with a handle, `StopBus`, or `StopClip`; an eligible pool replacement also releases them.
- Scheduling, startup timeouts, and fades use `AudioSettings.dspTime`. They continue when `Time.timeScale` is zero and pause while `AudioListener.pause` is true.
- Application suspension is separate from global audio pause. It is enabled by default, pauses only package-owned playback, preserves pending scheduled delay, and resumes only the sources it paused. Caller-paused playback stays paused, including pauses requested while the app is suspended.
- A request that fails to start is released after the configured timeout. A warning is logged for a loop or longer clip that reaches this timeout.
- Pausing and resuming active audio use the configured fade duration. Pass `fade: false` for an immediate transition. A scheduled sound pauses immediately and keeps its remaining delay; resume schedules that remainder. Reversing a pause fade resumes toward the original source gain. Stopping paused, suspended or not-yet-started playback releases its slot immediately. Pause/resume during app suspension also apply immediately because there is no audible signal to fade.
- Package volume changes use dedicated mixer parameters. Audio Mixer snapshots do not drive those parameters.
- Other `AudioSource` components in your scenes can continue to play independently of this package.

If your project removes built-in modules, enable IMGUI and Physics 3D before importing the sample. The runtime package itself requires only the Audio module.

## Runtime cost and cleanup

Set **Initial Pool Size** to the expected simultaneous count to create source objects before gameplay. Growing a pool allocates; reusing a prewarmed slot does not create a new object. Maximum Pool Size remains a hard limit. Package API calls run on Unity's main thread.

One manager-owned callback processes lifecycle, fades and spatial positions in LateUpdate, after ordinary Update movement. It disables itself when no work remains. Active 3D sounds maintain world position even while paused; established 2D loops need no recurring lifecycle work. The `SoundSystem.PlaybackPump` profiler marker isolates this work from your UI and gameplay. No per-source Update component is used.

The pool clears clip, routing and target references after playback. Removing the manager also removes its owned source objects. Consumer clips are never unloaded or destroyed. The demo creates tones, visual indicators and IMGUI controls; their allocations are separate from the package runtime. Measure CPU/native audio cost in your target Player with your own clips and voice count.

## API overview

All methods below are on `AudioManager`. Every bus must be included in the active `AudioSystemConfiguration`.

| Method | Result |
|---|---|
| `Instance` | Active manager, or `null` if none is available. |
| `TryPlay(clip, bus, out handle, options = default, delaySeconds = 0)` | `bool`; tries 2D playback with per-call settings and returns a handle. Use `out _` for fire-and-forget playback. |
| `TryPlay(clip, bus, target, out handle, options = default, delaySeconds = 0)` | `bool`; tries followed 3D playback with per-call settings and returns a handle. |
| `TryPlayAtPosition(clip, bus, position, out handle, options = default, delaySeconds = 0)` | `bool`; tries fixed-position 3D playback with per-call settings and returns a handle. |
| `StopBus(bus, fade = true)` / `StopClip(clip, fade = true)` | Number of playback slots stopped. |
| `PauseBus(bus, fade = true)` / `PauseClip(clip, fade = true)` | Number of playback slots paused. |
| `ResumeBus(bus, fade = true)` / `ResumeClip(clip, fade = true)` | Number of paused playback slots resumed. |
| `IsBusPlaying(bus)` / `IsClipPlaying(clip)` | Whether matching audio is scheduled or playing; paused audio returns `false`. |
| `SetBusVolume(bus, decibels)` | `bool`; applies a finite value clamped to -80 to +20 dB. Requires an exposed volume parameter. |
| `GetBusVolume(bus)` | Current volume in dB; uses the bus default if unreadable, or -80 dB for an invalid bus. |
| `SetBusVolumePercent(bus, percent)` | `bool`; clamps finite values to 0–100 and applies them relative to the bus default. 0% silences the bus; 100% restores its configured default. Non-finite input is rejected. |
| `GetBusVolumePercent(bus)` | Current bus level as 0–100% relative to its configured default, clamped to that range. |

The manager owns pooled `AudioSource` components. Do not retain or modify them directly.

### Other public types

| Type | Public members | What they provide |
|---|---|---|
| `AudioManager` | `Instance`, `Configuration` | Access the active manager and its assigned settings. |
| `AudioSystemConfiguration` | `Mixer`, `InitialPoolSize`, `MaximumPoolSize`, `FadeDurationSeconds`, `PlaybackStartTimeoutSeconds`, `PersistVolumePreferences`, `PausePlaybackOnApplicationSuspend`, `PreferenceKeyPrefix`, `BusCount`, `TryGetBusAt`, `ContainsBus` | Inspect the mixer, registered buses, pool and lifecycle settings. |
| `AudioBusDefinition` | `MixerGroup`, `ExposedVolumeParameter`, `DefaultVolumeDb`, `MinDistance`, `MaxDistance`, `RolloffMode` | Inspect a bus's routing, volume parameter, default level, and 3D settings. Keep preference IDs unique in the active configuration. |
| `AudioPlaybackOptions` | Constructor parameters `loop`, `volume`, `pitch`, `minDistance`, `maxDistance`, `rolloffMode` | Set per-playback loop, gain, pitch, and optional spatial overrides. Defaults are one-shot, volume 1, pitch 1, and bus 3D settings. Spatial overrides apply only to fixed-position/followed playback. |
| `AudioPlaybackHandle` | `State`, `IsValid`, `IsPlaying`, `Stop`, `Pause`, `Resume` | Inspect or control one playback generation without exposing its pooled `AudioSource`. |
| `AudioPlaybackState` | `Invalid`, `Scheduled`, `Starting`, `Playing`, `Paused`, `FadingToPause`, `FadingToResume`, `FadingToStop` | Identify the current lifecycle state of a handle. |

`AudioSystemConfiguration` and `AudioBusDefinition` are configuration assets. `AudioPlaybackOptions` is passed into a play request; its values are not editable afterward. A paused playback is not counted by `IsBusPlaying` or `IsClipPlaying`.

## Troubleshooting

| Problem | Check |
|---|---|
| `AudioManager.Instance` is `null` | Confirm one active manager has an `AudioSystemConfiguration` assigned. Check the Console for errors. |
| `TryPlay` returns `false` | Check the clip and bus references, confirm the bus is in the configuration, and check whether the pool is full. |
| A bus has no sound or volume control | Confirm its mixer group belongs to the configured mixer. Volume control also requires a valid exposed parameter. |
| Volume resets after restart | Enable preference saving and keep the preference prefix and bus ID unchanged. |
| A loop keeps playing | Stop it with `StopBus` or `StopClip`; loops do not end automatically. |
| Audio stops after deactivating the manager object | Keep the `AudioManager` GameObject active while playback is expected. Deactivating it also disables the pooled `AudioSource` objects; use a handle, `StopBus`, or `StopClip` to control playback. |

Before shipping your game, verify mixer routing, volume changes, suspension/resume, and audible output on each target platform. For 3D audio, check distance falloff and moving targets in a Player build.

## License

MIT. See [`LICENSE.md`](../LICENSE.md).

If you happen to mention Sound System in your credits or documentation, that would be lovely, but it is entirely optional.

## Unity references

- [Package manifest and samples](https://docs.unity.com/en-us/engine/6000.5/manual/packages-list/cus-pkg-lp/cus-pkg-development/cus-samples)
- [AudioSource.spatialBlend](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/AudioSource-spatialBlend.html)
- [AudioSettings.dspTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings-dspTime.html)
- [AudioListener.pause](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioListener-pause.html)

## Maintenance and performance

- [Maintainer guide](maintenance.md): ownership, lifecycle invariants and safe changes.
- [Performance guide](performance.md): Player measurements, pool sizing and what the numbers cover.
