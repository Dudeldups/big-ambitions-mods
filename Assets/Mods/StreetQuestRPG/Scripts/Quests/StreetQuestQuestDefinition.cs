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
        public string[] nextQuestIds;
        public string[] requiredQuestIds;
        public string[] requiredStoryFlags;
        public StreetQuestQuestFavorRequirementDefinition[] requiredFavors;
        public StreetQuestQuestObjectiveDefinition[] objectives;
        public StreetQuestQuestRewardDefinition[] rewards;
        public string[] acceptedStoryFlags;
        public string[] completedStoryFlags;
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
        public string[] NextQuestIds => nextQuestIds ?? Array.Empty<string>();
        public string[] RequiredQuestIds => requiredQuestIds ?? Array.Empty<string>();
        public string[] RequiredStoryFlags => requiredStoryFlags ?? Array.Empty<string>();
        public StreetQuestQuestFavorRequirementDefinition[] RequiredFavors =>
            requiredFavors ?? Array.Empty<StreetQuestQuestFavorRequirementDefinition>();
        public StreetQuestQuestObjectiveDefinition[] Objectives => objectives ?? Array.Empty<StreetQuestQuestObjectiveDefinition>();
        public StreetQuestQuestRewardDefinition[] Rewards => rewards ?? Array.Empty<StreetQuestQuestRewardDefinition>();
        public string[] AcceptedStoryFlags => acceptedStoryFlags ?? Array.Empty<string>();
        public string[] CompletedStoryFlags => completedStoryFlags ?? Array.Empty<string>();
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
            if (requiredQuestIds == null || requiredQuestIds.Length == 0) requiredQuestIds = fallback.requiredQuestIds;
            if (requiredStoryFlags == null || requiredStoryFlags.Length == 0) requiredStoryFlags = fallback.requiredStoryFlags;
            if (requiredFavors == null || requiredFavors.Length == 0)
                requiredFavors = fallback.requiredFavors;
            if (objectives == null || objectives.Length == 0) objectives = fallback.objectives;
            if (rewards == null || rewards.Length == 0) rewards = fallback.rewards;
            if (acceptedStoryFlags == null || acceptedStoryFlags.Length == 0) acceptedStoryFlags = fallback.acceptedStoryFlags;
            if (completedStoryFlags == null || completedStoryFlags.Length == 0) completedStoryFlags = fallback.completedStoryFlags;
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
