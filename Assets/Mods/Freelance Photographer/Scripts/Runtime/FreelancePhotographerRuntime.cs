#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Helpers;
using UI.Notification;
using UnityEngine;

namespace FreelancePhotographer
{
    internal sealed partial class FreelancePhotographerRuntime : MonoBehaviour
    {
        private ModContext? context;
        private PhotographyContractService? contracts;
        private object? currentSave;
        private bool dependencyReady;
        private bool dependencyErrorReported;
        private float nextDependencyCheck;
        private float nextContractTick;
        private bool boardOpen;
        private bool photographyMode;
        private bool resultOpen;
        private bool navigationBlocked;
        private NavigationBlocker activeBlocker;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool cursorCaptured;
        private string feedbackKey = string.Empty;
        private int feedbackActualCount;
        private int feedbackRequiredCount;

        internal static FreelancePhotographerRuntime Initialize(ModContext context)
        {
            var runtime = FindObjectOfType<FreelancePhotographerRuntime>();
            if (runtime == null)
            {
                var runtimeObject = new GameObject("FreelancePhotographer.Runtime");
                DontDestroyOnLoad(runtimeObject);
                runtime = runtimeObject.AddComponent<FreelancePhotographerRuntime>();
            }

            runtime.Configure(context);
            return runtime;
        }

        internal void Shutdown()
        {
            RestoreInteractionState();
            Destroy(gameObject);
        }

        private void Configure(ModContext modContext)
        {
            RestoreInteractionState();
            context = modContext;
            contracts = new PhotographyContractService(
                PhotographyContractCatalog.Load(modContext.ModRootPath, modContext.Logger));
            currentSave = null;
            dependencyReady = false;
            dependencyErrorReported = false;
            nextDependencyCheck = 0f;
            nextContractTick = 0f;
            feedbackKey = string.Empty;
        }

        private void Update()
        {
            var save = SaveGameManager.Current;
            if (!ReferenceEquals(currentSave, save))
            {
                RestoreInteractionState();
                PhotographySaveService.ResetCache();
                currentSave = save;
                dependencyErrorReported = false;
                dependencyReady = false;
                nextDependencyCheck = 0f;
                nextContractTick = 0f;
            }

            if (save == null || contracts == null)
                return;

            if (Time.unscaledTime >= nextDependencyCheck)
            {
                nextDependencyCheck = Time.unscaledTime + 2f;
                RefreshDependencyState();
            }

            if (!dependencyReady)
                return;

            if (Time.unscaledTime >= nextContractTick)
            {
                nextContractTick = Time.unscaledTime + 1f;
                contracts.Tick(PhotographyEquipmentService.Capture());
            }

            HandleInput();
        }

        private void RefreshDependencyState()
        {
            var missing = PhotographyEquipmentService.FindMissingCameraStoreItems();
            var wasReady = dependencyReady;
            dependencyReady = missing.Count == 0;
            if (dependencyReady)
            {
                if (!wasReady)
                    context?.Logger.Info("Freelance Photographer: Camera Store item contract resolved; gameplay enabled.");
                return;
            }

            if (wasReady)
                RestoreInteractionState();
            if (dependencyErrorReported)
                return;

            dependencyErrorReported = true;
            var detail = string.Join(", ", missing);
            context?.Logger.Error(
                "Freelance Photographer requires the Camera Store mod. Install and enable Camera Store, then reload the game. " +
                "Missing item IDs: " + detail);
            Notifications.ShowError(
                "freelancephotographer:dependency_missing",
                "freelancephotographer_dependency_missing",
                false);
        }

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (photographyMode)
                    ExitPhotographyMode();
                if (resultOpen)
                    SetResultOpen(false);
                SetBoardOpen(!boardOpen);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (photographyMode)
                    ExitPhotographyMode();
                else if (boardOpen)
                    SetBoardOpen(false);
                else if (resultOpen)
                    SetResultOpen(false);
                return;
            }

            if (boardOpen)
                return;

            var active = contracts?.State?.activeContract;
            if (active?.hasCapturedShot == true)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    SubmitCapturedShot();
                else if (Input.GetKeyDown(KeyCode.R))
                    RetakeCapturedShot();
                else if (Input.GetKeyDown(KeyCode.P))
                    SetResultOpen(true);
                return;
            }

            if (Input.GetKeyDown(KeyCode.P))
            {
                if (photographyMode)
                    ExitPhotographyMode();
                else
                    EnterPhotographyMode();
                return;
            }

            if (photographyMode && Input.GetKeyDown(KeyCode.Space))
                CapturePhotograph();
        }

        private void EnterPhotographyMode()
        {
            feedbackKey = string.Empty;
            var active = contracts?.State?.activeContract;
            if (active == null)
            {
                SetFeedback("freelancephotographer:shot_active_contract_required");
                return;
            }

            if (PlayerHelper.PlayerController == null || PlayerHelper.IsUsingVehicle)
            {
                SetFeedback("freelancephotographer:shot_normal_gameplay_required");
                return;
            }

            var equipment = PhotographyEquipmentService.Capture();
            if (equipment.CameraTier <= 0)
            {
                SetFeedback("freelancephotographer:shot_camera_required");
                return;
            }

            CaptureCursor();
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            SetNavigationBlocker(NavigationBlocker.FreeLookCamera);
            photographyMode = true;
        }

        private void ExitPhotographyMode()
        {
            photographyMode = false;
            ReleaseNavigationBlocker();
            RestoreCursor();
        }

        private void CapturePhotograph()
        {
            var active = contracts?.State?.activeContract;
            if (active == null)
            {
                ExitPhotographyMode();
                return;
            }

            try
            {
                var result = PhotographyShotEvaluator.Evaluate(active, PhotographyEquipmentService.Capture());
                if (!result.IsValid)
                {
                    SetFeedback(result.FailureKey, result.ActualSubjectCount, result.RequiredSubjectCount);
                    return;
                }

                contracts?.RecordCapture(result);
                feedbackKey = string.Empty;
                ExitPhotographyMode();
                SetResultOpen(true);
            }
            catch (Exception exception)
            {
                context?.Logger.Error("Freelance Photographer: capture evaluation failed. " + exception);
                SetFeedback("freelancephotographer:shot_evaluation_failed");
                ExitPhotographyMode();
            }
        }

        private void SubmitCapturedShot()
        {
            var payout = contracts?.Submit() ?? 0;
            if (payout <= 0)
                return;

            SetResultOpen(false);
            Notifications.Show(
                NotificationType.Success,
                "freelancephotographer:contract_paid",
                new Dictionary<string, string> { { "amount", payout.ToString("N0") } },
                4f,
                "freelancephotographer_contract_paid",
                null,
                true,
                false);
            feedbackKey = string.Empty;
        }

        private void RetakeCapturedShot()
        {
            SetResultOpen(false);
            contracts?.Retake();
            EnterPhotographyMode();
        }

        private void SetBoardOpen(bool open)
        {
            if (boardOpen == open)
                return;

            if (open)
            {
                SetResultOpen(false);
                CaptureCursor();
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                SetNavigationBlocker(NavigationBlocker.JobOfferPanel);
            }
            else
            {
                ReleaseNavigationBlocker();
                RestoreCursor();
            }

            boardOpen = open;
        }

        private void SetResultOpen(bool open)
        {
            if (resultOpen == open)
                return;

            if (open)
            {
                if (contracts?.State?.activeContract?.hasCapturedShot != true)
                    return;
                CaptureCursor();
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                SetNavigationBlocker(NavigationBlocker.JobOfferPanel);
            }
            else
            {
                ReleaseNavigationBlocker();
                RestoreCursor();
            }

            resultOpen = open;
        }

        private void SetFeedback(string key, int actual = 0, int required = 0)
        {
            feedbackKey = key;
            feedbackActualCount = actual;
            feedbackRequiredCount = required;
        }

        private void CaptureCursor()
        {
            if (cursorCaptured)
                return;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            cursorCaptured = true;
        }

        private void RestoreCursor()
        {
            if (!cursorCaptured)
                return;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            cursorCaptured = false;
        }

        private void SetNavigationBlocker(NavigationBlocker blocker)
        {
            if (navigationBlocked || PlayerHelper.PlayerController == null)
                return;
            PlayerHelper.PlayerController.SetNavigationBlocker(blocker);
            activeBlocker = blocker;
            navigationBlocked = true;
        }

        private void ReleaseNavigationBlocker()
        {
            if (!navigationBlocked)
                return;
            if (PlayerHelper.PlayerController != null)
                PlayerHelper.PlayerController.UnsetNavigationBlocker(activeBlocker);
            navigationBlocked = false;
        }

        private void RestoreInteractionState()
        {
            boardOpen = false;
            photographyMode = false;
            resultOpen = false;
            ReleaseNavigationBlocker();
            RestoreCursor();
        }

        private void OnDisable()
        {
            RestoreInteractionState();
        }
    }
}
