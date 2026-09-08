#nullable enable
using BAModAPI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeveloperTools
{
    [DefaultExecutionOrder(-10000)]
    internal sealed class DeveloperToolsRuntime : MonoBehaviour
    {
        private ModContext? context;
        private DeveloperToolsSettings? settings;
        private DeveloperToolsOverlay? overlay;
        private DeveloperToolsPlayerService? playerService;
        private DeveloperToolsMapTeleport? mapTeleport;

        public static DeveloperToolsRuntime Initialize(ModContext context, DeveloperToolsSettings settings)
        {
            var existing = FindObjectOfType<DeveloperToolsRuntime>();
            if (existing != null)
                Destroy(existing.gameObject);

            var gameObject = new GameObject(nameof(DeveloperToolsRuntime));
            DontDestroyOnLoad(gameObject);
            var runtime = gameObject.AddComponent<DeveloperToolsRuntime>();
            runtime.context = context;
            runtime.settings = settings;
            runtime.playerService = new DeveloperToolsPlayerService(context);
            runtime.overlay = new DeveloperToolsOverlay(
                context,
                new DeveloperToolsVehicleService(context),
                new DeveloperToolsItemService(context),
                runtime.playerService,
                new DeveloperToolsTimeService(context));
            runtime.mapTeleport = new DeveloperToolsMapTeleport(context, runtime.playerService);
            return runtime;
        }

        public void Shutdown()
        {
            overlay?.Shutdown();
            Destroy(gameObject);
        }

        private void OnEnable() => SceneManager.sceneLoaded += HandleSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= HandleSceneLoaded;

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => overlay?.Hide();

        private void Update()
        {
            if (settings == null || overlay == null || playerService == null)
                return;

            if (settings.UiHotkey != KeyCode.None && Input.GetKeyDown(settings.UiHotkey))
                overlay.Toggle();
            if (settings.MoneyHotkey != KeyCode.None && Input.GetKeyDown(settings.MoneyHotkey))
                playerService.AddMoney(10000000f, out _);
            if (settings.NeedsHotkey != KeyCode.None && Input.GetKeyDown(settings.NeedsHotkey))
                playerService.ResetNeeds(out _);
            if (overlay.IsVisible && Input.GetKeyDown(KeyCode.Escape))
                overlay.Hide();

            if (overlay.ShouldConsumeGameplayInput)
                overlay.ConsumeGameplayInput();
        }

        private void LateUpdate()
        {
            mapTeleport?.Update(overlay?.IsPointerInsideWindow ?? false);
            overlay?.SuppressUnexpectedMiniMenu();
        }

        private void OnGUI() => overlay?.OnGui();
    }
}
