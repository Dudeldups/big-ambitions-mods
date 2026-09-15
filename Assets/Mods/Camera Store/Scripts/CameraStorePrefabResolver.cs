#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem;
using UnityEngine;

namespace CameraStore
{
    internal sealed class CameraStorePrefabResolver
    {
        private static readonly Dictionary<string, string> PrefabKeys = new(StringComparer.Ordinal)
        {
            [CameraStoreIds.CompactCamera] = "compact_camera",
            [CameraStoreIds.DslrCamera] = "dslr_camera",
            [CameraStoreIds.ProfessionalCamera] = "professional_camera",
            [CameraStoreIds.ActionCamera] = "action_camera",
            [CameraStoreIds.CameraLens] = "camera_lens",
            [CameraStoreIds.Tripod] = "tripod",
            [CameraStoreIds.CameraFlash] = "camera_flash",
            [CameraStoreIds.CameraBag] = "camera_bag",
            [CameraStoreIds.CameraDisplay] = "camera_display",
            [CameraStoreIds.CameraAccessoriesShelf] = "camera_accessories_shelf"
        };
        private static readonly HashSet<string> LoggedPrefabIds = new(StringComparer.Ordinal);

        private CameraStoreMethodDetour? getIdWithoutTypeDetour;

        public void Apply(AssetBundle bundle)
        {
            ValidateBundledPrefabs(bundle);

            var target = typeof(StringIdExtensions).GetMethod(
                nameof(StringIdExtensions.GetIdWithoutType),
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            var replacement = typeof(CameraStorePrefabResolver).GetMethod(
                nameof(GetIdWithoutType),
                BindingFlags.NonPublic | BindingFlags.Static);
            if (target == null || replacement == null)
                throw new MissingMethodException("Camera Store could not resolve StringIdExtensions.GetIdWithoutType.");

            getIdWithoutTypeDetour = new CameraStoreMethodDetour(target, replacement);
            if (!getIdWithoutTypeDetour.Apply(out var error))
                throw new InvalidOperationException("Camera Store could not install its stable-ID asset-key resolver: " + error);
        }

        public void Restore()
        {
            if (getIdWithoutTypeDetour == null)
                return;

            if (!getIdWithoutTypeDetour.Restore(out var error))
                Debug.LogWarning("Camera Store could not restore StringIdExtensions.GetIdWithoutType: " + error);

            getIdWithoutTypeDetour = null;
            LoggedPrefabIds.Clear();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string GetIdWithoutType(string id)
        {
            if (PrefabKeys.TryGetValue(id, out var cameraStorePrefabKey))
            {
                if (LoggedPrefabIds.Add(id))
                    Debug.Log("[Camera Store] Resolved stable ID " + id + " to asset key " + cameraStorePrefabKey + ".");

                return cameraStorePrefabKey;
            }

            // Preserve the game's implementation exactly for every ID not owned by Camera Store.
            if (string.IsNullOrWhiteSpace(id) || !id.Contains("_"))
                return string.Empty;

            return id.Split(new[] { '_' }, StringSplitOptions.None)[1];
        }

        private static void ValidateBundledPrefabs(AssetBundle bundle)
        {
            foreach (var pair in PrefabKeys)
            {
                var assetPath = "Assets/Mods/Camera Store/Prefabs/" + pair.Value + ".prefab";
                var prefab = bundle.LoadAsset<GameObject>(assetPath);
                if (prefab == null)
                    throw new InvalidOperationException("Camera Store prefab is missing from its AssetBundle: " + assetPath);

                var controller = prefab.GetComponent<ItemController>();
                if (controller == null)
                    throw new InvalidOperationException("Camera Store prefab has no ItemController: " + assetPath);

                if (!string.Equals(controller.itemName, pair.Key, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Camera Store prefab item ID mismatch: " + assetPath + " has " + controller.itemName + ".");
                }
            }
        }
    }
}
