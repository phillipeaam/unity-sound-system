using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Phillipe2AM.SoundSystem.Configuration;
using Phillipe2AM.SoundSystem.Tests.Runtime.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;

namespace Phillipe2AM.SoundSystem.Tests.Runtime
{
    /// <summary>Shared Unity objects, configuration, and helpers for runtime audio tests.</summary>
    [PrebuildSetup(typeof(AudioTestResources))]
    [PostBuildCleanup(typeof(AudioTestResources))]
    public abstract class AudioPlaybackTestFixture
    {
        private const string TestMixerResourceName = AudioTestResources.MixerResourceName;
        private const string TestBusId = "RuntimeTestBus";
        private const string TestPreferenceKeyPrefix = "SoundSystemUnitTest";
        protected const string TestBusVolumePreferenceKey = TestPreferenceKeyPrefix + "." + TestBusId + ".VolumeDb";

        private AudioMixer _mixer;
        private readonly List<Object> _extraObjects = new();

        protected AudioBusDefinition Bus;
        protected AudioClip Clip;
        protected GameObject Owner;
        protected GameObject ManagerParent;
        protected GameObject ManagerObject;
        protected AudioSystemConfiguration ManagerConfiguration;

        protected void InitializeAudioTestContext()
        {
            AudioListener.pause = false;
            _mixer = Resources.Load<AudioMixer>(TestMixerResourceName);
            Assert.IsNotNull(_mixer, "The package's runtime test mixer fixture must load from Resources.");

            Owner = new GameObject("SoundSystemTestOwner");
            Owner.AddComponent<AudioListener>();
            Bus = ScriptableObject.CreateInstance<AudioBusDefinition>();
            SetField(Bus, ReflectionFields.AudioBusDefinition.Id, TestBusId);
            SetField(
                Bus,
                ReflectionFields.AudioBusDefinition.MixerGroup,
                _mixer.FindMatchingGroups("Master")[0]);
            SetField(Bus, ReflectionFields.AudioBusDefinition.ExposedVolumeParameter, "Master_Volume");
            SetField(Bus, ReflectionFields.AudioBusDefinition.DefaultVolumeDb, -6f);
            Clip = AudioClip.Create("SoundSystemTestClip", 4410, 1, 44100, false);
        }

        protected void CleanupAudioTestContext()
        {
            AudioListener.pause = false;
            PlayerPrefs.DeleteKey(TestBusVolumePreferenceKey);

            foreach (Object extra in _extraObjects)
            {
                if (extra != null) Object.Destroy(extra);
            }
            _extraObjects.Clear();

            if (ManagerObject != null)
            {
                Object.Destroy(ManagerObject);
            }

            if (ManagerParent != null)
            {
                Object.Destroy(ManagerParent);
            }

            if (ManagerConfiguration != null)
            {
                Object.Destroy(ManagerConfiguration);
            }

            if (Owner != null)
            {
                Object.Destroy(Owner);
            }

            if (Bus != null)
            {
                Object.Destroy(Bus);
            }

            if (Clip != null)
            {
                Object.Destroy(Clip);
            }
        }

        protected AudioSystemConfiguration CreateConfiguration(bool persist)
        {
            AudioSystemConfiguration configuration = TrackForCleanup(ScriptableObject.CreateInstance<AudioSystemConfiguration>());
            SetField(configuration, ReflectionFields.AudioSystemConfiguration.Mixer, _mixer);
            SetField(
                configuration,
                ReflectionFields.AudioSystemConfiguration.PersistVolumePreferences,
                persist);
            SetField(
                configuration,
                ReflectionFields.AudioSystemConfiguration.PreferenceKeyPrefix,
                TestPreferenceKeyPrefix);
            SetField(
                configuration,
                ReflectionFields.AudioSystemConfiguration.Buses,
                new System.Collections.Generic.List<AudioBusDefinition> { Bus });
            return configuration;
        }

        /// <summary>Registers an additional fixture-owned Unity object before any assertion can fail.</summary>
        protected T TrackForCleanup<T>(T value) where T : Object
        {
            _extraObjects.Add(value);
            return value;
        }

        /// <summary>Creates a configured manager without running Awake before its references are assigned.</summary>
        protected AudioManager CreateManager(AudioSystemConfiguration configuration, string name)
        {
            ManagerConfiguration = configuration;
            ManagerObject = new GameObject(name);
            ManagerObject.SetActive(false);
            AudioManager manager = ManagerObject.AddComponent<AudioManager>();
            SetField(manager, ReflectionFields.AudioManager.Configuration, configuration);
            SetField(manager, ReflectionFields.AudioManager.PersistAcrossScenes, false);
            ManagerObject.SetActive(true);
            return manager;
        }

        protected static void SetField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }

        protected static T GetField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return (T)field.GetValue(target);
        }

        protected static void InvokeApplicationPause(AudioManager manager, bool paused)
        {
            MethodInfo method =
                typeof(AudioManager).GetMethod(
                    ReflectionMethods.AudioManager.OnApplicationPause,
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(manager, new object[] { paused });
        }
    }
}
