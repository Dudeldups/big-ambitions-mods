using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Buildings;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        private const string ApartmentLayoutHelperTypeName = "BusinessLayoutSets.BusinessLayoutSetHelper";
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

            payload = new StreetQuestApartmentInteriorPayload
            {
                Layout = resolvedLayoutName,
                InteriorDesigns = interiorDesigns,
                ItemInstances = itemInstances,
                ItemsInBuilding = itemsInBuilding,
                DeliveredItems = deliveredItems,
                DirtSpots = dirtSpots,
                RegisteredLayoutTempPath = registeredLayoutTempPath
            };

            LogDebug(
                $"ApartmentLayoutPayloadPrepared character={option.CharacterId} state={option.StateId} layoutFile={option.ApartmentLayoutFile} layoutName={resolvedLayoutName} tempPath={registeredLayoutTempPath}");
            return true;
        }

        private static bool TryInsertApartmentLayoutSet(
            BuildingRegistration registration,
            string tempLayoutPath,
            string layoutName)
        {
            if (registration == null || string.IsNullOrWhiteSpace(tempLayoutPath) || !File.Exists(tempLayoutPath))
                return false;

            try
            {
                var helperType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(ApartmentLayoutHelperTypeName, false))
                    .FirstOrDefault(type => type != null);
                if (helperType == null)
                {
                    LogDebug($"ApartmentLayoutInsertFailed reason=helper_missing type={ApartmentLayoutHelperTypeName}");
                    return false;
                }

                var deserializeMethod = helperType.GetMethod(
                    "Deserialize",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);
                if (deserializeMethod == null)
                {
                    LogDebug("ApartmentLayoutInsertFailed reason=deserialize_missing");
                    return false;
                }

                var layoutSet = deserializeMethod.Invoke(null, new object[] { tempLayoutPath });
                if (layoutSet == null)
                {
                    LogDebug($"ApartmentLayoutInsertFailed reason=layoutset_null path={tempLayoutPath}");
                    return false;
                }

                var insertMethod = helperType.GetMethod(
                    "InsertLayoutSet",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(BuildingRegistration), layoutSet.GetType(), typeof(bool), typeof(bool) },
                    null);
                if (insertMethod == null)
                {
                    LogDebug("ApartmentLayoutInsertFailed reason=insert_missing");
                    return false;
                }

                insertMethod.Invoke(null, new object[] { registration, layoutSet, false, false });
                LogDebug(
                    $"ApartmentLayoutInserted layout={layoutName} tempPath={tempLayoutPath} itemInstances={DescribeValueShape(GetMemberValue(registration, "itemInstances"))} itemsInBuilding={DescribeValueShape(GetMemberValue(registration, "itemsInBuilding"))}");
                return true;
            }
            catch (TargetInvocationException exception)
            {
                var inner = exception.InnerException;
                LogDebug(
                    $"ApartmentLayoutInsertFailed reason={exception.GetType().Name}:{exception.Message} inner={inner?.GetType().Name}:{inner?.Message} layout={layoutName} path={tempLayoutPath} registrationLayout={GetMemberValue(registration, "Layout")} buildingType={GetMemberValue(registration, "BuildingType")} buildingSize={GetMemberValue(registration, "BuildingSize")} businessType={GetMemberValue(registration, "BusinessType")}");
                return false;
            }
            catch (Exception exception)
            {
                LogDebug(
                    $"ApartmentLayoutInsertFailed reason={exception.GetType().Name}:{exception.Message} layout={layoutName} path={tempLayoutPath} registrationLayout={GetMemberValue(registration, "Layout")} buildingType={GetMemberValue(registration, "BuildingType")} buildingSize={GetMemberValue(registration, "BuildingSize")} businessType={GetMemberValue(registration, "BusinessType")}");
                return false;
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
