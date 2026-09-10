#nullable enable
using System;
using System.Collections.Generic;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class CadillacEscaladeSetup
{
    private const string ModRoot = "Assets/Mods/Cadillac_Escalade";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/cadillac_escalade.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/CadillacDamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/CadillacEscalade.asset";
    private const string VehiclePrefabPath = ModRoot + "/CadillacEscalade.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/CadillacEscalade.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/cadillacescalade.unity3d";
    private const string VehicleTypeName =
        "cadillacescalade-vehicle:vehicletype_cadillacescalade";
    private const float TargetLength = 5.144f;
    // Cadillac's published 2.007 m width excludes mirrors. The supplied GLB
    // bounds include both extended mirrors, so normalize that span separately
    // to avoid squeezing the body inside the correctly sized wheel track.
    private const float TargetVisualWidthIncludingMirrors = 2.45f;
    private const float TargetHeight = 1.887f;
    private const float BodyGroundClearance = 0.18f;
    private const float WheelRadius = 0.408f;
    private const float WheelWidth = 0.285f;
    private const float TireFrictionCircleStrength = 0.90f;
    private const float AntiRollBarForce = 10500f;
    private const float FrontSuspensionTravel = 0.15f;
    private const float RearSuspensionTravel = 0.15f;
    private const float DeformationStrength = 0.13f;
    private const float DeformationRadius = 0.45f;
    private const float DeformationRandomness = 0.012f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.22f, -0.10f);
    private static readonly Vector3 LowerColliderCenter = new Vector3(0f, 0.40f, -0.05f);
    private static readonly Vector3 LowerColliderSize = new Vector3(1.90f, 0.50f, 4.92f);
    private static readonly Vector3 UpperColliderCenter = new Vector3(0f, 0.88f, -0.22f);
    private static readonly Vector3 UpperColliderSize = new Vector3(1.66f, 0.70f, 3.15f);

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-0.865f, WheelRadius, 1.500f) },
            { "FrontRight_WheelController", new Vector3(0.865f, WheelRadius, 1.500f) },
            { "RearLeft_WheelController", new Vector3(-0.850f, WheelRadius, -1.446f) },
            { "RearRight_WheelController", new Vector3(0.850f, WheelRadius, -1.446f) },
        };

    private static readonly float[] EscaladeGears =
    {
        -3.06f,
        0f,
        4.03f,
        2.36f,
        1.53f,
        1.15f,
        0.85f,
        0.67f,
    };

    private static AnimationCurve CreateEscaladePowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.10f, 0.20f),
            new Keyframe(0.35f, 0.55f),
            new Keyframe(0.62f, 0.86f),
            new Keyframe(0.82f, 0.97f),
            new Keyframe(0.92f, 1f),
            new Keyframe(1f, 0.88f));

    [MenuItem("Big Ambitions Mods/Setup Cadillac Escalade")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "CadillacEscalade setup complete: generated a fitted Escalade with four " +
            "independent wheel visuals, six-speed automatic, 40:60 AWD, V8 audio, functional lamp " +
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
        VerifyCadillacBundle();
    }

    private static void VerifyCadillacBundle()
    {
        var bundle = AssetBundle.LoadFromFile(WindowsBundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");

        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException(
                    "Cadillac bundle is missing its VehicleType or prefab.");

            var issues = new List<string>();
            var serialized = new SerializedObject(vehicleType);
            var price = ReadNumber(serialized.FindProperty("price"));
            var fuel = ReadNumber(serialized.FindProperty("maxFuel"));
            var cargo = ReadNumber(serialized.FindProperty("maxCargoCapacity"));
            var speed = ReadNumber(serialized.FindProperty("maxSpeed"));
            var power = ReadNumber(serialized.FindProperty("enginePower"));
            if (Math.Abs(price - 74225f) > 0.5f) issues.Add($"price={price}");
            if (Math.Abs(fuel - 98f) > 0.5f) issues.Add($"fuel={fuel}");
            if (Math.Abs(cargo - 24f) > 0.5f) issues.Add($"cargo={cargo}");
            if (Math.Abs(speed - 171f) > 0.5f) issues.Add($"speed={speed}");
            if (Math.Abs(power - 301f) > 0.5f) issues.Add($"power={power}");

            if (!TryGetCadillacRendererBounds(prefab.transform, out var bounds))
                issues.Add("no Cadillac renderer bounds");
            else
            {
                if (bounds.size.z < 5.08f || bounds.size.z > 5.22f)
                    issues.Add($"length={bounds.size.z:F3}");
                if (bounds.size.x < 2.40f || bounds.size.x > 2.50f)
                    issues.Add($"mirrorSpan={bounds.size.x:F3}");
                if (bounds.size.y < 1.82f || bounds.size.y > 1.96f)
                    issues.Add($"height={bounds.size.y:F3}");
            }

            var wheelMounts = 0;
            var rotors = 0;
            var fixedCalipers = 0;
            var positiveWheelTransforms = true;
            var straightTireMeshes = 0;
            var glassRenderers = 0;
            var bodyPaintSlots = 0;
            var caliperSlots = 0;
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(transform.name, "CadillacWheelFrontLeft", StringComparison.Ordinal) ||
                    string.Equals(transform.name, "CadillacWheelFrontRight", StringComparison.Ordinal) ||
                    string.Equals(transform.name, "CadillacWheelRearLeft", StringComparison.Ordinal) ||
                    string.Equals(transform.name, "CadillacWheelRearRight", StringComparison.Ordinal))
                {
                    wheelMounts++;
                    positiveWheelTransforms &=
                        Vector3.Distance(transform.localScale, Vector3.one) < 0.0001f;
                }
                if (transform.name.StartsWith("CadillacBrakeRotor", StringComparison.Ordinal))
                    rotors++;
                if (transform.name.StartsWith("CadillacFixedCaliper", StringComparison.Ordinal))
                    fixedCalipers++;
                if (transform.name.StartsWith("CadillacWheel", StringComparison.Ordinal) &&
                    transform.name.EndsWith("Tire", StringComparison.Ordinal))
                {
                    var tireMesh = transform.GetComponent<MeshFilter>()?.sharedMesh;
                    if (tireMesh != null)
                    {
                        var size = tireMesh.bounds.size;
                        if (Math.Abs(size.x - WheelWidth) < 0.005f &&
                            Math.Abs(size.y - WheelRadius * 2f) < 0.005f &&
                            Math.Abs(size.z - WheelRadius * 2f) < 0.005f)
                        {
                            straightTireMeshes++;
                        }
                    }
                }
            }

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var hasGlass = false;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                        continue;
                    if (CadillacEscaladeMaterials.IsCabinGlassMaterial(material))
                        hasGlass = true;
                    if (IsBodyPaintMaterial(material))
                        bodyPaintSlots++;
                    if (IsCaliperMaterial(material))
                        caliperSlots++;
                }
                if (hasGlass)
                    glassRenderers++;
            }

            var frontLeft = FindTransform(prefab.transform, "FrontLeft_WheelController");
            var frontRight = FindTransform(prefab.transform, "FrontRight_WheelController");
            var rearLeft = FindTransform(prefab.transform, "RearLeft_WheelController");
            var rearRight = FindTransform(prefab.transform, "RearRight_WheelController");
            if (frontLeft == null || frontRight == null || rearLeft == null || rearRight == null)
            {
                issues.Add("wheel controllers missing");
            }
            else
            {
                var wheelbase = Math.Abs(frontLeft.localPosition.z - rearLeft.localPosition.z);
                var frontTrack = Math.Abs(frontRight.localPosition.x - frontLeft.localPosition.x);
                var rearTrack = Math.Abs(rearRight.localPosition.x - rearLeft.localPosition.x);
                if (Math.Abs(wheelbase - 2.946f) > 0.01f)
                    issues.Add($"wheelbase={wheelbase:F3}");
                if (Math.Abs(frontTrack - 1.730f) > 0.01f)
                    issues.Add($"frontTrack={frontTrack:F3}");
                if (Math.Abs(rearTrack - 1.700f) > 0.01f)
                    issues.Add($"rearTrack={rearTrack:F3}");
            }

            var body = prefab.GetComponent<Rigidbody>();
            if (body == null || Math.Abs(body.mass - 2575f) > 0.5f ||
                Vector3.Distance(body.centerOfMass, StableCenterOfMass) > 0.001f)
            {
                issues.Add("mass/center-of-mass");
            }

            var bodyCollider = FindTransform(prefab.transform, "BodyCollider");
            var bodyColliders = bodyCollider?.GetComponents<BoxCollider>() ?? Array.Empty<BoxCollider>();
            if (bodyColliders.Length < 2 ||
                Vector3.Distance(bodyColliders[0].center, LowerColliderCenter) > 0.001f ||
                Vector3.Distance(bodyColliders[0].size, LowerColliderSize) > 0.001f ||
                Vector3.Distance(bodyColliders[1].center, UpperColliderCenter) > 0.001f ||
                Vector3.Distance(bodyColliders[1].size, UpperColliderSize) > 0.001f)
            {
                issues.Add("body-collider-profile");
            }

            var transmissionVerified = false;
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null ||
                    !string.Equals(
                        component.GetType().FullName,
                        "NWH.VehiclePhysics2.VehicleController",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                var vehicle = new SerializedObject(component);
                var transmission = FindRelativeProperty(
                    vehicle,
                    "powertrain.transmission");
                var gears = transmission?.FindPropertyRelative("gears");
                transmissionVerified =
                    Math.Abs(ReadNumber(
                        transmission?.FindPropertyRelative("finalGearRatio")) - 3.42f) < 0.01f &&
                    gears != null &&
                    gears.isArray &&
                    gears.arraySize == EscaladeGears.Length;
                var clutch = FindRelativeProperty(vehicle, "powertrain.clutch");
                transmissionVerified &=
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("creepTorque"))) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("creepSpeedLimit")) - 1f) < 0.01f;
                break;
            }
            if (!transmissionVerified)
                issues.Add("six-speed powertrain");

            if (wheelMounts != 4) issues.Add($"wheelMounts={wheelMounts}");
            if (rotors != 4) issues.Add($"rotors={rotors}");
            if (fixedCalipers != 4) issues.Add($"fixedCalipers={fixedCalipers}");
            if (!positiveWheelTransforms) issues.Add("wheel mount scale");
            if (straightTireMeshes != 4) issues.Add($"straightTireMeshes={straightTireMeshes}");
            if (glassRenderers < 2) issues.Add($"glassRenderers={glassRenderers}");
            if (bodyPaintSlots < 1) issues.Add($"bodyPaintSlots={bodyPaintSlots}");
            if (caliperSlots != 4) issues.Add($"caliperSlots={caliperSlots}");
            if (FindTransform(prefab.transform, "Animate_SteeringWheel_033") == null)
                issues.Add("steering/driver reference");
            if (FindTransform(prefab.transform, "Spotlights") == null)
                issues.Add("headlight beam template");
            if (FindTransform(prefab.transform, "CadillacDamageBody") == null)
                issues.Add("deformable body");

            if (issues.Count > 0)
                throw new InvalidOperationException(
                    "Cadillac bundle verification failed: " + string.Join(", ", issues));

            Debug.Log(
                $"CadillacEscalade bundle verified: price={price:F0}, fuel={fuel:F0}L, " +
                $"cargo={cargo:F0}, speed={speed:F0}kph, power={power:F0}kW, " +
                $"bounds={bounds.size}, wheels={wheelMounts}, rotors={rotors}, " +
                $"straightTires={straightTireMeshes}, fixedCalipers={fixedCalipers}, " +
                $"glassRenderers={glassRenderers}, " +
                $"bodyPaintSlots={bodyPaintSlots}, caliperSlots={caliperSlots}, " +
                "transmission=6-speed-automatic, drivetrain=40:60-AWD.");
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
                throw new InvalidOperationException("Could not create the Cadillac VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated Cadillac VehicleType asset did not load.");

        target.name = "CadillacEscalade";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 74225f);
        SetNumber(serialized, "maxFuel", 98f);
        SetNumber(serialized, "maxCargoCapacity", 24f);
        SetNumber(serialized, "maxSpeed", 171f);
        SetNumber(serialized, "enginePower", 301f);
        SetNumber(serialized, "brakeForce", 23000f);
        SetNumber(serialized, "turnRadius", 35f);
        SetNumber(serialized, "damageIntensity", 0.75f);
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
            throw new InvalidOperationException("Cadillac GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "CadillacEscalade";
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
                throw new InvalidOperationException("Could not instantiate the Cadillac model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "CadillacVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureExitMarkers(root, modelInstance);
            AssignPersistentMaterials(modelInstance);
            AttachWheelVisuals(root, modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            var fix = CadillacEscaladeMaterials.FixSolidMaterials(root);
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);

            Debug.Log(
                $"CadillacEscalade: prepared decal-safe materials renderers={fix.RendererCount}, " +
                $"decalMasksCleared={fix.DecalMasksCleared}, " +
                $"opaqueFixed={fix.OpaqueMaterialsFixed}, " +
                $"transparentFixed={fix.TransparentMaterialsFixed}, " +
                $"cabinGlass={fix.CabinGlassRenderers}/" +
                $"reenabled={fix.CabinGlassRenderersReenabled}, " +
                $"rimMaterialsConfigured={rimMaterialsConfigured}, " +
                $"hdrpValidated={fix.MaterialsValidated}.");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Cadillac vehicle prefab.");
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

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>() ??
                   throw new InvalidOperationException("Reference prefab has no Rigidbody.");
        body.mass = 2575f;
        body.drag = 0f;
        body.angularDrag = 1.8f;
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
            SetNumber(serialized, "baseMass", 2575f);
            SetNumber(serialized, "combinedMass", 2575f);
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
                SetRelativeNumber(serialized, "spring.maxForce", 26000f);
                SetRelativeNumber(serialized, "wheel.radius", WheelRadius);
                SetRelativeNumber(serialized, "wheel.width", WheelWidth);
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

        colliders[0].center = LowerColliderCenter;
        colliders[0].size = LowerColliderSize;
        colliders[1].center = UpperColliderCenter;
        colliders[1].size = UpperColliderSize;
    }

    private static void ConfigureExitMarkers(GameObject root, GameObject model)
    {
        const float driverSide = -1.25f;
        SetLocalPosition(root, "Animate_SteeringWheel_033", new Vector3(-0.52f, 1.25f, 0.68f));
        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.20f, 0.30f));
        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.20f, 0.30f));
        Debug.Log(
            $"CadillacEscalade: left-hand-drive steering reference=(-0.52,1.25,0.68); " +
            $"driver/passenger exits={driverSide:F2}/{-driverSide:F2}.");
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
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1200f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 500f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 500f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.32f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", 301f);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateEscaladePowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 600f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 6200f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.80f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.42f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 6f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.35f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 1800f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 5900f);
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
                gears.arraySize = EscaladeGears.Length;
                for (var index = 0; index < EscaladeGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = EscaladeGears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 171f);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        if (!found)
            throw new InvalidOperationException("NWH vehicle controller was not found on the reference prefab.");
    }

    private static void NormalizeModel(GameObject model)
    {
        model.transform.localPosition = Vector3.zero;
        // glTFast already resolves the Sketchfab root matrix to a Y-up model
        // whose nose points along vehicle +Z. Preserve that authored rotation.
        model.transform.localScale = Vector3.one;

        if (!TryGetModelBodyBounds(model.transform, out var bounds))
            throw new InvalidOperationException("Cadillac model contains no renderers.");

        if (!TryGetModelBodyBounds(model.transform, out bounds) || bounds.size.z <= 0.001f)
            throw new InvalidOperationException("Cadillac model length could not be measured.");

        var scale = new Vector3(
            TargetVisualWidthIncludingMirrors / bounds.size.x,
            TargetLength / bounds.size.z,
            (TargetHeight - BodyGroundClearance) / bounds.size.y);
        model.transform.localScale = scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled Cadillac bounds could not be measured.");

        model.transform.position += new Vector3(
            -bounds.center.x,
            BodyGroundClearance - bounds.min.y,
            -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final Cadillac bounds could not be measured.");

        Debug.Log(
            $"CadillacEscalade: normalized supplied GLB scale={scale}, " +
            $"bounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var mapping = new Dictionary<string, (string Rim, string Tire, string StraightRim, string StraightTire)>
        {
            {
                "FrontLeft_WheelController",
                ("Cadillac_Escalade_obj.006", "gum.003", "Cadillac_Escalade_obj.001", "gum.001")
            },
            {
                "FrontRight_WheelController",
                ("Cadillac_Escalade_obj.005", "gum.002", "Cadillac_Escalade_obj.004", "gum.000")
            },
            {
                "RearLeft_WheelController",
                ("Cadillac_Escalade_obj.001", "gum.001", "Cadillac_Escalade_obj.001", "gum.001")
            },
            {
                "RearRight_WheelController",
                ("Cadillac_Escalade_obj.004", "gum.000", "Cadillac_Escalade_obj.004", "gum.000")
            },
        };

        var straightRotations = new Dictionary<string, Quaternion>();
        foreach (var value in mapping.Values)
        {
            if (!straightRotations.ContainsKey(value.StraightRim))
            {
                var reference = FindTransform(model.transform, value.StraightRim) ??
                                throw new InvalidOperationException(
                                    $"Straight rim reference '{value.StraightRim}' is missing.");
                straightRotations.Add(value.StraightRim, reference.localRotation);
            }
            if (!straightRotations.ContainsKey(value.StraightTire))
            {
                var reference = FindTransform(model.transform, value.StraightTire) ??
                                throw new InvalidOperationException(
                                    $"Straight tire reference '{value.StraightTire}' is missing.");
                straightRotations.Add(value.StraightTire, reference.localRotation);
            }
        }

        foreach (var pair in mapping)
        {
            var rimSource = FindTransform(model.transform, pair.Value.Rim) ??
                            throw new InvalidOperationException(
                                $"Model rim group '{pair.Value.Rim}' is missing.");
            var tireSource = FindTransform(model.transform, pair.Value.Tire) ??
                             throw new InvalidOperationException(
                                 $"Model tire group '{pair.Value.Tire}' is missing.");
            rimSource.localRotation = straightRotations[pair.Value.StraightRim];
            tireSource.localRotation = straightRotations[pair.Value.StraightTire];
            var controller = FindTransform(root.transform, pair.Key) ??
                             throw new InvalidOperationException(
                                 $"Wheel controller '{pair.Key}' is missing.");
            if (!TryGetRendererBounds(tireSource, out var tireBounds) ||
                tireBounds.size.x <= 0.001f ||
                tireBounds.size.y <= 0.001f ||
                tireBounds.size.z <= 0.001f)
            {
                throw new InvalidOperationException(
                    $"Model tire group '{pair.Value.Tire}' has invalid bounds.");
            }

            controller.localPosition = WheelControllerPositions[pair.Key];
            var suffix = pair.Key
                .Replace("_WheelController", string.Empty)
                .Replace("_", string.Empty);
            var mount = new GameObject(
                "CadillacWheel" + suffix);
            mount.transform.SetParent(root.transform, false);
            mount.transform.localPosition = controller.localPosition;
            rimSource.SetParent(mount.transform, true);
            tireSource.SetParent(mount.transform, true);
            mount.transform.localScale = new Vector3(
                WheelWidth / tireBounds.size.x,
                (WheelRadius * 2f) / tireBounds.size.y,
                (WheelRadius * 2f) / tireBounds.size.z);
            if (!TryGetRendererBounds(rimSource, out var fittedRimBounds) ||
                !TryGetRendererBounds(tireSource, out tireBounds))
                throw new InvalidOperationException(
                    $"Model wheel groups '{pair.Value.Rim}/{pair.Value.Tire}' could not be fitted.");
            // The supplied front rim and tire nodes do not share the same pivot.
            // Center each mesh independently so the imported pivot offset cannot
            // throw a baked wheel several metres away from its controller.
            rimSource.position += mount.transform.position - fittedRimBounds.center;
            tireSource.position += mount.transform.position - tireBounds.center;

            BakeWheelPart(root, mount, rimSource, suffix + "Rim");
            BakeWheelPart(root, mount, tireSource, suffix + "Tire");
            mount.transform.localScale = Vector3.one;
            CreateBrakeHardware(root, mount, controller, suffix);
            Debug.Log(
                $"CadillacEscalade: fitted {pair.Value.Rim}/{pair.Value.Tire} to {pair.Key} " +
                $"center={controller.localPosition}, tire={WheelWidth:F3}x{WheelRadius * 2f:F3}m.");

            AssignWheelVisual(controller, mount);
        }
    }

    private static void BakeWheelPart(
        GameObject root,
        GameObject mount,
        Transform sourceGroup,
        string assetSuffix)
    {
        var renderer = sourceGroup.GetComponentInChildren<MeshRenderer>(true) ??
                       throw new InvalidOperationException(
                           $"Wheel source '{sourceGroup.name}' has no mesh renderer.");
        var filter = renderer.GetComponent<MeshFilter>();
        if (filter?.sharedMesh == null)
            throw new InvalidOperationException(
                $"Wheel source '{sourceGroup.name}' has no readable mesh.");

        EnsureAssetFolder(MeshFolder);
        var meshPath = $"{MeshFolder}/CadillacWheel{assetSuffix}.asset";
        var baked = UnityEngine.Object.Instantiate(filter.sharedMesh);
        baked.name = "CadillacWheel" + assetSuffix;
        var sourceToRoot = root.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
        var sourceToMount =
            Matrix4x4.Translate(-mount.transform.localPosition) * sourceToRoot;
        var vertices = baked.vertices;
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = sourceToMount.MultiplyPoint3x4(vertices[index]);
        baked.vertices = vertices;

        var normals = baked.normals;
        if (normals.Length == vertices.Length)
        {
            var normalMatrix = sourceToMount.inverse.transpose;
            for (var index = 0; index < normals.Length; index++)
                normals[index] = normalMatrix.MultiplyVector(normals[index]).normalized;
            baked.normals = normals;
        }

        var tangents = baked.tangents;
        var mirrored = sourceToMount.determinant < 0f;
        if (tangents.Length == vertices.Length)
        {
            for (var index = 0; index < tangents.Length; index++)
            {
                var tangent = tangents[index];
                var direction = sourceToMount.MultiplyVector(
                    new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                tangents[index] = new Vector4(
                    direction.x,
                    direction.y,
                    direction.z,
                    mirrored ? -tangent.w : tangent.w);
            }
            baked.tangents = tangents;
        }

        if (mirrored)
        {
            for (var subMesh = 0; subMesh < baked.subMeshCount; subMesh++)
            {
                var triangles = baked.GetTriangles(subMesh);
                for (var index = 0; index + 2 < triangles.Length; index += 3)
                    (triangles[index + 1], triangles[index + 2]) =
                        (triangles[index + 2], triangles[index + 1]);
                baked.SetTriangles(triangles, subMesh, false);
            }
        }
        baked.RecalculateBounds();
        baked.UploadMeshData(false);

        var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (persistent == null)
        {
            AssetDatabase.CreateAsset(baked, meshPath);
            persistent = baked;
        }
        else
        {
            EditorUtility.CopySerialized(baked, persistent);
            UnityEngine.Object.DestroyImmediate(baked);
            EditorUtility.SetDirty(persistent);
        }

        var geometry = new GameObject("CadillacWheel" + assetSuffix);
        geometry.transform.SetParent(mount.transform, false);
        geometry.AddComponent<MeshFilter>().sharedMesh = persistent;
        var targetRenderer = geometry.AddComponent<MeshRenderer>();
        targetRenderer.sharedMaterials = renderer.sharedMaterials;
        targetRenderer.shadowCastingMode = renderer.shadowCastingMode;
        targetRenderer.receiveShadows = renderer.receiveShadows;
        targetRenderer.lightProbeUsage = renderer.lightProbeUsage;
        targetRenderer.reflectionProbeUsage = renderer.reflectionProbeUsage;
        targetRenderer.renderingLayerMask = renderer.renderingLayerMask;
        UnityEngine.Object.DestroyImmediate(sourceGroup.gameObject);
    }

    private static void CreateBrakeHardware(
        GameObject root,
        GameObject wheelMount,
        Transform controller,
        string suffix)
    {
        var rotorMaterial = GetOrCreateBrakeMaterial(
            "CadillacBrakeRotor",
            new Color(0.32f, 0.34f, 0.36f, 1f),
            0.82f,
            0.48f);
        var caliperMaterial = GetOrCreateBrakeMaterial(
            "CadillacCaliper",
            new Color(0.55f, 0.03f, 0.025f, 1f),
            0.48f,
            0.62f);

        var rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rotor.name = "CadillacBrakeRotor" + suffix;
        UnityEngine.Object.DestroyImmediate(rotor.GetComponent<Collider>());
        rotor.transform.SetParent(wheelMount.transform, false);
        rotor.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        rotor.transform.localScale = new Vector3(0.32f, 0.012f, 0.32f);
        rotor.GetComponent<MeshRenderer>().sharedMaterial = rotorMaterial;

        var fixedCaliper = new GameObject("CadillacFixedCaliper" + suffix);
        fixedCaliper.transform.SetParent(root.transform, false);
        fixedCaliper.transform.localPosition = controller.localPosition;
        var caliper = GameObject.CreatePrimitive(PrimitiveType.Cube);
        caliper.name = "CadillacCaliper" + suffix;
        UnityEngine.Object.DestroyImmediate(caliper.GetComponent<Collider>());
        caliper.transform.SetParent(fixedCaliper.transform, false);
        var side = controller.localPosition.x < 0f ? -1f : 1f;
        caliper.transform.localPosition = new Vector3(side * 0.04f, 0.10f, -0.13f);
        caliper.transform.localScale = new Vector3(0.055f, 0.20f, 0.085f);
        caliper.GetComponent<MeshRenderer>().sharedMaterial = caliperMaterial;
    }

    private static Material GetOrCreateBrakeMaterial(
        string name,
        Color color,
        float metallic,
        float smoothness)
    {
        EnsureAssetFolder(MaterialFolder);
        var path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("HDRP/Lit") ??
                         Shader.Find("High Definition Render Pipeline/Lit") ??
                         throw new InvalidOperationException("HDRP Lit shader is unavailable.");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
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
            if (string.Equals(
                    renderer.name,
                    "Cadillac_Escalade_obj_3",
                    StringComparison.Ordinal))
            {
                sourceRenderer = renderer;
                break;
            }
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException("The Cadillac outer body mesh was not found.");

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");

        var bakedMesh = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
        bakedMesh.name = "CadillacDamageBody";
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

        var damageBody = new GameObject("CadillacDamageBody")
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
        material.name.IndexOf(
            "CadillacOpaque_03_White",
            StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorAccentPaintMaterial(Material material) =>
        false;

    private static bool IsRimMaterial(Material material) =>
        material.name.IndexOf(
            "CadillacOpaque_14_material_2",
            StringComparison.OrdinalIgnoreCase) >= 0;

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!CadillacEscaladeMaterials.IsCadillacRenderer(renderer.transform))
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
            throw new InvalidOperationException("The shared Cadillac rim material is missing.");

        ApplyRimFinish(leftMaterial, CadillacEscaladeMaterials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!CadillacEscaladeMaterials.IsCadillacRenderer(renderer.transform))
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
                $"Expected four Cadillac rim slots, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", CadillacEscaladeMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", CadillacEscaladeMaterials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("CadillacCaliper", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static bool EscaladePowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateEscaladePowerCurve();
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
        false;

    private static bool IsInteriorPrimaryPaintMaterial(Material material) =>
        false;

    private static bool IsInteriorSecondaryPaintMaterial(Material material) =>
        false;

    private static bool IsInteriorDarkPaintMaterial(Material material) =>
        false;

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
                    var transparent = CadillacEscaladeMaterials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var assetName =
                        $"Cadillac{kind}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
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

        manifest.ModId = "CadillacEscalade";
        manifest.DisplayName = "Cadillac Escalade";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "cadillacescalade.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("Cadillac manifest references could not be assigned.");
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
                if (current.name == "Cadillac_Escalade_obj.001" ||
                    current.name == "Cadillac_Escalade_obj.004" ||
                    current.name == "Cadillac_Escalade_obj.005" ||
                    current.name == "Cadillac_Escalade_obj.006" ||
                    current.name == "gum.000" ||
                    current.name == "gum.001" ||
                    current.name == "gum.002" ||
                    current.name == "gum.003")
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

    private static bool TryGetCadillacRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!CadillacEscaladeMaterials.IsCadillacRenderer(renderer.transform))
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
