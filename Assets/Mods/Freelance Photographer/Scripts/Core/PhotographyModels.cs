#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreelancePhotographer
{
    internal enum PhotographyCategory
    {
        Location,
        Business,
        Vehicle,
        Street
    }

    internal enum PhotographyAccessory
    {
        None,
        Lens,
        Tripod,
        Flash
    }

    [Serializable]
    internal sealed class PhotographyContractDefinition
    {
        public string id = string.Empty;
        public PhotographyCategory category;
        public string titleKey = string.Empty;
        public string descriptionKey = string.Empty;
        public int minimumLevel = 1;
        public int requiredTier = 1;
        public PhotographyAccessory requiredAccessory;
        public float minimumDistance = 4f;
        public float idealDistanceMinimum = 10f;
        public float idealDistanceMaximum = 20f;
        public float maximumDistance = 35f;
        public int minimumPayout = 300;
        public int maximumPayout = 500;
        public int requiredSubjectCount = 1;
    }

    [Serializable]
    internal sealed class PhotographyContractCatalogData
    {
        public int availableContractCount = 4;
        public int minimumAvailableDays = 1;
        public int maximumAvailableDays = 3;
        public int acceptedContractDays = 4;
        public List<PhotographyContractDefinition> definitions = new();
    }

    [Serializable]
    internal sealed class PhotographyContractInstance
    {
        public string id = string.Empty;
        public string definitionId = string.Empty;
        public PhotographyCategory category;
        public string titleKey = string.Empty;
        public string descriptionKey = string.Empty;
        public int requiredTier = 1;
        public PhotographyAccessory requiredAccessory;
        public float minimumDistance;
        public float idealDistanceMinimum;
        public float idealDistanceMaximum;
        public float maximumDistance;
        public int requiredSubjectCount = 1;
        public int basePayout;
        public double availableUntil;
        public double acceptedUntil;
        public string targetStreet = string.Empty;
        public int targetNumber;
        public string targetDisplayName = string.Empty;
        public bool hasCapturedShot;
        public int capturedQuality;
        public int framingScore;
        public int distanceScore;
        public int visibilityScore;
        public int equipmentScore;
        public int timingScore;
        public int bonusScore;

        public string TargetKey => string.IsNullOrWhiteSpace(targetStreet)
            ? category.ToString()
            : targetStreet + ":" + targetNumber;
    }

    [Serializable]
    internal sealed class PhotographySaveState
    {
        public int version = 1;
        public int xp;
        public int reputation;
        public int completedContracts;
        public int lifetimeIncome;
        public int lastContractRefreshDay = -1;
        public List<PhotographyContractInstance> availableContracts = new();
        public PhotographyContractInstance? activeContract;
        public List<string> recentTargets = new();

        public int Level => xp >= 750 ? 3 : xp >= 250 ? 2 : 1;

        public void Normalize()
        {
            version = 1;
            reputation = Mathf.Clamp(reputation, 0, 100);
            availableContracts ??= new List<PhotographyContractInstance>();
            recentTargets ??= new List<string>();
        }
    }

    internal sealed class PhotographyShotResult
    {
        internal bool IsValid;
        internal string FailureKey = string.Empty;
        internal int ActualSubjectCount;
        internal int RequiredSubjectCount;
        internal string SubjectName = string.Empty;
        internal int Quality;
        internal int Framing;
        internal int Distance;
        internal int Visibility;
        internal int Equipment;
        internal int Timing;
        internal int Bonus;
    }
}
