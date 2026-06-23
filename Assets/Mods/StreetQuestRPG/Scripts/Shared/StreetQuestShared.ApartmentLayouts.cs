using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
            out StreetQuestApartmentInteriorPayload payload)
        {
            payload = null;
            if (option == null || string.IsNullOrWhiteSpace(option.ApartmentLayoutFile))
                return false;

            if (!TryRegisterApartmentLayout(option.ApartmentLayoutFile, option.ApartmentLayoutName, out var resolvedLayoutName))
                return false;

            payload = new StreetQuestApartmentInteriorPayload
            {
                Layout = resolvedLayoutName,
                InteriorDesigns = null,
                ItemInstances = null,
                ItemsInBuilding = null,
                DeliveredItems = null,
                DirtSpots = null
            };

            LogDebug(
                $"ApartmentLayoutPayloadPrepared character={option.CharacterId} state={option.StateId} layoutFile={option.ApartmentLayoutFile} layoutName={resolvedLayoutName}");
            return true;
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
