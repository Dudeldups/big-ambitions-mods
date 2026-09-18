#nullable enable
using System;
using System.Collections.Generic;
using Services;

namespace CameraStore
{
    internal sealed class CameraStoreFurnitureRetailerIntegration
    {
        private static readonly string[] RetailerContactIds =
        {
            "AJ Pederson & Son",
            "Essentials Appliances",
            "Hampton Supplies"
        };

        private readonly Dictionary<string, RetailerState> states =
            new(StringComparer.Ordinal);

        public bool Apply()
        {
            var registrations = SaveGameManager.Current?.BuildingRegistrations;
            if (registrations == null)
                return false;

            var retailerRegistrations = new Dictionary<string, BuildingRegistration>(StringComparer.Ordinal);
            foreach (var contactId in RetailerContactIds)
            {
                var registration = registrations.Find(candidate =>
                    candidate != null && string.Equals(candidate.BusinessName, contactId, StringComparison.Ordinal));
                if (registration == null)
                    return false;

                retailerRegistrations.Add(contactId, registration);
            }

            foreach (var contactId in RetailerContactIds)
            {
                var registration = retailerRegistrations[contactId];
                CaptureExternalCatalog(contactId, registration);

                var state = states[contactId];
                var desiredCatalog = UniqueCopy(state.ExternalCatalog);
                AddUniqueRange(desiredCatalog, CameraStoreIds.Furniture);

                var hasCurrentCatalog = ContractItemsForSaleService.TryGetItemsForContact(
                    contactId,
                    out List<string> currentCatalog);
                if (!hasCurrentCatalog || currentCatalog == null || !SameList(currentCatalog, desiredCatalog))
                    ContractItemsForSaleService.SetItemsForContact(contactId, desiredCatalog);

                state.LastAppliedCatalog = desiredCatalog;
            }

            return true;
        }

        public void Restore()
        {
            foreach (var pair in states)
            {
                var state = pair.Value;
                if (state.ExternalHadExplicitCatalog)
                    ContractItemsForSaleService.SetItemsForContact(pair.Key, state.ExternalCatalog);
                else
                    ContractItemsForSaleService.SetItemsForContact(pair.Key, null);
            }

            states.Clear();
        }

        private void CaptureExternalCatalog(string contactId, BuildingRegistration registration)
        {
            var hasCurrentCatalog = ContractItemsForSaleService.TryGetItemsForContact(
                contactId,
                out List<string> currentCatalog);
            currentCatalog ??= new List<string>();

            if (!states.TryGetValue(contactId, out var state))
            {
                state = new RetailerState
                {
                    ExternalHadExplicitCatalog = hasCurrentCatalog,
                    ExternalCatalog = hasCurrentCatalog
                        ? UniqueCopyWithoutCameraStoreFurniture(currentCatalog)
                        : UniqueCopy(registration.GetListOfItemsForSale())
                };
                states.Add(contactId, state);
                return;
            }

            if (hasCurrentCatalog && SameList(currentCatalog, state.LastAppliedCatalog))
                return;

            state.ExternalHadExplicitCatalog = hasCurrentCatalog;
            state.ExternalCatalog = hasCurrentCatalog
                ? UniqueCopyWithoutCameraStoreFurniture(currentCatalog)
                : UniqueCopy(registration.GetListOfItemsForSale());
        }

        private static List<string> UniqueCopyWithoutCameraStoreFurniture(IEnumerable<string>? source)
        {
            var result = new List<string>();
            if (source == null)
                return result;

            foreach (var itemName in source)
            {
                if (!IsCameraStoreFurniture(itemName))
                    AddUnique(result, itemName);
            }

            return result;
        }

        private static List<string> UniqueCopy(IEnumerable<string>? source)
        {
            var result = new List<string>();
            if (source != null)
                AddUniqueRange(result, source);

            return result;
        }

        private static void AddUniqueRange(List<string> destination, IEnumerable<string> source)
        {
            foreach (var entry in source)
                AddUnique(destination, entry);
        }

        private static void AddUnique(List<string> destination, string entry)
        {
            if (!string.IsNullOrEmpty(entry) && !destination.Contains(entry))
                destination.Add(entry);
        }

        private static bool IsCameraStoreFurniture(string itemName)
        {
            foreach (var furnitureItemName in CameraStoreIds.Furniture)
            {
                if (string.Equals(itemName, furnitureItemName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool SameList(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left.Count != right.Count)
                return false;

            for (var index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private sealed class RetailerState
        {
            internal bool ExternalHadExplicitCatalog;
            internal List<string> ExternalCatalog = new();
            internal List<string> LastAppliedCatalog = new();
        }
    }
}
