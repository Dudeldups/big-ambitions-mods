using System;

namespace StreetQuestRPG
{
    [Serializable]
    internal sealed class StreetQuestQuestDefinition
    {
        public string id;
        public string giverCharacterId;
        public string turnInCharacterId;
        public string previousQuestId;
        public string nextQuestId;
        public string requiredItemName;
        public int requiredAmount = 1;
        public int rewardAmount;
        public string offerTextKey;
        public string activeTextKey;
        public string readyTextKey;
        public string acceptedPlayerMessageKey;
        public string acceptedManagerMessageKey;
        public string completedPlayerMessageKey;
        public string completedManagerMessageKey;
        public bool enabled = true;

        public string Id => id;
        public string GiverCharacterId => giverCharacterId;
        public string TurnInCharacterId => turnInCharacterId;
        public string PreviousQuestId => previousQuestId;
        public string NextQuestId => nextQuestId;
        public string RequiredItemName => requiredItemName;
        public int RequiredAmount => requiredAmount <= 0 ? 1 : requiredAmount;
        public int RewardAmount => rewardAmount;
        public string OfferTextKey => offerTextKey;
        public string ActiveTextKey => activeTextKey;
        public string ReadyTextKey => readyTextKey;
        public string AcceptedPlayerMessageKey => acceptedPlayerMessageKey;
        public string AcceptedManagerMessageKey => acceptedManagerMessageKey;
        public string CompletedPlayerMessageKey => completedPlayerMessageKey;
        public string CompletedManagerMessageKey => completedManagerMessageKey;
        public bool Enabled => enabled;

        public string GiverContactId => ResolveContactId(giverCharacterId, StreetQuestShared.HomelessContactId);
        public string TurnInContactId => ResolveContactId(turnInCharacterId, StreetQuestShared.HomelessContactId);

        public void FillMissingValuesFrom(StreetQuestQuestDefinition fallback)
        {
            if (fallback == null)
                return;

            if (string.IsNullOrWhiteSpace(id)) id = fallback.id;
            if (string.IsNullOrWhiteSpace(giverCharacterId)) giverCharacterId = fallback.giverCharacterId;
            if (string.IsNullOrWhiteSpace(turnInCharacterId)) turnInCharacterId = fallback.turnInCharacterId;
            if (string.IsNullOrWhiteSpace(requiredItemName)) requiredItemName = fallback.requiredItemName;
            if (requiredAmount <= 0) requiredAmount = fallback.requiredAmount;
            if (rewardAmount <= 0) rewardAmount = fallback.rewardAmount;
            if (string.IsNullOrWhiteSpace(offerTextKey)) offerTextKey = fallback.offerTextKey;
            if (string.IsNullOrWhiteSpace(activeTextKey)) activeTextKey = fallback.activeTextKey;
            if (string.IsNullOrWhiteSpace(readyTextKey)) readyTextKey = fallback.readyTextKey;
            if (string.IsNullOrWhiteSpace(acceptedPlayerMessageKey)) acceptedPlayerMessageKey = fallback.acceptedPlayerMessageKey;
            if (string.IsNullOrWhiteSpace(acceptedManagerMessageKey)) acceptedManagerMessageKey = fallback.acceptedManagerMessageKey;
            if (string.IsNullOrWhiteSpace(completedPlayerMessageKey)) completedPlayerMessageKey = fallback.completedPlayerMessageKey;
            if (string.IsNullOrWhiteSpace(completedManagerMessageKey)) completedManagerMessageKey = fallback.completedManagerMessageKey;
        }

        private static string ResolveContactId(string characterId, string fallbackContactId)
        {
            var character = StreetQuestCharacterCatalog.Get(characterId);
            return string.IsNullOrWhiteSpace(character?.contactId)
                ? fallbackContactId
                : character.contactId;
        }
    }
}
