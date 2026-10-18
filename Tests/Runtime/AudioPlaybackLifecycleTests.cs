using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Phillipe2AM.SoundSystem.Playback;
using Phillipe2AM.SoundSystem.Tests.Runtime.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

namespace Phillipe2AM.SoundSystem.Tests.Runtime
{
    /// <summary>Playback startup, completion, suspension, and failure handling.</summary>
    public sealed class AudioPlaybackLifecycleTests : AudioPlaybackTestFixture
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

        /// <summary>Verifies that suspension preserves scheduled delays, caller pauses, and fade timing without changing global listener pause.</summary>
        [Test]
        public void PoolPreservesScheduledDelayAndCallerPauseAcrossApplicationSuspension()
        {
            AudioSourcePool pool = new AudioSourcePool(Owner.transform, 1, 1, 0f, 5f);
            double start = AudioSettings.dspTime;
            Assert.IsTrue(pool.Play(Clip, Bus, 2f));
            AudioSourcePlayback playback = GetField<System.Collections.Generic.List<AudioSourcePlayback>>(
                pool,
                ReflectionFields.AudioSourcePool.Playbacks)[0];

            pool.PauseForApplication(start + 0.25d);
            Assert.AreEqual(AudioPlaybackState.Paused, playback.GetPublicState());
            Assert.IsFalse(pool.HasPendingPlaybackWork, "The active pump must sleep while the application is suspended.");
            pool.ResumeFromApplication(start + 20d);
            Assert.AreEqual(AudioPlaybackState.Scheduled, playback.GetPublicState());
            Assert.That(
                GetField<double>(playback, ReflectionFields.AudioSourcePlayback.ScheduledStartTime),
                Is.EqualTo(start + 21.75d).Within(0.001d));
            pool.StopAll();

            Assert.IsTrue(pool.Play(Clip, Bus, 0f));
            playback.Pause(false, 0f, AudioSettings.dspTime);
            Assert.IsTrue(playback.IsPaused);
            pool.PauseForApplication(AudioSettings.dspTime);
            pool.ResumeFromApplication(AudioSettings.dspTime + 10d);
            Assert.IsTrue(playback.IsPaused, "Returning from application suspension must preserve caller-paused playback.");
            Assert.IsFalse(playback.Source.isPlaying);
            pool.StopAll();

            Assert.IsTrue(pool.Play(Clip, Bus, 0f));
            double callerPauseStart = AudioSettings.dspTime;
            playback.Pause(false, 0f, callerPauseStart);
            pool.PauseForApplication(callerPauseStart);
            pool.ResumeFromApplication(callerPauseStart + 10d);
            playback.Resume(true, 1f, callerPauseStart + 10d);
            playback.TickFade(callerPauseStart + 10.5d);
            Assert.That(playback.Source.volume, Is.EqualTo(0.5f).Within(0.05f),
                "A caller resume after application return must not inherit the whole suspended duration as fade time.");
            pool.StopAll();
            Assert.IsFalse(AudioListener.pause, "The package must not toggle the global listener pause.");
        }

        /// <summary>Verifies that application suspension pauses and resumes package-owned playback without toggling AudioListener.pause.</summary>
        [Test]
        public void ManagerPausesOwnedPlaybackOnApplicationSuspendAndResumesItOnReturn()
        {
            ManagerConfiguration = CreateConfiguration(false);
            AudioManager manager = CreateManager(ManagerConfiguration, "SoundSystemApplicationPauseManager");

            Assert.IsTrue(ManagerConfiguration.PausePlaybackOnApplicationSuspend);
            Assert.IsTrue(manager.TryPlay(Clip, Bus, out AudioPlaybackHandle handle,
                new AudioPlaybackOptions(loop: true)));
            AudioSource source = ManagerObject.GetComponentInChildren<AudioSource>();
            Assert.IsTrue(source.isPlaying);

            InvokeApplicationPause(manager, true);
            Assert.IsFalse(source.isPlaying);
            Assert.IsFalse(AudioListener.pause, "Application suspension must not change the global listener state.");

            InvokeApplicationPause(manager, false);
            Assert.IsTrue(source.isPlaying);
            Assert.IsTrue(handle.IsValid);
            Assert.IsTrue(handle.Stop(false));
        }

        /// <summary>Verifies that disabling suspension handling leaves playback running while the application is suspended.</summary>
        [Test]
        public void ManagerCanDisableApplicationSuspendHandling()
        {
            ManagerConfiguration = CreateConfiguration(false);
            SetField(ManagerConfiguration, ReflectionFields.AudioSystemConfiguration.PausePlaybackOnSuspend, false);
            AudioManager manager = CreateManager(ManagerConfiguration, "SoundSystemApplicationPauseDisabledManager");

            Assert.IsTrue(manager.TryPlay(Clip, Bus, out AudioPlaybackHandle handle,
                new AudioPlaybackOptions(loop: true)));
            AudioSource source = ManagerObject.GetComponentInChildren<AudioSource>();
            InvokeApplicationPause(manager, true);
            Assert.IsTrue(source.isPlaying, "Disabled application-pause handling must leave playback running.");
            InvokeApplicationPause(manager, false);
            Assert.IsTrue(source.isPlaying);
            Assert.IsTrue(handle.Stop(false));
        }

        /// <summary>Verifies that a playback which never starts is warned about and releases its slot after the configured timeout.</summary>
        [Test]
        public void NeverStartedPlaybackReleasesSlotAfterConfiguredTimeout()
        {
            AudioSourcePlayback playback = new AudioSourcePlayback(Owner.transform, 0);
            double now = AudioSettings.dspTime;
            playback.Play(Clip, Bus, 0f, 0.1f, now, loop: true);
            playback.Source.enabled = false;

            LogAssert.Expect(LogType.Warning, new Regex("did not start within its 0\\.1-second timeout"));
            Assert.IsTrue(playback.RefreshState(now + 1d));
            Assert.IsFalse(playback.IsActive);
        }

        /// <summary>Verifies that a short one-shot missed by state polling releases at its expected end without a timeout warning.</summary>
        [Test]
        public void UnobservedShortOneShotReleasesAtExpectedEndWithoutTimeoutWarning()
        {
            AudioSourcePlayback playback = new AudioSourcePlayback(Owner.transform, 0);
            double now = AudioSettings.dspTime;
            playback.Play(Clip, Bus, 0f, 5f, now);
            playback.Source.enabled = false;

            Assert.IsTrue(playback.RefreshState(now + Clip.length + 1d));
            Assert.IsFalse(playback.IsActive);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Verifies that completed one-shots release automatically, clear pooled references, and reuse the same source.</summary>
        [UnityTest]
        public IEnumerator ManagerReleasesCompletedOneShotWithoutAnotherApiCallAndReusesSource()
        {
            ManagerConfiguration = CreateConfiguration(false);
            AudioManager manager = CreateManager(ManagerConfiguration, "SoundSystemCompletionTestManager");

            Assert.IsTrue(manager.TryPlay(Clip, Bus, out _));
            AudioSource source = ManagerObject.GetComponentInChildren<AudioSource>();
            Assert.IsNotNull(source);
            Assert.AreSame(Clip, source.clip);
            Assert.IsTrue(source.isPlaying);
            Assert.IsTrue(GetField<bool>(manager, ReflectionFields.AudioManager.IsPlaybackPumpRunning));

            yield return new WaitForSecondsRealtime(Clip.length + 0.5f);

            Assert.IsNotNull(source);
            Assert.IsNull(source.clip);
            Assert.IsNull(source.outputAudioMixerGroup);
            Assert.IsFalse(GetField<bool>(manager, ReflectionFields.AudioManager.IsPlaybackPumpRunning));
            Assert.IsNotNull(Clip, "The consumer-owned clip asset must remain valid after slot release.");

            Assert.IsTrue(manager.TryPlay(Clip, Bus, out _));
            Assert.AreSame(source, ManagerObject.GetComponentInChildren<AudioSource>());
            yield return null;
        }

        /// <summary>Verifies that resuming a paused sound without a fade restarts the manager's lifecycle pump.</summary>
        [UnityTest]
        public IEnumerator ResumingWithoutFadeRestartsLifecyclePumpAfterPausedPumpStops()
        {
            ManagerConfiguration = CreateConfiguration(false);
            AudioManager manager = CreateManager(ManagerConfiguration, "SoundSystemResumeTestManager");

            Assert.IsTrue(manager.TryPlay(Clip, Bus, out _));
            Assert.AreEqual(1, manager.PauseClip(Clip, false));
            yield return new WaitForSecondsRealtime(0.05f);
            Assert.IsFalse(GetField<bool>(manager, ReflectionFields.AudioManager.IsPlaybackPumpRunning));

            Assert.AreEqual(1, manager.ResumeClip(Clip, false));
            Assert.IsTrue(GetField<bool>(manager, ReflectionFields.AudioManager.IsPlaybackPumpRunning));
            manager.StopClip(Clip, false);
            yield return null;
        }

        /// <summary>Verifies that a long one-shot which misses its startup deadline warns and releases its slot.</summary>
        [Test]
        public void LongOneShotThatMissesStartupDeadlineLogsAndReleases()
        {
            AudioClip longClip = TrackForCleanup(AudioClip.Create("LongSoundSystemTestClip", 441000, 1, 44100, false));
            AudioSourcePlayback playback = new AudioSourcePlayback(Owner.transform, 0);
            double now = AudioSettings.dspTime;
            playback.Play(longClip, Bus, 0f, 0.1f, now);
            playback.Source.enabled = false;

            LogAssert.Expect(LogType.Warning, new Regex("did not start within its 0\\.1-second timeout"));
            Assert.IsTrue(playback.RefreshState(now + 1d));
            Assert.IsFalse(playback.IsActive);

            UnityEngine.Object.Destroy(longClip);
        }

        /// <summary>Verifies that global listener pause freezes scheduled playback and its startup timeout.</summary>
        [Test]
        public void GlobalListenerPausePreservesScheduledPlaybackAndTimeout()
        {
            AudioSourcePlayback playback = new AudioSourcePlayback(Owner.transform, 0);
            double now = AudioSettings.dspTime;
            playback.Play(Clip, Bus, 1f, 0.1f, now);

            AudioListener.pause = true;
            Assert.IsFalse(playback.RefreshState(now + 10d));
            Assert.IsTrue(playback.IsActive);

            AudioListener.pause = false;
            Assert.IsFalse(playback.RefreshState(now + 0.5d));
            Assert.IsTrue(playback.IsActive);
            playback.Release();
        }

        /// <summary>Verifies that global listener pause freezes a fade until audio playback resumes.</summary>
        [Test]
        public void GlobalListenerPauseFreezesFadeUntilAudioResumes()
        {
            AudioSourcePlayback playback = new AudioSourcePlayback(Owner.transform, 0);
            double now = AudioSettings.dspTime;
            playback.Play(Clip, Bus, 0f, 5f, now);
            Assert.IsFalse(playback.Pause(true, 1f, now));

            AudioListener.pause = true;
            Assert.IsFalse(playback.TickFade(now + 10d));
            Assert.IsTrue(playback.IsFading);
            Assert.AreEqual(1f, playback.Source.volume);

            AudioListener.pause = false;
            Assert.IsFalse(playback.TickFade(now + 0.5d));
            Assert.That(playback.Source.volume, Is.EqualTo(0.5f).Within(0.05f));
            Assert.IsFalse(playback.TickFade(now + 1d));
            Assert.IsTrue(playback.IsPaused);
            playback.Release();
        }

        /// <summary>Verifies that a playback-pump failure is reported once and releases package-owned playback.</summary>
        [UnityTest]
        public IEnumerator PlaybackPumpFailureLogsOnceAndReleasesOwnedPlayback()
        {
            ManagerConfiguration = CreateConfiguration(false);
            ManagerObject = new GameObject("SoundSystemFailureTestManager");
            ManagerObject.SetActive(false);
            AudioManager manager = ManagerObject.AddComponent<AudioManager>();
            SetField(manager, ReflectionFields.AudioManager.Configuration, ManagerConfiguration);
            ManagerObject.SetActive(true);

            LogAssert.Expect(
                LogType.Warning,
                "AudioManager.TryPlay rejected a non-finite delay. Provide a finite number of seconds.");
            Assert.IsFalse(manager.TryPlay(Clip, Bus, out _, delaySeconds: float.NaN));
            Assert.IsTrue(manager.TryPlay(Clip, Bus, out _));
            LogAssert.Expect(LogType.Exception, new Regex("injected playback pump failure"));
            manager.HandlePlaybackPumpFailure(new InvalidOperationException("injected playback pump failure"));
            Assert.IsFalse(manager.IsClipPlaying(Clip));

            yield return null;
        }

        /// <summary>Verifies that a persistent manager on a child object is disabled with actionable diagnostics.</summary>
        [Test]
        public void PersistentManagerOnChildObjectIsDisabledWithActionableDiagnostic()
        {
            ManagerConfiguration = CreateConfiguration(false);
            ManagerParent = new GameObject("SoundSystemManagerParent");
            ManagerObject = new GameObject("SoundSystemChildManager");
            ManagerObject.SetActive(false);
            ManagerObject.transform.SetParent(ManagerParent.transform);

            LogAssert.Expect(LogType.Warning, new Regex("A persistent AudioManager must be on a root GameObject"));
            AudioManager manager = ManagerObject.AddComponent<AudioManager>();
            SetField(manager, ReflectionFields.AudioManager.Configuration, ManagerConfiguration);

            LogAssert.Expect(
                LogType.Error,
                new Regex("AudioManager is configured to persist across scenes but is attached to a child GameObject"));
            ManagerObject.SetActive(true);

            Assert.IsFalse(manager.enabled);
            Assert.IsNull(AudioManager.Instance);
        }
    }
}
