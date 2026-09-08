#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BigfootMonsterTruckSetup
{
    private const string WindowsBundlePath =
        "Assets/Mods/BigfootMonsterTruck/AssetBundles/Windows/bigfootmonstertruck.unity3d";
    private const string AudiAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string AudiPrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath =
        "Assets/Mods/BigfootMonsterTruck/Models/bigfoot-driveable.glb";
    private const string TargetAssetPath =
        "Assets/Mods/BigfootMonsterTruck/BigfootMonsterTruck.asset";
    private const string TargetPrefabPath =
        "Assets/Mods/BigfootMonsterTruck/BigfootMonsterTruck.prefab";
    private const string VehicleTypeName =
        "bigfootmonstertruck-vehicle:vehicletype_bigfootmonstertruck";

    [MenuItem("Big Ambitions Mods/Setup Bigfoot Monster Truck")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "BigfootMonsterTruck setup complete: generated driveable prefab, vehicle type, " +
            "four animated wheel visuals, centered seat anchor, colliders and transparent windshield.");
    }

    public static void VerifyBuiltBundle()
    {
        var bundle = AssetBundle.LoadFromFile(WindowsBundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");
        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(TargetAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(TargetPrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException("Bundle is missing its VehicleType or vehicle prefab.");

            var wheelControllers = 0;
            var wheelVisuals = 0;
            var hasSeat = false;
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.EndsWith("_WheelController", StringComparison.Ordinal) &&
                    !transform.name.StartsWith("Wheel", StringComparison.Ordinal))
                    wheelControllers++;
                if (transform.name.StartsWith("Wheel", StringComparison.Ordinal) &&
                    transform.name.EndsWith("Visual", StringComparison.Ordinal))
                    wheelVisuals++;
                hasSeat |= string.Equals(transform.name, "BigfootDriverSeat", StringComparison.Ordinal);
            }

            var visibleRenderers = 0;
            var hasTransparentGlass = false;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled && renderer.sharedMaterials.Length > 0)
                    visibleRenderers++;
                foreach (var material in renderer.sharedMaterials)
                    if (material != null &&
                        material.name.IndexOf("Windshield_Glass", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        material.renderQueue >= 3000)
                        hasTransparentGlass = true;
            }

            if (wheelControllers != 4 || wheelVisuals != 4 || !hasSeat ||
                visibleRenderers == 0 || !hasTransparentGlass)
            {
                throw new InvalidOperationException(
                    $"Bundle verification failed: controllers={wheelControllers}, " +
                    $"wheelVisuals={wheelVisuals}, seat={hasSeat}, visibleRenderers={visibleRenderers}, " +
                    $"transparentGlass={hasTransparentGlass}.");
            }

            Debug.Log(
                $"BigfootMonsterTruck bundle verified: controllers={wheelControllers}, " +
                $"wheelVisuals={wheelVisuals}, visibleRenderers={visibleRenderers}, " +
                "centerSeat=true, transparentGlass=true.");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static UnityEngine.Object CreateVehicleType()
    {
        var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AudiAssetPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R VehicleType reference asset was not found.");

        AssetDatabase.DeleteAsset(TargetAssetPath);
        if (!AssetDatabase.CopyAsset(AudiAssetPath, TargetAssetPath))
            throw new InvalidOperationException("Could not create the Bigfoot VehicleType asset.");

        var target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetAssetPath);
        if (target == null)
            throw new InvalidOperationException("Generated Bigfoot VehicleType asset did not load.");

        target.name = "BigfootMonsterTruck";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetBool(serialized, "isATruck", false);
        SetBool(serialized, "fitsHandTruck", true);
        SetBool(serialized, "fitsFlatbed", false);
        SetBool(serialized, "autoParkSupported", false);
        SetBool(serialized, "hasRadio", true);
        SetBool(serialized, "enclosed", true);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        return target;
    }

    private static void CreateVehiclePrefab(UnityEngine.Object vehicleType)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(AudiPrefabPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R reference prefab was not found.");
        if (model == null)
            throw new InvalidOperationException("Processed Bigfoot GLB did not import as a prefab.");

        AssetDatabase.DeleteAsset(TargetPrefabPath);
        var root = UnityEngine.Object.Instantiate(source);
        root.name = "BigfootMonsterTruck";
        try
        {
            StripAudiGeometry(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureExitMarkers(root);
            ConfigureVehicleReferences(root, vehicleType);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the processed Bigfoot model.");
            modelInstance.name = "BigfootVisual";
            AttachWheelVisuals(root, modelInstance);
            CreateSeatAnchor(root);
            ConfigureRendererReferences(root, modelInstance);

            var result = PrefabUtility.SaveAsPrefabAsset(root, TargetPrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Bigfoot vehicle prefab.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void StripAudiGeometry(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.enabled = false;
            renderer.sharedMaterials = Array.Empty<Material>();
        }
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            renderer.enabled = false;
            renderer.sharedMesh = null;
            renderer.sharedMaterials = Array.Empty<Material>();
        }
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            filter.sharedMesh = null;
    }

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>();
        if (body == null)
            throw new InvalidOperationException("Reference prefab has no Rigidbody.");
        body.mass = 4500f;
        body.drag = 0.02f;
        body.angularDrag = 0.12f;
        body.centerOfMass = new Vector3(0f, 0.72f, 0f);
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    private static void ConfigureWheelControllers(GameObject root)
    {
        SetLocalPosition(root, "FrontLeft_WheelController", new Vector3(-1.35f, 0.82f, 1.60f));
        SetLocalPosition(root, "FrontRight_WheelController", new Vector3(1.35f, 0.82f, 1.60f));
        SetLocalPosition(root, "RearLeft_WheelController", new Vector3(-1.35f, 0.82f, -1.60f));
        SetLocalPosition(root, "RearRight_WheelController", new Vector3(1.35f, 0.82f, -1.60f));
    }

    private static void ConfigureBodyColliders(GameObject root)
    {
        var holder = FindTransform(root.transform, "BodyCollider") ??
                     throw new InvalidOperationException("Reference BodyCollider is missing.");
        var colliders = holder.GetComponents<BoxCollider>();
        if (colliders.Length < 2)
            throw new InvalidOperationException("Reference vehicle needs two body colliders.");
        colliders[0].center = new Vector3(0f, 1.05f, 0f);
        colliders[0].size = new Vector3(2.45f, 1.0f, 5.3f);
        colliders[1].center = new Vector3(0f, 2.0f, 0.2f);
        colliders[1].size = new Vector3(1.95f, 1.25f, 2.8f);
    }

    private static void ConfigureExitMarkers(GameObject root)
    {
        SetLocalPosition(root, "Driverside", new Vector3(-2.05f, 0.1f, 0.1f));
        SetLocalPosition(root, "Passengerside", new Vector3(2.05f, 0.1f, 0.1f));
    }

    private static void ConfigureVehicleReferences(GameObject root, UnityEngine.Object vehicleType)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var vehicleTypeProperty = serialized.FindProperty("vehicleType");
            if (vehicleTypeProperty?.propertyType == SerializedPropertyType.ObjectReference)
                vehicleTypeProperty.objectReferenceValue = vehicleType;
            var instance = serialized.FindProperty("vehicleInstance");
            var typeName = instance?.FindPropertyRelative("vehicleTypeName");
            if (typeName?.propertyType == SerializedPropertyType.String)
                typeName.stringValue = VehicleTypeName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var mapping = new Dictionary<string, string>
        {
            { "WheelFrontLeftVisual", "FrontLeft_WheelController" },
            { "WheelFrontRightVisual", "FrontRight_WheelController" },
            { "WheelRearLeftVisual", "RearLeft_WheelController" },
            { "WheelRearRightVisual", "RearRight_WheelController" },
        };

        foreach (var pair in mapping)
        {
            var wheel = FindTransform(model.transform, pair.Key) ??
                        throw new InvalidOperationException($"Model wheel '{pair.Key}' is missing.");
            var target = FindWheelVisualTarget(root, pair.Value) ??
                         throw new InvalidOperationException(
                             $"Wheel visual reference for '{pair.Value}' is missing.");
            wheel.SetParent(target, true);
            wheel.localPosition = Vector3.zero;
        }
    }

    private static Transform? FindWheelVisualTarget(GameObject root, string controllerName)
    {
        var controller = FindTransform(root.transform, controllerName);
        if (controller == null)
            return null;
        foreach (var component in controller.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var visual = serialized.FindProperty("wheel")?.FindPropertyRelative("visual");
            if (visual?.objectReferenceValue is Transform transform)
                return transform;
        }
        return controller;
    }

    private static void CreateSeatAnchor(GameObject root)
    {
        var anchor = new GameObject("BigfootDriverSeat");
        anchor.transform.SetParent(root.transform, false);
        anchor.transform.localPosition = new Vector3(0f, 1.72f, 0.18f);
        anchor.transform.localRotation = Quaternion.identity;
    }

    private static void ConfigureRendererReferences(GameObject root, GameObject model)
    {
        var renderers = new List<Renderer>(model.GetComponentsInChildren<Renderer>(true));
        foreach (var wheelName in new[]
                 {
                     "WheelFrontLeft_WheelController", "WheelFrontRight_WheelController",
                     "WheelRearLeft_WheelController", "WheelRearRight_WheelController"
                 })
        {
            var wheel = FindTransform(root.transform, wheelName);
            if (wheel != null)
                renderers.AddRange(wheel.GetComponentsInChildren<Renderer>(true));
        }

        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            AssignRendererArray(serialized.FindProperty("bodyMeshes"), renderers);
            AssignRendererArray(serialized.FindProperty("renderers"), renderers);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void AssignRendererArray(SerializedProperty? property, List<Renderer> renderers)
    {
        if (property == null || !property.isArray || property.propertyType == SerializedPropertyType.String)
            return;
        property.arraySize = renderers.Count;
        for (var index = 0; index < renderers.Count; index++)
        {
            var element = property.GetArrayElementAtIndex(index);
            if (element.propertyType == SerializedPropertyType.ObjectReference)
                element.objectReferenceValue = renderers[index];
        }
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        return null;
    }

    private static void SetLocalPosition(GameObject root, string name, Vector3 position)
    {
        var transform = FindTransform(root.transform, name) ??
                        throw new InvalidOperationException($"Reference transform '{name}' is missing.");
        transform.localPosition = position;
    }

    private static void SetString(SerializedObject target, string name, string value)
    {
        var property = target.FindProperty(name);
        if (property != null)
            property.stringValue = value;
    }

    private static void SetBool(SerializedObject target, string name, bool value)
    {
        var property = target.FindProperty(name);
        if (property != null)
            property.boolValue = value;
    }
}
