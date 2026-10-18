using Phillipe2AM.SoundSystem;
using Phillipe2AM.SoundSystem.Configuration;
using Phillipe2AM.SoundSystem.Playback;
using UnityEngine;

namespace Samples.BasicAudioSetup
{
    public sealed partial class BasicAudioSampleControls
    {
        private void OnGUI()
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            float scale = CalculateUiScale();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale) * previousMatrix;
            if (!_showPanel)
            {
                if (GUI.Button(new Rect(10f, 10f, 150f, 30f), "Show audio controls")) _showPanel = true;
                GUI.matrix = previousMatrix;
                return;
            }

            _bodyStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
            _titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _legendStyle ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            Rect panel = new Rect(10f, 10f, ControlPanelWidth, ControlPanelHeight);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.08f, 0.13f, 0.22f, 1f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUILayout.BeginArea(panel, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sound System / Basic Audio Setup", _titleStyle);
            if (GUILayout.Button("Hide", GUILayout.Width(42f))) _showPanel = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=#FFD700>Gold: listener (you).</color> <color=#26D9FF>Cyan: moving target.</color>", _legendStyle);
            GUILayout.Label("<color=#31E65A>Green: follows.</color> <color=#FF4DCC>Magenta: stays in place.</color>", _legendStyle);
            GUILayout.Label("Try headphones. Options apply to the next request.", _bodyStyle);
            int section = GUILayout.Toolbar(_section, SectionLabels, GUILayout.Height(28f));
            if (section != _section) { _section = section; _controlPanelScrollPosition = Vector2.zero; }
            _controlPanelScrollPosition = GUILayout.BeginScrollView(_controlPanelScrollPosition);
            AudioManager manager = AudioManager.Instance;
            if (manager == null || manager.Configuration == null)
            {
                GUILayout.Label("AudioManager unavailable. Assign the sample configuration to its manager.", _bodyStyle);
            }
            else
            {
                switch (_section)
                {
                    case 0: DrawSetup(manager); break;
                    case 1: DrawPlayback(manager); break;
                    case 2: DrawControls(manager); break;
                    case 3: DrawVolumes(manager); break;
                    case 4: DrawPool(manager); break;
                }
                GUILayout.Space(12f);
                GUILayout.Label(_spatialStatus, _bodyStyle);
                GUILayout.Space(8f);
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Runtime demo • generated tones", _bodyStyle);
            if (manager != null && GUILayout.Button("Stop all", GUILayout.Width(85f))) StopAllSampleAudio(manager);
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
        }

        private void DrawSetup(AudioManager manager)
        {
            AudioSystemConfiguration configuration = manager.Configuration;
            GUILayout.Label("1. Mixer → bus assets → configuration → manager", _titleStyle);
            GUILayout.Label("Each project owns its mixer, bus definitions and configuration. This sample supplies its own assets in Audio/.", _bodyStyle);
            string mixerName = configuration.Mixer != null ? configuration.Mixer.name : "Missing — assign the sample mixer in the configuration asset";
            GUILayout.Label($"Configuration: {configuration.name}\nMixer: {mixerName}\nBuses: {configuration.BusCount}\nPool: {configuration.InitialPoolSize} initial / {configuration.MaximumPoolSize} maximum\nFade: {configuration.FadeDurationSeconds:0.##} s\nStart timeout: {configuration.PlaybackStartTimeoutSeconds:0.##} s\nSave preferences: {configuration.PersistVolumePreferences}\nPause on app suspend: {configuration.PausePlaybackOnApplicationSuspend}", _bodyStyle);
            GUILayout.Space(8f);
            GUILayout.Label("2. Start with Play, then explore Controls and Volume", _titleStyle);
            GUILayout.Label("TryPlay returns acceptance plus a handle for one instance. TryPlayAtPosition takes a world position; the Transform overload follows a target. Pass out _ for fire-and-forget.", _bodyStyle);
            GUILayout.Label("The gold capsule owns the only AudioListener. The camera only frames the scene. Music and SFX are sample choices; your project can define any buses.", _bodyStyle);
            GUILayout.Space(8f);
            GUILayout.Label("3. Check real application suspension in a Player", _titleStyle);
            GUILayout.Label("Start a delayed sound or loop, pause another manually, background the app, then return. Only automatic pauses should resume; remaining delays and manual pauses are preserved. The Editor toolbar Pause is not application suspension.", _bodyStyle);
            GUILayout.Label("This demo uses IMGUI and runtime visuals. Profile the package separately from this educational interface.", _bodyStyle);
        }

        private void DrawPlayback(AudioManager manager)
        {
            GUILayout.Label("Choose settings, then request a sound", _titleStyle);
            _loopPlayback = GUILayout.Toggle(_loopPlayback, "Loop (off = one-shot)");
            DrawPlaybackSlider("Source volume", ref _playbackVolume, 0f, 1f, true);
            DrawPlaybackSlider("Pitch / speed", ref _playbackPitch, 0.1f, 3f);
            DrawPlaybackSlider("Delay (seconds)", ref _delaySeconds, 0f, 5f);
            GUILayout.Label("2D • no position or distance falloff", _titleStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("TryPlay • Music")) PlayMusic(manager);
            if (GUILayout.Button("TryPlay • SFX")) PlayTwoDimensional(manager);
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            GUILayout.Label("3D • defaults from the SFX bus", _titleStyle);
            _overrideSpatialSettings = GUILayout.Toggle(_overrideSpatialSettings, "Override distance and rolloff for the next call");
            if (_overrideSpatialSettings)
            {
                DrawPlaybackSlider("Min distance", ref _minDistance, 0f, 10f);
                _maxDistance = Mathf.Max(_maxDistance, _minDistance + 0.1f);
                DrawPlaybackSlider("Max distance", ref _maxDistance, _minDistance + 0.1f, 80f);
                int index = GUILayout.Toolbar(_rolloffMode == AudioRolloffMode.Linear ? 1 : 0, RolloffModeLabels);
                _rolloffMode = index == 1 ? AudioRolloffMode.Linear : AudioRolloffMode.Logarithmic;
            }
            else if (_sfxBus != null)
                GUILayout.Label($"Inherited: {_sfxBus.MinDistance:0.#}–{_sfxBus.MaxDistance:0.#} units / {_sfxBus.RolloffMode}", _bodyStyle);
            GUILayout.Label("Linear reaches silence at max distance. Logarithmic does not use it as a hard cutoff.", _bodyStyle);
            GUILayout.BeginHorizontal();
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && _movingBeacon != null;
            if (GUILayout.Button("TryPlay • Follow target")) PlayFollowingBeacon(manager);
            if (GUILayout.Button("TryPlayAtPosition • Pulse")) PlayFixedPulse(manager);
            GUI.enabled = wasEnabled;
            GUILayout.EndHorizontal();
            if (GUILayout.Button(_movingBeacon != null ? "Destroy target (sound keeps its last position)" : "Restore moving target"))
            {
                if (_movingBeacon != null) Destroy(_movingBeacon);
                else CreateMovingBeacon();
            }
            GUILayout.Label($"Last: {_lastPlaybackLabel} / {_lastPlaybackHandle.State}", _bodyStyle);
        }

        private void DrawControls(AudioManager manager)
        {
            GUILayout.Label("Control one playback by handle", _titleStyle);
            if (GUILayout.Button("Start two matching SFX loops")) PlayComparisonPair(manager);
            AudioPlaybackState state = _lastPlaybackHandle.State;
            GUILayout.Label($"Selected: {_lastPlaybackLabel}\nState: {state} | IsValid: {_lastPlaybackHandle.IsValid} | IsPlaying: {_lastPlaybackHandle.IsPlaying}\nOther: {_otherPlaybackLabel} / {_otherPlaybackHandle.State}", _bodyStyle);
            _useFade = GUILayout.Toggle(_useFade, $"Use fade ({manager.Configuration.FadeDurationSeconds:0.##} s)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("handle.Pause")) _controlResult = $"Pause accepted: {_lastPlaybackHandle.Pause(_useFade)}";
            if (GUILayout.Button("handle.Resume")) _controlResult = $"Resume accepted: {_lastPlaybackHandle.Resume(_useFade)}";
            if (GUILayout.Button("handle.Stop")) _controlResult = $"Stop accepted: {_lastPlaybackHandle.Stop(_useFade)}";
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Swap selected / other handle"))
            {
                (_lastPlaybackHandle, _otherPlaybackHandle) = (_otherPlaybackHandle, _lastPlaybackHandle);
                (_lastPlaybackLabel, _otherPlaybackLabel) = (_otherPlaybackLabel, _lastPlaybackLabel);
            }
            GUILayout.Label("Ended or replaced handles report Invalid. Reusing a slot cannot make an old handle valid again.", _bodyStyle);
            GUILayout.Space(10f);
            GUILayout.Label("Control all matching instances", _titleStyle);
            _selectedBus = GUILayout.Toolbar(_selectedBus, BusLabels);
            AudioBusDefinition bus = _selectedBus == 0 ? _musicBus : _sfxBus;
            GUILayout.Label($"IsBusPlaying: {manager.IsBusPlaying(bus)}", _bodyStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("PauseBus")) _controlResult = $"PauseBus affected: {manager.PauseBus(bus, _useFade)}";
            if (GUILayout.Button("ResumeBus")) _controlResult = $"ResumeBus affected: {manager.ResumeBus(bus, _useFade)}";
            if (GUILayout.Button("StopBus")) _controlResult = $"StopBus affected: {manager.StopBus(bus, _useFade)}";
            GUILayout.EndHorizontal();
            _selectedClip = GUILayout.Toolbar(_selectedClip, ClipLabels);
            AudioClip clip = GetSelectedClip();
            GUILayout.Label($"IsClipPlaying: {manager.IsClipPlaying(clip)}", _bodyStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("PauseClip")) _controlResult = $"PauseClip affected: {manager.PauseClip(clip, _useFade)}";
            if (GUILayout.Button("ResumeClip")) _controlResult = $"ResumeClip affected: {manager.ResumeClip(clip, _useFade)}";
            if (GUILayout.Button("StopClip")) _controlResult = $"StopClip affected: {manager.StopClip(clip, _useFade)}";
            GUILayout.EndHorizontal();
            GUILayout.Label(_controlResult, _bodyStyle);
        }

        private AudioClip GetSelectedClip() => _selectedClip switch
        {
            0 => _musicTone,
            1 => _sfxTone,
            2 => _followTone,
            _ => _positionTone
        };

        private void DrawVolumes(AudioManager manager)
        {
            GUILayout.Label("Bus gain • independent of source volume", _titleStyle);
            GUILayout.Label("0% = mute. 100% = configured DefaultVolumeDb. At 50%, gain is approximately 6 dB below that default.", _bodyStyle);
            DrawBusVolumeSlider(manager, _musicBus, "Music", ref _musicVolumePercent);
            DrawBusVolumeSlider(manager, _sfxBus, "SFX", ref _sfxVolumePercent);
            if (GUILayout.Button("Reset both to 100%"))
            {
                ResetBusVolume(manager, _musicBus, ref _musicVolumePercent);
                ResetBusVolume(manager, _sfxBus, ref _sfxVolumePercent);
            }
            GUILayout.Space(12f);
            GUILayout.Label("Absolute decibel API • -80 to +20 dB", _titleStyle);
            GUILayout.Label("This uses the same mixer parameter and saved value. A level above the bus default still reads at most 100%.", _bodyStyle);
            DrawDecibelSlider(manager, _musicBus, "Music");
            DrawDecibelSlider(manager, _sfxBus, "SFX");
            GUILayout.Label($"Preference persistence: {manager.Configuration.PersistVolumePreferences}. This sample disables saving so experiments do not change your next session.", _bodyStyle);
        }

        private void DrawDecibelSlider(AudioManager manager, AudioBusDefinition bus, string label)
        {
            if (bus == null)
            {
                GUILayout.Label($"{label}: assign its bus asset to the sample controls.", _bodyStyle);
                return;
            }

            float current = manager.GetBusVolume(bus);
            GUILayout.Label($"{label}: {current:0.0} dB / {manager.GetBusVolumePercent(bus):0}% (default {bus.DefaultVolumeDb:0.0} dB)", _bodyStyle);
            float next = GUILayout.HorizontalSlider(current, AudioSystemConfiguration.MinimumVolumeDb, AudioSystemConfiguration.MaximumVolumeDb);
            if (!Mathf.Approximately(current, next) && manager.SetBusVolume(bus, next))
            {
                _musicVolumePercent = manager.GetBusVolumePercent(_musicBus);
                _sfxVolumePercent = manager.GetBusVolumePercent(_sfxBus);
            }
        }

        private void DrawPool(AudioManager manager)
        {
            GUILayout.Label($"Bounded pool • {manager.Configuration.MaximumPoolSize} slots", _titleStyle);
            GUILayout.Label("These isolated scenarios stop sample playback first. Loops use low source gain to keep the combined output comfortable.", _bodyStyle);
            if (GUILayout.Button("Replace oldest • same SFX bus + loop")) FillSfxPool(manager);
            GUILayout.Label("Sends max + 1 requests. All are accepted; the first handle becomes Invalid, and the newest stays valid. Inspect the manager: source count stays within its limit.", _bodyStyle);
            GUILayout.Space(8f);
            if (GUILayout.Button("Reject • different bus or loop category")) DemonstrateRejection(manager);
            GUILayout.Label("Fills the pool with SFX loops. Requests for Music and an SFX one-shot are rejected. Current SFX loops continue. One capacity warning is expected in the Console.", _bodyStyle);
            GUILayout.Label($"First retained handle: {_otherPlaybackHandle.State}\nNewest handle: {_lastPlaybackHandle.State}", _bodyStyle);
        }

        private static void DrawPlaybackSlider(
            string label,
            ref float value,
            float minimum,
            float maximum,
            bool displayAsPercentage = false)
        {
            GUILayout.BeginHorizontal();
            string displayedValue = displayAsPercentage ? $"{value * 100f:0}%" : $"{value:0.0}";
            GUILayout.Label($"{label}: {displayedValue}", GUILayout.Width(146f));
            value = GUILayout.HorizontalSlider(value, minimum, maximum);
            GUILayout.EndHorizontal();
        }

        private static float CalculateUiScale()
        {
            const float targetScreenHeightOccupancy = 0.88f;
            const float screenPadding = 20f;
            const float minimumScale = 0.05f;
            const float maximumScale = 4f;

            float targetScale = Screen.height * targetScreenHeightOccupancy / ControlPanelHeight;
            float widthFitScale = (Screen.width - screenPadding) / ControlPanelWidth;
            float heightFitScale = (Screen.height - screenPadding) / ControlPanelHeight;
            float fitScale = Mathf.Min(widthFitScale, heightFitScale);
            return Mathf.Clamp(Mathf.Min(targetScale, fitScale), minimumScale, maximumScale);
        }

        private void InitializeVolumeSliders()
        {
            AudioManager manager = AudioManager.Instance;
            if (manager != null)
            {
                InitializeVolumeSliders(manager);
            }
        }

        private void InitializeVolumeSliders(AudioManager manager)
        {
            if (_volumeSlidersInitialized || manager == null)
            {
                return;
            }

            _musicVolumePercent = manager.GetBusVolumePercent(_musicBus);
            _sfxVolumePercent = manager.GetBusVolumePercent(_sfxBus);
            _volumeSlidersInitialized = true;
        }

        private static void DrawBusVolumeSlider(
            AudioManager manager,
            AudioBusDefinition bus,
            string label,
            ref float volumePercent)
        {
            if (bus == null)
            {
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {volumePercent:0}%", GUILayout.Width(82f));
            float nextVolumePercent = GUILayout.HorizontalSlider(volumePercent, 0f, 100f);
            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(nextVolumePercent, volumePercent) &&
                manager.SetBusVolumePercent(bus, nextVolumePercent))
            {
                volumePercent = manager.GetBusVolumePercent(bus);
            }
        }

        private static void ResetBusVolume(AudioManager manager, AudioBusDefinition bus, ref float volumePercent)
        {
            if (bus != null && manager.SetBusVolumePercent(bus, 100f))
            {
                volumePercent = manager.GetBusVolumePercent(bus);
            }
        }

    }
}
