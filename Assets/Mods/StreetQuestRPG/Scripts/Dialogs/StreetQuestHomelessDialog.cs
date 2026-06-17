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
            if (progress == StreetQuestQuestProgressState.NotStarted)
            {
                var introStage = StreetQuestShared.GetHomelessIntroStage();
                if (introStage <= 0)
                    return BuildBackOffEntry(currentQuest);
                if (introStage == 1)
                    return BuildBackstoryEntry(currentQuest);

                return BuildOfferEntry(currentQuest);
            }

            return progress switch
            {
                StreetQuestQuestProgressState.Active => BuildActiveEntry(currentQuest),
                StreetQuestQuestProgressState.ReadyToTurnIn => BuildReadyToTurnInEntry(currentQuest),
                _ => BuildEndEntry("streetquest:dialog_finished")
            };
        }

        private DialogEntry BuildBackOffEntry(StreetQuestQuestDefinition quest)
        {
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = "streetquest:dialog_intro_back_off".Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "streetquest:dialog_whats_up".Localize(),
                SecondOptionTextOverride = "streetquest:dialog_leave",
                OnConfirm = () => OnAskWhatsUp(quest),
                OnSecondOption = CloseDialog,
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private DialogEntry OnAskWhatsUp(StreetQuestQuestDefinition quest)
        {
            StreetQuestShared.UnlockHomelessBackstory();
            return BuildBackstoryEntry(quest);
        }

        private DialogEntry BuildBackstoryEntry(StreetQuestQuestDefinition quest)
        {
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = "streetquest:dialog_intro_backstory".Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "streetquest:dialog_yes".Localize(),
                SecondOptionTextOverride = "streetquest:dialog_no",
                OnConfirm = () => OnAgreeToHelp(quest),
                OnSecondOption = CloseDialog,
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private DialogEntry OnAgreeToHelp(StreetQuestQuestDefinition quest)
        {
            StreetQuestShared.UnlockHomelessQuestOffer();
            return BuildOfferEntry(quest);
        }

        private DialogEntry BuildOfferEntry(StreetQuestQuestDefinition quest)
        {
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = quest.OfferTextKey.Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "streetquest:dialog_yes".Localize(),
                SecondOptionTextOverride = "streetquest:dialog_no",
                OnConfirm = () => OnAcceptQuest(quest),
                OnSecondOption = CloseDialog,
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private DialogEntry OnAcceptQuest(StreetQuestQuestDefinition quest)
        {
            if (!StreetQuestShared.AcceptQuest(quest))
                return BuildActiveEntry(quest);

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
