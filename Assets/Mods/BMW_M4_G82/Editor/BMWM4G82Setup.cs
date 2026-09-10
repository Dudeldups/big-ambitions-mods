#nullable enable
using System;
using System.Collections.Generic;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class BMWM4G82Setup
{
    private const string ModRoot = "Assets/Mods/BMW_M4_G82";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/bmw_m4.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/BMWDamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/BMWM4G82.asset";
    private const string VehiclePrefabPath = ModRoot + "/BMWM4G82.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/BMWM4G82.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/bmw_m4_g82.unity3d";
    private const string VehicleTypeName =
        "bmwm4g82-vehicle:vehicletype_bmwm4g82";
    private const float TargetLength = 4.794f;
    private const float TargetWidth = 1.887f;
    private const float TargetHeight = 1.394f;
    private const float TargetWheelbase = 2.857f;
    private const float FrontTrack = 1.617f;
    private const float RearTrack = 1.605f;
    private const float FrontTireWidth = 0.275f;
    private const float RearTireWidth = 0.285f;
    private const float FrontTireRadius = 0.3376f;
    private const float RearTireRadius = 0.3395f;
    private const float TireFrictionCircleStrength = 0.96f;
    private const float AntiRollBarForce = 7200f;
    private const float FrontSuspensionTravel = 0.11f;
    private const float RearSuspensionTravel = 0.10f;
    private const float DeformationStrength = 0.17f;
    private const float DeformationRadius = 0.24f;
    private const float DeformationRandomness = 0.005f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.18f, -0.08f);
    private static readonly Vector3 SteeringAnchorPosition = new Vector3(-0.38f, 0.92f, 0.55f);

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-0.8085f, FrontTireRadius, 1.4285f) },
            { "FrontRight_WheelController", new Vector3(0.8085f, FrontTireRadius, 1.4285f) },
            { "RearLeft_WheelController", new Vector3(-0.8025f, RearTireRadius, -1.4285f) },
            { "RearRight_WheelController", new Vector3(0.8025f, RearTireRadius, -1.4285f) },
        };

    private static readonly float[] M4Gears =
    {
        -3.478f,
        0f,
        5.000f,
        3.200f,
        2.143f,
        1.720f,
        1.313f,
        1.000f,
        0.823f,
        0.640f,
    };

    private static AnimationCurve CreateM4PowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.12f, 0.19f),
            new Keyframe(0.38f, 0.70f),
            new Keyframe(0.66f, 0.92f),
            new Keyframe(0.87f, 1f),
            new Keyframe(1f, 0.90f));

    [MenuItem("Big Ambitions Mods/Setup BMW M4 G82")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "BMWM4G82 setup complete: generated a fitted G82 M4 Competition with four " +
            "independent wheel visuals, eight-speed M Steptronic, M xDrive, inline-six audio, functional lamp " +
            "geometry, colorable body/calipers, and corrected glass materials.");
    }

    public static void GenerateAndBuild()
    {
        Generate();
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    public static void VerifyBuiltBundle()
    {
        var bundle = AssetBundle.LoadFromFile(WindowsBundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");

        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException("Bundle is missing its BMW VehicleType or prefab.");

            var vehicleSerialized = new SerializedObject(vehicleType);
            var bundledVehicleTypeName =
                vehicleSerialized.FindProperty("vehicleTypeName")?.stringValue;
            if (!string.Equals(bundledVehicleTypeName, VehicleTypeName, StringComparison.Ordinal) ||
                !string.Equals(prefab.name, "BMWM4G82", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"BMW purchase identity mismatch vehicleType='{bundledVehicleTypeName}', " +
                    $"prefab='{prefab.name}'.");
            }

            var price = ReadNumber(vehicleSerialized.FindProperty("price"));
            var maxFuel = ReadNumber(vehicleSerialized.FindProperty("maxFuel"));
            var maxCargo = ReadNumber(vehicleSerialized.FindProperty("maxCargoCapacity"));
            var maxSpeed = ReadNumber(vehicleSerialized.FindProperty("maxSpeed"));
            var enginePower = ReadNumber(vehicleSerialized.FindProperty("enginePower"));
            var luxury = vehicleSerialized.FindProperty("isLuxuryCar")?.boolValue ?? false;
            if (Math.Abs(price - 79795f) > 0.1f || Math.Abs(maxFuel - 59f) > 0.1f ||
                Math.Abs(maxCargo - 12f) > 0.1f || Math.Abs(maxSpeed - 290f) > 0.1f ||
                Math.Abs(enginePower - 375f) > 0.1f || !luxury)
            {
                throw new InvalidOperationException(
                    $"BMW VehicleType mismatch price={price} fuel={maxFuel} cargo={maxCargo} " +
                    $"speed={maxSpeed} power={enginePower} luxury={luxury}.");
            }

            var requiredUniqueNames = new[]
            {
                "BMWVisual", "BodyCollider", "BMWDamageBody", "Steering_wheel",
                "Driverside", "Passengerside", "BMW_DRL_Source", "BMW_Lamp_Source",
                "BMW_RearLamp_Source", "FrontLeft_WheelController", "FrontRight_WheelController",
                "RearLeft_WheelController", "RearRight_WheelController", "BMWWheelFrontLeft",
                "BMWWheelFrontRight", "BMWWheelRearLeft", "BMWWheelRearRight",
                "BMWFixedCaliperFrontLeft", "BMWFixedCaliperFrontRight",
                "BMWFixedCaliperRearLeft", "BMWFixedCaliperRearRight",
            };
            foreach (var requiredName in requiredUniqueNames)
            {
                var count = CountTransformsNamed(prefab.transform, requiredName);
                if (count != 1)
                    throw new InvalidOperationException(
                        $"BMW prefab requires exactly one '{requiredName}' transform, found {count}.");
            }

            var missingScripts = 0;
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                if (component == null) missingScripts++;
            var missingMaterials = 0;
            var missingMeshes = 0;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                    continue;
                foreach (var material in renderer.sharedMaterials)
                    if (material == null) missingMaterials++;
                if (renderer is MeshRenderer &&
                    renderer.GetComponent<MeshFilter>()?.sharedMesh == null)
                    missingMeshes++;
            }
            if (missingScripts != 0 || missingMaterials != 0 || missingMeshes != 0)
                throw new InvalidOperationException(
                    $"BMW prefab has missing content scripts={missingScripts}, " +
                    $"materials={missingMaterials}, meshes={missingMeshes}.");

            var body = prefab.GetComponent<Rigidbody>() ??
                       throw new InvalidOperationException("BMW prefab root Rigidbody is missing.");
            if (Math.Abs(body.mass - 1775f) > 0.1f ||
                Vector3.Distance(body.centerOfMass, StableCenterOfMass) > 0.001f)
                throw new InvalidOperationException(
                    $"BMW root physics mismatch mass={body.mass}, center={body.centerOfMass}.");
            var steeringAnchor = FindTransform(prefab.transform, "Steering_wheel")!;
            var driverExit = FindTransform(prefab.transform, "Driverside")!;
            var passengerExit = FindTransform(prefab.transform, "Passengerside")!;
            if (Vector3.Distance(steeringAnchor.localPosition, SteeringAnchorPosition) > 0.001f ||
                driverExit.localPosition.x > -1.4f || passengerExit.localPosition.x < 1.4f)
                throw new InvalidOperationException(
                    $"BMW LHD cabin anchors mismatch steering={steeringAnchor.localPosition}, " +
                    $"exits={driverExit.localPosition}/{passengerExit.localPosition}.");

            var visual = FindTransform(prefab.transform, "BMWVisual") ??
                         throw new InvalidOperationException("BMW visual root is missing.");
            if (!TryGetModelBodyBounds(visual, out var bounds) ||
                Math.Abs(bounds.size.x - TargetWidth) > 0.06f ||
                Math.Abs(bounds.size.y - TargetHeight) > 0.06f ||
                Math.Abs(bounds.size.z - TargetLength) > 0.06f)
            {
                throw new InvalidOperationException($"BMW body bounds are invalid: {bounds.size}.");
            }

            var centers = new Dictionary<string, Vector3>();
            var wheelCount = 0;
            var caliperCount = 0;
            var rollingParts = 0;
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.StartsWith("BMWWheel", StringComparison.Ordinal))
                {
                    wheelCount++;
                    centers[transform.name] = transform.localPosition;
                    rollingParts += transform.GetComponentsInChildren<MeshRenderer>(true).Length;
                }
                else if (transform.name.StartsWith("BMWFixedCaliper", StringComparison.Ordinal))
                {
                    caliperCount++;
                }
            }
            if (wheelCount != 4 || caliperCount != 4 || rollingParts < 16)
                throw new InvalidOperationException(
                    $"BMW wheel assembly incomplete wheels={wheelCount} calipers={caliperCount} rollingParts={rollingParts}.");

            if (!centers.TryGetValue("BMWWheelFrontLeft", out var frontLeft) ||
                !centers.TryGetValue("BMWWheelFrontRight", out var frontRight) ||
                !centers.TryGetValue("BMWWheelRearLeft", out var rearLeft) ||
                !centers.TryGetValue("BMWWheelRearRight", out var rearRight))
                throw new InvalidOperationException("BMW wheel centers are missing.");
            var frontTrack = Math.Abs(frontRight.x - frontLeft.x);
            var rearTrack = Math.Abs(rearRight.x - rearLeft.x);
            var wheelbase = Math.Abs((frontLeft.z + frontRight.z - rearLeft.z - rearRight.z) * 0.5f);
            if (Math.Abs(frontTrack - FrontTrack) > 0.01f ||
                Math.Abs(rearTrack - RearTrack) > 0.01f ||
                Math.Abs(wheelbase - TargetWheelbase) > 0.01f)
                throw new InvalidOperationException(
                    $"BMW wheel geometry mismatch wheelbase={wheelbase:F3} tracks={frontTrack:F3}/{rearTrack:F3}.");

            var bodyCollider = FindTransform(prefab.transform, "BodyCollider") ??
                               throw new InvalidOperationException("BMW body collider holder is missing.");
            if (bodyCollider.GetComponents<BoxCollider>().Length < 2)
                throw new InvalidOperationException("BMW body colliders are incomplete.");
            if (FindTransform(prefab.transform, "BMWDamageBody") == null ||
                FindTransform(prefab.transform, "BMW_DRL_Source") == null ||
                FindTransform(prefab.transform, "BMW_Lamp_Source") == null ||
                FindTransform(prefab.transform, "BMW_RearLamp_Source") == null ||
                FindTransform(prefab.transform, "Steering_wheel") == null)
                throw new InvalidOperationException("BMW damage, lighting, or driver anchors are incomplete.");

            var materialResult = BMWM4G82Materials.FixSolidMaterials(prefab);
            if (materialResult.CabinGlassRenderers < 2 || materialResult.TransparentMaterialsFixed < 2)
                throw new InvalidOperationException(
                    $"BMW glass validation failed renderers={materialResult.CabinGlassRenderers} transparent={materialResult.TransparentMaterialsFixed}.");
            if (Math.Abs(prefab.transform.localScale.x - 1f) > 0.001f ||
                Math.Abs(prefab.transform.localScale.y - 1f) > 0.001f ||
                Math.Abs(prefab.transform.localScale.z - 1f) > 0.001f)
                throw new InvalidOperationException($"BMW prefab root scale is not one: {prefab.transform.localScale}.");

            Debug.Log(
                $"BMWM4G82 bundle verified: price={price:F0}, fuel={maxFuel:F0}L, cargo={maxCargo:F0}, " +
                $"speed={maxSpeed:F0}km/h, power={enginePower:F0}kW, bounds={bounds.size}, " +
                $"wheelbase={wheelbase:F3}, tracks={frontTrack:F3}/{rearTrack:F3}, " +
                $"wheels={wheelCount}, fixedCalipers={caliperCount}, rollingParts={rollingParts}, " +
                $"glass={materialResult.CabinGlassRenderers}, materials={materialResult.RendererCount}.");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static UnityEngine.Object CreateVehicleType()
    {
        var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ReferenceAssetPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R VehicleType reference asset was not found.");

        var target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        if (target == null)
        {
            if (!AssetDatabase.CopyAsset(ReferenceAssetPath, VehicleAssetPath))
                throw new InvalidOperationException("Could not create the BMW VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated BMW VehicleType asset did not load.");

        target.name = "BMWM4G82";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 79795f);
        SetNumber(serialized, "maxFuel", 59f);
        SetNumber(serialized, "maxCargoCapacity", 12f);
        SetNumber(serialized, "maxSpeed", 290f);
        SetNumber(serialized, "enginePower", 375f);
        SetNumber(serialized, "brakeForce", 22000f);
        SetNumber(serialized, "turnRadius", 25f);
        SetNumber(serialized, "damageIntensity", 0.50f);
        SetBool(serialized, "isATruck", false);
        SetBool(serialized, "isHandVehicle", false);
        SetBool(serialized, "fitsHandTruck", false);
        SetBool(serialized, "fitsFlatbed", false);
        SetBool(serialized, "autoParkSupported", true);
        SetBool(serialized, "hasRadio", true);
        SetBool(serialized, "isLuxuryCar", true);
        SetBool(serialized, "enclosed", true);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        return target;
    }

    private static void CreateVehiclePrefab(UnityEngine.Object vehicleType)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePrefabPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R reference prefab was not found.");
        if (model == null)
            throw new InvalidOperationException("BMW GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "BMWM4G82";
        try
        {
            StripAudiGeometry(root);
            RemoveAudiSpecificBehaviours(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigurePowertrain(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the BMW model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "BMWVisual";
            RemoveModelLights(modelInstance);
            RemoveStudioGeometry(modelInstance);
            NameKeyRenderers(modelInstance);
            NormalizeModel(modelInstance);
            CreateSteeringAnchor(root);
            ConfigureExitMarkers(root);
            AssignPersistentMaterials(modelInstance);
            AttachWheelVisuals(root, modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            var fix = BMWM4G82Materials.FixSolidMaterials(root);
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);

            Debug.Log(
                $"BMWM4G82: prepared decal-safe materials renderers={fix.RendererCount}, " +
                $"decalMasksCleared={fix.DecalMasksCleared}, " +
                $"opaqueFixed={fix.OpaqueMaterialsFixed}, " +
                $"transparentFixed={fix.TransparentMaterialsFixed}, " +
                $"cabinGlass={fix.CabinGlassRenderers}/" +
                $"reenabled={fix.CabinGlassRenderersReenabled}, " +
                $"rimMaterialsConfigured={rimMaterialsConfigured}, " +
                $"hdrpValidated={fix.MaterialsValidated}.");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the BMW vehicle prefab.");
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

    private static void RemoveAudiSpecificBehaviours(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component != null &&
                component.GetType().Name.StartsWith("AudiRS6R", StringComparison.Ordinal))
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }
    }

    private static void RemoveModelLights(GameObject model)
    {
        foreach (var light in model.GetComponentsInChildren<Light>(true))
            UnityEngine.Object.DestroyImmediate(light.gameObject);
    }

    private static void RemoveStudioGeometry(GameObject model)
    {
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            var material = renderer.sharedMaterial;
            if (material == null ||
                material.name.IndexOf("Material.002", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            renderer.enabled = false;
            renderer.sharedMaterials = Array.Empty<Material>();
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = null;
        }
    }

    private static void NameKeyRenderers(GameObject model)
    {
        var glassIndex = 0;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            var materialName = renderer.sharedMaterial?.name ?? string.Empty;
            if (materialName.IndexOf("PaintTNR", StringComparison.OrdinalIgnoreCase) >= 0)
                renderer.name = "BMW_Body_Source";
            else if (materialName.IndexOf("LightA", StringComparison.OrdinalIgnoreCase) >= 0)
                renderer.name = "BMW_Lamp_Source";
            else if (string.Equals(materialName, "emit", StringComparison.OrdinalIgnoreCase))
                renderer.name = "BMW_DRL_Source";
            else if (string.Equals(materialName, "red_glass", StringComparison.OrdinalIgnoreCase))
                renderer.name = "BMW_RearLamp_Source";
            else if (materialName.IndexOf("glasswindshiled", StringComparison.OrdinalIgnoreCase) >= 0)
                renderer.name = $"BMW_CabinGlass_{glassIndex++}";
            else if (string.Equals(materialName, "phong2", StringComparison.OrdinalIgnoreCase))
                renderer.name = "BMW_SteeringVisual_Source";
        }

        if (FindTransform(model.transform, "BMW_Body_Source") == null ||
            FindTransform(model.transform, "BMW_Lamp_Source") == null ||
            FindTransform(model.transform, "BMW_DRL_Source") == null ||
            FindTransform(model.transform, "BMW_RearLamp_Source") == null)
        {
            throw new InvalidOperationException(
                "BMW model material classification could not locate body and lamp renderers.");
        }
    }

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>() ??
                   throw new InvalidOperationException("Reference prefab has no Rigidbody.");
        body.mass = 1775f;
        body.drag = 0f;
        body.angularDrag = 1.45f;
        body.centerOfMass = StableCenterOfMass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var configuredCenter = serialized.FindProperty("centerOfMass");
            var useDefaultCenter = serialized.FindProperty("useDefaultCenterOfMass");
            if (configuredCenter?.propertyType != SerializedPropertyType.Vector3 ||
                useDefaultCenter?.propertyType != SerializedPropertyType.Boolean)
            {
                continue;
            }

            useDefaultCenter.boolValue = false;
            configuredCenter.vector3Value = StableCenterOfMass;
            var combinedCenter = serialized.FindProperty("combinedCenterOfMass");
            if (combinedCenter?.propertyType == SerializedPropertyType.Vector3)
                combinedCenter.vector3Value = StableCenterOfMass;
            SetNumber(serialized, "baseMass", 1775f);
            SetNumber(serialized, "combinedMass", 1775f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void ConfigureWheelControllers(GameObject root)
    {
        foreach (var pair in WheelControllerPositions)
        {
            var transform = FindTransform(root.transform, pair.Key) ??
                            throw new InvalidOperationException($"Wheel controller '{pair.Key}' is missing.");
            transform.localPosition = pair.Value;
            var isFront = pair.Key.StartsWith("Front", StringComparison.Ordinal);
            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                var serialized = new SerializedObject(component);
                SetRelativeNumber(
                    serialized,
                    "spring.maxLength",
                    isFront ? FrontSuspensionTravel : RearSuspensionTravel);
                SetRelativeNumber(serialized, "spring.maxForce", isFront ? 19500f : 18500f);
                SetRelativeNumber(serialized, "wheel.radius", isFront ? FrontTireRadius : RearTireRadius);
                SetRelativeNumber(serialized, "wheel.width", isFront ? FrontTireWidth : RearTireWidth);
                SetRelativeNumber(serialized, "frictionCircleStrength", TireFrictionCircleStrength);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    private static void ConfigureBodyColliders(GameObject root)
    {
        var holder = FindTransform(root.transform, "BodyCollider") ??
                     throw new InvalidOperationException("Reference BodyCollider is missing.");
        var colliders = holder.GetComponents<BoxCollider>();
        if (colliders.Length < 2)
            throw new InvalidOperationException("Reference vehicle requires two body colliders.");

        colliders[0].center = new Vector3(0f, 0.36f, -0.02f);
        colliders[0].size = new Vector3(1.82f, 0.48f, 4.58f);
        colliders[1].center = new Vector3(0f, 0.86f, -0.18f);
        colliders[1].size = new Vector3(1.62f, 0.76f, 2.68f);
    }

    private static void CreateSteeringAnchor(GameObject root)
    {
        var steeringAnchor = new GameObject("Steering_wheel");
        steeringAnchor.transform.SetParent(root.transform, false);
        steeringAnchor.transform.localPosition = SteeringAnchorPosition;
        Debug.Log(
            $"BMWM4G82: fitted left-hand-drive steering anchor at " +
            $"{SteeringAnchorPosition} from the G82 cabin proportions.");
    }

    private static void ConfigureExitMarkers(GameObject root)
    {
        var steeringWheel = FindTransform(root.transform, "Steering_wheel") ??
                            throw new InvalidOperationException("Fitted steering-wheel anchor is missing.");
        var steeringPosition = steeringWheel.localPosition;
        var driverSide = steeringPosition.x < 0f ? -1.45f : 1.45f;
        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.1f, 0f));
        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.1f, 0f));
        Debug.Log(
            $"BMWM4G82: steering wheel x={steeringPosition.x:F3}; " +
            $"driver exit x={driverSide:F2}.");
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

    private static void ConfigurePowertrain(GameObject root)
    {
        var found = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            if (string.Equals(
                    component.GetType().FullName,
                    "NWH.VehiclePhysics2.VehicleController",
                    StringComparison.Ordinal))
            {
                found = true;
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1150f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 450f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 500f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.14f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", 375f);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateM4PowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 800f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 7200f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.55f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", true);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0.28f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.154f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 8f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.11f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 2400f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 7000f);
                SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);

                var wheelGroups = FindRelativeProperty(serialized, "powertrain.wheelGroups");
                if (wheelGroups == null || !wheelGroups.isArray || wheelGroups.arraySize != 2)
                    throw new InvalidOperationException("Reference wheel groups are missing.");
                for (var index = 0; index < wheelGroups.arraySize; index++)
                {
                    var antiRoll = wheelGroups.GetArrayElementAtIndex(index)
                        .FindPropertyRelative("antiRollBarForce");
                    if (antiRoll?.propertyType != SerializedPropertyType.Float)
                        throw new InvalidOperationException("Wheel group anti-roll setting is missing.");
                    antiRoll.floatValue = AntiRollBarForce;
                }

                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("Reference transmission gear array is missing.");
                gears.arraySize = M4Gears.Length;
                for (var index = 0; index < M4Gears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = M4Gears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 290f);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        if (!found)
            throw new InvalidOperationException("NWH vehicle controller was not found on the reference prefab.");
    }

    private static void NormalizeModel(GameObject model)
    {
        model.transform.localPosition = Vector3.zero;
        // The supplied Sketchfab GLB uses Y for length and Z for height.
        model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        model.transform.localScale = Vector3.one;

        if (!TryGetModelBodyBounds(model.transform, out var bounds))
            throw new InvalidOperationException("BMW model contains no renderers.");

        var headlights = FindTransform(model.transform, "BMW_DRL_Source");
        var tailLights = FindTransform(model.transform, "BMW_RearLamp_Source");
        if (headlights != null && tailLights != null &&
            TryGetRendererBounds(headlights, out var headBounds) &&
            TryGetRendererBounds(tailLights, out var tailBounds) &&
            headBounds.center.z < tailBounds.center.z)
        {
            // Yaw around the chassis/parent up axis. Rotating in self space here
            // would turn around the GLB's authored longitudinal axis instead.
            model.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
        }

        if (!TryGetModelBodyBounds(model.transform, out bounds) || bounds.size.z <= 0.001f)
            throw new InvalidOperationException("BMW model length could not be measured.");

        var scale = new Vector3(
            TargetWidth / bounds.size.x,
            TargetLength / bounds.size.z,
            TargetHeight / bounds.size.y);
        model.transform.localScale = scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled BMW bounds could not be measured.");

        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final BMW bounds could not be measured.");

        Debug.Log(
            $"BMWM4G82: normalized supplied GLB scale={scale}, " +
            $"bounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var sources = new List<MeshRenderer>();
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            if (IsWheelSourceMaterial(renderer.sharedMaterial))
            {
                sources.Add(renderer);
                Debug.Log($"BMWM4G82 wheel source renderer='{renderer.name}' material='{renderer.sharedMaterial.name}'.");
            }
        if (sources.Count < 5)
            throw new InvalidOperationException($"Expected the BMW wheel source renderers, found {sources.Count}.");

        if (!TryGetWheelRegionBounds(root.transform, sources, true, true, out var frontLeftBounds) ||
            !TryGetWheelRegionBounds(root.transform, sources, false, true, out var rearLeftBounds))
        {
            throw new InvalidOperationException("BMW tire regions could not be measured.");
        }

        var axleMidpoint = (frontLeftBounds.center.z + rearLeftBounds.center.z) * 0.5f;
        var corners = new[]
        {
            new WheelCorner("FrontLeft", "FrontLeft_WheelController", true, true,
                new Vector3(-FrontTrack * 0.5f, FrontTireRadius, axleMidpoint + TargetWheelbase * 0.5f),
                FrontTireWidth, FrontTireRadius),
            new WheelCorner("FrontRight", "FrontRight_WheelController", true, false,
                new Vector3(FrontTrack * 0.5f, FrontTireRadius, axleMidpoint + TargetWheelbase * 0.5f),
                FrontTireWidth, FrontTireRadius),
            new WheelCorner("RearLeft", "RearLeft_WheelController", false, true,
                new Vector3(-RearTrack * 0.5f, RearTireRadius, axleMidpoint - TargetWheelbase * 0.5f),
                RearTireWidth, RearTireRadius),
            new WheelCorner("RearRight", "RearRight_WheelController", false, false,
                new Vector3(RearTrack * 0.5f, RearTireRadius, axleMidpoint - TargetWheelbase * 0.5f),
                RearTireWidth, RearTireRadius),
        };

        foreach (var corner in corners)
            CreateWheelCorner(root, sources, corner);

        foreach (var renderer in sources)
        {
            renderer.enabled = false;
            renderer.sharedMaterials = Array.Empty<Material>();
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = null;
        }
    }

    private readonly struct WheelCorner
    {
        public WheelCorner(string suffix, string controllerName, bool front, bool left,
            Vector3 position, float width, float radius)
        {
            Suffix = suffix;
            ControllerName = controllerName;
            Front = front;
            Left = left;
            Position = position;
            Width = width;
            Radius = radius;
        }

        public string Suffix { get; }
        public string ControllerName { get; }
        public bool Front { get; }
        public bool Left { get; }
        public Vector3 Position { get; }
        public float Width { get; }
        public float Radius { get; }
    }

    private static void CreateWheelCorner(GameObject root, List<MeshRenderer> sources, WheelCorner corner)
    {
        var controller = FindTransform(root.transform, corner.ControllerName) ??
                         throw new InvalidOperationException($"Wheel controller '{corner.ControllerName}' is missing.");
        controller.localPosition = corner.Position;

        var mount = new GameObject("BMWWheel" + corner.Suffix);
        mount.transform.SetParent(root.transform, false);
        mount.transform.localPosition = corner.Position;
        var geometry = new GameObject("Geometry_" + corner.Suffix);
        geometry.transform.SetParent(root.transform, false);

        MeshRenderer? tireRenderer = null;
        var rollingParts = 0;
        foreach (var source in sources)
        {
            if (!IsRollingWheelMaterial(source.sharedMaterial))
                continue;
            var created = CreateFilteredWheelPart(root, geometry, source, corner, false);
            if (created == null)
                continue;
            rollingParts++;
            if (IsTireMaterial(source.sharedMaterial))
                tireRenderer = created;
        }
        if (tireRenderer == null || rollingParts < 4)
            throw new InvalidOperationException(
                $"BMW {corner.Suffix} rolling assembly is incomplete ({rollingParts} parts).");

        geometry.transform.SetParent(mount.transform, true);
        if (!TryGetRendererBounds(tireRenderer.transform, out var tireBounds))
            throw new InvalidOperationException($"BMW {corner.Suffix} tire bounds are missing.");
        mount.transform.localScale = new Vector3(
            corner.Width / tireBounds.size.x,
            (corner.Radius * 2f) / tireBounds.size.y,
            (corner.Radius * 2f) / tireBounds.size.z);
        if (!TryGetRendererBounds(tireRenderer.transform, out tireBounds))
            throw new InvalidOperationException($"BMW {corner.Suffix} fitted tire bounds are missing.");
        var alignmentDelta = mount.transform.position - tireBounds.center;
        geometry.transform.position += alignmentDelta;

        var fixedMount = new GameObject("BMWFixedCaliper" + corner.Suffix);
        fixedMount.transform.SetParent(root.transform, false);
        fixedMount.transform.localPosition = corner.Position;
        fixedMount.transform.localScale = mount.transform.localScale;
        var caliperGeometry = new GameObject("BMWCaliperGeometry_" + corner.Suffix);
        caliperGeometry.transform.SetParent(root.transform, false);
        var caliper = default(MeshRenderer);
        foreach (var source in sources)
        {
            if (!IsCaliperMaterial(source.sharedMaterial))
                continue;
            caliper = CreateFilteredWheelPart(root, caliperGeometry, source, corner, true);
            if (caliper != null)
                break;
        }
        if (caliper == null)
            throw new InvalidOperationException($"BMW {corner.Suffix} caliper geometry is missing.");
        caliperGeometry.transform.SetParent(fixedMount.transform, true);
        caliperGeometry.transform.position += alignmentDelta;

        AssignWheelVisual(controller, mount);
        Debug.Log(
            $"BMWM4G82: fitted {corner.Suffix} center={corner.Position} " +
            $"tire={corner.Width:F3}x{corner.Radius * 2f:F3}m rollingParts={rollingParts}.");
    }

    private static MeshRenderer? CreateFilteredWheelPart(GameObject root, GameObject holder,
        MeshRenderer source, WheelCorner corner, bool caliper)
    {
        var filter = source.GetComponent<MeshFilter>();
        if (filter?.sharedMesh == null)
            return null;
        var mesh = CreateFilteredRootSpaceMesh(root.transform, source, vertex =>
            (corner.Left ? vertex.x < 0f : vertex.x >= 0f) &&
            (corner.Front ? vertex.z >= 0f : vertex.z < 0f));
        if (mesh == null)
            return null;

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");
        var materialMarker = SanitizeAssetName(source.sharedMaterial?.name ?? source.name);
        var path = $"{MeshFolder}/BMW_{corner.Suffix}_{materialMarker}.asset";
        var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (persistent == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            persistent = mesh;
        }
        else
        {
            EditorUtility.CopySerialized(mesh, persistent);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(persistent);
        }

        var part = new GameObject((caliper ? "BMW_Caliper_" : "BMW_WheelPart_") +
                                  corner.Suffix + "_" + materialMarker);
        part.transform.SetParent(holder.transform, false);
        part.AddComponent<MeshFilter>().sharedMesh = persistent;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = source.sharedMaterials;
        renderer.shadowCastingMode = source.shadowCastingMode;
        renderer.receiveShadows = source.receiveShadows;
        renderer.lightProbeUsage = source.lightProbeUsage;
        renderer.reflectionProbeUsage = source.reflectionProbeUsage;
        renderer.renderingLayerMask = source.renderingLayerMask;
        return renderer;
    }

    private static Mesh? CreateFilteredRootSpaceMesh(Transform root, MeshRenderer source,
        Func<Vector3, bool> includeVertex)
    {
        var sourceMesh = source.GetComponent<MeshFilter>()?.sharedMesh;
        if (sourceMesh == null)
            return null;
        var sourceVertices = sourceMesh.vertices;
        var vertices = new Vector3[sourceVertices.Length];
        var sourceToRoot = root.worldToLocalMatrix * source.transform.localToWorldMatrix;
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = sourceToRoot.MultiplyPoint3x4(sourceVertices[index]);

        var selectedBySubMesh = new List<int>[sourceMesh.subMeshCount];
        var selectedVertices = new HashSet<int>();
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var selected = new List<int>();
            var triangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var a = triangles[index];
                var b = triangles[index + 1];
                var c = triangles[index + 2];
                if (!includeVertex((vertices[a] + vertices[b] + vertices[c]) / 3f))
                    continue;
                selected.Add(a);
                selected.Add(b);
                selected.Add(c);
                selectedVertices.Add(a);
                selectedVertices.Add(b);
                selectedVertices.Add(c);
            }
            selectedBySubMesh[subMesh] = selected;
        }
        if (selectedVertices.Count == 0)
            return null;

        var mesh = UnityEngine.Object.Instantiate(sourceMesh);
        mesh.name = sourceMesh.name + "_BMWCorner";
        mesh.vertices = vertices;
        var normals = mesh.normals;
        if (normals.Length == vertices.Length)
        {
            var normalMatrix = sourceToRoot.inverse.transpose;
            for (var index = 0; index < normals.Length; index++)
                normals[index] = normalMatrix.MultiplyVector(normals[index]).normalized;
            mesh.normals = normals;
        }
        var tangents = mesh.tangents;
        if (tangents.Length == vertices.Length)
        {
            for (var index = 0; index < tangents.Length; index++)
            {
                var tangent = tangents[index];
                var direction = sourceToRoot.MultiplyVector(
                    new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                tangents[index] = new Vector4(direction.x, direction.y, direction.z, tangent.w);
            }
            mesh.tangents = tangents;
        }
        mesh.subMeshCount = sourceMesh.subMeshCount;
        for (var subMesh = 0; subMesh < selectedBySubMesh.Length; subMesh++)
            mesh.SetTriangles(selectedBySubMesh[subMesh], subMesh, false);
        using (var enumerator = selectedVertices.GetEnumerator())
        {
            enumerator.MoveNext();
            var bounds = new Bounds(vertices[enumerator.Current], Vector3.zero);
            while (enumerator.MoveNext())
                bounds.Encapsulate(vertices[enumerator.Current]);
            mesh.bounds = bounds;
        }
        mesh.UploadMeshData(false);
        return mesh;
    }

    private static bool TryGetWheelRegionBounds(Transform root, List<MeshRenderer> sources,
        bool front, bool left, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var source in sources)
        {
            if (!IsTireMaterial(source.sharedMaterial))
                continue;
            var mesh = source.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null)
                continue;
            foreach (var vertex in mesh.vertices)
            {
                var point = root.InverseTransformPoint(source.transform.TransformPoint(vertex));
                if ((left ? point.x >= 0f : point.x < 0f) ||
                    (front ? point.z < 0f : point.z >= 0f))
                    continue;
                if (!found)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(point);
                }
            }
        }
        return found;
    }

    private static bool IsWheelSourceMaterial(Material? material)
    {
        var name = material?.name ?? string.Empty;
        return name.IndexOf("sidetyre", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("disk.001", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("disk_001", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("metalblack", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.EndsWith("_main", StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf("Material.001", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Material_001", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsRollingWheelMaterial(Material? material) =>
        IsWheelSourceMaterial(material) && !IsCaliperMaterial(material!);

    private static bool IsTireMaterial(Material? material) =>
        material?.name.IndexOf("sidetyre", StringComparison.OrdinalIgnoreCase) >= 0;

    private static void AssignWheelVisual(Transform controller, GameObject visualObject)
    {
        foreach (var component in controller.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var wheel = serialized.FindProperty("wheel");
            var visual = wheel?.FindPropertyRelative("visual");
            if (visual?.propertyType != SerializedPropertyType.ObjectReference)
                continue;
            visual.objectReferenceValue = visualObject;
            var visualTransform = wheel?.FindPropertyRelative("visualTransform");
            if (visualTransform?.propertyType == SerializedPropertyType.ObjectReference)
                visualTransform.objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return;
        }

        throw new InvalidOperationException($"Wheel controller '{controller.name}' has no visual property.");
    }

    private static void ConfigureRendererReferences(GameObject root)
    {
        var renderers = new List<Renderer>();
        var paintRenderers = new List<Renderer>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || renderer.sharedMaterials.Length == 0)
                continue;
            renderers.Add(renderer);
            foreach (var material in renderer.sharedMaterials)
                if (material != null && IsBodyPaintMaterial(material))
                {
                    paintRenderers.Add(renderer);
                    break;
                }
        }

        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            AssignRendererArray(serialized.FindProperty("bodyMeshes"), paintRenderers);
            AssignRendererArray(serialized.FindProperty("renderers"), renderers);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static MeshFilter CreateDeformableBody(GameObject root, GameObject modelInstance)
    {
        MeshRenderer? sourceRenderer = null;
        foreach (var renderer in modelInstance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (string.Equals(renderer.name, "BMW_Body_Source", StringComparison.Ordinal))
            {
                sourceRenderer = renderer;
                break;
            }
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException("The BMW outer body mesh was not found.");

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");

        var bakedMesh = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
        bakedMesh.name = "BMWDamageBody";
        var sourceToRoot = root.transform.worldToLocalMatrix * sourceFilter.transform.localToWorldMatrix;

        var vertices = bakedMesh.vertices;
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = sourceToRoot.MultiplyPoint3x4(vertices[index]);
        bakedMesh.vertices = vertices;

        var normals = bakedMesh.normals;
        if (normals.Length == vertices.Length)
        {
            var normalMatrix = sourceToRoot.inverse.transpose;
            for (var index = 0; index < normals.Length; index++)
                normals[index] = normalMatrix.MultiplyVector(normals[index]).normalized;
            bakedMesh.normals = normals;
        }

        var tangents = bakedMesh.tangents;
        if (tangents.Length == vertices.Length)
        {
            for (var index = 0; index < tangents.Length; index++)
            {
                var tangent = tangents[index];
                var direction = sourceToRoot.MultiplyVector(
                    new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                tangents[index] = new Vector4(direction.x, direction.y, direction.z, tangent.w);
            }
            bakedMesh.tangents = tangents;
        }
        bakedMesh.RecalculateBounds();
        bakedMesh.UploadMeshData(false);

        var persistentMesh = AssetDatabase.LoadAssetAtPath<Mesh>(DamageBodyMeshPath);
        if (persistentMesh == null)
        {
            AssetDatabase.CreateAsset(bakedMesh, DamageBodyMeshPath);
            persistentMesh = bakedMesh;
        }
        else
        {
            EditorUtility.CopySerialized(bakedMesh, persistentMesh);
            UnityEngine.Object.DestroyImmediate(bakedMesh);
            EditorUtility.SetDirty(persistentMesh);
        }

        var damageBody = new GameObject("BMWDamageBody")
        {
            layer = sourceRenderer.gameObject.layer,
        };
        damageBody.transform.SetParent(root.transform, false);
        var damageFilter = damageBody.AddComponent<MeshFilter>();
        damageFilter.sharedMesh = persistentMesh;
        var damageRenderer = damageBody.AddComponent<MeshRenderer>();
        damageRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        damageRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        damageRenderer.receiveShadows = sourceRenderer.receiveShadows;
        damageRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        damageRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        damageRenderer.motionVectorGenerationMode = sourceRenderer.motionVectorGenerationMode;
        damageRenderer.allowOcclusionWhenDynamic = sourceRenderer.allowOcclusionWhenDynamic;
        damageRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;

        sourceRenderer.enabled = false;
        sourceRenderer.sharedMaterials = Array.Empty<Material>();
        return damageFilter;
    }

    private static void ConfigureVehicleDeformation(GameObject root, MeshFilter bodyFilter)
    {
        var configured = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null ||
                !string.Equals(
                    component.GetType().Name,
                    "VehicleDeformationController",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var serialized = new SerializedObject(component);
            var meshFilters = serialized.FindProperty("meshFilters");
            if (meshFilters == null || !meshFilters.isArray)
                throw new InvalidOperationException("Vehicle deformation mesh list is missing.");
            meshFilters.arraySize = 1;
            meshFilters.GetArrayElementAtIndex(0).objectReferenceValue = bodyFilter;
            var originals = serialized.FindProperty("originalMeshes");
            if (originals != null && originals.isArray)
                originals.ClearArray();
            SetNumber(serialized, "deformationStrength", DeformationStrength);
            SetNumber(serialized, "deformationRadius", DeformationRadius);
            SetNumber(serialized, "deformationRandomness", DeformationRandomness);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            configured = true;
        }

        if (!configured)
            throw new InvalidOperationException("Vehicle deformation controller is missing.");
    }

    private static bool IsBodyPaintMaterial(Material material) =>
        material.name.IndexOf("PaintTNR", StringComparison.OrdinalIgnoreCase) >= 0 ||
        material.name.IndexOf("Coloured", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorAccentPaintMaterial(Material material) =>
        material.name.IndexOf("InteriorA", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRimMaterial(Material material) =>
        material.name.EndsWith("_main", StringComparison.OrdinalIgnoreCase);

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!BMWM4G82Materials.IsBMWRenderer(renderer.transform))
                continue;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material != null && IsRimMaterial(material) &&
                    material.name.IndexOf("_Right", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    leftMaterial = material;
                    break;
                }
            }
            if (leftMaterial != null)
                break;
        }

        if (leftMaterial == null)
            throw new InvalidOperationException("The shared BMW rim material is missing.");

        ApplyRimFinish(leftMaterial, BMWM4G82Materials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!BMWM4G82Materials.IsBMWRenderer(renderer.transform))
                continue;
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var index = 0; index < materials.Length; index++)
            {
                if (materials[index] == null || !IsRimMaterial(materials[index]))
                    continue;
                materials[index] = leftMaterial;
                renderer.SetPropertyBlock(null, index);
                configuredSlots++;
                changed = true;
            }
            if (changed)
                renderer.sharedMaterials = materials;
        }

        if (configuredSlots != 4)
            throw new InvalidOperationException(
                $"Expected four BMW rim slots, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", BMWM4G82Materials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", BMWM4G82Materials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("Material.001", StringComparison.OrdinalIgnoreCase) >= 0 ||
        material.name.IndexOf("Material_001", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static bool M4PowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateM4PowerCurve();
        if (curve == null || curve.length != expected.length)
            return false;
        for (var index = 0; index < expected.length; index++)
        {
            if (Math.Abs(curve[index].time - expected[index].time) > 0.002f ||
                Math.Abs(curve[index].value - expected[index].value) > 0.002f)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsRimInnerPaintMaterial(Material material) =>
        false;

    private static bool IsSeatPaintMaterial(Material material) =>
        false;

    private static bool IsBaseTextureReadable(Material material)
    {
        Texture? texture = null;
        if (material.HasProperty("_BaseColorMap"))
            texture = material.GetTexture("_BaseColorMap");
        if (texture == null && material.HasProperty("_MainTex"))
            texture = material.GetTexture("_MainTex");
        return texture is Texture2D texture2D && texture2D.isReadable;
    }

    private static bool IsDarkBodyPaintMaterial(Material material) =>
        material.name.IndexOf("Carbon1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorPrimaryPaintMaterial(Material material) =>
        material.name.IndexOf("InteriorA", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorSecondaryPaintMaterial(Material material) =>
        material.name.IndexOf("phong2", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorDarkPaintMaterial(Material material) =>
        material.name.IndexOf("InteriorA", StringComparison.OrdinalIgnoreCase) >= 0;

    private static void AssignPersistentMaterials(GameObject model)
    {
        EnsureAssetFolder(MaterialFolder);
        var replacements = new Dictionary<Material, Material>();
        var opaqueMaterialIndex = 0;
        var transparentMaterialIndex = 0;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                    continue;

                if (!replacements.TryGetValue(source, out var persistent))
                {
                    var transparent = BMWM4G82Materials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var assetName =
                        $"BMW{kind}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
                    var path = $"{MaterialFolder}/{assetName}.mat";
                    persistent = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (persistent == null)
                    {
                        persistent = new Material(source) { name = assetName };
                        AssetDatabase.CreateAsset(persistent, path);
                    }
                    else
                    {
                        persistent.CopyPropertiesFromMaterial(source);
                        persistent.shader = source.shader;
                        persistent.name = assetName;
                    }

                    replacements.Add(source, persistent);
                }

                materials[index] = persistent;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    private static void MarkMaterialsDirty(GameObject model)
    {
        var materials = new HashSet<Material>();
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material != null && materials.Add(material))
                {
                    EditorUtility.SetDirty(material);
                }
            }
        }
    }

    private static void CreateManifest()
    {
        var manifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(ManifestPath);
        if (manifest == null)
        {
            manifest = ScriptableObject.CreateInstance<BAModManifest>();
            AssetDatabase.CreateAsset(manifest, ManifestPath);
        }

        manifest.ModId = "BMWM4G82";
        manifest.DisplayName = "BMW M4 G82";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "bmw_m4_g82.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("BMW manifest references could not be assigned.");
        EditorUtility.SetDirty(manifest);
    }

    private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (var index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return true;
    }

    private static bool TryGetModelBodyBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var current = renderer.transform;
            var belongsToSourceWheel = false;
            while (current != null && current != root)
            {
                if (current.name.StartsWith("Circle.001", StringComparison.Ordinal) ||
                    current.name.StartsWith("Circle.004", StringComparison.Ordinal))
                {
                    belongsToSourceWheel = true;
                    break;
                }
                current = current.parent;
            }

            if (belongsToSourceWheel)
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private static bool TryGetBMWRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!BMWM4G82Materials.IsBMWRenderer(renderer.transform))
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        }

        return null;
    }

    private static int CountTransformsNamed(Transform root, string name)
    {
        var count = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal)) count++;
        return count;
    }

    private static Transform? FindTransformWithNameFragment(Transform root, string fragment)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return transform;
        }

        return null;
    }

    private static void SetLocalPosition(GameObject root, string name, Vector3 value)
    {
        var transform = FindTransform(root.transform, name) ??
                        throw new InvalidOperationException($"Reference transform '{name}' is missing.");
        transform.localPosition = value;
    }

    private static void AssignRendererArray(
        SerializedProperty? property,
        List<Renderer> renderers)
    {
        if (property == null || !property.isArray ||
            property.propertyType == SerializedPropertyType.String)
        {
            return;
        }

        property.arraySize = renderers.Count;
        for (var index = 0; index < renderers.Count; index++)
        {
            var element = property.GetArrayElementAtIndex(index);
            if (element.propertyType == SerializedPropertyType.ObjectReference)
                element.objectReferenceValue = renderers[index];
        }
    }

    private static SerializedProperty? FindRelativeProperty(
        SerializedObject serialized,
        string path)
    {
        var parts = path.Split('.');
        var property = serialized.FindProperty(parts[0]);
        for (var index = 1; index < parts.Length && property != null; index++)
            property = property.FindPropertyRelative(parts[index]);
        return property;
    }

    private static void SetRelativeNumber(
        SerializedObject serialized,
        string path,
        float value)
    {
        var property = FindRelativeProperty(serialized, path);
        if (property == null)
            return;
        if (property.propertyType == SerializedPropertyType.Integer)
            property.intValue = Mathf.RoundToInt(value);
        else if (property.propertyType == SerializedPropertyType.Float)
            property.floatValue = value;
        else if (property.propertyType == SerializedPropertyType.Enum)
            property.enumValueIndex = Mathf.RoundToInt(value);
    }

    private static void SetRelativeBool(
        SerializedObject serialized,
        string path,
        bool value)
    {
        var property = FindRelativeProperty(serialized, path);
        if (property?.propertyType == SerializedPropertyType.Boolean)
            property.boolValue = value;
    }

    private static void SetString(SerializedObject serialized, string name, string value)
    {
        var property = serialized.FindProperty(name);
        if (property?.propertyType == SerializedPropertyType.String)
            property.stringValue = value;
    }

    private static void SetBool(SerializedObject serialized, string name, bool value)
    {
        var property = serialized.FindProperty(name);
        if (property?.propertyType == SerializedPropertyType.Boolean)
            property.boolValue = value;
    }

    private static void SetNumber(SerializedObject serialized, string name, float value)
    {
        var property = serialized.FindProperty(name);
        if (property == null)
            return;
        if (property.propertyType == SerializedPropertyType.Integer)
            property.intValue = Mathf.RoundToInt(value);
        else if (property.propertyType == SerializedPropertyType.Float)
            property.floatValue = value;
    }

    private static float ReadNumber(SerializedProperty? property)
    {
        if (property == null)
            return 0f;
        if (property.propertyType == SerializedPropertyType.Integer)
            return property.intValue;
        return property.propertyType == SerializedPropertyType.Float
            ? property.floatValue
            : 0f;
    }

    private static void EnsureAssetFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;
        var separator = folder.LastIndexOf('/');
        if (separator <= 0)
            throw new InvalidOperationException($"Invalid asset folder '{folder}'.");
        var parent = folder.Substring(0, separator);
        EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(parent, folder.Substring(separator + 1));
    }

    private static string SanitizeAssetName(string value)
    {
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '-' && chars[index] != '_')
                chars[index] = '_';
        }

        return new string(chars);
    }
}
