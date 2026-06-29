#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxInvestmentLimitService
    {
        private const long MaxNumeralAmountFallback = int.MaxValue;
        private const string DebugLogFileName = "bighax-investment-debug.log";
        private const string DebugFallbackFileName = "bighax-investment-debug.log";

        private static readonly string[] DialogPaths =
        {
            "Canvases/FullMenu/Canvas/AppsContainer/Contacts/Layout 30-70/Right/Conversation/Viewport/Content/PlayerMessageTemplate/InputTemplates/BankInvestment",
            "Canvases/PlayerHUD/DialogUI/Panel/Entries (Scroll View)/Viewport/Content/PlayerMessageTemplate/InputTemplates/BankInvestment"
        };

        private readonly List<InvestmentDialogTarget> resolvedTargets = new List<InvestmentDialogTarget>();

        public void InvalidateCache()
        {
            resolvedTargets.Clear();
        }

        public void ApplyConfiguredLimit(ModContext context, BigHaxSettings settings)
        {
            LogDebug($"ApplyConfiguredLimit start. ConfiguredLimit={settings.MaximumInvestmentAmount}.");
            var targets = ResolveTargets(context);
            LogDebug($"ResolveTargets finished. TargetCount={targets.Count}.");
            if (targets.Count == 0)
            {
                BigHaxLogger.WarnOnce(
                    context,
                    "missing-investment-targets",
                    "BigHax: could not resolve the fixed bank investment dialog templates.");
                LogDebug("No fixed bank investment dialog templates were resolved.");
                return;
            }

            var configuredLimit = settings.MaximumInvestmentAmount;
            foreach (var target in targets)
            {
                target.ApplyLimit(configuredLimit);
            }
        }

        public void RestoreOriginalLimit()
        {
            foreach (var target in resolvedTargets)
                target.RestoreOriginalValues();
        }

        private List<InvestmentDialogTarget> ResolveTargets(ModContext context)
        {
            if (resolvedTargets.Count > 0)
            {
                LogDebug($"Using cached investment targets. Count={resolvedTargets.Count}.");
                return resolvedTargets;
            }

            foreach (var dialogPath in DialogPaths)
            {
                LogDebug($"Resolving dialog path '{dialogPath}'.");
                var dialogTransform = ResolveDialogTransform(dialogPath);
                if (dialogTransform == null)
                {
                    LogDebug($"Dialog path '{dialogPath}' was not found.");
                    continue;
                }

                LogDebug(
                    $"Dialog path '{dialogPath}' resolved to '{BuildHierarchyPath(dialogTransform)}' (activeSelf={dialogTransform.gameObject.activeSelf}, activeInHierarchy={dialogTransform.gameObject.activeInHierarchy}).");

                var dialogTarget = TryCreateTarget(dialogTransform.gameObject);
                if (dialogTarget == null)
                {
                    BigHaxLogger.WarnOnce(
                        context,
                        "invalid-investment-target-" + dialogPath,
                        $"BigHax: found investment dialog path '{dialogPath}' but could not patch its fields.");
                    LogDebug($"Dialog path '{dialogPath}' resolved, but target creation failed.");
                    continue;
                }

                resolvedTargets.Add(dialogTarget);
                LogDebug($"Dialog path '{dialogPath}' target creation succeeded.");
            }

            return resolvedTargets;
        }

        private static Transform? ResolveDialogTransform(string dialogPath)
        {
            var separatorIndex = dialogPath.IndexOf('/');
            var rootName = separatorIndex >= 0 ? dialogPath.Substring(0, separatorIndex) : dialogPath;
            var relativePath = separatorIndex >= 0 ? dialogPath.Substring(separatorIndex + 1) : string.Empty;
            var rootObject = GameObject.Find(rootName);
            if (rootObject == null)
            {
                LogDebug($"GameObject.Find('{rootName}') returned null.");
                return null;
            }

            LogDebug($"GameObject.Find('{rootName}') found '{rootObject.name}' (activeSelf={rootObject.activeSelf}, activeInHierarchy={rootObject.activeInHierarchy}).");

            if (string.IsNullOrEmpty(relativePath))
                return rootObject.transform;

            var result = rootObject.transform.Find(relativePath);
            if (result == null)
                LogDebug($"Transform.Find('{relativePath}') under '{rootName}' returned null.");

            return result;
        }

        private static InvestmentDialogTarget? TryCreateTarget(GameObject dialogGameObject)
        {
            LogDebug($"TryCreateTarget on '{dialogGameObject.name}'.");
            var dialogComponent = FindComponentWithField(dialogGameObject, "maxInvestment");
            if (dialogComponent == null)
            {
                LogDebug($"No component with field 'maxInvestment' found on '{dialogGameObject.name}'.");
                return null;
            }

            var dialogType = dialogComponent.GetType();
            var maxInvestmentField = dialogType.GetField("maxInvestment", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var amountFieldField = dialogType.GetField("amountField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (maxInvestmentField == null || amountFieldField == null)
            {
                LogDebug($"Dialog component '{dialogType.FullName}' is missing maxInvestment or amountField.");
                return null;
            }

            var amountField = amountFieldField.GetValue(dialogComponent);
            if (amountField == null)
            {
                LogDebug($"Dialog component '{dialogType.FullName}' returned null amountField.");
                return null;
            }

            var maxNumeralAmountField = amountField.GetType().GetField(
                "maxNumeralAmount",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (maxNumeralAmountField == null)
            {
                LogDebug($"Amount field component '{amountField.GetType().FullName}' is missing maxNumeralAmount.");
                return null;
            }

            LogDebug(
                $"Dialog component '{dialogType.FullName}' with amount field '{amountField.GetType().FullName}' is patchable. maxInvestmentType={maxInvestmentField.FieldType.FullName}, maxNumeralAmountType={maxNumeralAmountField.FieldType.FullName}, currentMaxInvestment={FormatObject(maxInvestmentField.GetValue(dialogComponent))}, currentMaxNumeralAmount={FormatObject(maxNumeralAmountField.GetValue(amountField))}.");

            return new InvestmentDialogTarget(dialogComponent, maxInvestmentField, amountField, maxNumeralAmountField);
        }

        private static Component? FindComponentWithField(GameObject gameObject, string fieldName)
        {
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                if (component.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null)
                    return component;
            }

            return null;
        }

        private sealed class InvestmentDialogTarget
        {
            private readonly object amountField;
            private readonly object dialog;
            private readonly FieldInfo maxInvestmentField;
            private readonly FieldInfo maxNumeralAmountField;
            private readonly object? originalMaxInvestment;
            private readonly object? originalMaxNumeralAmount;

            public InvestmentDialogTarget(
                object dialog,
                FieldInfo maxInvestmentField,
                object amountField,
                FieldInfo maxNumeralAmountField)
            {
                this.dialog = dialog;
                this.maxInvestmentField = maxInvestmentField;
                this.amountField = amountField;
                this.maxNumeralAmountField = maxNumeralAmountField;
                originalMaxInvestment = maxInvestmentField.GetValue(dialog);
                originalMaxNumeralAmount = maxNumeralAmountField.GetValue(amountField);
            }

            public void ApplyLimit(long configuredLimit)
            {
                LogDebug(
                    $"Applying limit {configuredLimit} to dialogType={dialog.GetType().FullName}. Before: maxInvestment={FormatObject(maxInvestmentField.GetValue(dialog))}, maxNumeralAmount={FormatObject(maxNumeralAmountField.GetValue(amountField))}.");
                TrySetNumericValue(maxInvestmentField, dialog, configuredLimit);

                if (!TrySetNumericValue(maxNumeralAmountField, amountField, configuredLimit))
                {
                    var clampedValue = Math.Min(configuredLimit, MaxNumeralAmountFallback);
                    TrySetNumericValue(maxNumeralAmountField, amountField, clampedValue);
                    LogDebug($"maxNumeralAmount required fallback clamp to {clampedValue}.");
                }

                LogDebug(
                    $"After apply: maxInvestment={FormatObject(maxInvestmentField.GetValue(dialog))}, maxNumeralAmount={FormatObject(maxNumeralAmountField.GetValue(amountField))}.");
            }

            public void RestoreOriginalValues()
            {
                maxInvestmentField.SetValue(dialog, originalMaxInvestment);
                maxNumeralAmountField.SetValue(amountField, originalMaxNumeralAmount);
            }
        }

        private static bool TrySetNumericValue(FieldInfo field, object target, long value)
        {
            var fieldType = field.FieldType;
            try
            {
                if (fieldType == typeof(int))
                {
                    if (value > int.MaxValue)
                        return false;

                    field.SetValue(target, (int)value);
                    return true;
                }

                if (fieldType == typeof(long))
                {
                    field.SetValue(target, value);
                    return true;
                }

                if (fieldType == typeof(float))
                {
                    field.SetValue(target, (float)value);
                    return true;
                }

                if (fieldType == typeof(double))
                {
                    field.SetValue(target, (double)value);
                    return true;
                }

                if (fieldType == typeof(decimal))
                {
                    field.SetValue(target, (decimal)value);
                    return true;
                }

                var convertedValue = Convert.ChangeType(value, fieldType);
                field.SetValue(target, convertedValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string FormatObject(object? value)
        {
            return value?.ToString() ?? "<null>";
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static void LogDebug(string message)
        {
            BigHaxFileLogger.Log(DebugLogFileName, DebugFallbackFileName, "[Investment] " + message);
        }
    }
}
