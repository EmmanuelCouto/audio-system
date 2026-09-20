using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Emmanuel.AudioSystem;

namespace Emmanuel.AudioSystem.Editor
{
[CustomEditor(typeof(Audio), true)]
[CanEditMultipleObjects]
internal sealed class AudioEditor : UnityEditor.Editor
{
    private const string SpatialSettingsPreferenceKey =
        "AudioSystem.AudioEditor.Expand3DSoundSettings";
    private const int MaxCachedPreviews = 128;
    private const float HeaderIconSize = 32f;
    private const float HeaderButtonPadding = 1f;
    private const float HeaderDropdownSize = 14f;
    private const string MaxInstancesControlName =
        "AudioSystem.AudioEditor.MaxInstances";

    private static readonly Dictionary<PreviewKey, Texture2D> PreviewCache = new();
    private static readonly GUIContent HeaderIconButtonContent = new();
    private static readonly GUIContent[] SingleClipLoopOptions =
    {
        new("None"),
        new("Enable")
    };
    private static readonly GUIContent[] ListLoopOptions =
    {
        new("None"),
        new("Sequential"),
        new("Random"),
        new("Shuffle")
    };

    private GUIContent headerDropdownIcon;
    private GUIStyle headerButtonHoverStyle;
    private AudioClipListGUI clipListGUI;
    private bool spatialSettingsExpanded;

    static AudioEditor()
    {
        AssemblyReloadEvents.beforeAssemblyReload += ClearPreviewCache;
        EditorApplication.projectChanged += ClearPreviewCache;
    }

    public override void OnInspectorGUI()
    {
        EnsureMouseMoveEvents();
        serializedObject.Update();

        var clipsProperty = serializedObject.FindProperty("clips");
        if (targets.Length == 1)
        {
            clipListGUI ??= new AudioClipListGUI(Repaint);
            clipListGUI.Draw(clipsProperty, (Audio)target);
        }
        else
        {
            EditorGUILayout.PropertyField(
                clipsProperty,
                new GUIContent("Audio Clips"),
                true);
        }
        EditorGUILayout.Space(3f);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("volume"));
        DrawPitchSettings();
        DrawMixerGroupDropdown(serializedObject.FindProperty("mixerGroup"));
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("playbackPolicy"),
            new GUIContent(
                "Playback Policy",
                "Overlap allows simultaneous instances. Single Instance replaces the same Audio. Exclusive replaces another Exclusive Audio in the same Category."));
        DrawInstanceLimitSettings();
        DrawPlaybackSettings(clipsProperty);
        DrawFadeDurations();
        Draw3DSoundSettings();

        if (serializedObject.ApplyModifiedProperties())
            EditorApplication.RepaintProjectWindow();
    }

    private void DrawPitchSettings()
    {
        var modeProperty = serializedObject.FindProperty("pitchMode");
        var pitchProperty = serializedObject.FindProperty("pitch");
        var rangeProperty = serializedObject.FindProperty("pitchRange");
        var label = new GUIContent(
            "Pitch",
            "Normal uses pitch 1. Fixed uses one value. Random selects a value from the configured range once per playback.");
        var position = EditorGUILayout.GetControlRect();
        var contentPosition = EditorGUI.PrefixLabel(position, label);
        var modeWidth = Mathf.Min(100f, contentPosition.width * 0.38f);
        var modePosition = new Rect(
            contentPosition.x,
            contentPosition.y,
            modeWidth,
            contentPosition.height);
        var valuePosition = new Rect(
            modePosition.xMax + 3f,
            contentPosition.y,
            Mathf.Max(0f, contentPosition.width - modeWidth - 3f),
            contentPosition.height);

        EditorGUI.PropertyField(modePosition, modeProperty, GUIContent.none);
        if (modeProperty.hasMultipleDifferentValues)
            return;

        switch ((AudioPitchMode)modeProperty.enumValueIndex)
        {
            case AudioPitchMode.Fixed:
                EditorGUI.Slider(valuePosition, pitchProperty, -3f, 3f, GUIContent.none);
                break;

            case AudioPitchMode.Random:
                DrawMinMaxFields(valuePosition, rangeProperty, -3f, 3f);
                break;
        }
    }

    private void DrawFadeDurations()
    {
        var fadeInProperty = serializedObject.FindProperty("fadeInDuration");
        var fadeOutProperty = serializedObject.FindProperty("fadeOutDuration");
        var label = new GUIContent(
            "Fade Duration",
            "Default durations used by FadeIn, FadeOut, and Change. Play does not fade automatically.");
        var position = EditorGUILayout.GetControlRect();
        var contentPosition = EditorGUI.PrefixLabel(position, label);
        var values = new[]
        {
            fadeInProperty.floatValue,
            fadeOutProperty.floatValue
        };
        var labels = new[]
        {
            new GUIContent("In"),
            new GUIContent("Out")
        };

        var previousMixedValue = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue =
            fadeInProperty.hasMultipleDifferentValues ||
            fadeOutProperty.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        EditorGUI.MultiFloatField(contentPosition, labels, values);
        if (EditorGUI.EndChangeCheck())
        {
            fadeInProperty.floatValue = Mathf.Max(0f, values[0]);
            fadeOutProperty.floatValue = Mathf.Max(0f, values[1]);
        }
        EditorGUI.showMixedValue = previousMixedValue;
    }

    private static void DrawMinMaxFields(
        Rect position,
        SerializedProperty rangeProperty,
        float minimum,
        float maximum)
    {
        var range = rangeProperty.vector2Value;
        var values = new[] { range.x, range.y };
        var labels = new[]
        {
            new GUIContent("Min"),
            new GUIContent("Max")
        };

        var previousMixedValue = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = rangeProperty.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        EditorGUI.MultiFloatField(position, labels, values);
        if (EditorGUI.EndChangeCheck())
        {
            var min = Mathf.Clamp(Mathf.Min(values[0], values[1]), minimum, maximum);
            var max = Mathf.Clamp(Mathf.Max(values[0], values[1]), minimum, maximum);
            rangeProperty.vector2Value = new Vector2(min, max);
        }
        EditorGUI.showMixedValue = previousMixedValue;
    }

    private void DrawPlaybackSettings(SerializedProperty clipsProperty)
    {
        var hasDifferentLists = clipsProperty.hasMultipleDifferentValues;
        var assignedClipCount = hasDifferentLists
            ? 0
            : CountAssignedClips(clipsProperty);
        var hasMultipleClips = hasDifferentLists || assignedClipCount > 1;

        if (!hasDifferentLists && assignedClipCount == 0)
            return;

        var loopProperty = serializedObject.FindProperty("loopMode");
        var label = new GUIContent(
            "Loop",
            hasMultipleClips
                ? "Controls whether the list repeats and how each next clip is selected."
                : "Controls whether the configured clip repeats.");
        var options = hasMultipleClips
            ? ListLoopOptions
            : SingleClipLoopOptions;
        var selectedIndex = hasMultipleClips
            ? Mathf.Clamp(loopProperty.enumValueIndex, 0, options.Length - 1)
            : loopProperty.enumValueIndex == (int)AudioLoopMode.None
                ? 0
                : 1;

        var previousMixedValue = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = loopProperty.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        selectedIndex = EditorGUILayout.Popup(
            label,
            selectedIndex,
            options);
        if (EditorGUI.EndChangeCheck())
        {
            loopProperty.enumValueIndex = hasMultipleClips
                ? selectedIndex
                : selectedIndex == 0
                    ? (int)AudioLoopMode.None
                    : (int)AudioLoopMode.Random;
        }
        EditorGUI.showMixedValue = previousMixedValue;
    }

    private static int CountAssignedClips(SerializedProperty clipsProperty)
    {
        var count = 0;
        for (var index = 0; index < clipsProperty.arraySize; index++)
        {
            if (clipsProperty.GetArrayElementAtIndex(index)
                    .objectReferenceValue != null)
            {
                count++;
            }
        }

        return count;
    }

    private void DrawInstanceLimitSettings()
    {
        var maximumProperty = serializedObject.FindProperty("maxInstances");
        DrawMaximumInstancesField(maximumProperty);

        if (!maximumProperty.hasMultipleDifferentValues &&
            maximumProperty.intValue <= 0)
            return;

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("instanceLimitBehavior"),
            new GUIContent(
                "At Instance Limit",
                "Ignore the new request, restart the oldest instance with its original settings, or replace it using the new request settings."));
    }

    private static void DrawMaximumInstancesField(
        SerializedProperty maximumProperty)
    {
        var label = new GUIContent(
            "Max Instances",
            "Maximum simultaneous instances of this Audio. Zero means unlimited.");

        if (maximumProperty.hasMultipleDifferentValues)
        {
            EditorGUILayout.PropertyField(maximumProperty, label);
            return;
        }

        var position = EditorGUILayout.GetControlRect();
        EditorGUI.BeginProperty(position, label, maximumProperty);
        var valuePosition = EditorGUI.PrefixLabel(position, label);

        GUI.SetNextControlName(MaxInstancesControlName);
        EditorGUI.BeginChangeCheck();
        var value = EditorGUI.DelayedIntField(
            valuePosition,
            maximumProperty.intValue);
        if (EditorGUI.EndChangeCheck())
            maximumProperty.intValue = Mathf.Max(0, value);

        if (maximumProperty.intValue <= 0 &&
            GUI.GetNameOfFocusedControl() != MaxInstancesControlName &&
            Event.current.type == EventType.Repaint)
        {
            GUI.Label(
                valuePosition,
                new GUIContent("Unlimited", label.tooltip),
                EditorStyles.textField);
        }

        EditorGUI.EndProperty();
    }

    private static void DrawMixerGroupDropdown(
        SerializedProperty mixerGroupProperty)
    {
        var settings = AudioSystemSettingsProvider.FindSettings();
        var mainMixer = settings != null ? settings.MainMixer : null;

        AudioMixerGroupDropdownGUI.Draw(
            mixerGroupProperty,
            new GUIContent(
                "Category",
                "Category and AudioMixerGroup used to route this Audio."),
            mainMixer,
            "Main / Root (Default)");

        var currentGroup = mixerGroupProperty.objectReferenceValue as
            UnityEngine.Audio.AudioMixerGroup;
        if (!mixerGroupProperty.hasMultipleDifferentValues &&
            currentGroup != null &&
            currentGroup.audioMixer != mainMixer)
        {
            EditorGUILayout.HelpBox(
                "This AudioMixerGroup does not belong to the configured Main AudioMixer.",
                MessageType.Warning);
        }
    }

    private void OnEnable()
    {
        clipListGUI = new AudioClipListGUI(Repaint);
        spatialSettingsExpanded = EditorPrefs.GetBool(
            SpatialSettingsPreferenceKey,
            false);
        AudioEditorPreviewPlayer.StateChanged += Repaint;
    }

    private void Draw3DSoundSettings()
    {
        var expanded = EditorGUILayout.Foldout(
            spatialSettingsExpanded,
            "3D Sound Settings",
            true);
        if (expanded != spatialSettingsExpanded)
        {
            spatialSettingsExpanded = expanded;
            EditorPrefs.SetBool(
                SpatialSettingsPreferenceKey,
                spatialSettingsExpanded);
        }

        if (!spatialSettingsExpanded)
            return;

        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.HelpBox(
                "These settings are applied when this Audio is played at a Transform or world position.",
                MessageType.Info);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("dopplerLevel"),
                new GUIContent("Doppler Level"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("spatialBlend"),
                new GUIContent("Spatial Blend"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("spread"),
                new GUIContent("Spread"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("reverbZoneMix"),
                new GUIContent("Reverb Zone Mix"));

            var rolloffProperty = serializedObject.FindProperty("rolloffMode");
            EditorGUILayout.PropertyField(
                rolloffProperty,
                new GUIContent("Volume Rolloff"));

            if (!rolloffProperty.hasMultipleDifferentValues &&
                rolloffProperty.enumValueIndex ==
                (int)AudioRolloffMode.Custom)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("customRolloffCurve"),
                    new GUIContent("Custom Rolloff"));
            }

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("minDistance"),
                new GUIContent("Min Distance"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("maxDistance"),
                new GUIContent("Max Distance"));
        }
    }

    private void OnDisable()
    {
        AudioEditorPreviewPlayer.StateChanged -= Repaint;
        AudioEditorPreviewPlayer.Stop();
    }

    public override bool RequiresConstantRepaint()
    {
        return true;
    }

    protected override void OnHeaderGUI()
    {
        EnsureMouseMoveEvents();
        base.OnHeaderGUI();
        EnsureHeaderVisuals();

        var headerPosition = GUILayoutUtility.GetLastRect();
        var iconPosition = new Rect(
            headerPosition.x + 6f,
            headerPosition.y + 6f,
            HeaderIconSize,
            HeaderIconSize);
        var buttonPosition = new Rect(
            iconPosition.x - HeaderButtonPadding,
            iconPosition.y - HeaderButtonPadding,
            iconPosition.width + (HeaderButtonPadding * 2f),
            iconPosition.height + (HeaderButtonPadding * 2f));

        serializedObject.UpdateIfRequiredOrScript();
        var colorProperty = serializedObject.FindProperty("iconColor");
        var color = colorProperty != null &&
                    !colorProperty.hasMultipleDifferentValues
            ? colorProperty.colorValue
            : Color.white;
        HeaderIconButtonContent.tooltip = colorProperty != null &&
                                          colorProperty.hasMultipleDifferentValues
            ? "Choose the icon color for the selected Audio assets."
            : $"Icon Color: {ToHex(color)}";

        var isHovering = buttonPosition.Contains(Event.current.mousePosition);
        DrawHeaderButtonHover(buttonPosition, isHovering);

        if (isHovering)
            DrawOfficialHeaderIcon(iconPosition);

        DrawHeaderDropdownIndicator(buttonPosition);

        var openPopup = GUI.Button(
            buttonPosition,
            HeaderIconButtonContent,
            GUIStyle.none);

        if (openPopup)
        {
            PopupWindow.Show(
                buttonPosition,
                new AudioIconColorPopup(
                    targets,
                    Repaint));
        }
    }

    private void EnsureHeaderVisuals()
    {
        headerDropdownIcon ??=
            EditorGUIUtility.IconContent("icon dropdown");

        if (headerButtonHoverStyle != null)
            return;

        headerButtonHoverStyle = new GUIStyle(EditorStyles.iconButton)
        {
            fixedWidth = 0f,
            fixedHeight = 0f,
            stretchWidth = true,
            stretchHeight = true,
            padding = new RectOffset(),
            margin = new RectOffset(),
            overflow = new RectOffset(),
            contentOffset = Vector2.zero
        };
    }

    private void DrawHeaderButtonHover(
        Rect buttonPosition,
        bool isHovering)
    {
        if (!isHovering || Event.current.type != EventType.Repaint)
            return;

        headerButtonHoverStyle.Draw(
            buttonPosition,
            GUIContent.none,
            true,
            false,
            false,
            false);
    }

    private Texture2D GetOfficialHeaderIcon()
    {
        var preview = AssetPreview.GetAssetPreview(target);
        return preview != null
            ? preview
            : AssetPreview.GetMiniThumbnail(target);
    }

    private void DrawOfficialHeaderIcon(Rect iconPosition)
    {
        var icon = GetOfficialHeaderIcon();
        if (icon == null)
            return;

        GUI.DrawTexture(
            iconPosition,
            icon,
            ScaleMode.ScaleToFit,
            true);
    }

    private static void EnsureMouseMoveEvents()
    {
        var hoveredWindow = EditorWindow.mouseOverWindow;
        if (hoveredWindow != null)
            hoveredWindow.wantsMouseMove = true;
    }

    private static string ToHex(Color color)
    {
        var color32 = (Color32)color;
        return color32.a == byte.MaxValue
            ? $"#{color32.r:X2}{color32.g:X2}{color32.b:X2}"
            : $"#{color32.r:X2}{color32.g:X2}{color32.b:X2}{color32.a:X2}";
    }

    private void DrawHeaderDropdownIndicator(Rect buttonPosition)
    {
        if (headerDropdownIcon?.image == null)
            return;

        var arrowPosition = new Rect(
            buttonPosition.xMax - HeaderDropdownSize + 1f,
            buttonPosition.yMax - HeaderDropdownSize + 1f,
            HeaderDropdownSize,
            HeaderDropdownSize);
        GUI.DrawTexture(
            arrowPosition,
            headerDropdownIcon.image,
            ScaleMode.ScaleToFit,
            true);
    }

    public override Texture2D RenderStaticPreview(
        string assetPath,
        UnityEngine.Object[] subAssets,
        int width,
        int height)
    {
        var audio = (Audio)target;
        var audioSettings = AudioSystemSettingsProvider.FindSettings();
        var icon = audioSettings != null
            ? audioSettings.GetIcon(audio.mixerGroup)
            : null;
        serializedObject.UpdateIfRequiredOrScript();
        var colorProperty = serializedObject.FindProperty("iconColor");
        var iconColor = colorProperty != null
            ? colorProperty.colorValue
            : Color.white;

        return icon != null
            ? GetTintedPreview(icon, iconColor, width, height)
            : base.RenderStaticPreview(assetPath, subAssets, width, height);
    }

    private static Texture2D GetTintedPreview(
        Texture2D source,
        Color color,
        int width,
        int height)
    {
        var key = new PreviewKey(source.GetInstanceID(), color, width, height);
        if (PreviewCache.TryGetValue(key, out var cachedPreview) &&
            cachedPreview != null)
        {
            return cachedPreview;
        }

        if (PreviewCache.Count >= MaxCachedPreviews)
            ClearPreviewCache();

        var preview = CreateTintedTexture(source, color, width, height);
        PreviewCache[key] = preview;
        return preview;
    }

    private static Texture2D CreateTintedTexture(
        Texture2D source,
        Color color,
        int width,
        int height)
    {
        var previousRenderTexture = RenderTexture.active;
        var renderTexture = RenderTexture.GetTemporary(
            width,
            height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear);

        try
        {
            RenderTexture.active = renderTexture;
            Graphics.Blit(source, renderTexture);

            var result = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = $"{source.name} ({color})"
            };
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);

            // Category icons are white masks. Replacing their RGB values keeps
            // the selected editor color exact, regardless of the project's
            // color space or the source texture's sRGB import setting.
            var tint = (Color32)color;
            var pixels = result.GetPixels32();
            for (var index = 0; index < pixels.Length; index++)
            {
                var sourceAlpha = pixels[index].a;
                pixels[index] = new Color32(
                    tint.r,
                    tint.g,
                    tint.b,
                    (byte)((sourceAlpha * tint.a + 127) / 255));
            }

            result.SetPixels32(pixels);
            result.Apply(false, false);
            return result;
        }
        finally
        {
            RenderTexture.active = previousRenderTexture;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }

    private static void ClearPreviewCache()
    {
        PreviewCache.Clear();
    }

    private readonly struct PreviewKey : IEquatable<PreviewKey>
    {
        private readonly int sourceId;
        private readonly Color32 color;
        private readonly int width;
        private readonly int height;

        public PreviewKey(int sourceId, Color color, int width, int height)
        {
            this.sourceId = sourceId;
            this.color = color;
            this.width = width;
            this.height = height;
        }

        public bool Equals(PreviewKey other)
        {
            return sourceId == other.sourceId &&
                   color.Equals(other.color) &&
                   width == other.width &&
                   height == other.height;
        }

        public override bool Equals(object obj)
        {
            return obj is PreviewKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(sourceId, color, width, height);
        }
    }
    }
}
