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

            if (!string.IsNullOrWhiteSpace(character.dialogTypeKey))
            {
                var dialogType = (CallDialogType)ModEnumHash.GetSafeHash(character.dialogTypeKey);
                contact.callDialogTypeOverride = dialogType;
            }

            var existsAfter = SaveGameManager.Current?.Contacts?.Any(existingContact =>
                existingContact != null &&
                string.Equals(existingContact.id, contactId, StringComparison.OrdinalIgnoreCase)) == true;
            return !existedBefore && existsAfter;
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
