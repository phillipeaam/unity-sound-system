# Maintaining Sound System

The package has one public facade and a few internal owners. Keep changes close to the rule they affect.

| Area | Responsibility |
|---|---|
| `AudioManager` | Public API, configuration, singleton and Unity application lifecycle. |
| `Configuration/` | Project-authored buses, numeric bounds and routing validation. |
| `AudioSourcePool` | Slot capacity, admission/replacement, handle generations and group control. |
| `AudioSourcePlayback` | One source's schedule, pause causes, fades, position and release. |
| `AudioPlaybackPump` | One shared LateUpdate, enabled only while work is pending. |
| `AudioVolumeController` | Mixer volume, deferred initialization and effective-dB preferences. |
| `Samples~/BasicAudioSetup` | Educational UI, generated tones and temporary visuals; optional consumer content. |
| `Tests/` | Behavior-focused fixtures and tests; no runtime dependency. |

## Lifecycle rules

- Every accepted request gets a new slot generation. An ended handle never controls a later request.
- Caller pause and application suspension are independent. Returning to the app must preserve a caller pause.
- All playback deadlines and fades use DSP time. Global listener pause freezes that clock. Source-level pauses require rebasing; overlapping causes must not count paused time twice.
- A held schedule retains its remaining delay. It restarts only when both pause causes allow it.
- Requested gain stays separate from the current fade gain. Reversing a fade preserves continuity and its original target volume.
- The manager owns source objects. A follow target never owns them. Released sources clear clip/routing/target references; clips remain consumer-owned.
- Admission may replace only the oldest occupied slot on the same bus with the same loop setting. Validate a request before replacing anything.

## Changing a rule

1. Identify its owner above and read the corresponding public XML documentation.
2. Add a focused regression to the matching test class before changing behavior. Keep private reflected member names in `Tests/Runtime/Reflection`.
3. Make the smallest cohesive change. Share repeated policy; add an interface or dependency only when a concrete second use needs it.
4. Run EditMode and PlayMode tests, then check the imported sample if its behavior or serialized data changed.
5. Preserve `.meta` GUIDs and serialized field names. Document intentional API/serialization changes and update the manual/changelog together.
6. Repeat a Player capture when changing admission, tracking, fades, allocation or recurring work. Avoid closures, LINQ, formatting, growing collections and object creation inside hot paths.

The test fixture stages its mixer Resources only while running/building tests. Ordinary Players must not contain the test mixers. Test-only classes and benchmark tools must never become package runtime dependencies.

See [performance](performance.md) for measurement scope and pool sizing. The workbench keeps feature specifications, benchmark sources and raw-result summaries outside the distributable package.
