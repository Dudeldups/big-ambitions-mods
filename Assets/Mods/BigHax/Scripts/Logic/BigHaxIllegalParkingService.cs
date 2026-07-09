#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxIllegalParkingService
    {
        private const string DebugLogFileName = "BigHax-parking-debug.log";
        private static readonly FieldInfo? ParkingTicketFeeField =
            typeof(ParkingSimulator).GetField("ParkingTicketFee", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        private readonly Dictionary<string, VehicleSnapshot> lastVehicleSnapshots = new Dictionary<string, VehicleSnapshot>(StringComparer.Ordinal);
        private bool capturedOriginalFee;
        private int originalParkingTicketFee;
        private bool warnedReadOnlyFeeField;
        private bool warnedMissingFeeField;

        public void InvalidateCache()
        {
        }

        public void ApplyConfiguredBehavior(ModContext? context, BigHaxSettings settings)
        {
            if (!settings.DisableIllegalParkingPenalties)
            {
                RestoreOriginalParkingTicketFee();
                return;
            }

            TryApplyZeroParkingTicketFee();
            CleanPlayerVehicles();
        }

        public void HandleNewHour(ModContext? context, BigHaxSettings settings)
        {
            if (!settings.DisableIllegalParkingPenalties)
                return;

            Log("Handling onNewHour refresh.");
            ApplyConfiguredBehavior(context, settings);
        }

        public void HandleNewDay(ModContext? context, BigHaxSettings settings)
        {
            if (!settings.DisableIllegalParkingPenalties)
                return;

            Log("Handling onNewDay refresh.");
            ApplyConfiguredBehavior(context, settings);
        }

        public void RestoreOriginalState()
        {
            RestoreOriginalParkingTicketFee();
            lastVehicleSnapshots.Clear();
        }

        private void TryApplyZeroParkingTicketFee()
        {
            if (ParkingTicketFeeField == null)
            {
                if (!warnedMissingFeeField)
                {
                    warnedMissingFeeField = true;
                    Log("ParkingTicketFee field could not be resolved via reflection.");
                }

                return;
            }

            if (!capturedOriginalFee)
            {
                originalParkingTicketFee = ReadParkingTicketFee();
                capturedOriginalFee = true;
                Log($"Captured original ParkingTicketFee={originalParkingTicketFee}.");
            }

            if (ParkingTicketFeeField.IsLiteral || ParkingTicketFeeField.IsInitOnly)
            {
                if (!warnedReadOnlyFeeField)
                {
                    warnedReadOnlyFeeField = true;
                    Log("ParkingTicketFee is read-only/constant in this build; skipping fee override and relying on vehicle cleanup.");
                }

                return;
            }

            var currentFee = ReadParkingTicketFee();
            if (currentFee == 0)
                return;

            Log($"Setting ParkingTicketFee from {currentFee} to 0.");
            try
            {
                WriteParkingTicketFee(0);
            }
            catch (Exception exception)
            {
                if (!warnedReadOnlyFeeField)
                {
                    warnedReadOnlyFeeField = true;
                    Log($"ParkingTicketFee override failed; relying on vehicle cleanup instead. {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        private void RestoreOriginalParkingTicketFee()
        {
            if (!capturedOriginalFee || ParkingTicketFeeField == null)
                return;

            var currentFee = ReadParkingTicketFee();
            if (currentFee == originalParkingTicketFee)
                return;

            Log($"Restoring ParkingTicketFee from {currentFee} to {originalParkingTicketFee}.");
            WriteParkingTicketFee(originalParkingTicketFee);
        }

        private void CleanPlayerVehicles()
        {
            var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
            if (allPlayerVehicles == null)
                return;

            foreach (var vehicleController in allPlayerVehicles)
            {
                var vehicleInstance = vehicleController?.vehicleInstance;
                if (vehicleInstance == null || string.IsNullOrWhiteSpace(vehicleInstance.id))
                    continue;

                var ticketCount = vehicleInstance.parkingTickets?.Count ?? 0;
                var unpaidAmount = vehicleInstance.unpaidParkingAmount;
                var parkingState = vehicleInstance.parkingState;
                var changed = false;

                if (ticketCount > 0 && vehicleInstance.parkingTickets != null)
                {
                    vehicleInstance.parkingTickets.Clear();
                    changed = true;
                }

                if (Math.Abs(unpaidAmount) > 0.001f)
                {
                    vehicleInstance.unpaidParkingAmount = 0f;
                    changed = true;
                }

                if (parkingState == ParkingState.Illegal)
                {
                    vehicleInstance.parkingState = ParkingState.Legal;
                    changed = true;
                }

                var currentSnapshot = new VehicleSnapshot(
                    vehicleInstance.vehicleTypeName ?? string.Empty,
                    parkingState,
                    ticketCount,
                    unpaidAmount);

                if (!changed || !NeedsLog(vehicleInstance.id, currentSnapshot))
                    continue;

                Log(
                    $"Vehicle '{vehicleInstance.id}' ({vehicleInstance.vehicleTypeName}) cleanup: " +
                    $"state {parkingState} -> {vehicleInstance.parkingState}, " +
                    $"tickets {ticketCount} -> {vehicleInstance.parkingTickets?.Count ?? 0}, " +
                    $"unpaid {unpaidAmount} -> {vehicleInstance.unpaidParkingAmount}.");
            }
        }

        private bool NeedsLog(string vehicleId, VehicleSnapshot snapshot)
        {
            if (!lastVehicleSnapshots.TryGetValue(vehicleId, out var previousSnapshot) ||
                !previousSnapshot.Equals(snapshot))
            {
                lastVehicleSnapshots[vehicleId] = snapshot;
                return true;
            }

            return false;
        }

        private static void Log(string message)
        {
            BigHaxFileLogger.Log(DebugLogFileName, DebugLogFileName, $"[parking] {message}");
        }

        private static int ReadParkingTicketFee()
        {
            if (ParkingTicketFeeField?.GetValue(null) is int intValue)
                return intValue;

            if (ParkingTicketFeeField?.GetValue(null) is float floatValue)
                return Mathf.RoundToInt(floatValue);

            return 0;
        }

        private static void WriteParkingTicketFee(int value)
        {
            if (ParkingTicketFeeField == null)
                return;

            if (ParkingTicketFeeField.FieldType == typeof(int))
            {
                ParkingTicketFeeField.SetValue(null, value);
                return;
            }

            if (ParkingTicketFeeField.FieldType == typeof(float))
            {
                ParkingTicketFeeField.SetValue(null, (float)value);
                return;
            }

            ParkingTicketFeeField.SetValue(null, Convert.ChangeType(value, ParkingTicketFeeField.FieldType));
        }

        private readonly struct VehicleSnapshot : IEquatable<VehicleSnapshot>
        {
            public VehicleSnapshot(string vehicleTypeName, ParkingState state, int ticketCount, float unpaidAmount)
            {
                VehicleTypeName = vehicleTypeName;
                State = state;
                TicketCount = ticketCount;
                UnpaidAmount = unpaidAmount;
            }

            public string VehicleTypeName { get; }
            public ParkingState State { get; }
            public int TicketCount { get; }
            public float UnpaidAmount { get; }

            public bool Equals(VehicleSnapshot other)
            {
                return string.Equals(VehicleTypeName, other.VehicleTypeName, StringComparison.Ordinal) &&
                       State == other.State &&
                       TicketCount == other.TicketCount &&
                       Math.Abs(UnpaidAmount - other.UnpaidAmount) < 0.001f;
            }

            public override bool Equals(object? obj)
            {
                return obj is VehicleSnapshot other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = VehicleTypeName != null ? StringComparer.Ordinal.GetHashCode(VehicleTypeName) : 0;
                    hashCode = (hashCode * 397) ^ (int)State;
                    hashCode = (hashCode * 397) ^ TicketCount;
                    hashCode = (hashCode * 397) ^ Mathf.RoundToInt(UnpaidAmount * 1000f);
                    return hashCode;
                }
            }
        }
    }
}
