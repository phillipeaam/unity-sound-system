namespace Phillipe2AM.SoundSystem.Playback
{
    /// <summary>Lifecycle state reported by an <see cref="AudioPlaybackHandle"/>.</summary>
    public enum AudioPlaybackState
    {
        /// <summary>The handle is default, stale, or no longer refers to an active playback.</summary>
        Invalid,

        /// <summary>The request was accepted and is waiting for its scheduled start time.</summary>
        Scheduled,

        /// <summary>The scheduled start time has arrived, but the audio source has not reported playback yet.</summary>
        Starting,

        /// <summary>The audio source is playing the clip.</summary>
        Playing,

        /// <summary>The playback is paused through the manager or the application is suspended; it resumes through the manager or when application suspension ends.</summary>
        Paused,

        /// <summary>The playback is fading out before it pauses.</summary>
        FadingToPause,

        /// <summary>The playback is fading in after being resumed.</summary>
        FadingToResume,

        /// <summary>The playback is fading out before it stops and releases its pool slot.</summary>
        FadingToStop
    }
}
