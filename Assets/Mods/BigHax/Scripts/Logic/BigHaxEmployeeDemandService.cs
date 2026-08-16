#nullable enable
using System;
using BAModAPI;
using Entities;

namespace BigHax
{
    internal sealed class BigHaxEmployeeDemandService
    {
        private const string EmployeeHiredEvent = "ba:gameevent_employeehired";

        private ModContext? context;
        private bool isEnabled;
        private bool isSubscribed;
        private bool clearedCurrentSaveEmployees;

        public void ApplyConfiguredBehavior(ModContext context, BigHaxSettings settings)
        {
            this.context = context;
            isEnabled = settings.RemoveEmployeeDemands;
            if (!isEnabled)
            {
                clearedCurrentSaveEmployees = false;
                Unsubscribe();
                return;
            }

            Subscribe();
            ClearCurrentSaveEmployeesOnce();
        }

        public void InvalidateCache()
        {
            clearedCurrentSaveEmployees = false;
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

                foreach (var employee in employees)
                    ClearDemands(employee);

                clearedCurrentSaveEmployees = true;
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
            }
        }

        private void HandleGameEvent(string eventId)
        {
            if (!isEnabled || eventId != EmployeeHiredEvent)
                return;

            try
            {
                var employees = SaveGameManager.Current?.EmployeeInstances;
                if (employees == null || employees.Count == 0)
                    return;

                var employee = employees[employees.Count - 1];
                ClearDemands(employee);
            }
            catch (Exception exception)
            {
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
    }
}
