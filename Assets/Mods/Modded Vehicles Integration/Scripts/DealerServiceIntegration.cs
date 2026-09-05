#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Buildings;
using Dialogs;

namespace ModdedVehiclesIntegration
{
    internal static class DealerServiceIntegration
    {
        private const string VehicleStoreSourceContactId = "General US Trucks";

        private static readonly string[] InteractiveDealerContactIds =
        {
            "City Cars",
            "Manhattan Luxury Cars",
            "The Hamptons Axis"
        };

        private static readonly List<PatchRecord> AppliedPatches = new List<PatchRecord>();
        private static readonly HashSet<string> ReportedProblems = new HashSet<string>(StringComparer.Ordinal);

        internal static void EnsureApplied(ModContext? context)
        {
            var registrations = SaveGameManager.Current?.BuildingRegistrations;
            if (registrations == null)
                return;

            var sourceRegistration = registrations.Find(candidate =>
                candidate != null &&
                string.Equals(candidate.BusinessName, VehicleStoreSourceContactId, StringComparison.Ordinal));
            var sourceService = sourceRegistration?.BuildingCached?.SpecialService;
            if (!(sourceService?.settings is VehicleStoreSettings sourceSettings))
            {
                ReportProblemOnce(
                    "source",
                    "General US Trucks is not ready or has no VehicleStoreSettings; dealer dialog integration is waiting.",
                    context);
                return;
            }

            foreach (var dealerContactId in InteractiveDealerContactIds)
            {
                try
                {
                    var registration = registrations.Find(candidate =>
                        candidate != null &&
                        string.Equals(candidate.BusinessName, dealerContactId, StringComparison.Ordinal));
                    var service = registration?.BuildingCached?.SpecialService;
                    if (registration == null || service == null)
                    {
                        ReportProblemOnce(
                            "dealer:" + dealerContactId,
                            $"dealer '{dealerContactId}' is not ready or has no SpecialService; dialog integration is waiting.",
                            context);
                        continue;
                    }

                    var patch = FindPatch(service);
                    if (patch == null)
                    {
                        patch = new PatchRecord(
                            service,
                            service.dialogType,
                            service.settings,
                            service.contactCategory,
                            service.isBusinessContact);
                        AppliedPatches.Add(patch);

                        context?.Logger.Info(
                            $"Modded Vehicles Integration: dealer dialog diagnosis for '{dealerContactId}' at '{registration.Address}': " +
                            $"dialog={service.dialogType}, settings={GetSettingsName(service.settings)}, " +
                            $"contactCategory={service.contactCategory}, businessContact={service.isBusinessContact}. " +
                            "The vanilla showroom configuration cannot open VehicleStoreDialog from a desk.");
                    }

                    var needsRepair =
                        service.dialogType != CallDialogType.VehicleStoreDialog ||
                        !ReferenceEquals(service.settings, sourceSettings) ||
                        service.contactCategory != sourceService.contactCategory ||
                        service.isBusinessContact != sourceService.isBusinessContact;
                    if (!needsRepair)
                        continue;

                    service.dialogType = CallDialogType.VehicleStoreDialog;
                    service.settings = sourceSettings;
                    service.contactCategory = sourceService.contactCategory;
                    service.isBusinessContact = sourceService.isBusinessContact;

                    context?.Logger.Info(
                        $"Modded Vehicles Integration: dealer dialog ready for '{dealerContactId}' at '{registration.Address}': " +
                        $"dialog={service.dialogType}, settings={GetSettingsName(service.settings)}, " +
                        $"contactCategory={service.contactCategory}, businessContact={service.isBusinessContact}.");
                }
                catch (Exception exception)
                {
                    context?.Logger.Error(exception);
                }
            }
        }

        internal static void Restore(ModContext? context)
        {
            for (var index = AppliedPatches.Count - 1; index >= 0; index--)
            {
                var patch = AppliedPatches[index];
                patch.Service.dialogType = patch.OriginalDialogType;
                patch.Service.settings = patch.OriginalSettings;
                patch.Service.contactCategory = patch.OriginalContactCategory;
                patch.Service.isBusinessContact = patch.OriginalIsBusinessContact;
            }

            if (AppliedPatches.Count > 0)
            {
                context?.Logger.Info(
                    $"Modded Vehicles Integration: restored native service configuration for {AppliedPatches.Count} dealer(s).");
            }

            AppliedPatches.Clear();
            ReportedProblems.Clear();
        }

        private static PatchRecord? FindPatch(SpecialService service)
        {
            foreach (var patch in AppliedPatches)
            {
                if (ReferenceEquals(patch.Service, service))
                    return patch;
            }

            return null;
        }

        private static void ReportProblemOnce(string key, string message, ModContext? context)
        {
            if (ReportedProblems.Add(key))
                context?.Logger.Warn("Modded Vehicles Integration: " + message);
        }

        private static string GetSettingsName(SpecialServiceSettings? settings)
        {
            return settings == null ? "null" : settings.GetType().Name;
        }

        private sealed class PatchRecord
        {
            internal PatchRecord(
                SpecialService service,
                CallDialogType originalDialogType,
                SpecialServiceSettings originalSettings,
                UI.Smartphone.Apps.Contacts.ContactCategoryName originalContactCategory,
                bool originalIsBusinessContact)
            {
                Service = service;
                OriginalDialogType = originalDialogType;
                OriginalSettings = originalSettings;
                OriginalContactCategory = originalContactCategory;
                OriginalIsBusinessContact = originalIsBusinessContact;
            }

            internal SpecialService Service { get; }
            internal CallDialogType OriginalDialogType { get; }
            internal SpecialServiceSettings OriginalSettings { get; }
            internal UI.Smartphone.Apps.Contacts.ContactCategoryName OriginalContactCategory { get; }
            internal bool OriginalIsBusinessContact { get; }
        }
    }
}
