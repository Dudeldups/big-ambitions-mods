using Dialogs;
using Entities;
using Localizor;
using System.Linq;
using UI.Dialog;

namespace StreetQuestRPG
{
    public sealed class StreetQuestCharacterDialog : Dialog
    {
        private readonly string _characterId;
        private readonly StreetQuestCharacterDefinition _character;

        public StreetQuestCharacterDialog(string characterId)
        {
            _characterId = characterId;
            _character = StreetQuestCharacterCatalog.Get(characterId) ?? StreetQuestCharacterCatalog.GetDefaultQuestGiver();
            npcNameKey = string.IsNullOrWhiteSpace(_character?.nameKey)
                ? StreetQuestShared.HomelessNameKey
                : _character.nameKey;

            StreetQuestShared.RecordCharacterInteraction(_characterId);
            DialogController.current.ShowEntry(Start());
        }

        private DialogEntry Start()
        {
            var currentQuest = StreetQuestShared.GetCurrentQuest();
            if (currentQuest == null)
                return BuildEndEntry("streetquest:dialog_finished");

            var progress = StreetQuestShared.GetQuestProgress(currentQuest.Id);
            if (progress == StreetQuestQuestProgressState.NotStarted &&
                string.Equals(currentQuest.GiverCharacterId, _characterId, System.StringComparison.OrdinalIgnoreCase))
            {
                if (HasIntroFlow())
                    return BuildIntroStageOneEntry(currentQuest);

                return BuildOfferEntry(currentQuest);
            }

            if (!string.Equals(currentQuest.TurnInCharacterId, _characterId, System.StringComparison.OrdinalIgnoreCase))
                return BuildEndEntry("streetquest:dialog_finished");

            return progress switch
            {
                StreetQuestQuestProgressState.Active => BuildActiveEntry(currentQuest),
                StreetQuestQuestProgressState.ReadyToTurnIn => BuildReadyToTurnInEntry(currentQuest),
                _ => BuildEndEntry("streetquest:dialog_finished")
            };
        }

        private bool HasIntroFlow() =>
            !string.IsNullOrWhiteSpace(_character?.introStageOneTextKey);

        private DialogEntry BuildIntroStageOneEntry(StreetQuestQuestDefinition quest)
        {
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = _character.introStageOneTextKey.Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = (_character.introStageOneConfirmTextKey ?? "streetquest:dialog_whats_up").Localize(),
                OnConfirm = () => OnAdvanceIntroStage(quest, _character.introStageOneCompletedFlagId, BuildIntroStageTwoEntry),
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private DialogEntry BuildIntroStageTwoEntry(StreetQuestQuestDefinition quest)
        {
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = _character.introStageTwoTextKey.Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = (_character.introStageTwoConfirmTextKey ?? "streetquest:dialog_yes").Localize(),
                OnConfirm = () => OnAdvanceIntroStage(quest, _character.introStageTwoCompletedFlagId, BuildOfferEntry),
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private DialogEntry OnAdvanceIntroStage(
            StreetQuestQuestDefinition quest,
            string storyFlagId,
            System.Func<StreetQuestQuestDefinition, DialogEntry> nextBuilder)
        {
            return nextBuilder(quest);
        }

        private DialogEntry BuildOfferEntry(StreetQuestQuestDefinition quest)
        {
            var confirmTextKey = quest.Objectives.Any(value =>
                value != null && value.ObjectiveType == StreetQuestQuestObjectiveType.BringItem)
                ? "streetquest:dialog_on_my_way"
                : "streetquest:dialog_yes";

            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = quest.OfferTextKey.Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = confirmTextKey.Localize(),
                OnConfirm = () => OnAcceptQuest(quest),
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private DialogEntry OnAcceptQuest(StreetQuestQuestDefinition quest)
        {
            if (!StreetQuestShared.AcceptQuest(quest))
                return BuildActiveEntry(quest);

            return BuildConversationEntry(quest.AcceptedManagerMessageKey);
        }

        private DialogEntry BuildActiveEntry(StreetQuestQuestDefinition quest)
        {
            StreetQuestShared.RecordCharacterInteraction(_characterId);
            if (StreetQuestShared.CanTurnIn(quest) &&
                string.Equals(quest.TurnInCharacterId, _characterId, System.StringComparison.OrdinalIgnoreCase))
            {
                StreetQuestShared.MarkReadyToTurnIn(quest);
                return BuildReadyToTurnInEntry(quest);
            }

            return BuildConversationEntry(quest.ActiveTextKey);
        }

        private DialogEntry BuildReadyToTurnInEntry(StreetQuestQuestDefinition quest)
        {
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = quest.ReadyTextKey.Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "streetquest:dialog_turn_in".Localize(),
                OnConfirm = () => OnCompleteQuest(quest),
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private DialogEntry OnCompleteQuest(StreetQuestQuestDefinition quest)
        {
            StreetQuestShared.MarkReadyToTurnIn(quest);
            if (!StreetQuestShared.CompleteQuest(quest))
                return BuildConversationEntry(quest.ActiveTextKey);

            return BuildConversationEntry(quest.CompletedManagerMessageKey);
        }

        private DialogEntry BuildConversationEntry(string messageKey)
        {
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = messageKey.Localize(),
                Template = DialogEntry.TemplateType.Text,
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private DialogEntry BuildEndEntry(string messageKey) => BuildConversationEntry(messageKey);
    }
}
