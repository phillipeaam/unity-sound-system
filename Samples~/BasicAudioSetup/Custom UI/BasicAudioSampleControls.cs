using System.Collections.Generic;
using Phillipe2AM.SoundSystem;
using Phillipe2AM.SoundSystem.Configuration;
using Phillipe2AM.SoundSystem.Playback;
using UnityEngine;

namespace Samples.BasicAudioSetup
{
    /// <summary>
    /// Runtime setup and lifetime for the Sound System package's importable sample scene.
    /// </summary>
    public sealed partial class BasicAudioSampleControls : MonoBehaviour
    {
        private static readonly string[] RolloffModeLabels = { "Logarithmic", "Linear" };

        private const float ControlPanelWidth = 400f;
        private const float ControlPanelHeight = 620f;

        private sealed class PositionIndicator
        {
            public GameObject GameObject;
            public Transform FollowTarget;
            public AudioPlaybackHandle PlaybackHandle;
        }

        [SerializeField]
        private AudioBusDefinition _musicBus;

        [SerializeField]
        private AudioBusDefinition _sfxBus;

        [SerializeField, Tooltip("Sample-owned shader referenced by the scene so Player builds retain the visual markers.")]
        private Shader _visualShader;

        private float _delaySeconds;
        private bool _useFade = true;
        private int _section = 1;
        private int _selectedBus;
        private int _selectedClip;
        private string _controlResult = "Start a playback, then try individual or bulk control.";
        private AudioPlaybackHandle _otherPlaybackHandle;
        private bool _showPanel = true;
        private GUIStyle _bodyStyle;
        private GUIStyle _titleStyle;
        private static readonly string[] SectionLabels = { "Setup", "Play", "Controls", "Volume", "Pool" };
        private static readonly string[] BusLabels = { "Music", "SFX" };
        private static readonly string[] ClipLabels = { "Music", "2D SFX", "Follow", "Pulse" };

        private AudioClip _musicTone;
        private AudioClip _sfxTone;
        private AudioClip _followTone;
        private AudioClip _positionTone;
        private GameObject _movingBeacon;
        private GameObject _listenerMarker;
        private Material _beaconMaterial;
        private Material _listenerMarkerMaterial;
        private Material _followIndicatorMaterial;
        private Material _fixedIndicatorMaterial;
        private readonly List<PositionIndicator> _activePositionIndicators = new();
        private Vector2 _controlPanelScrollPosition;
        private AudioPlaybackHandle _lastPlaybackHandle;
        private string _lastPlaybackLabel = "No individual playback selected.";
        private string _otherPlaybackLabel = "No comparison playback selected.";
        private bool _loopPlayback;
        private bool _overrideSpatialSettings;
        private float _playbackVolume = 1f;
        private float _playbackPitch = 1f;
        private float _minDistance = 1f;
        private float _maxDistance = 25f;
        private AudioRolloffMode _rolloffMode = AudioRolloffMode.Logarithmic;
        private float _musicVolumePercent = 100f;
        private float _sfxVolumePercent = 100f;
        private bool _volumeSlidersInitialized;
        private float _motionTime;
        private string _spatialStatus = "Press a 3D button.";
        private GUIStyle _legendStyle;

        private void Awake()
        {
            _musicTone = CreateTone("Sample Music Tone", 220f, 330f, 2f, true);
            _sfxTone = CreateTone("Sample SFX Tone", 880f, 0f, 0.2f, false);
            _followTone = CreateTone("Follow Beacon Pulse", 520f, 780f, 2.5f, false);
            _positionTone = CreateTone("Position Snapshot Pulse", 260f, 130f, 1.1f, false);
            CreateListenerMarker();
            CreateMovingBeacon();
            CreateAudioPositionIndicators();
        }

        private void Update()
        {
            InitializeVolumeSliders();

            if (_movingBeacon == null)
            {
                UpdateAudioPositionIndicators();
                return;
            }

            _motionTime += Time.deltaTime;
            _movingBeacon.transform.position = new Vector3(
                Mathf.Sin(_motionTime * 1.1f) * 5.5f,
                Mathf.Sin(_motionTime * 1.7f) * 0.8f,
                8f);
            UpdateAudioPositionIndicators();
        }

        private void OnDestroy()
        {
            AudioManager manager = AudioManager.Instance;

            if (manager != null && _sfxBus != null)
            {
                manager.StopBus(_sfxBus, false);
            }

            if (manager != null && _musicBus != null)
            {
                manager.StopBus(_musicBus, false);
            }

            if (_musicTone != null)
            {
                Destroy(_musicTone);
            }

            if (_sfxTone != null)
            {
                Destroy(_sfxTone);
            }

            if (_followTone != null)
            {
                Destroy(_followTone);
            }

            if (_positionTone != null)
            {
                Destroy(_positionTone);
            }

            if (_movingBeacon != null)
            {
                Destroy(_movingBeacon);
            }

            if (_listenerMarker != null)
            {
                Destroy(_listenerMarker);
            }

            if (_beaconMaterial != null)
            {
                Destroy(_beaconMaterial);
            }

            if (_listenerMarkerMaterial != null)
            {
                Destroy(_listenerMarkerMaterial);
            }

            foreach (PositionIndicator indicator in _activePositionIndicators)
            {
                DestroyPositionIndicator(indicator);
            }

            if (_followIndicatorMaterial != null)
            {
                Destroy(_followIndicatorMaterial);
            }

            if (_fixedIndicatorMaterial != null)
            {
                Destroy(_fixedIndicatorMaterial);
            }
        }

    }
}
