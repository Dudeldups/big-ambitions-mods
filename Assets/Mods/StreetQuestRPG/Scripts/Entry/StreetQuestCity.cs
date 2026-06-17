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
            StreetQuestShared.InitializeDebugLogging(context, nameof(StreetQuestCity));
            StreetQuestCharacterCatalog.Initialize(context?.ModRootPath, context?.Logger);
            StreetQuestQuestCatalog.Initialize(context?.ModRootPath, context?.Logger);

            var defaultQuestGiver = StreetQuestCharacterCatalog.GetDefaultQuestGiver();
            var dialogTypeKey = string.IsNullOrWhiteSpace(defaultQuestGiver?.dialogTypeKey)
                ? "streetquest_mack_dialog"
                : defaultQuestGiver.dialogTypeKey;
            var questDialogType = (CallDialogType)ModEnumHash.GetSafeHash(dialogTypeKey);

            StreetQuestShared.CleanupLegacyContacts();
            CallDialogFactory.RegisterDialog(questDialogType, () => new StreetQuestMackDialog());
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
