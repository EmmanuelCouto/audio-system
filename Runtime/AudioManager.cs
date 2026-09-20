using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Emmanuel.AudioSystem
{
public readonly struct AudioPlayOptions
{
    private readonly bool initialized;
    private readonly float volumeMultiplier;
    private readonly bool followTransform;

    public AudioPlayOptions(
        float volumeMultiplier = 1f,
        float? pitch = null,
        int? clipIndex = null,
        Transform point = null,
        bool followTransform = false,
        float? normalizedStartTime = null,
        float? fadeIn = null,
        float? fadeOut = null)
    {
        initialized = true;
        this.volumeMultiplier = volumeMultiplier;
        Pitch = pitch.HasValue
            ? Mathf.Clamp(pitch.Value, -3f, 3f)
            : null;
        ClipIndex = clipIndex;
        Point = point;
        this.followTransform = followTransform;
        NormalizedStartTime = normalizedStartTime.HasValue
            ? Mathf.Clamp01(normalizedStartTime.Value)
            : null;
        FadeIn = fadeIn.HasValue ? Mathf.Max(0f, fadeIn.Value) : null;
        FadeOut = fadeOut.HasValue ? Mathf.Max(0f, fadeOut.Value) : null;
    }

    public float VolumeMultiplier => initialized
        ? Mathf.Clamp01(volumeMultiplier)
        : 1f;

    public float? Pitch { get; }
    public int? ClipIndex { get; }
    public Transform Point { get; }
    public float? NormalizedStartTime { get; }
    public float? FadeIn { get; }
    public float? FadeOut { get; }
    public bool FollowTransform => initialized &&
                                   followTransform &&
                                   Point != null;
}

internal enum AudioHandleKind
{
    None,
    Voice,
    LoopList
}

public readonly struct AudioHandle : IEquatable<AudioHandle>
{
    internal AudioHandle(int id, AudioHandleKind kind)
    {
        Id = id;
        Kind = kind;
    }

    internal int Id { get; }
    internal AudioHandleKind Kind { get; }

    public bool IsValid => AudioManager.IsValid(this);
    public bool IsPlaying => AudioManager.IsPlaying(this);
    public bool IsPaused => AudioManager.IsPaused(this);

    public bool Stop() => AudioManager.Stop(this);
    public bool Pause() => AudioManager.Pause(this);
    public bool Resume() => AudioManager.Resume(this);

    public AudioHandle SetVolume(float multiplier)
    {
        AudioManager.SetVolume(this, multiplier);
        return this;
    }

    public AudioHandle FadeIn(float duration)
    {
        AudioManager.SetVolume(this, 0f);
        AudioManager.FadeTo(this, 1f, duration, false);
        return this;
    }

    public AudioHandle FadeIn()
    {
        AudioManager.FadeIn(this);
        return this;
    }

    public AudioHandle FadeOut(float duration, bool stopWhenSilent = true)
    {
        AudioManager.FadeTo(this, 0f, duration, stopWhenSilent);
        return this;
    }

    public AudioHandle FadeOut()
    {
        AudioManager.FadeOut(this);
        return this;
    }

    public AudioHandle FadeTo(float multiplier, float duration)
    {
        AudioManager.FadeTo(this, multiplier, duration, false);
        return this;
    }

    public bool Equals(AudioHandle other)
    {
        return Id == other.Id && Kind == other.Kind;
    }

    public override bool Equals(object obj)
    {
        return obj is AudioHandle other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, (int)Kind);
    }

    public static bool operator ==(AudioHandle left, AudioHandle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(AudioHandle left, AudioHandle right)
    {
        return !left.Equals(right);
    }
}

[DefaultExecutionOrder(-32000)]
internal sealed class AudioCoroutineRunner : MonoBehaviour
{
    internal AudioPlaybackRegistry PlaybackRegistry { private get; set; }
    internal bool IsLifecyclePaused =>
        applicationPaused || !applicationFocused;

    private bool applicationPaused;
    private bool applicationFocused = true;
    private bool ownsListenerPause;
    private bool listenerPauseBeforeLifecycle;

    private void Update()
    {
        PlaybackRegistry?.Update();
    }

    private void OnApplicationPause(bool paused)
    {
        applicationPaused = paused;
        UpdateLifecyclePause();
    }

    private void OnApplicationFocus(bool focused)
    {
        applicationFocused = focused;
        UpdateLifecyclePause();
    }

    private void OnDestroy()
    {
        RestoreListenerPause();
    }

    private void UpdateLifecyclePause()
    {
        var shouldPause = applicationPaused || !applicationFocused;
        if (shouldPause)
        {
            if (ownsListenerPause)
                return;

            listenerPauseBeforeLifecycle = AudioListener.pause;
            AudioListener.pause = true;
            ownsListenerPause = true;
            return;
        }

        RestoreListenerPause();
    }

    private void RestoreListenerPause()
    {
        if (!ownsListenerPause)
            return;

        AudioListener.pause = listenerPauseBeforeLifecycle;
        ownsListenerPause = false;
    }
}

internal sealed class AudioLoopListPlayback
{
    internal int Id { get; set; }
    internal Audio Audio { get; set; }
    internal bool IsPositional { get; set; }
    internal Vector3 Position { get; set; }
    internal Transform FollowTarget { get; set; }
    internal float Pitch { get; set; } = 1f;
    internal float CurrentStartNormalizedTime { get; set; }
    internal float InitialStartNormalizedTime { get; set; }
    internal int StartClipIndex { get; set; }
    internal long StartOrder { get; set; }
    internal AudioClipSelectionState SelectionState { get; set; }
    internal AudioMixerGroup MixerGroup { get; set; }
    internal PlaybackPolicy Policy { get; set; }
    internal bool IsPaused { get; set; }
    internal float VolumeMultiplier { get; set; } = 1f;
    internal float FadeInDuration { get; set; }
    internal float FadeOutDuration { get; set; }
    internal int CurrentPlaybackId { get; set; }
    internal Coroutine Coroutine { get; set; }
}

internal sealed class AudioClipSelectionState
{
    internal int NextSequentialIndex { get; set; }
    internal List<int> ShuffleIndices { get; } = new();
    internal int ShufflePosition { get; set; }
    internal int LastShuffleIndex { get; set; } = -1;
}

internal static class AudioManager
{
    private enum InstanceLimitResult
    {
        Continue,
        Rejected,
        Restarted
    }

    private const float MaxNormalizedStartTime = 0.9999f;

    private static readonly List<AudioLoopListPlayback> ActiveLoopLists = new(8);
    private static readonly Dictionary<long, int> FadeVersions = new(16);
    private static readonly Dictionary<int, int> ChangeVersions = new(8);

    private static GameObject root;
    private static AudioSourcePool sourcePool;
    private static AudioPlaybackRegistry playbackRegistry;
    private static AudioMixerGroup rootMixerGroup;
    private static AudioCoroutineRunner coroutineRunner;
    private static int nextLoopListId = 1;
    private static long nextStartOrder = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        root = null;
        sourcePool = null;
        playbackRegistry = null;
        rootMixerGroup = null;
        coroutineRunner = null;
        ActiveLoopLists.Clear();
        FadeVersions.Clear();
        ChangeVersions.Clear();
        nextLoopListId = 1;
        nextStartOrder = 1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        EnsureInitialized();
    }

    internal static AudioHandle Play(Audio audio)
    {
        return Play(audio, default(AudioPlayOptions));
    }

    internal static AudioHandle Play(
        Audio audio,
        Transform point,
        float? pitch = null)
    {
        return Play(audio, new AudioPlayOptions(
            pitch: pitch,
            point: point));
    }

    internal static AudioHandle Play(
        Audio audio,
        int index,
        Transform point = null,
        float? pitch = null)
    {
        return Play(audio, new AudioPlayOptions(
            pitch: pitch,
            clipIndex: index,
            point: point));
    }

    internal static AudioHandle Play(
        Audio audio,
        AudioPlayOptions options)
    {
        return PlayInternal(audio, options, null);
    }

    internal static AudioHandle PlayAtPosition(
        Audio audio,
        Vector3 position,
        int? index = null,
        float? pitch = null)
    {
        var options = new AudioPlayOptions(
            pitch: pitch,
            clipIndex: index);
        return PlayInternal(audio, options, position);
    }

    internal static void Change(Audio audio)
    {
        Change(audio, 0.5f);
    }

    internal static void Change(Audio audio, float waitTime)
    {
        Change(audio, waitTime, null, null);
    }

    internal static void Change(
        Audio audio,
        float waitTime,
        float fadeOutDuration,
        float fadeInDuration)
    {
        Change(audio, waitTime, (float?)fadeOutDuration, fadeInDuration);
    }

    private static void Change(
        Audio audio,
        float waitTime,
        float? fadeOutDuration,
        float? fadeInDuration)
    {
        if (!CanPlay(audio))
            return;

        EnsureInitialized();
        if (coroutineRunner.IsLifecyclePaused)
            return;

        var mixerGroup = ResolveOutputGroup(audio);
        var changeKey = GetMixerGroupKey(mixerGroup);
        var version = InvalidateChange(changeKey);
        var outgoing = new List<AudioHandle>();

        if (audio.playbackPolicy == PlaybackPolicy.Exclusive)
            CollectExclusiveHandles(mixerGroup, outgoing);

        coroutineRunner.StartCoroutine(ChangeRoutine(
            audio,
            outgoing,
            Mathf.Max(0f, waitTime),
            fadeOutDuration.HasValue
                ? Mathf.Max(0f, fadeOutDuration.Value)
                : null,
            fadeInDuration.HasValue
                ? Mathf.Max(0f, fadeInDuration.Value)
                : null,
            changeKey,
            version));
    }

    internal static void Pause(Audio audio)
    {
        if (audio == null)
            return;

        EnsureInitialized();
        foreach (var loopList in ActiveLoopLists)
        {
            if (loopList.Audio == audio)
                SetLoopListPaused(loopList, true);
        }

        playbackRegistry.PauseByAudio(audio);
    }

    internal static void Resume(Audio audio)
    {
        if (audio == null)
            return;

        EnsureInitialized();
        foreach (var loopList in ActiveLoopLists)
        {
            if (loopList.Audio == audio)
                SetLoopListPaused(loopList, false);
        }

        playbackRegistry.ResumeByAudio(audio);
    }

    internal static void TogglePause(Audio audio)
    {
        if (audio == null)
            return;

        EnsureInitialized();
        var hasPausedPlayback = playbackRegistry.HasPausedPlayback(audio);
        if (!hasPausedPlayback)
        {
            foreach (var loopList in ActiveLoopLists)
            {
                if (loopList.Audio == audio && loopList.IsPaused)
                {
                    hasPausedPlayback = true;
                    break;
                }
            }
        }

        if (hasPausedPlayback)
            Resume(audio);
        else
            Pause(audio);
    }

    internal static void Stop(Audio audio)
    {
        if (audio == null)
            return;

        EnsureInitialized();
        StopLoopListsByAudio(audio);
        playbackRegistry.StopByAudio(audio);
    }

    internal static void FadeIn(Audio audio)
    {
        if (audio == null)
            return;

        var handle = Play(audio);
        handle.FadeIn();
    }

    internal static void FadeIn(Audio audio, float duration)
    {
        var handle = Play(audio);
        handle.FadeIn(Mathf.Max(0f, duration));
    }

    internal static void FadeOut(Audio audio)
    {
        if (audio == null)
            return;

        EnsureInitialized();
        var handles = new List<AudioHandle>();
        CollectHandles(audio, handles);
        foreach (var handle in handles)
            handle.FadeOut();
    }

    internal static void FadeOut(Audio audio, float duration)
    {
        if (audio == null)
            return;

        EnsureInitialized();
        var handles = new List<AudioHandle>();
        CollectHandles(audio, handles);
        foreach (var handle in handles)
            handle.FadeOut(Mathf.Max(0f, duration));
    }

    internal static void FadeTo(
        Audio audio,
        float volumeMultiplier,
        float duration = 0.7f)
    {
        if (audio == null)
            return;

        EnsureInitialized();
        var handles = new List<AudioHandle>();
        CollectHandles(audio, handles);
        foreach (var handle in handles)
        {
            handle.FadeTo(
                Mathf.Clamp01(volumeMultiplier),
                Mathf.Max(0f, duration));
        }
    }

    internal static bool IsValid(AudioHandle handle)
    {
        if (handle.Id == 0)
            return false;

        EnsureInitialized();
        return handle.Kind switch
        {
            AudioHandleKind.Voice => playbackRegistry.Contains(handle.Id),
            AudioHandleKind.LoopList => FindLoopList(handle.Id) != null,
            _ => false
        };
    }

    internal static bool IsPlaying(AudioHandle handle)
    {
        if (!IsValid(handle))
            return false;

        if (handle.Kind == AudioHandleKind.Voice)
            return playbackRegistry.IsPlaying(handle.Id);

        var loopList = FindLoopList(handle.Id);
        return loopList != null &&
               !loopList.IsPaused &&
               playbackRegistry.IsPlaying(loopList.CurrentPlaybackId);
    }

    internal static bool IsPaused(AudioHandle handle)
    {
        if (!IsValid(handle))
            return false;

        if (handle.Kind == AudioHandleKind.Voice)
            return playbackRegistry.IsPaused(handle.Id);

        var loopList = FindLoopList(handle.Id);
        return loopList != null && loopList.IsPaused;
    }

    internal static bool Stop(AudioHandle handle)
    {
        if (!IsValid(handle))
            return false;

        InvalidateFade(handle);
        if (handle.Kind == AudioHandleKind.Voice)
            return playbackRegistry.Stop(handle.Id);

        var loopList = FindLoopList(handle.Id);
        if (loopList == null)
            return false;

        StopLoopList(loopList);
        return true;
    }

    internal static bool Pause(AudioHandle handle)
    {
        if (!IsValid(handle))
            return false;

        if (handle.Kind == AudioHandleKind.Voice)
            return playbackRegistry.Pause(handle.Id);

        var loopList = FindLoopList(handle.Id);
        if (loopList == null)
            return false;

        SetLoopListPaused(loopList, true);
        return true;
    }

    internal static bool Resume(AudioHandle handle)
    {
        if (!IsValid(handle))
            return false;

        if (handle.Kind == AudioHandleKind.Voice)
            return playbackRegistry.Resume(handle.Id);

        var loopList = FindLoopList(handle.Id);
        if (loopList == null)
            return false;

        SetLoopListPaused(loopList, false);
        return true;
    }

    internal static bool SetVolume(AudioHandle handle, float multiplier)
    {
        if (!IsValid(handle))
            return false;

        multiplier = Mathf.Clamp01(multiplier);
        if (handle.Kind == AudioHandleKind.Voice)
            return playbackRegistry.SetVolumeMultiplier(handle.Id, multiplier);

        var loopList = FindLoopList(handle.Id);
        if (loopList == null)
            return false;

        loopList.VolumeMultiplier = multiplier;
        return playbackRegistry.SetVolumeMultiplier(
            loopList.CurrentPlaybackId,
            multiplier);
    }

    internal static bool FadeTo(
        AudioHandle handle,
        float multiplier,
        float duration,
        bool stopWhenSilent)
    {
        if (!TryGetVolume(handle, out var initialMultiplier))
            return false;

        multiplier = Mathf.Clamp01(multiplier);
        var version = InvalidateFade(handle);

        if (duration <= 0f)
        {
            SetVolume(handle, multiplier);
            if (stopWhenSilent && Mathf.Approximately(multiplier, 0f))
                Stop(handle);
            return true;
        }

        coroutineRunner.StartCoroutine(FadeRoutine(
            handle,
            initialMultiplier,
            multiplier,
            duration,
            stopWhenSilent,
            version));
        return true;
    }

    internal static bool FadeIn(AudioHandle handle)
    {
        if (!TryGetFadeDurations(handle, out var fadeIn, out _))
            return false;

        SetVolume(handle, 0f);
        return FadeTo(handle, 1f, fadeIn, false);
    }

    internal static bool FadeOut(AudioHandle handle)
    {
        return TryGetFadeDurations(handle, out _, out var fadeOut) &&
               FadeTo(handle, 0f, fadeOut, true);
    }

    private static AudioHandle PlayInternal(
        Audio audio,
        AudioPlayOptions options,
        Vector3? worldPosition)
    {
        if (!CanPlay(audio))
            return default;

        EnsureInitialized();
        if (coroutineRunner.IsLifecyclePaused)
            return default;

        if (audio.loopMode != AudioLoopMode.None &&
            CountValidClips(audio.clips) > 1)
            return PlayLoopList(audio, options, worldPosition);

        var hasExplicitIndex = options.ClipIndex.HasValue;
        var clip = hasExplicitIndex
            ? GetClip(audio, options.ClipIndex)
            : null;
        if ((hasExplicitIndex && clip == null) ||
            (!hasExplicitIndex && CountValidClips(audio.clips) == 0))
            return default;

        ApplyPlaybackPolicy(audio);
        var limitResult = ApplyInstanceLimit(audio, out var limitedHandle);
        if (limitResult == InstanceLimitResult.Rejected)
            return default;
        if (limitResult == InstanceLimitResult.Restarted)
            return limitedHandle;

        clip ??= SelectClip(audio);

        var point = options.Point;
        var positional = point != null || worldPosition.HasValue;
        var position = point != null
            ? point.position
            : worldPosition.GetValueOrDefault();
        var pitch = ResolvePitch(audio, options);
        var fadeIn = ResolveFadeIn(audio, options);
        var fadeOut = ResolveFadeOut(audio, options);
        var playbackId = PlayPooled(
            audio,
            clip,
            audio.loopMode != AudioLoopMode.None,
            pitch,
            positional,
            position,
            options.VolumeMultiplier,
            0,
            options.FollowTransform ? point : null,
            options.NormalizedStartTime,
            GetNextStartOrder(),
            fadeIn,
            fadeOut);

        return playbackId == 0
            ? default
            : new AudioHandle(playbackId, AudioHandleKind.Voice);
    }

    private static AudioClip GetClip(Audio audio, int? index)
    {
        if (!index.HasValue)
            return SelectClip(audio);

        if (index.Value < 0 || index.Value >= audio.clips.Length)
        {
            Debug.LogWarning(
                $"Index {index.Value} is out of bounds for Audio '{audio.name}'.");
            return null;
        }

        var clip = audio.clips[index.Value];
        if (clip == null)
        {
            Debug.LogWarning(
                $"Audio '{audio.name}' contains a null clip at index {index.Value}.");
        }

        return clip;
    }

    internal static AudioClip SelectClip(Audio audio)
    {
        if (audio == null || audio.clips == null || audio.clips.Length == 0)
            return null;

        var index = SelectRandomIndex(audio.clips);

        return index >= 0 ? audio.clips[index] : null;
    }

    private static int SelectClipIndex(
        AudioClip[] clips,
        AudioLoopMode loopMode,
        AudioClipSelectionState state)
    {
        return loopMode switch
        {
            AudioLoopMode.Sequential => SelectSequentialIndex(
                clips,
                state),
            AudioLoopMode.Shuffle => SelectShuffleIndex(
                clips,
                state),
            _ => SelectRandomIndex(clips)
        };
    }

    private static int SelectRandomIndex(AudioClip[] clips)
    {
        var validCount = CountValidClips(clips);
        if (validCount == 0)
            return -1;

        var selected = UnityEngine.Random.Range(0, validCount);
        for (var index = 0; index < clips.Length; index++)
        {
            if (clips[index] == null)
                continue;

            if (selected-- == 0)
                return index;
        }

        return -1;
    }

    private static int SelectSequentialIndex(
        AudioClip[] clips,
        AudioClipSelectionState state)
    {
        for (var offset = 0; offset < clips.Length; offset++)
        {
            var index = (state.NextSequentialIndex + offset) % clips.Length;
            if (clips[index] == null)
                continue;

            state.NextSequentialIndex = (index + 1) % clips.Length;
            return index;
        }

        return -1;
    }

    private static int SelectShuffleIndex(
        AudioClip[] clips,
        AudioClipSelectionState state)
    {
        var validCount = CountValidClips(clips);
        if (validCount == 0)
            return -1;

        if (state.ShufflePosition >= state.ShuffleIndices.Count ||
            !IsShuffleValid(clips, state.ShuffleIndices, validCount))
        {
            RebuildShuffle(clips, state);
        }

        var selected = state.ShuffleIndices[state.ShufflePosition++];
        state.LastShuffleIndex = selected;
        return selected;
    }

    private static void RebuildShuffle(
        AudioClip[] clips,
        AudioClipSelectionState state)
    {
        var indices = state.ShuffleIndices;
        indices.Clear();
        for (var index = 0; index < clips.Length; index++)
        {
            if (clips[index] != null)
                indices.Add(index);
        }

        for (var index = indices.Count - 1; index > 0; index--)
        {
            var swapIndex = UnityEngine.Random.Range(0, index + 1);
            (indices[index], indices[swapIndex]) =
                (indices[swapIndex], indices[index]);
        }

        if (indices.Count > 1 && indices[0] == state.LastShuffleIndex)
        {
            (indices[0], indices[1]) = (indices[1], indices[0]);
        }

        state.ShufflePosition = 0;
    }

    private static void InitializeSelectionAfterFirst(
        AudioClip[] clips,
        AudioLoopMode loopMode,
        AudioClipSelectionState state,
        int firstIndex)
    {
        state.NextSequentialIndex = clips.Length > 0
            ? (firstIndex + 1) % clips.Length
            : 0;

        if (loopMode != AudioLoopMode.Shuffle)
            return;

        RebuildShuffle(clips, state);
        var firstPosition = state.ShuffleIndices.IndexOf(firstIndex);
        if (firstPosition > 0)
        {
            (state.ShuffleIndices[0], state.ShuffleIndices[firstPosition]) =
                (state.ShuffleIndices[firstPosition], state.ShuffleIndices[0]);
        }

        state.ShufflePosition = firstPosition >= 0 ? 1 : 0;
        state.LastShuffleIndex = firstIndex;
    }

    private static bool IsShuffleValid(
        AudioClip[] clips,
        List<int> indices,
        int validCount)
    {
        if (indices.Count != validCount)
            return false;

        foreach (var index in indices)
        {
            if (index < 0 || index >= clips.Length || clips[index] == null)
                return false;
        }

        return true;
    }

    private static int CountValidClips(AudioClip[] clips)
    {
        var count = 0;
        foreach (var clip in clips)
        {
            if (clip != null)
                count++;
        }

        return count;
    }

    private static AudioHandle PlayLoopList(
        Audio audio,
        AudioPlayOptions options,
        Vector3? worldPosition)
    {
        var hasExplicitIndex = options.ClipIndex.HasValue;
        if (hasExplicitIndex &&
            GetClip(audio, options.ClipIndex) == null)
        {
            return default;
        }

        if (!hasExplicitIndex && CountValidClips(audio.clips) == 0)
            return default;

        ApplyPlaybackPolicy(audio);
        var limitResult = ApplyInstanceLimit(audio, out var limitedHandle);
        if (limitResult == InstanceLimitResult.Rejected)
            return default;
        if (limitResult == InstanceLimitResult.Restarted)
            return limitedHandle;

        var selectionState = new AudioClipSelectionState();
        var firstIndex = hasExplicitIndex
            ? options.ClipIndex.Value
            : SelectClipIndex(
                audio.clips,
                audio.loopMode,
                selectionState);
        if (firstIndex < 0)
            return default;

        if (hasExplicitIndex)
        {
            InitializeSelectionAfterFirst(
                audio.clips,
                audio.loopMode,
                selectionState,
                firstIndex);
        }

        var point = options.Point;
        var initialStartTime = GetSafeNormalizedStartTime(
            options.NormalizedStartTime);
        var positional = point != null || worldPosition.HasValue;
        var loopList = new AudioLoopListPlayback
        {
            Id = GetNextLoopListId(),
            Audio = audio,
            IsPositional = positional,
            Position = point != null
                ? point.position
                : worldPosition.GetValueOrDefault(),
            FollowTarget = options.FollowTransform ? point : null,
            Pitch = ResolvePitch(audio, options),
            FadeInDuration = ResolveFadeIn(audio, options),
            FadeOutDuration = ResolveFadeOut(audio, options),
            InitialStartNormalizedTime = initialStartTime,
            StartClipIndex = firstIndex,
            StartOrder = GetNextStartOrder(),
            SelectionState = selectionState,
            VolumeMultiplier = options.VolumeMultiplier,
            MixerGroup = ResolveOutputGroup(audio),
            Policy = audio.playbackPolicy
        };
        ActiveLoopLists.Add(loopList);

        if (!PlayLoopListClip(
                loopList,
                firstIndex,
                initialStartTime))
        {
            ActiveLoopLists.Remove(loopList);
            return default;
        }

        if (CountValidClips(audio.clips) > 1)
        {
            loopList.Coroutine = coroutineRunner.StartCoroutine(
                PlayLoopListRoutine(loopList, firstIndex));
        }

        return new AudioHandle(loopList.Id, AudioHandleKind.LoopList);
    }

    private static IEnumerator PlayLoopListRoutine(
        AudioLoopListPlayback loopList,
        int currentIndex)
    {
        while (loopList != null && ActiveLoopLists.Contains(loopList))
        {
            var currentClip = loopList.Audio.clips[currentIndex];
            if (currentClip == null)
            {
                StopCurrentLoopListVoice(loopList);
                loopList.Coroutine = null;
                ActiveLoopLists.Remove(loopList);
                yield break;
            }

            var absolutePitch = Mathf.Abs(loopList.Pitch);
            var remainingClip = 1f - loopList.CurrentStartNormalizedTime;
            var duration = absolutePitch <= Mathf.Epsilon
                ? float.PositiveInfinity
                : currentClip.length * remainingClip /
                  absolutePitch * 0.97f;
            yield return WaitForLoopListDuration(
                loopList,
                duration);

            if (!ActiveLoopLists.Contains(loopList))
                yield break;

            var nextIndex = SelectClipIndex(
                loopList.Audio.clips,
                loopList.Audio.loopMode,
                loopList.SelectionState);
            StopCurrentLoopListVoice(loopList);
            if (nextIndex < 0 ||
                !PlayLoopListClip(loopList, nextIndex))
            {
                loopList.Coroutine = null;
                ActiveLoopLists.Remove(loopList);
                yield break;
            }

            currentIndex = nextIndex;
        }
    }

    private static IEnumerator WaitForLoopListDuration(
        AudioLoopListPlayback loopList,
        float duration)
    {
        var elapsedTime = 0f;
        while (elapsedTime < duration &&
               loopList != null &&
               ActiveLoopLists.Contains(loopList))
        {
            if (!loopList.IsPaused && !AudioListener.pause)
                elapsedTime += Time.unscaledDeltaTime;

            yield return null;
        }
    }

    private static bool PlayLoopListClip(
        AudioLoopListPlayback loopList,
        int clipIndex,
        float? normalizedStartTime = null)
    {
        if (loopList.FollowTarget != null)
            loopList.Position = loopList.FollowTarget.position;

        loopList.CurrentStartNormalizedTime = GetSafeNormalizedStartTime(
            normalizedStartTime);
        loopList.CurrentPlaybackId = PlayPooled(
            loopList.Audio,
            loopList.Audio.clips[clipIndex],
            true,
            loopList.Pitch,
            loopList.IsPositional,
            loopList.Position,
            loopList.VolumeMultiplier,
            loopList.Id,
            loopList.FollowTarget,
            normalizedStartTime,
            loopList.StartOrder,
            loopList.FadeInDuration,
            loopList.FadeOutDuration);

        return loopList.CurrentPlaybackId != 0;
    }

    private static int PlayPooled(
        Audio audio,
        AudioClip clip,
        bool loop,
        float pitch,
        bool positional,
        Vector3 position,
        float volumeMultiplier,
        int ownerLoopListId,
        Transform followTarget,
        float? normalizedStartTime,
        long startOrder,
        float fadeInDuration,
        float fadeOutDuration)
    {
        var source = sourcePool.Get();
        if (source == null)
            return 0;

        ConfigureSource(
            source,
            audio,
            clip,
            loop,
            pitch,
            positional,
            position,
            volumeMultiplier);
        ApplyStartTime(source, normalizedStartTime);
        source.Play();
        return playbackRegistry.Register(
            audio,
            source,
            loop,
            volumeMultiplier,
            ownerLoopListId,
            followTarget,
            startOrder,
            GetSafeNormalizedStartTime(normalizedStartTime),
            fadeInDuration,
            fadeOutDuration);
    }

    private static void ApplyStartTime(
        AudioSource source,
        float? normalizedStartTime)
    {
        if (!normalizedStartTime.HasValue ||
            source.clip == null ||
            source.clip.length <= 0f)
        {
            return;
        }

        source.time = source.clip.length *
                      GetSafeNormalizedStartTime(normalizedStartTime);
    }

    private static float GetSafeNormalizedStartTime(
        float? normalizedStartTime)
    {
        return normalizedStartTime.HasValue
            ? Mathf.Clamp(
                normalizedStartTime.Value,
                0f,
                MaxNormalizedStartTime)
            : 0f;
    }

    private static void ConfigureSource(
        AudioSource source,
        Audio audio,
        AudioClip clip,
        bool loop,
        float pitch,
        bool positional,
        Vector3 position,
        float volumeMultiplier)
    {
        source.clip = clip;
        source.outputAudioMixerGroup = ResolveOutputGroup(audio);
        source.volume = audio.volume * volumeMultiplier;
        source.loop = loop;
        source.pitch = pitch;
        source.spatialBlend = positional ? audio.spatialBlend : 0f;
        source.dopplerLevel = positional ? audio.dopplerLevel : 1f;
        source.spread = positional ? audio.spread : 0f;
        source.reverbZoneMix = positional ? audio.reverbZoneMix : 1f;
        source.rolloffMode = positional
            ? audio.rolloffMode
            : AudioRolloffMode.Logarithmic;
        source.minDistance = positional ? audio.minDistance : 1f;
        source.maxDistance = positional ? audio.maxDistance : 500f;

        if (positional &&
            audio.rolloffMode == AudioRolloffMode.Custom &&
            audio.customRolloffCurve != null)
        {
            source.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                audio.customRolloffCurve);
        }

        source.transform.position = positional
            ? position
            : root.transform.position;
    }

    private static IEnumerator ChangeRoutine(
        Audio audio,
        List<AudioHandle> outgoing,
        float waitTime,
        float? fadeOutDuration,
        float? fadeInDuration,
        int changeKey,
        int version)
    {
        if (outgoing.Count > 0)
        {
            var longestFadeOut = 0f;
            foreach (var handle in outgoing)
            {
                var duration = fadeOutDuration ?? GetFadeOutDuration(handle);
                longestFadeOut = Mathf.Max(longestFadeOut, duration);
                handle.FadeOut(duration);
            }

            yield return WaitForDuration(
                longestFadeOut,
                changeKey,
                version);
        }

        if (!IsChangeCurrent(changeKey, version))
            yield break;

        yield return WaitForDuration(waitTime, changeKey, version);
        if (!IsChangeCurrent(changeKey, version))
            yield break;

        var incoming = Play(audio);
        incoming.FadeIn(fadeInDuration ?? audio.fadeInDuration);

        if (IsChangeCurrent(changeKey, version))
            ChangeVersions.Remove(changeKey);
    }

    private static IEnumerator WaitForDuration(
        float duration,
        int changeKey,
        int version)
    {
        var elapsedTime = 0f;
        while (elapsedTime < duration &&
               IsChangeCurrent(changeKey, version))
        {
            if (!AudioListener.pause)
                elapsedTime += Time.unscaledDeltaTime;

            yield return null;
        }
    }

    private static IEnumerator FadeRoutine(
        AudioHandle handle,
        float initialMultiplier,
        float targetMultiplier,
        float duration,
        bool stopWhenSilent,
        int version)
    {
        var elapsedTime = 0f;
        while (elapsedTime < duration &&
               IsFadeCurrent(handle, version) &&
               IsValid(handle))
        {
            if (!IsPaused(handle) && !AudioListener.pause)
            {
                elapsedTime += Time.unscaledDeltaTime;
                SetVolume(
                    handle,
                    Mathf.Lerp(
                        initialMultiplier,
                        targetMultiplier,
                        Mathf.Clamp01(elapsedTime / duration)));
            }

            yield return null;
        }

        if (!IsFadeCurrent(handle, version))
            yield break;

        if (IsValid(handle))
        {
            SetVolume(handle, targetMultiplier);
            if (stopWhenSilent && Mathf.Approximately(targetMultiplier, 0f))
                Stop(handle);
        }

        FadeVersions.Remove(GetHandleKey(handle));
    }

    private static void CollectHandles(
        Audio audio,
        List<AudioHandle> handles)
    {
        foreach (var loopList in ActiveLoopLists)
        {
            if (loopList.Audio == audio)
            {
                handles.Add(new AudioHandle(
                    loopList.Id,
                    AudioHandleKind.LoopList));
            }
        }

        var playbackIds = new List<int>();
        playbackRegistry.CollectByAudio(audio, playbackIds);
        foreach (var playbackId in playbackIds)
        {
            handles.Add(new AudioHandle(
                playbackId,
                AudioHandleKind.Voice));
        }
    }

    private static void CollectExclusiveHandles(
        AudioMixerGroup mixerGroup,
        List<AudioHandle> handles)
    {
        foreach (var loopList in ActiveLoopLists)
        {
            if (loopList.Policy == PlaybackPolicy.Exclusive &&
                loopList.MixerGroup == mixerGroup)
            {
                handles.Add(new AudioHandle(
                    loopList.Id,
                    AudioHandleKind.LoopList));
            }
        }

        var playbackIds = new List<int>();
        playbackRegistry.CollectExclusive(mixerGroup, playbackIds);
        foreach (var playbackId in playbackIds)
        {
            handles.Add(new AudioHandle(
                playbackId,
                AudioHandleKind.Voice));
        }
    }

    private static int InvalidateChange(int changeKey)
    {
        ChangeVersions.TryGetValue(changeKey, out var version);
        version++;
        ChangeVersions[changeKey] = version;
        return version;
    }

    private static bool IsChangeCurrent(int changeKey, int version)
    {
        return ChangeVersions.TryGetValue(changeKey, out var currentVersion) &&
               currentVersion == version;
    }

    private static int GetMixerGroupKey(AudioMixerGroup mixerGroup)
    {
        return mixerGroup != null ? mixerGroup.GetInstanceID() : 0;
    }

    private static bool TryGetVolume(
        AudioHandle handle,
        out float multiplier)
    {
        multiplier = 0f;
        if (!IsValid(handle))
            return false;

        if (handle.Kind == AudioHandleKind.Voice)
        {
            return playbackRegistry.TryGetVolumeMultiplier(
                handle.Id,
                out multiplier);
        }

        var loopList = FindLoopList(handle.Id);
        if (loopList == null)
            return false;

        multiplier = loopList.VolumeMultiplier;
        return true;
    }

    private static int InvalidateFade(AudioHandle handle)
    {
        var key = GetHandleKey(handle);
        FadeVersions.TryGetValue(key, out var version);
        version++;
        FadeVersions[key] = version;
        return version;
    }

    private static bool IsFadeCurrent(AudioHandle handle, int version)
    {
        return FadeVersions.TryGetValue(
                   GetHandleKey(handle),
                   out var currentVersion) &&
               currentVersion == version;
    }

    private static long GetHandleKey(AudioHandle handle)
    {
        return ((long)handle.Kind << 32) | (uint)handle.Id;
    }

    private static void StopCurrentLoopListVoice(
        AudioLoopListPlayback loopList)
    {
        if (loopList.CurrentPlaybackId == 0)
            return;

        playbackRegistry.Stop(loopList.CurrentPlaybackId);
        loopList.CurrentPlaybackId = 0;
    }

    private static void StopLoopList(AudioLoopListPlayback loopList)
    {
        if (loopList.Coroutine != null && coroutineRunner != null)
        {
            coroutineRunner.StopCoroutine(loopList.Coroutine);
            loopList.Coroutine = null;
        }

        StopCurrentLoopListVoice(loopList);
        ActiveLoopLists.Remove(loopList);
        InvalidateFade(new AudioHandle(loopList.Id, AudioHandleKind.LoopList));
    }

    private static void SetLoopListPaused(
        AudioLoopListPlayback loopList,
        bool paused)
    {
        if (loopList.IsPaused == paused)
            return;

        loopList.IsPaused = paused;
        if (paused)
            playbackRegistry.Pause(loopList.CurrentPlaybackId);
        else
            playbackRegistry.Resume(loopList.CurrentPlaybackId);
    }

    private static AudioLoopListPlayback FindLoopList(int loopListId)
    {
        foreach (var loopList in ActiveLoopLists)
        {
            if (loopList.Id == loopListId)
                return loopList;
        }

        return null;
    }

    private static void ApplyPlaybackPolicy(Audio audio)
    {
        switch (audio.playbackPolicy)
        {
            case PlaybackPolicy.SingleInstance:
                StopLoopListsByAudio(audio);
                playbackRegistry.StopByAudio(audio);
                break;

            case PlaybackPolicy.Exclusive:
                var mixerGroup = ResolveOutputGroup(audio);
                StopExclusiveLoopLists(mixerGroup);
                playbackRegistry.StopExclusive(mixerGroup);
                break;
        }
    }

    private static InstanceLimitResult ApplyInstanceLimit(
        Audio audio,
        out AudioHandle handle)
    {
        handle = default;
        if (audio.maxInstances <= 0)
            return InstanceLimitResult.Continue;

        playbackRegistry.Update();
        if (playbackRegistry.CountByAudio(audio) < audio.maxInstances)
            return InstanceLimitResult.Continue;

        if (!TryGetOldestHandle(audio, out handle))
            return InstanceLimitResult.Continue;

        switch (audio.instanceLimitBehavior)
        {
            case InstanceLimitBehavior.RestartOldest:
                if (Restart(handle))
                    return InstanceLimitResult.Restarted;

                handle = default;
                return InstanceLimitResult.Continue;

            case InstanceLimitBehavior.ReplaceOldest:
                Stop(handle);
                handle = default;
                return InstanceLimitResult.Continue;

            default:
                handle = default;
                return InstanceLimitResult.Rejected;
        }
    }

    private static bool TryGetOldestHandle(
        Audio audio,
        out AudioHandle handle)
    {
        handle = default;
        if (!playbackRegistry.TryGetOldestByAudio(
                audio,
                out var playbackId,
                out var ownerLoopListId))
        {
            return false;
        }

        if (ownerLoopListId != 0 && FindLoopList(ownerLoopListId) != null)
        {
            handle = new AudioHandle(
                ownerLoopListId,
                AudioHandleKind.LoopList);
            return true;
        }

        handle = new AudioHandle(playbackId, AudioHandleKind.Voice);
        return true;
    }

    private static bool Restart(AudioHandle handle)
    {
        InvalidateFade(handle);
        var startOrder = GetNextStartOrder();
        if (handle.Kind == AudioHandleKind.Voice)
            return playbackRegistry.Restart(handle.Id, startOrder);

        var loopList = FindLoopList(handle.Id);
        if (loopList == null)
            return false;

        return RestartLoopList(loopList, startOrder);
    }

    private static bool RestartLoopList(
        AudioLoopListPlayback loopList,
        long startOrder)
    {
        if (loopList.Coroutine != null && coroutineRunner != null)
        {
            coroutineRunner.StopCoroutine(loopList.Coroutine);
            loopList.Coroutine = null;
        }

        StopCurrentLoopListVoice(loopList);
        loopList.IsPaused = false;
        loopList.StartOrder = startOrder;

        var clips = loopList.Audio.clips;
        var firstIndex = loopList.StartClipIndex;
        if (firstIndex < 0 ||
            firstIndex >= clips.Length ||
            clips[firstIndex] == null)
        {
            firstIndex = SelectRandomIndex(clips);
        }

        if (firstIndex < 0)
        {
            ActiveLoopLists.Remove(loopList);
            return false;
        }

        loopList.SelectionState = new AudioClipSelectionState();
        InitializeSelectionAfterFirst(
            clips,
            loopList.Audio.loopMode,
            loopList.SelectionState,
            firstIndex);
        if (!PlayLoopListClip(
                loopList,
                firstIndex,
                loopList.InitialStartNormalizedTime))
        {
            ActiveLoopLists.Remove(loopList);
            return false;
        }

        if (CountValidClips(clips) > 1)
        {
            loopList.Coroutine = coroutineRunner.StartCoroutine(
                PlayLoopListRoutine(loopList, firstIndex));
        }

        return true;
    }

    private static void StopLoopListsByAudio(Audio audio)
    {
        for (var index = ActiveLoopLists.Count - 1; index >= 0; index--)
        {
            var loopList = ActiveLoopLists[index];
            if (loopList.Audio == audio)
                StopLoopList(loopList);
        }
    }

    private static void StopExclusiveLoopLists(AudioMixerGroup mixerGroup)
    {
        for (var index = ActiveLoopLists.Count - 1; index >= 0; index--)
        {
            var loopList = ActiveLoopLists[index];
            if (loopList.Policy == PlaybackPolicy.Exclusive &&
                loopList.MixerGroup == mixerGroup)
            {
                StopLoopList(loopList);
            }
        }
    }

    private static int GetNextLoopListId()
    {
        var id = nextLoopListId;
        nextLoopListId = nextLoopListId == int.MaxValue
            ? 1
            : nextLoopListId + 1;
        return id;
    }

    private static long GetNextStartOrder()
    {
        var order = nextStartOrder;
        nextStartOrder = nextStartOrder == long.MaxValue
            ? 1
            : nextStartOrder + 1;
        return order;
    }

    private static bool CanPlay(Audio audio)
    {
        if (audio != null && audio.clips != null && audio.clips.Length > 0)
            return true;

        Debug.LogWarning("Attempted to play a null or empty Audio asset.");
        return false;
    }

    private static float ResolvePitch(
        Audio audio,
        AudioPlayOptions options)
    {
        if (options.Pitch.HasValue)
            return Mathf.Clamp(options.Pitch.Value, -3f, 3f);

        return audio.pitchMode switch
        {
            AudioPitchMode.Fixed => Mathf.Clamp(audio.pitch, -3f, 3f),
            AudioPitchMode.Random => UnityEngine.Random.Range(
                Mathf.Min(audio.pitchRange.x, audio.pitchRange.y),
                Mathf.Max(audio.pitchRange.x, audio.pitchRange.y)),
            _ => 1f
        };
    }

    private static float ResolveFadeIn(
        Audio audio,
        AudioPlayOptions options)
    {
        return Mathf.Max(0f, options.FadeIn ?? audio.fadeInDuration);
    }

    private static float ResolveFadeOut(
        Audio audio,
        AudioPlayOptions options)
    {
        return Mathf.Max(0f, options.FadeOut ?? audio.fadeOutDuration);
    }

    private static bool TryGetFadeDurations(
        AudioHandle handle,
        out float fadeIn,
        out float fadeOut)
    {
        if (!IsValid(handle))
        {
            fadeIn = 0f;
            fadeOut = 0f;
            return false;
        }

        if (handle.Kind == AudioHandleKind.Voice)
        {
            return playbackRegistry.TryGetFadeDurations(
                handle.Id,
                out fadeIn,
                out fadeOut);
        }

        var loopList = FindLoopList(handle.Id);
        if (loopList != null)
        {
            fadeIn = loopList.FadeInDuration;
            fadeOut = loopList.FadeOutDuration;
            return true;
        }

        fadeIn = 0f;
        fadeOut = 0f;
        return false;
    }

    private static float GetFadeOutDuration(AudioHandle handle)
    {
        return TryGetFadeDurations(handle, out _, out var fadeOut)
            ? fadeOut
            : 0f;
    }

    private static void EnsureInitialized()
    {
        if (root != null)
            return;

        var settings = AudioSystemSettings.Instance;
        var mixer = settings != null ? settings.MainMixer : null;

        root = new GameObject("[AudioManager]");
        UnityEngine.Object.DontDestroyOnLoad(root);
        coroutineRunner = root.AddComponent<AudioCoroutineRunner>();

        rootMixerGroup = FindRootMixerGroup(mixer);
        var initialVoiceCount = settings != null
            ? settings.InitialVoiceCount
            : 8;
        var maxVoiceCount = settings != null
            ? settings.MaxVoiceCount
            : 24;
        sourcePool = new AudioSourcePool(
            root.transform,
            rootMixerGroup,
            initialVoiceCount,
            maxVoiceCount);
        playbackRegistry = new AudioPlaybackRegistry(
            sourcePool,
            maxVoiceCount);
        coroutineRunner.PlaybackRegistry = playbackRegistry;

        if (settings == null)
        {
            Debug.LogError(
                "Audio System settings are unavailable. Open Project Settings > Audio System " +
                "and make sure its settings asset is registered as a preloaded asset.");
        }
        else if (mixer == null)
        {
            Debug.LogError(
                "No main AudioMixer is assigned in Project Settings > Audio System.");
        }
    }

    private static AudioMixerGroup ResolveOutputGroup(Audio audio)
    {
        return audio.mixerGroup != null ? audio.mixerGroup : rootMixerGroup;
    }

    private static AudioMixerGroup FindRootMixerGroup(AudioMixer mixer)
    {
        if (mixer == null)
            return null;

        var groups = mixer.FindMatchingGroups(string.Empty);
        if (groups.Length > 0)
            return groups[0];

        Debug.LogError(
            $"AudioMixer '{mixer.name}' does not contain any mixer groups.");
        return null;
    }
}
}
