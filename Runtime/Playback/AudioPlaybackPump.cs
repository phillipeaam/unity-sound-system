using UnityEngine;

namespace Phillipe2AM.SoundSystem.Playback
{
    /// <summary>One manager-owned late-frame callback, disabled whenever the pool has no pending work.</summary>
    [AddComponentMenu("")]
    internal sealed class AudioPlaybackPump : MonoBehaviour
    {
        private AudioManager _manager;

        internal void Initialize(AudioManager manager)
        {
            _manager = manager;
            enabled = false;
        }

        private void LateUpdate()
        {
            _manager.TickPlaybackPump();
        }
    }
}
