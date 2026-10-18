using UnityEngine;
using UnityEngine.Audio;

namespace Phillipe2AM.SoundSystem.Configuration
{
    /// <summary>
    /// Defines a project-owned logical audio bus used by <see cref="AudioManager"/> playback calls.
    /// A bus routes audio to an <see cref="AudioMixerGroup"/>, supplies default 3D falloff settings,
    /// and can expose a mixer volume parameter. Its stable ID forms part of the saved volume key.
    /// Add each bus asset to an <see cref="AudioSystemConfiguration"/> before passing it to the manager.
    /// </summary>
    [CreateAssetMenu(menuName = "Phillipe 2AM/Sound System/Bus Definition", fileName = "AudioBus")]
    public sealed class AudioBusDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable project-defined key used only for local volume preferences. Keep it unchanged to preserve saved volume.")]
        private string _id;

        [SerializeField]
        [Tooltip("Audio Mixer group that receives clips played through this bus. It must belong to the mixer assigned to the Audio System Configuration.")]
        private AudioMixerGroup _mixerGroup;

        [SerializeField]
        [Tooltip("Optional exposed mixer volume parameter controlled by this bus. Leave empty when this bus does not need volume control.")]
        private string _exposedVolumeParameter;

        [SerializeField, Range(AudioSystemConfiguration.MinimumVolumeDb, AudioSystemConfiguration.MaximumVolumeDb)]
        [Tooltip("Default volume in decibels used when no saved preference exists. Finite values are clamped to -80 dB through +20 dB.")]
        private float _defaultVolumeDb;

        [SerializeField, Min(0f)]
        [Tooltip("Default minimum distance for positional 3D playback on this bus.")]
        private float _minDistance = 1f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Default maximum distance for positional 3D playback on this bus. Must be greater than Min Distance.")]
        private float _maxDistance = 500f;

        [SerializeField]
        [Tooltip("Built-in distance rolloff used by positional 3D playback. Custom rolloff curves are not supported.")]
        private AudioRolloffMode _rolloffMode = AudioRolloffMode.Logarithmic;

        internal string Id => _id;

        /// <summary>Gets the group from the configured mixer that receives playback routed through this bus.</summary>
        public AudioMixerGroup MixerGroup => _mixerGroup;

        /// <summary>Gets the optional exposed mixer volume parameter controlled by <see cref="AudioManager.SetBusVolume"/>. If empty, playback still works but this bus cannot be volume-controlled through the package.</summary>
        public string ExposedVolumeParameter => _exposedVolumeParameter;

        /// <summary>Gets the starting bus volume in decibels used when no saved volume preference exists.</summary>
        public float DefaultVolumeDb => _defaultVolumeDb;

        /// <summary>Gets the default minimum distance for 3D playback, in Unity world units. Per-call <see cref="AudioPlaybackOptions"/> can override it.</summary>
        public float MinDistance => AudioNumericUtility.IsFinite(_minDistance) ? Mathf.Max(0f, _minDistance) : 1f;

        /// <summary>Gets the default maximum distance for 3D playback, in Unity world units. Per-call <see cref="AudioPlaybackOptions"/> can override it.</summary>
        public float MaxDistance => AudioNumericUtility.IsFinite(_maxDistance) ? Mathf.Max(MinDistance + 0.01f, _maxDistance) : 500f;

        /// <summary>Gets the built-in distance rolloff curve used by 3D playback on this bus. Per-call <see cref="AudioPlaybackOptions"/> can override it.</summary>
        public AudioRolloffMode RolloffMode => _rolloffMode == AudioRolloffMode.Linear
            ? AudioRolloffMode.Linear
            : AudioRolloffMode.Logarithmic;

        internal bool HasValidSpatialSettings =>
            AudioNumericUtility.IsFinite(_minDistance) &&
            AudioNumericUtility.IsFinite(_maxDistance) &&
            _minDistance >= 0f &&
            _maxDistance > _minDistance &&
            (_rolloffMode == AudioRolloffMode.Logarithmic || _rolloffMode == AudioRolloffMode.Linear);

        private void OnValidate()
        {
            if (!AudioNumericUtility.IsFinite(_defaultVolumeDb))
            {
                Debug.LogWarning($"Audio bus '{name}' had a non-finite default volume; it was reset to 0 dB.", this);
                _defaultVolumeDb = 0f;
            }

            _defaultVolumeDb = AudioNumericUtility.ClampVolumeDb(_defaultVolumeDb);

            if (!AudioNumericUtility.IsFinite(_minDistance))
            {
                Debug.LogWarning($"Audio bus '{name}' had a non-finite minimum 3D distance; it was reset to 1.", this);
                _minDistance = 1f;
            }

            if (!AudioNumericUtility.IsFinite(_maxDistance))
            {
                Debug.LogWarning($"Audio bus '{name}' had a non-finite maximum 3D distance; it was reset to 500.", this);
                _maxDistance = 500f;
            }

            _minDistance = Mathf.Max(0f, _minDistance);
            _maxDistance = Mathf.Max(_minDistance + 0.01f, _maxDistance);

            if (_rolloffMode != AudioRolloffMode.Linear && _rolloffMode != AudioRolloffMode.Logarithmic)
            {
                Debug.LogWarning($"Audio bus '{name}' uses unsupported custom rolloff; it was reset to Logarithmic.", this);
                _rolloffMode = AudioRolloffMode.Logarithmic;
            }

            if (string.IsNullOrWhiteSpace(_id))
            {
                _id = name;
            }
        }
    }
}
