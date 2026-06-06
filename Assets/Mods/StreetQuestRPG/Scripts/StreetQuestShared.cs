using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Dialogs;
using Entities;
using Helpers;
using Localizor.LanguageChangeEvent;
using UI.Notification;
using UnityEngine;
using UnityEngine.UI;

namespace StreetQuestRPG
{
    [Flags]
    internal enum StreetQuestPhysicalQuestGiverInstallResult
    {
        None = 0,
        RuntimeItem = 1 << 0,
        SpecialService = 1 << 1
    }

    internal static class StreetQuestShared
    {
        private const string QuestStateModDataKey = "streetquest:quest_state_v1";
        private const string SpawnedQuestGiverName = "StreetQuestRPG.OutdoorQuestGiver";
        private const string SellerStandOverlayHeaderKey = HomelessNameKey;
        private const string QuestGiverButtonLabel = "Talk to Mack";
        private static readonly bool UseFixedSpawnPosition = false;
        private static readonly Vector3 FixedSpawnPosition = new(0f, 0f, 0f);
        private static readonly Vector3 FixedForward = new(0f, 0f, -1f);
        private static readonly Vector3 DefaultSpawnOffsetFromPlayer = new(0f, 0f, 4f);
        private static readonly Vector3 SellerPositionLocalOffset = new(0f, 0f, -0.85f);
        private static readonly Vector3 NavTargetLocalOffset = new(0f, 0f, 1.25f);
        private static readonly BindingFlags ReflectionFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<string, int> OriginalDialogTypesByAddress = new();
        private static readonly Dictionary<int, PatchedItemDialogTarget> OriginalDialogTypesByItemTarget = new();
        private static readonly Dictionary<int, PatchedSellerStandButtonTarget> PatchedSellerStandButtons = new();
        private static GameObject SpawnedQuestGiverRoot;
        private static Component SpawnedQuestGiverController;
        private static Vector3? PreferredQuestGiverSpawnPosition;

        public const string HomelessContactId = "streetquest:homeless_contact";
        public const string CourierContactId = "streetquest:courier_contact";
        public const string HomelessNameKey = "streetquest:homeless_name";
        public const string CourierNameKey = "streetquest:courier_name";

        public static readonly Address HomelessAddress = new("ba:street_secondavenue", 6);
        public static readonly string[] ExperimentalItemHostNames =
        {
            "ba:itemname_casinoblackjacktable",
            "ba:itemname_casinoroulettetable",
            "ba:itemname_casinoslotmachine"
        };

        public static StreetQuestPhysicalQuestGiverInstallResult TryInstallPhysicalQuestGiver(CallDialogType dialogType)
        {
            var result = StreetQuestPhysicalQuestGiverInstallResult.None;
            foreach (var itemHostName in ExperimentalItemHostNames)
            {
                if (TryOverrideRuntimeItemDialog(itemHostName, dialogType))
                    result |= StreetQuestPhysicalQuestGiverInstallResult.RuntimeItem;
            }

            if (TryOverrideSpecialServiceDialog(HomelessAddress, dialogType))
                result |= StreetQuestPhysicalQuestGiverInstallResult.SpecialService;

            return result;
        }

        public static void CleanupLegacyContacts()
        {
            try
            {
                SaveGameManager.Current?.Contacts?.RemoveAll(contact =>
                    contact != null && (contact.id == HomelessContactId || contact.id == CourierContactId));

                var notificationsField = typeof(Contact).GetField(
                    "AddedContactNotifications",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (notificationsField?.GetValue(null) is List<Contact> notifications)
                {
                    notifications.RemoveAll(contact =>
                        contact != null && (contact.id == HomelessContactId || contact.id == CourierContactId));
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to clean legacy contacts. {exception}");
            }
        }

        public static void RestorePatchedDialogs()
        {
            PatchedSellerStandButtons.Clear();
            DestroySpawnedOutdoorQuestGiver();

            foreach (var patchedTarget in OriginalDialogTypesByItemTarget.Values.ToList())
            {
                if (patchedTarget.Target == null)
                    continue;

                try
                {
                    SetMemberValue(
                        patchedTarget.Target,
                        patchedTarget.MemberName,
                        (CallDialogType)patchedTarget.OriginalDialogType);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"StreetQuestRPG: Failed to restore item dialog for {patchedTarget.ItemName}. {exception}");
                }
            }

            OriginalDialogTypesByItemTarget.Clear();

            foreach (var originalDialogType in OriginalDialogTypesByAddress.ToList())
            {
                var splitIndex = originalDialogType.Key.LastIndexOf(':');
                if (splitIndex < 0)
                    continue;

                if (!int.TryParse(originalDialogType.Key.Substring(splitIndex + 1), out var streetNumber))
                    continue;

                var streetName = originalDialogType.Key.Substring(0, splitIndex);
                TryOverrideSpecialServiceDialog(
                    new Address(streetName, streetNumber),
                    (CallDialogType)originalDialogType.Value,
                    preserveOriginal: false);
            }

            OriginalDialogTypesByAddress.Clear();
        }

        public static bool EnsureSpawnedOutdoorQuestGiver()
        {
            if (SpawnedQuestGiverRoot != null && SpawnedQuestGiverController != null)
                return true;

            try
            {
                var sellerStandControllerType = FindType("SellerStandController");
                if (sellerStandControllerType == null)
                    return false;

                var spawnPosition = GetQuestGiverSpawnPosition();
                if (!spawnPosition.HasValue)
                    return false;

                var playerController = PlayerHelper.PlayerController;
                var facingForward = UseFixedSpawnPosition
                    ? FlattenDirection(FixedForward)
                    : playerController != null
                        ? FlattenDirection(playerController.transform.forward)
                        : Vector3.forward;
                if (facingForward.sqrMagnitude < 0.001f)
                    facingForward = Vector3.forward;

                var root = new GameObject(SpawnedQuestGiverName);
                root.name = SpawnedQuestGiverName;
                root.transform.position = spawnPosition.Value;
                root.transform.rotation = Quaternion.LookRotation(-facingForward, Vector3.up);
                BuildQuestGiverVisual(root.transform);

                var interactionCollider = root.AddComponent<BoxCollider>();
                interactionCollider.center = new Vector3(0f, 0.95f, 0f);
                interactionCollider.size = new Vector3(1.8f, 1.9f, 1.2f);

                var navTarget = new GameObject("NavMeshTarget").transform;
                navTarget.SetParent(root.transform, false);
                navTarget.localPosition = NavTargetLocalOffset;

                var sellerPosition = new GameObject("SellerPosition").transform;
                sellerPosition.SetParent(root.transform, false);
                sellerPosition.localPosition = SellerPositionLocalOffset;

                var sellerStandController =
                    (Component)root.AddComponent(sellerStandControllerType);

                SetMemberValue(sellerStandController, "primaryInteractionEnabled", true);
                SetMemberValue(sellerStandController, "simpleOverlayType", 4);
                SetMemberValue(sellerStandController, "detailedOverlayType", 1024);
                SetMemberValue(sellerStandController, "customOverlayHeaderKey", SellerStandOverlayHeaderKey);
                SetMemberValue(
                    sellerStandController,
                    "renderers",
                    root.GetComponentsInChildren<Renderer>());
                SetMemberValue(
                    sellerStandController,
                    "navMeshTargets",
                    new[] { navTarget });
                SetMemberValue(
                    sellerStandController,
                    "itemsToSell",
                    new[] { "ba:itemname_hotdog" });
                SetMemberValue(sellerStandController, "sellerPosition", sellerPosition);
                InvokeParameterlessMethod(sellerStandController, "Show");

                SpawnedQuestGiverRoot = root;
                SpawnedQuestGiverController = sellerStandController;
                ShowDebugNotification(
                    $"Quest giver spawned at {FormatVector3(root.transform.position)}",
                    "streetquest-debug-spawn");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to spawn outdoor quest giver. {exception}");
                DestroySpawnedOutdoorQuestGiver();
                return false;
            }
        }

        public static void MoveSpawnedQuestGiverToPlayer()
        {
            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
            {
                Debug.LogWarning("StreetQuestRPG: Could not move quest giver because the player controller is unavailable.");
                return;
            }

            var playerForward = FlattenDirection(playerController.transform.forward);
            if (playerForward.sqrMagnitude < 0.001f)
                playerForward = Vector3.forward;

            var newPosition = playerController.transform.position + playerForward.normalized * DefaultSpawnOffsetFromPlayer.z;
            PreferredQuestGiverSpawnPosition = newPosition;

            if (!EnsureSpawnedOutdoorQuestGiver() || SpawnedQuestGiverRoot == null)
                return;

            SpawnedQuestGiverRoot.transform.position = newPosition;
            SpawnedQuestGiverRoot.transform.rotation = Quaternion.LookRotation(-playerForward.normalized, Vector3.up);
            ShowDebugNotification(
                $"Quest giver moved to {FormatVector3(newPosition)}",
                "streetquest-debug-move");
        }

        public static void LogCoordinateSnapshot()
        {
            var playerController = PlayerHelper.PlayerController;
            var playerPosition = playerController != null
                ? playerController.transform.position
                : PlayerHelper.GetPosition();

            var questGiverPosition = SpawnedQuestGiverRoot != null
                ? SpawnedQuestGiverRoot.transform.position
                : (Vector3?)null;

            ShowDebugNotification(
                $"Player {FormatVector3(playerPosition)}"
                + (questGiverPosition.HasValue
                    ? $" | Quest giver {FormatVector3(questGiverPosition.Value)}"
                    : " | Quest giver not spawned"),
                "streetquest-debug-coords");
        }

        public static bool TryPatchSellerStandOverlayButtons(CallDialogType dialogType)
        {
            var sellerStandOverlayType = FindType("Player.HUD.ItemInfoOverlays.SellerStandOverlay");
            if (sellerStandOverlayType == null)
                return false;

            var activeController = GetActiveDetailedOverlayController();
            if (!IsSpawnedQuestGiverController(activeController))
                return false;

            var patchedAny = false;
            var activeButtonIds = new HashSet<int>();
            foreach (var overlayObject in Resources.FindObjectsOfTypeAll(sellerStandOverlayType))
            {
                if (overlayObject == null)
                    continue;

                var templateTransform = GetMemberValue(overlayObject, "buyButtonTemplate") as Transform;
                var buttonContainer = templateTransform?.parent;
                if (buttonContainer == null)
                    continue;

                foreach (Transform child in buttonContainer)
                {
                    if (child == null || child == templateTransform)
                        continue;

                    var button = child.GetComponent<Button>();
                    if (button == null || !child.gameObject.activeInHierarchy)
                        continue;

                    var buttonId = button.GetInstanceID();
                    activeButtonIds.Add(buttonId);
                    if (PatchedSellerStandButtons.TryGetValue(buttonId, out var patchedButton)
                        && patchedButton.Button == button
                        && patchedButton.DialogType.Equals(dialogType))
                        continue;

                    UpdateSellerStandButtonVisuals(button);
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => TryOpenQuestDialog(dialogType));
                    PatchedSellerStandButtons[buttonId] = new PatchedSellerStandButtonTarget
                    {
                        Button = button,
                        DialogType = dialogType
                    };
                    patchedAny = true;
                }
            }

            foreach (var staleButtonId in PatchedSellerStandButtons.Keys.Where(x => !activeButtonIds.Contains(x)).ToList())
                PatchedSellerStandButtons.Remove(staleButtonId);

            return patchedAny;
        }

        public static StreetQuestQuestDefinition GetCurrentQuest()
        {
            var record = GetQuestStateRecord();
            return StreetQuestQuestCatalog.Get(record.CurrentQuestId);
        }

        public static StreetQuestQuestProgressState GetQuestProgress(string questId)
        {
            var record = GetQuestStateRecord();
            if (record.CompletedQuestIds.Contains(questId))
                return StreetQuestQuestProgressState.Completed;

            return record.CurrentQuestId == questId
                ? record.CurrentQuestState
                : StreetQuestQuestProgressState.NotStarted;
        }

        public static bool HasIntroducedHomelessQuestline()
        {
            var record = GetQuestStateRecord();
            return record.CompletedQuestIds.Count > 0
                   || record.CurrentQuestState != StreetQuestQuestProgressState.NotStarted
                   || record.CurrentQuestId != StreetQuestQuestCatalog.FirstQuest.Id;
        }

        public static bool AcceptQuest(StreetQuestQuestDefinition quest)
        {
            if (quest == null)
                return false;

            var record = GetQuestStateRecord();
            if (record.CurrentQuestId != quest.Id ||
                record.CurrentQuestState != StreetQuestQuestProgressState.NotStarted)
                return false;

            record.CurrentQuestState = StreetQuestQuestProgressState.Active;
            SaveQuestStateRecord(record);
            return true;
        }

        public static bool CanTurnIn(StreetQuestQuestDefinition quest)
        {
            if (quest == null)
                return false;

            return GetPlayerItemAmount(quest.RequiredItemName) >= quest.RequiredAmount;
        }

        public static bool MarkReadyToTurnIn(StreetQuestQuestDefinition quest)
        {
            if (quest == null || !CanTurnIn(quest))
                return false;

            var record = GetQuestStateRecord();
            if (record.CurrentQuestId != quest.Id)
                return false;

            if (record.CurrentQuestState == StreetQuestQuestProgressState.Active)
            {
                record.CurrentQuestState = StreetQuestQuestProgressState.ReadyToTurnIn;
                SaveQuestStateRecord(record);
            }

            return record.CurrentQuestState == StreetQuestQuestProgressState.ReadyToTurnIn;
        }

        public static bool CompleteQuest(StreetQuestQuestDefinition quest)
        {
            if (quest == null || !CanTurnIn(quest) || !TryConsumeQuestItems(quest.RequiredItemName, quest.RequiredAmount))
                return false;

            var record = GetQuestStateRecord();
            if (record.CurrentQuestId != quest.Id)
                return false;

            GrantReward(quest.RewardAmount);
            record.CompletedQuestIds.Add(quest.Id);

            if (string.IsNullOrEmpty(quest.NextQuestId))
            {
                record.CurrentQuestId = string.Empty;
                record.CurrentQuestState = StreetQuestQuestProgressState.Completed;
            }
            else
            {
                record.CurrentQuestId = quest.NextQuestId;
                record.CurrentQuestState = StreetQuestQuestProgressState.NotStarted;
            }

            SaveQuestStateRecord(record);
            return true;
        }

        public static int GetPlayerItemAmount(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return 0;

            var holder = GetPlayerInventoryHolder();
            return holder?.GetAmountByItemName(itemName) ?? 0;
        }

        private static bool TryConsumeQuestItems(string itemName, int amount)
        {
            if (string.IsNullOrEmpty(itemName) || amount <= 0)
                return false;

            var holder = GetPlayerInventoryHolder();
            if (holder == null || holder.GetAmountByItemName(itemName) < amount)
                return false;

            var remainingAmount = amount;
            var cargoInstances = holder.GetCargoInstances();
            if (cargoInstances == null)
                return false;

            foreach (var cargoInstance in cargoInstances.Where(x => x != null && x.itemName == itemName).ToList())
            {
                var amountToRemove = Math.Min(cargoInstance.amount, remainingAmount);
                holder.ReduceFromCargo(cargoInstance, amountToRemove);
                remainingAmount -= amountToRemove;
                if (remainingAmount <= 0)
                    return true;
            }

            return remainingAmount <= 0;
        }

        private static ICargoHolder GetPlayerInventoryHolder()
        {
            return PlayerHelper.GetCurrentCargoHolder();
        }

        private static void GrantReward(int rewardAmount)
        {
            if (rewardAmount <= 0)
                return;

            var transactionData = new Dictionary<string, string>
            {
                { "amount", rewardAmount.ToString() }
            };
            var transactionInfo = new TransactionInfo("streetquest:transaction_reward", transactionData, false);

            if (!GameManager.ChangeMoneySafe(rewardAmount, transactionInfo, showNotification: true))
            {
                var saveGame = SaveGameManager.Current;
                if (saveGame != null)
                    saveGame.Money += rewardAmount;
            }
        }

        private static StreetQuestQuestStateRecord GetQuestStateRecord()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.modData == null)
                return new StreetQuestQuestStateRecord();

            if (!saveGame.modData.TryGetValue(QuestStateModDataKey, out var serializedRecord))
                return new StreetQuestQuestStateRecord();

            var record = StreetQuestQuestStateRecord.Deserialize(serializedRecord);
            if (!string.IsNullOrEmpty(record.CurrentQuestId) &&
                StreetQuestQuestCatalog.Get(record.CurrentQuestId) == null)
                return new StreetQuestQuestStateRecord();

            return record;
        }

        private static void SaveQuestStateRecord(StreetQuestQuestStateRecord record)
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return;

            saveGame.modData ??= new Dictionary<string, string>();
            saveGame.modData[QuestStateModDataKey] = record.Serialize();
        }

        private static bool TryOverrideSpecialServiceDialog(
            Address address,
            CallDialogType dialogType,
            bool preserveOriginal = true)
        {
            if (address == null)
                return false;

            try
            {
                var building = BuildingHelper.GetBuilding(address);
                if (building == null)
                    return false;

                var specialService = GetMemberValue(building, "SpecialService") ?? GetMemberValue(building, "specialService");
                if (specialService == null)
                    return false;

                var currentDialogValue = GetMemberValue(specialService, "dialogType");
                if (currentDialogValue == null)
                    return false;

                if (preserveOriginal)
                {
                    var addressKey = GetAddressKey(address);
                    if (!OriginalDialogTypesByAddress.ContainsKey(addressKey))
                        OriginalDialogTypesByAddress[addressKey] = Convert.ToInt32(currentDialogValue);
                }

                return SetMemberValue(specialService, "dialogType", dialogType);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"StreetQuestRPG: Failed to override special service dialog at {GetAddressKey(address)}. {exception}");
                return false;
            }
        }

        private static bool TryOverrideRuntimeItemDialog(
            string itemName,
            CallDialogType dialogType,
            bool preserveOriginal = true)
        {
            if (string.IsNullOrEmpty(itemName))
                return false;

            var patchedAny = false;
            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null)
                    continue;

                if (!string.Equals(GetMemberValue(behaviour, "itemName") as string, itemName, StringComparison.Ordinal))
                    continue;

                var currentDialogValue = GetMemberValue(behaviour, "callDialogType");
                if (currentDialogValue == null)
                    continue;

                var instanceId = behaviour.GetInstanceID();
                if (preserveOriginal && !OriginalDialogTypesByItemTarget.ContainsKey(instanceId))
                {
                    OriginalDialogTypesByItemTarget[instanceId] = new PatchedItemDialogTarget
                    {
                        ItemName = itemName,
                        MemberName = "callDialogType",
                        OriginalDialogType = Convert.ToInt32(currentDialogValue),
                        Target = behaviour
                    };
                }

                if (SetMemberValue(behaviour, "callDialogType", dialogType))
                    patchedAny = true;
            }

            return patchedAny;
        }

        private static void TryOpenQuestDialog(CallDialogType dialogType)
        {
            try
            {
                var dialogUiType = FindType("UI.Dialog.DialogUI");
                if (dialogUiType == null)
                    throw new InvalidOperationException("StreetQuestRPG: Could not resolve UI.Dialog.DialogUI.");

                var dialogUi = Resources.FindObjectsOfTypeAll(dialogUiType).FirstOrDefault();
                if (dialogUi == null)
                    throw new InvalidOperationException("StreetQuestRPG: Could not find a DialogUI instance.");

                var showDialogMethod = dialogUiType.GetMethod(
                    "ShowDialog",
                    ReflectionFlags,
                    null,
                    new[]
                    {
                        typeof(CallDialogType),
                        FindType("NavigationBlocker"),
                        typeof(Contact),
                        typeof(Action),
                        FindType("ThirdPersonCharacter")
                    },
                    null);

                if (showDialogMethod == null)
                    throw new InvalidOperationException("StreetQuestRPG: Could not resolve DialogUI.ShowDialog.");

                showDialogMethod.Invoke(dialogUi, new object[] { dialogType, null, null, null, null });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to open physical quest dialog. {exception}");
            }
        }

        private static void DestroySpawnedOutdoorQuestGiver()
        {
            if (SpawnedQuestGiverRoot != null)
            {
                UnityEngine.Object.Destroy(SpawnedQuestGiverRoot);
                SpawnedQuestGiverRoot = null;
            }

            SpawnedQuestGiverController = null;
        }

        private static object GetActiveDetailedOverlayController()
        {
            var overlayManagerType = FindType("Player.HUD.ItemInfoOverlays.OverlayManager");
            if (overlayManagerType == null)
                return null;

            var overlayManager = Resources.FindObjectsOfTypeAll(overlayManagerType).FirstOrDefault();
            if (overlayManager == null)
                return null;

            var detailedOverlay = GetMemberValue(overlayManager, "detailedOverlay");
            if (detailedOverlay == null)
                return null;

            return GetMemberValue(detailedOverlay, "linkedController")
                   ?? GetMemberValue(detailedOverlay, "relevantController");
        }

        private static bool IsSpawnedQuestGiverController(object controller)
        {
            if (controller == null || SpawnedQuestGiverController == null)
                return false;

            return ReferenceEquals(controller, SpawnedQuestGiverController)
                   || (controller is UnityEngine.Object unityObject
                       && unityObject.GetInstanceID() == SpawnedQuestGiverController.GetInstanceID());
        }

        private static Vector3? GetQuestGiverSpawnPosition()
        {
            if (UseFixedSpawnPosition)
                return FixedSpawnPosition;

            if (PreferredQuestGiverSpawnPosition.HasValue)
                return PreferredQuestGiverSpawnPosition.Value;

            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return null;

            var playerForward = FlattenDirection(playerController.transform.forward);
            if (playerForward.sqrMagnitude < 0.001f)
                playerForward = Vector3.forward;

            var spawnPosition = playerController.transform.position + playerForward.normalized * DefaultSpawnOffsetFromPlayer.z;
            PreferredQuestGiverSpawnPosition = spawnPosition;
            return spawnPosition;
        }

        private static Vector3 FlattenDirection(Vector3 direction)
        {
            direction.y = 0f;
            return direction.normalized;
        }

        private static string FormatVector3(Vector3 value) =>
            $"({value.x:F2}, {value.y:F2}, {value.z:F2})";

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static string GetAddressKey(Address address) => $"{address.streetName}:{address.streetNumber}";

        private static void BuildQuestGiverVisual(Transform parent)
        {
            var countertop = CreateVisualBlock(
                parent,
                "Countertop",
                new Vector3(0f, 0.9f, 0f),
                new Vector3(1.7f, 0.16f, 0.7f),
                new Color(0.33f, 0.24f, 0.16f));
            AddOutlineAccent(countertop.transform, new Vector3(0f, -0.09f, 0f), new Vector3(1.78f, 0.03f, 0.78f));

            CreateVisualBlock(
                parent,
                "CrateBase",
                new Vector3(0f, 0.38f, 0f),
                new Vector3(1.55f, 0.72f, 0.62f),
                new Color(0.18f, 0.16f, 0.14f));

            CreateVisualBlock(
                parent,
                "SignPostLeft",
                new Vector3(-0.58f, 1.4f, -0.18f),
                new Vector3(0.08f, 1f, 0.08f),
                new Color(0.22f, 0.18f, 0.12f));
            CreateVisualBlock(
                parent,
                "SignPostRight",
                new Vector3(0.58f, 1.4f, -0.18f),
                new Vector3(0.08f, 1f, 0.08f),
                new Color(0.22f, 0.18f, 0.12f));
            CreateVisualBlock(
                parent,
                "SignBoard",
                new Vector3(0f, 1.75f, -0.18f),
                new Vector3(1.28f, 0.5f, 0.08f),
                new Color(0.75f, 0.69f, 0.52f));

            var label = new GameObject("QuestGiverLabel");
            label.transform.SetParent(parent, false);
            label.transform.localPosition = new Vector3(0f, 1.75f, -0.24f);
            var textMesh = label.AddComponent<TextMesh>();
            textMesh.text = "MACK";
            textMesh.fontSize = 72;
            textMesh.characterSize = 0.06f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(0.1f, 0.08f, 0.05f);
        }

        private static void AddOutlineAccent(Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            CreateVisualBlock(
                parent,
                "CounterAccent",
                localPosition,
                localScale,
                new Color(0.88f, 0.76f, 0.34f));
        }

        private static GameObject CreateVisualBlock(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = localPosition;
            block.transform.localScale = localScale;

            var collider = block.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.Destroy(collider);

            var renderer = block.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = CreateRuntimeMaterial(color);
                if (material != null)
                    renderer.sharedMaterial = material;
            }

            return block;
        }

        private static Material CreateRuntimeMaterial(Color color)
        {
            var shader = Shader.Find("Standard")
                         ?? Shader.Find("HDRP/Lit")
                         ?? Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            var material = new Material(shader);
            if (material.HasProperty("_Color"))
                material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            return material;
        }

        private static void InvokeParameterlessMethod(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrEmpty(methodName))
                return;

            for (var instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                var method = instanceType.GetMethod(methodName, ReflectionFlags, null, Type.EmptyTypes, null);
                if (method == null)
                    continue;

                method.Invoke(instance, null);
                return;
            }
        }

        private static void ShowDebugNotification(string message, string duplicateIdentifier)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                Notifications.Show(
                    NotificationType.Info,
                    message,
                    null,
                    6f,
                    duplicateIdentifier,
                    null,
                    false,
                    false);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to show debug notification. {exception}");
                Debug.Log(message);
            }
        }

        private static void UpdateSellerStandButtonVisuals(Button button)
        {
            if (button == null)
                return;

            foreach (var text in button.GetComponentsInChildren<Text>(true))
                text.text = QuestGiverButtonLabel;

            var localizationComponents = button.GetComponentsInChildren<TextLocalizationComponent>(true);
            foreach (var localizationComponent in localizationComponents)
            {
                localizationComponent.enabled = false;
                localizationComponent.gameObject.SetActive(true);
            }

            var tmpTextType = FindType("TMPro.TMP_Text");
            if (tmpTextType == null)
                return;

            foreach (var component in button.GetComponentsInChildren(tmpTextType, true))
            {
                var textProperty = tmpTextType.GetProperty("text", ReflectionFlags);
                textProperty?.SetValue(component, QuestGiverButtonLabel);
            }
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            for (var instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                var property = instanceType.GetProperty(memberName, ReflectionFlags);
                if (property != null)
                    return property.GetValue(instance);

                var field = instanceType.GetField(memberName, ReflectionFlags);
                if (field != null)
                    return field.GetValue(instance);
            }

            return null;
        }

        private static bool SetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return false;

            for (var instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                var property = instanceType.GetProperty(memberName, ReflectionFlags);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(instance, ConvertMemberValue(value, property.PropertyType));
                    return true;
                }

                var field = instanceType.GetField(memberName, ReflectionFlags);
                if (field == null)
                    continue;

                field.SetValue(instance, ConvertMemberValue(value, field.FieldType));
                return true;
            }

            return false;
        }

        private static object ConvertMemberValue(object value, Type targetType)
        {
            if (targetType.IsEnum)
            {
                var intValue = Convert.ToInt32(value);
                return Enum.ToObject(targetType, intValue);
            }

            return Convert.ChangeType(value, targetType);
        }

        private sealed class PatchedItemDialogTarget
        {
            public string ItemName { get; set; } = string.Empty;
            public string MemberName { get; set; } = string.Empty;
            public int OriginalDialogType { get; set; }
            public object Target { get; set; }
        }

        private sealed class PatchedSellerStandButtonTarget
        {
            public Button Button { get; set; }
            public CallDialogType DialogType { get; set; }
        }
    }
}
