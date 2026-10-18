using System;
using System.Collections.Generic;
using Phillipe2AM.SoundSystem.Configuration;
using UnityEngine;

namespace Phillipe2AM.SoundSystem.Playback
{
    /// <summary>Allocates bounded playback slots and routes control requests to matching slots.</summary>
    internal sealed class AudioSourcePool
    {
        private readonly Transform _owner;
        private readonly int _maximumSize;
        private readonly float _fadeDuration;
        private readonly float _playbackStartTimeoutSeconds;
        private readonly List<AudioSourcePlayback> _playbacks;
        private readonly List<AudioSourcePlayback> _fadingPlaybacks;
        private bool _hasLoggedCapacity;
        private bool _isApplicationSuspended;
        private long _nextPlaybackSequence;

        public event Action PlaybackPumpRequested;

        public bool HasPendingPlaybackWork
        {
            get
            {
                if (_isApplicationSuspended)
                {
                    return false;
                }
                if (_fadingPlaybacks.Count > 0)
                {
                    return true;
                }
                foreach (AudioSourcePlayback playback in _playbacks)
                {
                    if (playback.NeedsSpatialPositionUpdate || playback.NeedsLifecycleMonitoring)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public AudioSourcePool(
            Transform owner,
            int initialSize,
            int maximumSize,
            float fadeDuration,
            float playbackStartTimeoutSeconds)
        {
            _owner = owner;
            _maximumSize = Mathf.Max(1, maximumSize);
            _fadeDuration = AudioNumericUtility.IsFinite(fadeDuration) ? Mathf.Max(0f, fadeDuration) : 0f;
            _playbackStartTimeoutSeconds = AudioNumericUtility.IsFinite(playbackStartTimeoutSeconds)
                ? Mathf.Max(AudioSystemConfiguration.MinimumPlaybackStartTimeoutSeconds, playbackStartTimeoutSeconds)
                : AudioSystemConfiguration.DefaultPlaybackStartTimeoutSeconds;
            _playbacks = new List<AudioSourcePlayback>(_maximumSize);
            _fadingPlaybacks = new List<AudioSourcePlayback>(_maximumSize);

            int poolSize = Mathf.Clamp(initialSize, 0, _maximumSize);
            for (int i = 0; i < poolSize; i++)
            {
                CreatePlayback();
            }
        }

        private enum MatchType
        {
            Bus,
            Clip
        }

        private enum PlaybackAction
        {
            Stop,
            Pause,
            Resume
        }

        public bool Play(AudioClip clip, AudioBusDefinition bus, float delaySeconds, bool loop = false)
        {
            return Play(clip, bus, delaySeconds, GetPlaybackDefaults(bus, loop), out _);
        }

        public bool Play(
            AudioClip clip,
            AudioBusDefinition bus,
            float delaySeconds,
            AudioPlaybackSettings settings,
            out AudioSourcePlayback playback)
        {
            if (!TryAcquire(bus, settings.Loop, out playback))
            {
                return false;
            }

            playback.Play(
                clip,
                bus,
                delaySeconds,
                _playbackStartTimeoutSeconds,
                AudioSettings.dspTime,
                settings,
                ++_nextPlaybackSequence);
            CompleteAdmission(playback);
            return true;
        }

        public bool Play(
            AudioClip clip,
            AudioBusDefinition bus,
            Transform target,
            float delaySeconds,
            bool loop = false)
        {
            return target != null && AudioNumericUtility.IsFinite(target.position) &&
                Play(clip, bus, target, delaySeconds, GetPlaybackDefaults(bus, loop), out _);
        }

        public bool Play(
            AudioClip clip,
            AudioBusDefinition bus,
            Transform target,
            float delaySeconds,
            AudioPlaybackSettings settings,
            out AudioSourcePlayback playback)
        {
            if (target == null || !AudioNumericUtility.IsFinite(target.position) ||
                !TryAcquire(bus, settings.Loop, out playback))
            {
                playback = null;
                return false;
            }

            playback.PlayFollowing(
                clip,
                bus,
                target,
                delaySeconds,
                _playbackStartTimeoutSeconds,
                AudioSettings.dspTime,
                settings,
                ++_nextPlaybackSequence);
            CompleteAdmission(playback);
            return true;
        }

        public bool PlayAtPosition(
            AudioClip clip,
            AudioBusDefinition bus,
            Vector3 worldPosition,
            float delaySeconds,
            bool loop = false)
        {
            return PlayAtPosition(clip, bus, worldPosition, delaySeconds, GetPlaybackDefaults(bus, loop), out _);
        }

        public bool PlayAtPosition(
            AudioClip clip,
            AudioBusDefinition bus,
            Vector3 worldPosition,
            float delaySeconds,
            AudioPlaybackSettings settings,
            out AudioSourcePlayback playback)
        {
            if (!AudioNumericUtility.IsFinite(worldPosition) || !TryAcquire(bus, settings.Loop, out playback))
            {
                playback = null;
                return false;
            }

            playback.PlayAtPosition(
                clip,
                bus,
                worldPosition,
                delaySeconds,
                _playbackStartTimeoutSeconds,
                AudioSettings.dspTime,
                settings,
                ++_nextPlaybackSequence);
            CompleteAdmission(playback);
            return true;
        }

        /// <summary>Every accepted shape uses the same suspension and pump policy.</summary>
        private void CompleteAdmission(AudioSourcePlayback playback)
        {
            if (_isApplicationSuspended)
            {
                playback.PauseForApplication(AudioSettings.dspTime);
                return;
            }

            PlaybackPumpRequested?.Invoke();
        }

        public void TickPlaybackStates(double now)
        {
            if (_isApplicationSuspended)
            {
                return;
            }

            foreach (AudioSourcePlayback playback in _playbacks)
            {
                RefreshPlaybackState(playback, now);
            }
        }

        public void TickFades(double now)
        {
            if (_isApplicationSuspended)
            {
                return;
            }

            for (int i = _fadingPlaybacks.Count - 1; i >= 0; i--)
            {
                AudioSourcePlayback playback = _fadingPlaybacks[i];
                bool released = playback.TickFade(now);

                if (!playback.IsFading)
                {
                    _fadingPlaybacks.RemoveAt(i);
                }

                if (released)
                {
                    _hasLoggedCapacity = false;
                }
            }
        }

        public int StopBus(AudioBusDefinition bus, bool fade)
        {
            return ProcessMatching(bus, null, MatchType.Bus, PlaybackAction.Stop, fade);
        }

        public int StopClip(AudioClip clip, bool fade)
        {
            return clip != null
                ? ProcessMatching(null, clip, MatchType.Clip, PlaybackAction.Stop, fade)
                : 0;
        }

        public int PauseBus(AudioBusDefinition bus, bool fade)
        {
            return ProcessMatching(bus, null, MatchType.Bus, PlaybackAction.Pause, fade);
        }

        public int PauseClip(AudioClip clip, bool fade)
        {
            return clip != null
                ? ProcessMatching(null, clip, MatchType.Clip, PlaybackAction.Pause, fade)
                : 0;
        }

        public int ResumeBus(AudioBusDefinition bus, bool fade)
        {
            return ProcessMatching(bus, null, MatchType.Bus, PlaybackAction.Resume, fade);
        }

        public int ResumeClip(AudioClip clip, bool fade)
        {
            return clip != null
                ? ProcessMatching(null, clip, MatchType.Clip, PlaybackAction.Resume, fade)
                : 0;
        }

        public bool IsBusPlaying(AudioBusDefinition bus)
        {
            RefreshPlaybackStates();

            foreach (var playback in _playbacks)
            {
                if (playback.IsPlayingBus(bus))
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsClipPlaying(AudioClip clip)
        {
            RefreshPlaybackStates();

            foreach (var playback in _playbacks)
            {
                if (playback.IsPlayingClip(clip))
                {
                    return true;
                }
            }

            return false;
        }

        public AudioPlaybackState GetPlaybackState(int slotIndex, uint generation)
        {
            return TryGetPlayback(slotIndex, generation, out AudioSourcePlayback playback)
                ? playback.GetPublicState()
                : AudioPlaybackState.Invalid;
        }

        public bool StopPlayback(int slotIndex, uint generation, bool fade)
        {
            return ApplyHandleAction(slotIndex, generation, PlaybackAction.Stop, fade);
        }

        public bool PausePlayback(int slotIndex, uint generation, bool fade)
        {
            return ApplyHandleAction(slotIndex, generation, PlaybackAction.Pause, fade);
        }

        public bool ResumePlayback(int slotIndex, uint generation, bool fade)
        {
            return ApplyHandleAction(slotIndex, generation, PlaybackAction.Resume, fade);
        }

        public void PauseForApplication(double now)
        {
            if (_isApplicationSuspended)
            {
                return;
            }

            _isApplicationSuspended = true;
            foreach (AudioSourcePlayback playback in _playbacks)
            {
                playback.PauseForApplication(now);
            }
        }

        public void ResumeFromApplication(double now)
        {
            if (!_isApplicationSuspended)
            {
                return;
            }

            foreach (AudioSourcePlayback playback in _playbacks)
            {
                playback.ResumeFromApplication(now);
            }

            _isApplicationSuspended = false;
            if (HasPendingPlaybackWork)
            {
                PlaybackPumpRequested?.Invoke();
            }
        }

        public void Dispose()
        {
            foreach (AudioSourcePlayback playback in _playbacks)
            {
                playback.Dispose();
            }
            _playbacks.Clear();
            _fadingPlaybacks.Clear();
        }

        public void StopAll()
        {
            foreach (var playback in _playbacks)
            {
                playback.Release();
            }

            _fadingPlaybacks.Clear();
            _hasLoggedCapacity = false;
            _isApplicationSuspended = false;
        }

        private AudioSourcePlayback Acquire(AudioBusDefinition bus, bool loop)
        {
            AudioSourcePlayback availablePlayback = FindAvailablePlayback(AudioSettings.dspTime);
            if (availablePlayback != null)
            {
                return availablePlayback;
            }

            if (_playbacks.Count < _maximumSize)
            {
                return CreatePlayback();
            }

            return ReplaceOldestEligiblePlayback(bus, loop);
        }

        private AudioSourcePlayback FindAvailablePlayback(double now)
        {
            foreach (AudioSourcePlayback playback in _playbacks)
            {
                RefreshPlaybackForAcquisition(playback, now);
                if (!playback.IsActive)
                {
                    return playback;
                }
            }

            return null;
        }

        private void RefreshPlaybackForAcquisition(AudioSourcePlayback playback, double now)
        {
            bool wasActive = playback.IsActive;
            bool wasFading = playback.IsFading;
            playback.EnsureSource();

            if (wasFading && !playback.IsFading)
            {
                _fadingPlaybacks.Remove(playback);
            }

            RefreshPlaybackState(playback, now);
            if (wasActive && !playback.IsActive)
            {
                _hasLoggedCapacity = false;
            }
        }

        private AudioSourcePlayback ReplaceOldestEligiblePlayback(AudioBusDefinition bus, bool loop)
        {
            AudioSourcePlayback oldestEligiblePlayback = FindOldestEligiblePlayback(bus, loop);
            if (oldestEligiblePlayback == null)
            {
                return null;
            }

            _fadingPlaybacks.Remove(oldestEligiblePlayback);
            oldestEligiblePlayback.Release();
            _hasLoggedCapacity = false;
            return oldestEligiblePlayback;
        }

        private AudioSourcePlayback FindOldestEligiblePlayback(AudioBusDefinition bus, bool loop)
        {
            AudioSourcePlayback oldestEligiblePlayback = null;

            foreach (AudioSourcePlayback playback in _playbacks)
            {
                if (!IsEligibleForReplacement(playback, bus, loop))
                {
                    continue;
                }

                if (oldestEligiblePlayback == null || playback.Sequence < oldestEligiblePlayback.Sequence)
                {
                    oldestEligiblePlayback = playback;
                }
            }

            return oldestEligiblePlayback;
        }

        private static bool IsEligibleForReplacement(AudioSourcePlayback playback, AudioBusDefinition bus, bool loop)
        {
            return playback.IsActive && playback.Bus == bus && playback.IsLooping == loop;
        }

        private bool TryAcquire(AudioBusDefinition bus, bool loop, out AudioSourcePlayback playback)
        {
            playback = Acquire(bus, loop);
            if (playback != null)
            {
                return true;
            }

            if (!_hasLoggedCapacity)
            {
                Debug.LogWarning(
                    "AudioSource pool reached its limit (" + _maximumSize + ") and has no active playback on bus '" +
                    (bus != null ? bus.name : "<missing>") + "' with the requested loop category. The request was skipped.",
                    _owner);
                _hasLoggedCapacity = true;
            }

            return false;
        }

        private bool TryGetPlayback(int slotIndex, uint generation, out AudioSourcePlayback playback)
        {
            playback = null;
            if (slotIndex < 0 || slotIndex >= _playbacks.Count || generation == 0)
            {
                return false;
            }

            AudioSourcePlayback candidate = _playbacks[slotIndex];
            RefreshPlaybackState(candidate, AudioSettings.dspTime);
            if (!candidate.IsActive || candidate.Generation != generation)
            {
                return false;
            }

            playback = candidate;
            return true;
        }

        private bool ApplyHandleAction(int slotIndex, uint generation, PlaybackAction action, bool fade)
        {
            if (!TryGetPlayback(slotIndex, generation, out AudioSourcePlayback playback))
            {
                return false;
            }

            bool wasFading = playback.IsFading;
            bool applied = TryApplyAction(playback, action, fade, AudioSettings.dspTime, out bool released);
            if (!applied)
            {
                return false;
            }

            TrackAppliedAction(playback, action, wasFading, released);
            return true;
        }

        private static AudioPlaybackSettings GetPlaybackDefaults(AudioBusDefinition bus, bool loop)
        {
            return new AudioPlaybackSettings(loop, 1f, 1f, bus.MinDistance, bus.MaxDistance, bus.RolloffMode);
        }

        private AudioSourcePlayback CreatePlayback()
        {
            AudioSourcePlayback playback = new AudioSourcePlayback(_owner, _playbacks.Count);
            _playbacks.Add(playback);
            return playback;
        }

        private int ProcessMatching(
            AudioBusDefinition bus,
            AudioClip clip,
            MatchType matchType,
            PlaybackAction action,
            bool fade)
        {
            int affected = 0;
            double now = AudioSettings.dspTime;

            foreach (AudioSourcePlayback playback in _playbacks)
            {
                if (ProcessMatchingPlayback(playback, bus, clip, matchType, action, fade, now))
                {
                    affected++;
                }
            }

            return affected;
        }

        private bool ProcessMatchingPlayback(
            AudioSourcePlayback playback,
            AudioBusDefinition bus,
            AudioClip clip,
            MatchType matchType,
            PlaybackAction action,
            bool fade,
            double now)
        {
            RefreshPlaybackState(playback, now);

            if (!playback.IsActive || playback.Source == null)
            {
                return false;
            }

            if (!Matches(playback, bus, clip, matchType))
            {
                return false;
            }

            bool wasFading = playback.IsFading;
            if (!TryApplyAction(playback, action, fade, now, out bool released))
            {
                return false;
            }

            TrackAppliedAction(playback, action, wasFading, released);
            return true;
        }

        private void TrackAppliedAction(
            AudioSourcePlayback playback,
            PlaybackAction action,
            bool wasFading,
            bool released)
        {
            if (released)
            {
                _hasLoggedCapacity = false;
            }

            UpdateFadeTracking(playback, wasFading);

            if (action == PlaybackAction.Resume && !playback.IsFading)
            {
                PlaybackPumpRequested?.Invoke();
            }
        }

        private void RefreshPlaybackStates()
        {
            TickPlaybackStates(AudioSettings.dspTime);
        }

        private void RefreshPlaybackState(AudioSourcePlayback playback, double now)
        {
            if (playback.RefreshState(now))
            {
                _fadingPlaybacks.Remove(playback);
                _hasLoggedCapacity = false;
            }
        }

        private bool TryApplyAction(
            AudioSourcePlayback playback,
            PlaybackAction action,
            bool fade,
            double now,
            out bool released)
        {
            switch (action)
            {
                case PlaybackAction.Stop:
                    released = playback.Stop(fade, _fadeDuration, now);
                    return true;
                case PlaybackAction.Pause when playback.CanPause:
                    released = playback.Pause(fade, _fadeDuration, now);
                    return true;
                case PlaybackAction.Resume when playback.CanResume:
                    released = playback.Resume(fade, _fadeDuration, now);
                    return true;
                default:
                    released = false;
                    return false;
            }
        }

        private void UpdateFadeTracking(AudioSourcePlayback playback, bool wasFading)
        {
            if (wasFading == playback.IsFading)
            {
                return;
            }

            if (playback.IsFading)
            {
                _fadingPlaybacks.Add(playback);
                PlaybackPumpRequested?.Invoke();
                return;
            }

            _fadingPlaybacks.Remove(playback);
        }

        private static bool Matches(
            AudioSourcePlayback playback,
            AudioBusDefinition bus,
            AudioClip clip,
            MatchType matchType)
        {
            return matchType == MatchType.Bus
                ? playback.Bus == bus
                : playback.Source.clip == clip;
        }
    }
}
