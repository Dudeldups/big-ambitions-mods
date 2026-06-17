using System;
using System.Threading.Tasks;
using BAModAPI;
using System.Linq;
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

            StreetQuestShared.CleanupLegacyContacts();
            foreach (var character in StreetQuestCharacterCatalog.All.Where(value => value != null && value.enabled))
            {
                var dialogTypeKey = string.IsNullOrWhiteSpace(character.dialogTypeKey)
                    ? "streetquest_mack_dialog"
                    : character.dialogTypeKey;
                var questDialogType = (CallDialogType)ModEnumHash.GetSafeHash(dialogTypeKey);
                var capturedCharacterId = character.id;
                CallDialogFactory.RegisterDialog(questDialogType, () => new StreetQuestCharacterDialog(capturedCharacterId));
            }

            EnsureWatcher();

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

        private static void EnsureWatcher()
        {
            if (_watcherObject == null)
            {
                _watcherObject = new GameObject("StreetQuestRPG.PhysicalQuestGiverWatcher");
                UnityEngine.Object.DontDestroyOnLoad(_watcherObject);
            }

            var watcher = _watcherObject.GetComponent<StreetQuestPhysicalQuestGiverWatcher>();
            if (watcher == null)
                watcher = _watcherObject.AddComponent<StreetQuestPhysicalQuestGiverWatcher>();

            watcher.Initialize();
        }
    }
}
