using System;
using System.Threading.Tasks;
using BAModAPI;
using Dialogs;
using UnityEngine;

namespace StreetQuestRPG
{
    [ModEntryOnCityLoad]
    public sealed class StreetQuestCity : IModBigAmbitions
    {
        private static GameObject _watcherObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            var questDialogType = (CallDialogType)ModEnumHash.GetSafeHash("streetquest_homeless_dialog");

            StreetQuestShared.CleanupLegacyContacts();
            CallDialogFactory.RegisterDialog(questDialogType, () => new StreetQuestHomelessDialog());
            EnsureWatcher(questDialogType);

            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (_watcherObject != null)
            {
                UnityEngine.Object.Destroy(_watcherObject);
                _watcherObject = null;
            }

            StreetQuestShared.RestorePatchedDialogs();
            return Task.CompletedTask;
        }

        private static void EnsureWatcher(CallDialogType dialogType)
        {
            if (_watcherObject == null)
            {
                _watcherObject = new GameObject("StreetQuestRPG.PhysicalQuestGiverWatcher");
                UnityEngine.Object.DontDestroyOnLoad(_watcherObject);
            }

            var watcher = _watcherObject.GetComponent<StreetQuestPhysicalQuestGiverWatcher>();
            if (watcher == null)
                watcher = _watcherObject.AddComponent<StreetQuestPhysicalQuestGiverWatcher>();

            watcher.Initialize(dialogType);
        }
    }
}
