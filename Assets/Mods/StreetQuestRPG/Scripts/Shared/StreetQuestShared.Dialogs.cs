using System;
using System.Linq;
using System.Reflection;
using Dialogs;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        internal static void TryOpenQuestDialog(CallDialogType dialogType)
        {
            try
            {
                var dialogUi = ResolveDialogUiInstance();
                var showDialogMethod = ResolveDialogUiShowDialogMethod();
                showDialogMethod.Invoke(dialogUi, new object[] { dialogType, null, null, null, null });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to open physical quest dialog. {exception}");
            }
        }

        private static object ResolveDialogUiInstance()
        {
            if (CachedDialogUiInstance != null)
                return CachedDialogUiInstance;

            var dialogUiType = ResolveDialogUiType();
            var dialogUi = Resources.FindObjectsOfTypeAll(dialogUiType).FirstOrDefault();
            if (dialogUi == null)
                throw new InvalidOperationException("StreetQuestRPG: Could not find a DialogUI instance.");

            CachedDialogUiInstance = dialogUi;
            return dialogUi;
        }

        private static MethodInfo ResolveDialogUiShowDialogMethod()
        {
            if (CachedDialogUiShowDialogMethod != null)
                return CachedDialogUiShowDialogMethod;

            var dialogUiType = ResolveDialogUiType();
            CachedDialogUiShowDialogMethod = dialogUiType.GetMethod(
                "ShowDialog",
                ReflectionFlags,
                null,
                new[]
                {
                    typeof(CallDialogType),
                    ResolveNavigationBlockerType(),
                    ResolveContactType(),
                    typeof(Action),
                    ResolveThirdPersonCharacterType()
                },
                null);

            if (CachedDialogUiShowDialogMethod == null)
                throw new InvalidOperationException("StreetQuestRPG: Could not resolve DialogUI.ShowDialog.");

            return CachedDialogUiShowDialogMethod;
        }

        private static Type ResolveDialogUiType()
        {
            CachedDialogUiType ??= FindType("UI.Dialog.DialogUI");
            return CachedDialogUiType ?? throw new InvalidOperationException("StreetQuestRPG: Could not resolve UI.Dialog.DialogUI.");
        }

        private static Type ResolveNavigationBlockerType()
        {
            CachedNavigationBlockerType ??= FindType("NavigationBlocker");
            return CachedNavigationBlockerType ?? throw new InvalidOperationException("StreetQuestRPG: Could not resolve NavigationBlocker.");
        }

        private static Type ResolveContactType()
        {
            CachedContactType ??= FindType("Contact");
            return CachedContactType ?? throw new InvalidOperationException("StreetQuestRPG: Could not resolve Contact.");
        }

        private static Type ResolveThirdPersonCharacterType()
        {
            CachedThirdPersonCharacterType ??= FindType("ThirdPersonCharacter");
            return CachedThirdPersonCharacterType ?? throw new InvalidOperationException("StreetQuestRPG: Could not resolve ThirdPersonCharacter.");
        }
    }
}
