using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Phillipe2AM.SoundSystem.Playback;
using Phillipe2AM.SoundSystem.Tests.Runtime.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

namespace Phillipe2AM.SoundSystem.Tests.Runtime
{
    /// <summary>Overlapping pauses, fade interruptions, ownership and stable handle identity.</summary>
    public sealed class AudioPlaybackEdgeCaseTests : AudioPlaybackTestFixture
    {
        [UnitySetUp]
        public IEnumerator SetUp() { InitializeAudioTestContext(); yield return null; }

        [UnityTearDown]
        public IEnumerator TearDown() { CleanupAudioTestContext(); yield return null; }

        /// <summary>A manual pause freezes a scheduled delay, reports Paused, and schedules the remainder on resume.</summary>
        [Test]
        public void CallerPausePreservesScheduledDelay()
        {
            var playback = new AudioSourcePlayback(Owner.transform, 0);
            double now = AudioSettings.dspTime;
            playback.Play(Clip, Bus, 2f, 5f, now);
            playback.Pause(true, 0.5f, now + 0.5d);
            Assert.AreEqual(AudioPlaybackState.Paused, playback.GetPublicState());
            playback.Resume(false, 0f, now + 10d);
            Assert.AreEqual(AudioPlaybackState.Scheduled, playback.GetPublicState());
            Assert.That(GetField<double>(playback, ReflectionFields.AudioSourcePlayback.ScheduledStartTime),
                Is.EqualTo(now + 11.5d).Within(0.001d));
            playback.Release();
        }

        /// <summary>A caller pause while an app-suspended schedule is pending prevents automatic restart.</summary>
        [Test]
        public void CallerPauseDuringSuspensionKeepsSchedulePausedOnReturn()
        {
            var playback = new AudioSourcePlayback(Owner.transform, 0);
            double now = AudioSettings.dspTime;
            playback.Play(Clip, Bus, 2f, 5f, now);
            playback.PauseForApplication(now + 0.5d);
            playback.Pause(false, 0f, now + 0.5d);
            playback.ResumeFromApplication(now + 10d);
            Assert.AreEqual(AudioPlaybackState.Paused, playback.GetPublicState());
            Assert.IsFalse(playback.Source.isPlaying);
            playback.Resume(false, 0f, now + 20d);
            Assert.That(GetField<double>(playback, ReflectionFields.AudioSourcePlayback.ScheduledStartTime),
                Is.EqualTo(now + 21.5d).Within(0.001d));
            playback.Release();
        }

        /// <summary>Caller resume during app suspension is deferred until return and resumes the retained source.</summary>
        [Test]
        public void CallerResumeDuringSuspensionResumesOnReturn()
        {
            var playback = new AudioSourcePlayback(Owner.transform, 0);
            double now = AudioSettings.dspTime;
            playback.Play(Clip, Bus, 0f, 5f, now, loop: true);
            playback.Pause(false, 0f, now);
            playback.PauseForApplication(now);
            playback.Resume(false, 0f, now + 1d);
            Assert.IsFalse(playback.Source.isPlaying);
            playback.ResumeFromApplication(now + 10d);
            Assert.IsTrue(playback.Source.isPlaying);
            playback.Release();
        }

        /// <summary>Interrupting a fade must not replace the original requested volume with a transient gain.</summary>
        [Test]
        public void RepeatedPauseDuringFadePreservesOriginalVolume()
        {
            var playback = new AudioSourcePlayback(Owner.transform, 0);
            double now = AudioSettings.dspTime;
            playback.Play(Clip, Bus, 0f, 5f, now,
                new AudioPlaybackSettings(true, 0.8f, 1f, 1f, 25f, AudioRolloffMode.Linear), 1);
            playback.Pause(true, 1f, now);
            playback.TickFade(now + 0.5d);
            Assert.That(playback.Source.volume, Is.EqualTo(0.4f).Within(0.001f));
            playback.Pause(true, 1f, now + 0.5d);
            playback.TickFade(now + 2d);
            playback.Resume(false, 0f, now + 2d);
            Assert.That(playback.Source.volume, Is.EqualTo(0.8f).Within(0.001f));
            playback.Release();
        }

        /// <summary>Fixed and detached spatial sounds retain world coordinates after owner transforms change.</summary>
        [Test]
        public void FixedSoundRemainsInWorldSpaceWhenOwnerMoves()
        {
            var playback = new AudioSourcePlayback(Owner.transform, 0);
            Vector3 position = new Vector3(2f, 3f, 4f);
            double now = AudioSettings.dspTime;
            playback.PlayAtPosition(Clip, Bus, position, 0f, 5f, now, loop: true);
            Owner.transform.SetPositionAndRotation(new Vector3(10f, 0f, 0f), Quaternion.Euler(0f, 40f, 0f));
            Owner.transform.localScale = Vector3.one * 2f;
            playback.RefreshState(now);
            Assert.That(Vector3.Distance(position, playback.Source.transform.position), Is.LessThan(0.001f));
            playback.Release();
        }

        /// <summary>Recovering a removed AudioSource component reuses its slot GameObject.</summary>
        [UnityTest]
        public IEnumerator SourceComponentRecoveryDoesNotAccumulateObjects()
        {
            var playback = new AudioSourcePlayback(Owner.transform, 0);
            GameObject original = playback.Source.gameObject;
            Object.Destroy(playback.Source);
            yield return null;
            Assert.IsTrue(playback.EnsureSource());
            Assert.AreSame(original, playback.Source.gameObject);
            Assert.AreEqual(1, Owner.transform.childCount);
        }

        /// <summary>Removing only the manager component disposes owned slots while retaining its host GameObject.</summary>
        [UnityTest]
        public IEnumerator ManagerComponentRemovalDisposesPoolObjects()
        {
            AudioManager manager = CreateManager();
            Assert.IsTrue(manager.TryPlay(Clip, Bus, out _));
            Assert.Greater(ManagerObject.transform.childCount, 0);
            Object.Destroy(manager);
            yield return null; yield return null;
            Assert.IsNotNull(ManagerObject);
            Assert.AreEqual(0, ManagerObject.transform.childCount);
        }

        /// <summary>A handle stored as a hash key stays findable after its Unity manager is destroyed.</summary>
        [UnityTest]
        public IEnumerator HandleHashIdentitySurvivesManagerDestruction()
        {
            AudioManager manager = CreateManager();
            Assert.IsTrue(manager.TryPlay(Clip, Bus, out AudioPlaybackHandle handle));
            int hash = handle.GetHashCode();
            var handles = new HashSet<AudioPlaybackHandle> { handle };
            Object.Destroy(manager);
            yield return null;
            Assert.AreEqual(hash, handle.GetHashCode());
            Assert.IsTrue(handles.Contains(handle));
            Assert.IsFalse(handle.IsValid);
        }

        /// <summary>An inaudible scheduled or application-suspended sound stops immediately despite a fade request.</summary>
        [Test]
        public void StopBeforeScheduledStartOrDuringSuspensionReleasesImmediately()
        {
            var playback = new AudioSourcePlayback(Owner.transform, 0);
            double now = AudioSettings.dspTime;
            playback.Play(Clip, Bus, 2f, 5f, now);
            Assert.IsTrue(playback.Stop(true, 1f, now));
            Assert.IsFalse(playback.IsActive);
            playback.Play(Clip, Bus, 0f, 5f, now, loop: true);
            playback.PauseForApplication(now);
            Assert.IsTrue(playback.Stop(true, 1f, now));
            Assert.IsFalse(playback.IsActive);
        }

        /// <summary>A fade-requested caller pause during suspension stays paused when the app returns.</summary>
        [Test]
        public void CallerFadePauseDuringSuspensionRemainsPaused()
        {
            var playback = new AudioSourcePlayback(Owner.transform, 0);
            double now = AudioSettings.dspTime;
            playback.Play(Clip, Bus, 0f, 5f, now, loop: true);
            playback.PauseForApplication(now);
            playback.Pause(true, 1f, now + 1d);
            playback.ResumeFromApplication(now + 10d);
            Assert.AreEqual(AudioPlaybackState.Paused, playback.GetPublicState());
            Assert.IsFalse(playback.Source.isPlaying);
            playback.Release();
        }

        /// <summary>Reversing an in-progress pause fade preserves the current gain and returns to the original target.</summary>
        [Test]
        public void HandleCanReversePauseFadeWithoutGainJump()
        {
            AudioManager manager = CreateManager();
            Assert.IsTrue(manager.TryPlay(Clip, Bus, out AudioPlaybackHandle handle,
                new AudioPlaybackOptions(loop: true, volume: 0.8f)));
            var pool = GetField<AudioSourcePool>(manager, ReflectionFields.AudioManager.SourcePool);
            var slots = GetField<List<AudioSourcePlayback>>(pool, ReflectionFields.AudioSourcePool.Playbacks);
            double now = AudioSettings.dspTime;
            Assert.IsTrue(handle.Pause());
            Assert.IsFalse(handle.Pause(), "Repeating a pending pause must not restart the fade.");
            slots[0].TickFade(now + ManagerConfiguration.FadeDurationSeconds * 0.5d);
            float gain = slots[0].Source.volume;
            Assert.IsTrue(handle.Resume());
            Assert.That(slots[0].Source.volume, Is.EqualTo(gain).Within(0.001f));
            slots[0].TickFade(now + ManagerConfiguration.FadeDurationSeconds * 2d);
            Assert.That(slots[0].Source.volume, Is.EqualTo(0.8f).Within(0.001f));
        }

        /// <summary>Overlapping caller/app pauses extend startup deadlines by their union, never twice.</summary>
        [Test]
        public void CallerResumeDuringSuspensionDoesNotDoubleCountPausedTime()
        {
            var playback = new AudioSourcePlayback(Owner.transform, 0);
            double now = AudioSettings.dspTime;
            playback.Play(Clip, Bus, 0f, 5f, now, loop: true);
            playback.Pause(false, 0f, now + 1d);
            playback.PauseForApplication(now + 3d);
            playback.Resume(false, 0f, now + 5d);
            playback.ResumeFromApplication(now + 10d);
            Assert.That(GetField<double>(playback, ReflectionFields.AudioSourcePlayback.StartDeadline),
                Is.EqualTo(now + 14d).Within(0.001d));
            playback.Release();
        }

        /// <summary>A one-shot ending during a pause fade clears its slot without a later caller request.</summary>
        [UnityTest]
        public IEnumerator NaturalCompletionDuringPauseFadeReleasesBeforeFadeEnds()
        {
            AudioManager manager = CreateManager();
            Assert.IsTrue(manager.TryPlay(Clip, Bus, out AudioPlaybackHandle handle));
            AudioSource source = ManagerObject.GetComponentInChildren<AudioSource>();
            Assert.IsTrue(handle.Pause(true));
            yield return new WaitForSecondsRealtime(Clip.length + 0.4f);
            Assert.IsNull(source.clip, "Natural completion must release the source instead of retaining a dead paused playback.");
            Assert.IsFalse(handle.IsValid);
        }

        private AudioManager CreateManager()
        {
            ManagerConfiguration = CreateConfiguration(false);
            AudioManager manager = CreateManager(ManagerConfiguration, "ReleaseEdgeTestManager");
            return manager;
        }
    }
}
