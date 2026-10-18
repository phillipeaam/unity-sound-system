using System.Reflection;
using NUnit.Framework;
using Phillipe2AM.SoundSystem.Configuration;
using Phillipe2AM.SoundSystem.Tests.Runtime.Reflection;
using Phillipe2AM.SoundSystem.Tests.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;

namespace Phillipe2AM.SoundSystem.Tests.Editor
{
    public sealed class AudioSystemConfigurationTests
    {
        private const string MixerAPath = AudioTestResources.FixtureDirectory + AudioTestResources.MixerResourceName + ".mixer";
        private const string MixerBPath = AudioTestResources.FixtureDirectory + "AudioTestMixerB.mixer";

        private AudioSystemConfiguration _configuration;
        private AudioBusDefinition _bus;

        [SetUp]
        public void SetUp()
        {
            _configuration = ScriptableObject.CreateInstance<AudioSystemConfiguration>();
            _bus = ScriptableObject.CreateInstance<AudioBusDefinition>();
            SetField(_bus, ReflectionFields.AudioBusDefinition.Id, "UnitTestBus");
            SetField(_bus, ReflectionFields.AudioBusDefinition.ExposedVolumeParameter, "Master_Volume");
        }

        /// <summary>Configuration starts with automatic app suspension and standard 3D attenuation defaults.</summary>
        [Test]
        public void ApplicationPauseAndSpatialBusDefaultsAreConfigured()
        {
            Assert.IsTrue(_configuration.PausePlaybackOnApplicationSuspend);
            Assert.AreEqual(1f, _bus.MinDistance);
            Assert.AreEqual(500f, _bus.MaxDistance);
            Assert.AreEqual(AudioRolloffMode.Logarithmic, _bus.RolloffMode);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_bus);
            Object.DestroyImmediate(_configuration);
        }

        /// <summary>Authoring validation repairs invalid pool sizes, fade duration and startup timeout.</summary>
        [Test]
        public void OnValidateNormalizesNonFiniteAndOutOfRangeConfiguration()
        {
            SetField(_configuration, ReflectionFields.AudioSystemConfiguration.MaximumPoolSize, 0);
            SetField(_configuration, ReflectionFields.AudioSystemConfiguration.InitialPoolSize, -2);
            SetField(_configuration, ReflectionFields.AudioSystemConfiguration.FadeDurationSeconds, float.NaN);
            SetField(
                _configuration,
                ReflectionFields.AudioSystemConfiguration.PlaybackStartTimeoutSeconds,
                float.PositiveInfinity);

            LogAssert.Expect(LogType.Warning, "Audio System Configuration fade duration was not finite and has been reset to 0 seconds.");
            LogAssert.Expect(LogType.Warning, "Audio System Configuration playback start timeout was not finite and has been reset to 5 seconds.");
            InvokeOnValidate(_configuration);

            Assert.AreEqual(1, _configuration.MaximumPoolSize);
            Assert.AreEqual(0, _configuration.InitialPoolSize);
            Assert.AreEqual(0f, _configuration.FadeDurationSeconds);
            Assert.AreEqual(AudioSystemConfiguration.DefaultPlaybackStartTimeoutSeconds, _configuration.PlaybackStartTimeoutSeconds);
        }

        /// <summary>A zero startup timeout is clamped to the documented minimum.</summary>
        [Test]
        public void OnValidateClampsPlaybackTimeoutToMinimum()
        {
            SetField(_configuration, ReflectionFields.AudioSystemConfiguration.PlaybackStartTimeoutSeconds, 0f);

            InvokeOnValidate(_configuration);

            Assert.AreEqual(
                AudioSystemConfiguration.MinimumPlaybackStartTimeoutSeconds,
                _configuration.PlaybackStartTimeoutSeconds);
        }

        /// <summary>Invalid bus gain is restored to a finite default with an actionable warning.</summary>
        [Test]
        public void BusDefinitionNormalizesNonFiniteDefaultVolume()
        {
            SetField(_bus, ReflectionFields.AudioBusDefinition.DefaultVolumeDb, float.NegativeInfinity);
            LogAssert.Expect(LogType.Warning, $"Audio bus '{_bus.name}' had a non-finite default volume; it was reset to 0 dB.");

            InvokeOnValidate(_bus);

            Assert.AreEqual(0f, _bus.DefaultVolumeDb);
        }

        /// <summary>A bus cannot route through a group owned by another mixer.</summary>
        [Test]
        public void TryValidateBusRejectsGroupOwnedByDifferentMixer()
        {
            AudioMixer mixerA = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerAPath);
            AudioMixer mixerB = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerBPath);
            Assert.IsNotNull(mixerA);
            Assert.IsNotNull(mixerB);

            SetField(_configuration, ReflectionFields.AudioSystemConfiguration.Mixer, mixerA);
            SetField(_bus, ReflectionFields.AudioBusDefinition.MixerGroup, mixerB.FindMatchingGroups("Master")[0]);
            SetField(
                _configuration,
                ReflectionFields.AudioSystemConfiguration.Buses,
                new System.Collections.Generic.List<AudioBusDefinition> { _bus });

            bool isValid = _configuration.TryValidateBus(_bus, false, out string issue);

            Assert.IsFalse(isValid);
            StringAssert.Contains(_bus.name, issue);
            StringAssert.Contains(mixerA.name, issue);
            StringAssert.Contains("Assign a group", issue);
        }

        /// <summary>Volume control rejects a parameter that the configured mixer has not exposed.</summary>
        [Test]
        public void TryValidateBusRejectsUnexposedVolumeParameter()
        {
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerAPath);
            Assert.IsNotNull(mixer);

            SetField(_configuration, ReflectionFields.AudioSystemConfiguration.Mixer, mixer);
            SetField(_bus, ReflectionFields.AudioBusDefinition.MixerGroup, mixer.FindMatchingGroups("Master")[0]);
            SetField(_bus, ReflectionFields.AudioBusDefinition.ExposedVolumeParameter, "Missing_Unit_Test_Parameter");
            SetField(
                _configuration,
                ReflectionFields.AudioSystemConfiguration.Buses,
                new System.Collections.Generic.List<AudioBusDefinition> { _bus });

            bool isValid = _configuration.TryValidateBus(_bus, true, out string issue);

            Assert.IsFalse(isValid);
            StringAssert.Contains("Missing_Unit_Test_Parameter", issue);
            StringAssert.Contains("Expose it or clear the field", issue);
        }

        /// <summary>Spatial defaults require maximum distance to exceed minimum distance.</summary>
        [Test]
        public void TryValidateBusRejectsInvalidSpatialDistanceRange()
        {
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerAPath);
            Assert.IsNotNull(mixer);

            SetField(_configuration, ReflectionFields.AudioSystemConfiguration.Mixer, mixer);
            SetField(_bus, ReflectionFields.AudioBusDefinition.MixerGroup, mixer.FindMatchingGroups("Master")[0]);
            SetField(_bus, ReflectionFields.AudioBusDefinition.ExposedVolumeParameter, string.Empty);
            SetField(_bus, ReflectionFields.AudioBusDefinition.MinDistance, 10f);
            SetField(_bus, ReflectionFields.AudioBusDefinition.MaxDistance, 5f);
            SetField(
                _configuration,
                ReflectionFields.AudioSystemConfiguration.Buses,
                new System.Collections.Generic.List<AudioBusDefinition> { _bus });

            bool isValid = _configuration.TryValidateBus(_bus, false, out string issue);

            Assert.IsFalse(isValid);
            StringAssert.Contains("invalid 3D distances", issue);
        }

        /// <summary>Out-of-range bus lookups return false without retaining an output reference.</summary>
        [Test]
        public void TryGetBusAtReturnsFalseForInvalidIndexes()
        {
            SetField(
                _configuration,
                ReflectionFields.AudioSystemConfiguration.Buses,
                new System.Collections.Generic.List<AudioBusDefinition> { _bus });

            Assert.IsFalse(_configuration.TryGetBusAt(-1, out AudioBusDefinition negativeBus));
            Assert.IsNull(negativeBus);
            Assert.IsFalse(_configuration.TryGetBusAt(1, out AudioBusDefinition pastEndBus));
            Assert.IsNull(pastEndBus);
            Assert.IsTrue(_configuration.TryGetBusAt(0, out AudioBusDefinition validBus));
            Assert.AreSame(_bus, validBus);
        }

        private static void InvokeOnValidate(object target)
        {
            MethodInfo method = target.GetType().GetMethod(
                ReflectionMethods.OnValidate,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(target, null);
        }

        private static void SetField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }
    }
}
