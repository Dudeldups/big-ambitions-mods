using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        private const string ApartmentLayoutHelperTypeName = "BusinessLayoutSets.BusinessLayoutSetHelper";
        private static readonly Dictionary<string, string> RegisteredApartmentLayoutNamesBySource =
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

            if (!TryRegisterApartmentLayout(option.ApartmentLayoutFile, option.ApartmentLayoutName, out var resolvedLayoutName))
                return false;

            var interiorDesigns = CreateEmptyValueLike(originalSnapshot?.GetRaw("interiorDesigns"), GetMemberType(registration, "interiorDesigns"));
            var itemInstances = CreateEmptyValueLike(originalSnapshot?.GetRaw("itemInstances"), GetMemberType(registration, "itemInstances"));
            var itemsInBuilding = CreateEmptyValueLike(originalSnapshot?.GetRaw("itemsInBuilding"), GetMemberType(registration, "itemsInBuilding"));
            var deliveredItems = CreateEmptyValueLike(originalSnapshot?.GetRaw("deliveredItems"), GetMemberType(registration, "deliveredItems"));
            var dirtSpots = CreateEmptyValueLike(originalSnapshot?.GetRaw("dirtSpots"), GetMemberType(registration, "dirtSpots"));

            if (TryResolveApartmentLayoutSourcePath(StreetQuestRuntimeBootstrap.CurrentModRootPath, option.ApartmentLayoutFile, out var sourcePath))
            {
                try
                {
                    var layoutJson = EnsureLayoutName(File.ReadAllText(sourcePath), resolvedLayoutName);
                    interiorDesigns = DeserializeInteriorDesigns(layoutJson, GetMemberType(registration, "interiorDesigns"), interiorDesigns);
                    itemInstances = DeserializeItemInstances(layoutJson, GetMemberType(registration, "itemInstances"), itemInstances, out var hydratedItemEntries);
                    itemsInBuilding = BuildItemsInBuilding(layoutJson, GetMemberType(registration, "itemsInBuilding"), itemInstances, itemsInBuilding);

                    LogDebug(
                        $"ApartmentLayoutHydrated character={option.CharacterId} state={option.StateId} itemEntries={hydratedItemEntries} interiorDesigns={DescribeValueShape(interiorDesigns)} itemsInBuilding={DescribeValueShape(itemsInBuilding)}");
                }
                catch (Exception exception)
                {
                    LogDebug(
                        $"ApartmentLayoutHydrateFailed character={option.CharacterId} state={option.StateId} file={option.ApartmentLayoutFile} reason={exception.GetType().Name}:{exception.Message}");
                }
            }

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
                $"ApartmentLayoutPayloadPrepared character={option.CharacterId} state={option.StateId} layoutFile={option.ApartmentLayoutFile} layoutName={resolvedLayoutName}");
            return true;
        }

        private static object DeserializeInteriorDesigns(string layoutJson, Type targetType, object fallback)
        {
            if (targetType == null || string.IsNullOrWhiteSpace(layoutJson))
                return fallback;

            if (!TryExtractJsonPropertyArray(layoutJson, "interiorDesigns", out var jsonFragment))
                return fallback;

            return DeserializeJsonFragment(jsonFragment, targetType) ?? fallback;
        }

        private static object DeserializeItemInstances(
            string layoutJson,
            Type targetType,
            object fallback,
            out int entryCount)
        {
            entryCount = 0;
            if (targetType == null || string.IsNullOrWhiteSpace(layoutJson))
                return fallback;

            if (!TryExtractJsonPropertyArray(layoutJson, "Items", out var jsonFragment))
                return fallback;

            var dictionary = Activator.CreateInstance(targetType);
            if (dictionary is not IDictionary typedDictionary)
                return fallback;

            var keyType = typeof(string);
            var valueType = typeof(object);
            if (targetType.IsGenericType)
            {
                var arguments = targetType.GetGenericArguments();
                if (arguments.Length >= 2)
                {
                    keyType = arguments[0];
                    valueType = arguments[1];
                }
            }

            var listType = typeof(List<>).MakeGenericType(valueType);
            var deserialized = DeserializeJsonFragment(jsonFragment, listType) as IEnumerable;
            if (deserialized == null)
                return fallback;

            foreach (var entry in deserialized)
            {
                if (entry == null)
                    continue;

                var rawId = GetMemberValue(entry, "id")?.ToString();
                if (string.IsNullOrWhiteSpace(rawId))
                    continue;

                var key = ConvertDictionaryKey(rawId, keyType);
                if (key == null)
                    continue;

                typedDictionary[key] = entry;
                entryCount++;
            }

            return dictionary;
        }

        private static object BuildItemsInBuilding(string layoutJson, Type targetType, object itemInstances, object fallback)
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

        private static object DeserializeJsonFragment(string json, Type targetType)
        {
            if (string.IsNullOrWhiteSpace(json) || targetType == null)
                return null;

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var serializer = new DataContractJsonSerializer(targetType);
            return serializer.ReadObject(stream);
        }

        private static bool TryExtractJsonPropertyArray(string json, string propertyName, out string fragment)
        {
            fragment = string.Empty;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
                return false;

            var propertyToken = $"\"{propertyName}\"";
            var propertyIndex = json.IndexOf(propertyToken, StringComparison.Ordinal);
            if (propertyIndex < 0)
                return false;

            var arrayStart = json.IndexOf('[', propertyIndex + propertyToken.Length);
            if (arrayStart < 0)
                return false;

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var index = arrayStart; index < json.Length; index++)
            {
                var current = json[index];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                    continue;

                if (current == '[')
                {
                    depth++;
                    continue;
                }

                if (current != ']')
                    continue;

                depth--;
                if (depth != 0)
                    continue;

                fragment = json.Substring(arrayStart, index - arrayStart + 1);
                return true;
            }

            return false;
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
            out string resolvedLayoutName)
        {
            resolvedLayoutName = string.Empty;
            var modRootPath = StreetQuestRuntimeBootstrap.CurrentModRootPath;
            if (string.IsNullOrWhiteSpace(modRootPath) || string.IsNullOrWhiteSpace(layoutFile))
                return false;

            if (!TryResolveApartmentLayoutSourcePath(modRootPath, layoutFile, out var sourcePath))
            {
                LogDebug($"ApartmentLayoutRegisterFailed reason=file_missing path={layoutFile}");
                return false;
            }

            var normalizedSourceKey = sourcePath.Trim().ToLowerInvariant();
            if (RegisteredApartmentLayoutNamesBySource.TryGetValue(normalizedSourceKey, out resolvedLayoutName) &&
                !string.IsNullOrWhiteSpace(resolvedLayoutName))
            {
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

            RegisteredApartmentLayoutNamesBySource[normalizedSourceKey] = resolvedLayoutName;
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
    }
}
