# Changelog

## [Unreleased]

## [1.0.0] - 2026-10-18

### Added

- Consumer-defined `AudioSystemConfiguration` and `AudioBusDefinition` assets for mixer routing, buses, and 3D spatial defaults.
- A bounded `AudioSource` pool with playback acceptance results and per-bus volume controls, including optional saved volume preferences.
- User-friendly 0–100% bus volume controls relative to each bus's configured default, alongside absolute decibel controls.
- A package-specific default prefix for saved bus-volume preferences to reduce collisions with other `PlayerPrefs` keys.
- Playback controls by handle, bus, or clip, including stop, pause, resume, and optional fades. All playback entry points use `TryPlay`/`TryPlayAtPosition` and return an acceptance result plus a handle; pass `out _` when individual control is unnecessary.
- One-shot playback by default, with an explicit per-call loop option plus per-playback volume, pitch, and 3D distance/rolloff settings.
- 3D playback at a fixed position or following a `Transform`. Followed playback finishes at its last position if the target is destroyed; existing calls without a spatial target remain 2D.
- Delayed playback with remaining-delay preservation across caller/app pauses, plus a configurable startup timeout.
- A **Basic Audio Setup** sample with a runnable scene, mixer configuration, and manual controls for `TryPlay` calls, per-call settings, individual handles, pool replacement, percentage/decibel bus volumes, scheduling, bulk controls, target destruction, and 3D audio. The sample is isolated until imported through Package Manager.
- Pool-capacity handling that replaces the oldest active playback only when the new request shares its bus and loop/one-shot category; unrelated requests are rejected while current playback is preserved.
- Application-suspension handling that pauses package-owned playback by default and resumes only playback paused by that policy, preserving scheduled delays and caller-controlled pauses.
- DSP-clock scheduling and fades that continue when `Time.timeScale` is zero and pause with `AudioListener.pause`.
- A shared playback callback that disables itself while idle, allocation-free warmed runtime paths, and deterministic cleanup of package-owned source objects.
- Playback continuity across desktop window-focus changes, including handle queries made before `AudioManager` receives its pause callback.
