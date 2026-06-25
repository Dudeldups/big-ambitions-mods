using System;
using Localizor;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        internal static bool IsCollectibleCharacter(StreetQuestCharacterDefinition character)
        {
            return character != null &&
                   !string.IsNullOrWhiteSpace(character.collectibleQuestItemId) &&
                   character.collectibleAmount > 0;
        }

        internal static bool HasCollectedCharacter(StreetQuestCharacterDefinition character)
        {
            if (character == null || string.IsNullOrWhiteSpace(character.collectibleCollectedStoryFlag))
                return false;

            return HasStoryFlag(character.collectibleCollectedStoryFlag);
        }

        internal static bool TryCollectCharacter(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return false;

            var character = StreetQuestCharacterCatalog.Get(characterId);
            if (!IsCollectibleCharacter(character))
            {
                LogDebug($"CollectibleCharacter skipped character={characterId} reason=not_collectible");
                return false;
            }

            if (HasCollectedCharacter(character))
            {
                LogDebug($"CollectibleCharacter skipped character={characterId} reason=already_collected");
                return false;
            }

            if (!StreetQuestInventoryService.AddItem(character.collectibleQuestItemId, character.collectibleAmount))
            {
                LogDebug($"CollectibleCharacter failed character={characterId} reason=inventory_add_failed item={character.collectibleQuestItemId} amount={character.collectibleAmount}");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(character.collectibleCollectedStoryFlag))
                AddStoryFlag(character.collectibleCollectedStoryFlag);
            else
                RefreshSpawnedCharacters();

            LogDebug($"CollectibleCharacter collected character={characterId} item={character.collectibleQuestItemId} amount={character.collectibleAmount} storyFlag={character.collectibleCollectedStoryFlag ?? "<none>"}");
            return true;
        }

        internal static string ResolveCollectibleDialogTextKey(StreetQuestCharacterDefinition character)
        {
            return string.IsNullOrWhiteSpace(character?.collectibleDialogTextKey)
                ? "streetquest:dialog_collectible_default"
                : character.collectibleDialogTextKey;
        }

        internal static string ResolveCollectibleConfirmTextKey(StreetQuestCharacterDefinition character)
        {
            return string.IsNullOrWhiteSpace(character?.collectibleConfirmTextKey)
                ? "streetquest:dialog_collectible_pick_up"
                : character.collectibleConfirmTextKey;
        }

        internal static string ResolveCollectibleCollectedTextKey(StreetQuestCharacterDefinition character)
        {
            return string.IsNullOrWhiteSpace(character?.collectibleCollectedTextKey)
                ? "streetquest:dialog_collectible_collected"
                : character.collectibleCollectedTextKey;
        }

        internal static string ResolveCollectibleDisplayName(StreetQuestCharacterDefinition character)
        {
            if (!string.IsNullOrWhiteSpace(character?.nameKey))
                return character.nameKey.Localize().ToString();

            return character?.displayName ?? character?.id ?? "NPC";
        }
    }
}
