using Phillipe2AM.SoundSystem.Configuration;
using UnityEngine;

namespace Phillipe2AM.SoundSystem.Playback
{
    /// <summary>
    /// Per-request overrides passed to an <see cref="AudioManager"/> playback method.
    /// Looping defaults to off, volume and pitch default to 1, and omitted 3D settings come from the selected bus.
    /// </summary>
    public readonly struct AudioPlaybackOptions
    {
        private bool Loop { get; }
        private float? Volume { get; }
        private float? Pitch { get; }
        private float? MinDistance { get; }
        private float? MaxDistance { get; }
        private AudioRolloffMode? RolloffMode { get; }

        /// <summary>Creates the settings overrides for one playback request.</summary>
        /// <param name="loop">Whether the clip repeats until explicitly stopped. Defaults to <see langword="false"/>.</param>
        /// <param name="volume">Linear playback volume from 0 (silent) to 1 (full volume). Defaults to 1.</param>
        /// <param name="pitch">Playback pitch and speed, from 0.1 to 3. Defaults to 1.</param>
        /// <param name="minDistance">Optional 3D distance at which the sound begins to attenuate. When omitted, uses the bus setting. Only valid for spatial playback.</param>
        /// <param name="maxDistance">Optional far distance used by the 3D rolloff curve. Must be greater than the resolved minimum distance. When omitted, uses the bus setting. Only valid for spatial playback.</param>
        /// <param name="rolloffMode">Optional built-in 3D distance curve: logarithmic or linear. When omitted, uses the bus setting. Only valid for spatial playback.</param>
        public AudioPlaybackOptions(
            bool loop = false,
            float? volume = null,
            float? pitch = null,
            float? minDistance = null,
            float? maxDistance = null,
            AudioRolloffMode? rolloffMode = null)
        {
            Loop = loop;
            Volume = volume;
            Pitch = pitch;
            MinDistance = minDistance;
            MaxDistance = maxDistance;
            RolloffMode = rolloffMode;
        }

        private bool HasSpatialOverrides => MinDistance.HasValue || MaxDistance.HasValue || RolloffMode.HasValue;

        internal bool TryResolve(AudioBusDefinition bus, bool isSpatial, out AudioPlaybackSettings settings, out string issue)
        {
            settings = default;
            issue = null;

            if (!isSpatial && HasSpatialOverrides)
            {
                issue = "3D distance and rolloff overrides require a fixed-position or followed-target playback request.";
                return false;
            }

            if (!TryResolveVolume(out float volume, out issue))
            {
                return false;
            }

            if (!TryResolvePitch(out float pitch, out issue))
            {
                return false;
            }

            if (!TryResolveSpatialSettings(
                    bus,
                    out float minDistance,
                    out float maxDistance,
                    out AudioRolloffMode rolloffMode,
                    out issue))
            {
                return false;
            }

            settings = new AudioPlaybackSettings(
                Loop,
                volume,
                pitch,
                minDistance,
                maxDistance,
                rolloffMode);

            return true;
        }

        private bool TryResolveVolume(out float volume, out string issue)
        {
            volume = Volume ?? 1f;
            issue = null;

            if (!AudioNumericUtility.IsFinite(volume) ||
                volume < 0f ||
                volume > 1f)
            {
                issue = "Playback volume must be finite and between 0 and 1.";
                return false;
            }

            return true;
        }

        private bool TryResolvePitch(out float pitch, out string issue)
        {
            pitch = Pitch ?? 1f;
            issue = null;

            if (!AudioNumericUtility.IsFinite(pitch) ||
                pitch < AudioPlaybackSettings.MinimumPitch ||
                pitch > AudioPlaybackSettings.MaximumPitch)
            {
                issue = $"Playback pitch must be finite and between {AudioPlaybackSettings.MinimumPitch} and {AudioPlaybackSettings.MaximumPitch}.";
                return false;
            }

            return true;
        }

        private bool TryResolveSpatialSettings(
            AudioBusDefinition bus,
            out float minDistance,
            out float maxDistance,
            out AudioRolloffMode rolloffMode,
            out string issue)
        {
            minDistance = MinDistance ?? bus.MinDistance;
            maxDistance = MaxDistance ?? bus.MaxDistance;
            rolloffMode = RolloffMode ?? bus.RolloffMode;
            issue = null;

            if (!AudioNumericUtility.IsFinite(minDistance) || minDistance < 0f ||
                !AudioNumericUtility.IsFinite(maxDistance) || maxDistance <= minDistance)
            {
                issue = "Playback 3D distances must be finite and non-negative, with maximum distance greater than minimum distance.";
                return false;
            }

            if (rolloffMode != AudioRolloffMode.Logarithmic && rolloffMode != AudioRolloffMode.Linear)
            {
                issue = "Playback rolloff must use Unity's built-in Logarithmic or Linear mode.";
                return false;
            }

            return true;
        }
    }

    internal readonly struct AudioPlaybackSettings
    {
        public const float MinimumPitch = 0.1f;
        public const float MaximumPitch = 3f;

        public bool Loop { get; }
        public float Volume { get; }
        public float Pitch { get; }
        public float MinDistance { get; }
        public float MaxDistance { get; }
        public AudioRolloffMode RolloffMode { get; }

        public AudioPlaybackSettings(bool loop, float volume, float pitch, float minDistance, float maxDistance, AudioRolloffMode rolloffMode)
        {
            Loop = loop;
            Volume = volume;
            Pitch = pitch;
            MinDistance = minDistance;
            MaxDistance = maxDistance;
            RolloffMode = rolloffMode;
        }
    }
}
