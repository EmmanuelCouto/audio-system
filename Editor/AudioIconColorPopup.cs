using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Emmanuel.AudioSystem;

namespace Emmanuel.AudioSystem.Editor
{
internal sealed class AudioIconColorPopup : PopupWindowContent
{
    private const string MixerGroupPropertyName = "mixerGroup";
    private const string IconColorPropertyName = "iconColor";
    private const int CategoryColumnCount = 4;
    private const int ColorColumnCount = 6;
    private const float Padding = 8f;
    private const float CategoryCellWidth = 68f;
    private const float CategoryCellHeight = 52f;
    private const float ColorCellSize = 24f;
    private const float CellSpacing = 3f;
    private const float LabelHeight = 18f;
    private const float CustomFieldHeight = 20f;
    private const float DividerHeight = 1f;
    private const float PopupWidth = 300f;

    private static readonly Color32[] PresetColors =
    {
        new(252, 191, 7, 255),
        new(255, 255, 255, 255),
        new(180, 180, 180, 255),
        new(100, 100, 100, 255),
        new(65, 115, 205, 255),
        new(45, 180, 205, 255),
        new(55, 185, 105, 255),
        new(160, 205, 65, 255),
        new(240, 145, 45, 255),
        new(225, 80, 75, 255),
        new(210, 60, 125, 255),
        new(160, 90, 210, 255),
        new(45, 90, 165, 255),
        new(30, 130, 155, 255),
        new(35, 135, 75, 255),
        new(175, 120, 35, 255),
        new(160, 55, 55, 255),
        new(105, 65, 160, 255)
    };

    private readonly UnityEngine.Object[] targets;
    private readonly Action repaintInspector;
    private readonly AudioSystemSettings settings;
    private SerializedObject serializedTargets;
    private GUIStyle categoryButtonStyle;
    private GUIStyle categoryLabelStyle;
    private GUIStyle presetButtonStyle;

    public AudioIconColorPopup(
        UnityEngine.Object[] targets,
        Action repaintInspector)
    {
        this.targets = targets
            .Where(target => target != null)
            .ToArray();
        this.repaintInspector = repaintInspector;
        settings = AudioSystemSettingsProvider.FindSettings();
    }

    public override Vector2 GetWindowSize()
    {
        var categoryCount = 1 + (settings?.Categories.Count ?? 0);
        var categoryRowCount = Mathf.Max(
            1,
            Mathf.CeilToInt(categoryCount / (float)CategoryColumnCount));
        var categoryGridHeight =
            (categoryRowCount * CategoryCellHeight) +
            ((categoryRowCount - 1) * CellSpacing);
        var colorRowCount = Mathf.CeilToInt(
            PresetColors.Length / (float)ColorColumnCount);
        var colorGridHeight =
            (colorRowCount * ColorCellSize) +
            ((colorRowCount - 1) * CellSpacing);
        var height =
            Padding + LabelHeight + categoryGridHeight +
            Padding + DividerHeight + Padding +
            LabelHeight + colorGridHeight +
            Padding + CustomFieldHeight + Padding;

        return new Vector2(PopupWidth, height);
    }

    public override void OnOpen()
    {
        if (targets.Length > 0)
            serializedTargets = new SerializedObject(targets);
    }

    public override void OnGUI(Rect rect)
    {
        if (serializedTargets == null)
            return;

        EnsureStyles();

        serializedTargets.UpdateIfRequiredOrScript();
        var mixerGroupProperty = serializedTargets.FindProperty(
            MixerGroupPropertyName);
        var colorProperty = serializedTargets.FindProperty(
            IconColorPropertyName);
        if (mixerGroupProperty == null || colorProperty == null)
            return;

        var contentWidth = rect.width - (Padding * 2f);
        var currentY = Padding;

        EditorGUI.LabelField(
            new Rect(Padding, currentY, contentWidth, LabelHeight),
            new GUIContent(
                "Category & Output",
                "Selects the category and changes the AudioMixerGroup used " +
                "when this Audio is played."),
            EditorStyles.boldLabel);
        currentY += LabelHeight;

        var categoryGridHeight = DrawCategoryOptions(
            mixerGroupProperty,
            rect.width,
            currentY);
        currentY += categoryGridHeight + Padding;

        EditorGUI.DrawRect(
            new Rect(Padding, currentY, contentWidth, DividerHeight),
            EditorGUIUtility.isProSkin
                ? new Color(0f, 0f, 0f, 0.45f)
                : new Color(0f, 0f, 0f, 0.2f));
        currentY += DividerHeight + Padding;

        EditorGUI.LabelField(
            new Rect(Padding, currentY, contentWidth, LabelHeight),
            new GUIContent(
                "Icon Color",
                "Changes only the Editor appearance and does not affect " +
                "audio routing."),
            EditorStyles.boldLabel);
        currentY += LabelHeight;

        var colorGridHeight = DrawPresets(
            colorProperty,
            rect.width,
            currentY);
        currentY += colorGridHeight + Padding;

        DrawCustomColorField(
            new Rect(
                Padding,
                currentY,
                contentWidth,
                CustomFieldHeight),
            colorProperty);
    }

    private float DrawCategoryOptions(
        SerializedProperty mixerGroupProperty,
        float availableWidth,
        float gridTop)
    {
        var categoryCount = 1 + (settings?.Categories.Count ?? 0);
        var rowCount = Mathf.Max(
            1,
            Mathf.CeilToInt(categoryCount / (float)CategoryColumnCount));
        var gridWidth =
            (CategoryColumnCount * CategoryCellWidth) +
            ((CategoryColumnCount - 1) * CellSpacing);
        var gridLeft = Mathf.Round((availableWidth - gridWidth) * 0.5f);

        DrawCategoryOption(
            mixerGroupProperty,
            0,
            gridLeft,
            gridTop,
            "Main / Root",
            null,
            settings != null ? settings.DefaultIcon : null,
            true);

        if (settings != null)
        {
            for (var index = 0; index < settings.Categories.Count; index++)
            {
                var category = settings.Categories[index];
                var categoryName = string.IsNullOrWhiteSpace(category.Name)
                    ? "Unnamed"
                    : category.Name;
                DrawCategoryOption(
                    mixerGroupProperty,
                    index + 1,
                    gridLeft,
                    gridTop,
                    categoryName,
                    category.MixerGroup,
                    category.Icon != null
                        ? category.Icon
                        : settings.DefaultIcon,
                    category.MixerGroup != null);
            }
        }

        return
            (rowCount * CategoryCellHeight) +
            ((rowCount - 1) * CellSpacing);
    }

    private void DrawCategoryOption(
        SerializedProperty mixerGroupProperty,
        int index,
        float gridLeft,
        float gridTop,
        string label,
        AudioMixerGroup mixerGroup,
        Texture2D icon,
        bool enabled)
    {
        var column = index % CategoryColumnCount;
        var row = index / CategoryColumnCount;
        var position = new Rect(
            gridLeft + (column * (CategoryCellWidth + CellSpacing)),
            gridTop + (row * (CategoryCellHeight + CellSpacing)),
            CategoryCellWidth,
            CategoryCellHeight);
        var selected = enabled &&
            !mixerGroupProperty.hasMultipleDifferentValues &&
            mixerGroupProperty.objectReferenceValue == mixerGroup;
        var tooltip = GetCategoryTooltip(label, mixerGroup, enabled);
        var previousEnabled = GUI.enabled;
        GUI.enabled = enabled;

        if (GUI.Button(
                position,
                new GUIContent(string.Empty, tooltip),
                categoryButtonStyle))
        {
            ApplyMixerGroup(mixerGroupProperty, mixerGroup);
        }

        if (Event.current.type == EventType.Repaint)
        {
            var iconPosition = new Rect(
                Mathf.Round(position.center.x - 14f),
                position.y + 4f,
                28f,
                28f);
            if (icon != null)
            {
                GUI.DrawTexture(
                    iconPosition,
                    icon,
                    ScaleMode.ScaleToFit,
                    true);
            }

            GUI.Label(
                new Rect(
                    position.x + 2f,
                    position.yMax - 18f,
                    position.width - 4f,
                    16f),
                label,
                categoryLabelStyle);
        }

        GUI.enabled = previousEnabled;

        if (selected)
            DrawSelectionBorder(position);
    }

    private static string GetCategoryTooltip(
        string label,
        AudioMixerGroup mixerGroup,
        bool enabled)
    {
        if (!enabled)
            return $"{label}\nNo AudioMixerGroup is assigned in Audio System settings.";

        if (mixerGroup == null)
            return "Main / Root\nRoutes playback to the root of the Main AudioMixer.";

        return $"{label}\nRoutes playback to " +
               $"{mixerGroup.audioMixer.name}/{mixerGroup.name}.";
    }

    private float DrawPresets(
        SerializedProperty colorProperty,
        float availableWidth,
        float gridTop)
    {
        var gridWidth =
            (ColorColumnCount * ColorCellSize) +
            ((ColorColumnCount - 1) * CellSpacing);
        var gridLeft = Mathf.Round(
            (availableWidth - gridWidth) * 0.5f);

        for (var index = 0; index < PresetColors.Length; index++)
        {
            var column = index % ColorColumnCount;
            var row = index / ColorColumnCount;
            var cellPosition = new Rect(
                gridLeft + (column * (ColorCellSize + CellSpacing)),
                gridTop + (row * (ColorCellSize + CellSpacing)),
                ColorCellSize,
                ColorCellSize);
            var preset = (Color)PresetColors[index];
            var selected =
                !colorProperty.hasMultipleDifferentValues &&
                ColorsMatch(colorProperty.colorValue, preset);

            if (GUI.Button(
                    cellPosition,
                    new GUIContent(
                        string.Empty,
                        $"#{ColorUtility.ToHtmlStringRGB(preset)}"),
                    presetButtonStyle))
            {
                ApplyColor(colorProperty, preset);
            }

            DrawPresetColor(cellPosition, preset);

            if (selected)
                DrawSelectionBorder(cellPosition);
        }

        var rowCount = Mathf.CeilToInt(
            PresetColors.Length / (float)ColorColumnCount);
        return
            (rowCount * ColorCellSize) +
            ((rowCount - 1) * CellSpacing);
    }

    private void EnsureStyles()
    {
        if (presetButtonStyle != null)
            return;

        categoryButtonStyle = new GUIStyle(EditorStyles.miniButton)
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
        categoryLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip
        };
        presetButtonStyle = new GUIStyle(EditorStyles.miniButton)
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

    private static void DrawPresetColor(Rect position, Color color)
    {
        const float swatchSize = 14f;
        var borderPosition = new Rect(
            Mathf.Round(position.center.x - (swatchSize * 0.5f)),
            Mathf.Round(position.center.y - (swatchSize * 0.5f)),
            swatchSize,
            swatchSize);
        var borderColor = EditorGUIUtility.isProSkin
            ? new Color(0f, 0f, 0f, 0.55f)
            : new Color(0f, 0f, 0f, 0.35f);
        EditorGUI.DrawRect(borderPosition, borderColor);
        EditorGUI.DrawRect(
            new Rect(
                borderPosition.x + 1f,
                borderPosition.y + 1f,
                borderPosition.width - 2f,
                borderPosition.height - 2f),
            color);
    }

    private void DrawCustomColorField(
        Rect position,
        SerializedProperty colorProperty)
    {
        const float labelWidth = 88f;
        const float spacing = 4f;
        var labelPosition = new Rect(
            position.x,
            position.y,
            labelWidth,
            position.height);
        var fieldPosition = new Rect(
            labelPosition.xMax + spacing,
            position.y,
            position.width - labelWidth - spacing,
            position.height);

        EditorGUI.LabelField(
            labelPosition,
            new GUIContent(
                "Custom Color",
                "Choose a custom tint using Unity's Color Picker."));

        var previousMixedValue = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue =
            colorProperty.hasMultipleDifferentValues;

        EditorGUI.BeginChangeCheck();
        var customColor = EditorGUI.ColorField(
            fieldPosition,
            GUIContent.none,
            colorProperty.colorValue,
            true,
            false,
            false);
        if (EditorGUI.EndChangeCheck())
            ApplyColor(colorProperty, customColor);

        EditorGUI.showMixedValue = previousMixedValue;
    }

    private void ApplyMixerGroup(
        SerializedProperty mixerGroupProperty,
        AudioMixerGroup mixerGroup)
    {
        mixerGroupProperty.objectReferenceValue = mixerGroup;
        ApplyChanges();
    }

    private void ApplyColor(
        SerializedProperty colorProperty,
        Color color)
    {
        color.a = 1f;
        colorProperty.colorValue = color;
        ApplyChanges();
    }

    private void ApplyChanges()
    {
        serializedTargets.ApplyModifiedProperties();
        EditorApplication.RepaintProjectWindow();
        repaintInspector?.Invoke();
        editorWindow.Repaint();
    }

    private static bool ColorsMatch(Color left, Color right)
    {
        var left32 = (Color32)left;
        var right32 = (Color32)right;
        return left32.r == right32.r &&
               left32.g == right32.g &&
               left32.b == right32.b;
    }

    private static void DrawSelectionBorder(Rect position)
    {
        const float thickness = 2f;
        var borderColor = EditorGUIUtility.isProSkin
            ? new Color(0.25f, 0.65f, 1f)
            : new Color(0.1f, 0.35f, 0.7f);

        EditorGUI.DrawRect(
            new Rect(position.x, position.y, position.width, thickness),
            borderColor);
        EditorGUI.DrawRect(
            new Rect(
                position.x,
                position.yMax - thickness,
                position.width,
                thickness),
            borderColor);
        EditorGUI.DrawRect(
            new Rect(position.x, position.y, thickness, position.height),
            borderColor);
        EditorGUI.DrawRect(
            new Rect(
                position.xMax - thickness,
                position.y,
                thickness,
                position.height),
            borderColor);
    }
}
}
