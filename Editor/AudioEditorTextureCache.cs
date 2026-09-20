using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Emmanuel.AudioSystem;

namespace Emmanuel.AudioSystem.Editor
{
internal static class AudioEditorTextureCache
{
    private const string CacheRoot = "AudioSystem/TextureCache";

    internal static string CreateKey(
        UnityEngine.Object source,
        string variant)
    {
        var assetPath = AssetDatabase.GetAssetPath(source);
        var guid = AssetDatabase.AssetPathToGUID(assetPath);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
            source,
            out _,
            out long localId);
        var dependencyHash = string.IsNullOrEmpty(assetPath)
            ? default
            : AssetDatabase.GetAssetDependencyHash(assetPath);

        return Hash128.Compute(
            $"{guid}:{localId}:{dependencyHash}:{variant}").ToString();
    }

    internal static Texture2D Load(
        string section,
        string key,
        int width,
        int height,
        string textureName,
        FilterMode filterMode)
    {
        try
        {
            var path = GetPath(section, key);
            if (!File.Exists(path))
                return null;

            var data = File.ReadAllBytes(path);
            if (data.Length != width * height * 4)
                return null;

            var texture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = textureName,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.LoadRawTextureData(data);
            texture.Apply(false, true);
            return texture;
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static void Save(
        string section,
        string key,
        Texture2D texture)
    {
        try
        {
            var path = GetPath(section, key);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var rawData = texture.GetRawTextureData<byte>();
            var data = new byte[rawData.Length];
            rawData.CopyTo(data);
            File.WriteAllBytes(path, data);
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException)
        {
            // The cache is optional. The editor can regenerate the texture.
        }
    }

    private static string GetPath(string section, string key)
    {
        var projectRoot = Path.GetFullPath(
            Path.Combine(Application.dataPath, ".."));
        return Path.Combine(
            projectRoot,
            "Library",
            CacheRoot,
            section,
            $"{key}.rgba");
    }
}
}
