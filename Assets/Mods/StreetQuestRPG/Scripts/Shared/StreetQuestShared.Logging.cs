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
        public static void InitializeDebugLogging(ModContext context, string source)
        {
            try
            {
                DebugLogDirectory = ResolveDebugLogDirectory(context?.ModRootPath);
                Directory.CreateDirectory(DebugLogDirectory);
                DebugLogFilePath = Path.Combine(DebugLogDirectory, "streetquest-debug.log");

                lock (LogSync)
                {
                    File.AppendAllText(
                        DebugLogFilePath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{source}] Logging initialized. ModRootPath={context?.ModRootPath ?? "<null>"}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }

        private static string ResolveDebugLogDirectory(string modRootPath)
        {
            if (!string.IsNullOrWhiteSpace(PreferredWorkspaceLogDirectory))
                return PreferredWorkspaceLogDirectory;

            if (!string.IsNullOrWhiteSpace(modRootPath))
            {
                try
                {
                    var normalizedRoot = Path.GetFullPath(modRootPath);
                    if (normalizedRoot.IndexOf(Path.Combine("Assets", "Mods"), StringComparison.OrdinalIgnoreCase) >= 0)
                        return Path.GetFullPath(Path.Combine(normalizedRoot, "..", "..", "Logs"));

                    return Path.Combine(normalizedRoot, "Logs");
                }
                catch
                {
                }
            }

            return Path.Combine(Application.persistentDataPath, "StreetQuestRPG", "Logs");
        }


        private static void ShowDebugNotification(string message, string duplicateIdentifier)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                Notifications.Show(
                    NotificationType.Info,
                    message,
                    null,
                    6f,
                    duplicateIdentifier,
                    null,
                    false,
                    false);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to show debug notification. {exception}");
                Debug.Log(message);
            }
        }


        private static void ShowInfoNotification(string message, string duplicateIdentifier = null, float duration = 6f)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                Notifications.Show(
                    NotificationType.Info,
                    message,
                    null,
                    duration,
                    duplicateIdentifier,
                    null,
                    false,
                    false);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to show info notification. {exception}");
                Debug.Log(message);
            }
        }

        internal static void NotifyInfo(string message, string duplicateIdentifier = null, float duration = 4f)
        {
            ShowInfoNotification(message, duplicateIdentifier, duration);
        }


        internal static void LogDebug(string message)
        {
            if (string.IsNullOrWhiteSpace(DebugLogFilePath))
                return;

            try
            {
                lock (LogSync)
                {
                    File.AppendAllText(
                        DebugLogFilePath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }


        public static void LogBootstrapState(string source)
        {
            try
            {
                var characterCount = StreetQuestCharacterCatalog.All.Count;
                var questCount = StreetQuestQuestCatalog.All.Count;
                LogDebug($"BootstrapState source={source} characters={characterCount} quests={questCount}");
            }
            catch (Exception exception)
            {
                LogDebug($"BootstrapState source={source} failed: {exception}");
            }
        }


        public static void LogConfigLoadFailure(string configType, string path, Exception exception)
        {
            LogDebug($"ConfigLoadFailure type={configType} path={path} exception={exception}");
        }


        internal static void LogSchedule(string message)
        {
            LogDebug($"Schedule: {message}");
        }
    }
}
