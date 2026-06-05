using Dialogs;
using Entities;
using Localizor;
using UI.Dialog;

namespace StreetQuestRPG
{
    public sealed class StreetQuestCourierDialog : Dialog
    {
        public StreetQuestCourierDialog()
        {
            npcNameKey = StreetQuestShared.CourierNameKey;
            DialogController.current.ShowEntry(Start());
        }

        private DialogEntry Start()
        {
            var currentQuest = StreetQuestShared.GetCurrentQuest();
            if (currentQuest == null || currentQuest.TurnInContactId != StreetQuestShared.CourierContactId)
                return BuildConversationEntry("streetquest:dialog_wrong_contact");

            if (StreetQuestShared.CanTurnIn(currentQuest))
            {
                StreetQuestShared.MarkReadyToTurnIn(currentQuest);
                return new DialogEntry
                {
                    headerKey = npcNameKey,
                    messageData = "streetquest:dialog_q2_courier_ready".Localize(),
                    Template = DialogEntry.TemplateType.Text,
                    ConfirmTextOverride = "streetquest:dialog_turn_in".Localize(),
                    SecondOptionTextOverride = "streetquest:dialog_not_yet",
                    OnConfirm = () => OnCompleteQuest(currentQuest),
                    OnSecondOption = CloseDialog,
                    OnCancel = DialogController.current.FinishDialog
                };
            }

            return BuildConversationEntry("streetquest:dialog_q2_courier_waiting");
        }

        private DialogEntry OnCompleteQuest(StreetQuestQuestDefinition quest)
        {
            StreetQuestShared.MarkReadyToTurnIn(quest);
            if (!StreetQuestShared.CompleteQuest(quest))
                return BuildConversationEntry("streetquest:dialog_q2_courier_waiting");

            DialogController.current.contact.ReceivePlayerMessage(
                new TextMessage(quest.CompletedPlayerMessageKey, null, true));
            DialogController.current.contact.SendMessage(
                new TextMessage(quest.CompletedManagerMessageKey, null, true));

            return BuildConversationEntry(quest.CompletedManagerMessageKey);
        }

        private DialogEntry BuildConversationEntry(string messageKey)
        {
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = messageKey.Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "streetquest:dialog_close".Localize(),
                OnConfirm = CloseDialog,
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private static DialogEntry CloseDialog()
        {
            DialogController.current.FinishDialog();
            return null;
        }
    }
}
