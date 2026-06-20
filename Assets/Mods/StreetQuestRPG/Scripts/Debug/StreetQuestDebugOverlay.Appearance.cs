using System;
using Helpers;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed partial class StreetQuestDebugOverlay
    {
        private static readonly string[] AppearancePrefabOptions =
        {
            "Characters/Homeless",
            "Characters/Pedestrian",
            "Characters/CasinoCustomer",
            "Characters/CinemaTheaterCustomer",
            "Characters/FullServiceCustomer",
            "Characters/GymCustomer",
            "Characters/HairdresserCustomer",
            "Characters/NightclubCustomer",
            "Characters/NightclubOutsidePedestrian",
            "Characters/SelfServiceCustomer",
            "Characters/StreetPerformer",
            "Characters/CarnivalPedestrian",
            "Characters/WaterPedestrian",
            "Characters/DummyHuman",
            "Characters/DummyAi",
            "Characters/HumanDefinitionLow"
        };

        private static string _appearanceSelectedPrefab = AppearancePrefabOptions[0];
        private static string _appearanceGender = "Female";
        private static string _appearanceAgeInDays = "12410";
        private static string _appearanceSeed = "22073";
        private static string _appearanceScale = "1.00";
        private static GameObject _appearancePreviewRoot;
        private static int _appearancePreviewSerial;

        private static void DrawAppearanceTab()
        {
            GUILayout.Label("Appearance Preview", _headerStyle);
            GUILayout.Label("Spawn a character prefab in front of the player and tweak gender, age, seed, and scale until the NPC looks right.", _textStyle);
            GUILayout.Space(8f);

            GUILayout.Label($"Selected Prefab: {_appearanceSelectedPrefab}", _textStyle);
            _appearancePrefabScroll = GUILayout.BeginScrollView(_appearancePrefabScroll, GUILayout.Height(140f));
            foreach (var prefabName in AppearancePrefabOptions)
            {
                var isSelected = string.Equals(_appearanceSelectedPrefab, prefabName, StringComparison.OrdinalIgnoreCase);
                if (GUILayout.Button(prefabName, isSelected ? _activeTabStyle : _buttonStyle, GUILayout.Height(26f)))
                    _appearanceSelectedPrefab = prefabName;
            }

            GUILayout.EndScrollView();
            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Gender", _textStyle, GUILayout.Width(70f));
            if (GUILayout.Button("Male", string.Equals(_appearanceGender, "Male", StringComparison.OrdinalIgnoreCase) ? _activeTabStyle : _buttonStyle, GUILayout.Width(90f)))
                _appearanceGender = "Male";
            if (GUILayout.Button("Female", string.Equals(_appearanceGender, "Female", StringComparison.OrdinalIgnoreCase) ? _activeTabStyle : _buttonStyle, GUILayout.Width(90f)))
                _appearanceGender = "Female";
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Age Days", _textStyle, GUILayout.Width(70f));
            _appearanceAgeInDays = GUILayout.TextField(_appearanceAgeInDays, GUILayout.Width(120f));
            GUILayout.Label("Seed", _textStyle, GUILayout.Width(40f));
            if (GUILayout.Button("<", _buttonStyle, GUILayout.Width(28f)))
                NudgeAppearanceSeed(-1);
            _appearanceSeed = GUILayout.TextField(_appearanceSeed, GUILayout.Width(120f));
            if (GUILayout.Button(">", _buttonStyle, GUILayout.Width(28f)))
                NudgeAppearanceSeed(1);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale", _textStyle, GUILayout.Width(70f));
            if (GUILayout.Button("<", _buttonStyle, GUILayout.Width(28f)))
                NudgeAppearanceScale(-0.05f);
            _appearanceScale = GUILayout.TextField(_appearanceScale, GUILayout.Width(120f));
            if (GUILayout.Button(">", _buttonStyle, GUILayout.Width(28f)))
                NudgeAppearanceScale(0.05f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn / Refresh", _buttonStyle, GUILayout.Width(130f)))
                SpawnOrRefreshAppearancePreview();
            if (GUILayout.Button("Despawn", _buttonStyle, GUILayout.Width(110f)))
                DestroyAppearancePreview();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Known working prefab candidates from AssetRipper:", _headerStyle);
            GUILayout.Label(
                "Homeless, Pedestrian, CasinoCustomer, CinemaTheaterCustomer, FullServiceCustomer, GymCustomer, HairdresserCustomer, NightclubCustomer, NightclubOutsidePedestrian, SelfServiceCustomer, StreetPerformer, CarnivalPedestrian, WaterPedestrian, DummyHuman, DummyAi, HumanDefinitionLow",
                _textStyle);
        }

        private static void SpawnOrRefreshAppearancePreview()
        {
            DestroyAppearancePreview();

            var player = PlayerHelper.PlayerController;
            if (player == null)
            {
                StreetQuestShared.NotifyInfo("Player not available for preview spawn.", "streetquest:debug_preview_player_missing", 2.5f);
                return;
            }

            var spawnForward = player.transform.forward;
            spawnForward.y = 0f;
            if (spawnForward.sqrMagnitude < 0.001f)
                spawnForward = Vector3.forward;
            else
                spawnForward.Normalize();

            var spawnPosition = player.transform.position + spawnForward * 2.5f;
            _appearancePreviewSerial++;
            var previewId = $"streetquest_debug_preview_{_appearancePreviewSerial}";

            var scale = ParseFloatOrDefault(_appearanceScale, 1f);
            scale = Mathf.Clamp(scale, 0.35f, 2.5f);

            var definition = new StreetQuestCharacterDefinition
            {
                id = previewId,
                displayName = "Preview",
                gameObjectName = "StreetQuestRPG.AppearancePreview",
                visualObjectName = "AppearancePreviewVisual",
                prefabName = _appearanceSelectedPrefab,
                gender = _appearanceGender,
                ageInDays = ParseIntOrDefault(_appearanceAgeInDays, 12410),
                appearanceSeed = ParseIntOrDefault(_appearanceSeed, 22073),
                enabled = true,
                interactable = false,
                useFixedSpawnPosition = true,
                position = StreetQuestVector3Data.From(spawnPosition),
                forward = StreetQuestVector3Data.From(spawnForward),
                localPosition = StreetQuestVector3Data.From(Vector3.zero),
                localEulerAngles = StreetQuestVector3Data.From(new Vector3(0f, 90f, 0f)),
                localScale = StreetQuestVector3Data.From(Vector3.one * scale),
                states = new[]
                {
                    new StreetQuestCharacterStateDefinition
                    {
                        id = "preview_default",
                        overrideEnabled = true,
                        enabled = true,
                        overrideUseFixedSpawnPosition = true,
                        useFixedSpawnPosition = true,
                        position = StreetQuestVector3Data.From(spawnPosition),
                        forward = StreetQuestVector3Data.From(spawnForward)
                    }
                }
            };

            var host = StreetQuestCharacterCreator.CreateHost(definition);
            if (host == null)
            {
                StreetQuestShared.NotifyInfo("Failed to create preview host.", "streetquest:debug_preview_host_failed", 2.5f);
                return;
            }

            if (!StreetQuestCharacterCreator.TryAttachPrefabVisual(host.transform, definition, out _))
            {
                UnityEngine.Object.Destroy(host);
                StreetQuestShared.NotifyInfo($"Failed to spawn prefab: {_appearanceSelectedPrefab}", "streetquest:debug_preview_prefab_failed", 2.5f);
                return;
            }

            _appearancePreviewRoot = host;
            StreetQuestShared.NotifyInfo(
                $"Preview spawned: {_appearanceSelectedPrefab} | {_appearanceGender} | age {definition.ageInDays} | seed {definition.appearanceSeed} | scale {scale:F2}",
                "streetquest:debug_preview_spawned",
                3f);
        }

        private static void DestroyAppearancePreview()
        {
            if (_appearancePreviewRoot == null)
                return;

            UnityEngine.Object.Destroy(_appearancePreviewRoot);
            _appearancePreviewRoot = null;
        }

        private static int ParseIntOrDefault(string value, int fallback)
        {
            return int.TryParse(value, out var parsed) ? parsed : fallback;
        }

        private static float ParseFloatOrDefault(string value, float fallback)
        {
            return float.TryParse(value, out var parsed) ? parsed : fallback;
        }

        private static void NudgeAppearanceSeed(int delta)
        {
            var currentSeed = ParseIntOrDefault(_appearanceSeed, 0);
            currentSeed += delta;
            _appearanceSeed = currentSeed.ToString();

            if (_appearancePreviewRoot != null)
                SpawnOrRefreshAppearancePreview();
        }

        private static void NudgeAppearanceScale(float delta)
        {
            var currentScale = ParseFloatOrDefault(_appearanceScale, 1f);
            currentScale = Mathf.Clamp(currentScale + delta, 0.35f, 2.5f);
            _appearanceScale = currentScale.ToString("0.00");

            if (_appearancePreviewRoot != null)
                SpawnOrRefreshAppearancePreview();
        }
    }
}
