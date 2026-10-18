using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Phillipe2AM.SoundSystem.Configuration;
using Phillipe2AM.SoundSystem.Tests.Runtime.Reflection;
using Phillipe2AM.SoundSystem.Volume;
using UnityEngine;
using UnityEngine.TestTools;

namespace Phillipe2AM.SoundSystem.Tests.Runtime
{
    /// <summary>Volume initialization, validation, and preference restoration.</summary>
    public sealed class AudioVolumeControllerTests : AudioPlaybackTestFixture
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

        /// <summary>Verifies that volume requests made before initialization override configured defaults afterward.</summary>
        [Test]
        public void EarlyVolumeRequestIsAppliedAfterInitialDefaults()
        {
            AudioSystemConfiguration configuration = CreateConfiguration(false);
            AudioVolumeController controller = new AudioVolumeController(configuration);

            Assert.IsTrue(controller.SetBusVolume(Bus, 12f));
            Assert.AreEqual(12f, controller.GetBusVolume(Bus));

            controller.ApplyInitialBusVolumes();

            Assert.That(controller.GetBusVolume(Bus), Is.EqualTo(12f).Within(0.1f));
            Object.Destroy(configuration);
        }

        /// <summary>Verifies that volume controls reject non-finite values and clamp finite values to the supported range.</summary>
        [Test]
        public void VolumeApiRejectsNonFiniteValuesAndClampsFiniteValues()
        {
            AudioSystemConfiguration configuration = CreateConfiguration(false);
            AudioVolumeController controller = new AudioVolumeController(configuration);
            controller.ApplyInitialBusVolumes();

            Assert.IsFalse(controller.SetBusVolume(Bus, float.NaN));
            Assert.IsFalse(controller.SetBusVolume(Bus, float.PositiveInfinity));
            Assert.IsTrue(controller.SetBusVolume(Bus, 100f));
            Assert.That(controller.GetBusVolume(Bus), Is.EqualTo(AudioSystemConfiguration.MaximumVolumeDb).Within(0.1f));
            Assert.IsTrue(controller.SetBusVolume(Bus, -100f));
            Assert.That(controller.GetBusVolume(Bus), Is.EqualTo(AudioSystemConfiguration.MinimumVolumeDb).Within(0.1f));

            Object.Destroy(configuration);
        }

        /// <summary>Verifies that an invalid persisted volume falls back to the bus's configured default.</summary>
        [Test]
        public void PersistedNonFiniteVolumeFallsBackToConfiguredDefault()
        {
            AudioSystemConfiguration configuration = CreateConfiguration(true);
            PlayerPrefs.SetFloat(TestBusVolumePreferenceKey, float.PositiveInfinity);
            AudioVolumeController controller = new AudioVolumeController(configuration);

            controller.ApplyInitialBusVolumes();

            Assert.That(controller.GetBusVolume(Bus), Is.EqualTo(Bus.DefaultVolumeDb).Within(0.1f));
            Object.Destroy(configuration);
        }

        /// <summary>Verifies logarithmic percentage mapping, finite-value clamping, and non-finite rejection.</summary>
        [Test]
        public void PercentageVolumeMapsToDecibelsAndClampsFiniteValues()
        {
            AudioSystemConfiguration configuration = CreateConfiguration(false);
            AudioVolumeController controller = new AudioVolumeController(configuration);
            controller.ApplyInitialBusVolumes();

            Assert.IsFalse(controller.SetBusVolumePercent(Bus, float.NaN));
            Assert.IsFalse(controller.SetBusVolumePercent(Bus, float.PositiveInfinity));
            Assert.IsFalse(controller.SetBusVolumePercent(Bus, float.NegativeInfinity));

            Assert.IsTrue(controller.SetBusVolumePercent(Bus, 50f));
            Assert.That(controller.GetBusVolume(Bus), Is.EqualTo(Bus.DefaultVolumeDb - 6.0206f).Within(0.1f));

            Assert.IsTrue(controller.SetBusVolumePercent(Bus, -10f));
            Assert.That(controller.GetBusVolume(Bus), Is.EqualTo(AudioSystemConfiguration.MinimumVolumeDb).Within(0.1f));

            Assert.IsTrue(controller.SetBusVolumePercent(Bus, 150f));
            Assert.That(controller.GetBusVolume(Bus), Is.EqualTo(Bus.DefaultVolumeDb).Within(0.1f));

            Object.Destroy(configuration);
        }

        /// <summary>Verifies percentage reads at mute, at the configured default, and after a default change.</summary>
        [Test]
        public void PercentageGetterUsesDefaultAndCapsSavedLevelAfterDefaultChanges()
        {
            AudioSystemConfiguration configuration = CreateConfiguration(true);
            AudioVolumeController controller = new AudioVolumeController(configuration);
            controller.ApplyInitialBusVolumes();

            Assert.That(controller.GetBusVolumePercent(Bus), Is.EqualTo(100f).Within(0.1f));
            Assert.IsTrue(controller.SetBusVolumePercent(Bus, 0f));
            Assert.That(controller.GetBusVolumePercent(Bus), Is.EqualTo(0f).Within(0.1f));

            Assert.IsTrue(controller.SetBusVolumePercent(Bus, 100f));
            Assert.That(PlayerPrefs.GetFloat(TestBusVolumePreferenceKey), Is.EqualTo(Bus.DefaultVolumeDb).Within(0.1f));

            SetField(Bus, ReflectionFields.AudioBusDefinition.DefaultVolumeDb, -12f);
            Assert.That(controller.GetBusVolumePercent(Bus), Is.EqualTo(100f).Within(0.1f));
            Assert.That(controller.GetBusVolume(Bus), Is.EqualTo(-6f).Within(0.1f),
                "Changing the default must not rewrite the previously saved effective dB level.");

            Assert.IsTrue(controller.SetBusVolumePercent(Bus, 100f));
            Assert.That(controller.GetBusVolume(Bus), Is.EqualTo(-12f).Within(0.1f));
            Assert.That(PlayerPrefs.GetFloat(TestBusVolumePreferenceKey), Is.EqualTo(-12f).Within(0.1f));

            Object.Destroy(configuration);
        }

        /// <summary>Verifies percentage requests made before initialization stay queued and persist as effective dB.</summary>
        [Test]
        public void EarlyPercentageRequestIsAppliedAndPersistedAfterInitialization()
        {
            AudioSystemConfiguration configuration = CreateConfiguration(true);
            AudioVolumeController controller = new AudioVolumeController(configuration);

            Assert.IsTrue(controller.SetBusVolumePercent(Bus, 25f));
            Assert.That(controller.GetBusVolumePercent(Bus), Is.EqualTo(25f).Within(0.1f));

            controller.ApplyInitialBusVolumes();

            float expectedDecibels = Bus.DefaultVolumeDb + 20f * Mathf.Log10(0.25f);
            Assert.That(controller.GetBusVolume(Bus), Is.EqualTo(expectedDecibels).Within(0.1f));
            Assert.That(PlayerPrefs.GetFloat(TestBusVolumePreferenceKey), Is.EqualTo(expectedDecibels).Within(0.1f));

            Object.Destroy(configuration);
        }

        /// <summary>Verifies percentage changes affect only the requested bus mixer parameter.</summary>
        [Test]
        public void PercentageVolumeChangesAreIsolatedPerBus()
        {
            AudioSystemConfiguration configuration = CreateConfiguration(false);
            AudioBusDefinition secondaryBus = TrackForCleanup(ScriptableObject.CreateInstance<AudioBusDefinition>());
            SetField(secondaryBus, ReflectionFields.AudioBusDefinition.Id, "RuntimeTestBusSecondary");
            SetField(
                secondaryBus,
                ReflectionFields.AudioBusDefinition.MixerGroup,
                configuration.Mixer.FindMatchingGroups("Background")[0]);
            SetField(secondaryBus, ReflectionFields.AudioBusDefinition.ExposedVolumeParameter, "Background_Volume");
            SetField(secondaryBus, ReflectionFields.AudioBusDefinition.DefaultVolumeDb, -18f);

            SetField(
                configuration,
                ReflectionFields.AudioSystemConfiguration.Buses,
                new List<AudioBusDefinition> { Bus, secondaryBus });
            AudioVolumeController controller = new AudioVolumeController(configuration);
            controller.ApplyInitialBusVolumes();

            Assert.IsTrue(controller.SetBusVolumePercent(Bus, 50f));
            Assert.That(controller.GetBusVolume(secondaryBus), Is.EqualTo(-18f).Within(0.1f));

            Assert.IsTrue(controller.SetBusVolumePercent(secondaryBus, 25f));
            Assert.That(controller.GetBusVolume(Bus), Is.EqualTo(-12.0206f).Within(0.1f));
            Assert.That(controller.GetBusVolumePercent(secondaryBus), Is.EqualTo(25f).Within(0.1f));

            Object.Destroy(configuration);
            Object.Destroy(secondaryBus);
        }

        /// <summary>Verifies the AudioManager exposes the percentage volume API before initialization completes.</summary>
        [Test]
        public void AudioManagerExposesPercentageVolumeControls()
        {
            ManagerConfiguration = CreateConfiguration(false);
            AudioManager manager = CreateManager(ManagerConfiguration, "SoundSystemPercentageVolumeManager");

            Assert.IsTrue(manager.SetBusVolumePercent(Bus, 40f));
            Assert.That(manager.GetBusVolumePercent(Bus), Is.EqualTo(40f).Within(0.1f));
        }
    }
}
