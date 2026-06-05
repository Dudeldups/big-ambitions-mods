using Dialogs;
using Entities;
using Localizor;
using UI.Dialog;

namespace StreetQuestRPG
{
    public sealed class StreetQuestHomelessDialog : Dialog
    {
        public StreetQuestHomelessDialog()
        {
            npcNameKey = StreetQuestShared.HomelessNameKey;
            DialogController.current.ShowEntry(Start());
        }

        private DialogEntry Start()
        {
            var currentQuest = StreetQuestShared.GetCurrentQuest();
            if (currentQuest == null)
                return BuildEndEntry("streetquest:dialog_finished");

            var progress = StreetQuestShared.GetQuestProgress(currentQuest.Id);
            return progress switch
            {
                StreetQuestQuestProgressState.NotStarted => BuildOfferEntry(currentQuest),
                StreetQuestQuestProgressState.Active => BuildActiveEntry(currentQuest),
                StreetQuestQuestProgressState.ReadyToTurnIn => BuildReadyToTurnInEntry(currentQuest),
                _ => BuildEndEntry("streetquest:dialog_finished")
            };
        }

        private DialogEntry BuildOfferEntry(StreetQuestQuestDefinition quest)
        {
            var offerKey = !StreetQuestShared.HasIntroducedHomelessQuestline()
                ? "streetquest:dialog_intro"
                : quest.OfferTextKey;

            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = offerKey.Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "streetquest:dialog_accept".Localize(),
                SecondOptionTextOverride = "streetquest:dialog_decline",
                OnConfirm = () => OnAcceptQuest(quest),
                OnSecondOption = CloseDialog,
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private DialogEntry OnAcceptQuest(StreetQuestQuestDefinition quest)
        {
            if (!StreetQuestShared.AcceptQuest(quest))
                return BuildActiveEntry(quest);

            DialogController.current.contact.ReceivePlayerMessage(
                new TextMessage(quest.AcceptedPlayerMessageKey, null, true));
            DialogController.current.contact.SendMessage(
                new TextMessage(quest.AcceptedManagerMessageKey, null, true));

            return BuildConversationEntry(quest.AcceptedManagerMessageKey, CloseDialog);
        }

        private DialogEntry BuildActiveEntry(StreetQuestQuestDefinition quest)
        {
            if (StreetQuestShared.CanTurnIn(quest) &&
                quest.TurnInContactId == StreetQuestShared.HomelessContactId)
            {
                StreetQuestShared.MarkReadyToTurnIn(quest);
                return BuildReadyToTurnInEntry(quest);
            }

            return BuildConversationEntry(quest.ActiveTextKey, CloseDialog);
        }

        private DialogEntry BuildReadyToTurnInEntry(StreetQuestQuestDefinition quest)
        {
            if (quest.TurnInContactId != StreetQuestShared.HomelessContactId)
                return BuildConversationEntry(quest.ReadyTextKey, CloseDialog);

            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = quest.ReadyTextKey.Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "streetquest:dialog_turn_in".Localize(),
                SecondOptionTextOverride = "streetquest:dialog_not_yet",
                OnConfirm = () => OnCompleteQuest(quest),
                OnSecondOption = CloseDialog,
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private DialogEntry OnCompleteQuest(StreetQuestQuestDefinition quest)
        {
            StreetQuestShared.MarkReadyToTurnIn(quest);
            if (!StreetQuestShared.CompleteQuest(quest))
                return BuildConversationEntry(quest.ActiveTextKey, CloseDialog);

            DialogController.current.contact.ReceivePlayerMessage(
                new TextMessage(quest.CompletedPlayerMessageKey, null, true));
            DialogController.current.contact.SendMessage(
                new TextMessage(quest.CompletedManagerMessageKey, null, true));

            return BuildConversationEntry(quest.CompletedManagerMessageKey, CloseDialog);
        }

        private DialogEntry BuildConversationEntry(string messageKey, System.Func<DialogEntry> onConfirm)
        {
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = messageKey.Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "streetquest:dialog_close".Localize(),
                OnConfirm = onConfirm,
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private DialogEntry BuildEndEntry(string messageKey) =>
            BuildConversationEntry(messageKey, CloseDialog);

        private static DialogEntry CloseDialog()
        {
            DialogController.current.FinishDialog();
            return null;
        }
    }
}
