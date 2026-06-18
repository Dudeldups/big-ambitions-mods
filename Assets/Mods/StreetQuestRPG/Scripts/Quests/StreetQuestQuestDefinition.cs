using System;
using System.Runtime.Serialization;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable, DataContract]
    internal sealed class StreetQuestQuestDefinition
    {
        [DataMember] public string id;
        [DataMember] public string giverCharacterId;
        [DataMember] public string turnInCharacterId;
        [DataMember] public string previousQuestId;
        [DataMember] public string nextQuestId;
        [DataMember] public string[] nextQuestIds;
        [DataMember] public string[] requiredQuestIds;
        [DataMember] public string[] requiredStoryFlags;
        [DataMember] public StreetQuestQuestFavorRequirementDefinition[] requiredFavors;
        [DataMember] public StreetQuestQuestObjectiveDefinition[] objectives;
        [DataMember] public StreetQuestQuestRewardDefinition[] rewards;
        [DataMember] public string[] acceptedStoryFlags;
        [DataMember] public string[] completedStoryFlags;
        [DataMember] public string introStageOneTextKey;
        [DataMember] public string introStageOneConfirmTextKey;
        [DataMember] public string introStageTwoTextKey;
        [DataMember] public string introStageTwoConfirmTextKey;
        [DataMember] public string offerTextKey;
        [DataMember] public string activeTextKey;
        [DataMember] public string readyTextKey;
        [DataMember] public string acceptedManagerMessageKey;
        [DataMember] public string completedManagerMessageKey;
        [DataMember] public string finishedTextKey;
        [DataMember] public bool enabled = true;

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
        public string IntroStageOneTextKey => introStageOneTextKey;
        public string IntroStageOneConfirmTextKey => introStageOneConfirmTextKey;
        public string IntroStageTwoTextKey => introStageTwoTextKey;
        public string IntroStageTwoConfirmTextKey => introStageTwoConfirmTextKey;
        public string OfferTextKey => offerTextKey;
        public string ActiveTextKey => activeTextKey;
        public string ReadyTextKey => readyTextKey;
        public string AcceptedManagerMessageKey => acceptedManagerMessageKey;
        public string CompletedManagerMessageKey => completedManagerMessageKey;
        public string FinishedTextKey => finishedTextKey;
        public bool Enabled => enabled;

        public string GiverContactId => ResolveContactId(giverCharacterId, StreetQuestShared.MackContactId);
        public string TurnInContactId => ResolveContactId(turnInCharacterId, StreetQuestShared.MackContactId);

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
            if (string.IsNullOrWhiteSpace(introStageOneTextKey)) introStageOneTextKey = fallback.introStageOneTextKey;
            if (string.IsNullOrWhiteSpace(introStageOneConfirmTextKey)) introStageOneConfirmTextKey = fallback.introStageOneConfirmTextKey;
            if (string.IsNullOrWhiteSpace(introStageTwoTextKey)) introStageTwoTextKey = fallback.introStageTwoTextKey;
            if (string.IsNullOrWhiteSpace(introStageTwoConfirmTextKey)) introStageTwoConfirmTextKey = fallback.introStageTwoConfirmTextKey;
            if (string.IsNullOrWhiteSpace(offerTextKey)) offerTextKey = fallback.offerTextKey;
            if (string.IsNullOrWhiteSpace(activeTextKey)) activeTextKey = fallback.activeTextKey;
            if (string.IsNullOrWhiteSpace(readyTextKey)) readyTextKey = fallback.readyTextKey;
            if (string.IsNullOrWhiteSpace(acceptedManagerMessageKey)) acceptedManagerMessageKey = fallback.acceptedManagerMessageKey;
            if (string.IsNullOrWhiteSpace(completedManagerMessageKey)) completedManagerMessageKey = fallback.completedManagerMessageKey;
            if (string.IsNullOrWhiteSpace(finishedTextKey)) finishedTextKey = fallback.finishedTextKey;
        }

        private static string ResolveContactId(string characterId, string fallbackContactId)
        {
            var character = StreetQuestCharacterCatalog.Get(characterId);
            return string.IsNullOrWhiteSpace(character?.contactId)
                ? fallbackContactId
                : character.contactId;
        }
    }
#pragma warning restore CS0649
}
