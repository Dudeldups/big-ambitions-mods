using System;
using System.Linq;
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
                LogDebug($"TryOpenQuestDialog start dialogType={dialogType}");
                var dialogUiType = FindType("UI.Dialog.DialogUI");
                if (dialogUiType == null)
                    throw new InvalidOperationException("StreetQuestRPG: Could not resolve UI.Dialog.DialogUI.");

                var dialogUi = Resources.FindObjectsOfTypeAll(dialogUiType).FirstOrDefault();
                if (dialogUi == null)
                    throw new InvalidOperationException("StreetQuestRPG: Could not find a DialogUI instance.");

                var showDialogMethod = dialogUiType.GetMethod(
                    "ShowDialog",
                    ReflectionFlags,
                    null,
                    new[]
                    {
                        typeof(CallDialogType),
                        FindType("NavigationBlocker"),
                        FindType("Contact"),
                        typeof(Action),
                        FindType("ThirdPersonCharacter")
                    },
                    null);

                if (showDialogMethod == null)
                    throw new InvalidOperationException("StreetQuestRPG: Could not resolve DialogUI.ShowDialog.");

                showDialogMethod.Invoke(dialogUi, new object[] { dialogType, null, null, null, null });
                LogDebug($"TryOpenQuestDialog success dialogType={dialogType}");
            }
            catch (Exception exception)
            {
                LogDebug($"TryOpenQuestDialog failed: {exception}");
                Debug.LogWarning($"StreetQuestRPG: Failed to open physical quest dialog. {exception}");
            }
        }
    }
}
