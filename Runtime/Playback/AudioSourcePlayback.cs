using Phillipe2AM.SoundSystem.Configuration;
using UnityEngine;

namespace Phillipe2AM.SoundSystem.Playback
{
    /// <summary>Owns the runtime state and fade lifecycle of one pooled AudioSource.</summary>
    internal sealed class AudioSourcePlayback
    {
        private const double StartGraceSeconds = 0.25d;
        private const double EndGraceSeconds = 0.25d;
        private readonly Transform _owner;
        private readonly int _index;
        private Transform _sourceTransform;
        // Caller pause and application suspension are independent causes. Returning from the
        // application must never clear IsPaused; a held schedule restarts only after both clear.
        private bool _schedulePaused;
        private double _callerPauseStartTime;
        private bool _isScheduled;
        private bool _hasStarted;
        private bool _applicationIsSuspended;
        // All lifecycle/fade timestamps use DSP time. AudioListener.pause freezes that clock;
        // source-level caller/app pauses require explicit deadline rebasing without double counting.
        private double _scheduledStartTime;
        private double _startDeadline;
        private double _expectedEndTime;
        private double _graceUntil;
        private double _applicationPauseStartTime;
        private double _remainingScheduledDelay;
        private float _startTimeoutSeconds;
        private double _fadeStartTime;
        private double _fadeDuration;
        // Requested gain stays stable across interrupted fades; current Source.volume is transient.
        private float _baseVolume;
        private float _fadeStartVolume;
        private float _fadeTargetVolume;
        private FadeAction _fade;
        private SpatialPlaybackMode _spatialMode;
        private Transform _followTarget;
        private Vector3 _lastSpatialPosition;
        private bool _hasLoggedFollowPositionWarning;

        public AudioSource Source { get; private set; }

        public int SlotIndex => _index;

        public uint Generation { get; private set; }

        public long Sequence { get; private set; }

        public AudioBusDefinition Bus { get; private set; }

        public bool IsActive { get; private set; }

        public bool IsPaused { get; private set; }

        public bool IsFading { get; private set; }

        public bool IsLooping { get; private set; }

        public bool NeedsSpatialPositionUpdate => IsActive && _spatialMode != SpatialPlaybackMode.TwoDimensional;

        public bool CanPause => !IsPaused && (!IsFading || _fade != FadeAction.Pause);

        public bool CanResume => IsPaused || (IsFading && _fade == FadeAction.Pause);

        public bool NeedsLifecycleMonitoring =>
            IsActive &&
            (Source == null ||
             (!IsPaused && !_applicationIsSuspended && !IsFading &&
              (_isScheduled || !_hasStarted || !IsLooping)));

        public AudioSourcePlayback(Transform owner, int index)
        {
            _owner = owner;
            _index = index;
            Source = CreateAudioSource();
        }

        private enum FadeAction
        {
            None,
            Stop,
            Pause,
            Resume
        }

        private enum SpatialPlaybackMode
        {
            TwoDimensional,
            FixedPosition,
            FollowingTarget
        }

        /// <summary>Recreates a source if it was destroyed outside the pool.</summary>
        public bool EnsureSource()
        {
            if (Source != null)
            {
                return false;
            }

            Source = CreateAudioSource();
            ResetState();
            return true;
        }

        public void Play(
            AudioClip clip,
            AudioBusDefinition bus,
            float delaySeconds,
            float startTimeoutSeconds,
            double now,
            bool loop = false)
        {
            StartPlayback(
                clip,
                bus,
                delaySeconds,
                startTimeoutSeconds,
                now,
                SpatialPlaybackMode.TwoDimensional,
                null,
                Vector3.zero,
                new AudioPlaybackSettings(loop, 1f, 1f, bus.MinDistance, bus.MaxDistance, bus.RolloffMode),
                Sequence + 1);
        }

        public void PlayAtPosition(
            AudioClip clip,
            AudioBusDefinition bus,
            Vector3 worldPosition,
            float delaySeconds,
            float startTimeoutSeconds,
            double now,
            bool loop = false)
        {
            StartPlayback(
                clip,
                bus,
                delaySeconds,
                startTimeoutSeconds,
                now,
                SpatialPlaybackMode.FixedPosition,
                null,
                worldPosition,
                new AudioPlaybackSettings(loop, 1f, 1f, bus.MinDistance, bus.MaxDistance, bus.RolloffMode),
                Sequence + 1);
        }

        public void PlayFollowing(
            AudioClip clip,
            AudioBusDefinition bus,
            Transform target,
            float delaySeconds,
            float startTimeoutSeconds,
            double now,
            bool loop = false)
        {
            StartPlayback(
                clip,
                bus,
                delaySeconds,
                startTimeoutSeconds,
                now,
                SpatialPlaybackMode.FollowingTarget,
                target,
                target.position,
                new AudioPlaybackSettings(loop, 1f, 1f, bus.MinDistance, bus.MaxDistance, bus.RolloffMode),
                Sequence + 1);
        }

        public void Play(
            AudioClip clip,
            AudioBusDefinition bus,
            float delaySeconds,
            float startTimeoutSeconds,
            double now,
            AudioPlaybackSettings settings,
            long sequence)
        {
            StartPlayback(clip, bus, delaySeconds, startTimeoutSeconds, now,
                SpatialPlaybackMode.TwoDimensional, null, Vector3.zero, settings, sequence);
        }

        public void PlayAtPosition(
            AudioClip clip,
            AudioBusDefinition bus,
            Vector3 worldPosition,
            float delaySeconds,
            float startTimeoutSeconds,
            double now,
            AudioPlaybackSettings settings,
            long sequence)
        {
            StartPlayback(clip, bus, delaySeconds, startTimeoutSeconds, now,
                SpatialPlaybackMode.FixedPosition, null, worldPosition, settings, sequence);
        }

        public void PlayFollowing(
            AudioClip clip,
            AudioBusDefinition bus,
            Transform target,
            float delaySeconds,
            float startTimeoutSeconds,
            double now,
            AudioPlaybackSettings settings,
            long sequence)
        {
            StartPlayback(clip, bus, delaySeconds, startTimeoutSeconds, now,
                SpatialPlaybackMode.FollowingTarget, target, target.position, settings, sequence);
        }

        public AudioPlaybackState GetPublicState()
        {
            if (!IsActive || Source == null)
            {
                return AudioPlaybackState.Invalid;
            }

            if (IsPaused || _applicationIsSuspended)
            {
                return AudioPlaybackState.Paused;
            }

            if (_isScheduled)
            {
                return AudioPlaybackState.Scheduled;
            }

            if (IsFading)
            {
                return _fade switch
                {
                    FadeAction.Stop => AudioPlaybackState.FadingToStop,
                    FadeAction.Pause => AudioPlaybackState.FadingToPause,
                    FadeAction.Resume => AudioPlaybackState.FadingToResume,
                    _ => AudioPlaybackState.Starting
                };
            }

            return _hasStarted || Source.isPlaying
                ? AudioPlaybackState.Playing
                : AudioPlaybackState.Starting;
        }

        public void PauseForApplication(double now)
        {
            if (!IsActive || Source == null || _applicationIsSuspended)
            {
                return;
            }

            _applicationIsSuspended = true;
            _applicationPauseStartTime = now;
            if (IsPaused)
            {
                return;
            }

            if (_isScheduled)
            {
                HoldScheduledPlayback(now);
                return;
            }

            Source.Pause();
        }

        public void ResumeFromApplication(double now)
        {
            if (!IsActive || Source == null || !_applicationIsSuspended)
            {
                return;
            }

            RefreshFollowTargetPosition();
            double suspendedDuration = System.Math.Max(0d, now - _applicationPauseStartTime);
            _applicationIsSuspended = false;
            _startDeadline += suspendedDuration;
            _expectedEndTime += suspendedDuration;
            _graceUntil += suspendedDuration;
            if (IsFading)
            {
                _fadeStartTime += System.Math.Max(0d, now - System.Math.Max(_applicationPauseStartTime, _fadeStartTime));
            }

            if (IsPaused)
            {
                _callerPauseStartTime += System.Math.Max(0d, now - System.Math.Max(_applicationPauseStartTime, _callerPauseStartTime));
                return;
            }

            if (_schedulePaused)
            {
                ResumeScheduledPlayback(now);
                return;
            }

            Source.UnPause();
        }

        private void HoldScheduledPlayback(double now)
        {
            _remainingScheduledDelay = System.Math.Max(0d, _scheduledStartTime - now);
            Source.Stop();
            _isScheduled = false;
            _schedulePaused = true;
            _hasStarted = false;
        }

        private void ResumeScheduledPlayback(double now)
        {
            _scheduledStartTime = now + _remainingScheduledDelay;
            _startDeadline = _scheduledStartTime + _startTimeoutSeconds;
            _expectedEndTime = _scheduledStartTime + (Source.clip != null ? Source.clip.length / Source.pitch : 0d) + EndGraceSeconds;
            _graceUntil = _scheduledStartTime + StartGraceSeconds;
            _isScheduled = true;
            _schedulePaused = false;
            _hasStarted = false;
            _remainingScheduledDelay = 0d;
            Source.PlayScheduled(_scheduledStartTime);
        }

        private void StartPlayback(
            AudioClip clip,
            AudioBusDefinition bus,
            float delaySeconds,
            float startTimeoutSeconds,
            double now,
            SpatialPlaybackMode spatialMode,
            Transform followTarget,
            Vector3 worldPosition,
            AudioPlaybackSettings settings,
            long sequence)
        {
            EnsureSource();

            unchecked
            {
                Generation++;
                if (Generation == 0)
                {
                    Generation = 1;
                }
            }

            Sequence = sequence;

            // The manager owns each reusable source; a transient target never owns the slot.
            ResetTransformToOwner();
            if (spatialMode != SpatialPlaybackMode.TwoDimensional)
            {
                _sourceTransform.position = worldPosition;
            }

            double delay = System.Math.Max(0d, delaySeconds);
            Source.clip = clip;
            Source.outputAudioMixerGroup = bus.MixerGroup;
            Source.loop = settings.Loop;
            Source.volume = settings.Volume;
            Source.pitch = settings.Pitch;
            Source.spatialBlend = spatialMode == SpatialPlaybackMode.TwoDimensional ? 0f : 1f;
            Source.minDistance = settings.MinDistance;
            Source.maxDistance = settings.MaxDistance;
            Source.rolloffMode = settings.RolloffMode;

            Bus = bus;
            IsActive = true;
            IsPaused = false;
            IsFading = false;
            IsLooping = settings.Loop;
            _applicationIsSuspended = false;
            _schedulePaused = false;
            _isScheduled = delay > 0d;
            _hasStarted = false;
            _scheduledStartTime = now + delay;
            _startTimeoutSeconds = startTimeoutSeconds;
            _startDeadline = _scheduledStartTime + startTimeoutSeconds;
            _expectedEndTime = _scheduledStartTime + clip.length / settings.Pitch + EndGraceSeconds;
            _graceUntil = _scheduledStartTime + StartGraceSeconds;
            _baseVolume = Source.volume;
            _fade = FadeAction.None;
            _spatialMode = spatialMode;
            _followTarget = followTarget;
            _lastSpatialPosition = worldPosition;
            _hasLoggedFollowPositionWarning = false;

            if (_isScheduled)
            {
                Source.PlayScheduled(_scheduledStartTime);
            }
            else
            {
                Source.Play();
            }
        }

        /// <summary>Reconciles natural playback completion and startup deadlines.</summary>
        /// <returns>True when this call released the playback slot.</returns>
        public bool RefreshState(double now)
        {
            if (!IsActive)
            {
                return false;
            }

            if (Source == null)
            {
                return Release();
            }

            RefreshFollowTargetPosition();

            if (ShouldDeferStateRefresh())
            {
                return false;
            }

            if (_isScheduled && now < _scheduledStartTime)
            {
                return false;
            }

            if (Source.isPlaying)
            {
                _isScheduled = false;
                _hasStarted = true;
                return false;
            }

            // A windowed Player stops its sources before dispatching every pause callback.
            // Another component may query a handle before AudioManager receives that callback.
            // Keep the slot and schedule intact until the manager handles suspension/return.
            if (IsPausedByWindowFocus())
            {
                return false;
            }

            _isScheduled = false;
            if (_hasStarted)
            {
                return ReleaseCompletedPlaybackAfterGrace(now);
            }

            return ReleaseIfPlaybackNeverStarted(now);
        }

        private static bool IsPausedByWindowFocus()
        {
#if UNITY_STANDALONE && !UNITY_EDITOR
            return !Application.runInBackground && !Application.isFocused;
#else
            return false;
#endif
        }

        private bool ShouldDeferStateRefresh()
        {
            if (IsPaused)
            {
                return true;
            }

            if (_applicationIsSuspended)
            {
                return true;
            }

            // Fades change gain, not clip lifetime. A one-shot can finish before its fade does.
            return AudioListener.pause;
        }

        private bool ReleaseIfPlaybackNeverStarted(double now)
        {
            if (HasMissedStartDeadline(now))
            {
                LogStartTimeout();
                return Release();
            }

            if (now < _expectedEndTime)
            {
                return false;
            }

            return Release();
        }

        private bool HasMissedStartDeadline(double now)
        {
            if (now < _startDeadline)
            {
                return false;
            }

            if (Source.loop)
            {
                return true;
            }

            return _startDeadline <= _expectedEndTime;
        }

        private void LogStartTimeout()
        {
            string clipName = Source.clip != null ? Source.clip.name : "<missing clip>";
            string busName = Bus != null ? Bus.name : "<missing bus>";
            Debug.LogWarning(
                $"Audio playback for clip '{clipName}' on bus '{busName}' did not start within its {(_startDeadline - _scheduledStartTime):0.##}-second timeout. Its pool slot was released. Check the clip's load state or increase Playback Start Timeout in the Audio System Configuration.",
                _owner);
        }

        private bool ReleaseCompletedPlaybackAfterGrace(double now)
        {
            if (now < _graceUntil)
            {
                return false;
            }

            return Release();
        }

        /// <returns>True when this fade tick released the playback slot.</returns>
        public bool TickFade(double now)
        {
            if (!IsFading)
            {
                return false;
            }

            if (_applicationIsSuspended || AudioListener.pause)
            {
                return false;
            }

            if (!IsActive || Source == null)
            {
                return Release();
            }

            return UpdateFade(now);
        }

        public bool IsPlayingBus(AudioBusDefinition bus)
        {
            return IsActive && !IsPaused && !_applicationIsSuspended && Source != null && Bus == bus;
        }

        public bool IsPlayingClip(AudioClip clip)
        {
            return clip != null && IsActive && !IsPaused && !_applicationIsSuspended &&
                Source != null && Source.clip == clip;
        }

        public bool Stop(bool fade, float fadeDuration, double now)
        {
            return BeginFade(FadeAction.Stop, fade, fadeDuration, now);
        }

        public bool Pause(bool fade, float fadeDuration, double now)
        {
            return BeginFade(FadeAction.Pause, fade, fadeDuration, now);
        }

        public bool Resume(bool fade, float fadeDuration, double now)
        {
            return BeginFade(FadeAction.Resume, fade, fadeDuration, now);
        }

        /// <returns>True when this call released the playback slot.</returns>
        public bool Release()
        {
            bool wasActive = IsActive;

            if (Source != null)
            {
                Source.Stop();
                Source.clip = null;
                Source.outputAudioMixerGroup = null;
                Source.loop = false;
                Source.mute = false;
                Source.volume = 1f;
                Source.pitch = 1f;
                Source.spatialBlend = 0f;
                Source.minDistance = 1f;
                Source.maxDistance = 500f;
                Source.rolloffMode = AudioRolloffMode.Logarithmic;
                Source.enabled = true;
                ResetTransformToOwner();
            }

            ResetState();
            return wasActive;
        }

        private AudioSource CreateAudioSource()
        {
            if (_sourceTransform == null)
            {
                GameObject sourceObject = new GameObject($"PooledAudioSource_{_index + 1:00}");
                _sourceTransform = sourceObject.transform;
                _sourceTransform.SetParent(_owner, false);
            }

            AudioSource source = _sourceTransform.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.loop = false;
            source.volume = 1f;
            source.minDistance = 1f;
            source.maxDistance = 500f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            return source;
        }

        /// <summary>Removes the slot object even when only its manager component is removed.</summary>
        public void Dispose()
        {
            Release();
            if (_sourceTransform != null)
            {
                Object.Destroy(_sourceTransform.gameObject);
            }
        }

        private void RefreshFollowTargetPosition()
        {
            if (_spatialMode == SpatialPlaybackMode.TwoDimensional || _sourceTransform == null)
            {
                return;
            }

            if (_spatialMode == SpatialPlaybackMode.FollowingTarget)
            {
                UpdateFollowPosition();
            }

            if (_sourceTransform.position != _lastSpatialPosition)
            {
                _sourceTransform.position = _lastSpatialPosition;
            }
        }

        private void UpdateFollowPosition()
        {
            if (_followTarget == null)
            {
                _followTarget = null;
                _spatialMode = SpatialPlaybackMode.FixedPosition;
                return;
            }

            Vector3 targetPosition = _followTarget.position;
            if (AudioNumericUtility.IsFinite(targetPosition))
            {
                _lastSpatialPosition = targetPosition;
                return;
            }

            if (!_hasLoggedFollowPositionWarning)
            {
                Debug.LogWarning(
                    "Audio playback stopped following a target with a non-finite world position and kept its last valid position.",
                    _owner);
                _hasLoggedFollowPositionWarning = true;
            }

            _followTarget = null;
            _spatialMode = SpatialPlaybackMode.FixedPosition;
        }

        private void ResetTransformToOwner()
        {
            if (_sourceTransform == null)
            {
                return;
            }

            if (_sourceTransform.parent != _owner)
            {
                _sourceTransform.SetParent(_owner, false);
            }
            _sourceTransform.localPosition = Vector3.zero;
            _sourceTransform.localRotation = Quaternion.identity;
            _sourceTransform.localScale = Vector3.one;
        }

        private bool UpdateFade(double now)
        {
            if (!IsFading)
            {
                return false;
            }

            float progress = Mathf.Clamp01((float)((now - _fadeStartTime) / _fadeDuration));
            Source.volume = Mathf.Lerp(_fadeStartVolume, _fadeTargetVolume, progress);

            return progress >= 1f && CompleteFade(now);
        }

        private bool BeginFade(FadeAction action, bool fade, float configuredFadeDuration, double now)
        {
            switch (action)
            {
                case FadeAction.Pause when !CanPause:
                case FadeAction.Resume when !CanResume:
                    return false;
                // Tick skips paused sources, so a stop request must release a paused slot directly.
                case FadeAction.Stop when IsPaused || _applicationIsSuspended || _isScheduled || _schedulePaused:
                    return Release();
                case FadeAction.None:
                default:
                    break;
            }

            if (TryControlHeldSchedule(action, now))
            {
                return false;
            }

            float duration = fade && !_applicationIsSuspended ? configuredFadeDuration : 0f;
            if (action == FadeAction.Resume)
            {
                ResumeCallerPlayback(duration, now);
            }

            IsFading = duration > 0f;
            _fade = action;

            if (!IsFading)
            {
                return CompleteFade(now);
            }

            _fadeStartTime = now;
            _fadeDuration = duration;
            _fadeStartVolume = Source.volume;
            _fadeTargetVolume = action == FadeAction.Resume ? _baseVolume : 0f;
            return false;
        }

        /// <summary>Restores caller playback without overriding an application-owned suspension.</summary>
        private void ResumeCallerPlayback(float duration, double now)
        {
            RefreshFollowTargetPosition();
            if (!_applicationIsSuspended)
            {
                Source.UnPause();
            }
            if (IsPaused)
            {
                double pauseEnd = _applicationIsSuspended ? _applicationPauseStartTime : now;
                double pausedDuration = System.Math.Max(0d, pauseEnd - _callerPauseStartTime);
                _startDeadline += pausedDuration;
                _expectedEndTime += pausedDuration;
                Source.volume = duration > 0f ? 0f : _baseVolume;
            }
            else if (duration <= 0f)
            {
                Source.volume = _baseVolume;
            }
            IsPaused = false;
            _graceUntil = (_applicationIsSuspended ? _applicationPauseStartTime : now) + StartGraceSeconds;
        }

        private bool TryControlHeldSchedule(FadeAction action, double now)
        {
            if (action != FadeAction.Pause && action != FadeAction.Resume)
            {
                return false;
            }
            if (!_isScheduled && !_schedulePaused)
            {
                return false;
            }

            // There is no audible signal to fade before a scheduled sound starts.
            if (action == FadeAction.Pause)
            {
                if (_isScheduled)
                {
                    HoldScheduledPlayback(now);
                }
                IsPaused = true;
                _callerPauseStartTime = now;
            }
            else
            {
                IsPaused = false;
                if (!_applicationIsSuspended)
                {
                    ResumeScheduledPlayback(now);
                }
            }
            IsFading = false;
            _fade = FadeAction.None;
            Source.volume = _baseVolume;
            return true;
        }

        private bool CompleteFade(double now)
        {
            IsFading = false;
            bool released = false;

            switch (_fade)
            {
                case FadeAction.Stop:
                    released = Release();
                    break;
                case FadeAction.Pause:
                    Source.Pause();
                    Source.volume = _baseVolume;
                    IsPaused = true;
                    _callerPauseStartTime = now;
                    break;
                case FadeAction.Resume:
                    Source.volume = _baseVolume;
                    _graceUntil = (_applicationIsSuspended ? _applicationPauseStartTime : now) + StartGraceSeconds;
                    break;
                case FadeAction.None:
                default:
                    break;
            }

            _fade = FadeAction.None;
            return released;
        }

        private void ResetState()
        {
            Bus = null;
            IsActive = false;
            IsPaused = false;
            IsLooping = false;
            _applicationIsSuspended = false;
            _isScheduled = false;
            _hasStarted = false;
            IsFading = false;
            _scheduledStartTime = 0d;
            _startDeadline = 0d;
            _expectedEndTime = 0d;
            _graceUntil = 0d;
            _applicationPauseStartTime = 0d;
            _remainingScheduledDelay = 0d;
            _schedulePaused = false;
            _callerPauseStartTime = 0d;
            _startTimeoutSeconds = 0f;
            _baseVolume = 1f;
            _fadeStartTime = 0d;
            _fadeDuration = 0d;
            _fadeStartVolume = 0f;
            _fadeTargetVolume = 0f;
            _fade = FadeAction.None;
            _spatialMode = SpatialPlaybackMode.TwoDimensional;
            _followTarget = null;
            _lastSpatialPosition = Vector3.zero;
            _hasLoggedFollowPositionWarning = false;
        }
    }
}
