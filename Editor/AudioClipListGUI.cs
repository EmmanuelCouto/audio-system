using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Emmanuel.AudioSystem;

namespace Emmanuel.AudioSystem.Editor
{
internal sealed class AudioClipListGUI
{
    private const float ElementVerticalPadding = 2f;
    private const float PlayButtonWidth = 24f;
    private const float ToggleButtonWidth = 22f;
    private const float ElementSpacing = 4f;
    private const float WaveformHeight = 42f;
    private const string CollapseAllPreferenceKey =
        "AudioSystem.AudioClipList.CollapseAll";

    private static readonly Color WaveformBackground =
        new Color32(82, 82, 82, 255);

    private readonly Action repaint;
    private readonly List<Rect> elementFieldRects = new();
    private readonly List<bool> collapsedStates = new();

    private ReorderableList list;
    private SerializedObject boundSerializedObject;
    private Audio audio;
    private Rect listRect;
    private GUIStyle playButtonStyle;
    private bool preferredCollapsedState;
    private bool rebuildListRequested;
    private int? listIndexToRestore;

    internal AudioClipListGUI(Action repaint)
    {
        this.repaint = repaint;
        preferredCollapsedState = EditorPrefs.GetBool(
            CollapseAllPreferenceKey,
            true);
    }

    internal void Draw(SerializedProperty clipsProperty, Audio currentAudio)
    {
        if (rebuildListRequested)
        {
            listIndexToRestore = list?.index;
            list = null;
            boundSerializedObject = null;
            rebuildListRequested = false;
        }

        EnsureList(clipsProperty, currentAudio);
        if (listIndexToRestore.HasValue)
        {
            list.index = Mathf.Clamp(
                listIndexToRestore.Value,
                -1,
                clipsProperty.arraySize - 1);
            listIndexToRestore = null;
        }
        EnsureStyles();
        EnsureStateCount(clipsProperty.arraySize);

        elementFieldRects.Clear();
        list.DoLayoutList();
        listRect = GUILayoutUtility.GetLastRect();
        HandleExternalDrop(clipsProperty);
    }

    private void EnsureList(
        SerializedProperty clipsProperty,
        Audio currentAudio)
    {
        if (list != null &&
            boundSerializedObject == clipsProperty.serializedObject)
        {
            audio = currentAudio;
            return;
        }

        boundSerializedObject = clipsProperty.serializedObject;
        audio = currentAudio;
        list = new ReorderableList(
            clipsProperty.serializedObject,
            clipsProperty,
            true,
            true,
            true,
            true)
        {
            drawHeaderCallback = DrawHeader,
            drawElementCallback = DrawElement,
            elementHeightCallback = GetElementHeight,
            onAddCallback = AddElement,
            onRemoveCallback = RemoveElement,
            onReorderCallbackWithDetails = ReorderElement,
            onCanRemoveCallback = currentList => currentList.count > 0,
            drawNoneElementCallback = DrawEmptyList
        };
    }

    private void DrawHeader(Rect position)
    {
        var togglePosition = new Rect(
            position.xMax - ToggleButtonWidth,
            position.y,
            ToggleButtonWidth,
            position.height);
        var playPosition = new Rect(
            togglePosition.x - ElementSpacing - PlayButtonWidth,
            position.y,
            PlayButtonWidth,
            position.height);
        var labelPosition = new Rect(
            position.x,
            position.y,
            playPosition.x - position.x - ElementSpacing,
            position.height);
        var expandAll = AreAllElementsCollapsed();
        var isPreviewingAudio = AudioEditorPreviewPlayer.IsPreviewing(audio);

        EditorGUI.LabelField(labelPosition, "Audio Clips");
        using (new EditorGUI.DisabledScope(!HasAnyClip()))
        {
            if (GUI.Button(
                    playPosition,
                    new GUIContent(
                        isPreviewingAudio ? "■" : "▶",
                        isPreviewingAudio
                            ? "Stop Audio preview"
                            : "Preview Audio using its Loop Type"),
                    playButtonStyle))
            {
                if (isPreviewingAudio)
                    AudioEditorPreviewPlayer.Stop();
                else
                    AudioEditorPreviewPlayer.Play(audio);
            }
        }

        using (new EditorGUI.DisabledScope(list.count == 0))
        {
            if (GUI.Button(
                    togglePosition,
                    new GUIContent(
                        expandAll ? "▼" : "▲",
                        expandAll ? "Expand all clips" : "Collapse all clips"),
                    EditorStyles.miniButton))
            {
                SetAllElementsCollapsed(!expandAll);
            }
        }
    }

    private bool HasAnyClip()
    {
        var clipsProperty = list.serializedProperty;
        for (var index = 0; index < clipsProperty.arraySize; index++)
        {
            if (clipsProperty
                    .GetArrayElementAtIndex(index)
                    .objectReferenceValue != null)
            {
                return true;
            }
        }

        return false;
    }

    private float GetElementHeight(int index)
    {
        var baseHeight = EditorGUIUtility.singleLineHeight +
                         ElementVerticalPadding * 2f;
        if (index < 0 || index >= collapsedStates.Count ||
            collapsedStates[index])
        {
            return baseHeight;
        }

        return baseHeight + ElementSpacing + WaveformHeight;
    }

    private void DrawElement(
        Rect position,
        int index,
        bool isActive,
        bool isFocused)
    {
        if (index < 0 || index >= list.serializedProperty.arraySize)
            return;

        position.y += ElementVerticalPadding;
        position.height = EditorGUIUtility.singleLineHeight;

        var element = list.serializedProperty.GetArrayElementAtIndex(index);
        var clip = element.objectReferenceValue as AudioClip;
        var isCollapsed = collapsedStates[index];
        var playPosition = new Rect(
            position.x,
            position.y,
            PlayButtonWidth,
            position.height);
        var togglePosition = new Rect(
            position.xMax - ToggleButtonWidth,
            position.y,
            ToggleButtonWidth,
            position.height);
        var fieldPosition = new Rect(
            playPosition.xMax + ElementSpacing,
            position.y,
            togglePosition.x - playPosition.xMax - ElementSpacing * 2f,
            position.height);

        DrawPlayButton(playPosition, clip);
        EditorGUI.PropertyField(fieldPosition, element, GUIContent.none);
        if (GUI.Button(
                togglePosition,
                new GUIContent(
                    isCollapsed ? "▼" : "▲",
                    isCollapsed ? "Expand clip" : "Collapse clip"),
                EditorStyles.miniButton))
        {
            collapsedStates[index] = !isCollapsed;
            repaint?.Invoke();
        }

        elementFieldRects.Add(fieldPosition);

        if (!isCollapsed)
        {
            var waveformPosition = new Rect(
                fieldPosition.x,
                position.yMax + ElementSpacing,
                togglePosition.xMax - fieldPosition.x,
                WaveformHeight);
            DrawWaveform(waveformPosition, clip);
        }
    }

    private void DrawPlayButton(Rect position, AudioClip clip)
    {
        var isPlaying = AudioEditorPreviewPlayer.IsPlayingClip(clip);
        var content = new GUIContent(
            isPlaying ? "■" : "▶",
            isPlaying ? "Stop preview" : "Play preview");

        using (new EditorGUI.DisabledScope(clip == null))
        {
            if (!GUI.Button(position, content, playButtonStyle))
                return;

            if (isPlaying)
                AudioEditorPreviewPlayer.Stop();
            else
                AudioEditorPreviewPlayer.PlayClip(audio, clip);
        }
    }

    private void DrawWaveform(Rect position, AudioClip clip)
    {
        EditorGUI.DrawRect(position, WaveformBackground);

        if (clip == null)
        {
            GUI.Label(
                position,
                "Drop an AudioClip here",
                EditorStyles.centeredGreyMiniLabel);
            return;
        }

        var waveform = AudioWaveformCache.Get(clip, out var unavailable);
        if (waveform != null)
        {
            GUI.DrawTexture(
                position,
                waveform,
                ScaleMode.StretchToFill,
                true);
        }
        else
        {
            GUI.Label(
                position,
                unavailable
                    ? "Waveform unavailable"
                    : "Generating waveform...",
                EditorStyles.centeredGreyMiniLabel);

            if (!unavailable)
                repaint?.Invoke();
        }

        HandleWaveformInput(position, clip);

        if (!AudioEditorPreviewPlayer.IsPlayingClip(clip))
            return;

        var progress = AudioEditorPreviewPlayer.NormalizedTime;
        var progressPosition = new Rect(
            position.x + position.width * progress,
            position.y,
            1f,
            position.height);
        EditorGUI.DrawRect(
            progressPosition,
            new Color(1f, 1f, 1f, 0.9f));
    }

    private void HandleWaveformInput(Rect position, AudioClip clip)
    {
        EditorGUIUtility.AddCursorRect(position, MouseCursor.Link);
        var controlId = GUIUtility.GetControlID(
            FocusType.Passive,
            position);
        var currentEvent = Event.current;

        switch (currentEvent.GetTypeForControl(controlId))
        {
            case EventType.MouseDown:
                if (currentEvent.button != 0 ||
                    !position.Contains(currentEvent.mousePosition))
                {
                    return;
                }

                GUIUtility.hotControl = controlId;
                SeekFromMouse(position, clip, currentEvent.mousePosition.x);
                currentEvent.Use();
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl != controlId)
                    return;

                SeekFromMouse(position, clip, currentEvent.mousePosition.x);
                currentEvent.Use();
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl != controlId ||
                    currentEvent.button != 0)
                {
                    return;
                }

                SeekFromMouse(position, clip, currentEvent.mousePosition.x);
                GUIUtility.hotControl = 0;
                currentEvent.Use();
                break;
        }
    }

    private void SeekFromMouse(
        Rect position,
        AudioClip clip,
        float mousePositionX)
    {
        var normalizedTime = Mathf.InverseLerp(
            position.x,
            position.xMax,
            mousePositionX);
        AudioEditorPreviewPlayer.SeekClip(audio, clip, normalizedTime);
        repaint?.Invoke();
    }

    private static void DrawEmptyList(Rect position)
    {
        EditorGUI.LabelField(
            position,
            "List is Empty",
            EditorStyles.centeredGreyMiniLabel);
    }

    private void AddElement(ReorderableList currentList)
    {
        var clipsProperty = currentList.serializedProperty;
        var index = clipsProperty.arraySize;
        clipsProperty.InsertArrayElementAtIndex(index);
        clipsProperty.GetArrayElementAtIndex(index).objectReferenceValue = null;
        collapsedStates.Add(preferredCollapsedState);
        currentList.index = index;
    }

    private void RemoveElement(ReorderableList currentList)
    {
        var clipsProperty = currentList.serializedProperty;
        var index = currentList.index;
        if (index < 0 || index >= clipsProperty.arraySize)
            return;

        var clip = clipsProperty
            .GetArrayElementAtIndex(index)
            .objectReferenceValue as AudioClip;
        if (AudioEditorPreviewPlayer.IsPlayingClip(clip))
            AudioEditorPreviewPlayer.Stop();

        DeleteElement(clipsProperty, index);
        if (index < collapsedStates.Count)
            collapsedStates.RemoveAt(index);
        currentList.index = Mathf.Clamp(
            index - 1,
            -1,
            clipsProperty.arraySize - 1);
    }

    private void HandleExternalDrop(SerializedProperty clipsProperty)
    {
        var currentEvent = Event.current;
        if (!listRect.Contains(currentEvent.mousePosition) ||
            IsOverElementField(currentEvent.mousePosition) ||
            (currentEvent.type != EventType.DragUpdated &&
             currentEvent.type != EventType.DragPerform))
        {
            return;
        }

        var droppedClips = DragAndDrop.objectReferences
            .OfType<AudioClip>()
            .ToArray();
        if (droppedClips.Length == 0)
            return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (currentEvent.type == EventType.DragUpdated)
        {
            currentEvent.Use();
            return;
        }

        DragAndDrop.AcceptDrag();
        foreach (var clip in droppedClips)
        {
            var index = clipsProperty.arraySize;
            clipsProperty.InsertArrayElementAtIndex(index);
            clipsProperty.GetArrayElementAtIndex(index).objectReferenceValue = clip;
            collapsedStates.Add(preferredCollapsedState);
        }

        list.index = clipsProperty.arraySize - 1;
        currentEvent.Use();
    }

    private void ReorderElement(
        ReorderableList currentList,
        int oldIndex,
        int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= collapsedStates.Count ||
            newIndex < 0 || newIndex >= collapsedStates.Count ||
            oldIndex == newIndex)
        {
            return;
        }

        var state = collapsedStates[oldIndex];
        collapsedStates.RemoveAt(oldIndex);
        collapsedStates.Insert(newIndex, state);
    }

    private void EnsureStateCount(int count)
    {
        while (collapsedStates.Count < count)
            collapsedStates.Add(preferredCollapsedState);
        while (collapsedStates.Count > count)
            collapsedStates.RemoveAt(collapsedStates.Count - 1);
    }

    private bool AreAllElementsCollapsed()
    {
        if (collapsedStates.Count == 0)
            return false;

        foreach (var collapsed in collapsedStates)
        {
            if (!collapsed)
                return false;
        }

        return true;
    }

    private void SetAllElementsCollapsed(bool collapsed)
    {
        for (var index = 0; index < collapsedStates.Count; index++)
            collapsedStates[index] = collapsed;

        preferredCollapsedState = collapsed;
        EditorPrefs.SetBool(CollapseAllPreferenceKey, collapsed);
        rebuildListRequested = true;
        repaint?.Invoke();
    }

    private bool IsOverElementField(Vector2 mousePosition)
    {
        foreach (var position in elementFieldRects)
        {
            if (position.Contains(mousePosition))
                return true;
        }

        return false;
    }

    private static void DeleteElement(
        SerializedProperty clipsProperty,
        int index)
    {
        var previousSize = clipsProperty.arraySize;
        clipsProperty.DeleteArrayElementAtIndex(index);
        if (clipsProperty.arraySize == previousSize)
            clipsProperty.DeleteArrayElementAtIndex(index);
    }

    private void EnsureStyles()
    {
        playButtonStyle ??= new GUIStyle(EditorStyles.miniButton)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(),
            fixedWidth = 0f,
            fixedHeight = 0f
        };
    }
}
}
