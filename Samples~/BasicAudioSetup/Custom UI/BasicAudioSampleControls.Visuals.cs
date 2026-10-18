using Phillipe2AM.SoundSystem;
using Phillipe2AM.SoundSystem.Configuration;
using Phillipe2AM.SoundSystem.Playback;
using UnityEngine;

namespace Samples.BasicAudioSetup
{
    public sealed partial class BasicAudioSampleControls
    {
        private void CreateMovingBeacon()
        {
            _movingBeacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _movingBeacon.name = "Moving 3D Audio Beacon";
            _movingBeacon.transform.position = new Vector3(0f, 0f, 8f);
            _movingBeacon.transform.localScale = Vector3.one * 0.65f;

            SphereCollider beaconCollider = _movingBeacon.GetComponent<SphereCollider>();
            if (beaconCollider != null)
            {
                Destroy(beaconCollider);
            }

            Renderer beaconRenderer = _movingBeacon.GetComponent<Renderer>();
            Shader shader = _visualShader;
            if (beaconRenderer == null || shader == null)
            {
                return;
            }

            if (_beaconMaterial == null)
            {
                _beaconMaterial = new Material(shader)
                {
                    name = "Basic Audio Setup Beacon Material",
                    color = new Color(0.15f, 0.8f, 1f)
                };
            }
            beaconRenderer.sharedMaterial = _beaconMaterial;
        }

        private void CreateListenerMarker()
        {
            _listenerMarker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _listenerMarker.name = "Listener Reference (You)";
            _listenerMarker.transform.position = new Vector3(0f, -2f, 8f);
            _listenerMarker.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            _listenerMarker.AddComponent<AudioListener>();

            CapsuleCollider markerCollider = _listenerMarker.GetComponent<CapsuleCollider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            Renderer markerRenderer = _listenerMarker.GetComponent<Renderer>();
            Shader shader = _visualShader;
            if (markerRenderer == null || shader == null)
            {
                return;
            }

            _listenerMarkerMaterial = new Material(shader)
            {
                name = "Basic Audio Setup Listener Marker Material",
                color = new Color(1f, 0.62f, 0.14f)
            };
            markerRenderer.sharedMaterial = _listenerMarkerMaterial;
        }

        private void CreateAudioPositionIndicators()
        {
            _followIndicatorMaterial = CreateIndicatorMaterial(
                "Basic Audio Setup Follow Indicator Material",
                new Color(0.2f, 1f, 0.3f));
            _fixedIndicatorMaterial = CreateIndicatorMaterial(
                "Basic Audio Setup Fixed Position Indicator Material",
                new Color(1f, 0.2f, 0.85f));
        }

        private void ShowFollowIndicator(Transform target, AudioPlaybackHandle handle)
        {
            if (target == null)
            {
                return;
            }

            _activePositionIndicators.Add(CreatePositionIndicator(
                "Follow 3D Audio Position Indicator",
                0.55f,
                _followIndicatorMaterial,
                target.position + Vector3.back * 0.1f,
                target,
                handle));
        }

        private void ShowFixedPositionIndicator(Vector3 position, AudioPlaybackHandle handle)
        {
            _activePositionIndicators.Add(CreatePositionIndicator(
                "Fixed 3D Audio Position Indicator",
                0.42f,
                _fixedIndicatorMaterial,
                position + Vector3.back * 0.1f,
                null,
                handle));
        }

        private void UpdateAudioPositionIndicators()
        {
            for (int index = _activePositionIndicators.Count - 1; index >= 0; index--)
            {
                PositionIndicator indicator = _activePositionIndicators[index];
                if (indicator.GameObject == null)
                {
                    _activePositionIndicators.RemoveAt(index);
                    continue;
                }

                if (indicator.FollowTarget != null)
                {
                    indicator.GameObject.transform.position =
                        indicator.FollowTarget.position + Vector3.back * 0.1f;
                }

                if (!indicator.PlaybackHandle.IsValid)
                {
                    DestroyPositionIndicator(indicator);
                    _activePositionIndicators.RemoveAt(index);
                }
            }
        }

        private static PositionIndicator CreatePositionIndicator(
            string objectName,
            float radius,
            Material material,
            Vector3 position,
            Transform followTarget,
            AudioPlaybackHandle handle)
        {
            GameObject indicatorObject = new GameObject(objectName);
            indicatorObject.transform.position = position;
            LineRenderer lineRenderer = indicatorObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.positionCount = 48;
            lineRenderer.startWidth = 0.045f;
            lineRenderer.endWidth = 0.045f;
            lineRenderer.sharedMaterial = material;
            lineRenderer.startColor = material != null ? material.color : Color.white;
            lineRenderer.endColor = lineRenderer.startColor;

            for (int pointIndex = 0; pointIndex < lineRenderer.positionCount; pointIndex++)
            {
                float angle = pointIndex * (2f * Mathf.PI / lineRenderer.positionCount);
                lineRenderer.SetPosition(
                    pointIndex,
                    new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }

            return new PositionIndicator
            {
                GameObject = indicatorObject,
                FollowTarget = followTarget,
                PlaybackHandle = handle
            };
        }

        private void ClearPositionIndicators()
        {
            foreach (PositionIndicator indicator in _activePositionIndicators)
            {
                DestroyPositionIndicator(indicator);
            }

            _activePositionIndicators.Clear();
        }

        private Material CreateIndicatorMaterial(string materialName, Color color)
        {
            Shader shader = _visualShader;
            if (shader == null)
            {
                return null;
            }

            return new Material(shader)
            {
                name = materialName,
                color = color
            };
        }

        private static void DestroyPositionIndicator(PositionIndicator indicator)
        {
            if (indicator != null && indicator.GameObject != null)
            {
                Destroy(indicator.GameObject);
            }
        }

        private static AudioClip CreateTone(string clipName, float frequencyA, float frequencyB, float durationSeconds, bool loop)
        {
            const int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * durationSeconds);
            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
            clip.name = clipName;

            float[] samples = new float[sampleCount];
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float time = sampleIndex / (float)sampleRate;
                float sample = Mathf.Sin(2f * Mathf.PI * frequencyA * time);
                if (frequencyB > 0f)
                {
                    sample = (sample + Mathf.Sin(2f * Mathf.PI * frequencyB * time)) * 0.5f;
                }

                float envelope = loop
                    ? Mathf.Min(1f, Mathf.Min(time * 20f, (durationSeconds - time) * 20f))
                    : Mathf.Pow(1f - time / durationSeconds, 2f);
                samples[sampleIndex] = sample * envelope * 0.2f;
            }

            clip.SetData(samples, 0);
            return clip;
        }
    }
}
