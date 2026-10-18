using System.Collections;
using NUnit.Framework;
using Phillipe2AM.SoundSystem.Playback;
using Phillipe2AM.SoundSystem.Tests.Runtime.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

namespace Phillipe2AM.SoundSystem.Tests.Runtime
{
    /// <summary>Per-playback controls and stale-handle protection.</summary>
    public sealed class AudioPlaybackHandleTests : AudioPlaybackTestFixture
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

        /// <summary>Verifies that a playback handle controls only its sound and applies per-playback volume and pitch.</summary>
        [Test]
        public void PerPlaybackHandleControlsOnlyItsGenerationAndAppliesOverrides()
        {
            ManagerConfiguration = CreateConfiguration(false);
            SetField(ManagerConfiguration, ReflectionFields.AudioSystemConfiguration.MaximumPoolSize, 2);
            AudioManager manager = CreateManager(ManagerConfiguration, "SoundSystemPlaybackHandleManager");

            AudioPlaybackOptions firstOptions = new AudioPlaybackOptions(loop: true, volume: 0.4f, pitch: 1.5f);
            AudioPlaybackOptions secondOptions = new AudioPlaybackOptions(loop: true, volume: 0.8f);
            Assert.IsTrue(manager.TryPlay(Clip, Bus, out AudioPlaybackHandle first, firstOptions));
            Assert.IsTrue(manager.TryPlay(Clip, Bus, out AudioPlaybackHandle second, secondOptions));
            Assert.AreNotEqual(first, second);

            System.Collections.Generic.List<AudioSourcePlayback> slots =
                GetField<System.Collections.Generic.List<AudioSourcePlayback>>(
                    GetField<AudioSourcePool>(manager, ReflectionFields.AudioManager.SourcePool),
                    ReflectionFields.AudioSourcePool.Playbacks);
            Assert.AreEqual(0.4f, slots[0].Source.volume);
            Assert.AreEqual(1.5f, slots[0].Source.pitch);
            Assert.AreEqual(0.8f, slots[1].Source.volume);

            Assert.IsTrue(first.Pause(false));
            Assert.AreEqual(AudioPlaybackState.Paused, first.State);
            Assert.IsTrue(second.IsValid, "Pausing one instance must leave another instance of the same clip active.");
            Assert.IsTrue(first.Resume(false));
            Assert.AreNotEqual(AudioPlaybackState.Paused, first.State);
            Assert.IsTrue(first.Stop(false));
            Assert.AreEqual(AudioPlaybackState.Invalid, first.State);
            Assert.IsTrue(second.IsValid);
            Assert.IsTrue(second.Stop(false));
        }

        /// <summary>Verifies that a stale handle cannot control a new playback reusing the same pool slot.</summary>
        [Test]
        public void ReplacedPlaybackHandleCannotControlTheNewSlotGeneration()
        {
            ManagerConfiguration = CreateConfiguration(false);
            SetField(ManagerConfiguration, ReflectionFields.AudioSystemConfiguration.InitialPoolSize, 1);
            SetField(ManagerConfiguration, ReflectionFields.AudioSystemConfiguration.MaximumPoolSize, 1);
            AudioManager manager = CreateManager(ManagerConfiguration, "SoundSystemReplacedHandleManager");

            Assert.IsTrue(manager.TryPlay(Clip, Bus, out AudioPlaybackHandle oldHandle));
            Assert.IsTrue(manager.TryPlay(Clip, Bus, out AudioPlaybackHandle currentHandle));
            Assert.AreEqual(AudioPlaybackState.Invalid, oldHandle.State);
            Assert.IsTrue(currentHandle.IsValid);
            Assert.IsFalse(oldHandle.Stop(false));
            Assert.IsTrue(currentHandle.IsValid);
            Assert.IsTrue(currentHandle.Stop(false));
        }
    }
}
