using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Characters;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Dialogs;
using Entities;
using Helpers;
using Localizor;
using Player.HUD.ItemInfoOverlays;
using UI.Notification;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        public static void CleanupLegacyContacts()
        {
            try
            {
                RemoveLegacyQuestGiverCtaBehaviors();
            }
            catch (Exception exception)
            {
                LogDebug($"CleanupLegacyContacts failed: {exception}");
                Debug.LogWarning($"StreetQuestRPG: Failed to clean legacy runtime state. {exception}");
            }
        }
    }
}
