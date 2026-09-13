#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Items;
using Buildings.Indoors.InteriorDesign;
using CameraControllers;
using GamePrompt.Runtime.Scripts;
using Helpers;
using UnityEngine;
using UnityEngine.EventSystems;

// Build 3675 can run the Steam valuation goal in GameManager.Awake before
// initialization-scope mods register saved vehicle types. The exception aborts
// Awake; registering the types later does not execute its remaining statements.
// Resume only that verified tail, once, after the game's load event.
internal static class Porsche911GT3RSLoadRecovery
{
    private static readonly MethodInfo? ExitBuildingMethod = typeof(GameManager).GetMethod(
        "OnExitBuilding", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? RefreshAchievementsMethod = typeof(GameManager).GetMethod(
        "ForceUpdateAchievementsOnSteam", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? PersonalGoalsField = typeof(GameManager).GetField(
        "personalGoals", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static GameManager? attemptedManager;

    internal static void CompleteInterruptedLoad(ModContext? context)
    {
        var manager = InstanceBehavior<GameManager>.Instance;
        var save = SaveGameManager.Current;
        if (manager == null || save?.privateDriverVehicleInstances == null || context == null ||
            ReferenceEquals(attemptedManager, manager))
            return;

        var hasPorsche = false;
        foreach (var vehicle in save.privateDriverVehicleInstances)
            hasPorsche |= vehicle != null && string.Equals(vehicle.vehicleTypeName,
                Porsche911GT3RSMod.VehicleTypeName, StringComparison.Ordinal);
        if (!hasPorsche)
            return;

        attemptedManager = manager;
        var exitRegistered = HasNativeExitCallback(manager);
        context.Logger.Info($"Porsche911GT3RS: chauffeur save startup check: " +
            $"nativeAwakeTail={exitRegistered}, interiorReady={InteriorDesignerHelper.TimeOfDayController != null}, " +
            $"playerRecords={save.VehicleInstances?.Count ?? 0}, chauffeurRecords={save.privateDriverVehicleInstances.Count}.");
        if (exitRegistered)
            return;

        // Multiple sentinels keep this from replaying an earlier, unrelated Awake
        // failure or overwriting a successfully initialized city.
        if (ExitBuildingMethod == null || RefreshAchievementsMethod == null ||
            PersonalGoalsField?.GetValue(manager) == null ||
            InteriorDesignerHelper.TimeOfDayController != null ||
            manager.indoorPlacementCamera == null || manager.timeOfDayController == null ||
            save.VehicleInstances == null)
        {
            context.Logger.Warn("Porsche911GT3RS: startup recovery skipped: native initialization state differs from the supported interrupted-Awake path.");
            return;
        }

        foreach (var vehicle in save.VehicleInstances)
        {
            if (vehicle == null || vehicle.VehicleType == null)
            {
                context.Logger.Warn($"Porsche911GT3RS: startup recovery stopped: player vehicle type still unresolved: '{vehicle?.vehicleTypeName ?? "<null record>"}'.");
                return;
            }
        }
        foreach (var vehicle in save.privateDriverVehicleInstances)
        {
            if (vehicle == null || vehicle.VehicleType == null)
            {
                context.Logger.Warn($"Porsche911GT3RS: startup recovery stopped: chauffeur vehicle type still unresolved: '{vehicle?.vehicleTypeName ?? "<null record>"}'.");
                return;
            }
        }

        try
        {
            var exitCallback = (Action<Address>)Delegate.CreateDelegate(
                typeof(Action<Address>), manager, ExitBuildingMethod);
            var placement = manager.indoorPlacementCamera.GetComponentInParent<PlacementCam>();
            if (placement == null)
                throw new InvalidOperationException("Native placement camera is unavailable.");

            // Let the real valuation run with the now-resolved types. No fabricated
            // vehicle prices, removed save records, or swallowed valuation errors.
            RefreshAchievementsMethod.Invoke(manager, null);
            if (manager.ForceLOD0)
            {
                foreach (var group in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
                    group.ForceLOD(0);
            }
            GlobalEvents.onExitBuilding += exitCallback;
            GamePromptManager.StartCollecting();
            InteriorDesignerHelper.Init(manager.timeOfDayController, placement, false);
            TutorialHelper.Init();
            context.Logger.Info("Porsche911GT3RS: completed interrupted city initialization after vehicle registration; native valuation, exit callback, interior state and tutorial initialization succeeded.");
        }
        catch (Exception exception)
        {
            var failure = exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException : exception;
            context.Logger.Warn($"Porsche911GT3RS: interrupted-load recovery failed: {failure}");
        }
    }

    internal static void ReportInputState(ModContext? context)
    {
        if (context == null || attemptedManager == null)
            return;
        var events = EventSystem.current;
        var hits = new List<RaycastResult>();
        if (events != null)
            events.RaycastAll(new PointerEventData(events) { position = new Vector2(Screen.width / 2f, Screen.height / 2f) }, hits);
        var top = hits.Count == 0 ? "<none>" : hits[0].gameObject.name;
        context.Logger.Info($"Porsche911GT3RS: post-load input state: eventSystem={events != null}, " +
            $"enabled={events != null && events.isActiveAndEnabled}, module='{events?.currentInputModule?.GetType().Name ?? "<none>"}', " +
            $"centerUiHit='{top}', loading={UI.Load.LoadScene.isLoading}, nativeAwakeTail={HasNativeExitCallback(attemptedManager)}.");
    }

    private static bool HasNativeExitCallback(GameManager manager)
    {
        if (ExitBuildingMethod == null)
            return false;
        var callbacks = GlobalEvents.onExitBuilding?.GetInvocationList();
        if (callbacks == null)
            return false;
        foreach (var callback in callbacks)
            if (ReferenceEquals(callback.Target, manager) && callback.Method == ExitBuildingMethod)
                return true;
        return false;
    }
}
