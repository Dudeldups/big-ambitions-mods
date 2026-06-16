#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VehicleRuntimeTuner.Profiles;
using VehicleRuntimeTuner.Utils;
using VehicleRuntimeTuner.Vehicle;

namespace VehicleRuntimeTuner.Editor
{
    internal static class VehicleRuntimeTunerAssetApplicator
    {
        public static bool TryLoadProfile(string profilePath, out VehicleTuningProfile? profile, out string message)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(profilePath))
            {
                message = "No profile path configured.";
                return false;
            }

            if (!File.Exists(profilePath))
            {
                message = $"Profile not found: {profilePath}";
                return false;
            }

            var json = File.ReadAllText(profilePath);
            profile = JsonUtility.FromJson<VehicleTuningProfile>(json);
            if (profile == null)
            {
                message = "Profile JSON could not be parsed.";
                return false;
            }

            message = $"Loaded profile '{profile.profileName}'.";
            return true;
        }

        public static string BuildDefaultProfilePath(string vehicleTypeName)
        {
            return VehicleRuntimeTunerPaths.GetProfilePath(vehicleTypeName);
        }

        public static string TryReadVehicleTypeName(UnityEngine.Object? vehicleAsset, GameObject? prefabAsset)
        {
            var fromAsset = TryReadVehicleTypeNameFromSerializedObject(vehicleAsset);
            if (!string.IsNullOrWhiteSpace(fromAsset))
                return fromAsset;

            if (prefabAsset == null)
                return string.Empty;

            var prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrWhiteSpace(prefabPath))
                return string.Empty;

            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (var behaviour in prefabRoot.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null)
                        continue;

                    var value = VehicleRuntimeTunerReflection.GetMemberValue(behaviour, "vehicleTypeName") as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        return value!;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            return string.Empty;
        }

        public static bool ApplyToPrefab(GameObject prefabAsset, VehicleTuningProfile profile, out string message)
        {
            message = "Prefab update failed.";
            var prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                message = "Prefab asset path could not be resolved.";
                return false;
            }

            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var rigidbody = prefabRoot.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    if (profile.body.mass.hasValue)
                        rigidbody.mass = profile.body.mass.value;
                    if (profile.body.drag.hasValue)
                        rigidbody.drag = profile.body.drag.value;
                    if (profile.body.angularDrag.hasValue)
                        rigidbody.angularDrag = profile.body.angularDrag.value;
                    if (profile.body.centerOfMass.hasValue)
                        rigidbody.centerOfMass = profile.body.centerOfMass.value;
                    EditorUtility.SetDirty(rigidbody);
                }

                var wheelColliders = prefabRoot.GetComponentsInChildren<WheelCollider>(true);
                VehicleWheelClassifier.SplitWheelColliders(wheelColliders, out var frontWheels, out var rearWheels);
                ApplyWheelGroup(
                    frontWheels,
                    profile.wheels.frontRadius,
                    profile.suspension.frontSuspensionDistance,
                    profile.suspension.frontSpring,
                    profile.suspension.frontDamper,
                    profile.suspension.frontTargetPosition);
                ApplyWheelGroup(
                    rearWheels,
                    profile.wheels.rearRadius,
                    profile.suspension.rearSuspensionDistance,
                    profile.suspension.rearSpring,
                    profile.suspension.rearDamper,
                    profile.suspension.rearTargetPosition);

                var wheelControllers = FindWheelControllers(prefabRoot);
                VehicleWheelClassifier.SplitWheelControllers(wheelControllers, out var frontControllers, out var rearControllers);
                ApplyWheelStruct(frontControllers, profile.wheels.frontRadius, profile.wheels.frontWidth);
                ApplyWheelStruct(rearControllers, profile.wheels.rearRadius, profile.wheels.rearWidth);

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                AssetDatabase.SaveAssets();
                message = $"Applied profile to prefab: {prefabPath}";
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        public static bool ApplyToVehicleAsset(UnityEngine.Object vehicleAsset, VehicleTuningProfile profile, out string message)
        {
            message = "Vehicle asset update failed.";
            var serialized = new SerializedObject(vehicleAsset);
            var changed = false;

            changed |= SetFloatProperty(serialized, "enginePower", profile.engine.enginePower);
            changed |= SetIntProperty(serialized, "maxSpeed", profile.engine.maxSpeed);
            changed |= SetIntProperty(serialized, "brakeForce", profile.brakes.brakeTorque);

            if (!changed)
            {
                message = "No vehicle asset values from the profile were set.";
                return false;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(vehicleAsset);
            AssetDatabase.SaveAssets();
            message = $"Applied profile to vehicle asset: {AssetDatabase.GetAssetPath(vehicleAsset)}";
            return true;
        }

        public static UnityEngine.Object? TryFindSiblingVehicleAsset(GameObject prefabAsset)
        {
            var prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrWhiteSpace(prefabPath))
                return null;

            var folder = Path.GetDirectoryName(prefabPath);
            var stem = Path.GetFileNameWithoutExtension(prefabPath);
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(stem))
                return null;

            var candidate = Path.Combine(folder, stem + ".asset").Replace('\\', '/');
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(candidate);
        }

        private static string TryReadVehicleTypeNameFromSerializedObject(UnityEngine.Object? asset)
        {
            if (asset == null)
                return string.Empty;

            var serialized = new SerializedObject(asset);
            return serialized.FindProperty("vehicleTypeName")?.stringValue ?? string.Empty;
        }

        private static void ApplyWheelGroup(
            IReadOnlyList<WheelCollider> wheelColliders,
            OptionalFloat radius,
            OptionalFloat suspensionDistance,
            OptionalFloat springValue,
            OptionalFloat damperValue,
            OptionalFloat targetPosition)
        {
            foreach (var wheelCollider in wheelColliders)
            {
                if (radius.hasValue)
                    wheelCollider.radius = radius.value;
                if (suspensionDistance.hasValue)
                    wheelCollider.suspensionDistance = suspensionDistance.value;

                if (springValue.hasValue || damperValue.hasValue || targetPosition.hasValue)
                {
                    var spring = wheelCollider.suspensionSpring;
                    if (springValue.hasValue)
                        spring.spring = springValue.value;
                    if (damperValue.hasValue)
                        spring.damper = damperValue.value;
                    if (targetPosition.hasValue)
                        spring.targetPosition = targetPosition.value;
                    wheelCollider.suspensionSpring = spring;
                }

                EditorUtility.SetDirty(wheelCollider);
            }
        }

        private static List<MonoBehaviour> FindWheelControllers(GameObject prefabRoot)
        {
            var list = new List<MonoBehaviour>();
            foreach (var behaviour in prefabRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;

                if (VehicleRuntimeTunerReflection.HasMember(behaviour, "wheel") &&
                    VehicleRuntimeTunerReflection.HasMember(behaviour, "spring"))
                {
                    list.Add(behaviour);
                }
            }

            return list;
        }

        private static void ApplyWheelStruct(IEnumerable<MonoBehaviour> wheelControllers, OptionalFloat radius, OptionalFloat width)
        {
            foreach (var wheelController in wheelControllers)
            {
                if (!VehicleRuntimeTunerReflection.TryGetMemberValue(wheelController, "wheel", out var wheelStruct) || wheelStruct == null)
                    continue;

                var changed = false;
                if (radius.hasValue)
                    changed |= VehicleRuntimeTunerReflection.TrySetMemberValue(wheelStruct, "radius", radius.value);
                if (width.hasValue)
                    changed |= VehicleRuntimeTunerReflection.TrySetMemberValue(wheelStruct, "width", width.value);

                if (!changed)
                    continue;

                VehicleRuntimeTunerReflection.TrySetMemberValue(wheelController, "wheel", wheelStruct);
                EditorUtility.SetDirty(wheelController);
            }
        }

        private static bool SetFloatProperty(SerializedObject serialized, string propertyName, OptionalFloat value)
        {
            if (!value.hasValue)
                return false;

            var property = serialized.FindProperty(propertyName);
            if (property == null)
                return false;

            switch (property.propertyType)
            {
                case SerializedPropertyType.Float:
                    property.floatValue = value.value;
                    return true;
                case SerializedPropertyType.Integer:
                    property.intValue = Mathf.RoundToInt(value.value);
                    return true;
                default:
                    return false;
            }
        }

        private static bool SetIntProperty(SerializedObject serialized, string propertyName, OptionalFloat value)
        {
            if (!value.hasValue)
                return false;

            var property = serialized.FindProperty(propertyName);
            if (property == null)
                return false;

            if (property.propertyType == SerializedPropertyType.Integer)
            {
                property.intValue = Mathf.RoundToInt(value.value);
                return true;
            }

            if (property.propertyType == SerializedPropertyType.Float)
            {
                property.floatValue = value.value;
                return true;
            }

            return false;
        }
    }
}
