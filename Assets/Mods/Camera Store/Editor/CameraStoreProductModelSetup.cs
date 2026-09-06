#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CameraStore.Editor
{
    public static class CameraStoreProductModelSetup
    {
        private const string Root = "Assets/Mods/Camera Store";
        private const string BundleName = "camerastore-businesstype";
        private const string BundleVariant = "unity3d";

        private sealed class ProductDefinition
        {
            public string Name;
            public string ItemId;
            public string PrefabPath;
            public string ModelPath;
            public int TriangleLimit;
        }

        private static readonly ProductDefinition[] Products =
        {
            Product("Compact Camera", "camerastore:item_compact_camera", "compact_camera", 1000),
            Product("DSLR Camera", "camerastore:item_dslr_camera", "dslr_camera", 6000),
            Product("Professional Camera", "camerastore:item_professional_camera", "professional_camera", 25000),
            Product("Action Camera", "camerastore:item_action_camera", "action_camera", 3500),
            Product("Camera Lens", "camerastore:item_camera_lens", "camera_lens", 9000),
            Product("Tripod", "camerastore:item_tripod", "tripod", 1000),
            new ProductDefinition
            {
                Name = "Camera Flash",
                ItemId = "camerastore:item_camera_flash",
                PrefabPath = Root + "/Prefabs/camera_flash.prefab",
                ModelPath = null,
                TriangleLimit = 1000,
            },
            Product("Camera Bag", "camerastore:item_camera_bag", "camera_bag", 20000),
        };

        [MenuItem("Big Ambitions/Camera Store/Apply Final Product Models")]
        public static void ApplyFinalProductModels()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (ProductDefinition product in Products)
            {
                ApplyProduct(product);
            }

            AssignCameraStoreBundle();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateFinalProductModels();
        }

        [MenuItem("Big Ambitions/Camera Store/Validate Final Product Models")]
        public static void ValidateFinalProductModels()
        {
            var lines = new List<string>
            {
                "Camera Store final product model validation",
                "Unity " + Application.unityVersion,
                "",
            };
            var errors = new List<string>();

            foreach (ProductDefinition product in Products)
            {
                ValidateProduct(product, lines, errors);
            }

            lines.Add("");
            lines.Add(errors.Count == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            foreach (string error in errors)
            {
                lines.Add("ERROR: " + error);
            }

            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/CameraStoreProductValidation.txt", lines);
            Debug.Log(string.Join("\n", lines));

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Camera Store product validation failed. See Logs/CameraStoreProductValidation.txt."
                );
            }
        }

        private static ProductDefinition Product(string name, string itemId, string assetName, int triangleLimit)
        {
            return new ProductDefinition
            {
                Name = name,
                ItemId = itemId,
                PrefabPath = Root + "/Prefabs/" + assetName + ".prefab",
                ModelPath = Root + "/Models/Products/" + assetName + ".glb",
                TriangleLimit = triangleLimit,
            };
        }

        private static void ApplyProduct(ProductDefinition product)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(product.PrefabPath);
            try
            {
                if (!string.IsNullOrEmpty(product.ModelPath))
                {
                    GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(product.ModelPath);
                    if (model == null)
                    {
                        throw new InvalidOperationException("Could not load imported model " + product.ModelPath);
                    }

                    while (root.transform.childCount > 0)
                    {
                        UnityEngine.Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
                    }

                    GameObject visual = UnityEngine.Object.Instantiate(model, root.transform, false);
                    visual.name = "FinalProductVisual";
                    visual.transform.localPosition = Vector3.zero;
                    visual.transform.localRotation = Quaternion.identity;
                    visual.transform.localScale = Vector3.one;
                }

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    throw new InvalidOperationException(product.Name + " does not have any renderers");
                }

                BoxCollider collider = root.GetComponent<BoxCollider>();
                if (collider == null)
                {
                    collider = root.AddComponent<BoxCollider>();
                }

                Bounds bounds = CalculateBounds(renderers);
                collider.center = root.transform.InverseTransformPoint(bounds.center);
                collider.size = new Vector3(
                    Mathf.Max(0.01f, bounds.size.x + 0.004f),
                    Mathf.Max(0.01f, bounds.size.y + 0.004f),
                    Mathf.Max(0.01f, bounds.size.z + 0.004f)
                );

                Component controller = root.GetComponents<Component>()
                    .FirstOrDefault(component => component != null && component.GetType().Name == "ItemController");
                if (controller == null)
                {
                    throw new InvalidOperationException(product.Name + " is missing ItemController");
                }

                var serializedController = new SerializedObject(controller);
                SetString(serializedController, "itemName", product.ItemId);
                SetBool(serializedController, "checkRenderersInChildren", true);
                SetObjectArray(serializedController, "renderers", renderers.Cast<UnityEngine.Object>().ToArray());
                SetObjectArray(serializedController, "colliders", new UnityEngine.Object[] { collider });
                serializedController.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, product.PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AssignCameraStoreBundle()
        {
            // Clear stale labels first. Documentation and generated bundle outputs were
            // assigned by an earlier broad setup pass and must never become bundle inputs.
            AssetDatabase.RemoveAssetBundleName(BundleName, true);

            foreach (string path in AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith(Root + "/", StringComparison.Ordinal)))
            {
                if (AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }

                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer == null)
                {
                    continue;
                }

                bool shouldBundle =
                    (path.StartsWith(Root + "/Items/", StringComparison.Ordinal) && path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    || (path.StartsWith(Root + "/Prefabs/", StringComparison.Ordinal) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    || (path.StartsWith(Root + "/Models/Materials/", StringComparison.Ordinal) && path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    || (path.StartsWith(Root + "/Models/Products/", StringComparison.Ordinal) && path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                    || string.Equals(path, Root + "/CameraStore.asset", StringComparison.Ordinal);
                string desiredName = shouldBundle ? BundleName : string.Empty;
                string desiredVariant = shouldBundle ? BundleVariant : string.Empty;

                if (!string.Equals(importer.assetBundleName, desiredName, StringComparison.Ordinal)
                    || !string.Equals(importer.assetBundleVariant, desiredVariant, StringComparison.Ordinal))
                {
                    importer.assetBundleName = desiredName;
                    importer.assetBundleVariant = desiredVariant;
                    importer.SaveAndReimport();
                }
            }
        }

        private static void ValidateProduct(ProductDefinition product, List<string> lines, List<string> errors)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(product.PrefabPath);
            if (prefab == null)
            {
                errors.Add(product.Name + ": prefab could not be loaded");
                return;
            }

            Component controller = prefab.GetComponents<Component>()
                .FirstOrDefault(component => component != null && component.GetType().Name == "ItemController");
            if (controller == null)
            {
                errors.Add(product.Name + ": ItemController missing");
                return;
            }

            var serializedController = new SerializedObject(controller);
            string itemId = serializedController.FindProperty("itemName")?.stringValue;
            if (!string.Equals(itemId, product.ItemId, StringComparison.Ordinal))
            {
                errors.Add(product.Name + ": stable item ID changed to " + itemId);
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                errors.Add(product.Name + ": no renderer found");
                return;
            }

            int missingMaterials = renderers.Sum(renderer => renderer.sharedMaterials.Count(material => material == null));
            if (missingMaterials > 0)
            {
                errors.Add(product.Name + ": " + missingMaterials + " missing material references");
            }

            int triangles = prefab.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh != null)
                .Sum(filter => CountTriangles(filter.sharedMesh));
            if (triangles > product.TriangleLimit)
            {
                errors.Add(product.Name + ": " + triangles + " triangles exceeds " + product.TriangleLimit);
            }

            BoxCollider collider = prefab.GetComponent<BoxCollider>();
            if (collider == null || collider.size.sqrMagnitude <= 0.000001f)
            {
                errors.Add(product.Name + ": shelf collider missing or empty");
            }

            Bounds bounds = CalculateBounds(renderers);
            string bundle = AssetImporter.GetAtPath(product.PrefabPath)?.assetBundleName;
            if (!string.Equals(bundle, BundleName, StringComparison.Ordinal))
            {
                errors.Add(product.Name + ": prefab is not assigned to the Camera Store bundle");
            }

            if (!string.IsNullOrEmpty(product.ModelPath))
            {
                string modelBundle = AssetImporter.GetAtPath(product.ModelPath)?.assetBundleName;
                if (!string.Equals(modelBundle, BundleName, StringComparison.Ordinal))
                {
                    errors.Add(product.Name + ": model is not assigned to the Camera Store bundle");
                }

                string[] forbidden = { "dji", "lumix", "canon", "samsung" };
                foreach (Material material in renderers.SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null))
                {
                    if (forbidden.Any(token => material.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        errors.Add(product.Name + ": branded material remains: " + material.name);
                    }

                    if (material.shader == null)
                    {
                        errors.Add(product.Name + ": material has no HDRP-compatible shader: " + material.name);
                    }
                }
            }

            lines.Add(
                string.Format(
                    "{0}: id={1}; renderers={2}; materials={3}; triangles={4}; bounds={5:F3} x {6:F3} x {7:F3} m; collider={8}; bundle={9}",
                    product.Name,
                    itemId,
                    renderers.Length,
                    renderers.Sum(renderer => renderer.sharedMaterials.Length),
                    triangles,
                    bounds.size.x,
                    bounds.size.y,
                    bounds.size.z,
                    collider != null ? "yes" : "no",
                    bundle
                )
            );
        }

        private static int CountTriangles(Mesh mesh)
        {
            long indices = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                indices += (long)mesh.GetIndexCount(subMesh);
            }

            return checked((int)(indices / 3));
        }

        private static Bounds CalculateBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void SetString(SerializedObject target, string propertyName, string value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property != null)
            {
                property.stringValue = value;
            }
        }

        private static void SetBool(SerializedObject target, string propertyName, bool value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetObjectArray(SerializedObject target, string propertyName, UnityEngine.Object[] values)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null || !property.isArray)
            {
                return;
            }

            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            }
        }
    }
}
#endif
