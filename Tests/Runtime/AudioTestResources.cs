using UnityEngine.TestTools;

namespace Phillipe2AM.SoundSystem.Tests.Runtime
{
    /// <summary>Stages mixer fixtures only for test runs so ordinary Players contain no test Resources.</summary>
    public sealed class AudioTestResources : IPrebuildSetup, IPostBuildCleanup
    {
        public const string MixerResourceName = "AudioTestMixerA";
        public const string FixtureDirectory = "Packages/com.phillipe2am.soundsystem/Tests/Runtime/Fixtures/";
        private const string GeneratedDirectory = "Assets/SoundSystemTestResources";
        private const string ResourcesDirectory = GeneratedDirectory + "/Resources";
        private const string GeneratedMixerPath = ResourcesDirectory + "/" + MixerResourceName + ".mixer";

        public void Setup()
        {
#if UNITY_EDITOR
            if (!UnityEditor.AssetDatabase.IsValidFolder(GeneratedDirectory))
                UnityEditor.AssetDatabase.CreateFolder("Assets", "SoundSystemTestResources");
            if (!UnityEditor.AssetDatabase.IsValidFolder(ResourcesDirectory))
                UnityEditor.AssetDatabase.CreateFolder(GeneratedDirectory, "Resources");
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>(GeneratedMixerPath) == null &&
                !UnityEditor.AssetDatabase.CopyAsset(FixtureDirectory + MixerResourceName + ".mixer", GeneratedMixerPath))
                throw new System.InvalidOperationException("Could not stage the Sound System test mixer.");
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.DeleteAsset(GeneratedDirectory);
#endif
        }
    }
}
