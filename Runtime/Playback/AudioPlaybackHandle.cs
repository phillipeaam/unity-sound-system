using System;

namespace Phillipe2AM.SoundSystem.Playback
{
    /// <summary>
    /// Identifies and controls one playback started by <see cref="AudioManager"/>.
    /// The handle refers to that playback generation and never exposes the pooled <see cref="UnityEngine.AudioSource"/>.
    /// It becomes invalid when playback ends, its pool slot is reused, or its manager is destroyed.
    /// </summary>
    public readonly struct AudioPlaybackHandle : IEquatable<AudioPlaybackHandle>
    {
        private readonly AudioManager _manager;
        private readonly int _slotIndex;
        private readonly uint _generation;

        internal AudioPlaybackHandle(AudioManager manager, int slotIndex, uint generation)
        {
            _manager = manager;
            _slotIndex = slotIndex;
            _generation = generation;
        }

        /// <summary>Gets the current lifecycle state, or <see cref="AudioPlaybackState.Invalid"/> when this handle no longer identifies an active playback.</summary>
        public AudioPlaybackState State => _manager != null
            ? _manager.GetPlaybackState(_slotIndex, _generation)
            : AudioPlaybackState.Invalid;

        /// <summary>Gets whether this handle still identifies an active playback.</summary>
        public bool IsValid => State != AudioPlaybackState.Invalid;

        /// <summary>Gets whether this playback is scheduled, starting, playing, or transitioning through a fade.</summary>
        public bool IsPlaying
        {
            get
            {
                AudioPlaybackState state = State;
                return state
                    is AudioPlaybackState.Scheduled
                    or AudioPlaybackState.Starting
                    or AudioPlaybackState.Playing
                    or AudioPlaybackState.FadingToPause
                    or AudioPlaybackState.FadingToResume
                    or AudioPlaybackState.FadingToStop;
            }
        }

        /// <summary>Stops this playback and invalidates the handle after the playback is released.</summary>
        /// <param name="fade">When <see langword="true"/>, fades over the configured duration; when <see langword="false"/>, stops immediately.</param>
        /// <returns><see langword="true"/> when the stop request was applied to this active playback; otherwise <see langword="false"/>.</returns>
        public bool Stop(bool fade = true)
        {
            return _manager != null && _manager.StopPlayback(_slotIndex, _generation, fade);
        }

        /// <summary>Pauses this playback while retaining its occupied pool slot for resuming. A scheduled sound retains its remaining delay.</summary>
        /// <param name="fade">When <see langword="true"/>, fades over the configured duration before pausing; when <see langword="false"/>, pauses immediately.</param>
        /// <returns><see langword="true"/> when the pause request was applied; otherwise <see langword="false"/>, for example when already paused or invalid.</returns>
        public bool Pause(bool fade = true)
        {
            return _manager != null && _manager.PausePlayback(_slotIndex, _generation, fade);
        }

        /// <summary>Resumes this playback after a caller pause, or reverses an in-progress pause fade. Scheduled playback waits its remaining delay.</summary>
        /// <param name="fade">When <see langword="true"/>, fades in over the configured duration; when <see langword="false"/>, resumes immediately.</param>
        /// <returns><see langword="true"/> when the resume request was applied; otherwise <see langword="false"/>, for example when it is not paused or the handle is invalid.</returns>
        public bool Resume(bool fade = true)
        {
            return _manager != null && _manager.ResumePlayback(_slotIndex, _generation, fade);
        }

        /// <summary>Checks whether another handle identifies the same manager, slot, and playback generation.</summary>
        /// <param name="other">The handle to compare with this handle.</param>
        /// <returns><see langword="true"/> when both handles identify the same playback; otherwise <see langword="false"/>.</returns>
        public bool Equals(AudioPlaybackHandle other)
        {
            return ReferenceEquals(_manager, other._manager) && _slotIndex == other._slotIndex && _generation == other._generation;
        }

        /// <summary>Checks whether an object is an equal <see cref="AudioPlaybackHandle"/>.</summary>
        /// <param name="obj">The object to compare with this handle.</param>
        /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal handle; otherwise <see langword="false"/>.</returns>
        public override bool Equals(object obj)
        {
            return obj is AudioPlaybackHandle other && Equals(other);
        }

        /// <summary>Returns a hash code based on the manager, slot, and playback generation.</summary>
        /// <returns>A hash code for this handle.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = !ReferenceEquals(_manager, null) ? _manager.GetHashCode() : 0;
                hash = (hash * 397) ^ _slotIndex;
                hash = (hash * 397) ^ (int)_generation;
                return hash;
            }
        }

        /// <summary>Checks whether two handles identify the same playback.</summary>
        /// <param name="left">The first handle.</param>
        /// <param name="right">The second handle.</param>
        /// <returns><see langword="true"/> when the handles identify the same playback; otherwise <see langword="false"/>.</returns>
        public static bool operator ==(AudioPlaybackHandle left, AudioPlaybackHandle right)
        {
            return left.Equals(right);
        }

        /// <summary>Checks whether two handles identify different playbacks.</summary>
        /// <param name="left">The first handle.</param>
        /// <param name="right">The second handle.</param>
        /// <returns><see langword="true"/> when the handles identify different playbacks; otherwise <see langword="false"/>.</returns>
        public static bool operator !=(AudioPlaybackHandle left, AudioPlaybackHandle right)
        {
            return !left.Equals(right);
        }
    }
}
