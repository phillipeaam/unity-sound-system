namespace Phillipe2AM.SoundSystem.Configuration
{
    internal static class AudioNumericUtility
    {
        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool IsFinite(UnityEngine.Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        public static float ClampVolumeDb(float value, float fallback = 0f)
        {
            return IsFinite(value)
                ? UnityEngine.Mathf.Clamp(
                    value,
                    AudioSystemConfiguration.MinimumVolumeDb,
                    AudioSystemConfiguration.MaximumVolumeDb)
                : UnityEngine.Mathf.Clamp(
                    fallback,
                    AudioSystemConfiguration.MinimumVolumeDb,
                    AudioSystemConfiguration.MaximumVolumeDb);
        }
    }
}
