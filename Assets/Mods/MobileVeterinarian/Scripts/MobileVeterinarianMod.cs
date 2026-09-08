#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using UnityEngine;

[assembly: RegisterModClass(typeof(MobileVeterinarian.MobileVeterinarianMod))]

namespace MobileVeterinarian
{
    [ModEntryOnCityLoad]
    public sealed class MobileVeterinarianMod : IModBigAmbitions
    {
        internal const string BundleKey = "AssetBundles/mobileveterinarian.unity3d";
        internal const string DoctorPrefabPath = "Assets/Mods/MobileVeterinarian/Doctor.prefab";

        private MobileVeterinarianRuntime? runtime;
        private GameObject? doctorPrefab;

        public string[] RelativeAssetBundlePaths => new[] { BundleKey };

        public Task OnLoadAsync(ModContext context)
        {
            _ = AnimalVehicleRegistry.GetRegistrations();
            var bundle = AssetService.GetBundle(context.ModId, BundleKey);
            doctorPrefab = bundle?.LoadAsset<GameObject>(DoctorPrefabPath);
            if (doctorPrefab == null)
            {
                context.Logger.Warn(
                    $"Mobile Veterinarian: doctor prefab load failed bundle='{BundleKey}' asset='{DoctorPrefabPath}'.");
            }
            else
            {
                context.Logger.Info(
                    $"Mobile Veterinarian: doctor prefab loaded bundle='{BundleKey}' asset='{DoctorPrefabPath}'.");
            }

            runtime = MobileVeterinarianRuntime.Install(context, doctorPrefab);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            runtime?.Shutdown("game or city unload");
            runtime = null;
            doctorPrefab = null;
            return Task.CompletedTask;
        }
    }
}
