using System;
using System.Linq;
using BigAmbitions.SaveSystem.Legacy;
using CustomNPCAPI;
using Entities;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        internal static void RefreshStreetQuestContactDialogOverrides()
        {
            var contacts = SaveGameManager.Current?.Contacts;
            if (contacts == null || contacts.Count == 0)
                return;

            var changed = 0;
            foreach (var character in StreetQuestCharacterCatalog.All.Where(value => value != null && value.interactable))
            {
                var contactId = ResolveContactId(character);
                if (string.IsNullOrWhiteSpace(contactId) || string.IsNullOrWhiteSpace(character.dialogTypeKey))
                    continue;
                var contact = CustomNpcPhone.FindContact(contactId);
                if (contact != null && CustomNpcPhone.ApplyDialogOverride(contact, character.dialogTypeKey))
                    changed++;
            }

            if (changed > 0 && SaveGameManager.Current != null)
            {
                SaveGameManager.Current.hasEverUsedMods = true;
                SaveGameManager.MarkChange();
            }
        }

        private static bool GrantContactReward(string characterId)
        {
            var character = StreetQuestCharacterCatalog.Get(characterId);
            if (character == null)
                return false;

            var contactId = ResolveContactId(character);
            if (string.IsNullOrWhiteSpace(contactId))
                return false;

            var existedBefore = CustomNpcPhone.FindContact(contactId) != null;
            try
            {
                var contact = CustomNpcPhone.EnsureContact(new CustomNpcPhoneDefinition
                {
                    ContactId = contactId,
                    ContactCategory = character.contactCategory,
                    DescriptionKey = ResolveContactDescriptionKey(character),
                    DialogTypeKey = character.dialogTypeKey
                });
                return !existedBefore && contact != null;
            }
            catch (Exception exception)
            {
                LogDebug($"GrantContactReward failed character={characterId} exception={exception}");
                return false;
            }
        }

        internal static void AppendPhoneNpcMessage(string characterId, string messageKey, bool sendNotificationInstantly = false)
        {
            var contact = EnsureStreetQuestContact(characterId);
            if (contact != null)
                CustomNpcPhone.AppendNpcMessage(contact, messageKey, sendNotificationInstantly);
        }

        internal static void AppendPhonePlayerMessage(string characterId, string messageKey)
        {
            var contact = EnsureStreetQuestContact(characterId);
            if (contact != null)
                CustomNpcPhone.AppendPlayerMessage(contact, messageKey);
        }

        private static Contact EnsureStreetQuestContact(string characterId)
        {
            var character = StreetQuestCharacterCatalog.Get(characterId);
            if (character == null)
                return null;

            try
            {
                return CustomNpcPhone.EnsureContact(new CustomNpcPhoneDefinition
                {
                    ContactId = ResolveContactId(character),
                    ContactCategory = character.contactCategory,
                    DescriptionKey = ResolveContactDescriptionKey(character),
                    DialogTypeKey = character.dialogTypeKey
                });
            }
            catch (Exception exception)
            {
                LogDebug($"EnsureStreetQuestContact failed character={characterId} exception={exception.GetType().Name}:{exception.Message}");
                return null;
            }
        }

        private static string ResolveContactId(StreetQuestCharacterDefinition character)
        {
            if (!string.IsNullOrWhiteSpace(character?.contactId)) return character.contactId;
            if (!string.IsNullOrWhiteSpace(character?.nameKey)) return character.nameKey;
            return character?.id;
        }

        private static string ResolveContactDescriptionKey(StreetQuestCharacterDefinition character)
        {
            if (!string.IsNullOrWhiteSpace(character?.contactDescriptionKey)) return character.contactDescriptionKey;
            if (!string.IsNullOrWhiteSpace(character?.professionKey)) return character.professionKey;
            if (!string.IsNullOrWhiteSpace(character?.nameKey)) return character.nameKey;
            return string.Empty;
        }
    }
}
