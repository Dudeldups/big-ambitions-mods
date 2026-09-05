#nullable enable
using System;
using BAModAPI;
using UnityEngine;

namespace CameraStore
{
    internal sealed class CameraStoreBusinessRegistration
    {
        private const string AssetPath = "Assets/Mods/Camera Store/CameraStore.asset";
        private BusinessType? businessType;

        public void LoadAndRegister(AssetBundle bundle)
        {
            businessType = bundle.LoadAsset<BusinessType>(AssetPath);
            if (businessType == null)
                throw new InvalidOperationException($"Camera Store business asset is missing: {AssetPath}");

            ModdingAPI.RegisterModBusinessType(businessType);
        }

        public void Unregister()
        {
            if (businessType == null)
                return;

            ModdingAPI.UnregisterModBusinessType(businessType);
            businessType = null;
        }
    }
}
