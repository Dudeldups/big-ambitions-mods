using System;
using System.IO;
using BAModAPI.Services;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static class StreetQuestAssetBundleService
    {
        internal const string BundleKey = "AssetBundles/streetquestrpg.unity3d";
        private const string ModAssetPathPrefix = "Assets/Mods/StreetQuestRPG/";
        private const string BundleFileName = "streetquestrpg.unity3d";

        internal static string[] GetRegisteredBundlePaths()
        {
            try
            {
                var bundlePath = GetInstalledBundlePath();
                if (IsValidBundleFile(bundlePath))
                    return new[] { BundleKey };
            }
            catch
            {
            }

            return Array.Empty<string>();
        }

        internal static bool IsBundledStreetQuestAssetPath(string assetPath)
        {
            return !string.IsNullOrWhiteSpace(assetPath)
                && assetPath.StartsWith(ModAssetPathPrefix, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TrySpawnPrefab(string prefabAssetPath, Transform parent, out GameObject instance)
        {
            instance = null;

            if (!IsBundledStreetQuestAssetPath(prefabAssetPath))
                return false;

            var modId = StreetQuestRuntimeBootstrap.CurrentModId;
            if (string.IsNullOrWhiteSpace(modId))
            {
                StreetQuestShared.LogDebug(
                    $"BundledPrefabSpawn failed prefab={prefabAssetPath} reason=mod_id_missing");
                return false;
            }

            try
            {
                var bundlePath = GetInstalledBundlePath();
                if (!IsValidBundleFile(bundlePath))
                {
                    StreetQuestShared.LogDebug(
                        $"BundledPrefabSpawn failed prefab={prefabAssetPath} bundle={BundleKey} reason=bundle_file_invalid path={bundlePath ?? "<null>"}");
                    return false;
                }

                var bundle = AssetService.GetBundle(modId, BundleKey);
                if (bundle == null)
                {
                    StreetQuestShared.LogDebug(
                        $"BundledPrefabSpawn failed prefab={prefabAssetPath} bundle={BundleKey} reason=bundle_missing");
                    return false;
                }

                instance = AssetService.Spawn(
                    modId,
                    BundleKey,
                    prefabAssetPath,
                    Vector3.zero,
                    Quaternion.identity,
                    parent,
                    false,
                    true,
                    true);

                if (instance == null)
                {
                    StreetQuestShared.LogDebug(
                        $"BundledPrefabSpawn failed prefab={prefabAssetPath} bundle={BundleKey} reason=spawn_returned_null");
                    return false;
                }

                StreetQuestShared.LogDebug(
                    $"BundledPrefabSpawn success prefab={prefabAssetPath} bundle={BundleKey}");
                return true;
            }
            catch (Exception exception)
            {
                StreetQuestShared.LogDebug(
                    $"BundledPrefabSpawn failed prefab={prefabAssetPath} bundle={BundleKey} reason={exception.GetType().Name}:{exception.Message}");
                instance = null;
                return false;
            }
        }

        private static string GetInstalledBundlePath()
        {
            var modRootPath = StreetQuestRuntimeBootstrap.CurrentModRootPath;
            if (string.IsNullOrWhiteSpace(modRootPath))
                return null;

            try
            {
                var assetBundlesRoot = Path.Combine(modRootPath, "AssetBundles");
                if (!Directory.Exists(assetBundlesRoot))
                    return null;

                var directPath = Path.Combine(assetBundlesRoot, BundleFileName);
                if (File.Exists(directPath))
                    return directPath;

                var nestedMatches = Directory.GetFiles(assetBundlesRoot, BundleFileName, SearchOption.AllDirectories);
                if (nestedMatches != null && nestedMatches.Length > 0)
                    return nestedMatches[0];
            }
            catch
            {
            }

            return null;
        }

        private static bool IsValidBundleFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                return true;
            }
            catch (Exception exception)
            {
                StreetQuestShared.LogDebug(
                    $"BundleValidation failed bundle={BundleKey} path={path} reason={exception.GetType().Name}:{exception.Message}");
                return false;
            }
        }
    }
}
