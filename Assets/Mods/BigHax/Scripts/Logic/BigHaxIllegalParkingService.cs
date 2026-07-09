#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Entities;
using Helpers;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxIllegalParkingService
    {
        private const string DebugLogFileName = "BigHax-parking-debug.log";
        private static readonly HashSet<string> ParkingTicketMessageKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "phone_government_parking_ticket",
            "ba:messagetype_phone_government_parking_ticket"
        };
        private static readonly HashSet<string> ParkingTicketTransactionTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ba:transaction_parkingticket",
            "transaction_parkingticket"
        };
        private readonly Dictionary<string, VehicleSnapshot> lastVehicleSnapshots = new Dictionary<string, VehicleSnapshot>(StringComparer.Ordinal);

        public void InvalidateCache()
        {
        }

        public void ApplyConfiguredBehavior(ModContext? context, BigHaxSettings settings)
        {
            if (!settings.DisableIllegalParkingPenalties)
                return;

            Log("ApplyConfiguredBehavior invoked.");
            CleanPlayerVehicles();
            CleanSaveGameVehicles();
            RefundParkingTicketTransactions();
            RemoveParkingTicketMessages();
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

        public void HandleVehicleExited(ModContext? context, BigHaxSettings settings)
        {
            if (!settings.DisableIllegalParkingPenalties)
                return;

            Log("Handling onExitVehicle refresh.");
            ApplyConfiguredBehavior(context, settings);
        }

        public void HandleVehicleEntered(ModContext? context, BigHaxSettings settings)
        {
            if (!settings.DisableIllegalParkingPenalties)
                return;

            Log("Handling onEnterVehicle refresh.");
            ApplyConfiguredBehavior(context, settings);
        }

        public void RestoreOriginalState()
        {
            lastVehicleSnapshots.Clear();
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

        private void CleanSaveGameVehicles()
        {
            var saveGame = SaveGameManager.Current;
            var vehicles = saveGame?.VehicleInstances;
            if (vehicles == null)
                return;

            foreach (var vehicleInstance in vehicles)
            {
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

                if (!changed)
                    continue;

                Log(
                    $"Saved vehicle '{vehicleInstance.id}' ({vehicleInstance.vehicleTypeName}) cleanup: " +
                    $"state {parkingState} -> {vehicleInstance.parkingState}, " +
                    $"tickets {ticketCount} -> {vehicleInstance.parkingTickets?.Count ?? 0}, " +
                    $"unpaid {unpaidAmount} -> {vehicleInstance.unpaidParkingAmount}.");
            }
        }

        private void RefundParkingTicketTransactions()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.Transactions == null || saveGame.Transactions.Count == 0)
                return;

            var originalCount = saveGame.Transactions.Count;
            var refundedAmount = 0f;
            var keptTransactions = new Queue<Transaction>(originalCount);
            while (saveGame.Transactions.Count > 0)
            {
                var transaction = saveGame.Transactions.Dequeue();
                if (transaction != null &&
                    !string.IsNullOrWhiteSpace(transaction.transactionType) &&
                    ParkingTicketTransactionTypes.Contains(transaction.transactionType))
                {
                    refundedAmount += Mathf.Abs(transaction.amount);
                    continue;
                }

                if (transaction != null)
                    keptTransactions.Enqueue(transaction);
            }

            saveGame.Transactions = keptTransactions;
            if (refundedAmount <= 0.001f)
                return;

            saveGame.Money += refundedAmount;
            Log($"Refunded parking-ticket transactions amount={refundedAmount:0.##}, removed={originalCount - keptTransactions.Count}.");
        }

        private void RemoveParkingTicketMessages()
        {
            var contacts = SaveGameManager.Current?.Contacts;
            if (contacts == null)
                return;

            foreach (var contact in contacts)
            {
                if (contact?.messagesQueue == null || contact.messagesQueue.Count == 0)
                    continue;

                var originalCount = contact.messagesQueue.Count;
                var keptMessages = new Queue<TextMessage>(originalCount);
                while (contact.messagesQueue.Count > 0)
                {
                    var message = contact.messagesQueue.Dequeue();
                    if (message != null &&
                        !string.IsNullOrWhiteSpace(message.messageKey) &&
                        ParkingTicketMessageKeys.Contains(message.messageKey))
                    {
                        continue;
                    }

                    if (message != null)
                        keptMessages.Enqueue(message);
                }

                contact.messagesQueue = keptMessages;
                if (originalCount != keptMessages.Count)
                    Log($"Removed {originalCount - keptMessages.Count} parking-ticket message(s) from contact '{contact.id}'.");
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
