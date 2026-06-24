using System;
using System.Linq;
using BAModAPI;
using BigAmbitions.SaveSystem.Legacy;
using Dialogs;
using Entities;
using UI.Smartphone.Apps.Contacts;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        internal static void RefreshStreetQuestContactDialogOverrides()
        {
            var saveGame = SaveGameManager.Current;
            var contacts = saveGame?.Contacts;
            if (contacts == null || contacts.Count == 0)
                return;

            var changed = 0;
            foreach (var character in StreetQuestCharacterCatalog.All.Where(value => value != null && value.interactable))
            {
                var contactId = ResolveContactId(character);
                if (string.IsNullOrWhiteSpace(contactId) || string.IsNullOrWhiteSpace(character.dialogTypeKey))
                    continue;

                var contact = contacts.FirstOrDefault(existingContact =>
                    existingContact != null &&
                    string.Equals(existingContact.id, contactId, StringComparison.OrdinalIgnoreCase));
                if (contact == null)
                    continue;

                if (ApplyContactDialogOverride(contact, character))
                    changed++;
            }

            if (changed > 0)
            {
                SaveGameManager.Current.hasEverUsedMods = true;
                SaveGameManager.MarkChange();
                LogDebug($"RefreshStreetQuestContactDialogOverrides changed={changed}");
            }
        }

        private static bool GrantContactReward(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return false;

            var character = StreetQuestCharacterCatalog.Get(characterId);
            if (character == null)
                return false;

            var contactId = ResolveContactId(character);
            if (string.IsNullOrWhiteSpace(contactId))
                return false;

            var existedBefore = SaveGameManager.Current?.Contacts?.Any(contact =>
                contact != null &&
                string.Equals(contact.id, contactId, StringComparison.OrdinalIgnoreCase)) == true;

            var category = ResolveContactCategory(character.contactCategory);
            var descriptionKey = ResolveContactDescriptionKey(character);
            Contact contact;
            try
            {
                contact = Contact.GetContact(contactId, category, descriptionKey);
            }
            catch (Exception exception)
            {
                LogDebug($"GrantContactReward failed character={characterId} exception={exception}");
                return false;
            }

            if (contact == null)
                return false;

            var changed = ApplyContactDialogOverride(contact, character);

            var existsAfter = SaveGameManager.Current?.Contacts?.Any(existingContact =>
                existingContact != null &&
                string.Equals(existingContact.id, contactId, StringComparison.OrdinalIgnoreCase)) == true;

            var saveGame = SaveGameManager.Current;
            if ((existsAfter || changed) && saveGame != null)
            {
                saveGame.hasEverUsedMods = true;
                SaveGameManager.MarkChange();
            }

            LogDebug($"GrantContactReward character={characterId} contactId={contactId} existedBefore={existedBefore} existsAfter={existsAfter}");
            return !existedBefore && existsAfter;
        }

        private static bool ApplyContactDialogOverride(Contact contact, StreetQuestCharacterDefinition character)
        {
            if (contact == null || string.IsNullOrWhiteSpace(character?.dialogTypeKey))
                return false;

            var dialogType = (CallDialogType)ModEnumHash.GetSafeHash(character.dialogTypeKey);
            if (contact.callDialogTypeOverride == dialogType)
                return false;

            contact.callDialogTypeOverride = dialogType;
            return true;
        }

        internal static void AppendPhoneNpcMessage(string characterId, string messageKey, bool sendNotificationInstantly = false)
        {
            if (string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(messageKey))
                return;

            var contact = EnsureStreetQuestContact(characterId);
            if (contact == null)
                return;

            contact.SendMessage(new TextMessage(messageKey, null, true, true), sendNotificationInstantly);
            SaveContactHistoryChange();
        }

        internal static void AppendPhonePlayerMessage(string characterId, string messageKey)
        {
            if (string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(messageKey))
                return;

            var contact = EnsureStreetQuestContact(characterId);
            if (contact == null)
                return;

            contact.ReceivePlayerMessage(new TextMessage(messageKey, null, true));
            SaveContactHistoryChange();
        }

        private static Contact EnsureStreetQuestContact(string characterId)
        {
            var character = StreetQuestCharacterCatalog.Get(characterId);
            if (character == null)
                return null;

            var contactId = ResolveContactId(character);
            if (string.IsNullOrWhiteSpace(contactId))
                return null;

            try
            {
                var contact = Contact.GetContact(
                    contactId,
                    ResolveContactCategory(character.contactCategory),
                    ResolveContactDescriptionKey(character));
                if (contact == null)
                    return null;

                ApplyContactDialogOverride(contact, character);
                return contact;
            }
            catch (Exception exception)
            {
                LogDebug($"EnsureStreetQuestContact failed character={characterId} exception={exception.GetType().Name}:{exception.Message}");
                return null;
            }
        }

        private static void SaveContactHistoryChange()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return;

            saveGame.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
        }

        private static string ResolveContactId(StreetQuestCharacterDefinition character)
        {
            if (!string.IsNullOrWhiteSpace(character?.contactId))
                return character.contactId;

            if (!string.IsNullOrWhiteSpace(character?.nameKey))
                return character.nameKey;

            return character?.id;
        }

        private static string ResolveContactDescriptionKey(StreetQuestCharacterDefinition character)
        {
            if (!string.IsNullOrWhiteSpace(character?.contactDescriptionKey))
                return character.contactDescriptionKey;

            if (!string.IsNullOrWhiteSpace(character?.professionKey))
                return character.professionKey;

            if (!string.IsNullOrWhiteSpace(character?.nameKey))
                return character.nameKey;

            return string.Empty;
        }

        private static ContactCategoryName ResolveContactCategory(string configuredValue)
        {
            if (!string.IsNullOrWhiteSpace(configuredValue) &&
                Enum.TryParse(configuredValue, true, out ContactCategoryName parsedCategory))
            {
                return parsedCategory;
            }

            return ContactCategoryName.General;
        }
    }
}
