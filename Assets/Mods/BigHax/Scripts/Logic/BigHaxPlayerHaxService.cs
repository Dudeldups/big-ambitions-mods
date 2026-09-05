#nullable enable
using System.Reflection;
using Helpers;

namespace BigHax
{
    /// <summary>
    /// Uses the game's own non-persistent energy invincibility switch and its
    /// existing hourly event rather than continuously restoring player stats.
    /// </summary>
    internal sealed class BigHaxPlayerHaxService
    {
        private const string NewHourEvent = "ba:gameevent_newhour";
        private static readonly FieldInfo? EnergyInvincibilityField = typeof(EnergyHelper).GetField(
            "_invincibility",
            BindingFlags.Static | BindingFlags.NonPublic);

        private bool energyInvincibilityEnabledByBigHax;
        private bool happinessDecayDisabled;
        private bool isSubscribed;
        private float? protectedHappiness;

        public void ApplyConfiguredBehavior(BigHaxSettings settings)
        {
            ApplyHungerAndEnergyDecaySetting(settings.DisablePlayerHungerAndEnergyDecay);

            var wasHappinessDecayDisabled = happinessDecayDisabled;
            happinessDecayDisabled = settings.DisablePlayerHappinessDecay;
            if (happinessDecayDisabled)
            {
                if (!wasHappinessDecayDisabled || !protectedHappiness.HasValue)
                    protectedHappiness = SaveGameManager.Current?.Happiness;

                Subscribe();
            }
            else
            {
                protectedHappiness = null;
                Unsubscribe();
            }
        }

        public void Shutdown()
        {
            ApplyHungerAndEnergyDecaySetting(false);
            happinessDecayDisabled = false;
            protectedHappiness = null;
            Unsubscribe();
        }

        private void ApplyHungerAndEnergyDecaySetting(bool disableDecay)
        {
            if (EnergyInvincibilityField == null)
                return;

            if (disableDecay)
            {
                EnergyInvincibilityField.SetValue(null, true);
                energyInvincibilityEnabledByBigHax = true;
            }
            else if (energyInvincibilityEnabledByBigHax)
            {
                EnergyInvincibilityField.SetValue(null, false);
                energyInvincibilityEnabledByBigHax = false;
            }
        }

        private void Subscribe()
        {
            if (isSubscribed)
                return;

            GameEvent.onGameEventTriggered += HandleGameEvent;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
                return;

            GameEvent.onGameEventTriggered -= HandleGameEvent;
            isSubscribed = false;
        }

        private void HandleGameEvent(string eventId)
        {
            if (happinessDecayDisabled && eventId == NewHourEvent)
                PreserveHappinessAfterHourlyUpdate();
        }

        private void PreserveHappinessAfterHourlyUpdate()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return;

            if (!protectedHappiness.HasValue)
            {
                protectedHappiness = saveGame.Happiness;
                return;
            }

            if (saveGame.Happiness >= protectedHappiness.Value)
            {
                protectedHappiness = saveGame.Happiness;
                return;
            }

            saveGame.Happiness = protectedHappiness.Value;
            SaveGameManager.MarkChange();
        }
    }
}
