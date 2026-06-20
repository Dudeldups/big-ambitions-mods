using System;
using System.Linq;
using System.Reflection;
using BAModAPI;
using BigAmbitions.SaveSystem.Legacy;
using Dialogs;
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
            var contactType = FindType("Contact");
            var getContactMethod = contactType?.GetMethod(
                "GetContact",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(ContactCategoryName), typeof(string) },
                null);
            var contact = getContactMethod?.Invoke(null, new object[] { contactId, category, descriptionKey });
            if (contact == null)
                return false;

            if (!string.IsNullOrWhiteSpace(character.dialogTypeKey))
            {
                var dialogType = (CallDialogType)ModEnumHash.GetSafeHash(character.dialogTypeKey);
                var property = contact.GetType().GetProperty("callDialogTypeOverride", BindingFlags.Public | BindingFlags.Instance);
                property?.SetValue(contact, dialogType);
            }

            return !existedBefore;
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
