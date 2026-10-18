# Performance and pool sizing

These measurements describe one native Player workload. Use them to understand costs, then profile your own clips and target devices.

## Measured environment

Unity 6000.6.0f1, macOS Development Player, Mono, Apple M4 Max, 48 kHz stereo and a 512-sample DSP buffer. VSync off, 120 fps cap, 640×360 window; no Deep Profiling or sample UI. Each scenario warms for 120 frames and captures 600 frames, repeated in three fresh Players. Values below are medians of the three run means/p95s; the largest sample is retained across runs. Profiler and host activity are included.

The workload uses loops with a nonzero signal and source gain, generated 2-second mono PCM clips (48 kHz, float sample data) and up to 100 moving targets. It does not measure compressed/streamed music, browser audio or mobile behavior.

## Pool slots and real voices

A pool limit controls source ownership. Unity's real-voice limit controls how many voices are mixed at once. See the [Audio Profiler documentation](https://docs.unity3d.com/6000.0/Documentation/Manual/ProfilerAudio.html).

| Native voice budget | Active sources | Observed real | Observed virtual |
|---|---:|---:|---:|
| 32 real / 512 virtual | 100 | 32 | 68 |
| 128 real / 512 virtual | 100 | 100 | 0 |

Both cases accepted 100 requests and kept exactly 100 source objects. After stopping, every clip reference was cleared and the idle pump stopped. This establishes this workload on this machine, not a platform-wide promise of 100 music tracks.

## Package CPU

The table isolates the shared `SoundSystem.PlaybackPump` main-thread marker with 100 real voices permitted. It excludes audio-engine mixing, rendering, demo UI and target movement performed by the benchmark.

| Workload | Slots | Pump mean / frame | Pump p95 | Largest sample |
|---|---:|---:|---:|---:|
| 2D | 0 | 0.0000 ms | 0.0000 ms | 0.0000 ms |
| 2D | 100 | 0.0000 ms | 0.0000 ms | 0.0000 ms |
| moving 3D | 1 | 0.0067 ms | 0.0110 ms | 0.1335 ms |
| moving 3D | 32 | 0.0225 ms | 0.0442 ms | 0.2318 ms |
| fixed 3D | 100 | 0.0343 ms | 0.0602 ms | 0.8725 ms |
| moving 3D | 100 | 0.0568 ms | 0.0961 ms | 0.5084 ms |
| 100 moving 3D / active fades | 100 | 0.0811 ms | 0.1384 ms | 0.7172 ms |

A started 2D loop needs no recurring package callback; the native audio engine still works. One-shots need completion checks. Positional playback maintains world positions, and fades add gain updates. There is one shared callback; no per-source Update.

For the 100-follow capture, Unity's native audio-engine marker `Audio.FMODSystemUpdate` averaged 0.0946 ms with p95 0.1651 ms. FMOD is the product name of the audio middleware/runtime Unity uses internally; Unity's documentation does not expand it as an acronym. This marker is main-thread engine work, separate from the package pump. The recorded `MasterDSP` marker covers one DSP stage, not total audio-thread utilization. Whole-Player CPU is retained in the workbench report; frame times include the 120 fps wait and must not be presented as package CPU.

## Allocations and memory

After warmup, measured playback/replacement, handle controls, bus/clip controls and volume operations produced **zero managed allocation events**. Raw capture descendants also recorded **zero bytes** in the pump and measured API blocks. A known-allocation probe verified the recorder. Setup, pool creation/growth, diagnostics, clips and educational UI can allocate.

| Content / native objects | Measured size |
|---|---:|
| 100 AudioSource components | 124.6 KiB |
| 100 sources + their GameObjects and Transforms | 161.3 KiB |
| One shared 2-second mono PCM clip | 375.8 KiB |
| 100 distinct 2-second mono PCM clips | 36.68 MiB |

Native object sizes exclude managed slot state, container storage and the rest of Unity. Clip sizes include native clip storage; process/audio memory counters include other engine allocations. These columns are not interchangeable or safe to sum into a whole-application budget. A 100-slot pool does not duplicate a shared clip. Longer clips, channels, sample rates, codecs and load types change content costs.

Creating a new 100-slot manager later in the run took a median 2.26 ms and recorded 614 managed allocation events. Earlier scenarios had already exercised engine/type initialization. This measures slot prewarming, not first-boot cost or steady playback. Prefer prewarming during loading when gameplay allocations would be disruptive.

## Choosing a capacity

- Start with the expected simultaneous cues, not the total number of clips in the project.
- Set Initial Pool Size to prewarm that working set; Maximum Pool Size is the hard cap.
- Remember the same-bus/loop replacement policy and test bursts at capacity.
- Configure Unity's voice limits for the intended audible workload; a larger pool alone cannot increase real voices.
- Profile actual music/SFX imports, mixer effects, distances and target devices. Keep content memory separate from source-management overhead.

Before/after captures use the same 512-sample buffer and workload. Earlier 1024-sample baseline captures are retained privately as supplemental evidence and excluded from these tables. Host activity was not fully isolated, so no controlled speedup claim is made. These measurements describe only the stated native macOS Player; WebGL uses a different browser audio backend and needs separate profiling. Check audio routing, spatial falloff, and suspension behavior in each platform build you ship.
