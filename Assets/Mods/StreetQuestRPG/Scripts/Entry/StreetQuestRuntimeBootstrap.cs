using System;
using System.Linq;
using BAModAPI;
using Dialogs;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static class StreetQuestRuntimeBootstrap
    {
        private static GameObject _watcherObject;
        private static string _modRootPath;
        private static IModLogger _logger;
        private static bool _dialogsRegistered;

        internal static string CurrentModRootPath => _modRootPath;

        public static void Configure(ModContext context, string source)
        {
            if (context == null)
                return;

            StreetQuestShared.InitializeDebugLogging(context, source);
            StreetQuestShared.SetRuntimeShutdownInProgress(false);
            _modRootPath = context.ModRootPath;
            _logger = context.Logger;
            StreetQuestShared.ResetSpawnRuntimeState();
            StreetQuestShared.ResetIndoorBuildingContext();
            StreetQuestCharacterRuntimeResolver.ClearCache();
            StreetQuestShared.ClearScheduleCaches();
            StreetQuestCharacterCatalog.Reload(_modRootPath, _logger);
            StreetQuestQuestCatalog.Reload(_modRootPath, _logger);
            _dialogsRegistered = false;
            if (_watcherObject != null)
            {
                var watcher = _watcherObject.GetComponent<StreetQuestPhysicalQuestGiverWatcher>();
                watcher?.ResetRuntimeState();
            }
            StreetQuestShared.LogBootstrapState($"Configure source={source}");
        }

        public static void EnsureWatcher()
        {
            if (_watcherObject == null)
            {
                _watcherObject = new GameObject("StreetQuestRPG.PhysicalQuestGiverWatcher");
                UnityEngine.Object.DontDestroyOnLoad(_watcherObject);
            }

            var watcher = _watcherObject.GetComponent<StreetQuestPhysicalQuestGiverWatcher>();
            if (watcher == null)
                watcher = _watcherObject.AddComponent<StreetQuestPhysicalQuestGiverWatcher>();

            watcher.Initialize();

            var mapMarkerWatcher = _watcherObject.GetComponent<StreetQuestMapMarkerWatcher>();
            if (mapMarkerWatcher == null)
                mapMarkerWatcher = _watcherObject.AddComponent<StreetQuestMapMarkerWatcher>();

            mapMarkerWatcher.Initialize();
        }

        public static bool EnsureCityRuntimeReady()
        {
            if (string.IsNullOrWhiteSpace(_modRootPath))
            {
                StreetQuestShared.LogBootstrapState("EnsureCityRuntimeReady skipped: modRootPath missing");
                return false;
            }

            StreetQuestCharacterCatalog.Initialize(_modRootPath, _logger);
            StreetQuestQuestCatalog.Initialize(_modRootPath, _logger);
            StreetQuestShared.LogBootstrapState("EnsureCityRuntimeReady after initialize");

            StreetQuestShared.PrimeApartmentVisitFromPersistedIndoorState();
            StreetQuestShared.CleanupLegacyContacts();
            RegisterDialogs();
            StreetQuestShared.RefreshStreetQuestContactDialogOverrides();
            return StreetQuestCharacterCatalog.All.Any(value => value != null);
        }

        public static void Shutdown()
        {
            StreetQuestShared.SetRuntimeShutdownInProgress(true);
            if (_watcherObject != null)
            {
                UnityEngine.Object.Destroy(_watcherObject);
                _watcherObject = null;
            }

            _dialogsRegistered = false;
        }

        private static void RegisterDialogs()
        {
            if (_dialogsRegistered)
                return;

            foreach (var character in StreetQuestCharacterCatalog.All.Where(value => value != null))
            {
                if (!character.interactable)
                    continue;

                var dialogTypeKey = string.IsNullOrWhiteSpace(character.dialogTypeKey)
                    ? "streetquest_mack_dialog"
                    : character.dialogTypeKey;
                var questDialogType = (CallDialogType)ModEnumHash.GetSafeHash(dialogTypeKey);
                var capturedCharacterId = character.id;
                CallDialogFactory.RegisterDialog(questDialogType, () => new StreetQuestCharacterDialog(capturedCharacterId));
            }

            _dialogsRegistered = true;
        }
    }
}
