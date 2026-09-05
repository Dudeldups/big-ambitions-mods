#nullable enable
using System;
using Localizor;
using UnityEngine;

namespace FreelancePhotographer
{
    internal sealed partial class FreelancePhotographerRuntime
    {
        private Rect boardRect = new Rect(0f, 0f, 680f, 610f);
        private Rect resultRect = new Rect(0f, 0f, 440f, 500f);
        private Vector2 boardScroll;
        private GUIStyle? titleStyle;
        private GUIStyle? headingStyle;
        private GUIStyle? bodyStyle;
        private GUIStyle? centeredStyle;

        private void OnGUI()
        {
            if (SaveGameManager.Current == null)
                return;

            EnsureStyles();
            GUI.depth = -900;

            if (!dependencyReady)
            {
                GUI.Box(new Rect(20f, 20f, 470f, 70f), L("freelancephotographer:dependency_missing"), bodyStyle);
                return;
            }

            GUI.Box(new Rect(16f, Screen.height - 42f, 245f, 28f), L("freelancephotographer:open_jobs_hint"), centeredStyle);

            if (boardOpen)
            {
                CenterRect(ref boardRect);
                boardRect = GUI.Window(913741, boardRect, DrawContractBoard, L("freelancephotographer:contact_name"));
            }

            if (photographyMode)
                DrawPhotographyHud();
            else if (!boardOpen && resultOpen && contracts?.State?.activeContract?.hasCapturedShot == true)
            {
                CenterRect(ref resultRect);
                resultRect = GUI.Window(913742, resultRect, DrawResultWindow,
                    L("freelancephotographer:photo_result"));
            }

            if (!string.IsNullOrWhiteSpace(feedbackKey) && !boardOpen)
            {
                var feedback = L(feedbackKey);
                if (feedbackKey == "freelancephotographer:shot_not_enough_pedestrians")
                    feedback = string.Format(feedback, feedbackActualCount, feedbackRequiredCount);
                GUI.Box(new Rect(Screen.width * 0.5f - 230f, Screen.height - 110f, 460f, 55f), feedback, centeredStyle);
            }
        }

        private void DrawContractBoard(int windowId)
        {
            var state = contracts?.State;
            if (state == null)
            {
                GUILayout.Label(L("freelancephotographer:save_unavailable"), bodyStyle);
                return;
            }

            GUILayout.Space(4f);
            GUILayout.Label(string.Format(L("freelancephotographer:career_summary"),
                RankName(state.Level), state.xp, state.reputation, state.completedContracts, state.lifetimeIncome), bodyStyle);
            GUILayout.Space(8f);
            GUILayout.Label(L("freelancephotographer:contract_active"), headingStyle);

            if (state.activeContract == null)
            {
                GUILayout.Label(L("freelancephotographer:no_active_contract"), bodyStyle);
            }
            else
            {
                DrawContractSummary(state.activeContract, false);
                if (state.activeContract.hasCapturedShot)
                    GUILayout.Label(L("freelancephotographer:captured_ready_to_submit"), bodyStyle);
            }

            GUILayout.Space(10f);
            GUILayout.Label(L("freelancephotographer:contracts_available"), headingStyle);
            boardScroll = GUILayout.BeginScrollView(boardScroll, GUILayout.Height(360f));
            if (state.availableContracts.Count == 0)
            {
                GUILayout.Label(L("freelancephotographer:no_available_contracts"), bodyStyle);
            }
            else
            {
                foreach (var contract in state.availableContracts)
                {
                    if (contract == null)
                        continue;
                    GUILayout.BeginVertical(GUI.skin.box);
                    DrawContractSummary(contract, true);
                    var previousEnabled = GUI.enabled;
                    GUI.enabled = state.activeContract == null;
                    var accepted = GUILayout.Button(L("freelancephotographer:accept_contract"), GUILayout.Height(30f)) &&
                                   contracts?.Accept(contract.id) == true;
                    GUI.enabled = previousEnabled;
                    GUILayout.EndVertical();
                    if (accepted)
                    {
                        feedbackKey = string.Empty;
                        SetBoardOpen(false);
                        break;
                    }
                    GUILayout.Space(6f);
                }
            }
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L("freelancephotographer:close"), GUILayout.Height(32f)))
                SetBoardOpen(false);
            GUI.DragWindow(new Rect(0f, 0f, boardRect.width, 28f));
        }

        private void DrawContractSummary(PhotographyContractInstance contract, bool showAvailability)
        {
            GUILayout.Label(L(contract.titleKey), headingStyle);
            GUILayout.Label(L(contract.descriptionKey), bodyStyle);
            GUILayout.Label(string.Format(L("freelancephotographer:contract_category"), CategoryName(contract.category)), bodyStyle);
            GUILayout.Label(string.Format(L("freelancephotographer:contract_target"), TargetName(contract)), bodyStyle);
            GUILayout.Label(string.Format(L("freelancephotographer:contract_reward"), contract.basePayout), bodyStyle);
            GUILayout.Label(string.Format(L("freelancephotographer:contract_camera_tier"), contract.requiredTier), bodyStyle);
            if (contract.requiredAccessory != PhotographyAccessory.None)
                GUILayout.Label(string.Format(L("freelancephotographer:contract_accessory"), AccessoryName(contract.requiredAccessory)), bodyStyle);

            var deadline = showAvailability ? contract.availableUntil : contract.acceptedUntil;
            GUILayout.Label(string.Format(L("freelancephotographer:contract_deadline"), FormatRemaining(deadline)), bodyStyle);
        }

        private void DrawPhotographyHud()
        {
            var active = contracts?.State?.activeContract;
            if (active == null)
                return;

            var equipment = PhotographyEquipmentService.Capture();
            GUI.Box(new Rect(Screen.width * 0.5f - 250f, 18f, 500f, 105f), GUIContent.none);
            GUI.Label(new Rect(Screen.width * 0.5f - 235f, 28f, 470f, 28f), L(active.titleKey), centeredStyle);
            GUI.Label(new Rect(Screen.width * 0.5f - 235f, 57f, 470f, 24f),
                string.Format(L("freelancephotographer:hud_target"), TargetName(active)), centeredStyle);
            GUI.Label(new Rect(Screen.width * 0.5f - 235f, 82f, 470f, 24f),
                string.Format(L("freelancephotographer:hud_camera"), equipment.CameraTier, equipment.QualityCap), centeredStyle);

            GUI.Label(new Rect(Screen.width * 0.5f - 20f, Screen.height * 0.5f - 22f, 40f, 44f), "+", titleStyle);
            GUI.Box(new Rect(Screen.width * 0.5f - 250f, Screen.height - 50f, 500f, 30f),
                L("freelancephotographer:capture_hint"), centeredStyle);
        }

        private void DrawResultWindow(int windowId)
        {
            var contract = contracts?.State?.activeContract;
            if (contract == null || !contract.hasCapturedShot)
                return;

            GUILayout.Space(6f);
            GUILayout.Label(L(contract.titleKey), titleStyle);
            GUILayout.Label(string.Format(L("freelancephotographer:quality_score"),
                contract.capturedQuality, QualityName(contract.capturedQuality)), centeredStyle);
            GUILayout.Space(8f);
            ScoreRow("freelancephotographer:score_framing", contract.framingScore, 25);
            ScoreRow("freelancephotographer:score_distance", contract.distanceScore, 20);
            ScoreRow("freelancephotographer:score_visibility", contract.visibilityScore, 20);
            ScoreRow("freelancephotographer:score_equipment", contract.equipmentScore, 15);
            ScoreRow("freelancephotographer:score_timing", contract.timingScore, 10);
            ScoreRow("freelancephotographer:score_bonus", contract.bonusScore, 10);
            GUILayout.Space(12f);
            var estimatedPayout = Mathf.RoundToInt(contract.basePayout *
                                                    PhotographyContractService.GetQualityMultiplier(contract.capturedQuality));
            GUILayout.Label(string.Format(L("freelancephotographer:estimated_payout"), estimatedPayout), titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("freelancephotographer:submit"), GUILayout.Height(38f)))
                SubmitCapturedShot();
            if (GUILayout.Button(L("freelancephotographer:retake"), GUILayout.Height(38f)))
                RetakeCapturedShot();
            GUILayout.EndHorizontal();
            GUILayout.Label(L("freelancephotographer:result_hotkeys"), centeredStyle);
            GUI.DragWindow(new Rect(0f, 0f, resultRect.width, 28f));
        }

        private void ScoreRow(string key, int score, int maximum)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(L(key), bodyStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(score + " / " + maximum, bodyStyle, GUILayout.Width(80f));
            GUILayout.EndHorizontal();
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
                return;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            headingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true
            };
            centeredStyle = new GUIStyle(bodyStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };
        }

        private static void CenterRect(ref Rect rect)
        {
            if (rect.x <= 0f && rect.y <= 0f)
            {
                rect.x = Mathf.Max(10f, (Screen.width - rect.width) * 0.5f);
                rect.y = Mathf.Max(10f, (Screen.height - rect.height) * 0.5f);
            }
        }

        private static string L(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;
            return key.Localize().ToString();
        }

        private static string TargetName(PhotographyContractInstance contract)
        {
            if (contract.targetDisplayName.StartsWith("freelancephotographer:", StringComparison.Ordinal))
                return L(contract.targetDisplayName);
            return contract.targetDisplayName;
        }

        private static string CategoryName(PhotographyCategory category)
        {
            return L("freelancephotographer:category_" + category.ToString().ToLowerInvariant());
        }

        private static string AccessoryName(PhotographyAccessory accessory)
        {
            return L("freelancephotographer:accessory_" + accessory.ToString().ToLowerInvariant());
        }

        private static string RankName(int level)
        {
            return L(level >= 3
                ? "freelancephotographer:rank_professional"
                : level == 2
                    ? "freelancephotographer:rank_freelancer"
                    : "freelancephotographer:rank_hobbyist");
        }

        private static string QualityName(int quality)
        {
            return L(quality >= 90
                ? "freelancephotographer:quality_outstanding"
                : quality >= 80
                    ? "freelancephotographer:quality_excellent"
                    : quality >= 65
                        ? "freelancephotographer:quality_good"
                        : quality >= 50
                            ? "freelancephotographer:quality_acceptable"
                            : "freelancephotographer:quality_poor");
        }

        private static string FormatRemaining(double deadline)
        {
            var hours = Math.Max(0d, deadline - PhotographySaveService.CurrentGameHours());
            if (hours >= 24d)
                return string.Format(L("freelancephotographer:remaining_days"), Math.Ceiling(hours / 24d));
            return string.Format(L("freelancephotographer:remaining_hours"), Math.Ceiling(hours));
        }
    }
}
