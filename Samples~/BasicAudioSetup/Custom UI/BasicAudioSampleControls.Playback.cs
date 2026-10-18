using Phillipe2AM.SoundSystem;
using Phillipe2AM.SoundSystem.Configuration;
using Phillipe2AM.SoundSystem.Playback;
using UnityEngine;

namespace Samples.BasicAudioSetup
{
    public sealed partial class BasicAudioSampleControls
    {
        private void PlayTwoDimensional(AudioManager manager)
        {
            if (manager.TryPlay(_sfxTone, _sfxBus, out AudioPlaybackHandle handle, CreateTwoDimensionalOptions(), _delaySeconds))
            {
                SetLastPlayback(handle, "2D SFX");
                _spatialStatus = "Started 2D SFX.";
            }
            else
            {
                _spatialStatus = "2D SFX unavailable.";
            }
        }

        private bool PlayFollowingBeacon(AudioManager manager)
        {
            if (_movingBeacon == null)
            {
                return false;
            }

            if (!manager.TryPlay(
                    _followTone,
                    _sfxBus,
                    _movingBeacon.transform,
                    out AudioPlaybackHandle handle,
                    CreateSpatialOptions(), _delaySeconds))
            {
                return false;
            }

            SetLastPlayback(handle, "Follow beacon");
            _spatialStatus = "Follow accepted. Green ring tracks the moving target.";
            ShowFollowIndicator(_movingBeacon.transform, handle);
            return true;
        }

        private bool PlayFixedPulse(AudioManager manager)
        {
            if (_movingBeacon == null)
            {
                return false;
            }

            Vector3 pulsePosition = _movingBeacon.transform.position;
            if (!manager.TryPlayAtPosition(
                    _positionTone,
                    _sfxBus,
                    pulsePosition,
                    out AudioPlaybackHandle handle,
                    CreateSpatialOptions(), _delaySeconds))
            {
                return false;
            }

            SetLastPlayback(handle, "Fixed-position pulse");
            _spatialStatus = "Fixed pulse accepted. Magenta ring stays at the captured position.";
            ShowFixedPositionIndicator(pulsePosition, handle);
            return true;
        }

        private void PlayMusic(AudioManager manager)
        {
            bool accepted = manager.TryPlay(_musicTone, _musicBus, out AudioPlaybackHandle handle,
                CreateTwoDimensionalOptions(), _delaySeconds);
            _spatialStatus = accepted ? "Music accepted. Use Controls to pause, resume or stop." : "Music rejected.";
            if (accepted) SetLastPlayback(handle, "Music");
        }

        private void StopAllSampleAudio(AudioManager manager)
        {
            manager.StopBus(_musicBus, false);
            manager.StopBus(_sfxBus, false);
            ClearPositionIndicators();
            _spatialStatus = "Sample playback stopped. Old handles are now invalid.";
        }

        private void FillSfxPool(AudioManager manager)
        {
            StopAllSampleAudio(manager);
            var options = new AudioPlaybackOptions(loop: true, volume: 0.05f);
            AudioPlaybackHandle oldest = default;
            int accepted = 0;
            int count = manager.Configuration.MaximumPoolSize + 1;
            for (int index = 0; index < count; index++)
            {
                if (!manager.TryPlay(_followTone, _sfxBus, out AudioPlaybackHandle handle, options)) continue;
                if (index == 0) oldest = handle;
                accepted++;
                SetLastPlayback(handle, "Newest pool request");
            }
            _otherPlaybackHandle = oldest;
            _otherPlaybackLabel = "Oldest pool request";
            _spatialStatus = $"{accepted}/{count} accepted. Oldest handle valid: {oldest.IsValid}. " +
                $"Newest valid: {_lastPlaybackHandle.IsValid}. Expected: false / true.";
        }

        private void DemonstrateRejection(AudioManager manager)
        {
            StopAllSampleAudio(manager);
            var options = new AudioPlaybackOptions(loop: true, volume: 0.05f);
            for (int index = 0; index < manager.Configuration.MaximumPoolSize; index++)
            {
                manager.TryPlay(_followTone, _sfxBus, out AudioPlaybackHandle handle, options);
                SetLastPlayback(handle, "Protected SFX loop");
            }
            bool otherBus = manager.TryPlay(_musicTone, _musicBus, out _, options);
            bool otherCategory = manager.TryPlay(_sfxTone, _sfxBus, out _);
            _spatialStatus = $"Music accepted: {otherBus}; one-shot accepted: {otherCategory}. " +
                $"SFX loops playing: {manager.IsBusPlaying(_sfxBus)}. Expected: false / false / true.";
        }

        private void PlayComparisonPair(AudioManager manager)
        {
            var options = new AudioPlaybackOptions(loop: true, volume: 0.25f);
            bool firstAccepted = manager.TryPlay(_followTone, _sfxBus, out _otherPlaybackHandle, options);
            _otherPlaybackLabel = "Other SFX loop (pair)";
            bool secondAccepted = manager.TryPlay(_followTone, _sfxBus, out AudioPlaybackHandle handle, options);
            if (secondAccepted) SetLastPlayback(handle, "Selected SFX loop (pair)");
            _controlResult = $"Pair accepted: {firstAccepted} / {secondAccepted}. Handle controls affect one instance; bus/clip controls affect both.";
            _spatialStatus = "Comparison requested. If the pool rejects it, stop sample playback and retry.";
        }

        private AudioPlaybackOptions CreateTwoDimensionalOptions()
        {
            return new AudioPlaybackOptions(
                loop: _loopPlayback,
                volume: _playbackVolume,
                pitch: _playbackPitch);
        }

        private AudioPlaybackOptions CreateSpatialOptions()
        {
            if (!_overrideSpatialSettings)
            {
                return new AudioPlaybackOptions(
                    loop: _loopPlayback,
                    volume: _playbackVolume,
                    pitch: _playbackPitch);
            }

            return new AudioPlaybackOptions(
                loop: _loopPlayback,
                volume: _playbackVolume,
                pitch: _playbackPitch,
                minDistance: _minDistance,
                maxDistance: _maxDistance,
                rolloffMode: _rolloffMode);
        }

        private void SetLastPlayback(AudioPlaybackHandle handle, string label)
        {
            _lastPlaybackHandle = handle;
            _lastPlaybackLabel = label;
        }

    }
}
