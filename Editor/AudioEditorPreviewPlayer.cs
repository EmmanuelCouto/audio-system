using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Emmanuel.AudioSystem;

namespace Emmanuel.AudioSystem.Editor
{
[InitializeOnLoad]
internal static class AudioEditorPreviewPlayer
{
    private const double PlaybackStartGracePeriod = 0.1d;

    private static GameObject previewObject;
    private static AudioSource previewSource;
    private static Audio currentAudio;
    private static Audio loopListAudio;
    private static float previewPitch = 1f;
    private static double playbackStartedAt;

    internal static event Action StateChanged;

    internal static AudioClip CurrentClip =>
        previewSource != null ? previewSource.clip : null;

    internal static bool IsPlaying =>
        previewSource != null && previewSource.isPlaying;

    internal static float NormalizedTime
    {
        get
        {
            var clip = CurrentClip;
            if (clip == null || clip.length <= 0f)
                return 0f;

            return Mathf.Clamp01(previewSource.time / clip.length);
        }
    }

    static AudioEditorPreviewPlayer()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.quitting += Cleanup;
        AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
    }

    internal static bool IsPlayingClip(AudioClip clip)
    {
        return clip != null && CurrentClip == clip && IsPlaying;
    }

    internal static bool IsPreviewing(Audio audio)
    {
        return audio != null &&
               currentAudio == audio &&
               previewSource != null &&
               previewSource.clip != null;
    }

    internal static void Play(Audio audio)
    {
        if (audio == null || audio.clips == null || audio.clips.Length == 0)
            return;

        if (audio.loopMode != AudioLoopMode.None &&
            CountValidClips(audio.clips) > 1)
        {
            loopListAudio = audio;
            previewPitch = ResolvePitch(audio);
            var clip = audio.clip;
            if (clip == null)
            {
                Stop();
                return;
            }

            StartClip(
                audio,
                clip,
                CountValidClips(audio.clips) == 1,
                true);
            return;
        }

        PlayClip(
            audio,
            audio.clip,
            audio.loopMode != AudioLoopMode.None);
    }

    internal static void PlayClip(Audio audio, AudioClip clip, bool loop = false)
    {
        if (audio == null || clip == null)
            return;

        StartClip(audio, clip, loop, false);
    }

    internal static void SeekClip(
        Audio audio,
        AudioClip clip,
        float normalizedTime)
    {
        if (audio == null || clip == null)
            return;

        if (previewSource == null ||
            CurrentClip != clip ||
            currentAudio != audio ||
            !previewSource.isPlaying)
        {
            StartClip(audio, clip, false, false, normalizedTime);
            return;
        }

        SetNormalizedTime(clip, normalizedTime);
        playbackStartedAt = EditorApplication.timeSinceStartup;
        StateChanged?.Invoke();
    }

    internal static void Stop()
    {
        currentAudio = null;
        loopListAudio = null;

        if (previewSource != null)
        {
            previewSource.Stop();
            previewSource.clip = null;
        }

        StateChanged?.Invoke();
    }

    private static void StartClip(
        Audio audio,
        AudioClip clip,
        bool loop,
        bool preserveLoopList,
        float normalizedStartTime = 0f)
    {
        if (clip == null)
            return;

        EnsureSource();
        currentAudio = audio;
        if (!preserveLoopList)
        {
            loopListAudio = null;
            previewPitch = ResolvePitch(audio);
        }

        previewSource.Stop();
        previewSource.clip = clip;
        previewSource.volume = Mathf.Clamp01(audio.volume);
        previewSource.pitch = previewPitch;
        previewSource.loop = loop;
        previewSource.outputAudioMixerGroup = ResolveOutputGroup(audio);
        SetNormalizedTime(clip, normalizedStartTime);
        previewSource.Play();
        playbackStartedAt = EditorApplication.timeSinceStartup;
        StateChanged?.Invoke();
    }

    private static void SetNormalizedTime(
        AudioClip clip,
        float normalizedTime)
    {
        if (previewSource == null || clip == null || clip.samples <= 0)
            return;

        previewSource.timeSamples = Mathf.Clamp(
            Mathf.RoundToInt(
                Mathf.Clamp01(normalizedTime) * (clip.samples - 1)),
            0,
            clip.samples - 1);
    }

    private static void Update()
    {
        if (previewSource == null || previewSource.clip == null)
            return;

        StateChanged?.Invoke();

        if (previewSource.isPlaying ||
            EditorApplication.timeSinceStartup - playbackStartedAt <
            PlaybackStartGracePeriod)
        {
            return;
        }

        if (loopListAudio != null)
        {
            var nextClip = loopListAudio.clip;
            if (nextClip != null)
            {
                StartClip(
                    loopListAudio,
                    nextClip,
                    CountValidClips(loopListAudio.clips) == 1,
                    true);
                return;
            }
        }

        Stop();
    }

    private static int CountValidClips(AudioClip[] clips)
    {
        if (clips == null)
            return 0;

        var count = 0;
        foreach (var clip in clips)
        {
            if (clip != null)
                count++;
        }

        return count;
    }

    private static void EnsureSource()
    {
        if (previewSource != null)
            return;

        previewObject = new GameObject("Audio System Preview")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        previewSource = previewObject.AddComponent<AudioSource>();
        previewSource.hideFlags = HideFlags.HideAndDontSave;
        previewSource.playOnAwake = false;
        previewSource.spatialBlend = 0f;
        previewSource.ignoreListenerPause = true;
    }

    private static AudioMixerGroup ResolveOutputGroup(Audio audio)
    {
        if (audio.mixerGroup != null)
            return audio.mixerGroup;

        var settings = AudioSystemSettingsProvider.FindSettings();
        var mixer = settings != null ? settings.MainMixer : null;
        if (mixer == null)
            return null;

        var groups = mixer.FindMatchingGroups(string.Empty);
        return groups.Length > 0 ? groups[0] : null;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode ||
            state == PlayModeStateChange.EnteredPlayMode)
        {
            Cleanup();
        }
    }

    private static void Cleanup()
    {
        currentAudio = null;
        loopListAudio = null;
        previewPitch = 1f;

        if (previewObject != null)
            UnityEngine.Object.DestroyImmediate(previewObject);

        previewObject = null;
        previewSource = null;
        StateChanged?.Invoke();
    }

    private static float ResolvePitch(Audio audio)
    {
        return audio.pitchMode switch
        {
            AudioPitchMode.Fixed => Mathf.Clamp(audio.pitch, -3f, 3f),
            AudioPitchMode.Random => UnityEngine.Random.Range(
                Mathf.Min(audio.pitchRange.x, audio.pitchRange.y),
                Mathf.Max(audio.pitchRange.x, audio.pitchRange.y)),
            _ => 1f
        };
    }
}
}
