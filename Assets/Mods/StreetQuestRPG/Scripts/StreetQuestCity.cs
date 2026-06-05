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
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            var questDialogType = (CallDialogType)ModEnumHash.GetSafeHash("streetquest_homeless_dialog");

            StreetQuestShared.CleanupLegacyContacts();
            CallDialogFactory.RegisterDialog(questDialogType, () => new StreetQuestHomelessDialog());
            if (!StreetQuestShared.TryInstallPhysicalQuestGiver(questDialogType))
                Debug.LogWarning("StreetQuestRPG: Failed to patch the physical quest giver interaction.");

            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            StreetQuestShared.RestorePatchedDialogs();
            return Task.CompletedTask;
        }
    }
}
