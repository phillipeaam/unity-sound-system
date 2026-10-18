using System.Collections;
using NUnit.Framework;
using Phillipe2AM.SoundSystem.Playback;
using Phillipe2AM.SoundSystem.Tests.Runtime.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

namespace Phillipe2AM.SoundSystem.Tests.Runtime
{
    /// <summary>Fixed-position and followed-target 3D playback behavior.</summary>
    public sealed class AudioSpatialPlaybackTests : AudioPlaybackTestFixture
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            InitializeAudioTestContext();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            CleanupAudioTestContext();
            yield return null;
        }

        /// <summary>Verifies that spatial playback applies distance, rolloff, volume, and pitch, and rejects invalid overrides.</summary>
        [Test]
        public void SpatialPlaybackOptionsSetDistanceAndRolloffAndInvalidOverridesAreRejected()
        {
            ManagerConfiguration = CreateConfiguration(false);
            SetField(ManagerConfiguration, ReflectionFields.AudioSystemConfiguration.MaximumPoolSize, 2);
            AudioManager manager = CreateManager(ManagerConfiguration, "SoundSystemSpatialOptionsManager");

            AudioPlaybackOptions options = new AudioPlaybackOptions(
                minDistance: 2f,
                maxDistance: 20f,
                rolloffMode: AudioRolloffMode.Linear,
                volume: 0.6f,
                pitch: 0.75f);
            Assert.IsTrue(manager.TryPlayAtPosition(Clip, Bus, new Vector3(2f, 0f, 0f), out AudioPlaybackHandle handle, options));
            AudioSource source = ManagerObject.GetComponentInChildren<AudioSource>();
            Assert.AreEqual(2f, source.minDistance);
            Assert.AreEqual(20f, source.maxDistance);
            Assert.AreEqual(AudioRolloffMode.Linear, source.rolloffMode);
            Assert.AreEqual(0.6f, source.volume);
            Assert.AreEqual(0.75f, source.pitch);
            Assert.IsTrue(handle.Stop(false));

            LogAssert.Expect(LogType.Warning, "AudioManager rejected playback options. Playback volume must be finite and between 0 and 1.");
            Assert.IsFalse(manager.TryPlay(Clip, Bus, out _, new AudioPlaybackOptions(volume: 1.1f)));
            Assert.IsFalse(manager.IsClipPlaying(Clip));
        }

        /// <summary>Verifies that fixed-position playback is 3D, stays at its starting position, and resets for reuse as 2D.</summary>
        [Test]
        public void FixedPositionPlaybackUsesFull3DAndResetsToReusable2DState()
        {
            GameObject eventObject = TrackForCleanup(new GameObject("SoundSystemExplosionOrigin"));
            AudioSourcePlayback playback = new AudioSourcePlayback(Owner.transform, 0);
            Vector3 worldPosition = new Vector3(3f, 4f, 5f);
            eventObject.transform.position = worldPosition;

            playback.PlayAtPosition(Clip, Bus, eventObject.transform.position, 0f, 5f, AudioSettings.dspTime);

            Assert.AreEqual(1f, playback.Source.spatialBlend);
            Assert.AreEqual(worldPosition, playback.Source.transform.position);
            Assert.AreSame(Owner.transform, playback.Source.transform.parent);
            eventObject.transform.position = new Vector3(30f, 40f, 50f);
            Assert.AreEqual(worldPosition, playback.Source.transform.position, "A position snapshot must not follow its origin object.");

            AudioSource originalSource = playback.Source;
            Assert.IsTrue(playback.Release());

            Assert.AreSame(originalSource, playback.Source);
            Assert.AreEqual(0f, playback.Source.spatialBlend);
            Assert.AreSame(Owner.transform, playback.Source.transform.parent);
            Assert.AreEqual(Vector3.zero, playback.Source.transform.localPosition);
            Assert.IsNull(playback.Source.clip);
            Assert.IsNull(playback.Source.outputAudioMixerGroup);

            Object.Destroy(eventObject);
        }

        /// <summary>Verifies that followed playback tracks its target through fades and pauses, then finishes at its last position if the target is destroyed.</summary>
        [UnityTest]
        public IEnumerator FollowPlaybackTracksTargetAndSurvivesTargetDestruction()
        {
            AudioSourcePool pool = new AudioSourcePool(Owner.transform, 1, 1, 0.5f, 5f);
            AudioSourcePlayback playback = GetField<System.Collections.Generic.List<AudioSourcePlayback>>(
                pool,
                ReflectionFields.AudioSourcePool.Playbacks)[0];
            GameObject targetObject = TrackForCleanup(new GameObject("SoundSystemMovingTarget"));
            targetObject.transform.position = new Vector3(1f, 2f, 3f);

            Assert.IsTrue(pool.Play(Clip, Bus, targetObject.transform, 0f, loop: true));
            Assert.AreEqual(1f, playback.Source.spatialBlend);
            Assert.IsFalse(playback.Source.transform.IsChildOf(targetObject.transform), "The target must not own the pooled source GameObject.");
            Assert.IsTrue(pool.HasPendingPlaybackWork, "Followed loops must keep the shared pump active while the target exists.");

            Assert.AreEqual(1, pool.PauseClip(Clip, true));
            Vector3 fadingPosition = new Vector3(3f, 5f, 7f);
            targetObject.transform.position = fadingPosition;
            pool.TickPlaybackStates(AudioSettings.dspTime + 0.1d);
            Assert.IsTrue(playback.IsFading);
            Assert.AreEqual(fadingPosition, playback.Source.transform.position, "A fading source must still track its target.");
            pool.TickFades(AudioSettings.dspTime + 1d);
            Assert.IsTrue(playback.IsPaused);
            Assert.AreEqual(1, pool.ResumeClip(Clip, false));

            Vector3 pausedPosition = new Vector3(4f, 5f, 6f);
            Assert.AreEqual(1, pool.PauseClip(Clip, false));
            targetObject.transform.position = pausedPosition;
            pool.TickPlaybackStates(AudioSettings.dspTime);
            Assert.AreEqual(pausedPosition, playback.Source.transform.position, "A paused source must still track its target.");
            Assert.AreEqual(1, pool.ResumeClip(Clip, false));

            Vector3 globallyPausedPosition = new Vector3(5f, 6f, 7f);
            AudioListener.pause = true;
            targetObject.transform.position = globallyPausedPosition;
            pool.TickPlaybackStates(AudioSettings.dspTime);
            Assert.AreEqual(globallyPausedPosition, playback.Source.transform.position, "Global listener pause must not stop transform tracking.");
            AudioListener.pause = false;

            Vector3 lastWorldPosition = new Vector3(7f, 8f, 9f);
            targetObject.transform.position = lastWorldPosition;
            pool.TickPlaybackStates(AudioSettings.dspTime);
            Assert.AreEqual(lastWorldPosition, playback.Source.transform.position);

            Object.Destroy(targetObject);
            yield return null;
            pool.TickPlaybackStates(AudioSettings.dspTime);

            Assert.IsTrue(playback.IsActive, "Destroying a target must not destroy or release its active source.");
            Assert.IsNotNull(playback.Source);
            Assert.AreEqual(lastWorldPosition, playback.Source.transform.position);
            Assert.IsNull(GetField<Transform>(playback, ReflectionFields.AudioSourcePlayback.FollowTarget));
            Assert.IsTrue(pool.HasPendingPlaybackWork, "The detached loop still maintains its world position if the manager moves.");

            AudioSource originalSource = playback.Source;
            pool.StopAll();
            Assert.AreSame(originalSource, playback.Source);
            Assert.AreEqual(0f, playback.Source.spatialBlend);
            Assert.AreSame(Owner.transform, playback.Source.transform.parent);
            Assert.AreEqual(Vector3.zero, playback.Source.transform.localPosition);
        }

        /// <summary>Verifies that a scheduled followed soundtracks its target before playback starts.</summary>
        [Test]
        public void ScheduledFollowPlaybackTracksTargetBeforeStart()
        {
            AudioSourcePool pool = new AudioSourcePool(Owner.transform, 1, 1, 0f, 5f);
            AudioSourcePlayback playback = GetField<System.Collections.Generic.List<AudioSourcePlayback>>(
                pool,
                ReflectionFields.AudioSourcePool.Playbacks)[0];
            GameObject targetObject = TrackForCleanup(new GameObject("SoundSystemScheduledTarget"));
            Vector3 scheduledPosition = new Vector3(11f, 12f, 13f);

            Assert.IsTrue(pool.Play(Clip, Bus, targetObject.transform, 5f));
            targetObject.transform.position = scheduledPosition;
            pool.TickPlaybackStates(AudioSettings.dspTime);

            Assert.IsTrue(playback.NeedsLifecycleMonitoring);
            Assert.AreEqual(1f, playback.Source.spatialBlend);
            Assert.AreEqual(scheduledPosition, playback.Source.transform.position);
            Assert.IsTrue(pool.HasPendingPlaybackWork);
            Assert.AreEqual(1, pool.StopClip(Clip, false));

            Object.Destroy(targetObject);
        }

        /// <summary>Verifies that the manager rejects a missing follow target and a non-finite fixed position.</summary>
        [Test]
        public void ManagerRejectsNullFollowTargetAndNonFiniteWorldPosition()
        {
            ManagerConfiguration = CreateConfiguration(false);
            AudioManager manager = CreateManager(ManagerConfiguration, "SoundSystemSpatialValidationManager");

            LogAssert.Expect(LogType.Warning, "AudioManager.TryPlay rejected a null follow target. Provide a valid Transform.");
            Assert.IsFalse(manager.TryPlay(Clip, Bus, null, out _));

            LogAssert.Expect(LogType.Warning, "AudioManager.TryPlayAtPosition rejected a non-finite world position.");
            Assert.IsFalse(manager.TryPlayAtPosition(Clip, Bus, new Vector3(float.NaN, 0f, 0f), out _));
        }

        /// <summary>Verifies the manager's fixed-position and followed playback APIs, including target destruction and source reuse.</summary>
        [UnityTest]
        public IEnumerator ManagerPlaysAtFixedPositionAndFollowsTargetUntilItIsDestroyed()
        {
            ManagerConfiguration = CreateConfiguration(false);
            SetField(ManagerConfiguration, ReflectionFields.AudioSystemConfiguration.InitialPoolSize, 1);
            SetField(ManagerConfiguration, ReflectionFields.AudioSystemConfiguration.MaximumPoolSize, 1);
            AudioManager manager = CreateManager(ManagerConfiguration, "SoundSystemSpatialApiManager");

            Vector3 fixedPosition = new Vector3(2f, 4f, 6f);
            AudioSource source = ManagerObject.GetComponentInChildren<AudioSource>();
            Assert.IsNotNull(source);
            Assert.IsTrue(manager.TryPlayAtPosition(Clip, Bus, fixedPosition, out _));
            Assert.IsNotNull(source);
            Assert.AreEqual(1f, source.spatialBlend);
            Assert.AreSame(ManagerObject.transform, source.transform.parent);
            Assert.AreEqual(fixedPosition, source.transform.position);
            Assert.AreEqual(1, manager.StopClip(Clip, false));
            Assert.AreSame(ManagerObject.transform, source.transform.parent);
            Assert.AreEqual(0f, source.spatialBlend);

            GameObject target = TrackForCleanup(new GameObject("SoundSystemSpatialApiTarget"));
            Vector3 followedPosition = new Vector3(9f, 8f, 7f);
            Assert.IsTrue(manager.TryPlay(Clip, Bus, target.transform, out _));
            Assert.AreSame(Clip, source.clip, "The released source slot should be reused for followed playback.");
            Assert.AreEqual(1f, source.spatialBlend);
            Assert.AreSame(ManagerObject.transform, source.transform.parent);
            target.transform.position = followedPosition;
            yield return null;
            Assert.AreEqual(followedPosition, source.transform.position);

            Object.Destroy(target);
            yield return null;
            Assert.IsNotNull(source);
            Assert.AreEqual(followedPosition, source.transform.position);
            Assert.AreSame(Clip, source.clip, "Target destruction must not interrupt the active clip.");

            yield return new WaitForSecondsRealtime(Clip.length + 0.5f);
            Assert.IsNull(source.clip, "A followed one-shot must release naturally after its target is destroyed.");
            Assert.IsNull(source.outputAudioMixerGroup);
            Assert.AreEqual(0f, source.spatialBlend);
            Assert.AreSame(ManagerObject.transform, source.transform.parent);

            Assert.IsTrue(manager.TryPlay(Clip, Bus, out _));
            Assert.AreSame(Clip, source.clip, "The same slot must be reusable for a later 2D request.");
            Assert.AreEqual(1, manager.StopClip(Clip, false));
        }
    }
}
