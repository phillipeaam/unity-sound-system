using System.Collections;
using NUnit.Framework;
using Phillipe2AM.SoundSystem.Configuration;
using Phillipe2AM.SoundSystem.Playback;
using Phillipe2AM.SoundSystem.Tests.Runtime.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

namespace Phillipe2AM.SoundSystem.Tests.Runtime
{
    /// <summary>Pool capacity, replacement eligibility, and source reuse.</summary>
    public sealed class AudioSourcePoolTests : AudioPlaybackTestFixture
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

        /// <summary>Verifies that a full pool replaces the oldest eligible playback and reuses its source.</summary>
        [Test]
        public void PoolReplacesTheOldestMatchingPlaybackAndReusesItsSource()
        {
            AudioSourcePool pool = new AudioSourcePool(Owner.transform, 0, 2, 0f, 5f);
            Assert.IsTrue(pool.Play(Clip, Bus, 0f));
            Assert.IsTrue(pool.Play(Clip, Bus, 0f));
            System.Collections.Generic.List<AudioSourcePlayback> slots =
                GetField<System.Collections.Generic.List<AudioSourcePlayback>>(pool, ReflectionFields.AudioSourcePool.Playbacks);
            AudioSourcePlayback oldest = slots[0];
            AudioSourcePlayback newer = slots[1];
            AudioSource originalSource = oldest.Source;
            uint oldestGeneration = oldest.Generation;
            uint newerGeneration = newer.Generation;

            Assert.IsTrue(pool.Play(Clip, Bus, 0f));
            Assert.AreNotEqual(oldestGeneration, oldest.Generation, "Replacing a slot must create a new handle generation.");
            Assert.AreEqual(newerGeneration, newer.Generation, "The newer eligible playback must remain active.");
            Assert.AreSame(originalSource, oldest.Source, "Replacement must reuse the pooled AudioSource component.");

            pool.StopAll();
        }

        /// <summary>Verifies that a full pool preserves active sounds when the new request has no matching bus and loop category.</summary>
        [Test]
        public void FullPoolRejectsRequestsWithoutAnEligibleBusAndLoopCategory()
        {
            AudioSourcePool pool = new AudioSourcePool(Owner.transform, 0, 1, 0f, 5f);
            Assert.IsTrue(pool.Play(Clip, Bus, 0f));
            AudioSourcePlayback slot = GetField<System.Collections.Generic.List<AudioSourcePlayback>>(
                pool,
                ReflectionFields.AudioSourcePool.Playbacks)[0];
            uint originalGeneration = slot.Generation;

            AudioBusDefinition otherBus = TrackForCleanup(ScriptableObject.CreateInstance<AudioBusDefinition>());
            SetField(otherBus, ReflectionFields.AudioBusDefinition.MixerGroup, Bus.MixerGroup);

            LogAssert.Expect(LogType.Warning,
                "AudioSource pool reached its limit (1) and has no active playback on bus '" + otherBus.name + "' with the requested loop category. The request was skipped.");
            Assert.IsFalse(pool.Play(Clip, otherBus, 0f));
            Assert.AreEqual(originalGeneration, slot.Generation);
            Assert.AreSame(Bus, slot.Bus);

            Assert.IsFalse(pool.Play(Clip, Bus, 0f, loop: true), "A one-shot must not be replaced by a loop request on the same bus.");
            Assert.AreEqual(originalGeneration, slot.Generation);

            pool.StopAll();
            Assert.IsTrue(pool.Play(Clip, otherBus, 0f), "A released slot must be available to another bus.");
            pool.StopAll();
            Object.Destroy(otherBus);
        }
    }
}
