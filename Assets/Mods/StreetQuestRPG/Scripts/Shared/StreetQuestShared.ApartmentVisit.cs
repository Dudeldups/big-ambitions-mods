using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Buildings;
using Helpers;
using Localizor;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        private const float ApartmentEntryTransitionTimeoutSeconds = 8f;

        private static readonly string[] ApartmentRegistrationFieldNames =
        {
            "Layout",
            "interiorDesigns",
            "itemInstances",
            "itemsInBuilding",
            "deliveredItems",
            "dirtSpots"
        };

        private static readonly Dictionary<string, StreetQuestApartmentInteriorPayload> ApartmentPayloadCacheByVisitKey =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> LoggedVanillaApartmentSnapshotAddresses =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> LoggedApartmentRegistrationDumpKeys =
            new(StringComparer.OrdinalIgnoreCase);

        private static StreetQuestApartmentVisitContext ActiveApartmentVisit;
        private static bool HasLoggedApartmentItemInstanceShape;

        internal static bool IsApartmentVisitContextActiveFor(string characterId)
        {
            return ActiveApartmentVisit != null &&
                   ActiveApartmentVisit.State == StreetQuestApartmentVisitState.ActiveInside &&
                   string.Equals(ActiveApartmentVisit.CharacterId, characterId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryEnterApartment(StreetQuestShared.ApartmentEntryOption option)
        {
            if (option == null)
                return false;

            if (ActiveApartmentVisit != null)
            {
                LogDebug(
                    $"ApartmentEntryFailed reason=visit_already_active character={option.CharacterId} state={option.StateId} activeKey={ActiveApartmentVisit.VisitKey}");
                NotifyInfo(
                    "streetquest:apartment_entry_failed".Localize(new Dictionary<string, string>
                    {
                        { "npcname", option.CharacterName ?? option.CharacterId ?? "NPC" }
                    }).ToString(),
                    $"streetquest:apartment_entry_failed:{option.CharacterId}",
                    3.5f);
                return false;
            }

            LogDebug(
                $"ApartmentEntryResolveStart character={option.CharacterId} state={option.StateId} exteriorAddress={option.ExteriorAddress}");

            if (!TryResolveApartmentVisitTarget(option, out var building, out var registration, out var parsedAddress, out var failureReason))
            {
                LogDebug(
                    $"ApartmentEntryFailed reason={failureReason} character={option.CharacterId} state={option.StateId} exteriorAddress={option.ExteriorAddress}");
                NotifyInfo(
                    "streetquest:apartment_entry_failed".Localize(new Dictionary<string, string>
                    {
                        { "npcname", option.CharacterName ?? option.CharacterId ?? "NPC" }
                    }).ToString(),
                    $"streetquest:apartment_entry_failed:{option.CharacterId}",
                    3.5f);
                return false;
            }

            var visitContext = CreateApartmentVisitContext(option, building, registration);
            TryLogApartmentRegistrationDump($"RoutedApartmentRegistrationBeforeEntry:{visitContext.VisitKey}", registration);

            if (!TryStartVanillaApartmentEntry(building, out var route))
            {
                LogDebug(
                    $"ApartmentEntryFailed reason=start_vanilla_entry_failed character={option.CharacterId} state={option.StateId} exteriorAddress={option.ExteriorAddress}");
                RestoreApartmentVisitContext(visitContext, "entry_start_failed", clearActiveVisit: false);
                NotifyInfo(
                    "streetquest:apartment_entry_failed".Localize(new Dictionary<string, string>
                    {
                        { "npcname", option.CharacterName ?? option.CharacterId ?? "NPC" }
                    }).ToString(),
                    $"streetquest:apartment_entry_failed:{option.CharacterId}",
                    3.5f);
                return false;
            }

            visitContext.EntryStartedAtSeconds = Time.unscaledTime;
            visitContext.EntryRoute = route;
            visitContext.State = StreetQuestApartmentVisitState.WaitingForIndoorTransition;
            ActiveApartmentVisit = visitContext;

            LogDebug(
                $"ApartmentEntryStarted character={option.CharacterId} state={option.StateId} address={option.ExteriorAddress} route={route} canEnter={SafeCanEnterBuilding(parsedAddress)} building={DescribeObject(building)} registration={DescribeObject(registration)}");
            return true;
        }

        internal static void TickApartmentVisit()
        {
            var visit = ActiveApartmentVisit;
            if (visit == null)
            {
                TryLogVanillaApartmentSnapshot();
                return;
            }

            if (visit.State == StreetQuestApartmentVisitState.WaitingForIndoorTransition)
            {
                if (IsIndoorGameplayContextActive())
                {
                    if (!visit.PayloadAppliedInside)
                    {
                        ApplyApartmentPayload(visit);
                        visit.PayloadAppliedInside = true;
                    }

                    visit.State = StreetQuestApartmentVisitState.ActiveInside;
                    RefreshSpawnedCharacters();
                    LogDebug(
                        $"ApartmentVisitEntered character={visit.CharacterId} state={visit.StateId} address={visit.ExteriorAddress} route={visit.EntryRoute}");
                    return;
                }

                if (Time.unscaledTime - visit.EntryStartedAtSeconds >= ApartmentEntryTransitionTimeoutSeconds)
                {
                    LogDebug(
                        $"ApartmentVisitTransitionTimeout character={visit.CharacterId} state={visit.StateId} address={visit.ExteriorAddress} route={visit.EntryRoute}");
                    RestoreActiveApartmentVisit("transition_timeout");
                }

                return;
            }

            if (visit.State == StreetQuestApartmentVisitState.ActiveInside &&
                !IsIndoorGameplayContextActive())
            {
                RestoreActiveApartmentVisit("returned_outdoor");
            }
        }

        internal static void RestoreActiveApartmentVisit(string reason)
        {
            if (ActiveApartmentVisit == null)
                return;

            RestoreApartmentVisitContext(ActiveApartmentVisit, reason, clearActiveVisit: true);
        }

        private static StreetQuestApartmentVisitContext CreateApartmentVisitContext(
            StreetQuestShared.ApartmentEntryOption option,
            Building building,
            BuildingRegistration registration)
        {
            var visitKey = BuildApartmentVisitKey(option);
            var originalSnapshot = CaptureApartmentRegistrationSnapshot(registration);

            ApartmentPayloadCacheByVisitKey.TryGetValue(visitKey, out var cachedPayload);
            var payload = cachedPayload ?? CreateDefaultApartmentPayload(option, originalSnapshot, registration);
            if (cachedPayload == null)
            {
                ApartmentPayloadCacheByVisitKey[visitKey] = payload;
                LogDebug(
                    $"ApartmentPayloadPrepared key={visitKey} source=blank_payload layout={payload.Layout ?? "<null>"}");
            }
            else
            {
                LogDebug(
                    $"ApartmentPayloadPrepared key={visitKey} source=session_cache layout={payload.Layout ?? "<null>"}");
            }

            return new StreetQuestApartmentVisitContext
            {
                CharacterId = option.CharacterId ?? string.Empty,
                CharacterName = option.CharacterName ?? option.CharacterId ?? "NPC",
                StateId = option.StateId ?? string.Empty,
                ExteriorAddress = option.ExteriorAddress ?? string.Empty,
                VisitKey = visitKey,
                Building = building,
                Registration = registration,
                OriginalSnapshot = originalSnapshot,
                ActivePayload = payload
            };
        }

        private static StreetQuestApartmentRegistrationSnapshot CaptureApartmentRegistrationSnapshot(BuildingRegistration registration)
        {
            var snapshot = new StreetQuestApartmentRegistrationSnapshot();
            if (registration == null)
                return snapshot;

            foreach (var fieldName in ApartmentRegistrationFieldNames)
                snapshot.FieldValues[fieldName] = GetMemberValue(registration, fieldName);

            return snapshot;
        }

        private static StreetQuestApartmentInteriorPayload CreateDefaultApartmentPayload(
            StreetQuestShared.ApartmentEntryOption option,
            StreetQuestApartmentRegistrationSnapshot originalSnapshot,
            BuildingRegistration registration)
        {
            if (TryCreateRegisteredLayoutApartmentPayload(option, originalSnapshot, registration, out var registeredLayoutPayload))
                return registeredLayoutPayload;

            return new StreetQuestApartmentInteriorPayload
            {
                Layout = originalSnapshot.Get<string>("Layout"),
                InteriorDesigns = CreateEmptyValueLike(originalSnapshot.GetRaw("interiorDesigns"), GetMemberType(registration, "interiorDesigns")),
                ItemInstances = CreateEmptyValueLike(originalSnapshot.GetRaw("itemInstances"), GetMemberType(registration, "itemInstances")),
                ItemsInBuilding = CreateEmptyValueLike(originalSnapshot.GetRaw("itemsInBuilding"), GetMemberType(registration, "itemsInBuilding")),
                DeliveredItems = CreateEmptyValueLike(originalSnapshot.GetRaw("deliveredItems"), GetMemberType(registration, "deliveredItems")),
                DirtSpots = CreateEmptyValueLike(originalSnapshot.GetRaw("dirtSpots"), GetMemberType(registration, "dirtSpots"))
            };
        }

        private static void ApplyApartmentPayload(StreetQuestApartmentVisitContext context)
        {
            if (context?.Registration == null || context.ActivePayload == null)
                return;

            var mergedItemInstances = MergeApartmentItemInstances(
                GetMemberValue(context.Registration, "itemInstances"),
                context.ActivePayload.ItemInstances,
                GetMemberType(context.Registration, "itemInstances"));
            if (mergedItemInstances != null)
                SetMemberValue(context.Registration, "itemInstances", mergedItemInstances);

            var originalLayout = context.OriginalSnapshot?.Get<string>("Layout") ?? "<null>";
            var payloadLayout = context.ActivePayload.Layout ?? "<null>";

            LogDebug(
                $"ApartmentPayloadApplied key={context.VisitKey} originalLayout={originalLayout} payloadLayout={payloadLayout} interiorDesigns={DescribeValueShape(GetMemberValue(context.Registration, "interiorDesigns"))} itemInstances={DescribeValueShape(GetMemberValue(context.Registration, "itemInstances"))} itemsInBuilding={DescribeValueShape(GetMemberValue(context.Registration, "itemsInBuilding"))}");
        }

        private static object MergeApartmentItemInstances(object liveItemInstances, object payloadItemInstances, Type targetType)
        {
            if (payloadItemInstances is not IDictionary payloadDictionary)
                return liveItemInstances;

            var merged = CloneValueLike(liveItemInstances, targetType) ??
                         CreateEmptyValueLike(liveItemInstances, targetType) ??
                         CreateEmptyValueLike(payloadItemInstances, targetType);
            if (merged is not IDictionary mergedDictionary)
                return liveItemInstances;

            foreach (DictionaryEntry entry in payloadDictionary)
                mergedDictionary[entry.Key] = entry.Value;

            return merged;
        }

        private static void CaptureApartmentPayload(StreetQuestApartmentVisitContext context)
        {
            if (context?.Registration == null || context.ActivePayload == null)
                return;

            context.ActivePayload.Layout = GetMemberValue(context.Registration, "Layout") as string;
            context.ActivePayload.InteriorDesigns = GetMemberValue(context.Registration, "interiorDesigns");
            context.ActivePayload.ItemInstances = GetMemberValue(context.Registration, "itemInstances");
            context.ActivePayload.ItemsInBuilding = GetMemberValue(context.Registration, "itemsInBuilding");
            context.ActivePayload.DeliveredItems = GetMemberValue(context.Registration, "deliveredItems");
            context.ActivePayload.DirtSpots = GetMemberValue(context.Registration, "dirtSpots");
            ApartmentPayloadCacheByVisitKey[context.VisitKey] = context.ActivePayload;

            TryLogApartmentItemInstanceShape(context.ActivePayload.ItemInstances, context.ActivePayload.ItemsInBuilding);

            LogDebug(
                $"ApartmentPayloadCaptured key={context.VisitKey} layout={context.ActivePayload.Layout ?? "<null>"} interiorDesigns={DescribeValueShape(context.ActivePayload.InteriorDesigns)} itemInstances={DescribeValueShape(context.ActivePayload.ItemInstances)} itemsInBuilding={DescribeValueShape(context.ActivePayload.ItemsInBuilding)}");
        }

        private static void TryLogApartmentItemInstanceShape(object itemInstances, object itemsInBuilding)
        {
            if (HasLoggedApartmentItemInstanceShape || itemInstances is not IDictionary dictionary || dictionary.Count == 0)
                return;

            foreach (DictionaryEntry entry in dictionary)
            {
                var keyType = entry.Key?.GetType().FullName ?? "<null>";
                var value = entry.Value;
                var valueType = value?.GetType();
                if (valueType == null)
                    continue;

                var fields = valueType.GetFields(ReflectionFlags)
                    .Select(field => $"{field.FieldType.Name} {field.Name}={SafeDescribeFieldValue(field, value)}")
                    .ToArray();
                var properties = valueType.GetProperties(ReflectionFlags)
                    .Where(property => property.GetIndexParameters().Length == 0)
                    .Take(12)
                    .Select(property => $"{property.PropertyType.Name} {property.Name}={SafeDescribePropertyValue(property, value)}")
                    .ToArray();

                LogDebug(
                    $"ApartmentItemInstanceShape dictionaryType={itemInstances.GetType().FullName} keyType={keyType} valueType={valueType.FullName} itemsInBuildingType={itemsInBuilding?.GetType().FullName ?? "<null>"} sampleKey={entry.Key} fields=[{string.Join(" | ", fields)}] properties=[{string.Join(" | ", properties)}]");
                HasLoggedApartmentItemInstanceShape = true;
                break;
            }
        }

        private static void TryLogVanillaApartmentSnapshot()
        {
            if (!IsIndoorGameplayContextActive())
                return;

            var addressKey = GetCurrentIndoorBuildingAddressKey();
            if (string.IsNullOrWhiteSpace(addressKey) ||
                !LoggedVanillaApartmentSnapshotAddresses.Add(addressKey))
                return;

            var registration = FindBuildingRegistrationByAddressText(addressKey);
            if (registration == null)
            {
                LogDebug($"VanillaApartmentSnapshotSkipped address={addressKey} reason=registration_not_found");
                return;
            }

            TryLogApartmentRegistrationDump($"VanillaApartmentRegistration:{addressKey}", registration);

            var layout = GetMemberValue(registration, "Layout") as string;
            var interiorDesigns = GetMemberValue(registration, "interiorDesigns");
            var itemInstances = GetMemberValue(registration, "itemInstances");
            var itemsInBuilding = GetMemberValue(registration, "itemsInBuilding");
            var deliveredItems = GetMemberValue(registration, "deliveredItems");
            var dirtSpots = GetMemberValue(registration, "dirtSpots");

            LogDebug(
                $"VanillaApartmentSnapshot address={addressKey} layout={layout ?? "<null>"} interiorDesigns={DescribeValueShape(interiorDesigns)} itemInstances={DescribeValueShape(itemInstances)} itemsInBuilding={DescribeValueShape(itemsInBuilding)} deliveredItems={DescribeValueShape(deliveredItems)} dirtSpots={DescribeValueShape(dirtSpots)}");

            TryLogApartmentItemInstanceShapeForLabel(
                "VanillaApartmentItemInstanceShape",
                itemInstances,
                itemsInBuilding);
        }

        private static void TryLogApartmentItemInstanceShapeForLabel(string label, object itemInstances, object itemsInBuilding)
        {
            if (itemInstances is not IDictionary dictionary || dictionary.Count == 0)
                return;

            foreach (DictionaryEntry entry in dictionary)
            {
                var keyType = entry.Key?.GetType().FullName ?? "<null>";
                var value = entry.Value;
                var valueType = value?.GetType();
                if (valueType == null)
                    continue;

                var fields = valueType.GetFields(ReflectionFlags)
                    .Select(field => $"{field.FieldType.Name} {field.Name}={SafeDescribeFieldValue(field, value)}")
                    .ToArray();
                var properties = valueType.GetProperties(ReflectionFlags)
                    .Where(property => property.GetIndexParameters().Length == 0)
                    .Take(12)
                    .Select(property => $"{property.PropertyType.Name} {property.Name}={SafeDescribePropertyValue(property, value)}")
                    .ToArray();

                LogDebug(
                    $"{label} dictionaryType={itemInstances.GetType().FullName} keyType={keyType} valueType={valueType.FullName} itemsInBuildingType={itemsInBuilding?.GetType().FullName ?? "<null>"} sampleKey={entry.Key} fields=[{string.Join(" | ", fields)}] properties=[{string.Join(" | ", properties)}]");
                break;
            }
        }

        private static void TryLogApartmentRegistrationDump(string key, object registration)
        {
            if (registration == null ||
                string.IsNullOrWhiteSpace(key) ||
                !LoggedApartmentRegistrationDumpKeys.Add(key))
                return;

            var type = registration.GetType();
            var fieldLines = type
                .GetFields(ReflectionFlags)
                .OrderBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
                .Select(field => $"{field.FieldType.Name} {field.Name}={SafeDescribeFieldValue(field, registration)}")
                .ToArray();
            var propertyLines = type
                .GetProperties(ReflectionFlags)
                .Where(property => property.GetIndexParameters().Length == 0)
                .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .Select(property => $"{property.PropertyType.Name} {property.Name}={SafeDescribePropertyValue(property, registration)}")
                .ToArray();

            LogDebug($"ApartmentRegistrationDump key={key} type={type.FullName}");
            LogDebug($"ApartmentRegistrationDumpFields key={key} values=[{string.Join(" | ", fieldLines)}]");
            LogDebug($"ApartmentRegistrationDumpProperties key={key} values=[{string.Join(" | ", propertyLines)}]");
        }

        private static string SafeDescribeFieldValue(FieldInfo field, object instance)
        {
            try
            {
                var value = field.GetValue(instance);
                return value?.ToString() ?? "<null>";
            }
            catch (Exception exception)
            {
                return $"<error:{exception.GetType().Name}>";
            }
        }

        private static string SafeDescribePropertyValue(PropertyInfo property, object instance)
        {
            try
            {
                var value = property.GetValue(instance);
                return value?.ToString() ?? "<null>";
            }
            catch (Exception exception)
            {
                return $"<error:{exception.GetType().Name}>";
            }
        }

        private static void RestoreApartmentVisitContext(
            StreetQuestApartmentVisitContext context,
            string reason,
            bool clearActiveVisit)
        {
            if (context == null)
                return;

            CaptureApartmentPayload(context);

            if (context.Registration != null && context.OriginalSnapshot != null)
            {
                foreach (var fieldName in ApartmentRegistrationFieldNames)
                    SetMemberValue(context.Registration, fieldName, context.OriginalSnapshot.GetRaw(fieldName));
            }

            LogDebug(
                $"ApartmentVisitRestored character={context.CharacterId} state={context.StateId} address={context.ExteriorAddress} reason={reason}");

            if (clearActiveVisit && ReferenceEquals(ActiveApartmentVisit, context))
                ActiveApartmentVisit = null;

            RefreshSpawnedCharacters();
        }

        private static bool TryResolveApartmentVisitTarget(
            StreetQuestShared.ApartmentEntryOption option,
            out Building building,
            out BuildingRegistration registration,
            out object parsedAddress,
            out string failureReason)
        {
            building = null;
            registration = null;
            parsedAddress = null;
            failureReason = string.Empty;

            var addressText = option.ExteriorAddress ?? string.Empty;
            if (string.IsNullOrWhiteSpace(addressText))
            {
                failureReason = "missing_exterior_address";
                return false;
            }

            registration = FindBuildingRegistrationByAddressText(addressText);
            parsedAddress = registration?.Address;

            if (parsedAddress == null)
            {
                try
                {
                    parsedAddress = BuildingHelper.ParseAddressString(addressText);
                }
                catch (Exception exception)
                {
                    failureReason = $"parse_address_failed:{exception.GetType().Name}";
                    return false;
                }
            }

            if (parsedAddress == null)
            {
                failureReason = "parsed_address_null";
                return false;
            }

            try
            {
                building = InvokeStaticBuildingHelperMethod<Building>("GetBuilding", parsedAddress);
            }
            catch (Exception exception)
            {
                failureReason = $"get_building_failed:{exception.GetType().Name}";
                return false;
            }

            if (building == null)
            {
                failureReason = "building_not_found";
                return false;
            }

            if (registration == null)
            {
                try
                {
                    registration = InvokeStaticBuildingHelperMethod<BuildingRegistration>("GetBuildingRegistration", parsedAddress) ??
                                   building.GetRegistration();
                }
                catch (Exception exception)
                {
                    failureReason = $"get_registration_failed:{exception.GetType().Name}";
                    return false;
                }
            }

            if (registration == null)
            {
                failureReason = "registration_not_found";
                return false;
            }

            return true;
        }

        private static BuildingRegistration FindBuildingRegistrationByAddressText(string addressText)
        {
            var normalizedAddress = NormalizeAddressKey(addressText);
            if (string.IsNullOrWhiteSpace(normalizedAddress))
                return null;

            var registrations = SaveGameManager.Current?.BuildingRegistrations;
            if (registrations == null)
                return null;

            foreach (var candidate in registrations)
            {
                if (candidate?.Address == null)
                    continue;

                var candidateText = NormalizeAddressKey(candidate.Address.ToString());
                if (string.Equals(candidateText, normalizedAddress, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
        }

        private static bool TryStartVanillaApartmentEntry(Building building, out string route)
        {
            route = string.Empty;
            if (building == null)
                return false;

            var cityManagerType = FindType("CityManager");
            var cityManager = cityManagerType != null ? UnityEngine.Object.FindObjectOfType(cityManagerType) : null;
            var loadIndoorsMethod = cityManagerType?.GetMethod(
                "LoadIndoors",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Building), typeof(bool) },
                null);
            if (cityManager != null && loadIndoorsMethod != null)
            {
                loadIndoorsMethod.Invoke(cityManager, new object[] { building, true });
                route = "CityManager.LoadIndoors";
                return true;
            }

            var buildingManagerType = FindType("BuildingManager");
            var buildingManager = buildingManagerType != null ? UnityEngine.Object.FindObjectOfType(buildingManagerType) : null;
            var enterBuildingMethod = buildingManagerType?.GetMethod(
                "EnterBuilding",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Building), typeof(bool), typeof(bool), typeof(int), typeof(int), typeof(bool) },
                null);
            if (buildingManager != null && enterBuildingMethod != null)
            {
                var result = enterBuildingMethod.Invoke(buildingManager, new object[] { building, false, false, -1, -1, true });
                route = $"BuildingManager.EnterBuilding result={result}";
                return result as bool? ?? false;
            }

            return false;
        }

        private static string BuildApartmentVisitKey(StreetQuestShared.ApartmentEntryOption option)
        {
            return string.Join(
                "|",
                new[]
                {
                    NormalizeAddressKey(option?.ExteriorAddress),
                    option?.CharacterId ?? string.Empty,
                    option?.StateId ?? string.Empty
                });
        }

        private static object CreateEmptyValueLike(object existingValue, Type targetType)
        {
            targetType ??= existingValue?.GetType();
            if (targetType == null)
                return null;

            if (targetType == typeof(string))
                return string.Empty;

            if (targetType.IsArray)
            {
                var elementType = targetType.GetElementType() ?? typeof(object);
                return Array.CreateInstance(elementType, 0);
            }

            if (typeof(IDictionary).IsAssignableFrom(targetType) ||
                typeof(IList).IsAssignableFrom(targetType) ||
                targetType.GetConstructor(Type.EmptyTypes) != null)
            {
                try
                {
                    return Activator.CreateInstance(targetType);
                }
                catch
                {
                }
            }

            return existingValue;
        }

        private static Type GetMemberType(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            for (var currentType = instance.GetType(); currentType != null; currentType = currentType.BaseType)
            {
                var property = currentType.GetProperty(memberName, ReflectionFlags);
                if (property != null)
                    return property.PropertyType;

                var field = currentType.GetField(memberName, ReflectionFlags);
                if (field != null)
                    return field.FieldType;
            }

            return null;
        }

        private static string DescribeValueShape(object value)
        {
            if (value == null)
                return "<null>";

            if (value is ICollection collection)
                return $"{value.GetType().Name}(count={collection.Count})";

            return value.GetType().Name;
        }

        private static bool SafeCanEnterBuilding(object parsedAddress)
        {
            try
            {
                return parsedAddress != null && InvokeStaticBuildingHelperMethod<bool>("CanEnterBuilding", parsedAddress);
            }
            catch
            {
                return false;
            }
        }

        private static T InvokeStaticBuildingHelperMethod<T>(string methodName, object argument)
        {
            var method = typeof(BuildingHelper).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                        return false;

                    var parameters = candidate.GetParameters();
                    return parameters.Length == 1 &&
                           argument != null &&
                           parameters[0].ParameterType.IsInstanceOfType(argument);
                });

            if (method == null)
                throw new MissingMethodException(typeof(BuildingHelper).FullName, methodName);

            var result = method.Invoke(null, new[] { argument });
            return result is T typedResult ? typedResult : default;
        }

        private sealed class StreetQuestApartmentVisitContext
        {
            public string CharacterId;
            public string CharacterName;
            public string StateId;
            public string ExteriorAddress;
            public string VisitKey;
            public Building Building;
            public BuildingRegistration Registration;
            public StreetQuestApartmentRegistrationSnapshot OriginalSnapshot;
            public StreetQuestApartmentInteriorPayload ActivePayload;
            public string EntryRoute;
            public float EntryStartedAtSeconds;
            public StreetQuestApartmentVisitState State;
            public bool PayloadAppliedInside;
        }

        private sealed class StreetQuestApartmentRegistrationSnapshot
        {
            public readonly Dictionary<string, object> FieldValues = new(StringComparer.Ordinal);

            public T Get<T>(string fieldName) where T : class
            {
                return GetRaw(fieldName) as T;
            }

            public object GetRaw(string fieldName)
            {
                return FieldValues.TryGetValue(fieldName, out var value) ? value : null;
            }
        }

        private sealed class StreetQuestApartmentInteriorPayload
        {
            public string Layout;
            public object InteriorDesigns;
            public object ItemInstances;
            public object ItemsInBuilding;
            public object DeliveredItems;
            public object DirtSpots;
        }

        private enum StreetQuestApartmentVisitState
        {
            WaitingForIndoorTransition,
            ActiveInside
        }
    }
}
