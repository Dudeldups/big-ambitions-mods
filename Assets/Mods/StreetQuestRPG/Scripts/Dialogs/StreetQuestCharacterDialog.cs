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
            _character = StreetQuestCharacterCatalog.Get(characterId);
            npcNameKey = string.IsNullOrWhiteSpace(_character?.nameKey)
                ? StreetQuestShared.MackNameKey
                : _character.nameKey;

            StreetQuestShared.RecordCharacterInteraction(_characterId);
            DialogController.current.ShowEntry(Start());
        }

        private DialogEntry Start()
        {
            var currentQuest = StreetQuestShared.GetCurrentQuest();
            if (currentQuest == null)
                return BuildFinishedEntry();

            var progress = StreetQuestShared.GetQuestProgress(currentQuest.Id);
            if (progress == StreetQuestQuestProgressState.NotStarted &&
                string.Equals(currentQuest.GiverCharacterId, _characterId, System.StringComparison.OrdinalIgnoreCase))
            {
                if (HasIntroFlow(currentQuest))
                    return BuildIntroStageEntry(currentQuest, 0);

                return BuildOfferEntry(currentQuest);
            }

            if (!string.Equals(currentQuest.TurnInCharacterId, _characterId, System.StringComparison.OrdinalIgnoreCase))
                return BuildFinishedEntry();

            return progress switch
            {
                StreetQuestQuestProgressState.Active => BuildActiveEntry(currentQuest),
                StreetQuestQuestProgressState.ReadyToTurnIn => BuildReadyToTurnInEntry(currentQuest),
                _ => BuildFinishedEntry()
            };
        }

        private static bool HasIntroFlow(StreetQuestQuestDefinition quest) =>
            quest?.IntroStages.Any(stage => stage != null && !string.IsNullOrWhiteSpace(stage.TextKey)) == true;

        private DialogEntry BuildIntroStageEntry(StreetQuestQuestDefinition quest, int stageIndex)
        {
            var stages = quest?.IntroStages
                .Where(stage => stage != null && !string.IsNullOrWhiteSpace(stage.TextKey))
                .ToArray();
            if (stages == null || stageIndex < 0 || stageIndex >= stages.Length)
                return BuildOfferEntry(quest);

            var stage = stages[stageIndex];
            System.Func<DialogEntry> nextEntryBuilder = stageIndex + 1 < stages.Length
                ? () => BuildIntroStageEntry(quest, stageIndex + 1)
                : () => BuildOfferEntry(quest);

            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = stage.TextKey.Localize(),
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = ResolveIntroConfirmTextKey(stage, stageIndex).Localize(),
                OnConfirm = nextEntryBuilder,
                OnCancel = DialogController.current.FinishDialog
            };
        }

        private static string ResolveIntroConfirmTextKey(StreetQuestQuestIntroStageDefinition stage, int stageIndex)
        {
            if (!string.IsNullOrWhiteSpace(stage?.ConfirmTextKey))
                return stage.ConfirmTextKey;

            return stageIndex == 0
                ? "streetquest:dialog_whats_up"
                : "streetquest:dialog_yes";
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

        private DialogEntry BuildFinishedEntry()
        {
            var finishedQuest = StreetQuestQuestCatalog.GetLastCompletedQuest(StreetQuestShared.GetQuestStateSnapshot());
            var messageKey = string.IsNullOrWhiteSpace(finishedQuest?.FinishedTextKey)
                ? "streetquest:dialog_q1_finished"
                : finishedQuest.FinishedTextKey;
            return BuildConversationEntry(messageKey);
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
