using UnityEngine;
using UnityEngine.Audio;

namespace Emmanuel.AudioSystem
{
public enum AudioLoopMode
{
    None,
    Sequential,
    Random,
    Shuffle
}

public enum AudioPitchMode
{
    Normal,
    Fixed,
    Random
}

public enum PlaybackPolicy
{
    Overlap,
    SingleInstance,
    Exclusive
}

public enum InstanceLimitBehavior
{
    IgnoreNew,
    RestartOldest,
    ReplaceOldest
}

public class Audio : ScriptableObject
{
    [Header("Audio Clips")]
    [Tooltip("Array of audio clips to be played")]
    public AudioClip[] clips;

    [Range(0, 1f)]
    public float volume = 1;

    [Tooltip("Controls whether playback uses normal, fixed, or randomized pitch.")]
    public AudioPitchMode pitchMode = AudioPitchMode.Normal;

    [Range(-3f, 3f)]
    [Tooltip("Fixed playback pitch. Can be overridden per playback with AudioPlayOptions.")]
    public float pitch = 1f;

    [Tooltip("Minimum and maximum pitch used by Random pitch mode.")]
    public Vector2 pitchRange = Vector2.one;

    [Min(0f)]
    [Tooltip("Default duration used by FadeIn and incoming Change transitions.")]
    public float fadeInDuration = 0.7f;

    [Min(0f)]
    [Tooltip("Default duration used by FadeOut and outgoing Change transitions.")]
    public float fadeOutDuration = 0.7f;

    [Tooltip("AudioMixerGroup used as the output for this audio. Uses the Main Mixer root when empty.")]
    public AudioMixerGroup mixerGroup;

    [Tooltip("Controls how this Audio interacts with active playbacks. Exclusive replaces only another Exclusive Audio in the same Category.")]
    public PlaybackPolicy playbackPolicy = PlaybackPolicy.Overlap;

    [Min(0)]
    [Tooltip("Maximum simultaneous instances of this Audio. Zero means unlimited.")]
    public int maxInstances;

    [Tooltip("Action taken when Max Instances has been reached. Restart Oldest preserves that instance and its original playback settings; Replace Oldest starts a new instance with the new request settings.")]
    public InstanceLimitBehavior instanceLimitBehavior =
        InstanceLimitBehavior.IgnoreNew;

    [Range(0f, 5f)]
    [Tooltip("Strength of the doppler effect for positional playback.")]
    public float dopplerLevel = 1f;

    [Range(0f, 1f)]
    [Tooltip("Controls how much positional playback is treated as 2D or 3D.")]
    public float spatialBlend = 1f;

    [Range(0f, 360f)]
    [Tooltip("Spread angle in speaker space for positional playback.")]
    public float spread;

    [Range(0f, 1.1f)]
    [Tooltip("Amount of the AudioSource signal routed to reverb zones.")]
    public float reverbZoneMix = 1f;

    [Tooltip("Distance attenuation mode used for positional playback.")]
    public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;

    [Min(0f)]
    [Tooltip("Distance at which positional audio begins to attenuate.")]
    public float minDistance = 1f;

    [Min(0f)]
    [Tooltip("Distance beyond which positional audio stops attenuating.")]
    public float maxDistance = 500f;

    [Tooltip("Volume attenuation curve used when Volume Rolloff is Custom.")]
    public AnimationCurve customRolloffCurve = AnimationCurve.Linear(
        0f,
        1f,
        1f,
        0f);

#if UNITY_EDITOR
    [SerializeField]
    private Color iconColor = new Color32(252, 191, 7, 255);
#endif

    [Tooltip("Controls repetition. With one clip, any enabled mode repeats it. With multiple clips, the mode defines how the loop list selects each clip.")]
    public AudioLoopMode loopMode = AudioLoopMode.None;

    private void OnValidate()
    {
        dopplerLevel = Mathf.Clamp(dopplerLevel, 0f, 5f);
        pitch = Mathf.Clamp(pitch, -3f, 3f);
        pitchRange.x = Mathf.Clamp(pitchRange.x, -3f, 3f);
        pitchRange.y = Mathf.Clamp(pitchRange.y, -3f, 3f);
        if (pitchRange.x > pitchRange.y)
            (pitchRange.x, pitchRange.y) = (pitchRange.y, pitchRange.x);
        fadeInDuration = Mathf.Max(0f, fadeInDuration);
        fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
        maxInstances = Mathf.Max(0, maxInstances);
        spatialBlend = Mathf.Clamp01(spatialBlend);
        spread = Mathf.Clamp(spread, 0f, 360f);
        reverbZoneMix = Mathf.Clamp(reverbZoneMix, 0f, 1.1f);
        minDistance = Mathf.Max(0f, minDistance);
        maxDistance = Mathf.Max(minDistance, maxDistance);
    }

    public void Play()
    {
        AudioManager.Play(this);
    }

    public void PlayAt(Transform point)
    {
        AudioManager.Play(this, point);
    }

    public void PlayAttached(Transform point)
    {
        AudioManager.Play(this, new AudioPlayOptions(
            point: point,
            followTransform: true));
    }

    public void PlayClip(int index)
    {
        AudioManager.Play(this, index);
    }

    public void PlayClipAt(int index, Transform point)
    {
        AudioManager.Play(this, index, point);
    }

    public void Change()
    {
        AudioManager.Change(this);
    }

    public void Change(float waitTime)
    {
        AudioManager.Change(this, waitTime);
    }

    public void Change(
        float waitTime,
        float fadeOutDuration,
        float fadeInDuration)
    {
        AudioManager.Change(
            this,
            waitTime,
            fadeOutDuration,
            fadeInDuration);
    }

    public void Pause()
    {
        AudioManager.Pause(this);
    }

    public void Resume()
    {
        AudioManager.Resume(this);
    }

    public void TogglePause()
    {
        AudioManager.TogglePause(this);
    }

    public void Stop()
    {
        AudioManager.Stop(this);
    }

    public void FadeIn()
    {
        AudioManager.FadeIn(this);
    }

    public void FadeIn(float duration)
    {
        AudioManager.FadeIn(this, duration);
    }

    public void FadeOut()
    {
        AudioManager.FadeOut(this);
    }

    public void FadeOut(float duration)
    {
        AudioManager.FadeOut(this, duration);
    }

    public void FadeTo(float volumeMultiplier)
    {
        AudioManager.FadeTo(this, volumeMultiplier);
    }

    public void FadeTo(float volumeMultiplier, float duration)
    {
        AudioManager.FadeTo(this, volumeMultiplier, duration);
    }

    public AudioHandle PlayTracked()
    {
        return AudioManager.Play(this);
    }

    public AudioHandle PlayTracked(AudioPlayOptions options)
    {
        return AudioManager.Play(this, options);
    }

    public AudioHandle PlayTracked(Transform point)
    {
        return AudioManager.Play(this, point);
    }

    public AudioHandle PlayTrackedAttached(Transform point)
    {
        return AudioManager.Play(this, new AudioPlayOptions(
            point: point,
            followTransform: true));
    }

    public AudioHandle PlayTracked(int index)
    {
        return AudioManager.Play(this, index);
    }

    public AudioHandle PlayTracked(int index, Transform point)
    {
        return AudioManager.Play(this, index, point);
    }

    public AudioHandle PlayTrackedAt(Vector3 position)
    {
        return AudioManager.PlayAtPosition(this, position);
    }

    public AudioClip Clip(int index = -1)
    {
        if (clips == null || clips.Length == 0)
        {
            Debug.LogWarning("No audio clips available in the Audio asset.");
            return null;
        }
        if (index < 0)
            return AudioManager.SelectClip(this);

        if (index >= clips.Length)
        {
            Debug.LogWarning($"Index {index} is out of bounds for the audio clips array.");
            return AudioManager.SelectClip(this);
        }
        return clips[index];
    }
    public AudioClip clip
    {
        get
        {
            if (clips == null || clips.Length == 0)
            {
                Debug.LogWarning("No audio clips available in the Audio asset.");
                return null;
            }
            return AudioManager.SelectClip(this);
        }
    }
}
}
