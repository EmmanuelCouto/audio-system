using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Emmanuel.AudioSystem;

namespace Emmanuel.AudioSystem.Editor
{
[InitializeOnLoad]
internal static class AudioWaveformCache
{
    private const int TextureWidth = 512;
    private const int TextureHeight = 64;
    private const int FramesPerChunk = 8192;
    private const int ChunksPerUpdate = 8;
    private const int MaxCachedWaveforms = 64;

    private static readonly Color32 WaveColor =
        new(252, 191, 7, 255);
    private static readonly Dictionary<int, Entry> Entries = new();
    private static readonly Queue<int> PendingEntries = new();

    static AudioWaveformCache()
    {
        EditorApplication.update += Update;
        EditorApplication.projectChanged += Clear;
        AssemblyReloadEvents.beforeAssemblyReload += Clear;
    }

    internal static Texture2D Get(AudioClip clip, out bool unavailable)
    {
        unavailable = false;
        if (clip == null)
            return null;

        var id = clip.GetInstanceID();
        if (!Entries.TryGetValue(id, out var entry))
        {
            EnsureCapacity();
            entry = new Entry(clip);
            Entries.Add(id, entry);
            if (!entry.IsComplete)
                PendingEntries.Enqueue(id);
        }

        unavailable = entry.failed;
        return entry.texture;
    }

    private static void Update()
    {
        if (PendingEntries.Count == 0)
            return;

        var id = PendingEntries.Peek();
        if (!Entries.TryGetValue(id, out var entry) || entry.IsComplete)
        {
            PendingEntries.Dequeue();
            return;
        }

        ProcessEntry(entry);
        if (entry.IsComplete)
            PendingEntries.Dequeue();
    }

    private static void ProcessEntry(Entry entry)
    {
        if (entry.clip == null || entry.clip.samples <= 0)
        {
            entry.failed = true;
            return;
        }

        if (entry.clip.loadState == AudioDataLoadState.Unloaded)
        {
            if (!entry.clip.LoadAudioData())
                entry.failed = true;
            return;
        }

        if (entry.clip.loadState == AudioDataLoadState.Loading)
            return;

        if (entry.clip.loadState == AudioDataLoadState.Failed)
        {
            entry.failed = true;
            return;
        }

        entry.EnsureBuffer();
        for (var chunk = 0;
             chunk < ChunksPerUpdate && entry.nextFrame < entry.clip.samples;
             chunk++)
        {
            if (!entry.ReadNextChunk())
            {
                entry.failed = true;
                return;
            }
        }

        if (entry.nextFrame >= entry.clip.samples)
            entry.CreateTexture();
    }

    private static void EnsureCapacity()
    {
        if (Entries.Count < MaxCachedWaveforms)
            return;

        var keyToRemove = 0;
        var hasEntry = false;
        foreach (var pair in Entries)
        {
            if (pair.Value.texture != null)
                Object.DestroyImmediate(pair.Value.texture);
            keyToRemove = pair.Key;
            hasEntry = true;
            break;
        }

        if (hasEntry)
            Entries.Remove(keyToRemove);
    }

    private static void Clear()
    {
        foreach (var entry in Entries.Values)
        {
            if (entry.texture != null)
                Object.DestroyImmediate(entry.texture);
        }

        Entries.Clear();
        PendingEntries.Clear();
    }

    private sealed class Entry
    {
        internal readonly AudioClip clip;
        internal Texture2D texture;
        internal bool failed;
        internal int nextFrame;

        private readonly float[] minimum = new float[TextureWidth];
        private readonly float[] maximum = new float[TextureWidth];
        private readonly string persistentCacheKey;
        private float[] sampleBuffer;

        internal bool IsComplete => texture != null || failed;

        internal Entry(AudioClip clip)
        {
            this.clip = clip;
            persistentCacheKey = AudioEditorTextureCache.CreateKey(
                clip,
                $"waveform-v1:{TextureWidth}:{TextureHeight}:{WaveColor}");
            texture = AudioEditorTextureCache.Load(
                "Waveforms",
                persistentCacheKey,
                TextureWidth,
                TextureHeight,
                $"{clip.name} Waveform",
                FilterMode.Bilinear);
        }

        internal void EnsureBuffer()
        {
            var requiredLength = FramesPerChunk * Mathf.Max(1, clip.channels);
            if (sampleBuffer == null || sampleBuffer.Length != requiredLength)
                sampleBuffer = new float[requiredLength];
        }

        internal bool ReadNextChunk()
        {
            var channels = Mathf.Max(1, clip.channels);
            var framesToRead = Mathf.Min(
                FramesPerChunk,
                clip.samples - nextFrame);
            var samplesToRead = framesToRead * channels;
            if (sampleBuffer.Length != samplesToRead)
                sampleBuffer = new float[samplesToRead];
            if (!clip.GetData(sampleBuffer, nextFrame))
                return false;

            for (var frame = 0; frame < framesToRead; frame++)
            {
                var frameMinimum = 0f;
                var frameMaximum = 0f;
                var sampleOffset = frame * channels;
                for (var channel = 0; channel < channels; channel++)
                {
                    var sample = sampleBuffer[sampleOffset + channel];
                    frameMinimum = Mathf.Min(frameMinimum, sample);
                    frameMaximum = Mathf.Max(frameMaximum, sample);
                }

                var waveformIndex = Mathf.Min(
                    TextureWidth - 1,
                    (int)((long)(nextFrame + frame) * TextureWidth /
                          clip.samples));
                minimum[waveformIndex] = Mathf.Min(
                    minimum[waveformIndex],
                    frameMinimum);
                maximum[waveformIndex] = Mathf.Max(
                    maximum[waveformIndex],
                    frameMaximum);
            }

            nextFrame += framesToRead;
            return true;
        }

        internal void CreateTexture()
        {
            var pixels = new Color32[TextureWidth * TextureHeight];
            var center = TextureHeight / 2;
            var amplitude = center - 2;

            for (var x = 0; x < TextureWidth; x++)
            {
                var bottom = Mathf.Clamp(
                    center + Mathf.RoundToInt(minimum[x] * amplitude),
                    1,
                    TextureHeight - 2);
                var top = Mathf.Clamp(
                    center + Mathf.RoundToInt(maximum[x] * amplitude),
                    1,
                    TextureHeight - 2);
                if (top < bottom)
                    (bottom, top) = (top, bottom);

                for (var y = bottom; y <= top; y++)
                    pixels[y * TextureWidth + x] = WaveColor;
            }

            texture = new Texture2D(
                TextureWidth,
                TextureHeight,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = $"{clip.name} Waveform",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            AudioEditorTextureCache.Save(
                "Waveforms",
                persistentCacheKey,
                texture);
            texture.Apply(false, true);
            sampleBuffer = null;
        }
    }
}
}
