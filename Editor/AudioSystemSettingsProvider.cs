using System;
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
    private const string DefaultMixerGuid =
        "69be4fd54c05ab84bbe796bd58c9d74c";
    private const string ProjectRootSettingsFolder =
        "Assets/Settings";
    private const string ProjectSettingsFolder =
        ProjectRootSettingsFolder + "/AudioSystem";
    private const string LegacyProjectSettingsPath =
        ProjectRootSettingsFolder + "/AudioSystemSettings.asset";
    private const string ProjectSettingsPath =
        ProjectSettingsFolder + "/AudioSystemSettings.asset";
    private const string ProjectMixerPath =
        ProjectSettingsFolder + "/Main.mixer";
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
        EditorApplication.delayCall += InitializeProjectSettings;
    }

    private static void InitializeProjectSettings()
    {
        EnsureSettingsIsPreloaded();
        HidePackageDefaultMixer();
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
        var categoriesProperty = serializedSettings.FindProperty("categories");
        var previousMixer = mainMixerProperty.objectReferenceValue as AudioMixer;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Set Main Audio Mixer", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(Description, EditorStyles.wordWrappedLabel);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(
            mainMixerProperty,
            new GUIContent(
                "Main Audio Mixer",
                "AudioMixer used by the Audio System at runtime."));
        if (EditorGUI.EndChangeCheck())
        {
            var newMixer = mainMixerProperty.objectReferenceValue as AudioMixer;
            AutoAssignCategoryGroups(
                categoriesProperty,
                previousMixer,
                newMixer);
        }

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
        var categoriesProperty = serializedSettings.FindProperty("categories");
        var previousMixer =
            mainMixerProperty.objectReferenceValue as AudioMixer;
        var defaultMixer = serializedSettings
            .FindProperty("defaultMixer")
            .objectReferenceValue as AudioMixer;

        if (defaultMixer == null)
            return;

        Undo.SetCurrentGroupName("Restore Default Audio Mixer");
        mainMixerProperty.objectReferenceValue = defaultMixer;
        AutoAssignCategoryGroups(
            categoriesProperty,
            previousMixer,
            defaultMixer);
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

        var legacySettings =
            AssetDatabase.LoadAssetAtPath<AudioSystemSettings>(
                LegacyProjectSettingsPath);
        if (legacySettings != null)
        {
            EnsureProjectFolders();
            var moveError = AssetDatabase.MoveAsset(
                LegacyProjectSettingsPath,
                ProjectSettingsPath);
            if (string.IsNullOrEmpty(moveError))
            {
                return PrepareProjectSettings(
                    AssetDatabase.LoadAssetAtPath<AudioSystemSettings>(
                        ProjectSettingsPath));
            }

            Debug.LogWarning(
                $"Audio System could not move its settings asset to " +
                $"'{ProjectSettingsPath}': {moveError}");
            return PrepareProjectSettings(legacySettings);
        }

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

        EnsureProjectFolders();

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
        var mainMixerProperty =
            serializedProjectSettings.FindProperty("mainMixer");
        var defaultMixerProperty =
            serializedProjectSettings.FindProperty("defaultMixer");
        var categoriesProperty =
            serializedProjectSettings.FindProperty("categories");
        var wasPackageDefault =
            packageDefaultProperty != null && packageDefaultProperty.boolValue;
        var currentMixer =
            mainMixerProperty?.objectReferenceValue as AudioMixer;
        var currentDefaultMixer =
            defaultMixerProperty?.objectReferenceValue as AudioMixer;
        var packageMixer = IsPackageAsset(currentMixer)
            ? currentMixer
            : IsPackageAsset(currentDefaultMixer)
                ? currentDefaultMixer
                : null;
        var editableDefaultMixer = EnsureEditableDefaultMixer();
        var changed = false;

        if (packageDefaultProperty != null && packageDefaultProperty.boolValue)
        {
            packageDefaultProperty.boolValue = false;
            changed = true;
        }

        if (editableDefaultMixer != null)
        {
            if (wasPackageDefault || IsPackageAsset(currentMixer))
            {
                mainMixerProperty.objectReferenceValue = editableDefaultMixer;
                changed = true;
            }

            if (defaultMixerProperty != null &&
                (currentDefaultMixer == null ||
                 IsPackageAsset(currentDefaultMixer)))
            {
                defaultMixerProperty.objectReferenceValue = editableDefaultMixer;
                changed = true;
            }

            if (packageMixer != null)
            {
                changed |= RemapCategoryGroups(
                    categoriesProperty,
                    packageMixer,
                    editableDefaultMixer);
                RemapAudioAssets(packageMixer, editableDefaultMixer);
            }
        }

        if (changed)
        {
            serializedProjectSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(projectSettings);
            AssetDatabase.SaveAssetIfDirty(projectSettings);
        }

        return projectSettings;
    }

    private static AudioMixer EnsureEditableDefaultMixer()
    {
        var existingMixer =
            AssetDatabase.LoadAssetAtPath<AudioMixer>(ProjectMixerPath);
        if (existingMixer != null)
            return existingMixer;

        EnsureProjectFolders();

        if (AssetDatabase.LoadMainAssetAtPath(ProjectMixerPath) != null)
        {
            Debug.LogError(
                $"Audio System could not create its editable mixer because " +
                $"'{ProjectMixerPath}' is already used by another asset.");
            return null;
        }

        var templatePath = AssetDatabase.GUIDToAssetPath(DefaultMixerGuid);
        if (string.IsNullOrEmpty(templatePath) ||
            AssetDatabase.LoadAssetAtPath<AudioMixer>(templatePath) == null)
        {
            Debug.LogError("Audio System default mixer template was not found.");
            return null;
        }

        if (!AssetDatabase.CopyAsset(templatePath, ProjectMixerPath))
        {
            Debug.LogError(
                $"Audio System could not create '{ProjectMixerPath}'.");
            return null;
        }

        AssetDatabase.ImportAsset(ProjectMixerPath);
        return AssetDatabase.LoadAssetAtPath<AudioMixer>(ProjectMixerPath);
    }

    private static void EnsureFolder(string path, string folderName)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        var parentPath = path.Substring(0, path.LastIndexOf('/'));
        AssetDatabase.CreateFolder(parentPath, folderName);
    }

    private static void EnsureProjectFolders()
    {
        EnsureFolder(ProjectRootSettingsFolder, "Settings");
        EnsureFolder(ProjectSettingsFolder, "AudioSystem");
    }

    private static void AutoAssignCategoryGroups(
        SerializedProperty categoriesProperty,
        AudioMixer previousMixer,
        AudioMixer newMixer)
    {
        if (categoriesProperty == null || newMixer == null ||
            newMixer == previousMixer)
        {
            return;
        }

        var availableGroups = newMixer
            .FindMatchingGroups(string.Empty)
            .ToList();
        var usedGroups = new HashSet<AudioMixerGroup>();

        for (var index = 0; index < categoriesProperty.arraySize; index++)
        {
            var categoryProperty =
                categoriesProperty.GetArrayElementAtIndex(index);
            var categoryName = categoryProperty
                .FindPropertyRelative("name")
                .stringValue;
            var groupProperty =
                categoryProperty.FindPropertyRelative("mixerGroup");
            var previousGroup =
                groupProperty.objectReferenceValue as AudioMixerGroup;
            var replacement = FindAvailableGroupByName(
                availableGroups,
                usedGroups,
                categoryName);

            if (replacement == null && previousGroup != null)
            {
                replacement = FindAvailableGroupByName(
                    availableGroups,
                    usedGroups,
                    previousGroup.name);
            }

            if (replacement == null)
                continue;

            groupProperty.objectReferenceValue = replacement;
            usedGroups.Add(replacement);
        }
    }

    private static AudioMixerGroup FindAvailableGroupByName(
        IEnumerable<AudioMixerGroup> groups,
        ISet<AudioMixerGroup> usedGroups,
        string expectedName)
    {
        if (string.IsNullOrWhiteSpace(expectedName))
            return null;

        return groups.FirstOrDefault(group =>
            !usedGroups.Contains(group) &&
            string.Equals(
                group.name,
                expectedName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool RemapCategoryGroups(
        SerializedProperty categoriesProperty,
        AudioMixer sourceMixer,
        AudioMixer destinationMixer)
    {
        if (categoriesProperty == null)
            return false;

        var changed = false;
        for (var index = 0; index < categoriesProperty.arraySize; index++)
        {
            var groupProperty = categoriesProperty
                .GetArrayElementAtIndex(index)
                .FindPropertyRelative("mixerGroup");
            var sourceGroup =
                groupProperty.objectReferenceValue as AudioMixerGroup;
            if (sourceGroup == null || sourceGroup.audioMixer != sourceMixer)
                continue;

            var destinationGroup = FindEquivalentGroup(
                sourceGroup,
                destinationMixer);
            if (destinationGroup == null)
                continue;

            groupProperty.objectReferenceValue = destinationGroup;
            changed = true;
        }

        return changed;
    }

    private static void RemapAudioAssets(
        AudioMixer sourceMixer,
        AudioMixer destinationMixer)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Audio"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/"))
                continue;

            var audio = AssetDatabase.LoadAssetAtPath<Audio>(path);
            if (audio == null || audio.mixerGroup == null ||
                audio.mixerGroup.audioMixer != sourceMixer)
            {
                continue;
            }

            var destinationGroup = FindEquivalentGroup(
                audio.mixerGroup,
                destinationMixer);
            if (destinationGroup == null)
                continue;

            var serializedAudio = new SerializedObject(audio);
            serializedAudio.FindProperty("mixerGroup").objectReferenceValue =
                destinationGroup;
            serializedAudio.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(audio);
            AssetDatabase.SaveAssetIfDirty(audio);
        }
    }

    private static AudioMixerGroup FindEquivalentGroup(
        AudioMixerGroup sourceGroup,
        AudioMixer destinationMixer)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
            sourceGroup,
            out _,
            out long sourceLocalId);

        var groups = destinationMixer.FindMatchingGroups(string.Empty);
        foreach (var group in groups)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                group,
                out _,
                out long destinationLocalId);
            if (sourceLocalId != 0 && destinationLocalId == sourceLocalId)
                return group;
        }

        return groups.FirstOrDefault(group => group.name == sourceGroup.name);
    }

    private static bool IsPackageAsset(UnityEngine.Object asset)
    {
        if (asset == null)
            return false;

        var path = AssetDatabase.GetAssetPath(asset);
        return path.StartsWith("Packages/");
    }

    private static void HidePackageDefaultMixer()
    {
        var templatePath = AssetDatabase.GUIDToAssetPath(DefaultMixerGuid);
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(templatePath);
        if (mixer == null)
            return;

        mixer.hideFlags |= HideFlags.HideInHierarchy | HideFlags.NotEditable;
        foreach (var group in mixer.FindMatchingGroups(string.Empty))
        {
            group.hideFlags |=
                HideFlags.HideInHierarchy | HideFlags.NotEditable;
        }
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
