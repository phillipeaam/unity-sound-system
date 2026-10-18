using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Phillipe2AM.SoundSystem.Configuration
{
    /// <summary>
    /// Project-owned settings assigned to <see cref="AudioManager"/>. This asset connects the project's
    /// mixer and bus definitions to the runtime playback pool, fade timing, and volume preferences.
    /// </summary>
    [CreateAssetMenu(menuName = "Phillipe 2AM/Sound System/Audio System Configuration", fileName = "AudioSystemConfiguration")]
    public sealed class AudioSystemConfiguration : ScriptableObject
    {
        /// <summary>Lowest volume accepted by the bus volume controls, in decibels.</summary>
        public const float MinimumVolumeDb = -80f;

        /// <summary>Highest volume accepted by the bus volume controls, in decibels.</summary>
        public const float MaximumVolumeDb = 20f;

        /// <summary>Fallback playback start timeout used when the configured value is invalid.</summary>
        public const float DefaultPlaybackStartTimeoutSeconds = 5f;

        /// <summary>Smallest accepted playback start timeout, in seconds.</summary>
        public const float MinimumPlaybackStartTimeoutSeconds = 0.1f;
        private const string DefaultPreferenceKeyPrefix = "Phillipe2AM.SoundSystem";

        [SerializeField]
        [Tooltip("Audio Mixer used to control the exposed volume parameter for each configured bus.")]
        private AudioMixer _mixer;

        [SerializeField]
        [Tooltip("Bus definitions available to this sound system. Each bus must reference a group and exposed volume parameter from the assigned mixer.")]
        private List<AudioBusDefinition> _buses = new();

        [SerializeField, Min(0)]
        [Tooltip("Number of playback slots created when the AudioManager initializes. Set to 0 to create slots only when audio is played.")]
        private int _initialPoolSize = 4;

        [SerializeField, Min(1)]
        [Tooltip("Maximum number of concurrent playback slots. When full, a request replaces the oldest active playback with the same bus and loop setting; if none is eligible, the request is skipped.")]
        private int _maximumPoolSize = 32;

        [SerializeField, Min(0f)]
        [Tooltip("Duration in seconds for fades used when pausing, resuming, or stopping playback. A value of 0 makes these operations immediate.")]
        private float _fadeDurationSeconds = 0.5f;

        [SerializeField, Min(MinimumPlaybackStartTimeoutSeconds)]
        [Tooltip("Maximum time, after its scheduled start, for a playback request to begin. If it does not start by then, its pool slot is released. A global AudioListener pause freezes this timer.")]
        private float _playbackStartTimeoutSeconds = DefaultPlaybackStartTimeoutSeconds;

        [SerializeField]
        [Tooltip("Save player-selected bus volumes in PlayerPrefs and restore them on initialization. When disabled, saved preferences are not restored and bus defaults are used instead.")]
        private bool _persistVolumePreferences = true;

        [SerializeField]
        [Tooltip("Prefix for saved volume preference keys. Each key also includes the bus ID; keep this stable to retain existing preferences.")]
        private string _preferenceKeyPrefix = DefaultPreferenceKeyPrefix;

        [SerializeField]
        [Tooltip("Pause package-owned playback while the application is suspended and resume only sources paused by this setting.")]
        private bool _pausePlaybackOnApplicationSuspend = true;

        /// <summary>Gets the mixer that owns the groups and exposed volume parameters used by the configured buses.</summary>
        public AudioMixer Mixer => _mixer;

        /// <summary>Gets the number of source slots created when the manager initializes. Zero creates slots on demand.</summary>
        public int InitialPoolSize => Mathf.Clamp(_initialPoolSize, 0, MaximumPoolSize);

        /// <summary>Gets the maximum number of simultaneous source slots. At capacity, the manager can replace the oldest playback with the same bus and loop setting.</summary>
        public int MaximumPoolSize => Mathf.Max(1, _maximumPoolSize);

        /// <summary>Gets the fade duration, in seconds, used by playback pause, resume, and stop requests.</summary>
        public float FadeDurationSeconds => AudioNumericUtility.IsFinite(_fadeDurationSeconds)
            ? Mathf.Max(0f, _fadeDurationSeconds)
            : 0f;

        /// <summary>Gets how long, in seconds after its scheduled start, a playback may take to begin before its slot is released.</summary>
        public float PlaybackStartTimeoutSeconds => AudioNumericUtility.IsFinite(_playbackStartTimeoutSeconds)
            ? Mathf.Max(MinimumPlaybackStartTimeoutSeconds, _playbackStartTimeoutSeconds)
            : DefaultPlaybackStartTimeoutSeconds;

        /// <summary>Gets whether bus volume changes are saved in <see cref="PlayerPrefs"/> and restored in a later session.</summary>
        public bool PersistVolumePreferences => _persistVolumePreferences;

        /// <summary>Gets whether the manager pauses its active playback while the application is suspended, then resumes playback it paused.</summary>
        public bool PausePlaybackOnApplicationSuspend => _pausePlaybackOnApplicationSuspend;

        /// <summary>Gets the prefix used with each bus ID to form its saved volume preference key.</summary>
        public string PreferenceKeyPrefix => string.IsNullOrWhiteSpace(_preferenceKeyPrefix)
            ? DefaultPreferenceKeyPrefix
            : _preferenceKeyPrefix.Trim();

        /// <summary>Gets the number of bus entries in this configuration.</summary>
        public int BusCount => _buses?.Count ?? 0;

        /// <summary>Gets the configured bus at a list position.</summary>
        /// <param name="index">Zero-based position in the bus list.</param>
        /// <param name="bus">Receives the bus at that position, or <see langword="null"/> when the index is invalid or the entry is empty.</param>
        /// <returns><see langword="true"/> when the position contains a bus; otherwise <see langword="false"/>.</returns>
        public bool TryGetBusAt(int index, out AudioBusDefinition bus)
        {
            if (_buses == null || index < 0 || index >= _buses.Count)
            {
                bus = null;
                return false;
            }

            bus = _buses[index];
            return bus != null;
        }

        /// <summary>Checks whether a bus asset is registered in this configuration.</summary>
        /// <param name="bus">The bus asset to look for.</param>
        /// <returns><see langword="true"/> when this configuration contains that asset; otherwise <see langword="false"/>.</returns>
        public bool ContainsBus(AudioBusDefinition bus)
        {
            if (bus == null || _buses == null)
            {
                return false;
            }

            foreach (AudioBusDefinition configuredBus in _buses)
            {
                if (configuredBus == bus)
                {
                    return true;
                }
            }

            return false;
        }

        internal bool TryValidateBus(AudioBusDefinition bus, bool requireVolumeParameter, out string issue)
        {
            if (!ContainsBus(bus))
            {
                issue = "The audio bus asset is not included in this audio system configuration.";
                return false;
            }

            if (_mixer == null)
            {
                issue = "Assign an AudioMixer to this audio system configuration.";
                return false;
            }

            if (bus.MixerGroup == null)
            {
                issue = $"Audio bus '{bus.name}' has no mixer group. Assign a group from the configured mixer.";
                return false;
            }

            if (bus.MixerGroup.audioMixer != _mixer)
            {
                issue = $"Audio bus '{bus.name}' routes to a group outside the configured mixer '{_mixer.name}'. Assign a group owned by '{_mixer.name}'.";
                return false;
            }

            if (!bus.HasValidSpatialSettings)
            {
                issue = $"Audio bus '{bus.name}' has invalid 3D distances or an unsupported rolloff mode. Use finite distances where 0 <= Min Distance < Max Distance and choose Linear or Logarithmic rolloff.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(bus.ExposedVolumeParameter))
            {
                if (requireVolumeParameter)
                {
                    issue = $"Audio bus '{bus.name}' has no exposed volume parameter. Expose the group's volume on mixer '{_mixer.name}'.";
                    return false;
                }

                issue = null;
                return true;
            }

            if (requireVolumeParameter && !_mixer.GetFloat(bus.ExposedVolumeParameter, out _))
            {
                issue = $"Audio bus '{bus.name}' references volume parameter '{bus.ExposedVolumeParameter}', which is not exposed by mixer '{_mixer.name}'. Expose it or clear the field.";
                return false;
            }

            issue = null;
            return true;
        }

        private void OnValidate()
        {
            NormalizePoolSizes();
            NormalizeFadeDuration();
            NormalizePlaybackStartTimeout();
            NormalizePreferenceKeyPrefix();
            ValidateBuses();
        }

        private void NormalizePoolSizes()
        {
            _maximumPoolSize = Mathf.Max(1, _maximumPoolSize);
            _initialPoolSize = Mathf.Clamp(_initialPoolSize, 0, _maximumPoolSize);
        }

        private void NormalizeFadeDuration()
        {
            if (!AudioNumericUtility.IsFinite(_fadeDurationSeconds))
            {
                Debug.LogWarning("Audio System Configuration fade duration was not finite and has been reset to 0 seconds.", this);
                _fadeDurationSeconds = 0f;
                return;
            }

            _fadeDurationSeconds = Mathf.Max(0f, _fadeDurationSeconds);
        }

        private void NormalizePlaybackStartTimeout()
        {
            if (!AudioNumericUtility.IsFinite(_playbackStartTimeoutSeconds))
            {
                Debug.LogWarning(
                    $"Audio System Configuration playback start timeout was not finite and has been reset to {DefaultPlaybackStartTimeoutSeconds} seconds.",
                    this);
                _playbackStartTimeoutSeconds = DefaultPlaybackStartTimeoutSeconds;
            }

            _playbackStartTimeoutSeconds = Mathf.Max(
                MinimumPlaybackStartTimeoutSeconds,
                _playbackStartTimeoutSeconds);
        }

        private void NormalizePreferenceKeyPrefix()
        {
            if (string.IsNullOrWhiteSpace(_preferenceKeyPrefix))
            {
                _preferenceKeyPrefix = DefaultPreferenceKeyPrefix;
            }
        }

        private void ValidateBuses()
        {
            if (_buses == null)
            {
                return;
            }

            HashSet<string> busIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<AudioBusDefinition> busAssets = new HashSet<AudioBusDefinition>();

            foreach (AudioBusDefinition bus in _buses)
            {
                if (bus == null)
                {
                    continue;
                }

                ValidateUniqueBusReferences(bus, busIds, busAssets);
                ValidateBusConfiguration(bus);
            }
        }

        private void ValidateUniqueBusReferences(
            AudioBusDefinition bus,
            HashSet<string> busIds,
            HashSet<AudioBusDefinition> busAssets)
        {
            if (!busAssets.Add(bus))
            {
                Debug.LogWarning($"Audio bus asset '{bus.name}' is listed more than once.", this);
            }

            if (string.IsNullOrWhiteSpace(bus.Id))
            {
                Debug.LogWarning($"Audio bus asset '{bus.name}' has no stable preference ID.", this);
                return;
            }

            if (!busIds.Add(bus.Id))
            {
                Debug.LogWarning(
                    $"Audio bus preference ID '{bus.Id}' is duplicated. Buses with the same ID share saved volume preferences.",
                    this);
            }
        }

        private void ValidateBusConfiguration(AudioBusDefinition bus)
        {
            if (bus.MixerGroup == null)
            {
                Debug.LogWarning($"Audio bus '{bus.name}' has no mixer group.", this);
                return;
            }

            bool requireVolumeParameter = !string.IsNullOrWhiteSpace(bus.ExposedVolumeParameter);
            if (TryValidateBus(bus, requireVolumeParameter, out string issue))
            {
                return;
            }

            Debug.LogWarning(issue, this);
        }
    }
}
