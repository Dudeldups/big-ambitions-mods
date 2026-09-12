#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BAModAPI;
using BigAmbitions.SaveSystem.Legacy;
using Data.VehicleColors;
using Helpers;
using Localizor;
using UnityEngine;
using Vehicles.VehicleTypes;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsVehicleService
    {
        private const float SpawnDistance = 7f;
        private static readonly MethodInfo? StopEngineMethod = typeof(CarController).GetMethod(
            "StopEngine",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly List<CatalogEntry> vanillaEntries = new List<CatalogEntry>();
        private readonly List<CatalogEntry> moddedEntries = new List<CatalogEntry>();
        private readonly List<VehicleColorEntry> colorEntries = new List<VehicleColorEntry>();
        private readonly List<VehicleColorEntry> recolorColorEntries = new List<VehicleColorEntry>();
        private readonly Dictionary<string, VehicleColor> developerColors =
            new Dictionary<string, VehicleColor>(StringComparer.Ordinal);
        private readonly List<VehicleColor> ownedDeveloperColors = new List<VehicleColor>();
        private readonly ModContext context;
        private string lastSpawnedVehicleId = string.Empty;

        private static readonly DeveloperColorDefinition[] AdditionalRecolorColors =
        {
            new DeveloperColorDefinition("DeveloperTools_Onyx", "Onyx", new Color32(20, 23, 28, 255), new Color32(70, 78, 90, 255), 2f),
            new DeveloperColorDefinition("DeveloperTools_Graphite", "Graphite", new Color32(65, 68, 72, 255), new Color32(125, 130, 138, 255), 2f),
            new DeveloperColorDefinition("DeveloperTools_Pearl", "Pearl", new Color32(215, 220, 225, 255), new Color32(255, 255, 255, 255), 2f),
            new DeveloperColorDefinition("DeveloperTools_Cream", "Cream", new Color32(250, 225, 175, 255), new Color32(255, 245, 215, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_Burgundy", "Burgundy", new Color32(105, 16, 38, 255), new Color32(185, 65, 90, 255), 2f),
            new DeveloperColorDefinition("DeveloperTools_Scarlet", "Scarlet", new Color32(220, 30, 20, 255), new Color32(255, 105, 80, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_Coral", "Coral", new Color32(238, 83, 74, 255), new Color32(255, 160, 140, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_Tangerine", "Tangerine", new Color32(245, 125, 25, 255), new Color32(255, 190, 95, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_Copper", "Copper", new Color32(166, 79, 45, 255), new Color32(235, 145, 95, 255), 2f),
            new DeveloperColorDefinition("DeveloperTools_Gold", "Gold", new Color32(196, 145, 35, 255), new Color32(255, 220, 115, 255), 2f),
            new DeveloperColorDefinition("DeveloperTools_Lemon", "Lemon", new Color32(240, 225, 35, 255), new Color32(255, 250, 120, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_Lime", "Lime", new Color32(104, 190, 35, 255), new Color32(180, 255, 100, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_Forest", "Forest", new Color32(25, 85, 40, 255), new Color32(80, 170, 100, 255), 2f),
            new DeveloperColorDefinition("DeveloperTools_Emerald", "Emerald", new Color32(0, 120, 72, 255), new Color32(70, 220, 145, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_Turquoise", "Turquoise", new Color32(0, 157, 154, 255), new Color32(80, 240, 230, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_Cyan", "Cyan", new Color32(0, 174, 239, 255), new Color32(95, 225, 255, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_SkyBlue", "Sky Blue", new Color32(85, 180, 240, 255), new Color32(165, 225, 255, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_Azure", "Azure", new Color32(0, 112, 221, 255), new Color32(90, 185, 255, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_MidnightBlue", "Midnight Blue", new Color32(15, 30, 70, 255), new Color32(60, 90, 160, 255), 2f),
            new DeveloperColorDefinition("DeveloperTools_Violet", "Violet", new Color32(105, 66, 180, 255), new Color32(175, 135, 255, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_Lavender", "Lavender", new Color32(170, 125, 215, 255), new Color32(225, 190, 255, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_Magenta", "Magenta", new Color32(194, 0, 151, 255), new Color32(255, 90, 225, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_HotPink", "Hot Pink", new Color32(255, 80, 165, 255), new Color32(255, 170, 215, 255), 1f),
            new DeveloperColorDefinition("DeveloperTools_Rose", "Rose", new Color32(230, 70, 125, 255), new Color32(255, 150, 190, 255), 1f)
        };

        public DeveloperToolsVehicleService(ModContext context) => this.context = context;
        public IReadOnlyList<CatalogEntry> VanillaEntries => vanillaEntries;
        public IReadOnlyList<CatalogEntry> ModdedEntries => moddedEntries;
        public IReadOnlyList<VehicleColorEntry> ColorEntries => colorEntries;
        public IReadOnlyList<VehicleColorEntry> RecolorColorEntries => recolorColorEntries;

        public void Refresh()
        {
            vanillaEntries.Clear();
            moddedEntries.Clear();
            foreach (var id in VehicleTypeHelper.GetVehicleTypeNames().Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
            {
                if (VehicleTypeHelper.GetVehicleType(id) != null)
                {
                    var entry = new CatalogEntry(id, Localize(id));
                    (IsModdedVehicleType(id) ? moddedEntries : vanillaEntries).Add(entry);
                }
            }
            vanillaEntries.Sort(CompareEntries);
            moddedEntries.Sort(CompareEntries);
            RefreshColors();
        }

        public bool Spawn(string vehicleTypeName, string vehicleColorName, out string message)
        {
            var player = PlayerHelper.PlayerController;
            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicleTypeName);
            var selectedColorName = vehicleColorName ?? string.Empty;
            if (player == null || vehicleType == null)
            {
                message = player == null ? "Player is not available." : "Selected vehicle is no longer registered.";
                context.Logger.Warn("DeveloperTools: vehicle spawn failed; " + message);
                return false;
            }

            try
            {
                var forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.forward;
                var rotation = Quaternion.LookRotation(forward, Vector3.up);
                var position = player.transform.position + forward * SpawnDistance + Vector3.up * 0.5f;
                var instance = new VehicleInstance(vehicleTypeName)
                {
                    id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
                    fuel = vehicleType.maxFuel * 0.98f,
                    vehicleColorName = selectedColorName
                };
                var controller = VehicleHelper.CreateAndSpawnVehicle(instance, position, rotation);
                if (controller == null)
                {
                    message = "The game did not return a spawned vehicle controller.";
                    context.Logger.Warn("DeveloperTools: vehicle spawn failed; type=" + vehicleTypeName + ", reason=no controller.");
                    return false;
                }

                VehicleHelper.TeleportVehicleToGround(controller, position, rotation);
                NotifyModVehicleCreated(controller, vehicleTypeName);
                var colorName = ApplyRegisteredColor(controller, instance, selectedColorName);
                NormalizeParkedMotorVehicle(controller, vehicleTypeName);
                lastSpawnedVehicleId = instance.id;
                message = "Spawned " + Localize(vehicleTypeName) +
                          (string.IsNullOrEmpty(colorName) ? "." : " with color " + colorName + ".");
                return true;
            }
            catch (Exception exception)
            {
                message = "Vehicle spawn failed: " + exception.Message;
                context.Logger.Error(exception);
                return false;
            }
        }

        public bool DespawnLast(out string message)
        {
            if (string.IsNullOrEmpty(lastSpawnedVehicleId))
            {
                message = "No test vehicle has been spawned in this session.";
                return false;
            }

            var controller = VehicleHelper.AllPlayerVehicles?.FirstOrDefault(value =>
                value?.vehicleInstance != null && string.Equals(value.vehicleInstance.id, lastSpawnedVehicleId, StringComparison.Ordinal));
            if (controller == null)
            {
                message = "The last spawned vehicle is no longer present.";
                lastSpawnedVehicleId = string.Empty;
                return false;
            }

            VehicleHelper.Delete(controller.vehicleInstance, controller);
            lastSpawnedVehicleId = string.Empty;
            message = "Despawned the last test vehicle.";
            return true;
        }

        public bool RepairVehicle(out string message)
        {
            var activeVehicleId = SaveGameManager.Current?.ActiveVehicleId;
            var controller = FindVehicleController(activeVehicleId) ?? FindVehicleController(lastSpawnedVehicleId);
            if (controller?.vehicleInstance == null)
            {
                message = "Enter a vehicle or spawn one before repairing it.";
                return false;
            }

            try
            {
                var recoveredMalformedDeformation = false;
                try
                {
                    controller.Repair();
                }
                catch (IndexOutOfRangeException)
                {
                    // Some modded vehicles have a different number of live mesh
                    // filters and authored original meshes. The game's reset loop
                    // indexes both arrays by the live-filter count and throws after
                    // its saved and physics damage state has already been repaired.
                    recoveredMalformedDeformation = true;
                }

                controller.vehicleInstance.damage = 0f;
                controller.vehicleInstance.deformations?.Clear();
                foreach (var deformation in controller.GetComponentsInChildren<VehicleDeformationController>(true))
                    if (deformation != null)
                        recoveredMalformedDeformation |= ResetDeformationSafely(deformation);

                SaveGameManager.MarkChange();
                GlobalEvents.onVehicleVariablesChanged?.Invoke();
                message = "Repaired " + Localize(controller.vehicleInstance.vehicleTypeName) + ".";
                if (recoveredMalformedDeformation && DeveloperToolsDiagnostics.Enabled)
                {
                    context.Logger.Info(
                        "DeveloperTools: repaired a vehicle with mismatched deformation mesh arrays; type=" +
                        controller.vehicleInstance.vehicleTypeName + ".");
                }
                return true;
            }
            catch (Exception exception)
            {
                message = "Vehicle repair failed: " + exception.GetBaseException().Message;
                context.Logger.Error(exception);
                return false;
            }
        }

        public bool RecolorVehicle(string colorName, out string message)
        {
            EnsureDeveloperColors();
            var controller = InstanceBehavior<GameManager>.Instance?.selectedVehicle ??
                             FindVehicleController(SaveGameManager.Current?.ActiveVehicleId) ??
                             FindVehicleController(lastSpawnedVehicleId);
            if (controller?.vehicleInstance == null || controller.CarFeatures == null)
            {
                message = "Enter a vehicle or spawn one before recoloring it.";
                return false;
            }

            if (!TryResolveColor(colorName, out var color))
            {
                message = "The selected vehicle color is no longer available.";
                return false;
            }

            try
            {
                controller.vehicleInstance.vehicleColorName = colorName;
                controller.CarFeatures.SetColor(color);
                SaveGameManager.MarkChange();
                GlobalEvents.onVehicleVariablesChanged?.Invoke();
                GameEvent.Invoke("developer-tools:vehicle-recolor");
                var displayName = recolorColorEntries
                    .FirstOrDefault(entry => entry.Name == colorName)?.DisplayName ?? colorName;
                message = "Recolored " + Localize(controller.vehicleInstance.vehicleTypeName) +
                          " to " + displayName + ".";
                context.Logger.Info(
                    "DeveloperTools: recolored vehicle type=" + controller.vehicleInstance.vehicleTypeName +
                    ", id=" + controller.vehicleInstance.id +
                    ", color=" + colorName +
                    ", extended=" + developerColors.ContainsKey(colorName) + ".");
                return true;
            }
            catch (Exception exception)
            {
                message = "Vehicle recolor failed: " + exception.GetBaseException().Message;
                context.Logger.Error(exception);
                return false;
            }
        }

        public void RestoreDeveloperColor(VehicleController controller)
        {
            EnsureDeveloperColors();
            if (controller?.vehicleInstance == null || controller.CarFeatures == null)
                return;

            var colorName = controller.vehicleInstance.vehicleColorName;
            if (string.IsNullOrWhiteSpace(colorName) ||
                !developerColors.TryGetValue(colorName, out var color))
                return;

            try
            {
                controller.CarFeatures.SetColor(color);
                GameEvent.Invoke("developer-tools:vehicle-recolor-restored");
            }
            catch (Exception exception)
            {
                context.Logger.Warn(
                    "DeveloperTools: could not restore extended vehicle color type=" +
                    controller.vehicleInstance.vehicleTypeName +
                    ", id=" + controller.vehicleInstance.id +
                    ", color=" + colorName +
                    ": " + exception.GetBaseException().Message);
            }
        }

        public void Shutdown()
        {
            foreach (var color in ownedDeveloperColors)
                if (color != null) UnityEngine.Object.Destroy(color);
            ownedDeveloperColors.Clear();
            developerColors.Clear();
            recolorColorEntries.Clear();
        }

        private static bool ResetDeformationSafely(VehicleDeformationController deformation)
        {
            var meshFilters = deformation.meshFilters ?? Array.Empty<MeshFilter>();
            var originalMeshes = deformation.originalMeshes ?? Array.Empty<Mesh>();
            var matchingCount = Math.Min(meshFilters.Length, originalMeshes.Length);
            for (var index = 0; index < matchingCount; index++)
            {
                if (meshFilters[index] != null && originalMeshes[index] != null)
                    meshFilters[index].mesh = originalMeshes[index];
            }

            return meshFilters.Length != originalMeshes.Length;
        }

        private static VehicleController? FindVehicleController(string? vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId))
                return null;

            return VehicleHelper.AllPlayerVehicles?.FirstOrDefault(value =>
                value?.vehicleInstance != null &&
                string.Equals(value.vehicleInstance.id, vehicleId, StringComparison.Ordinal));
        }

        private void NotifyModVehicleCreated(VehicleController controller, string vehicleTypeName)
        {
            if (!IsModdedVehicleType(vehicleTypeName) || GlobalEvents.onEnterVehicle == null)
                return;

            // The game exposes no vehicle-created event. Vehicle mods therefore
            // commonly use their enter callback to configure newly discovered
            // controllers. Notify external listeners individually before the
            // first real entry, but never run the game's own enter listeners or
            // mutate player/vehicle occupancy state.
            var gameAssembly = typeof(VehicleController).Assembly;
            foreach (var callback in GlobalEvents.onEnterVehicle.GetInvocationList())
            {
                if (callback.Method.DeclaringType?.Assembly == gameAssembly)
                    continue;

                try
                {
                    if (callback is Action<VehicleController> vehicleCallback)
                        vehicleCallback(controller);
                }
                catch (Exception exception)
                {
                    context.Logger.Warn(
                        "DeveloperTools: an external vehicle initializer failed for type=" +
                        vehicleTypeName + ": " + exception.GetBaseException().Message);
                }
            }
        }

        private void NormalizeParkedMotorVehicle(VehicleController controller, string vehicleTypeName)
        {
            if (controller is not CarController carController || StopEngineMethod == null)
                return;

            try
            {
                // A freshly spawned parked car must begin stopped. Some modded
                // prefabs serialize their engine as already running, causing the
                // first StartEngine call to be ignored with RPM stuck at zero.
                // Use the same transition as a normal vehicle exit so the first
                // real entry performs a complete engine start.
                StopEngineMethod.Invoke(carController, null);
            }
            catch (Exception exception)
            {
                context.Logger.Warn(
                    "DeveloperTools: could not normalize the parked engine for type=" +
                    vehicleTypeName + ": " + exception.GetBaseException().Message);
            }
        }

        public string GetDefaultRedColorName()
            => GetClosestRedColorName(colorEntries);

        public string GetDefaultRecolorColorName()
            => GetClosestRedColorName(recolorColorEntries);

        private static string GetClosestRedColorName(IReadOnlyCollection<VehicleColorEntry> entries)
        {
            if (entries.Count == 0)
                return string.Empty;

            var target = Color.red;
            return entries
                .OrderBy(entry =>
                {
                    var difference = (Vector4)entry.Tint - (Vector4)target;
                    return difference.sqrMagnitude;
                })
                .First().Name;
        }

        private void RefreshColors()
        {
            colorEntries.Clear();
            recolorColorEntries.Clear();
            var colors = InstanceBehavior<GlobalReferences>.Instance?.vehicleColors;
            var originalIndex = 0;
            if (colors != null)
            {
                foreach (var color in colors.Where(value => value != null))
                {
                    var name = ((UnityEngine.Object)color).name;
                    if (string.IsNullOrWhiteSpace(name) || colorEntries.Any(entry => entry.Name == name))
                        continue;
                    var entry = new VehicleColorEntry(name, name, color.tint, originalIndex++);
                    colorEntries.Add(entry);
                    recolorColorEntries.Add(entry);
                }
            }

            EnsureDeveloperColors();
            foreach (var definition in AdditionalRecolorColors)
            {
                if (developerColors.TryGetValue(definition.Name, out var color))
                {
                    recolorColorEntries.Add(new VehicleColorEntry(
                        definition.Name,
                        definition.DisplayName,
                        color.tint,
                        originalIndex++));
                }
            }
            colorEntries.Sort(CompareColors);
            recolorColorEntries.Sort(CompareColors);
        }

        private static string ApplyRegisteredColor(
            VehicleController controller,
            VehicleInstance instance,
            string colorName)
        {
            if (controller.CarFeatures == null || string.IsNullOrWhiteSpace(colorName) ||
                !VehicleHelper.TryGetVehicleColor(colorName, out VehicleColor color) || color == null)
                return string.Empty;

            instance.vehicleColorName = colorName;
            controller.CarFeatures.SetColor(color);
            return colorName;
        }

        private void EnsureDeveloperColors()
        {
            if (developerColors.Count > 0)
                return;

            foreach (var definition in AdditionalRecolorColors)
            {
                var color = ScriptableObject.CreateInstance<VehicleColor>();
                ((UnityEngine.Object)color).name = definition.Name;
                color.tint = definition.Tint;
                color.fresnelColor = definition.FresnelColor;
                color.fresnelPower = definition.FresnelPower;
                color.randomWeight = 0f;
                color.hideFlags = HideFlags.HideAndDontSave;
                developerColors[definition.Name] = color;
                ownedDeveloperColors.Add(color);
            }
        }

        private bool TryResolveColor(string colorName, out VehicleColor color)
        {
            color = null!;
            return !string.IsNullOrWhiteSpace(colorName) &&
                   (developerColors.TryGetValue(colorName, out color) ||
                    VehicleHelper.TryGetVehicleColor(colorName, out color));
        }

        private static string Localize(string id)
        {
            try
            {
                var localized = id.GetLocalization()?.ToString();
                return string.IsNullOrWhiteSpace(localized) ? id : localized!;
            }
            catch
            {
                return id;
            }
        }

        private static bool IsModdedVehicleType(string id)
        {
            if (VehicleTypeHelper.IsModVehicleType(id))
                return true;

            // Big Ambitions' own ids use the "ba" namespace. Some compatible
            // vehicle registrars add a namespaced type to the shared catalog
            // without also adding it to the API's separate mod-type index.
            // Classify every non-game namespace as modded instead of maintaining
            // a list of particular mods or vehicle ids.
            var separatorIndex = id.IndexOf(':');
            return separatorIndex > 0 &&
                   !string.Equals(id.Substring(0, separatorIndex), "ba", StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareEntries(CatalogEntry left, CatalogEntry right) =>
            string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);

        private static int CompareColors(VehicleColorEntry left, VehicleColorEntry right)
        {
            var comparison = left.Group.CompareTo(right.Group);
            if (comparison != 0) return comparison;
            comparison = left.Hue.CompareTo(right.Hue);
            if (comparison != 0) return comparison;
            comparison = left.Value.CompareTo(right.Value);
            if (comparison != 0) return comparison;
            comparison = right.Saturation.CompareTo(left.Saturation);
            return comparison != 0 ? comparison : left.OriginalIndex.CompareTo(right.OriginalIndex);
        }
    }

    internal sealed class CatalogEntry
    {
        public CatalogEntry(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }
        public string DisplayName { get; }
    }

    internal sealed class VehicleColorEntry
    {
        public VehicleColorEntry(string name, string displayName, Color tint, int originalIndex)
        {
            Name = name;
            DisplayName = displayName;
            Tint = tint;
            OriginalIndex = originalIndex;
            Color.RGBToHSV(tint, out var hue, out var saturation, out var value);
            Group = saturation < 0.14f ? 0 : 1;
            Hue = Group == 0 ? 0f : hue >= 0.95f && saturation >= 0.5f ? hue - 1f : hue;
            Saturation = saturation;
            Value = value;
        }

        public string Name { get; }
        public string DisplayName { get; }
        public Color Tint { get; }
        public int Group { get; }
        public float Hue { get; }
        public float Saturation { get; }
        public float Value { get; }
        public int OriginalIndex { get; }
    }

    internal readonly struct DeveloperColorDefinition
    {
        internal DeveloperColorDefinition(
            string name,
            string displayName,
            Color32 tint,
            Color32 fresnelColor,
            float fresnelPower)
        {
            Name = name;
            DisplayName = displayName;
            Tint = tint;
            FresnelColor = fresnelColor;
            FresnelPower = fresnelPower;
        }

        internal string Name { get; }
        internal string DisplayName { get; }
        internal Color32 Tint { get; }
        internal Color32 FresnelColor { get; }
        internal float FresnelPower { get; }
    }
}
