using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Emmanuel.AudioSystem
{
internal sealed class AudioSourcePool
{
    private readonly List<AudioSource> sources;
    private readonly Transform parent;
    private readonly AudioMixerGroup defaultOutput;
    private readonly int maxSize;
    private bool capacityWarningIssued;

    internal AudioSourcePool(
        Transform parent,
        AudioMixerGroup defaultOutput,
        int initialSize,
        int maxSize)
    {
        this.parent = parent;
        this.defaultOutput = defaultOutput;
        this.maxSize = Mathf.Max(1, maxSize);
        sources = new List<AudioSource>(this.maxSize);

        var prewarmCount = Mathf.Clamp(initialSize, 1, this.maxSize);
        for (var index = 0; index < prewarmCount; index++)
            sources.Add(CreateSource(index));
    }

    internal AudioSource Get()
    {
        foreach (var source in sources)
        {
            if (source.gameObject.activeSelf)
                continue;

            source.gameObject.SetActive(true);
            capacityWarningIssued = false;
            return source;
        }

        if (sources.Count < maxSize)
        {
            var source = CreateSource(sources.Count);
            sources.Add(source);
            source.gameObject.SetActive(true);
            capacityWarningIssued = false;
            return source;
        }

        if (!capacityWarningIssued)
        {
            Debug.LogWarning(
                $"AudioSource pool reached its limit of {maxSize} simultaneous voices. " +
                "Increase Max Voices in Project Settings > Audio System if this is expected.");
            capacityWarningIssued = true;
        }

        return null;
    }

    internal void Release(AudioSource source)
    {
        if (source == null || !sources.Contains(source))
            return;

        ResetSource(source);
        source.gameObject.SetActive(false);
        capacityWarningIssued = false;
    }

    private AudioSource CreateSource(int index)
    {
        var sourceObject = new GameObject($"Voice {index + 1}");
        sourceObject.transform.SetParent(parent, false);

        var source = sourceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        ResetSource(source);
        sourceObject.SetActive(false);
        return source;
    }

    private void ResetSource(AudioSource source)
    {
        source.Stop();
        source.clip = null;
        source.outputAudioMixerGroup = defaultOutput;
        source.playOnAwake = false;
        source.loop = false;
        source.mute = false;
        source.volume = 1f;
        source.pitch = 1f;
        source.panStereo = 0f;
        source.spatialBlend = 0f;
        source.reverbZoneMix = 1f;
        source.dopplerLevel = 1f;
        source.spread = 0f;
        source.priority = 128;
        source.bypassEffects = false;
        source.bypassListenerEffects = false;
        source.bypassReverbZones = false;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 1f;
        source.maxDistance = 500f;
        source.transform.localPosition = Vector3.zero;
        source.transform.localRotation = Quaternion.identity;
    }
}

internal enum AudioPlaybackState
{
    Free,
    Playing,
    Paused
}

internal struct AudioPlaybackRecord
{
    internal int Id;
    internal Audio Audio;
    internal AudioSource Source;
    internal AudioMixerGroup MixerGroup;
    internal PlaybackPolicy Policy;
    internal AudioPlaybackState State;
    internal float VolumeMultiplier;
    internal int OwnerLoopListId;
    internal Transform FollowTarget;
    internal bool IsLooping;
    internal int StartedFrame;
    internal long StartOrder;
    internal float StartNormalizedTime;
    internal float FadeInDuration;
    internal float FadeOutDuration;
}

internal sealed class AudioPlaybackRegistry
{
    private readonly List<AudioPlaybackRecord> activePlaybacks;
    private readonly AudioSourcePool sourcePool;
    private int nextId = 1;

    internal AudioPlaybackRegistry(
        AudioSourcePool sourcePool,
        int capacity)
    {
        this.sourcePool = sourcePool;
        activePlaybacks = new List<AudioPlaybackRecord>(
            Mathf.Max(1, capacity));
    }

    internal int Register(
        Audio audio,
        AudioSource source,
        bool isLooping,
        float volumeMultiplier,
        int ownerLoopListId,
        Transform followTarget,
        long startOrder,
        float startNormalizedTime,
        float fadeInDuration,
        float fadeOutDuration)
    {
        RemoveExistingRecord(source);

        var id = nextId;
        nextId = nextId == int.MaxValue ? 1 : nextId + 1;

        activePlaybacks.Add(new AudioPlaybackRecord
        {
            Id = id,
            Audio = audio,
            Source = source,
            MixerGroup = source.outputAudioMixerGroup,
            Policy = audio.playbackPolicy,
            State = AudioPlaybackState.Playing,
            VolumeMultiplier = volumeMultiplier,
            OwnerLoopListId = ownerLoopListId,
            FollowTarget = followTarget,
            IsLooping = isLooping,
            StartedFrame = Time.frameCount,
            StartOrder = startOrder,
            StartNormalizedTime = startNormalizedTime,
            FadeInDuration = fadeInDuration,
            FadeOutDuration = fadeOutDuration
        });

        return id;
    }

    internal bool Contains(int playbackId)
    {
        return FindIndex(playbackId) >= 0;
    }

    internal int CountByAudio(Audio audio)
    {
        var count = 0;
        foreach (var playback in activePlaybacks)
        {
            if (playback.Audio == audio)
                count++;
        }

        return count;
    }

    internal bool TryGetOldestByAudio(
        Audio audio,
        out int playbackId,
        out int ownerLoopListId)
    {
        var oldestOrder = long.MaxValue;
        playbackId = 0;
        ownerLoopListId = 0;

        foreach (var playback in activePlaybacks)
        {
            if (playback.Audio != audio || playback.StartOrder >= oldestOrder)
                continue;

            oldestOrder = playback.StartOrder;
            playbackId = playback.Id;
            ownerLoopListId = playback.OwnerLoopListId;
        }

        return playbackId != 0;
    }

    internal bool Restart(int playbackId, long startOrder)
    {
        var index = FindIndex(playbackId);
        if (index < 0)
            return false;

        var playback = activePlaybacks[index];
        var source = playback.Source;
        if (source == null || source.clip == null)
        {
            ReleaseAt(index);
            return false;
        }

        source.Stop();
        source.time = source.clip.length * playback.StartNormalizedTime;
        source.Play();
        playback.State = AudioPlaybackState.Playing;
        playback.StartedFrame = Time.frameCount;
        playback.StartOrder = startOrder;
        activePlaybacks[index] = playback;
        return true;
    }

    internal bool IsPlaying(int playbackId)
    {
        var index = FindIndex(playbackId);
        if (index < 0)
            return false;

        var playback = activePlaybacks[index];
        return playback.State == AudioPlaybackState.Playing &&
               playback.Source != null &&
               playback.Source.isPlaying;
    }

    internal bool IsPaused(int playbackId)
    {
        var index = FindIndex(playbackId);
        return index >= 0 &&
               activePlaybacks[index].State == AudioPlaybackState.Paused;
    }

    internal bool TryGetVolumeMultiplier(
        int playbackId,
        out float multiplier)
    {
        var index = FindIndex(playbackId);
        if (index < 0)
        {
            multiplier = 0f;
            return false;
        }

        multiplier = activePlaybacks[index].VolumeMultiplier;
        return true;
    }

    internal bool TryGetFadeDurations(
        int playbackId,
        out float fadeIn,
        out float fadeOut)
    {
        var index = FindIndex(playbackId);
        if (index < 0)
        {
            fadeIn = 0f;
            fadeOut = 0f;
            return false;
        }

        fadeIn = activePlaybacks[index].FadeInDuration;
        fadeOut = activePlaybacks[index].FadeOutDuration;
        return true;
    }

    internal bool SetVolumeMultiplier(int playbackId, float multiplier)
    {
        var index = FindIndex(playbackId);
        if (index < 0)
            return false;

        var playback = activePlaybacks[index];
        if (playback.Source == null)
        {
            activePlaybacks.RemoveAt(index);
            return false;
        }

        playback.VolumeMultiplier = Mathf.Clamp01(multiplier);
        playback.Source.volume = playback.Audio.volume *
                                 playback.VolumeMultiplier;
        activePlaybacks[index] = playback;
        return true;
    }

    internal bool Stop(int playbackId)
    {
        var index = FindIndex(playbackId);
        if (index < 0)
            return false;

        ReleaseAt(index);
        return true;
    }

    internal void StopByAudio(Audio audio)
    {
        for (var index = activePlaybacks.Count - 1; index >= 0; index--)
        {
            if (activePlaybacks[index].Audio == audio)
                ReleaseAt(index);
        }
    }

    internal void PauseByAudio(Audio audio)
    {
        for (var index = activePlaybacks.Count - 1; index >= 0; index--)
        {
            if (activePlaybacks[index].Audio == audio)
                Pause(activePlaybacks[index].Id);
        }
    }

    internal void ResumeByAudio(Audio audio)
    {
        for (var index = activePlaybacks.Count - 1; index >= 0; index--)
        {
            if (activePlaybacks[index].Audio == audio)
                Resume(activePlaybacks[index].Id);
        }
    }

    internal bool HasPausedPlayback(Audio audio)
    {
        foreach (var playback in activePlaybacks)
        {
            if (playback.Audio == audio &&
                playback.State == AudioPlaybackState.Paused)
            {
                return true;
            }
        }

        return false;
    }

    internal void CollectByAudio(Audio audio, List<int> playbackIds)
    {
        foreach (var playback in activePlaybacks)
        {
            if (playback.Audio == audio && playback.OwnerLoopListId == 0)
                playbackIds.Add(playback.Id);
        }
    }

    internal void CollectExclusive(
        AudioMixerGroup mixerGroup,
        List<int> playbackIds)
    {
        foreach (var playback in activePlaybacks)
        {
            if (playback.Policy == PlaybackPolicy.Exclusive &&
                playback.MixerGroup == mixerGroup &&
                playback.OwnerLoopListId == 0)
            {
                playbackIds.Add(playback.Id);
            }
        }
    }

    internal void StopExclusive(AudioMixerGroup mixerGroup)
    {
        for (var index = activePlaybacks.Count - 1; index >= 0; index--)
        {
            var playback = activePlaybacks[index];
            if (playback.Policy == PlaybackPolicy.Exclusive &&
                playback.MixerGroup == mixerGroup)
            {
                ReleaseAt(index);
            }
        }
    }

    internal bool Pause(int playbackId)
    {
        var index = FindIndex(playbackId);
        if (index < 0)
            return false;

        var playback = activePlaybacks[index];
        if (playback.State == AudioPlaybackState.Paused)
            return true;

        if (playback.Source == null)
        {
            activePlaybacks.RemoveAt(index);
            return false;
        }

        playback.Source.Pause();
        playback.State = AudioPlaybackState.Paused;
        activePlaybacks[index] = playback;
        return true;
    }

    internal bool Resume(int playbackId)
    {
        var index = FindIndex(playbackId);
        if (index < 0)
            return false;

        var playback = activePlaybacks[index];
        if (playback.State == AudioPlaybackState.Playing)
            return true;

        if (playback.Source == null)
        {
            activePlaybacks.RemoveAt(index);
            return false;
        }

        playback.Source.UnPause();
        playback.State = AudioPlaybackState.Playing;
        activePlaybacks[index] = playback;
        return true;
    }

    internal void Update()
    {
        for (var index = activePlaybacks.Count - 1; index >= 0; index--)
        {
            var playback = activePlaybacks[index];
            var source = playback.Source;

            if (source == null)
            {
                activePlaybacks.RemoveAt(index);
                continue;
            }

            if (!source.gameObject.activeSelf)
            {
                ReleaseAt(index);
                continue;
            }

            if (playback.FollowTarget != null)
                source.transform.position = playback.FollowTarget.position;

            if (playback.State == AudioPlaybackState.Paused ||
                playback.IsLooping ||
                playback.StartedFrame == Time.frameCount ||
                AudioListener.pause)
            {
                continue;
            }

            if (!source.isPlaying)
                ReleaseAt(index);
        }
    }

    private int FindIndex(int playbackId)
    {
        for (var index = 0; index < activePlaybacks.Count; index++)
        {
            if (activePlaybacks[index].Id == playbackId)
                return index;
        }

        return -1;
    }

    private void RemoveExistingRecord(AudioSource source)
    {
        for (var index = activePlaybacks.Count - 1; index >= 0; index--)
        {
            if (activePlaybacks[index].Source == source)
                activePlaybacks.RemoveAt(index);
        }
    }

    private void ReleaseAt(int index)
    {
        var playback = activePlaybacks[index];
        playback.State = AudioPlaybackState.Free;
        sourcePool.Release(playback.Source);
        activePlaybacks.RemoveAt(index);
    }
}
}
