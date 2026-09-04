using System;
using System.Reflection;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        public static void CleanupLegacyContacts()
        {
            try
            {
                RemoveLegacyStreetQuestCtaBehaviors();
            }
            catch (Exception exception)
            {
                LogDebug($"CleanupLegacyContacts failed: {exception}");
                Debug.LogWarning($"StreetQuestRPG: Failed to clean legacy runtime state. {exception}");
            }
        }

        private static void RemoveLegacyStreetQuestCtaBehaviors()
        {
            var ctaManagerType = FindType("CtaManager");
            var ctaBehaviorsField = ctaManagerType?.GetField("CtaBehaviors", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var list = ctaBehaviorsField?.GetValue(null) as System.Collections.IList;
            if (list == null)
                return;

            for (var index = list.Count - 1; index >= 0; index--)
            {
                var behavior = list[index];
                if (behavior == null)
                    continue;
                var typeName = behavior.GetType().FullName ?? string.Empty;
                if (typeName.IndexOf("StreetQuestGiverCtaBehavior", StringComparison.Ordinal) >= 0)
                    list.RemoveAt(index);
            }
        }
    }
}
