using System.Collections;
using NUnit.Framework;
using Phillipe2AM.SoundSystem.Playback;
using UnityEngine;
using UnityEngine.TestTools;

namespace Phillipe2AM.SoundSystem.Tests.Runtime
{
    /// <summary>Manager ownership, singleton safety and test-fixture lifecycle guarantees.</summary>
    public sealed class AudioManagerLifecycleTests : AudioPlaybackTestFixture
    {
        [UnitySetUp]
        public IEnumerator SetUp() { InitializeAudioTestContext(); yield return null; }

        [UnityTearDown]
        public IEnumerator TearDown() { CleanupAudioTestContext(); yield return null; }

        /// <summary>Unity prevents adding a second manager to the same object, protecting the active singleton.</summary>
        [Test]
        public void ManagerDisallowsDuplicateComponents()
        {
            Assert.IsTrue(System.Attribute.IsDefined(typeof(AudioManager), typeof(DisallowMultipleComponent)));
        }

        /// <summary>All admission shapes retain delays and application-pause ownership when accepted while suspended.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AdmissionDuringApplicationSuspensionUsesSamePolicy(int shape)
        {
            AudioManager manager = CreateManager(CreateConfiguration(false), "Suspended admission manager");
            InvokeApplicationPause(manager, true);
            AudioPlaybackHandle handle;
            bool accepted = shape == 0 ? manager.TryPlay(Clip, Bus, out handle, delaySeconds: 2f) : shape == 1
                ? manager.TryPlayAtPosition(Clip, Bus, Vector3.one, out handle, delaySeconds: 2f)
                : manager.TryPlay(Clip, Bus, Owner.transform, out handle, delaySeconds: 2f);
            Assert.IsTrue(accepted);
            Assert.AreEqual(AudioPlaybackState.Paused, handle.State);
            Assert.IsFalse(AudioListener.pause, "Application suspension must not toggle the global listener.");
            InvokeApplicationPause(manager, false);
            Assert.AreEqual(AudioPlaybackState.Scheduled, handle.State);
            Assert.IsTrue(handle.Stop(false));
        }

        /// <summary>A duplicate manager on another object removes only its own object and leaves playback usable.</summary>
        [UnityTest]
        public IEnumerator DuplicateManagerLeavesActiveManagerUsable()
        {
            AudioManager original = CreateManager(CreateConfiguration(false), "Active test manager");
            GameObject originalObject = ManagerObject;
            var duplicate = TrackForCleanup(new GameObject("Duplicate test manager"));
            duplicate.AddComponent<AudioManager>();
            yield return null;
            Assert.IsTrue(duplicate == null);
            Assert.AreSame(original, AudioManager.Instance);
            Assert.IsTrue(originalObject != null);
            Assert.IsTrue(original.TryPlay(Clip, Bus, out var handle));
            Assert.IsTrue(handle.Stop(false));
        }
    }
}
