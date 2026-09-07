#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BAModAPI.Services;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem;
using Helpers;
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

        private CameraStoreMethodDetour? createPrefabItemDetour;

        public void Apply(AssetBundle bundle)
        {
            ValidateBundledPrefabs(bundle);

            var target = typeof(PrefabHelper).GetMethod(
                nameof(PrefabHelper.CreatePrefabItem),
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(Transform) },
                null);
            var replacement = typeof(CameraStorePrefabResolver).GetMethod(
                nameof(CreatePrefabItem),
                BindingFlags.NonPublic | BindingFlags.Static);
            if (target == null || replacement == null)
                throw new MissingMethodException("Camera Store could not resolve PrefabHelper.CreatePrefabItem.");

            createPrefabItemDetour = new CameraStoreMethodDetour(target, replacement);
            if (!createPrefabItemDetour.Apply(out var error))
                throw new InvalidOperationException("Camera Store could not install its stable-ID prefab resolver: " + error);
        }

        public void Restore()
        {
            if (createPrefabItemDetour == null)
                return;

            if (!createPrefabItemDetour.Restore(out var error))
                Debug.LogWarning("Camera Store could not restore PrefabHelper.CreatePrefabItem: " + error);

            createPrefabItemDetour = null;
            LoggedPrefabIds.Clear();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ItemController CreatePrefabItem(string itemName, Transform itemContainer)
        {
            var isCameraStoreItem = PrefabKeys.TryGetValue(itemName, out var cameraStorePrefabKey);
            var prefabKey = isCameraStoreItem
                ? cameraStorePrefabKey
                : itemName.GetIdWithoutType();
            if (isCameraStoreItem && LoggedPrefabIds.Add(itemName))
                Debug.Log("[Camera Store] Resolved " + itemName + " to Prefabs/" + prefabKey + ".prefab.");

            var controller = PrefabHelper.CreatePrefab<ItemController>(prefabKey, itemContainer);
            controller.itemName = itemName;

            if (ItemsGetter.IsModItem(itemName))
                AssetService.RemapShaders(controller.gameObject, null);

            return controller;
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
