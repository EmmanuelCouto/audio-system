using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Emmanuel.AudioSystem;

namespace Emmanuel.AudioSystem.Editor
{
internal static class AudioMixerGroupDropdownGUI
{
    internal static void Draw(
        SerializedProperty groupProperty,
        GUIContent label,
        AudioMixer mainMixer,
        string emptyOption,
        ISet<AudioMixerGroup> excludedGroups = null)
    {
        var currentGroup =
            groupProperty.objectReferenceValue as AudioMixerGroup;
        var availableGroups = new List<AudioMixerGroup> { null };

        if (currentGroup != null && currentGroup.audioMixer != mainMixer)
            availableGroups.Add(currentGroup);

        if (mainMixer != null)
        {
            foreach (var group in mainMixer.FindMatchingGroups(string.Empty))
            {
                if (group == currentGroup ||
                    excludedGroups == null ||
                    !excludedGroups.Contains(group))
                {
                    availableGroups.Add(group);
                }
            }
        }

        var optionNames = availableGroups
            .Select(group => GetOptionName(
                group,
                mainMixer,
                emptyOption))
            .ToArray();
        var currentIndex = Mathf.Max(
            0,
            availableGroups.IndexOf(currentGroup));

        var previousMixedValue = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = groupProperty.hasMultipleDifferentValues;

        EditorGUI.BeginChangeCheck();
        var selectedIndex = EditorGUILayout.Popup(
            label,
            currentIndex,
            optionNames);
        if (EditorGUI.EndChangeCheck())
            groupProperty.objectReferenceValue = availableGroups[selectedIndex];

        EditorGUI.showMixedValue = previousMixedValue;
    }

    private static string GetOptionName(
        AudioMixerGroup group,
        AudioMixer mainMixer,
        string emptyOption)
    {
        if (group == null)
            return emptyOption;

        return group.audioMixer == mainMixer
            ? group.name
            : $"Invalid: {group.name}";
    }
}
}
