using System.Collections.Generic;
using Phillipe2AM.SoundSystem.Configuration;
using UnityEngine.Audio;

namespace Phillipe2AM.SoundSystem.Volume
{
    /// <summary>Applies configured bus levels and persists the player's local volume choices.</summary>
    internal sealed class AudioVolumeController
    {
        private const string PreferenceKeySeparator = ".";
        private const string VolumePreferenceKeySuffix = ".VolumeDb";

        private readonly AudioSystemConfiguration _configuration;
        private readonly Dictionary<AudioBusDefinition, float> _pendingVolumes = new();
        private readonly HashSet<AudioBusDefinition> _failedMixerApplications = new();
        private readonly Dictionary<AudioBusDefinition, string> _preferenceKeys = new();

        private bool _isInitialized;

        public AudioVolumeController(AudioSystemConfiguration configuration)
        {
            _configuration = configuration;
        }

        public bool SetBusVolume(AudioBusDefinition bus, float decibels)
        {
            if (!AudioNumericUtility.IsFinite(decibels))
            {
                return false;
            }

            if (_configuration == null)
            {
                return false;
            }

            if (!_configuration.TryValidateBus(bus, true, out _))
            {
                return false;
            }

            float volume = AudioNumericUtility.ClampVolumeDb(decibels);

            if (!_isInitialized)
            {
                _pendingVolumes[bus] = volume;
                return true;
            }

            return ApplyVolume(bus, volume, true);
        }

        public bool SetBusVolumePercent(AudioBusDefinition bus, float percent)
        {
            if (!AudioNumericUtility.IsFinite(percent) || bus == null)
            {
                return false;
            }

            float clampedPercent = UnityEngine.Mathf.Clamp(percent, 0f, 100f);
            float defaultVolume = AudioNumericUtility.ClampVolumeDb(bus.DefaultVolumeDb);
            float decibels = clampedPercent <= 0f
                ? AudioSystemConfiguration.MinimumVolumeDb
                : defaultVolume + 20f * UnityEngine.Mathf.Log10(clampedPercent / 100f);

            return SetBusVolume(bus, decibels);
        }

        public float GetBusVolume(AudioBusDefinition bus)
        {
            if (!ContainsBus(bus))
            {
                return AudioSystemConfiguration.MinimumVolumeDb;
            }

            if (!_isInitialized)
            {
                return _pendingVolumes.TryGetValue(bus, out float pendingVolume)
                    ? pendingVolume
                    : GetInitialBusVolume(bus);
            }

            if (!_configuration.TryValidateBus(bus, true, out _))
            {
                return bus.DefaultVolumeDb;
            }

            return _configuration.Mixer.GetFloat(bus.ExposedVolumeParameter, out float volume)
                ? AudioNumericUtility.ClampVolumeDb(volume, bus.DefaultVolumeDb)
                : bus.DefaultVolumeDb;
        }

        public float GetBusVolumePercent(AudioBusDefinition bus)
        {
            if (!ContainsBus(bus))
            {
                return 0f;
            }

            float decibels = GetBusVolume(bus);
            if (decibels <= AudioSystemConfiguration.MinimumVolumeDb)
            {
                return 0f;
            }

            float defaultVolume = AudioNumericUtility.ClampVolumeDb(bus.DefaultVolumeDb);
            float percent = 100f * UnityEngine.Mathf.Pow(10f, (decibels - defaultVolume) / 20f);
            return UnityEngine.Mathf.Clamp(percent, 0f, 100f);
        }

        public void ApplyInitialBusVolumes()
        {
            if (_isInitialized)
            {
                return;
            }

            AudioMixer mixer = _configuration != null ? _configuration.Mixer : null;
            if (mixer == null)
            {
                UnityEngine.Debug.LogError("The Audio System Configuration does not reference an AudioMixer.", _configuration);
                _isInitialized = true;
                _pendingVolumes.Clear();
                return;
            }

            for (int index = 0; index < _configuration.BusCount; index++)
            {
                if (!_configuration.TryGetBusAt(index, out AudioBusDefinition bus))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(bus.ExposedVolumeParameter))
                {
                    continue;
                }

                if (!_configuration.TryValidateBus(bus, true, out string issue))
                {
                    UnityEngine.Debug.LogWarning(issue, _configuration);
                    continue;
                }

                ApplyVolume(bus, GetInitialBusVolume(bus), false);
            }

            _isInitialized = true;

            foreach (KeyValuePair<AudioBusDefinition, float> pending in _pendingVolumes)
            {
                ApplyVolume(pending.Key, pending.Value, true);
            }

            _pendingVolumes.Clear();
        }

        public void SavePreferences()
        {
            if (_configuration == null)
            {
                return;
            }

            if (!_configuration.PersistVolumePreferences)
            {
                return;
            }

            UnityEngine.PlayerPrefs.Save();
        }

        private bool ApplyVolume(AudioBusDefinition bus, float volume, bool persist)
        {
            if (!_configuration.Mixer.SetFloat(bus.ExposedVolumeParameter, volume))
            {
                LogMixerApplicationFailure(bus);
                return false;
            }

            _failedMixerApplications.Remove(bus);
            if (!persist)
            {
                return true;
            }

            if (!_configuration.PersistVolumePreferences)
            {
                return true;
            }

            UnityEngine.PlayerPrefs.SetFloat(GetPreferenceKey(bus), volume);
            return true;
        }

        private float GetInitialBusVolume(AudioBusDefinition bus)
        {
            float defaultVolume = bus.DefaultVolumeDb;
            if (!_configuration.PersistVolumePreferences)
            {
                return defaultVolume;
            }

            float savedVolume = UnityEngine.PlayerPrefs.GetFloat(GetPreferenceKey(bus), defaultVolume);
            return AudioNumericUtility.ClampVolumeDb(savedVolume, defaultVolume);
        }

        private bool ContainsBus(AudioBusDefinition bus)
        {
            if (bus == null)
            {
                return false;
            }
            
            return _configuration != null && _configuration.ContainsBus(bus);
        }

        private string GetPreferenceKey(AudioBusDefinition bus)
        {
            if (_preferenceKeys.TryGetValue(bus, out string key))
            {
                return key;
            }

            key = string.Concat(_configuration.PreferenceKeyPrefix, PreferenceKeySeparator, bus.Id, VolumePreferenceKeySuffix);
            _preferenceKeys.Add(bus, key);
            return key;
        }

        private void LogMixerApplicationFailure(AudioBusDefinition bus)
        {
            if (!_failedMixerApplications.Add(bus))
            {
                return;
            }

            UnityEngine.Debug.LogWarning(
                $"Audio Mixer rejected volume updates for bus '{bus.name}' parameter '{bus.ExposedVolumeParameter}'. Confirm the parameter is exposed on mixer '{_configuration.Mixer.name}' and is not being edited by an active snapshot.",
                _configuration);
        }
    }
}
