using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using BigAmbitions.Items;
using Buildings;
using Helpers;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        private const string ApartmentLayoutHelperTypeName = "BusinessLayoutSets.BusinessLayoutSetHelper";
        private const string BlueprintLegacyFixTypeName = "Blueprints.Compatibility.BlueprintFixesEA11.UpdateLegacyIds";
        private static readonly Dictionary<string, StreetQuestRegisteredApartmentLayout> RegisteredApartmentLayoutsBySource =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool TryCreateRegisteredLayoutApartmentPayload(
            StreetQuestShared.ApartmentEntryOption option,
            StreetQuestApartmentRegistrationSnapshot originalSnapshot,
            object registration,
            out StreetQuestApartmentInteriorPayload payload)
        {
            payload = null;
            if (option == null || string.IsNullOrWhiteSpace(option.ApartmentLayoutFile))
                return false;

            if (!TryRegisterApartmentLayout(
                    option.ApartmentLayoutFile,
                    option.ApartmentLayoutName,
                    out var resolvedLayoutName,
                    out var registeredLayoutTempPath))
                return false;

            var interiorDesigns = CreateEmptyValueLike(originalSnapshot?.GetRaw("interiorDesigns"), GetMemberType(registration, "interiorDesigns"));
            var itemInstances = CreateEmptyValueLike(originalSnapshot?.GetRaw("itemInstances"), GetMemberType(registration, "itemInstances"));
            var itemsInBuilding = CreateEmptyValueLike(originalSnapshot?.GetRaw("itemsInBuilding"), GetMemberType(registration, "itemsInBuilding"));
            var deliveredItems = CreateEmptyValueLike(originalSnapshot?.GetRaw("deliveredItems"), GetMemberType(registration, "deliveredItems"));
            var dirtSpots = CreateEmptyValueLike(originalSnapshot?.GetRaw("dirtSpots"), GetMemberType(registration, "dirtSpots"));

            if (TryHydrateApartmentLayoutPayloadFromAddressInsert(
                    option,
                    originalSnapshot,
                    registration,
                    registeredLayoutTempPath,
                    out var insertedPayload))
            {
                payload = insertedPayload;
                LogDebug(
                    $"ApartmentLayoutPayloadPrepared character={option.CharacterId} state={option.StateId} layoutFile={option.ApartmentLayoutFile} layoutName={resolvedLayoutName} tempPath={registeredLayoutTempPath} mode=address_insert");
                return true;
            }

            TryHydrateApartmentLayoutPayloadFromHelper(
                option,
                registration,
                registeredLayoutTempPath,
                ref interiorDesigns,
                ref itemInstances,
                ref itemsInBuilding);

            payload = new StreetQuestApartmentInteriorPayload
            {
                Layout = resolvedLayoutName,
                InteriorDesigns = interiorDesigns,
                ItemInstances = itemInstances,
                ItemsInBuilding = itemsInBuilding,
                DeliveredItems = deliveredItems,
                DirtSpots = dirtSpots
            };

            LogDebug(
                $"ApartmentLayoutPayloadPrepared character={option.CharacterId} state={option.StateId} layoutFile={option.ApartmentLayoutFile} layoutName={resolvedLayoutName} tempPath={registeredLayoutTempPath}");
            return true;
        }

        private static bool TryHydrateApartmentLayoutPayloadFromAddressInsert(
            StreetQuestShared.ApartmentEntryOption option,
            StreetQuestApartmentRegistrationSnapshot originalSnapshot,
            object registration,
            string tempLayoutPath,
            out StreetQuestApartmentInteriorPayload payload)
        {
            payload = null;
            if (registration is not BuildingRegistration typedRegistration ||
                string.IsNullOrWhiteSpace(tempLayoutPath) ||
                !File.Exists(tempLayoutPath))
                return false;

            try
            {
                var helperType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(ApartmentLayoutHelperTypeName, false))
                    .FirstOrDefault(type => type != null);
                if (helperType == null)
                    return false;

                var deserializeMethod = helperType.GetMethod(
                    "Deserialize",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);
                var addressInsertMethod = helperType.GetMethod(
                    "InsertLayoutSet",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typedRegistration.Address?.GetType(), deserializeMethod?.ReturnType, typeof(bool) },
                    null);
                if (deserializeMethod == null || addressInsertMethod == null || typedRegistration.Address == null)
                    return false;

                var layoutSet = deserializeMethod.Invoke(null, new object[] { tempLayoutPath });
                if (layoutSet == null)
                    return false;

                addressInsertMethod.Invoke(null, new[] { typedRegistration.Address, layoutSet, false as object });

                payload = new StreetQuestApartmentInteriorPayload
                {
                    Layout = GetMemberValue(typedRegistration, "Layout") as string,
                    InteriorDesigns = GetMemberValue(typedRegistration, "interiorDesigns"),
                    ItemInstances = GetMemberValue(typedRegistration, "itemInstances"),
                    ItemsInBuilding = GetMemberValue(typedRegistration, "itemsInBuilding"),
                    DeliveredItems = GetMemberValue(typedRegistration, "deliveredItems"),
                    DirtSpots = GetMemberValue(typedRegistration, "dirtSpots")
                };

                LogDebug(
                    $"ApartmentLayoutHydrated character={option.CharacterId} state={option.StateId} mode=address_insert layout={payload.Layout ?? "<null>"} interiorDesigns={DescribeValueShape(payload.InteriorDesigns)} itemInstances={DescribeValueShape(payload.ItemInstances)} itemsInBuilding={DescribeValueShape(payload.ItemsInBuilding)}");

                foreach (var fieldName in ApartmentRegistrationFieldNames)
                    SetMemberValue(typedRegistration, fieldName, originalSnapshot.GetRaw(fieldName));

                return true;
            }
            catch (TargetInvocationException exception)
            {
                foreach (var fieldName in ApartmentRegistrationFieldNames)
                    SetMemberValue(registration, fieldName, originalSnapshot.GetRaw(fieldName));

                var inner = exception.InnerException;
                LogDebug(
                    $"ApartmentLayoutAddressInsertFailed reason={exception.GetType().Name}:{exception.Message} inner={inner?.GetType().Name}:{inner?.Message} character={option?.CharacterId} state={option?.StateId} path={tempLayoutPath}");
                return false;
            }
            catch (Exception exception)
            {
                foreach (var fieldName in ApartmentRegistrationFieldNames)
                    SetMemberValue(registration, fieldName, originalSnapshot.GetRaw(fieldName));

                LogDebug(
                    $"ApartmentLayoutAddressInsertFailed reason={exception.GetType().Name}:{exception.Message} character={option?.CharacterId} state={option?.StateId} path={tempLayoutPath}");
                return false;
            }
        }

        private static void TryHydrateApartmentLayoutPayloadFromHelper(
            StreetQuestShared.ApartmentEntryOption option,
            object registration,
            string tempLayoutPath,
            ref object interiorDesigns,
            ref object itemInstances,
            ref object itemsInBuilding)
        {
            if (registration == null || string.IsNullOrWhiteSpace(tempLayoutPath) || !File.Exists(tempLayoutPath))
                return;

            try
            {
                var helperType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(ApartmentLayoutHelperTypeName, false))
                    .FirstOrDefault(type => type != null);
                if (helperType == null)
                {
                    LogDebug($"ApartmentLayoutHydrateFailed reason=helper_missing type={ApartmentLayoutHelperTypeName}");
                    return;
                }

                var deserializeMethod = helperType.GetMethod(
                    "Deserialize",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);
                if (deserializeMethod == null)
                {
                    LogDebug("ApartmentLayoutHydrateFailed reason=deserialize_missing");
                    return;
                }

                var layoutSet = deserializeMethod.Invoke(null, new object[] { tempLayoutPath });
                if (layoutSet == null)
                {
                    LogDebug($"ApartmentLayoutHydrateFailed reason=layoutset_null path={tempLayoutPath}");
                    return;
                }

                TryApplyLegacyLayoutFix(layoutSet);

                var layoutInteriorDesigns = GetMemberValue(layoutSet, "interiorDesigns");
                if (layoutInteriorDesigns != null)
                    interiorDesigns = layoutInteriorDesigns;

                var layoutItems = GetMemberValue(layoutSet, "Items");
                var convertedItemInstances = CreateResidentialItemInstancesFromLayoutItems(registration, layoutItems);
                if (convertedItemInstances != null)
                {
                    itemInstances = BuildItemInstanceDictionary(
                        convertedItemInstances,
                        GetMemberType(registration, "itemInstances"),
                        itemInstances,
                        out var itemCount);
                    itemsInBuilding = BuildItemsInBuilding(
                        GetMemberType(registration, "itemsInBuilding"),
                        itemInstances,
                        itemsInBuilding);

                    LogDebug(
                        $"ApartmentLayoutHydrated character={option.CharacterId} state={option.StateId} itemEntries={itemCount} interiorDesigns={DescribeValueShape(interiorDesigns)} itemInstances={DescribeValueShape(itemInstances)} itemsInBuilding={DescribeValueShape(itemsInBuilding)}");
                    return;
                }

                LogDebug(
                    $"ApartmentLayoutHydrateFailed reason=converted_items_null character={option.CharacterId} state={option.StateId} path={tempLayoutPath}");
            }
            catch (Exception exception)
            {
                LogDebug(
                    $"ApartmentLayoutHydrateFailed reason={exception.GetType().Name}:{exception.Message} character={option?.CharacterId} state={option?.StateId} path={tempLayoutPath}");
            }
        }

        private static void TryApplyLegacyLayoutFix(object layoutSet)
        {
            if (layoutSet == null)
                return;

            try
            {
                var fixerType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(BlueprintLegacyFixTypeName, false))
                    .FirstOrDefault(type => type != null);
                if (fixerType == null)
                    return;

                var applyLayoutMethod = fixerType.GetMethod(
                    "ApplyLayout",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { layoutSet.GetType() },
                    null);
                applyLayoutMethod?.Invoke(null, new[] { layoutSet });
            }
            catch (Exception exception)
            {
                LogDebug($"ApartmentLayoutLegacyFixSkipped reason={exception.GetType().Name}:{exception.Message}");
            }
        }

        private static object BuildItemInstanceDictionary(
            IEnumerable itemInstances,
            Type targetType,
            object fallback,
            out int entryCount)
        {
            entryCount = 0;
            if (targetType == null)
                return fallback;

            var dictionary = Activator.CreateInstance(targetType);
            if (dictionary is not IDictionary typedDictionary)
                return fallback;

            var keyType = typeof(string);
            if (targetType.IsGenericType)
            {
                var arguments = targetType.GetGenericArguments();
                if (arguments.Length >= 1)
                    keyType = arguments[0];
            }

            foreach (var itemInstance in itemInstances)
            {
                if (itemInstance == null)
                    continue;

                var rawId = GetMemberValue(itemInstance, "id")?.ToString() ??
                            GetMemberValue(itemInstance, "Id")?.ToString();
                if (string.IsNullOrWhiteSpace(rawId))
                    continue;

                var key = ConvertDictionaryKey(rawId, keyType);
                if (key == null)
                    continue;

                typedDictionary[key] = itemInstance;
                entryCount++;
            }

            return dictionary;
        }

        private static IEnumerable CreateResidentialItemInstancesFromLayoutItems(object registration, object layoutItems)
        {
            if (registration == null || layoutItems is not IEnumerable enumerableLayoutItems)
                return null;

            var itemInstancesType = GetMemberType(registration, "itemInstances");
            var itemInstanceType = itemInstancesType?.GetGenericArguments().LastOrDefault();
            if (itemInstanceType == null)
                return null;

            var resultListType = typeof(List<>).MakeGenericType(itemInstanceType);
            if (Activator.CreateInstance(resultListType) is not IList resultList)
                return null;

            var address = GetMemberValue(registration, "Address");
            var addressText = address?.ToString() ?? string.Empty;
            ParseAddressParts(addressText, out var streetName, out var streetNumber);

            foreach (var layoutItem in enumerableLayoutItems)
            {
                if (layoutItem == null)
                    continue;

                object itemInstance;
                try
                {
                    itemInstance = FormatterServices.GetUninitializedObject(itemInstanceType);
                }
                catch
                {
                    continue;
                }

                CopyMemberValue(layoutItem, itemInstance, "id", "id");
                CopyMemberValue(layoutItem, itemInstance, "itemName", "itemName");
                CopyMemberValue(layoutItem, itemInstance, "position", "position");
                CopyMemberValue(layoutItem, itemInstance, "rotation", "rotation");
                CopyMemberValue(layoutItem, itemInstance, "stackedItems", "stackedItems");
                CopyMemberValue(layoutItem, itemInstance, "parentId", "parentId");
                CopyMemberValue(layoutItem, itemInstance, "dirtSpotsThatAffects", "dirtSpotsThatAffects");
                CopyMemberValue(layoutItem, itemInstance, "customPositions", "customPositions");
                CopyMemberValue(layoutItem, itemInstance, "customColors", "customColors");
                CopyMemberValue(layoutItem, itemInstance, "customValue", "customValue");
                CopyMemberValue(layoutItem, itemInstance, "linkedItemName", "linkedItemName");
                CopyMemberValue(layoutItem, itemInstance, "worldSpaceTextValue", "worldSpaceTextValue");
                CopyMemberValue(layoutItem, itemInstance, "playerItemPurchaserSettings", "playerItemPurchaserSettings");
                CopyMemberValue(layoutItem, itemInstance, "dirtAffectedCells", "dirtAffectedCells");

                SetMemberValue(itemInstance, "streetName", streetName);
                SetMemberValue(itemInstance, "streetNumber", streetNumber);
                SetMemberValue(itemInstance, "_addressCached", address);
                SetMemberValue(itemInstance, "_itemCached", ResolveItemByName(GetMemberValue(itemInstance, "itemName") as string));
                SetMemberValue(itemInstance, "yRotation", ExtractYawDegrees(GetMemberValue(layoutItem, "rotation")));
                SetMemberValue(itemInstance, "priceOnPurchase", 0f);
                SetMemberValue(itemInstance, "pricePerUnitOnPurchaseTime", 0f);
                SetMemberValue(itemInstance, "stateIndex", 0);
                SetMemberValue(itemInstance, "instanceQuantity", 0);
                SetMemberValue(itemInstance, "paid", false);
                SetMemberValue(itemInstance, "isSecured", false);

                resultList.Add(itemInstance);
            }

            return resultList;
        }

        private static object ResolveItemByName(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return null;

            try
            {
                return ItemsGetter.GetByName(itemName) ??
                       ItemsGetter.AllItems?.FirstOrDefault(item =>
                           string.Equals(item?.itemName, itemName, StringComparison.Ordinal));
            }
            catch
            {
                return null;
            }
        }

        private static void ParseAddressParts(string addressText, out string streetName, out int streetNumber)
        {
            streetName = string.Empty;
            streetNumber = 0;
            if (string.IsNullOrWhiteSpace(addressText))
                return;

            var separatorIndex = addressText.LastIndexOf(' ');
            if (separatorIndex <= 0 || separatorIndex >= addressText.Length - 1)
            {
                streetName = addressText;
                return;
            }

            streetName = addressText.Substring(0, separatorIndex).Trim();
            int.TryParse(addressText.Substring(separatorIndex + 1).Trim(), out streetNumber);
        }

        private static void CopyMemberValue(object source, object destination, string sourceMemberName, string destinationMemberName)
        {
            var value = GetMemberValue(source, sourceMemberName);
            if (value != null)
                SetMemberValue(destination, destinationMemberName, value);
        }

        private static int ConvertToInt(object value)
        {
            try
            {
                return value == null ? 0 : Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private static float ExtractYawDegrees(object serializableQuaternion)
        {
            if (serializableQuaternion == null)
                return 0f;

            try
            {
                var x = Convert.ToSingle(GetMemberValue(serializableQuaternion, "x") ?? 0f);
                var y = Convert.ToSingle(GetMemberValue(serializableQuaternion, "y") ?? 0f);
                var z = Convert.ToSingle(GetMemberValue(serializableQuaternion, "z") ?? 0f);
                var w = Convert.ToSingle(GetMemberValue(serializableQuaternion, "w") ?? 1f);
                return Quaternion.Euler(0f, Quaternion.LookRotation((new Quaternion(x, y, z, w)) * Vector3.forward).eulerAngles.y, 0f).eulerAngles.y;
            }
            catch
            {
                return 0f;
            }
        }

        private static object BuildItemsInBuilding(Type targetType, object itemInstances, object fallback)
        {
            if (targetType == null)
                return fallback;

            var result = CreateEmptyValueLike(fallback, targetType) ?? CreateEmptyValueLike(itemInstances, targetType);
            if (result is not IList typedList || itemInstances is not IDictionary itemDictionary)
                return result ?? fallback;

            Type elementType = null;
            if (targetType.IsArray)
                elementType = targetType.GetElementType();
            else if (targetType.IsGenericType)
                elementType = targetType.GetGenericArguments().FirstOrDefault();

            if (elementType == null)
                return result ?? fallback;

            foreach (DictionaryEntry entry in itemDictionary)
            {
                if (elementType == typeof(string))
                {
                    typedList.Add(entry.Key?.ToString() ?? string.Empty);
                    continue;
                }

                if (entry.Value != null && elementType.IsInstanceOfType(entry.Value))
                    typedList.Add(entry.Value);
            }

            return result ?? fallback;
        }

        private static object ConvertDictionaryKey(string rawKey, Type keyType)
        {
            if (keyType == typeof(string))
                return rawKey;

            try
            {
                return Convert.ChangeType(rawKey, keyType);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryRegisterApartmentLayout(
            string layoutFile,
            string requestedLayoutName,
            out string resolvedLayoutName,
            out string tempLayoutPath)
        {
            resolvedLayoutName = string.Empty;
            tempLayoutPath = string.Empty;
            var modRootPath = StreetQuestRuntimeBootstrap.CurrentModRootPath;
            if (string.IsNullOrWhiteSpace(modRootPath) || string.IsNullOrWhiteSpace(layoutFile))
                return false;

            if (!TryResolveApartmentLayoutSourcePath(modRootPath, layoutFile, out var sourcePath))
            {
                LogDebug($"ApartmentLayoutRegisterFailed reason=file_missing path={layoutFile}");
                return false;
            }

            var normalizedSourceKey = sourcePath.Trim().ToLowerInvariant();
            if (RegisteredApartmentLayoutsBySource.TryGetValue(normalizedSourceKey, out var existingLayout) &&
                existingLayout != null &&
                !string.IsNullOrWhiteSpace(existingLayout.LayoutName) &&
                !string.IsNullOrWhiteSpace(existingLayout.TempPath) &&
                File.Exists(existingLayout.TempPath))
            {
                resolvedLayoutName = existingLayout.LayoutName;
                tempLayoutPath = existingLayout.TempPath;
                return true;
            }

            var helperType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(ApartmentLayoutHelperTypeName, false))
                .FirstOrDefault(type => type != null);
            if (helperType == null)
            {
                LogDebug($"ApartmentLayoutRegisterFailed reason=helper_missing type={ApartmentLayoutHelperTypeName}");
                return false;
            }

            var registerMethod = helperType.GetMethod(
                "SetBusinessLayoutSynchronous",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);
            if (registerMethod == null)
            {
                LogDebug("ApartmentLayoutRegisterFailed reason=helper_method_missing method=SetBusinessLayoutSynchronous");
                return false;
            }

            var layoutJson = File.ReadAllText(sourcePath);
            resolvedLayoutName = ResolveApartmentLayoutName(layoutFile, requestedLayoutName);
            var patchedJson = EnsureLayoutName(layoutJson, resolvedLayoutName);

            var tempDirectory = Path.Combine(Application.temporaryCachePath, "BAModLayouts", "StreetQuestRPG");
            Directory.CreateDirectory(tempDirectory);

            var tempFileName = $"{resolvedLayoutName}.json";
            var tempPath = Path.Combine(tempDirectory, tempFileName);
            File.WriteAllText(tempPath, patchedJson);
            registerMethod.Invoke(null, new object[] { tempPath });

            tempLayoutPath = tempPath;
            RegisteredApartmentLayoutsBySource[normalizedSourceKey] = new StreetQuestRegisteredApartmentLayout
            {
                LayoutName = resolvedLayoutName,
                TempPath = tempLayoutPath
            };
            LogDebug($"ApartmentLayoutRegistered source={layoutFile} layoutName={resolvedLayoutName} tempPath={tempPath}");
            return true;
        }

        private static bool TryResolveApartmentLayoutSourcePath(string modRootPath, string layoutFile, out string sourcePath)
        {
            sourcePath = string.Empty;
            if (string.IsNullOrWhiteSpace(modRootPath) || string.IsNullOrWhiteSpace(layoutFile))
                return false;

            var candidates = new[]
            {
                Path.Combine(modRootPath, layoutFile),
                Path.Combine(modRootPath, "Layouts", layoutFile),
                Path.Combine(modRootPath, "Layouts", Path.GetFileName(layoutFile) ?? string.Empty),
                Path.Combine(modRootPath, "Config", layoutFile),
                Path.Combine(modRootPath, "Config", Path.GetFileName(layoutFile) ?? string.Empty)
            };

            foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (!File.Exists(candidate))
                    continue;

                sourcePath = candidate;
                return true;
            }

            return false;
        }

        private static string ResolveApartmentLayoutName(string layoutFile, string requestedLayoutName)
        {
            if (!string.IsNullOrWhiteSpace(requestedLayoutName))
                return requestedLayoutName.Trim();

            var fileName = Path.GetFileNameWithoutExtension(layoutFile) ?? "StreetQuestApartment";
            return Regex.Replace(fileName, "[^A-Za-z0-9_]+", string.Empty);
        }

        private static string EnsureLayoutName(string json, string layoutName)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;

            var replacement = $"\"LayoutName\": \"{layoutName}\"";
            const string pattern = "\"LayoutName\"\\s*:\\s*\"[^\"]*\"";
            if (Regex.IsMatch(json, pattern, RegexOptions.CultureInvariant))
            {
                return Regex.Replace(json, pattern, replacement, RegexOptions.CultureInvariant);
            }

            return json;
        }

        private sealed class StreetQuestRegisteredApartmentLayout
        {
            public string LayoutName;
            public string TempPath;
        }
    }
}
