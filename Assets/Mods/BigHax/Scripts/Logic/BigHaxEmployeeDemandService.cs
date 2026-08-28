#nullable enable
using System.Collections;
using System;
using System.Reflection;
using BAModAPI;
using Entities;
using Helpers;
using UI.Smartphone.Apps.Contacts;

namespace BigHax
{
    internal sealed class BigHaxEmployeeDemandService
    {
        private const string EmployeeHiredEvent = "ba:gameevent_employeehired";
        private const string NewDayEvent = "ba:gameevent_newday";
        private const string NewDemandMessageKey = "ba:messagetype_employee_contact_message_new_demand";
        private const string DebugLogFile = "BigHax-employee-demands.log";

        private static readonly FieldInfo? DemandsGeneratedTodayField =
            typeof(EmployeeHelper).GetField("DemandsGeneratedToday", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo? ContactsToSendNotificationField =
            typeof(ContactsHelper).GetField("ContactsToSendNotification", BindingFlags.Static | BindingFlags.NonPublic);

        private ModContext? context;
        private bool isEnabled;
        private bool isSubscribed;
        private bool clearedCurrentSaveEmployees;
        private bool dailyCleanupScheduled;
        private bool saveCleanupAfterLoad;
        private bool missingDemandCounterLogged;
        private Action? scheduleDailyCleanup;

        public void SetDailyCleanupScheduler(Action scheduler)
        {
            scheduleDailyCleanup = scheduler;
            dailyCleanupScheduled = false;
        }

        public void ApplyConfiguredBehavior(ModContext context, BigHaxSettings settings)
        {
            this.context = context;
            isEnabled = settings.RemoveEmployeeDemands;
            if (!isEnabled)
            {
                clearedCurrentSaveEmployees = false;
                dailyCleanupScheduled = false;
                Unsubscribe();
                return;
            }

            Subscribe();
            ClearCurrentSaveEmployeesOnce();
        }

        public void InvalidateCache()
        {
            clearedCurrentSaveEmployees = false;
            dailyCleanupScheduled = false;
        }

        public bool TryScheduleDailyCleanup()
        {
            if (!isEnabled || dailyCleanupScheduled)
                return false;

            dailyCleanupScheduled = true;
            Log("scheduled post-daily demand cleanup.");
            return true;
        }

        public IEnumerator ClearNewDemandsAfterDailyUpdate()
        {
            // The game raises onNewDay before its employee loop generates skill-based demands.
            yield return null;
            dailyCleanupScheduled = false;

            if (!isEnabled)
                yield break;

            var generatedDemandCount = GetDemandsGeneratedToday();
            Log($"post-daily demand cleanup evaluated; vanilla generated {generatedDemandCount} demand(s).");
            if (generatedDemandCount <= 0)
                yield break;

            try
            {
                var employees = SaveGameManager.Current?.EmployeeInstances;
                if (employees == null)
                    yield break;

                var clearedEmployeeCount = 0;
                var removedMessageCount = 0;
                foreach (var employee in employees)
                {
                    if (ClearDemands(employee) > 0)
                    {
                        clearedEmployeeCount++;
                        removedMessageCount += RemoveDemandMessages(employee);
                    }
                }

                MarkSaveChangedIfNeeded(clearedEmployeeCount > 0 || removedMessageCount > 0);
                Log($"vanilla generated {generatedDemandCount} demand(s); cleared demands from {clearedEmployeeCount} employee(s) and removed {removedMessageCount} demand message(s).");
            }
            catch (Exception exception)
            {
                Log($"failed to clear skill-based demands: {exception}");
                context?.Logger.Error(exception);
            }
        }

        public IEnumerator RemoveSavedDemandMessagesAfterLoad()
        {
            // Contact queues can finish restoring after the normal runtime initialization pass.
            yield return null;

            if (!isEnabled)
                yield break;

            try
            {
                var employees = SaveGameManager.Current?.EmployeeInstances;
                if (employees == null)
                    yield break;

                var removedMessageCount = 0;
                foreach (var employee in employees)
                    removedMessageCount += RemoveDemandMessages(employee);

                MarkSaveChangedIfNeeded(removedMessageCount > 0);
                SaveDemandCleanupAfterLoadIfNeeded();
                Log($"post-load cleanup removed {removedMessageCount} saved demand message(s) from {employees.Count} employee(s).");
            }
            catch (Exception exception)
            {
                Log($"failed to remove saved demand messages: {exception}");
                context?.Logger.Error(exception);
            }
        }

        public void Unsubscribe()
        {
            if (!isSubscribed)
                return;

            GameEvent.onGameEventTriggered -= HandleGameEvent;
            isSubscribed = false;
        }

        private void Subscribe()
        {
            if (isSubscribed)
                return;

            GameEvent.onGameEventTriggered += HandleGameEvent;
            isSubscribed = true;
        }

        private void ClearCurrentSaveEmployeesOnce()
        {
            if (clearedCurrentSaveEmployees)
                return;

            try
            {
                var employees = SaveGameManager.Current?.EmployeeInstances;
                if (employees == null)
                    return;

                var removedDemandCount = 0;
                var removedMessageCount = 0;
                foreach (var employee in employees)
                {
                    removedDemandCount += ClearDemands(employee);
                    removedMessageCount += RemoveDemandMessages(employee);
                }

                clearedCurrentSaveEmployees = true;
                MarkSaveChangedIfNeeded(removedDemandCount > 0 || removedMessageCount > 0);
                saveCleanupAfterLoad |= removedDemandCount > 0 || removedMessageCount > 0;
                Log($"cleared {removedDemandCount} demand(s) and removed {removedMessageCount} demand message(s) from {employees.Count} existing employee(s).");
            }
            catch (Exception exception)
            {
                Log($"failed to clear existing employee demands: {exception}");
                context?.Logger.Error(exception);
            }
        }

        private void HandleGameEvent(string eventId)
        {
            if (!isEnabled)
                return;

            if (eventId == NewDayEvent)
            {
                if (TryScheduleDailyCleanup())
                    scheduleDailyCleanup?.Invoke();

                return;
            }

            if (eventId != EmployeeHiredEvent)
                return;

            try
            {
                var employees = SaveGameManager.Current?.EmployeeInstances;
                if (employees == null || employees.Count == 0)
                    return;

                var employee = employees[employees.Count - 1];
                var removedDemandCount = ClearDemands(employee);
                MarkSaveChangedIfNeeded(removedDemandCount > 0);
                Log($"cleared {removedDemandCount} demand(s) from newly hired employee '{employee.characterData?.name ?? employee.id}'.");
            }
            catch (Exception exception)
            {
                Log($"failed to clear newly hired employee demands: {exception}");
                context?.Logger.Error(exception);
            }
        }

        private static int ClearDemands(EmployeeInstance? employee)
        {
            if (employee?.demands == null)
                return 0;

            var removedDemandCount = employee.demands.Count;
            employee.demands.Clear();
            return removedDemandCount;
        }

        private static int RemoveDemandMessages(EmployeeInstance? employee)
        {
            if (employee == null)
                return 0;

            var contact = employee.GetContact(ContactCategoryName.Employees, false);
            if (contact?.messagesQueue == null || contact.messagesQueue.Count == 0)
                return 0;

            var retainedMessages = new System.Collections.Generic.Queue<TextMessage>();
            var removedMessageCount = 0;
            while (contact.messagesQueue.Count > 0)
            {
                var message = contact.messagesQueue.Dequeue();
                if (message.messageKey == NewDemandMessageKey)
                {
                    removedMessageCount++;
                    continue;
                }

                retainedMessages.Enqueue(message);
            }

            while (retainedMessages.Count > 0)
                contact.messagesQueue.Enqueue(retainedMessages.Dequeue());

            if (removedMessageCount > 0 && !contact.HasUnreadMessages)
                RemovePendingContactNotification(contact);

            return removedMessageCount;
        }

        private static void RemovePendingContactNotification(Contact contact)
        {
            if (ContactsToSendNotificationField?.GetValue(null) is not IList pendingContacts)
                return;

            for (var index = pendingContacts.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(pendingContacts[index], contact))
                    pendingContacts.RemoveAt(index);
            }
        }

        private static void MarkSaveChangedIfNeeded(bool changed)
        {
            if (!changed || SaveGameManager.Current == null)
                return;

            SaveGameManager.Current.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
        }

        private void SaveDemandCleanupAfterLoadIfNeeded()
        {
            if (!saveCleanupAfterLoad)
                return;

            saveCleanupAfterLoad = false;
            var saved = SaveGameManager.Save(SaveGameManager.SaveType.Default, null, null);
            Log($"one-time post-load save requested for demand cleanup; accepted={saved}.");
        }

        private int GetDemandsGeneratedToday()
        {
            if (DemandsGeneratedTodayField?.GetValue(null) is int demandCount)
                return demandCount;

            if (!missingDemandCounterLogged)
            {
                missingDemandCounterLogged = true;
                Log("could not read EmployeeHelper.DemandsGeneratedToday; daily cleanup skipped.");
            }

            return 0;
        }

        private static void Log(string message)
        {
            BigHaxFileLogger.Log(DebugLogFile, DebugLogFile, $"[employee demands] {message}");
        }
    }
}
