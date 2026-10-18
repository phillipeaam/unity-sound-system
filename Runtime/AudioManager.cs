using System;
using Phillipe2AM.SoundSystem.Configuration;
using Phillipe2AM.SoundSystem.Playback;
using Phillipe2AM.SoundSystem.Volume;
using UnityEngine;
using Unity.Profiling;

namespace Phillipe2AM.SoundSystem
{
    /// <summary>
    /// Main runtime interface for playing and controlling audio through configured mixer buses.
    /// Add one instance to a scene, assign its <see cref="AudioSystemConfiguration"/>, then call
    /// its methods or use <see cref="Instance"/> to access the active manager.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Audio System Configuration asset that defines this project's mixer and buses.")]
        private AudioSystemConfiguration _configuration;

        [SerializeField]
        [Tooltip("Keep this manager alive when scenes change. A persistent manager must be attached directly to a root GameObject.")]
        private bool _persistAcrossScenes = true;

        private AudioSourcePool _sourcePool;
        private AudioVolumeController _volumeController;
        private static readonly ProfilerMarker PlaybackMarker = new("SoundSystem.PlaybackPump");
        private AudioPlaybackPump _playbackPump;
        private bool _isPlaybackPumpRunning;
        private bool _applicationSuspended;

        /// <summary>Gets the active, initialized manager, or <see langword="null"/> when none exists.</summary>
        public static AudioManager Instance { get; private set; }

        /// <summary>Gets the project audio mixer, buses, pool, and playback settings assigned to this manager, or <see langword="null"/> when none is assigned.</summary>
        public AudioSystemConfiguration Configuration => _configuration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (_persistAcrossScenes && transform.parent != null)
            {
                Debug.LogError(
                    "AudioManager is configured to persist across scenes but is attached to a child GameObject. Move it to a root GameObject or disable persistence.",
                    this);
                enabled = false;
                return;
            }

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (_configuration == null)
            {
                Debug.LogError("AudioManager requires an AudioSystemConfiguration asset.", this);
                Instance = null;
                enabled = false;
                return;
            }

            if (_persistAcrossScenes)
            {
                DontDestroyOnLoad(this);
            }

            _playbackPump = gameObject.AddComponent<AudioPlaybackPump>();
            _playbackPump.Initialize(this);
            _sourcePool = new AudioSourcePool(
                transform,
                _configuration.InitialPoolSize,
                _configuration.MaximumPoolSize,
                _configuration.FadeDurationSeconds,
                _configuration.PlaybackStartTimeoutSeconds);

            _sourcePool.PlaybackPumpRequested += StartPlaybackPump;
            _volumeController = new AudioVolumeController(_configuration);
        }

        private void Start()
        {
            _volumeController?.ApplyInitialBusVolumes();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                SaveVolumePreferences();
                if (_configuration != null && _configuration.PausePlaybackOnApplicationSuspend && _sourcePool != null)
                {
                    _applicationSuspended = true;
                    _sourcePool.PauseForApplication(AudioSettings.dspTime);
                }

                return;
            }

            // Unity also sends an initial false callback on startup. Only resume a pause this manager handled.
            if (_applicationSuspended)
            {
                _applicationSuspended = false;
                _sourcePool?.ResumeFromApplication(AudioSettings.dspTime);
            }
        }

        private void OnApplicationQuit()
        {
            SaveVolumePreferences();
        }

        private void OnDestroy()
        {
            if (_playbackPump != null)
            {
                _playbackPump.enabled = false;
                Destroy(_playbackPump);
            }
            _isPlaybackPumpRunning = false;

            if (_sourcePool != null)
            {
                _sourcePool.PlaybackPumpRequested -= StartPlaybackPump;
                _sourcePool.Dispose();
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Requests 2D playback routed through the configured <paramref name="bus"/> and returns a handle
        /// for controlling this specific playback. The sound has no world position or distance falloff.
        /// </summary>
        /// <param name="clip">The Unity audio asset to play. It must not be <see langword="null"/>.</param>
        /// <param name="bus">A bus asset registered in <see cref="Configuration"/>. It selects the mixer group and optional mixer volume parameter for this 2D playback.</param>
        /// <param name="handle">Receives a handle for this playback. It is invalid when the request fails; pass <c>out _</c> when individual control is not needed.</param>
        /// <param name="options">Per-playback loop, volume, and pitch. 3D distance or rolloff overrides are not valid for this 2D overload. With <see langword="default"/>, playback is one-shot with per-source volume 1 and pitch 1.</param>
        /// <param name="delaySeconds">Finite delay in seconds before playback begins. Zero or a negative value starts immediately.</param>
        /// <returns><see langword="true"/> if playback was accepted; otherwise <see langword="false"/> when the manager is not initialized, the request is invalid, or the full pool has no replaceable playback in this bus and loop category.</returns>
        public bool TryPlay(
            AudioClip clip,
            AudioBusDefinition bus,
            out AudioPlaybackHandle handle,
            AudioPlaybackOptions options = default,
            float delaySeconds = 0f)
        {
            handle = default;
            if (!TryValidatePlaybackRequest(clip, bus, delaySeconds, false, options,
                    out AudioBusDefinition configuredBus, out AudioPlaybackSettings settings) ||
                !_sourcePool.Play(clip, configuredBus, delaySeconds, settings, out AudioSourcePlayback playback))
            {
                return false;
            }

            handle = new AudioPlaybackHandle(this, playback.SlotIndex, playback.Generation);
            return true;
        }

        /// <summary>
        /// Requests 3D playback that follows a target's world position and returns a handle for controlling
        /// this specific playback. The manager-owned audio source follows the target while it exists; if the
        /// target is destroyed, playback finishes at its last valid position.
        /// </summary>
        /// <param name="clip">The Unity audio asset to play. It must not be <see langword="null"/>.</param>
        /// <param name="bus">A bus asset registered in <see cref="Configuration"/>. It selects the mixer group and supplies defaults for distance falloff.</param>
        /// <param name="target">The transform whose world position the sound follows. It must exist and have finite coordinates; the manager does not parent the audio source to it.</param>
        /// <param name="handle">Receives a handle for this playback. It is invalid when the request fails; pass <c>out _</c> when individual control is not needed.</param>
        /// <param name="options">Per-playback loop, volume, pitch, and optional 3D distance and rolloff overrides. With <see langword="default"/>, playback is one-shot at per-source volume 1 and pitch 1, using the bus's 3D defaults.</param>
        /// <param name="delaySeconds">Finite delay in seconds before playback begins. Zero or a negative value starts immediately.</param>
        /// <returns><see langword="true"/> if playback was accepted; otherwise <see langword="false"/> when the manager is not initialized, the request is invalid, or the full pool has no replaceable playback in this bus and loop category.</returns>
        public bool TryPlay(
            AudioClip clip,
            AudioBusDefinition bus,
            Transform target,
            out AudioPlaybackHandle handle,
            AudioPlaybackOptions options = default,
            float delaySeconds = 0f)
        {
            handle = default;
            if (_sourcePool == null || target == null || !AudioNumericUtility.IsFinite(target.position))
            {
                if (target == null)
                {
                    Debug.LogWarning("AudioManager.TryPlay rejected a null follow target. Provide a valid Transform.", this);
                }
                else if (!AudioNumericUtility.IsFinite(target.position))
                {
                    Debug.LogWarning("AudioManager.TryPlay rejected a target with a non-finite world position.", this);
                }

                return false;
            }

            if (!TryValidatePlaybackRequest(clip, bus, delaySeconds, true, options,
                    out AudioBusDefinition configuredBus, out AudioPlaybackSettings settings) ||
                !_sourcePool.Play(clip, configuredBus, target, delaySeconds, settings, out AudioSourcePlayback playback))
            {
                return false;
            }

            handle = new AudioPlaybackHandle(this, playback.SlotIndex, playback.Generation);
            return true;
        }

        /// <summary>
        /// Requests 3D playback at a fixed world position and returns a handle for controlling this specific
        /// playback. The position is captured when the request is made and does not follow another object.
        /// </summary>
        /// <param name="clip">The Unity audio asset to play. It must not be <see langword="null"/>.</param>
        /// <param name="bus">A bus asset registered in <see cref="Configuration"/>. It selects the mixer group and supplies defaults for distance falloff.</param>
        /// <param name="worldPosition">The sound's fixed position in world space. All coordinates must be finite.</param>
        /// <param name="handle">Receives a handle for this playback. It is invalid when the request fails; pass <c>out _</c> when individual control is not needed.</param>
        /// <param name="options">Per-playback loop, volume, pitch, and optional 3D distance and rolloff overrides. With <see langword="default"/>, playback is one-shot at per-source volume 1 and pitch 1, using the bus's 3D defaults.</param>
        /// <param name="delaySeconds">Finite delay in seconds before playback begins. Zero or a negative value starts immediately.</param>
        /// <returns><see langword="true"/> if playback was accepted; otherwise <see langword="false"/> when the manager is not initialized, the request is invalid, or the full pool has no replaceable playback in this bus and loop category.</returns>
        public bool TryPlayAtPosition(
            AudioClip clip,
            AudioBusDefinition bus,
            Vector3 worldPosition,
            out AudioPlaybackHandle handle,
            AudioPlaybackOptions options = default,
            float delaySeconds = 0f)
        {
            handle = default;
            if (_sourcePool == null)
            {
                return false;
            }

            if (!AudioNumericUtility.IsFinite(worldPosition))
            {
                Debug.LogWarning("AudioManager.TryPlayAtPosition rejected a non-finite world position.", this);
                return false;
            }

            if (!TryValidatePlaybackRequest(clip, bus, delaySeconds, true, options,
                    out AudioBusDefinition configuredBus, out AudioPlaybackSettings settings) ||
                !_sourcePool.PlayAtPosition(clip, configuredBus, worldPosition, delaySeconds, settings,
                    out AudioSourcePlayback playback))
            {
                return false;
            }

            handle = new AudioPlaybackHandle(this, playback.SlotIndex, playback.Generation);
            return true;
        }

        internal AudioPlaybackState GetPlaybackState(int slotIndex, uint generation)
        {
            return _sourcePool != null
                ? _sourcePool.GetPlaybackState(slotIndex, generation)
                : AudioPlaybackState.Invalid;
        }

        internal bool StopPlayback(int slotIndex, uint generation, bool fade)
        {
            return _sourcePool != null && _sourcePool.StopPlayback(slotIndex, generation, fade);
        }

        internal bool PausePlayback(int slotIndex, uint generation, bool fade)
        {
            return _sourcePool != null && _sourcePool.PausePlayback(slotIndex, generation, fade);
        }

        internal bool ResumePlayback(int slotIndex, uint generation, bool fade)
        {
            return _sourcePool != null && _sourcePool.ResumePlayback(slotIndex, generation, fade);
        }

        /// <summary>Stops active or scheduled playback routed through a configured bus.</summary>
        /// <param name="bus">The configured bus whose playback should stop.</param>
        /// <param name="fade">When <see langword="true"/>, fades over <see cref="AudioSystemConfiguration.FadeDurationSeconds"/>; when <see langword="false"/>, stops immediately.</param>
        /// <returns>The number of playbacks for which the stop action was applied. Returns zero for an unconfigured bus or when nothing matched.</returns>
        public int StopBus(AudioBusDefinition bus, bool fade = true)
        {
            return _sourcePool != null && TryGetBus(bus, out _)
                ? _sourcePool.StopBus(bus, fade)
                : 0;
        }

        /// <summary>Stops active or scheduled playback using the exact clip asset.</summary>
        /// <param name="clip">The clip asset to match. A <see langword="null"/> clip matches nothing.</param>
        /// <param name="fade">When <see langword="true"/>, fades over <see cref="AudioSystemConfiguration.FadeDurationSeconds"/>; when <see langword="false"/>, stops immediately.</param>
        /// <returns>The number of playbacks for which the stop action was applied. Returns zero when nothing matched.</returns>
        public int StopClip(AudioClip clip, bool fade = true)
        {
            return _sourcePool?.StopClip(clip, fade) ?? 0;
        }

        /// <summary>Pauses active or scheduled playback routed through a configured bus.</summary>
        /// <param name="bus">The configured bus whose playback should pause.</param>
        /// <param name="fade">When <see langword="true"/>, fades over <see cref="AudioSystemConfiguration.FadeDurationSeconds"/> before pausing; when <see langword="false"/>, pauses immediately.</param>
        /// <returns>The number of playbacks for which the pause action was applied. Returns zero for an unconfigured bus or when nothing matched.</returns>
        public int PauseBus(AudioBusDefinition bus, bool fade = true)
        {
            return _sourcePool != null && TryGetBus(bus, out _)
                ? _sourcePool.PauseBus(bus, fade)
                : 0;
        }

        /// <summary>Pauses active or scheduled playback using the exact clip asset.</summary>
        /// <param name="clip">The clip asset to match. A <see langword="null"/> clip matches nothing.</param>
        /// <param name="fade">When <see langword="true"/>, fades over <see cref="AudioSystemConfiguration.FadeDurationSeconds"/> before pausing; when <see langword="false"/>, pauses immediately.</param>
        /// <returns>The number of playbacks for which the pause action was applied. Returns zero when nothing matched.</returns>
        public int PauseClip(AudioClip clip, bool fade = true)
        {
            return _sourcePool?.PauseClip(clip, fade) ?? 0;
        }

        /// <summary>Resumes playback paused through this manager on a configured bus.</summary>
        /// <param name="bus">The configured bus whose paused playback should resume.</param>
        /// <param name="fade">When <see langword="true"/>, fades in over <see cref="AudioSystemConfiguration.FadeDurationSeconds"/>; when <see langword="false"/>, resumes at its stored volume immediately.</param>
        /// <returns>The number of paused playbacks for which the resume action was applied. Returns zero for an unconfigured bus or when nothing matched.</returns>
        public int ResumeBus(AudioBusDefinition bus, bool fade = true)
        {
            return _sourcePool != null && TryGetBus(bus, out _)
                ? _sourcePool.ResumeBus(bus, fade)
                : 0;
        }

        /// <summary>Resumes playback of the exact clip asset when it was paused through this manager.</summary>
        /// <param name="clip">The clip asset to match. A <see langword="null"/> clip matches nothing.</param>
        /// <param name="fade">When <see langword="true"/>, fades in over <see cref="AudioSystemConfiguration.FadeDurationSeconds"/>; when <see langword="false"/>, resumes at its stored volume immediately.</param>
        /// <returns>The number of paused playbacks for which the resume action was applied. Returns zero when nothing matched.</returns>
        public int ResumeClip(AudioClip clip, bool fade = true)
        {
            return _sourcePool?.ResumeClip(clip, fade) ?? 0;
        }

        /// <summary>Checks whether a configured bus has audio that is scheduled, playing, or fading, and is not paused or suspended by the manager.</summary>
        /// <param name="bus">The configured bus to check.</param>
        /// <returns><see langword="true"/> when matching audio exists; otherwise <see langword="false"/>, including for an unconfigured bus.</returns>
        public bool IsBusPlaying(AudioBusDefinition bus)
        {
            return _sourcePool != null && TryGetBus(bus, out _) && _sourcePool.IsBusPlaying(bus);
        }

        /// <summary>Checks whether the exact clip asset has audio that is scheduled, playing, or fading, and is not paused or suspended by the manager.</summary>
        /// <param name="clip">The clip asset to check. A <see langword="null"/> clip returns <see langword="false"/>.</param>
        /// <returns><see langword="true"/> when matching audio exists; otherwise <see langword="false"/>.</returns>
        public bool IsClipPlaying(AudioClip clip)
        {
            return _sourcePool != null && _sourcePool.IsClipPlaying(clip);
        }

        /// <summary>Sets the exposed mixer volume for a configured bus and saves the value when volume preference persistence is enabled.</summary>
        /// <param name="bus">A configured bus with an exposed volume parameter on the configured mixer.</param>
        /// <param name="decibels">Requested level in decibels. Finite values are clamped to <see cref="AudioSystemConfiguration.MinimumVolumeDb"/> through <see cref="AudioSystemConfiguration.MaximumVolumeDb"/>.</param>
        /// <returns><see langword="true"/> when the request is accepted; otherwise <see langword="false"/> if the bus, mixer parameter, or value is invalid.</returns>
        public bool SetBusVolume(AudioBusDefinition bus, float decibels)
        {
            return _volumeController != null &&
                TryGetBus(bus, out AudioBusDefinition configuredBus) &&
                _volumeController.SetBusVolume(configuredBus, decibels);
        }

        /// <summary>Reads the current exposed mixer volume for a configured bus in decibels.</summary>
        /// <param name="bus">The configured bus whose exposed volume should be read.</param>
        /// <returns>The current mixer volume after initialization; before volume initialization, returns the pending, saved, or bus default value. Returns the bus default when the mixer value cannot be read, and <see cref="AudioSystemConfiguration.MinimumVolumeDb"/> when the manager is not initialized or the bus is null or unconfigured.</returns>
        public float GetBusVolume(AudioBusDefinition bus)
        {
            if (_volumeController == null || !TryGetBus(bus, out AudioBusDefinition configuredBus))
            {
                return AudioSystemConfiguration.MinimumVolumeDb;
            }

            return _volumeController.GetBusVolume(configuredBus);
        }

        /// <summary>Sets a configured bus to a percentage of its <see cref="AudioBusDefinition.DefaultVolumeDb"/> level and saves the effective dB value when preference persistence is enabled.</summary>
        /// <param name="bus">A bus asset registered in <see cref="Configuration"/> with an exposed volume parameter on the configured mixer.</param>
        /// <param name="percent">Finite requested level from 0 to 100. Values outside that range are clamped; 0 silences the bus and 100 applies the bus's configured default.</param>
        /// <returns><see langword="true"/> when the volume request is accepted; otherwise <see langword="false"/> when the manager or bus is unavailable, the mixer parameter is invalid, or the value is NaN or infinity.</returns>
        public bool SetBusVolumePercent(AudioBusDefinition bus, float percent)
        {
            return _volumeController != null &&
                TryGetBus(bus, out AudioBusDefinition configuredBus) &&
                _volumeController.SetBusVolumePercent(configuredBus, percent);
        }

        /// <summary>Reads a configured bus's effective mixer volume as a percentage of its current <see cref="AudioBusDefinition.DefaultVolumeDb"/> level.</summary>
        /// <param name="bus">The configured bus whose exposed volume parameter should be read.</param>
        /// <returns>The effective level from 0 to 100, capped at 100 when the saved dB level exceeds a lowered bus default. Returns 0 at the mixer floor or when the manager or bus is unavailable.</returns>
        public float GetBusVolumePercent(AudioBusDefinition bus)
        {
            if (_volumeController == null || !TryGetBus(bus, out AudioBusDefinition configuredBus))
            {
                return 0f;
            }

            return _volumeController.GetBusVolumePercent(configuredBus);
        }

        private bool TryGetBus(AudioBusDefinition requestedBus, out AudioBusDefinition bus)
        {
            if (requestedBus != null && _configuration != null && _configuration.ContainsBus(requestedBus))
            {
                bus = requestedBus;
                return true;
            }

            bus = null;
            return false;
        }

        private bool TryValidatePlaybackRequest(
            AudioClip clip,
            AudioBusDefinition bus,
            float delaySeconds,
            bool isSpatial,
            AudioPlaybackOptions options,
            out AudioBusDefinition configuredBus,
            out AudioPlaybackSettings settings)
        {
            configuredBus = null;
            settings = default;

            if (_sourcePool == null)
            {
                return false;
            }

            if (clip == null)
            {
                Debug.LogWarning("AudioManager.TryPlay received a null AudioClip.", this);
                return false;
            }

            if (!TryGetBus(bus, out configuredBus))
            {
                Debug.LogWarning(
                    "AudioManager received a null, unconfigured, or invalid audio bus asset '" +
                    (bus != null ? bus.name : string.Empty) + "'.",
                    this);
                return false;
            }

            if (!_configuration.TryValidateBus(configuredBus, false, out string busIssue))
            {
                Debug.LogWarning(busIssue, this);
                configuredBus = null;
                return false;
            }

            if (!AudioNumericUtility.IsFinite(delaySeconds))
            {
                Debug.LogWarning("AudioManager.TryPlay rejected a non-finite delay. Provide a finite number of seconds.", this);
                configuredBus = null;
                return false;
            }

            if (!options.TryResolve(configuredBus, isSpatial, out settings, out string optionsIssue))
            {
                Debug.LogWarning("AudioManager rejected playback options. " + optionsIssue, this);
                configuredBus = null;
                return false;
            }

            return true;
        }

        private void SaveVolumePreferences()
        {
            _volumeController?.SavePreferences();
        }

        private void StartPlaybackPump()
        {
            if (_isPlaybackPumpRunning || _sourcePool is not { HasPendingPlaybackWork: true })
            {
                return;
            }

            _isPlaybackPumpRunning = true;
            _playbackPump.enabled = true;
        }

        internal void TickPlaybackPump()
        {
            using (PlaybackMarker.Auto())
            {
                try
                {
                    double now = AudioSettings.dspTime;
                    _sourcePool.TickPlaybackStates(now);
                    _sourcePool.TickFades(now);
                }
                catch (Exception exception)
                {
                    HandlePlaybackPumpFailure(exception);
                }

                if (!_sourcePool.HasPendingPlaybackWork)
                {
                    _isPlaybackPumpRunning = false;
                    _playbackPump.enabled = false;
                }
            }
        }

        private void OnValidate()
        {
            if (_persistAcrossScenes && transform.parent != null)
            {
                Debug.LogWarning(
                    "A persistent AudioManager must be on a root GameObject. Move this component to the root or disable scene persistence.",
                    this);
            }
        }

        internal void HandlePlaybackPumpFailure(Exception exception)
        {
            Exception recoveryFailure = null;
            try
            {
                _sourcePool?.StopAll();
            }
            catch (Exception cleanupException)
            {
                recoveryFailure = cleanupException;
            }

            Exception diagnostic = recoveryFailure == null
                ? exception
                : new AggregateException(
                    "The audio playback pump failed and releasing its playback slots also failed.",
                    exception,
                    recoveryFailure);

            Debug.LogException(diagnostic, this);
        }
    }
}
