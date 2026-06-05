using System;
using System.Threading.Tasks;
using BAModAPI;
using Dialogs;
using Entities;
using UI.Smartphone.Apps.Contacts;

namespace StreetQuestRPG
{
    [ModEntryOnCityLoad]
    public sealed class StreetQuestCity : IModBigAmbitions
    {
        private Contact _courierContact;
        private Contact _homelessContact;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            var homelessDialogType = (CallDialogType)ModEnumHash.GetSafeHash("streetquest_homeless_dialog");
            var courierDialogType = (CallDialogType)ModEnumHash.GetSafeHash("streetquest_courier_dialog");

            _homelessContact = StreetQuestShared.EnsureContact(
                StreetQuestShared.HomelessContactId,
                ContactCategoryName.General,
                "streetquest:homeless_description",
                homelessDialogType);
            _courierContact = StreetQuestShared.EnsureContact(
                StreetQuestShared.CourierContactId,
                ContactCategoryName.General,
                "streetquest:courier_description",
                courierDialogType);

            CallDialogFactory.RegisterDialog(homelessDialogType, () => new StreetQuestHomelessDialog());
            CallDialogFactory.RegisterDialog(courierDialogType, () => new StreetQuestCourierDialog());
            StreetQuestShared.RefreshQuestInteractionAddress();

            if (_homelessContact.messagesQueue == null || _homelessContact.messagesQueue.Count == 0)
                _homelessContact.SendMessage(new TextMessage("streetquest:textmessage_welcome"), sendNotificationInstantly: true);

            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            _courierContact = null;
            _homelessContact = null;
            return Task.CompletedTask;
        }
    }
}
