namespace StreetQuestRPG
{
    internal sealed class StreetQuestQuestDefinition
    {
        public StreetQuestQuestDefinition(
            string id,
            string giverContactId,
            string turnInContactId,
            string requiredItemName,
            int requiredAmount,
            int rewardAmount,
            string offerTextKey,
            string activeTextKey,
            string readyTextKey,
            string acceptedPlayerMessageKey,
            string acceptedManagerMessageKey,
            string completedPlayerMessageKey,
            string completedManagerMessageKey,
            string nextQuestId)
        {
            Id = id;
            GiverContactId = giverContactId;
            TurnInContactId = turnInContactId;
            RequiredItemName = requiredItemName;
            RequiredAmount = requiredAmount;
            RewardAmount = rewardAmount;
            OfferTextKey = offerTextKey;
            ActiveTextKey = activeTextKey;
            ReadyTextKey = readyTextKey;
            AcceptedPlayerMessageKey = acceptedPlayerMessageKey;
            AcceptedManagerMessageKey = acceptedManagerMessageKey;
            CompletedPlayerMessageKey = completedPlayerMessageKey;
            CompletedManagerMessageKey = completedManagerMessageKey;
            NextQuestId = nextQuestId;
        }

        public string Id { get; }
        public string GiverContactId { get; }
        public string TurnInContactId { get; }
        public string RequiredItemName { get; }
        public int RequiredAmount { get; }
        public int RewardAmount { get; }
        public string OfferTextKey { get; }
        public string ActiveTextKey { get; }
        public string ReadyTextKey { get; }
        public string AcceptedPlayerMessageKey { get; }
        public string AcceptedManagerMessageKey { get; }
        public string CompletedPlayerMessageKey { get; }
        public string CompletedManagerMessageKey { get; }
        public string NextQuestId { get; }
    }
}
