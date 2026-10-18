using System;
using System.Collections;
using NUnit.Framework;
using Phillipe2AM.SoundSystem.Playback;
using Phillipe2AM.SoundSystem.Tests.Runtime.Reflection;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace Phillipe2AM.SoundSystem.Tests.Runtime
{
    /// <summary>Warmed package allocations, excluding fixture creation and test-framework overhead.</summary>
    public sealed class AudioPerformanceTests : AudioPlaybackTestFixture
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            InitializeAudioTestContext();
            ManagerConfiguration = CreateConfiguration(true);
            SetField(ManagerConfiguration, ReflectionFields.AudioSystemConfiguration.InitialPoolSize, 32);
            CreateManager(ManagerConfiguration, "AudioPerformanceManager");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() { CleanupAudioTestContext(); yield return null; }

        /// <summary>Calibrates GC.Alloc so an unavailable counter cannot silently claim zero allocations.</summary>
        [Test]
        public void AllocationRecorderDetectsKnownAllocation()
        {
            using var recorder = StartAllocationRecorder();
            object probe = new byte[4096];
            recorder.Stop();
            GC.KeepAlive(probe);
            Assert.IsTrue(recorder.Valid);
            Assert.Greater(recorder.Count, 0);
        }

        /// <summary>Prewarmed playback and immediate stop reuse slots without managed allocations.</summary>
        [Test]
        public void WarmPlaybackAndStopDoNotAllocate()
        {
            AudioManager manager = ManagerObject.GetComponent<AudioManager>();
            var options = new AudioPlaybackOptions(loop: true, volume: 0f);
            Assert.IsTrue(manager.TryPlay(Clip, Bus, out AudioPlaybackHandle probe, options));
            Assert.IsTrue(probe.Stop(false));
            AssertZeroAllocations(() => manager.TryPlay(Clip, Bus, out AudioPlaybackHandle handle, options) && handle.Stop(false));
            Assert.IsFalse(manager.IsBusPlaying(Bus));
            Assert.AreEqual(32, ManagerObject.transform.childCount);
        }

        /// <summary>Warmed persistence and volume queries reuse stable preference keys.</summary>
        [Test]
        public void WarmPersistedVolumeChangesDoNotAllocate()
        {
            AudioManager manager = ManagerObject.GetComponent<AudioManager>();
            Assert.IsTrue(manager.SetBusVolumePercent(Bus, 50f));
            AssertZeroAllocations(() => manager.SetBusVolumePercent(Bus, 50f) &&
                Mathf.Abs(manager.GetBusVolumePercent(Bus) - 50f) < 0.1f);
        }

        /// <summary>A saturated spatial pool, tracking and handle controls reuse their warmed state.</summary>
        [Test]
        public void WarmSpatialReplacementAndControlsDoNotAllocate()
        {
            AudioManager manager = ManagerObject.GetComponent<AudioManager>();
            var options = new AudioPlaybackOptions(loop: true, volume: 0f);
            for (int i = 0; i < 32; i++) Assert.IsTrue(manager.TryPlay(Clip, Bus, Owner.transform, out _, options));
            AssertZeroAllocations(() =>
            {
                bool accepted = manager.TryPlay(Clip, Bus, Owner.transform, out AudioPlaybackHandle handle, options);
                bool paused = handle.Pause(false);
                bool resumed = handle.Resume(false);
                manager.TickPlaybackPump();
                return accepted && paused && resumed && handle.IsValid && handle.State != AudioPlaybackState.Paused;
            });
            Assert.AreEqual(32, ManagerObject.transform.childCount);
        }

        /// <summary>Starting, ticking and completing repeated pause/resume fades produces no managed garbage.</summary>
        [Test]
        public void WarmPauseAndResumeFadesDoNotAllocate()
        {
            AudioManager manager = ManagerObject.GetComponent<AudioManager>();
            Assert.IsTrue(manager.TryPlay(Clip, Bus, out AudioPlaybackHandle handle,
                new AudioPlaybackOptions(loop: true, volume: 0f)));
            var pool = GetField<AudioSourcePool>(manager, ReflectionFields.AudioManager.SourcePool);
            AssertZeroAllocations(() =>
            {
                double end = AudioSettings.dspTime + ManagerConfiguration.FadeDurationSeconds * 2d;
                bool paused = handle.Pause(true);
                pool.TickFades(end);
                bool held = handle.State == AudioPlaybackState.Paused;
                bool resumed = handle.Resume(true);
                pool.TickFades(end);
                return paused && held && resumed && handle.IsValid && handle.State != AudioPlaybackState.Paused;
            });
        }

        private static ProfilerRecorder StartAllocationRecorder() => ProfilerRecorder.StartNew(
            ProfilerCategory.Memory, "GC.Alloc", 4096,
            ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

        private static void AssertZeroAllocations(Func<bool> action)
        {
            for (int i = 0; i < 32; i++) Assert.IsTrue(action(), "Warmup must perform the requested work.");
            int failures = 0;
            using var recorder = StartAllocationRecorder();
            for (int i = 0; i < 128; i++) if (!action()) failures++;
            recorder.Stop();
            Assert.AreEqual(0, failures, "Rejected/no-op requests cannot establish allocation-free successful behavior.");
            Assert.IsTrue(recorder.Valid, "The allocation recorder must be available.");
            Assert.AreEqual(0, recorder.Count, "Warm package operations must not allocate managed memory.");
        }
    }
}
