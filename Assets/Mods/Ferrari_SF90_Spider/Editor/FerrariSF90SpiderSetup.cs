#nullable enable
using System;
using System.Collections.Generic;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class FerrariSF90SpiderSetup
{
    private const string ModRoot = "Assets/Mods/Ferrari_SF90_Spider";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/2021_ferrari_sf90_spider.glb";
    private const string LightOverlayModelPath = ModRoot + "/Models/FerrariSF90LightOverlays.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string VehicleAssetPath = ModRoot + "/FerrariSF90Spider.asset";
    private const string VehiclePrefabPath = ModRoot + "/FerrariSF90Spider.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/FerrariSF90Spider.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/ferrarisf90spider.unity3d";
    private const string VehicleTypeName =
        "ferrarisf90spider-vehicle:vehicletype_ferrarisf90spider";

    // Ferrari-published dimensions. The supplied model includes mirrors, while
    // Ferrari's 1.973 m width is body width without mirrors. Preserve the model's
    // authored proportions by fitting the visible mirror span separately.
    private const float TargetLength = 4.704f;
    private const float TargetVisualWidthIncludingMirrors = 2.22f;
    private const float TargetHeight = 1.191f;
    private const float Wheelbase = 2.649f;
    private const float FrontTrack = 1.679f;
    private const float RearTrack = 1.652f;
    private const float FrontWheelRadius = 0.34325f; // 255/35 ZR20
    private const float RearWheelRadius = 0.34850f;  // 315/30 ZR20
    private const float FrontWheelWidth = 0.255f;
    private const float RearWheelWidth = 0.315f;
    private const float BodyGroundClearance = 0.105f;

    private const float VehicleMass = 1670f;
    private const float PeriodSpiderMsrp = 558000f;
    private const float FuelCapacityLitres = 68f;
    private const float RatedSystemPowerKw = 735f;
    // NWH solver starting value, deliberately separate from the displayed real
    // system output. Final value must be calibrated against 2.5 s / 7.0 s targets.
    private const float RoadCalibrationPowerKw = 390f;
    private const float BrakeTorque = 3200f;
    private const float BrakeActuationTime = 0.08f;
    private const float FinalDriveRatio = 4.51f;
    private const float TireFrictionCircleStrength = 0.96f;
    private const float FrontLateralGrip = 0.98f;
    private const float RearLateralGrip = 0.96f;
    private const float AntiRollBarForce = 7200f;
    private const float FrontSuspensionTravel = 0.075f;
    private const float RearSuspensionTravel = 0.075f;
    private const float ExitMarkerOffset = 1.42f;

    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.09f, -0.13f);
    private static readonly Vector3 LowerColliderCenter = new Vector3(0f, 0.36f, 0f);
    private static readonly Vector3 LowerColliderSize = new Vector3(1.93f, 0.44f, 4.66f);
    private static readonly Vector3 UpperColliderCenter = new Vector3(0f, 0.73f, -0.16f);
    private static readonly Vector3 UpperColliderSize = new Vector3(1.68f, 0.58f, 2.46f);
    private static readonly Vector3 FrontContactColliderCenter = new Vector3(0f, 0.52f, 1.82f);
    private static readonly Vector3 FrontContactColliderSize = new Vector3(1.86f, 0.38f, 0.92f);
    private static readonly Vector3 SteeringReferencePosition = new Vector3(-0.37f, 0.80f, 0.39f);

    private const string WheelMaterialMarker = "Wheel1A_3D_3DWheel1C_Material";
    private const string CaliperMaterialMarker = "CallipersCalliperA_Zone_Material";
    private const string PaintMaterialMarker = "2021Paint_Material";
    private const string LightMaterialMarker = "LightA_Material";

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f, FrontWheelRadius, Wheelbase * 0.5f) },
            { "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f, FrontWheelRadius, Wheelbase * 0.5f) },
            { "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f, RearWheelRadius, -Wheelbase * 0.5f) },
            { "RearRight_WheelController", new Vector3(RearTrack * 0.5f, RearWheelRadius, -Wheelbase * 0.5f) },
        };

    // Exact Ferrari ratios are not published in the supplied source material.
    // These are simulation starting ratios for the real eight-speed DCT layout.
    private static readonly float[] SF90SpiderGears =
    {
        -3.45f,
        0f,
        3.45f,
        2.26f,
        1.65f,
        1.29f,
        1.03f,
        0.84f,
        0.67f,
        0.48f,
    };

    private static readonly (string SourceName, string RuntimeName)[] LightOverlayNames =
    {
        ("VehicleLightRef_DRL_FL", "FerrariSF90Spider_Light_DRL_FL"),
        ("VehicleLightRef_DRL_FR", "FerrariSF90Spider_Light_DRL_FR"),
        ("VehicleLightRef_FrontIndicatorSecondary_FL", "FerrariSF90Spider_Light_FrontIndicatorSecondary_FL"),
        ("VehicleLightRef_FrontIndicatorSecondary_FR", "FerrariSF90Spider_Light_FrontIndicatorSecondary_FR"),
        ("VehicleLightRef_FrontIndicator_FL", "FerrariSF90Spider_Light_FrontIndicator_FL"),
        ("VehicleLightRef_FrontIndicator_FR", "FerrariSF90Spider_Light_FrontIndicator_FR"),
        ("VehicleLightRef_Headlamps", "FerrariSF90Spider_Light_Headlamps"),
        ("VehicleLightRef_TailLights", "FerrariSF90Spider_Light_TailLights"),
        ("VehicleLightRef_BrakeLights", "FerrariSF90Spider_Light_BrakeLights"),
        ("VehicleLightRef_ThirdBrakeLight", "FerrariSF90Spider_Light_ThirdBrakeLight"),
        ("VehicleLightRef_ReverseLights", "FerrariSF90Spider_Light_ReverseLights"),
        ("VehicleLightRef_RearIndicator_RL", "FerrariSF90Spider_Light_RearIndicator_RL"),
        ("VehicleLightRef_RearIndicator_RR", "FerrariSF90Spider_Light_RearIndicator_RR"),
    };

    [MenuItem("Big Ambitions Mods/Setup Ferrari SF90 Spider")]
    public static void Generate()
    {
        EnsureAssetFolder(MaterialFolder);
        EnsureAssetFolder(MeshFolder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ValidateProjectAssets(false);
        Debug.Log(
            "FerrariSF90Spider setup complete: model normalized, four wheel assemblies and " +
            "four fixed calipers generated from the supplied GLB, eight-speed DCT baseline, " +
            "AWD reference drivetrain, paint/glass/audio/runtime hooks configured. " +
            "If FerrariSF90LightOverlays.glb is absent, functional lamp overlays remain the " +
            "only intentionally deferred visual step.");
    }

    [MenuItem("Big Ambitions Mods/Setup + Build Ferrari SF90 Spider")]
    public static void GenerateAndBuild()
    {
        Generate();
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    [MenuItem("Big Ambitions Mods/Validate Ferrari SF90 Spider")]
    public static void ValidateProject()
    {
        ValidateProjectAssets(true);
    }

    [MenuItem("Big Ambitions Mods/Verify Ferrari SF90 Spider Bundle")]
    public static void VerifyBuiltBundle()
    {
        var bundle = AssetBundle.LoadFromFile(WindowsBundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");
        try
        {
            var type = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (type == null || prefab == null)
                throw new InvalidOperationException("SF90 bundle is missing VehicleType or prefab.");
            ValidateVehicleType(type);
            ValidatePrefab(prefab, true);
            Debug.Log("FerrariSF90Spider bundle verified successfully.");
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
                throw new InvalidOperationException("Could not create the SF90 VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated SF90 VehicleType asset did not load.");

        target.name = "FerrariSF90Spider";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", PeriodSpiderMsrp);
        SetNumber(serialized, "maxFuel", FuelCapacityLitres);
        SetNumber(serialized, "maxCargoCapacity", 2f);
        SetNumber(serialized, "maxSpeed", 340f);
        SetNumber(serialized, "enginePower", RatedSystemPowerKw);
        SetNumber(serialized, "brakeForce", BrakeTorque);
        SetNumber(serialized, "turnRadius", 25f);
        SetNumber(serialized, "damageIntensity", 0.80f);
        SetBool(serialized, "isATruck", false);
        SetBool(serialized, "isHandVehicle", false);
        SetBool(serialized, "fitsHandTruck", false);
        SetBool(serialized, "fitsFlatbed", true);
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
            throw new InvalidOperationException($"SF90 GLB did not import at '{ModelPath}'.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "FerrariSF90Spider";
        try
        {
            StripReferenceGeometry(root);
            RemoveReferenceBehaviours(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigurePowertrain(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the SF90 model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "FerrariSF90SpiderVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureExitAndDriverMarkers(root);
            AssignPersistentMaterials(modelInstance);
            var overlays = CreateLightOverlayReferences(modelInstance);
            AttachWheelVisualsAndCalipers(root, modelInstance);

            var materialFix = FerrariSF90SpiderMaterials.FixSolidMaterials(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);
            ConfigureRuntimeBootstrap(root);

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the SF90 vehicle prefab.");

            Debug.Log(
                $"FerrariSF90Spider prefab prepared: renderers={materialFix.RendererCount}, " +
                $"opaqueFixed={materialFix.OpaqueMaterialsFixed}, " +
                $"transparentFixed={materialFix.TransparentMaterialsFixed}, " +
                $"cabinGlass={materialFix.CabinGlassRenderers}, lightOverlays={overlays}/13.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void StripReferenceGeometry(GameObject root)
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

    private static void RemoveReferenceBehaviours(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var name = component.GetType().Name;
            if (name.StartsWith("AudiRS6R", StringComparison.Ordinal) ||
                name.StartsWith("CadillacEscalade", StringComparison.Ordinal) ||
                name.StartsWith("KoenigseggJesko", StringComparison.Ordinal) ||
                name.StartsWith("LamborghiniRevuelto", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(component);
        }
    }

    private static void RemoveModelLights(GameObject model)
    {
        foreach (var light in model.GetComponentsInChildren<Light>(true))
            UnityEngine.Object.DestroyImmediate(light.gameObject);
    }

    private static void ConfigureRuntimeBootstrap(GameObject root)
    {
        if (root.GetComponent<FerrariSF90SpiderPaintController>() == null)
            root.AddComponent<FerrariSF90SpiderPaintController>();
    }

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>() ?? root.AddComponent<Rigidbody>();
        body.mass = VehicleMass;
        body.drag = 0f;
        body.angularDrag = 1.45f;
        body.centerOfMass = StableCenterOfMass;
    }

    private static void ConfigureWheelControllers(GameObject root)
    {
        foreach (var pair in WheelControllerPositions)
        {
            var transform = FindTransform(root.transform, pair.Key) ??
                            throw new InvalidOperationException($"Wheel controller '{pair.Key}' is missing.");
            transform.localPosition = pair.Value;
            var front = pair.Key.StartsWith("Front", StringComparison.Ordinal);
            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                    continue;
                var serialized = new SerializedObject(component);
                SetRelativeNumber(serialized, "spring.maxLength", front ? FrontSuspensionTravel : RearSuspensionTravel);
                SetRelativeNumber(serialized, "spring.maxForce", 20500f);
                SetRelativeNumber(serialized, "wheel.radius", front ? FrontWheelRadius : RearWheelRadius);
                SetRelativeNumber(serialized, "wheel.width", front ? FrontWheelWidth : RearWheelWidth);
                SetRelativeNumber(serialized, "sideFriction.grip", front ? FrontLateralGrip : RearLateralGrip);
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
            throw new InvalidOperationException("Reference vehicle requires two BodyCollider boxes.");
        colliders[0].center = LowerColliderCenter;
        colliders[0].size = LowerColliderSize;
        colliders[1].center = UpperColliderCenter;
        colliders[1].size = UpperColliderSize;
        var frontContact = colliders.Length > 2 ? colliders[2] : holder.gameObject.AddComponent<BoxCollider>();
        frontContact.center = FrontContactColliderCenter;
        frontContact.size = FrontContactColliderSize;
        frontContact.isTrigger = false;
        frontContact.enabled = true;
    }

    private static void ConfigureExitAndDriverMarkers(GameObject root)
    {
        SetLocalPosition(root, "Driverside", new Vector3(-ExitMarkerOffset, 0.10f, 0f));
        SetLocalPosition(root, "Passengerside", new Vector3(ExitMarkerOffset, 0.10f, 0f));

        var steering = FindTransform(root.transform, "STEERING_WHEEL");
        if (steering == null)
        {
            var marker = new GameObject("STEERING_WHEEL");
            marker.transform.SetParent(root.transform, false);
            steering = marker.transform;
        }
        steering.localPosition = SteeringReferencePosition;
        steering.localRotation = Quaternion.identity;
        steering.localScale = Vector3.one;

        // Keep the inherited vanilla steering reference coherent as well.
        var inherited = FindTransform(root.transform, "Animate_SteeringWheel_033");
        if (inherited != null)
            inherited.localPosition = SteeringReferencePosition;
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

    private static AnimationCurve CreatePowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.11f, 0.10f),
            new Keyframe(0.34f, 0.46f),
            new Keyframe(0.56f, 0.68f),
            new Keyframe(0.78f, 0.88f),
            new Keyframe(0.94f, 1f),
            new Keyframe(1f, 0.97f));

    private static void ConfigurePowertrain(GameObject root)
    {
        var found = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            if (string.Equals(component.GetType().FullName, "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
            {
                found = true;
                SetRelativeNumber(serialized, "brakes.maxTorque", BrakeTorque);
                SetRelativeNumber(serialized, "brakes.actuationTime", BrakeActuationTime);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1400f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 700f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 650f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.12f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", RoadCalibrationPowerKw);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType == SerializedPropertyType.AnimationCurve)
                    powerCurve.animationCurveValue = CreatePowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 900f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 8000f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.42f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", FinalDriveRatio);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 8f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.065f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 5000f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 7600f);
                SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);
                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("Reference transmission gear array is missing.");
                gears.arraySize = SF90SpiderGears.Length;
                for (var index = 0; index < SF90SpiderGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = SF90SpiderGears[index];

                var wheelGroups = FindRelativeProperty(serialized, "powertrain.wheelGroups");
                if (wheelGroups != null && wheelGroups.isArray)
                    for (var index = 0; index < wheelGroups.arraySize; index++)
                    {
                        var antiRoll = wheelGroups.GetArrayElementAtIndex(index).FindPropertyRelative("antiRollBarForce");
                        if (antiRoll?.propertyType == SerializedPropertyType.Float)
                            antiRoll.floatValue = AntiRollBarForce;
                    }
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeBool(serialized, "module.active", true);
                SetRelativeNumber(serialized, "module.speedLimit", 340f);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        if (!found)
            throw new InvalidOperationException("NWH VehicleController was not found on the reference prefab.");
    }

    private static void NormalizeModel(GameObject model)
    {
        model.transform.localPosition = Vector3.zero;
        model.transform.localScale = Vector3.one;
        if (!TryGetModelBodyBounds(model.transform, out var bounds))
            throw new InvalidOperationException("SF90 model body contains no usable renderers.");
        if (bounds.size.x <= 0.001f || bounds.size.y <= 0.001f || bounds.size.z <= 0.001f)
            throw new InvalidOperationException("SF90 imported body bounds are invalid.");

        var scale = new Vector3(
            TargetVisualWidthIncludingMirrors / bounds.size.x,
            (TargetHeight - BodyGroundClearance) / bounds.size.y,
            TargetLength / bounds.size.z);
        model.transform.localScale = scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled SF90 body bounds could not be measured.");
        model.transform.position += new Vector3(
            -bounds.center.x,
            BodyGroundClearance - bounds.min.y,
            -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final SF90 body bounds could not be measured.");

        Debug.Log($"FerrariSF90Spider model normalization: scale={scale}, bodyBounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisualsAndCalipers(GameObject root, GameObject model)
    {
        var wheelRenderers = new List<MeshRenderer>();
        var caliperRenderers = new List<MeshRenderer>();
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (HasMaterialMarker(renderer.sharedMaterials, WheelMaterialMarker))
                wheelRenderers.Add(renderer);
            else if (HasMaterialMarker(renderer.sharedMaterials, CaliperMaterialMarker))
                caliperRenderers.Add(renderer);
        }
        if (wheelRenderers.Count == 0)
            throw new InvalidOperationException("No SF90 wheel renderers were found in the supplied GLB.");
        if (caliperRenderers.Count == 0)
            throw new InvalidOperationException("No SF90 caliper renderers were found in the supplied GLB.");

        var sourceFrontLeft = FindTransform(model.transform, "3DWheel Front L");
        var sourceRearLeft = FindTransform(model.transform, "3DWheel Rear L");
        var sourceLeftSign = sourceFrontLeft != null
            ? Mathf.Sign(root.transform.InverseTransformPoint(sourceFrontLeft.position).x)
            : -1f;
        if (Mathf.Abs(sourceLeftSign) < 0.5f)
            sourceLeftSign = -1f;
        var frontZ = sourceFrontLeft != null
            ? root.transform.InverseTransformPoint(sourceFrontLeft.position).z
            : 1f;
        var rearZ = sourceRearLeft != null
            ? root.transform.InverseTransformPoint(sourceRearLeft.position).z
            : -1f;
        var axleMid = (frontZ + rearZ) * 0.5f;
        var frontPositive = frontZ >= rearZ;

        var wheelsByCorner = NewCornerMap();
        var calipersByCorner = NewCornerMap();
        foreach (var renderer in wheelRenderers)
            wheelsByCorner[ClassifyCorner(root.transform, renderer.bounds.center, sourceLeftSign, axleMid, frontPositive)].Add(renderer);
        foreach (var renderer in caliperRenderers)
            calipersByCorner[ClassifyCorner(root.transform, renderer.bounds.center, sourceLeftSign, axleMid, frontPositive)].Add(renderer);

        var toDestroy = new HashSet<GameObject>();
        foreach (var corner in new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" })
        {
            if (wheelsByCorner[corner].Count == 0)
                throw new InvalidOperationException($"SF90 source wheel cluster '{corner}' is empty.");
            if (calipersByCorner[corner].Count == 0)
                throw new InvalidOperationException($"SF90 source caliper cluster '{corner}' is empty.");

            var front = corner.StartsWith("Front", StringComparison.Ordinal);
            var controllerName = corner.Replace("Left", "Left_WheelController").Replace("Right", "Right_WheelController");
            var controller = FindTransform(root.transform, controllerName) ??
                             throw new InvalidOperationException($"Wheel controller '{controllerName}' is missing.");
            var targetPosition = WheelControllerPositions[controllerName];
            controller.localPosition = targetPosition;

            var sourceBounds = GetLocalBounds(root.transform, wheelsByCorner[corner]);
            var targetRadius = front ? FrontWheelRadius : RearWheelRadius;
            var targetWidth = front ? FrontWheelWidth : RearWheelWidth;
            var fit = new Vector3(
                targetWidth / Mathf.Max(0.0001f, sourceBounds.size.x),
                (targetRadius * 2f) / Mathf.Max(0.0001f, sourceBounds.size.y),
                (targetRadius * 2f) / Mathf.Max(0.0001f, sourceBounds.size.z));

            var wheelMount = new GameObject("FerrariSF90SpiderWheel" + corner);
            wheelMount.transform.SetParent(root.transform, false);
            wheelMount.transform.localPosition = targetPosition;
            var wheelMesh = BakeCombinedMesh(
                root.transform,
                wheelsByCorner[corner],
                sourceBounds.center,
                fit,
                $"{MeshFolder}/FerrariSF90SpiderWheel{corner}.asset",
                "FerrariSF90SpiderWheel" + corner);
            wheelMount.AddComponent<MeshFilter>().sharedMesh = wheelMesh;
            var wheelRenderer = wheelMount.AddComponent<MeshRenderer>();
            CopyRendererSettings(wheelsByCorner[corner][0], wheelRenderer);
            wheelRenderer.sharedMaterial = FirstMaterial(wheelsByCorner[corner]);
            AssignWheelVisual(controller, wheelMount);

            var fixedCaliper = new GameObject("FerrariSF90SpiderFixedCaliper" + corner);
            fixedCaliper.transform.SetParent(root.transform, false);
            fixedCaliper.transform.localPosition = targetPosition;
            var caliperGeometry = new GameObject("FerrariSF90SpiderCaliper" + corner);
            caliperGeometry.transform.SetParent(fixedCaliper.transform, false);
            var caliperMesh = BakeCombinedMesh(
                root.transform,
                calipersByCorner[corner],
                sourceBounds.center,
                fit,
                $"{MeshFolder}/FerrariSF90SpiderCaliper{corner}.asset",
                "FerrariSF90SpiderCaliper" + corner);
            caliperGeometry.AddComponent<MeshFilter>().sharedMesh = caliperMesh;
            var caliperRenderer = caliperGeometry.AddComponent<MeshRenderer>();
            CopyRendererSettings(calipersByCorner[corner][0], caliperRenderer);
            caliperRenderer.sharedMaterial = FirstMaterial(calipersByCorner[corner]);

            foreach (var renderer in wheelsByCorner[corner]) toDestroy.Add(renderer.gameObject);
            foreach (var renderer in calipersByCorner[corner]) toDestroy.Add(renderer.gameObject);

            Debug.Log(
                $"FerrariSF90Spider {corner}: sourceParts={wheelsByCorner[corner].Count}, " +
                $"caliperParts={calipersByCorner[corner].Count}, sourceBounds={sourceBounds.size}, " +
                $"fit={fit}, target={targetPosition}.");
        }

        foreach (var gameObject in toDestroy)
            if (gameObject != null)
                UnityEngine.Object.DestroyImmediate(gameObject);
    }

    private static Dictionary<string, List<MeshRenderer>> NewCornerMap() =>
        new Dictionary<string, List<MeshRenderer>>
        {
            { "FrontLeft", new List<MeshRenderer>() },
            { "FrontRight", new List<MeshRenderer>() },
            { "RearLeft", new List<MeshRenderer>() },
            { "RearRight", new List<MeshRenderer>() },
        };

    private static string ClassifyCorner(
        Transform vehicleRoot,
        Vector3 worldCenter,
        float sourceLeftSign,
        float axleMid,
        bool frontPositive)
    {
        var local = vehicleRoot.InverseTransformPoint(worldCenter);
        var isLeft = Mathf.Sign(local.x == 0f ? sourceLeftSign : local.x) == Mathf.Sign(sourceLeftSign);
        var isFront = frontPositive ? local.z >= axleMid : local.z <= axleMid;
        return (isFront ? "Front" : "Rear") + (isLeft ? "Left" : "Right");
    }

    private static Bounds GetLocalBounds(Transform targetRoot, IReadOnlyList<MeshRenderer> renderers)
    {
        var found = false;
        var bounds = default(Bounds);
        foreach (var renderer in renderers)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter?.sharedMesh == null)
                continue;
            var matrix = targetRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var vertices = filter.sharedMesh.vertices;
            for (var index = 0; index < vertices.Length; index++)
            {
                var point = matrix.MultiplyPoint3x4(vertices[index]);
                if (!found)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    found = true;
                }
                else
                    bounds.Encapsulate(point);
            }
        }
        if (!found)
            throw new InvalidOperationException("Could not calculate source mesh bounds.");
        return bounds;
    }

    private static Mesh BakeCombinedMesh(
        Transform vehicleRoot,
        IReadOnlyList<MeshRenderer> renderers,
        Vector3 authoredWheelCenter,
        Vector3 fit,
        string assetPath,
        string meshName)
    {
        var combines = new List<CombineInstance>();
        foreach (var renderer in renderers)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter?.sharedMesh == null)
                continue;
            var sourceToRoot = vehicleRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var matrix = Matrix4x4.Scale(fit) * Matrix4x4.Translate(-authoredWheelCenter) * sourceToRoot;
            for (var subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
            {
                combines.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    subMeshIndex = subMesh,
                    transform = matrix,
                });
            }
        }
        if (combines.Count == 0)
            throw new InvalidOperationException($"No readable meshes were available for '{meshName}'.");

        var combined = new Mesh { name = meshName, indexFormat = IndexFormat.UInt32 };
        combined.CombineMeshes(combines.ToArray(), true, true, false);
        combined.RecalculateBounds();
        if (combined.normals == null || combined.normals.Length != combined.vertexCount)
            combined.RecalculateNormals();
        combined.UploadMeshData(false);

        EnsureAssetFolder(MeshFolder);
        var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (persistent == null)
        {
            AssetDatabase.CreateAsset(combined, assetPath);
            persistent = combined;
        }
        else
        {
            EditorUtility.CopySerialized(combined, persistent);
            UnityEngine.Object.DestroyImmediate(combined);
            EditorUtility.SetDirty(persistent);
        }
        return persistent;
    }

    private static Material FirstMaterial(IReadOnlyList<MeshRenderer> renderers)
    {
        foreach (var renderer in renderers)
            foreach (var material in renderer.sharedMaterials)
                if (material != null)
                    return material;
        throw new InvalidOperationException("Generated geometry has no source material.");
    }

    private static void CopyRendererSettings(Renderer source, Renderer target)
    {
        target.shadowCastingMode = source.shadowCastingMode;
        target.receiveShadows = source.receiveShadows;
        target.lightProbeUsage = source.lightProbeUsage;
        target.reflectionProbeUsage = source.reflectionProbeUsage;
        target.motionVectorGenerationMode = source.motionVectorGenerationMode;
        target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        target.renderingLayerMask = source.renderingLayerMask;
    }

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
        throw new InvalidOperationException($"Wheel controller '{controller.name}' has no visual reference.");
    }

    private static int CreateLightOverlayReferences(GameObject model)
    {
        var overlayModel = AssetDatabase.LoadAssetAtPath<GameObject>(LightOverlayModelPath);
        if (overlayModel == null)
        {
            Debug.LogWarning(
                "FerrariSF90Spider: FerrariSF90LightOverlays.glb not found. Vehicle setup will " +
                "remain drivable and headlight beams still work, but DRL/indicator/brake/reverse " +
                "surface overlays must be exported after labeling the lamp mesh in Blender.");
            return 0;
        }

        var lightAnchor = FindTransformWithNameFragment(model.transform, "Light_Geo_lodA_") ??
                          FindTransform(model.transform, "Light_Geo_lodA");
        if (lightAnchor == null)
            throw new InvalidOperationException("SF90 Light_Geo_lodA anchor was not found.");

        var created = 0;
        foreach (var pair in LightOverlayNames)
        {
            var source = FindTransform(overlayModel.transform, pair.SourceName);
            var mesh = source?.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null)
            {
                Debug.LogWarning($"FerrariSF90Spider: overlay mesh '{pair.SourceName}' is missing.");
                continue;
            }
            var existing = FindTransform(model.transform, pair.RuntimeName);
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var overlay = new GameObject(pair.RuntimeName);
            overlay.transform.SetParent(lightAnchor, false);
            overlay.transform.localPosition = Vector3.zero;
            overlay.transform.localRotation = Quaternion.identity;
            overlay.transform.localScale = Vector3.one;
            overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = overlay.AddComponent<MeshRenderer>();
            renderer.enabled = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            created++;
        }
        return created;
    }

    private static void AssignPersistentMaterials(GameObject model)
    {
        EnsureAssetFolder(MaterialFolder);
        var replacements = new Dictionary<string, Material>(StringComparer.Ordinal);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                    continue;
                var assetName = GetPersistentMaterialName(source);
                if (!replacements.TryGetValue(assetName, out var persistent))
                {
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
                        EditorUtility.SetDirty(persistent);
                    }
                    replacements.Add(assetName, persistent);
                }
                materials[index] = persistent;
                changed = true;
            }
            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    private static string GetPersistentMaterialName(Material source)
    {
        var name = source.name;
        if (ContainsIgnoreCase(name, PaintMaterialMarker))
            return "FerrariSF90SpiderBodyPaint_2021Paint_Material";
        if (ContainsIgnoreCase(name, CaliperMaterialMarker))
            return "FerrariSF90Spider_" + CaliperMaterialMarker;
        if (ContainsIgnoreCase(name, WheelMaterialMarker))
            return "FerrariSF90Spider_" + WheelMaterialMarker;
        if (ContainsIgnoreCase(name, "Window_Material"))
            return "FerrariSF90SpiderWindow_Material";
        if (ContainsIgnoreCase(name, "RED_GLASS"))
            return "FerrariSF90Spider_RED_GLASS";
        if (ContainsIgnoreCase(name, LightMaterialMarker))
            return "FerrariSF90SpiderLightA_Material";
        return "FerrariSF90Spider_" + SanitizeAssetName(name);
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
                if (material != null && ContainsIgnoreCase(material.name, PaintMaterialMarker))
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

    private static void CreateManifest()
    {
        var manifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(ManifestPath);
        if (manifest == null)
        {
            manifest = ScriptableObject.CreateInstance<BAModManifest>();
            AssetDatabase.CreateAsset(manifest, ManifestPath);
        }
        manifest.ModId = "FerrariSF90Spider";
        manifest.DisplayName = "Ferrari SF90 Spider";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "ferrarisf90spider.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows;
        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("SF90 manifest references could not be assigned.");
        EditorUtility.SetDirty(manifest);
    }

    private static void ValidateProjectAssets(bool logSuccess)
    {
        var vehicleType = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath) ??
                          throw new InvalidOperationException("FerrariSF90Spider.asset is missing. Run setup first.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePrefabPath) ??
                     throw new InvalidOperationException("FerrariSF90Spider.prefab is missing. Run setup first.");
        ValidateVehicleType(vehicleType);
        ValidatePrefab(prefab, false);
        ValidateAudioFiles();
        if (logSuccess)
            Debug.Log("FerrariSF90Spider project assets validated successfully.");
    }

    private static void ValidateVehicleType(UnityEngine.Object vehicleType)
    {
        var serialized = new SerializedObject(vehicleType);
        var issues = new List<string>();
        if (serialized.FindProperty("vehicleTypeName")?.stringValue != VehicleTypeName)
            issues.Add("vehicleTypeName");
        if (Mathf.Abs(ReadNumber(serialized.FindProperty("price")) - PeriodSpiderMsrp) > 1f)
            issues.Add("price");
        if (Mathf.Abs(ReadNumber(serialized.FindProperty("maxSpeed")) - 340f) > 0.5f)
            issues.Add("maxSpeed");
        if (Mathf.Abs(ReadNumber(serialized.FindProperty("enginePower")) - RatedSystemPowerKw) > 0.5f)
            issues.Add("enginePower");
        if (issues.Count > 0)
            throw new InvalidOperationException("SF90 VehicleType validation failed: " + string.Join(", ", issues));
    }

    private static void ValidatePrefab(GameObject prefab, bool bundleValidation)
    {
        var issues = new List<string>();
        var body = prefab.GetComponent<Rigidbody>();
        if (body == null || Mathf.Abs(body.mass - VehicleMass) > 0.5f)
            issues.Add("mass");
        if (body != null && Vector3.Distance(body.centerOfMass, StableCenterOfMass) > 0.002f)
            issues.Add("centerOfMass");

        foreach (var pair in WheelControllerPositions)
        {
            var controller = FindTransform(prefab.transform, pair.Key);
            if (controller == null || Vector3.Distance(controller.localPosition, pair.Value) > 0.005f)
                issues.Add(pair.Key);
        }
        foreach (var corner in new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" })
        {
            if (FindTransform(prefab.transform, "FerrariSF90SpiderWheel" + corner) == null)
                issues.Add("wheel:" + corner);
            if (FindTransform(prefab.transform, "FerrariSF90SpiderFixedCaliper" + corner) == null)
                issues.Add("caliper:" + corner);
        }
        if (FindTransform(prefab.transform, "STEERING_WHEEL") == null)
            issues.Add("STEERING_WHEEL marker");
        if (FindTransform(prefab.transform, "Spotlights") == null)
            issues.Add("Spotlights beam donor");
        if (prefab.GetComponent<FerrariSF90SpiderPaintController>() == null)
            issues.Add("paint bootstrap");

        var paintSlots = 0;
        var glassSlots = 0;
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                if (ContainsIgnoreCase(material.name, PaintMaterialMarker)) paintSlots++;
                if (FerrariSF90SpiderMaterials.IsCabinGlassMaterial(material)) glassSlots++;
            }
        if (paintSlots == 0) issues.Add("body paint material");
        if (glassSlots == 0) issues.Add("cabin glass material");

        var transmissionVerified = false;
        foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || !string.Equals(component.GetType().FullName, "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
                continue;
            var serialized = new SerializedObject(component);
            var count = FindRelativeProperty(serialized, "powertrain.transmission.forwardGearCount");
            var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
            transmissionVerified = Mathf.Abs(ReadNumber(count) - 8f) < 0.1f &&
                                   gears != null && gears.isArray && gears.arraySize == SF90SpiderGears.Length;
        }
        if (!transmissionVerified) issues.Add("8-speed transmission");

        if (issues.Count > 0)
            throw new InvalidOperationException("SF90 prefab validation failed: " + string.Join(", ", issues));

        var overlayCount = 0;
        foreach (var pair in LightOverlayNames)
            if (FindTransform(prefab.transform, pair.RuntimeName)?.GetComponent<MeshFilter>()?.sharedMesh != null)
                overlayCount++;
        if (overlayCount < LightOverlayNames.Length)
            Debug.LogWarning(
                $"FerrariSF90Spider validation: functional lamp overlays={overlayCount}/{LightOverlayNames.Length}. " +
                "This is expected until FerrariSF90LightOverlays.glb is created from labeled lamp geometry.");

        if (!bundleValidation && TryGetFerrariRendererBounds(prefab.transform, out var bounds))
            Debug.Log($"FerrariSF90Spider visual bounds={bounds.size}, lightOverlays={overlayCount}/13.");
    }

    private static void ValidateAudioFiles()
    {
        foreach (var name in new[]
                 {
                     "EngineLow.wav", "EngineLowLoad.wav", "EngineMid.wav", "EngineMidLoad.wav",
                     "EngineHigh.wav", "EngineHighLoad.wav", "HornLow.wav", "HornHigh.wav"
                 })
        {
            var path = ModRoot + "/Config/Audio/" + name;
            if (!System.IO.File.Exists(path))
                throw new InvalidOperationException($"Required runtime audio file '{path}' is missing.");
        }
    }

    private static bool TryGetFerrariRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !FerrariSF90SpiderMaterials.IsFerrariRenderer(renderer.transform))
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    private static bool TryGetModelBodyBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (HasMaterialMarker(renderer.sharedMaterials, WheelMaterialMarker) ||
                HasMaterialMarker(renderer.sharedMaterials, CaliperMaterialMarker))
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    private static bool HasMaterialMarker(IReadOnlyList<Material> materials, string marker)
    {
        for (var index = 0; index < materials.Count; index++)
            if (materials[index] != null && ContainsIgnoreCase(materials[index].name, marker))
                return true;
        return false;
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        return null;
    }

    private static Transform? FindTransformWithNameFragment(Transform root, string fragment)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (ContainsIgnoreCase(transform.name, fragment))
                return transform;
        return null;
    }

    private static void SetLocalPosition(GameObject root, string name, Vector3 value)
    {
        var transform = FindTransform(root.transform, name) ??
                        throw new InvalidOperationException($"Required transform '{name}' is missing.");
        transform.localPosition = value;
    }

    private static void AssignRendererArray(SerializedProperty? property, IReadOnlyList<Renderer> values)
    {
        if (property == null || !property.isArray || property.propertyType == SerializedPropertyType.String)
            return;
        property.arraySize = values.Count;
        for (var index = 0; index < values.Count; index++)
        {
            var element = property.GetArrayElementAtIndex(index);
            if (element.propertyType == SerializedPropertyType.ObjectReference)
                element.objectReferenceValue = values[index];
        }
    }

    private static SerializedProperty? FindRelativeProperty(SerializedObject serialized, string path)
    {
        var parts = path.Split('.');
        SerializedProperty? property = serialized.FindProperty(parts[0]);
        for (var index = 1; index < parts.Length && property != null; index++)
            property = property.FindPropertyRelative(parts[index]);
        return property;
    }

    private static void SetRelativeNumber(SerializedObject serialized, string path, float value)
    {
        var property = FindRelativeProperty(serialized, path);
        if (property == null) return;
        WriteNumber(property, value);
    }

    private static void SetRelativeBool(SerializedObject serialized, string path, bool value)
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
        if (property != null) WriteNumber(property, value);
    }

    private static void WriteNumber(SerializedProperty property, float value)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Float:
                property.floatValue = value;
                break;
            case SerializedPropertyType.Integer:
                property.intValue = Mathf.RoundToInt(value);
                break;
        }
    }

    private static float ReadNumber(SerializedProperty? property)
    {
        if (property == null) return float.NaN;
        if (property.propertyType == SerializedPropertyType.Float) return property.floatValue;
        if (property.propertyType == SerializedPropertyType.Integer) return property.intValue;
        return float.NaN;
    }

    private static bool ContainsIgnoreCase(string value, string marker) =>
        value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string SanitizeAssetName(string value)
    {
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
            if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '_' && chars[index] != '-')
                chars[index] = '_';
        return new string(chars).Trim('_');
    }

    private static void MarkMaterialsDirty(GameObject root)
    {
        var materials = new HashSet<Material>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material != null && materials.Add(material))
                    EditorUtility.SetDirty(material);
    }

    private static void EnsureAssetFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        var slash = folder.LastIndexOf('/');
        if (slash <= 0) throw new InvalidOperationException($"Invalid asset folder '{folder}'.");
        var parent = folder.Substring(0, slash);
        var leaf = folder.Substring(slash + 1);
        EnsureAssetFolder(parent);
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(parent, leaf);
    }
}
