using UnityEngine;
using UnityEngine.Audio;

namespace Emmanuel.AudioSystem
{
#if UNITY_EDITOR
using System;
using System.Collections.Generic;

[Serializable]
public sealed class AudioCategory
{
    [SerializeField]
    private string name;

    [SerializeField]
    private AudioMixerGroup mixerGroup;

    [SerializeField]
    private Texture2D icon;

    public string Name => name;
    public AudioMixerGroup MixerGroup => mixerGroup;
    public Texture2D Icon => icon;
}
#endif

public sealed class AudioSystemSettings : ScriptableObject
{
    [SerializeField]
    [Tooltip("Main AudioMixer used by the Audio System.")]
    private AudioMixer mainMixer;

    [SerializeField, HideInInspector]
    private AudioMixer defaultMixer;

    [SerializeField, HideInInspector]
    private bool isPackageDefault;

    [SerializeField, Min(1)]
    [Tooltip("Number of reusable AudioSources created when the Audio System initializes.")]
    private int initialVoiceCount = 8;

    [SerializeField, Min(1)]
    [Tooltip("Maximum number of simultaneous pooled AudioSources.")]
    private int maxVoiceCount = 24;

    public AudioMixer MainMixer => mainMixer;
    public int InitialVoiceCount => Mathf.Max(1, initialVoiceCount);
    public int MaxVoiceCount => Mathf.Max(InitialVoiceCount, maxVoiceCount);

#if UNITY_EDITOR
    [SerializeField]
    private Texture2D defaultIcon;

    [SerializeField]
    private List<AudioCategory> categories = new();

    public Texture2D DefaultIcon => defaultIcon;
    public IReadOnlyList<AudioCategory> Categories => categories;
#endif

    internal static AudioSystemSettings Instance { get; private set; }

    private void OnEnable()
    {
        if (!isPackageDefault)
            Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnValidate()
    {
        initialVoiceCount = Mathf.Max(1, initialVoiceCount);
        maxVoiceCount = Mathf.Max(initialVoiceCount, maxVoiceCount);
    }

#if UNITY_EDITOR
    public Texture2D GetIcon(AudioMixerGroup mixerGroup)
    {
        if (mixerGroup == null)
            return defaultIcon;

        foreach (var category in categories)
        {
            if (category.MixerGroup == mixerGroup && category.Icon != null)
                return category.Icon;
        }

        return defaultIcon;
    }
#endif
}
}
