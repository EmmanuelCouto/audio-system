using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Emmanuel.AudioSystem;

namespace Emmanuel.AudioSystem.Editor
{
[InitializeOnLoad]
internal static class AudioSystemSettingsProvider
{
    private const string DefaultSettingsGuid =
        "2ee1aa5ae6a74511ad9fda7afafef466";
    private const string ProjectSettingsFolder =
        "Assets/Settings";
    private const string ProjectSettingsPath =
        ProjectSettingsFolder + "/AudioSystemSettings.asset";
    private const string Description =
        "Configure the AudioMixer used by the Audio System. Assign the default " +
        "mixer or replace it with a project-specific one.";
    private const string CategoriesDescription =
        "Bind category names and editor icons to groups from the Main Audio Mixer.";
    private const string PlaybackDescription =
        "Configure the reusable AudioSources used for simultaneous and positional playback.";

    private static AudioSystemSettings settings;
    private static SerializedObject serializedSettings;

    static AudioSystemSettingsProvider()
    {
        EditorApplication.delayCall += EnsureSettingsIsPreloaded;
    }

    [SettingsProvider]
    private static SettingsProvider CreateProvider()
    {
        return new SettingsProvider("Project/Audio System", SettingsScope.Project)
        {
            label = "Audio System",
            activateHandler = (_, _) => LoadSettings(),
            titleBarGuiHandler = DrawTitleBarActions,
            guiHandler = _ => DrawSettings(),
            keywords = new[]
            {
                "Audio", "Mixer", "Music", "SFX", "Pool", "Voices"
            }
        };
    }

    private static void DrawSettings()
    {
        if (serializedSettings == null || serializedSettings.targetObject == null)
            LoadSettings();

        if (serializedSettings == null)
        {
            EditorGUILayout.HelpBox(
                "The Audio System settings asset could not be found.",
                MessageType.Error);
            return;
        }

        serializedSettings.Update();
        var mainMixerProperty = serializedSettings.FindProperty("mainMixer");

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Set Main Audio Mixer", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(Description, EditorStyles.wordWrappedLabel);
        EditorGUILayout.PropertyField(
            mainMixerProperty,
            new GUIContent(
                "Main Audio Mixer",
                "AudioMixer used by the Audio System at runtime."));

        DrawPlaybackSettings();
        DrawCategories(mainMixerProperty);

        if (serializedSettings.ApplyModifiedProperties())
            EditorUtility.SetDirty(serializedSettings.targetObject);
    }

    private static void DrawPlaybackSettings()
    {
        var initialVoiceCount =
            serializedSettings.FindProperty("initialVoiceCount");
        var maxVoiceCount =
            serializedSettings.FindProperty("maxVoiceCount");

        EditorGUILayout.Space(14);
        EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            PlaybackDescription,
            EditorStyles.wordWrappedLabel);
        EditorGUILayout.PropertyField(
            initialVoiceCount,
            new GUIContent(
                "Initial Voices",
                "AudioSources created when the Audio System initializes."));
        EditorGUILayout.PropertyField(
            maxVoiceCount,
            new GUIContent(
                "Max Voices",
                "Maximum simultaneous pooled voices. Music uses a separate source."));

        if (maxVoiceCount.intValue < initialVoiceCount.intValue)
            maxVoiceCount.intValue = initialVoiceCount.intValue;
    }

    private static void DrawCategories(SerializedProperty mainMixerProperty)
    {
        var defaultIconProperty = serializedSettings.FindProperty("defaultIcon");
        var categoriesProperty = serializedSettings.FindProperty("categories");
        var mainMixer = mainMixerProperty.objectReferenceValue as AudioMixer;

        EditorGUILayout.Space(14);
        EditorGUILayout.LabelField("Audio Categories", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            CategoriesDescription,
            EditorStyles.wordWrappedLabel);
        EditorGUILayout.PropertyField(
            defaultIconProperty,
            new GUIContent(
                "Default Icon",
                "Fallback used when an Audio has no group or its group has no configured icon."));

        EditorGUILayout.Space(4);
        var removeIndex = -1;
        for (var i = 0; i < categoriesProperty.arraySize; i++)
        {
            var categoryProperty = categoriesProperty.GetArrayElementAtIndex(i);
            if (DrawCategory(categoryProperty, categoriesProperty, i, mainMixer))
                removeIndex = i;
        }

        if (removeIndex >= 0)
            categoriesProperty.DeleteArrayElementAtIndex(removeIndex);

        if (GUILayout.Button("Add Category", GUILayout.Width(110)))
            AddCategory(categoriesProperty);
    }

    private static bool DrawCategory(
        SerializedProperty categoryProperty,
        SerializedProperty categoriesProperty,
        int categoryIndex,
        AudioMixer mainMixer)
    {
        var nameProperty = categoryProperty.FindPropertyRelative("name");
        var groupProperty = categoryProperty.FindPropertyRelative("mixerGroup");
        var iconProperty = categoryProperty.FindPropertyRelative("icon");

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(
                    nameProperty,
                    new GUIContent("Name"));

                if (GUILayout.Button("−", GUILayout.Width(22)))
                    return true;
            }

            DrawMixerGroupDropdown(
                groupProperty,
                categoriesProperty,
                categoryIndex,
                mainMixer);
            EditorGUILayout.PropertyField(
                iconProperty,
                new GUIContent("Icon"));

            DrawCategoryValidation(
                groupProperty.objectReferenceValue as AudioMixerGroup,
                categoriesProperty,
                categoryIndex,
                mainMixer);
        }

        return false;
    }

    private static void DrawMixerGroupDropdown(
        SerializedProperty groupProperty,
        SerializedProperty categoriesProperty,
        int categoryIndex,
        AudioMixer mainMixer)
    {
        var groupsUsedElsewhere = GetGroupsUsedElsewhere(
            categoriesProperty,
            categoryIndex);

        AudioMixerGroupDropdownGUI.Draw(
            groupProperty,
            new GUIContent("Mixer Group"),
            mainMixer,
            "None",
            groupsUsedElsewhere);
    }

    private static HashSet<AudioMixerGroup> GetGroupsUsedElsewhere(
        SerializedProperty categoriesProperty,
        int categoryIndex)
    {
        var result = new HashSet<AudioMixerGroup>();
        for (var i = 0; i < categoriesProperty.arraySize; i++)
        {
            if (i == categoryIndex) continue;

            var group = categoriesProperty
                .GetArrayElementAtIndex(i)
                .FindPropertyRelative("mixerGroup")
                .objectReferenceValue as AudioMixerGroup;
            if (group != null)
                result.Add(group);
        }

        return result;
    }

    private static void DrawCategoryValidation(
        AudioMixerGroup group,
        SerializedProperty categoriesProperty,
        int categoryIndex,
        AudioMixer mainMixer)
    {
        if (group != null && group.audioMixer != mainMixer)
        {
            EditorGUILayout.HelpBox(
                "This group does not belong to the configured Main Audio Mixer.",
                MessageType.Warning);
        }

        if (group != null &&
            GetGroupsUsedElsewhere(categoriesProperty, categoryIndex).Contains(group))
        {
            EditorGUILayout.HelpBox(
                "This AudioMixerGroup is assigned to more than one category.",
                MessageType.Error);
        }
    }

    private static void AddCategory(SerializedProperty categoriesProperty)
    {
        var newIndex = categoriesProperty.arraySize;
        categoriesProperty.arraySize++;

        var categoryProperty = categoriesProperty.GetArrayElementAtIndex(newIndex);
        categoryProperty.FindPropertyRelative("name").stringValue = "New Category";
        categoryProperty.FindPropertyRelative("mixerGroup").objectReferenceValue = null;
        categoryProperty.FindPropertyRelative("icon").objectReferenceValue = null;
    }

    private static void DrawTitleBarActions()
    {
        if (serializedSettings == null || serializedSettings.targetObject == null)
            LoadSettings();

        if (serializedSettings == null)
            return;

        var menuContent = EditorGUIUtility.IconContent("_Menu");
        menuContent.tooltip = "Audio System actions";
        var buttonRect = GUILayoutUtility.GetRect(
            19,
            19,
            GUILayout.Width(19),
            GUILayout.Height(19));
        buttonRect.y += 6;

        if (GUI.Button(buttonRect, menuContent, EditorStyles.iconButton))
        {
            ShowActionsMenu();
        }
    }

    private static void ShowActionsMenu()
    {
        serializedSettings.Update();
        var mainMixerProperty = serializedSettings.FindProperty("mainMixer");
        var defaultMixerProperty = serializedSettings.FindProperty("defaultMixer");
        var currentMixer = mainMixerProperty.objectReferenceValue;
        var defaultMixer = defaultMixerProperty.objectReferenceValue;
        var menu = new GenericMenu();

        if (currentMixer != null)
            menu.AddItem(
                new GUIContent("Ping Current Mixer"),
                false,
                () => EditorGUIUtility.PingObject(currentMixer));
        else
            menu.AddDisabledItem(new GUIContent("Ping Current Mixer"));

        menu.AddSeparator(string.Empty);

        if (defaultMixer != null && currentMixer != defaultMixer)
            menu.AddItem(
                new GUIContent("Restore Default Mixer"),
                false,
                RestoreDefaultMixer);
        else
            menu.AddDisabledItem(new GUIContent("Restore Default Mixer"));

        menu.ShowAsContext();
    }

    private static void RestoreDefaultMixer()
    {
        serializedSettings.Update();
        var mainMixerProperty = serializedSettings.FindProperty("mainMixer");
        var defaultMixer = serializedSettings
            .FindProperty("defaultMixer")
            .objectReferenceValue;

        if (defaultMixer == null)
            return;

        Undo.SetCurrentGroupName("Restore Default Audio Mixer");
        mainMixerProperty.objectReferenceValue = defaultMixer;
        serializedSettings.ApplyModifiedProperties();
        EditorUtility.SetDirty(settings);
    }

    private static void LoadSettings()
    {
        settings = FindSettings();
        serializedSettings = settings != null ? new SerializedObject(settings) : null;

        if (settings != null)
            AddToPreloadedAssets(settings);
    }

    private static void EnsureSettingsIsPreloaded()
    {
        var audioSettings = FindSettings();
        if (audioSettings != null)
            AddToPreloadedAssets(audioSettings);
    }

    internal static AudioSystemSettings FindSettings()
    {
        var projectSettings = FindProjectSettings();
        return projectSettings != null
            ? projectSettings
            : CreateProjectSettings();
    }

    private static AudioSystemSettings FindProjectSettings()
    {
        var settingsAtDefaultLocation =
            AssetDatabase.LoadAssetAtPath<AudioSystemSettings>(
                ProjectSettingsPath);
        if (settingsAtDefaultLocation != null)
            return PrepareProjectSettings(settingsAtDefaultLocation);

        foreach (var guid in AssetDatabase.FindAssets("t:AudioSystemSettings"))
        {
            if (guid == DefaultSettingsGuid)
                continue;

            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/"))
                continue;

            var projectSettings =
                AssetDatabase.LoadAssetAtPath<AudioSystemSettings>(path);
            if (projectSettings != null)
                return PrepareProjectSettings(projectSettings);
        }

        return null;
    }

    private static AudioSystemSettings CreateProjectSettings()
    {
        var defaultSettingsPath =
            AssetDatabase.GUIDToAssetPath(DefaultSettingsGuid);
        var defaultSettings =
            AssetDatabase.LoadAssetAtPath<AudioSystemSettings>(
                defaultSettingsPath);
        if (defaultSettings == null)
            return null;

        if (!AssetDatabase.IsValidFolder(ProjectSettingsFolder))
            AssetDatabase.CreateFolder("Assets", "Settings");

        if (!AssetDatabase.CopyAsset(
                defaultSettingsPath,
                ProjectSettingsPath))
        {
            return null;
        }

        AssetDatabase.ImportAsset(ProjectSettingsPath);
        var projectSettings =
            AssetDatabase.LoadAssetAtPath<AudioSystemSettings>(
                ProjectSettingsPath);
        return PrepareProjectSettings(projectSettings);
    }

    private static AudioSystemSettings PrepareProjectSettings(
        AudioSystemSettings projectSettings)
    {
        if (projectSettings == null)
            return null;

        var serializedProjectSettings = new SerializedObject(projectSettings);
        var packageDefaultProperty =
            serializedProjectSettings.FindProperty("isPackageDefault");
        if (packageDefaultProperty == null || !packageDefaultProperty.boolValue)
            return projectSettings;

        packageDefaultProperty.boolValue = false;
        serializedProjectSettings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(projectSettings);
        AssetDatabase.SaveAssetIfDirty(projectSettings);
        return projectSettings;
    }

    private static void AddToPreloadedAssets(AudioSystemSettings audioSettings)
    {
        var preloadedAssets = PlayerSettings.GetPreloadedAssets();
        var updatedAssets = preloadedAssets
            .Where(asset => asset is not AudioSystemSettings)
            .Append(audioSettings)
            .ToArray();

        if (preloadedAssets.SequenceEqual(updatedAssets))
            return;

        PlayerSettings.SetPreloadedAssets(updatedAssets);
    }
}
}
