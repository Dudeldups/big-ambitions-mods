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
        private DeveloperToolsTimeService? timeService;
        private DeveloperToolsTrafficService? trafficService;

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
            runtime.playerService = new DeveloperToolsPlayerService();
            runtime.timeService = new DeveloperToolsTimeService(context);
            runtime.trafficService = new DeveloperToolsTrafficService(context);
            runtime.trafficService.PrepareTrafficPoolCapacity();
            runtime.overlay = new DeveloperToolsOverlay(
                context,
                new DeveloperToolsVehicleService(context),
                new DeveloperToolsItemService(context),
                runtime.playerService,
                runtime.timeService,
                runtime.trafficService,
                new DeveloperToolsPauseService(context));
            runtime.mapTeleport = new DeveloperToolsMapTeleport(context, runtime.playerService);
            return runtime;
        }

        public void Shutdown()
        {
            GlobalEvents.onNewHour -= HandleNewHour;
            overlay?.Shutdown();
            timeService?.Shutdown();
            trafficService?.Shutdown();
            Destroy(gameObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            GlobalEvents.onNewHour -= HandleNewHour;
            GlobalEvents.onNewHour += HandleNewHour;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            GlobalEvents.onNewHour -= HandleNewHour;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            GlobalEvents.onNewHour -= HandleNewHour;
            GlobalEvents.onNewHour += HandleNewHour;
            trafficService?.PrepareTrafficPoolCapacity();
            overlay?.Hide();
        }

        private void HandleNewHour()
        {
            if (trafficService != null)
                StartCoroutine(trafficService.ReapplyTrafficAfterHourlyUpdate());
        }

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
        }

        private void OnGUI() => overlay?.OnGui();
    }
}
