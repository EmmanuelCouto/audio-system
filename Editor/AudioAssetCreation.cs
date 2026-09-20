using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Emmanuel.AudioSystem;

namespace Emmanuel.AudioSystem.Editor
{
internal static class AudioAssetCreation
{
    private const string CreateAudioMenu =
        "Assets/Create/Audio/Audio";
    private const string CreateIndividualAudioMenu =
        "Assets/Create/Audio/Audio For Each Selected Clip";

    [MenuItem(CreateAudioMenu, false, 1)]
    private static void CreateAudio()
    {
        var selectedClips = GetSelectedClips();
        var assetName = Selection.activeObject is AudioClip activeClip
            ? activeClip.name
            : "New Audio";
        var audio = CreateAudioAsset(
            GetTargetFolder(),
            assetName,
            selectedClips);

        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = audio;
    }

    [MenuItem(CreateIndividualAudioMenu, false, 2)]
    private static void CreateIndividualAudioForEachClip()
    {
        var selectedClips = GetSelectedClips();
        var createdAssets = new List<Audio>(selectedClips.Length);

        Undo.SetCurrentGroupName("Create Audio Assets");
        foreach (var clip in selectedClips)
        {
            var clipPath = AssetDatabase.GetAssetPath(clip);
            var folder = NormalizeAssetPath(
                Path.GetDirectoryName(clipPath) ?? "Assets");
            createdAssets.Add(CreateAudioAsset(
                folder,
                clip.name,
                new[] { clip }));
        }

        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.objects = createdAssets.ToArray();
        if (createdAssets.Count > 0)
            EditorGUIUtility.PingObject(createdAssets[0]);
    }

    [MenuItem(CreateIndividualAudioMenu, true)]
    private static bool ValidateCreateIndividualAudioForEachClip()
    {
        return Selection.objects.Any(selected => selected is AudioClip);
    }

    private static Audio CreateAudioAsset(
        string folder,
        string assetName,
        AudioClip[] clips)
    {
        var audio = ScriptableObject.CreateInstance<Audio>();
        audio.clips = clips;

        var path = AssetDatabase.GenerateUniqueAssetPath(
            $"{folder}/{assetName}.asset");
        AssetDatabase.CreateAsset(audio, path);
        Undo.RegisterCreatedObjectUndo(audio, "Create Audio Asset");
        return audio;
    }

    private static AudioClip[] GetSelectedClips()
    {
        return Selection.objects
            .OfType<AudioClip>()
            .ToArray();
    }

    private static string GetTargetFolder()
    {
        if (Selection.activeObject == null)
            return "Assets";

        var selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (AssetDatabase.IsValidFolder(selectedPath))
            return selectedPath;

        return NormalizeAssetPath(
            Path.GetDirectoryName(selectedPath) ?? "Assets");
    }

    private static string NormalizeAssetPath(string path)
    {
        return path.Replace('\\', '/');
    }
}
}
