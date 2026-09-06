#nullable enable
using System.Reflection;
using Buildings.Office.Headquarters;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxHrManagerCapacityService
    {
        private const int DiagnosticCalculationLimit = 20;

        private static bool enabled;
        private static int diagnosticCalculationCount;
        private BigHaxMethodDetour? capacityDetour;

        public void Initialize()
        {
            var target = typeof(HrManagerHelper).GetMethod(
                nameof(HrManagerHelper.CalculateMaxAssignableEmployees),
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(float) },
                null);
            var replacement = typeof(BigHaxHrManagerCapacityService).GetMethod(
                nameof(CalculateMaxAssignableEmployees),
                BindingFlags.NonPublic | BindingFlags.Static);

            if (target == null || replacement == null)
            {
                BigHaxLogger.HrManagerDiagnostic("HR manager capacity detour failed: method not found.");
                return;
            }

            capacityDetour = new BigHaxMethodDetour(target, replacement);
            if (!capacityDetour.Apply(out var error))
            {
                BigHaxLogger.HrManagerDiagnostic("HR manager capacity detour failed: " + error);
                return;
            }

            BigHaxLogger.HrManagerDiagnostic("HR manager capacity detour installed.");
        }

        public void ApplyConfiguredBehavior(BigHaxSettings settings)
        {
            var changed = enabled != settings.EnableMaximumHrManagerCapacity;
            enabled = settings.EnableMaximumHrManagerCapacity;
            if (!changed)
                return;

            diagnosticCalculationCount = 0;
            BigHaxLogger.HrManagerDiagnostic(
                "HR manager capacity configured: enabled=" + enabled +
                ", override=" + BigHaxSettings.MaximumHrManagerCapacity +
                ", detour=" + (capacityDetour?.IsApplied == true) + ".");
        }

        public void Shutdown()
        {
            enabled = false;
            if (capacityDetour != null && !capacityDetour.Restore(out var error))
                BigHaxLogger.HrManagerDiagnostic("HR manager capacity detour restore skipped: " + error);

            capacityDetour = null;
            diagnosticCalculationCount = 0;
        }

        private static int CalculateMaxAssignableEmployees(float skill)
        {
            // Mirrors the vanilla 1.0 formula so disabling the hax preserves the
            // original skill-scaled range of 10 through 50 employees.
            var vanillaCapacity = 10 + (Mathf.FloorToInt(skill / 5f / 5f) * 10);
            var capacity = enabled ? BigHaxSettings.MaximumHrManagerCapacity : vanillaCapacity;

            if (diagnosticCalculationCount < DiagnosticCalculationLimit)
            {
                diagnosticCalculationCount++;
                BigHaxLogger.HrManagerDiagnostic(
                    "HR manager capacity calculated: skill=" + skill +
                    ", enabled=" + enabled +
                    ", vanillaCapacity=" + vanillaCapacity +
                    ", result=" + capacity + ".");
            }

            return capacity;
        }
    }
}
