namespace Phillipe2AM.SoundSystem.Tests.Runtime.Reflection
{
    /// <summary>Names of private production fields read or assigned by package tests.</summary>
    public static class ReflectionFields
    {
        public static class AudioBusDefinition
        {
            public const string Id = "_id";
            public const string DefaultVolumeDb = "_defaultVolumeDb";
            public const string ExposedVolumeParameter = "_exposedVolumeParameter";
            public const string MaxDistance = "_maxDistance";
            public const string MinDistance = "_minDistance";
            public const string MixerGroup = "_mixerGroup";
        }

        public static class AudioSystemConfiguration
        {
            public const string Buses = "_buses";
            public const string FadeDurationSeconds = "_fadeDurationSeconds";
            public const string InitialPoolSize = "_initialPoolSize";
            public const string MaximumPoolSize = "_maximumPoolSize";
            public const string Mixer = "_mixer";
            public const string PausePlaybackOnSuspend = "_pausePlaybackOnApplicationSuspend";
            public const string PlaybackStartTimeoutSeconds = "_playbackStartTimeoutSeconds";
            public const string PersistVolumePreferences = "_persistVolumePreferences";
            public const string PreferenceKeyPrefix = "_preferenceKeyPrefix";
        }

        public static class AudioManager
        {
            public const string Configuration = "_configuration";
            public const string IsPlaybackPumpRunning = "_isPlaybackPumpRunning";
            public const string PersistAcrossScenes = "_persistAcrossScenes";
            public const string SourcePool = "_sourcePool";
        }

        public static class AudioSourcePool
        {
            public const string Playbacks = "_playbacks";
        }

        public static class AudioSourcePlayback
        {
            public const string FollowTarget = "_followTarget";
            public const string ScheduledStartTime = "_scheduledStartTime";
            public const string StartDeadline = "_startDeadline";
        }
    }
}
