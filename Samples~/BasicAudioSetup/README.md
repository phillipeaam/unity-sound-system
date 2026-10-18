# Basic Audio Setup

Import this sample from **Sound System > Samples** in Package Manager, open `BasicAudioSetup.unity`, and enter Play. Its mixer, bus assets and configuration live in `Audio/`. All tones are generated locally; no external audio or UI package is required. The optional demo uses Unity's built-in **IMGUI** and **Physics 3D** modules, enabled in standard templates. If your project strips built-in modules, enable these before importing the sample. The runtime package itself only depends on Audio.

The gold capsule carries the only AudioListener. Cyan is the moving target, green marks a followed sound, and magenta marks a fixed world position. Use headphones. **Hide** makes the field visible on small viewports; **Stop all** cleans up sample playback and indicators.

## Try the public API

| Section | Action | Expected result / API |
|---|---|---|
| Setup | Inspect the displayed configuration; select the manager and Audio/ assets in the Inspector | Mixer → bus definitions → AudioSystemConfiguration → AudioManager. Runtime access uses `Instance` and `Configuration`. |
| Play | Choose loop, source volume, pitch and delay; play Music or SFX | `TryPlay` starts 2D audio and returns acceptance plus a handle. Loop defaults to false; the source gain is a 0–1 multiplier. |
| Play | Use a delay, then pause from Controls before it starts | Handle reports Paused. Resume waits for the remaining delay rather than the original deadline. |
| Play | Follow the target, then play a fixed pulse | Transform `TryPlay` follows; `TryPlayAtPosition` stays at the captured position. Green moves; magenta stays. |
| Play | Enable 3D overrides, change min/max distance and rolloff; disable overrides | Only the next 3D call changes. Without overrides, SFX bus defaults apply. Linear reaches silence at max distance; Logarithmic does not use it as a hard cutoff. |
| Play | Start a followed loop, then destroy the target | Sound/green indicator stay at the last position until stopped. Restore target creates a new target for future calls. |
| Controls | Start two matching loops; pause, resume or stop the selected handle; swap handles | `State`, `IsValid`, `IsPlaying` and handle controls refer only to one instance. An ended/replaced handle remains Invalid. |
| Controls | Choose bus or clip; use Pause/Resume/Stop with or without fade | `PauseBus`, `ResumeBus`, `StopBus`, `PauseClip`, `ResumeClip`, `StopClip` show affected counts. `IsBusPlaying`/`IsClipPlaying` exclude paused sounds. |
| Volume | Move independent 0–100% sliders; reset both | `SetBusVolumePercent` / `GetBusVolumePercent`: 0% mutes, 100% applies each bus default; 50% is about 6 dB below it. |
| Volume | Move absolute dB sliders | `SetBusVolume` / `GetBusVolume` address the same mixer parameter. Above-default dB can read as 100%; reset applies the configured default. |
| Pool | Replace oldest | Starts max + 1 quiet SFX loops. First handle becomes Invalid; newest remains valid. No extra source beyond capacity. |
| Pool | Reject a different bus or loop category | Fills with SFX loops, rejects Music and a one-shot SFX, and preserves current loops. One capacity warning is expected. |

For fire-and-forget calls use `out _`. Per-call options affect future requests; bus-volume changes affect all playback routed through that bus. Finite negative delays start immediately. A rejected request returns false and an invalid handle; no substitute audio is played.

## Application suspension and persistence

The sample configuration disables preference persistence so each session starts at its bus defaults. Enable it on the configuration to try saved effective dB values; keep stable bus IDs and the preference prefix unchanged. On a target Player, start a delayed sound and a loop, pause another manually, background the app and return: automatic pauses resume, manual pauses stay paused, and remaining delays are preserved. The Editor toolbar Pause does not simulate application suspension.

## Files and ownership

The `Custom UI` folder keeps the sample-only interface code and shader together, separate from the scene and audio configuration. `BasicAudioSampleControls.cs` owns startup/cleanup; `.Playback.cs` contains public API examples; `.UI.cs` draws the sections; `.Visuals.cs` owns generated tones and markers. The shader is included through the scene reference. Temporary rings are destroyed after their handles become Invalid. This educational IMGUI interface and its visuals allocate independently of the package's warmed runtime, so exclude them when profiling the audio system.

Sample sources live in the package's `Samples~/BasicAudioSetup` folder and compile only after import into `Assets/Samples`. The package README contains the animated preview and links to the full setup/API manual. Replace the example buses, mixer and tones with assets owned by your project.
