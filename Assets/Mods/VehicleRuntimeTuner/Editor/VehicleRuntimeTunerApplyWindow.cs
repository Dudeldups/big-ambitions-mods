#nullable enable
using UnityEditor;
using UnityEngine;

namespace VehicleRuntimeTuner.Editor
{
    public sealed class VehicleRuntimeTunerApplyWindow : EditorWindow
    {
        private GameObject? prefabAsset;
        private UnityEngine.Object? vehicleAsset;
        private string profilePath = string.Empty;
        private string status = "Select a prefab and/or vehicle asset.";

        [MenuItem("Big Ambitions/Vehicle Runtime Tuner/Apply Saved Profile", priority = 30)]
        public static void Open()
        {
            var window = GetWindow<VehicleRuntimeTunerApplyWindow>("Vehicle Tuner Apply");
            window.minSize = new Vector2(520f, 260f);
            window.TryAutoPopulateFromSelection();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Apply Saved Vehicle Tuner Profile", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Loads a saved runtime tuner JSON profile and writes the values back into the Unity prefab and vehicle asset.",
                MessageType.Info);

            prefabAsset = (GameObject?)EditorGUILayout.ObjectField("Vehicle Prefab", prefabAsset, typeof(GameObject), false);
            vehicleAsset = EditorGUILayout.ObjectField("Vehicle Asset", vehicleAsset, typeof(UnityEngine.Object), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                profilePath = EditorGUILayout.TextField("Profile JSON", profilePath);
                if (GUILayout.Button("Auto", GUILayout.Width(70f)))
                    AutoResolveProfilePath();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selection"))
                    TryAutoPopulateFromSelection();

                if (GUILayout.Button("Auto Find Asset") && prefabAsset != null)
                {
                    vehicleAsset = VehicleRuntimeTunerAssetApplicator.TryFindSiblingVehicleAsset(prefabAsset);
                    status = vehicleAsset != null ? "Found sibling vehicle asset." : "No sibling vehicle asset found.";
                }
            }

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply To Prefab"))
                    ApplyToPrefab();
                if (GUILayout.Button("Apply To Vehicle Asset"))
                    ApplyToVehicleAsset();
                if (GUILayout.Button("Apply Both"))
                    ApplyBoth();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        private void TryAutoPopulateFromSelection()
        {
            if (Selection.activeObject is GameObject selectedPrefab)
            {
                prefabAsset = selectedPrefab;
                vehicleAsset = VehicleRuntimeTunerAssetApplicator.TryFindSiblingVehicleAsset(selectedPrefab);
            }
            else if (Selection.activeObject != null)
            {
                vehicleAsset = Selection.activeObject;
            }

            AutoResolveProfilePath();
        }

        private void AutoResolveProfilePath()
        {
            var vehicleTypeName = VehicleRuntimeTunerAssetApplicator.TryReadVehicleTypeName(vehicleAsset, prefabAsset);
            if (string.IsNullOrWhiteSpace(vehicleTypeName))
            {
                status = "Could not auto-resolve vehicleTypeName. Assign the vehicle asset or enter the profile path manually.";
                return;
            }

            profilePath = VehicleRuntimeTunerAssetApplicator.BuildDefaultProfilePath(vehicleTypeName);
            status = $"Auto-resolved profile path for '{vehicleTypeName}'.";
        }

        private void ApplyToPrefab()
        {
            if (prefabAsset == null)
            {
                status = "Assign a vehicle prefab first.";
                return;
            }

            if (!VehicleRuntimeTunerAssetApplicator.TryLoadProfile(profilePath, out var profile, out status) || profile == null)
                return;

            VehicleRuntimeTunerAssetApplicator.ApplyToPrefab(prefabAsset, profile, out status);
        }

        private void ApplyToVehicleAsset()
        {
            if (vehicleAsset == null)
            {
                status = "Assign a vehicle asset first.";
                return;
            }

            if (!VehicleRuntimeTunerAssetApplicator.TryLoadProfile(profilePath, out var profile, out status) || profile == null)
                return;

            VehicleRuntimeTunerAssetApplicator.ApplyToVehicleAsset(vehicleAsset, profile, out status);
        }

        private void ApplyBoth()
        {
            if (!VehicleRuntimeTunerAssetApplicator.TryLoadProfile(profilePath, out var profile, out status) || profile == null)
                return;

            var updatedAny = false;
            if (prefabAsset != null)
                updatedAny |= VehicleRuntimeTunerAssetApplicator.ApplyToPrefab(prefabAsset, profile, out status);
            if (vehicleAsset != null)
                updatedAny |= VehicleRuntimeTunerAssetApplicator.ApplyToVehicleAsset(vehicleAsset, profile, out status);

            if (!updatedAny)
                status = "Nothing was applied. Assign a prefab and/or vehicle asset.";
        }
    }
}
