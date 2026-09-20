using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Emmanuel.AudioSystem;

namespace Emmanuel.AudioSystem.Editor
{
internal sealed class AudioIconColorPopup : PopupWindowContent
{
    private const string IconColorPropertyName = "iconColor";
    private const int ColumnCount = 6;
    private const float Padding = 8f;
    private const float CellSize = 24f;
    private const float CellSpacing = 3f;
    private const float LabelHeight = 18f;
    private const float CustomFieldHeight = 20f;
    private const float PopupWidth = 210f;

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
    private SerializedObject serializedTargets;
    private GUIStyle presetButtonStyle;

    public AudioIconColorPopup(
        UnityEngine.Object[] targets,
        Action repaintInspector)
    {
        this.targets = targets
            .Where(target => target != null)
            .ToArray();
        this.repaintInspector = repaintInspector;
    }

    public override Vector2 GetWindowSize()
    {
        var gridWidth =
            (ColumnCount * CellSize) +
            ((ColumnCount - 1) * CellSpacing);
        var rowCount = Mathf.CeilToInt(
            PresetColors.Length / (float)ColumnCount);
        var gridHeight =
            (rowCount * CellSize) +
            ((rowCount - 1) * CellSpacing);

        return new Vector2(
            Mathf.Max(PopupWidth, gridWidth + (Padding * 2f)),
            Padding + LabelHeight + gridHeight + Padding +
            CustomFieldHeight + Padding);
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
        var colorProperty = serializedTargets.FindProperty(
            IconColorPropertyName);
        if (colorProperty == null)
            return;

        var contentWidth = rect.width - (Padding * 2f);
        var labelPosition = new Rect(
            Padding,
            Padding,
            contentWidth,
            LabelHeight);
        EditorGUI.LabelField(
            labelPosition,
            "Preset Colors",
            EditorStyles.miniLabel);

        var gridTop = labelPosition.yMax;
        DrawPresets(colorProperty, rect.width, gridTop);

        var rowCount = Mathf.CeilToInt(
            PresetColors.Length / (float)ColumnCount);
        var gridHeight =
            (rowCount * CellSize) +
            ((rowCount - 1) * CellSpacing);
        var customPosition = new Rect(
            Padding,
            gridTop + gridHeight + Padding,
            contentWidth,
            CustomFieldHeight);

        DrawCustomColorField(customPosition, colorProperty);
    }

    private void DrawPresets(
        SerializedProperty colorProperty,
        float availableWidth,
        float gridTop)
    {
        var gridWidth =
            (ColumnCount * CellSize) +
            ((ColumnCount - 1) * CellSpacing);
        var gridLeft = Mathf.Round(
            (availableWidth - gridWidth) * 0.5f);

        for (var index = 0; index < PresetColors.Length; index++)
        {
            var column = index % ColumnCount;
            var row = index / ColumnCount;
            var cellPosition = new Rect(
                gridLeft + (column * (CellSize + CellSpacing)),
                gridTop + (row * (CellSize + CellSpacing)),
                CellSize,
                CellSize);
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
                editorWindow.Close();
                GUIUtility.ExitGUI();
            }

            DrawPresetColor(cellPosition, preset);

            if (selected)
                DrawSelectionBorder(cellPosition);
        }
    }

    private void EnsureStyles()
    {
        if (presetButtonStyle != null)
            return;

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
            true,
            false);
        if (EditorGUI.EndChangeCheck())
            ApplyColor(colorProperty, customColor);

        EditorGUI.showMixedValue = previousMixedValue;
    }

    private void ApplyColor(
        SerializedProperty colorProperty,
        Color color)
    {
        colorProperty.colorValue = color;
        serializedTargets.ApplyModifiedProperties();
        EditorApplication.RepaintProjectWindow();
        repaintInspector?.Invoke();
        editorWindow.Repaint();
    }

    private static bool ColorsMatch(Color left, Color right)
    {
        return ((Color32)left).Equals((Color32)right);
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
