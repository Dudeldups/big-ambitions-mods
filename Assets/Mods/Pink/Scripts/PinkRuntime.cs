#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pink
{
    internal static class PinkRuntime
    {
        private static readonly string[] VehicleTags = { "Vehicle", "AITrafficCar" };
        private static readonly string[] NpcTags = { "NPC" };

        private static readonly string[] VehicleBehaviourTypeTokens =
        {
            "VehicleController", "CarController", "TrafficCar", "AICar", "AiCar", "ParkedVehicle", "VehicleBehaviour", "ShowcaseVehicle"
        };

        private static readonly string[] NpcBehaviourTypeTokens =
        {
            "Pedestrian", "Npc", "NPC", "Customer", "Employee", "Citizen", "Human", "CharacterController"
        };

        // These are checked against the candidate root object name only, not against the full path.
        // Full paths often contain words like Street/Building even for valid NPCs or parked vehicles.
        private static readonly string[] HardRejectRootNameTokens =
        {
            "citymanager", "city manager", "traffic system", "trafficsystem", "citymap", "roads", "road system",
            "buildingblocks", "buildings", "environment", "terrain", "canvas", "ui"
        };

        private static readonly string[] NpcSkipPathTokens =
        {
            // These are usually stationary vendor/carnival helper humans. They dominated the NPC scan,
            // while the visible walking pedestrians stayed untouched. Skip them so behaviour-based pedestrians
            // get processed first.
            "street vendor", "sm_streetvendor", "hotdogstand", "hot dog", "cottoncandy", "cotton candy",
            "popcorncart", "popcorn cart", "carnival"
        };

        private static readonly string[] NpcFallbackPathDenyTokens =
        {
            "gamemanager/player", "upperfloors", "upper floors", "ground floor", "facade", "roof",
            "building", "basement", "semi-basement", "station", "sign", "watertank", "stairs",
            "window", "garage", "metalbarricade"
        };

        private static readonly string[] NpcFallbackStrictMaterialTokens =
        {
            "m_shirt", "m_hoodie", "m_sweater", "m_suit", "m_polo", "m_croptop", "m_top",
            "m_inside", "m_uniform", "m_jacket", "m_disco", "m_sportshirt", "m_highneck",
            "m_bicolor", "m_openedshirt", "m_doctorgown", "m_sneakers"
        };

        private static readonly string[] VehicleDenyTokens =
        {
            "glass", "window", "windshield", "windscreen", "screen", "wheel", "tire", "tyre", "rim", "interior", "seat", "steering",
            "plate", "license", "licence", "holder", "light", "headlight", "taillight", "tail_light", "brake", "indicator",
            "emissive", "emission", "shadow", "decal", "mirror", "chrome", "cab", "cabin", "drivercab", "driver_cab", "cockpit",
            "damage", "damaged", "dent", "dented", "crash", "scratch", "dirt", "rust", "undercarriage", "underbody"
        };

        private static readonly string[] VehicleAllowTokens =
        {
            // Used only when the candidate root is already known to be a vehicle.
            "body", "paint", "carpaint", "car_paint", "door", "hood", "bonnet", "trunk", "boot", "bumper", "fender", "panel",
            "chassis", "shell", "exterior", "bodywork", "flatbed", "van", "truck", "taxi", "saloon", "sedan", "suv",
            "pickup", "compact", "vehicle"
        };

        private static readonly string[] VehicleFallbackAllowTokens =
        {
            // Used by the global active-renderer fallback. Keep this strict; generic words like door/body/panel/exterior
            // also appear on buildings. Do not use bare "van" here: it matches words like "advanced".
            "carpaint", "car_paint", "carbody", "body_car", "paint_car", "vehiclebody", "body_vehicle",
            "flatbed", "truck", "taxi", "taxicab", "yellowcab", "cabbody", "body_cab", "cab_body", "saloon", "sedan", "suv", "pickup",
            "deliveryvan", "delivery_van", "vanbody", "body_van", "van_body",
            "m_car", "m_vehicle", "m_truck", "m_van", "m_taxi", "m_cab", "m_sedan", "m_suv", "m_pickup", "m_flatbed"
        };

        private static readonly string[] TaxiAllowTokens =
        {
            "taxi", "taxicab", "yellowcab", "yellow cab", "cabbody", "body_cab", "cab_body", "m_cab"
        };

        private static readonly string[] TaxiBroadTokens =
        {
            "taxi", "taxicab", "yellowcab", "yellow cab", "cabbody", "body_cab", "cab_body", "m_cab", "cab"
        };

        private static readonly string[] VehicleDenyTokensWithoutCab =
        {
            "glass", "window", "windshield", "windscreen", "screen", "wheel", "tire", "tyre", "rim", "interior", "seat", "steering",
            "plate", "license", "licence", "holder", "light", "headlight", "taillight", "tail_light", "brake", "indicator",
            "emissive", "emission", "shadow", "decal", "mirror", "chrome",
            "damage", "damaged", "dent", "dented", "crash", "scratch", "dirt", "rust", "undercarriage", "underbody"
        };

        private static readonly string[] NpcAllowTokens =
        {
            "shirt", "tshirt", "t-shirt", "tee", "top", "torso", "upper", "chest", "cloth", "clothes", "clothing",
            "jacket", "hoodie", "sweater", "outfit", "body_top", "upperbody", "upper_body", "suit", "polo", "gown"
        };

        private static readonly string[] NpcDenyTokens =
        {
            "skin", "face", "head", "hair", "eye", "brow", "lash", "mouth", "teeth", "tongue", "hand",
            "arm", "leg", "pants", "trouser", "jean", "short", "sock", "shoe", "foot", "hat", "cap", "glasses",
            "beard", "mustache", "moustache", "nail", "skinbase", "basewithoutsss"
        };

        private static readonly int[] CandidateColorPropertyIds =
        {
            Shader.PropertyToID("_BaseColor"),
            Shader.PropertyToID("_Color"),
            Shader.PropertyToID("_MainColor"),
            Shader.PropertyToID("_TintColor"),
            Shader.PropertyToID("_VehicleColor"),
            Shader.PropertyToID("_PaintColor"),
            Shader.PropertyToID("_PrimaryColor"),
            Shader.PropertyToID("_SecondaryColor"),
            Shader.PropertyToID("_Color1"),
            Shader.PropertyToID("_Color2"),
            Shader.PropertyToID("_AlbedoColor"),
            Shader.PropertyToID("_DiffuseColor")
        };

        private static readonly string[] ShaderColorPropertyDenyTokens =
        {
            "emission", "emissive", "rim", "outline", "shadow", "spec", "highlight", "damage", "dirt"
        };

        private static readonly Dictionary<int, int[]> ShaderColorPropertyIdCache = new Dictionary<int, int[]>();

        private static readonly bool AggressiveNpcClothingTint = true;
        private const bool EnableNpcDiagnosticSamples = false;
        private const int MaxNpcDiagnosticSamples = 80;
        private const int MaxTaxiDiagnosticSamples = 0;
        private const int MaxTrafficLightDiagnosticSamples = 0;
        private const int MaxBlueBinDiagnosticSamples = 0;
        private const int RendererFallbackMaxPasses = 1;

        private static int npcDiagnosticSamples;
        private static int taxiDiagnosticSamples;
        private static int trafficLightDiagnosticSamples;
        private static int blueBinDiagnosticSamples;

        private static readonly Color BasePinkColor = new Color(1f, 0.35f, 0.7f, 1f);

        private static readonly Color[] PinkPalette =
        {
            new Color(0.45f, 0.03f, 0.28f, 1f), // darker 3
            new Color(0.62f, 0.08f, 0.38f, 1f), // darker 2
            new Color(0.82f, 0.18f, 0.52f, 1f), // darker 1
            new Color(1f, 0.35f, 0.7f, 1f),     // base / current bright pink
            new Color(1f, 0.50f, 0.80f, 1f),    // lighter 1
            new Color(1f, 0.65f, 0.88f, 1f),    // lighter 2
            new Color(1f, 0.80f, 0.95f, 1f)     // lighter 3
        };

        private static readonly Color FixedBrightPink = new Color(1f, 0.35f, 0.7f, 1f);
        private static readonly Color FixedDarkPink = new Color(0.62f, 0.08f, 0.38f, 1f);
        private static readonly Color FixedTrashPink = new Color(0.82f, 0.18f, 0.52f, 1f);
        private static readonly Color FixedTrashLightPink = new Color(1f, 0.80f, 0.95f, 1f);

        private static readonly Dictionary<int, MaterialColorSnapshot> PatchedMaterials = new Dictionary<int, MaterialColorSnapshot>();
        private static readonly Dictionary<RendererSlotKey, RendererPropertyBlockSnapshot> PatchedRendererSlots = new Dictionary<RendererSlotKey, RendererPropertyBlockSnapshot>();
        private static readonly HashSet<int> ProcessedVehicleRoots = new HashSet<int>();
        private static readonly HashSet<int> ProcessedNpcRoots = new HashSet<int>();
        private static readonly HashSet<int> ProcessedVehicleRenderers = new HashSet<int>();
        private static readonly HashSet<int> ProcessedNpcRenderers = new HashSet<int>();
        private static readonly HashSet<int> ProcessedWorldPropRenderers = new HashSet<int>();
        private static readonly HashSet<int> SeenCandidateRoots = new HashSet<int>();
        private static readonly HashSet<int> LoggedRejectedRoots = new HashSet<int>();
        private static readonly Dictionary<string, int> BehaviourTypeHitCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static IModLogger? logger;

        internal static int PatchedMaterialCount => PatchedMaterials.Count;
        internal static int PatchedRendererSlotCount => PatchedRendererSlots.Count;
        internal static int ProcessedVehicleRootCount => ProcessedVehicleRoots.Count;
        internal static int ProcessedNpcRootCount => ProcessedNpcRoots.Count;
        internal static int CandidateRootCount => SeenCandidateRoots.Count;
        internal static int ProcessedVehicleRendererCount => ProcessedVehicleRenderers.Count;
        internal static int ProcessedNpcRendererCount => ProcessedNpcRenderers.Count;

        internal static void Initialize(string modId, IModLogger? modLogger, bool enableDebugLogging, bool enableVerbosePatchLogging)
        {
            logger = modLogger;
            PinkFileLogger.Initialize(modId, modLogger, enableDebugLogging, enableVerbosePatchLogging);
            PinkFileLogger.Info(
                "PinkRuntime initialized. Renderer layer scan is disabled; scanning controller/tag candidates only. " +
                "Vehicle and NPC tinting is filtered per active renderer/material slot. Vehicle renderer fallback is enabled with stricter vehicle tokens and dynamic shader color-property discovery and delivery-truck cab filtering and aggressive NPC clothing tinting, damage-material filtering, and street-vendor NPC exclusion and strict active NPC clothing renderer fallback and parked-car-stable 7-tone pink palette plus strict world-prop scan for hydrants, light closed trash bins, medium open trash bins, and darker mailboxes.",
                alsoGameLog: true);
        }

        internal static void Reset()
        {
            PinkFileLogger.Info(
                $"PinkRuntime reset. patchedMaterials={PatchedMaterials.Count} patchedRendererSlots={PatchedRendererSlots.Count} " +
                $"processedVehicles={ProcessedVehicleRoots.Count} processedNpcs={ProcessedNpcRoots.Count} processedVehicleRenderers={ProcessedVehicleRenderers.Count} processedNpcRenderers={ProcessedNpcRenderers.Count} candidates={SeenCandidateRoots.Count}.",
                alsoGameLog: true);

            PatchedMaterials.Clear();
            PatchedRendererSlots.Clear();
            ProcessedVehicleRoots.Clear();
            ProcessedNpcRoots.Clear();
            ProcessedVehicleRenderers.Clear();
            ProcessedNpcRenderers.Clear();
            ProcessedWorldPropRenderers.Clear();
            SeenCandidateRoots.Clear();
            LoggedRejectedRoots.Clear();
            BehaviourTypeHitCounts.Clear();
            ShaderColorPropertyIdCache.Clear();
            npcDiagnosticSamples = 0;
            taxiDiagnosticSamples = 0;
            trafficLightDiagnosticSamples = 0;
            blueBinDiagnosticSamples = 0;
            logger = null;
            PinkFileLogger.Shutdown();
        }

        internal static ScanResult ApplyPinkPass(int passIndex)
        {
            var foundVehicleCandidates = 0;
            var foundNpcCandidates = 0;
            var fallbackVehicleRenderers = 0;
            var vehicleRootPatches = 0;
            var vehicleFallbackPatches = 0;
            var npcPatches = 0;
            var npcRendererFallback = 0;
            var npcFallbackPatches = 0;
            var worldPropCandidates = 0;
            var worldPropPatches = 0;
            var newCandidateCount = 0;
            var newPatchCount = 0;
            var skippedAlreadyProcessed = 0;
            var passStart = Time.realtimeSinceStartup;

            foreach (var root in EnumerateVehicleRoots())
            {
                if (root == null)
                    continue;

                foundVehicleCandidates++;
                var rootId = root.GetInstanceID();
                if (SeenCandidateRoots.Add(rootId))
                    newCandidateCount++;

                if (ProcessedVehicleRoots.Contains(rootId))
                {
                    skippedAlreadyProcessed++;
                    continue;
                }

                ProcessedVehicleRoots.Add(rootId);
                var patched = TintVehicleRoot(root);
                vehicleRootPatches += patched;
                newPatchCount += patched;
            }

            if (passIndex <= RendererFallbackMaxPasses)
            {
                foreach (var renderer in EnumerateVehicleFallbackRenderers())
                {
                    if (renderer == null)
                        continue;

                    fallbackVehicleRenderers++;
                    var rendererId = renderer.GetInstanceID();
                    if (ProcessedVehicleRenderers.Contains(rendererId))
                    {
                        skippedAlreadyProcessed++;
                        continue;
                    }

                    ProcessedVehicleRenderers.Add(rendererId);
                    var patched = TintRendererMaterials(renderer, RootKind.Vehicle, strictVehicleFallback: true);
                    vehicleFallbackPatches += patched;
                    newPatchCount += patched;
                }
            }

            foreach (var root in EnumerateNpcRoots())
            {
                if (root == null)
                    continue;

                foundNpcCandidates++;
                var rootId = root.GetInstanceID();
                if (SeenCandidateRoots.Add(rootId))
                    newCandidateCount++;

                if (ProcessedNpcRoots.Contains(rootId))
                {
                    skippedAlreadyProcessed++;
                    continue;
                }

                ProcessedNpcRoots.Add(rootId);
                var patched = TintNpcRoot(root);
                npcPatches += patched;
                newPatchCount += patched;
            }

            if (passIndex <= RendererFallbackMaxPasses)
            {
                foreach (var renderer in EnumerateNpcFallbackRenderers())
                {
                    if (renderer == null)
                        continue;

                    npcRendererFallback++;
                    var rendererId = renderer.GetInstanceID();
                    if (ProcessedNpcRenderers.Contains(rendererId))
                    {
                        skippedAlreadyProcessed++;
                        continue;
                    }

                    ProcessedNpcRenderers.Add(rendererId);
                    var patched = TintRendererMaterials(renderer, RootKind.Npc);
                    npcFallbackPatches += patched;
                    newPatchCount += patched;
                }
            }

            foreach (var renderer in EnumerateSimpleWorldPropRenderers())
            {
                if (renderer == null)
                    continue;

                worldPropCandidates++;
                var rendererId = renderer.GetInstanceID();
                if (ProcessedWorldPropRenderers.Contains(rendererId))
                {
                    skippedAlreadyProcessed++;
                    continue;
                }

                ProcessedWorldPropRenderers.Add(rendererId);
                var patched = TintSimpleWorldPropRenderer(renderer);
                worldPropPatches += patched;
                newPatchCount += patched;
            }

            var elapsedMs = (Time.realtimeSinceStartup - passStart) * 1000f;
            PinkFileLogger.Info(
                $"Pass {passIndex}: vehicleCandidates={foundVehicleCandidates}, vehicleRendererFallback={fallbackVehicleRenderers}, npcCandidates={foundNpcCandidates}, " +
                $"newCandidates={newCandidateCount}, alreadyProcessed={skippedAlreadyProcessed}, newPatches={newPatchCount}, " +
                $"vehicleRootPatches={vehicleRootPatches}, vehicleFallbackPatches={vehicleFallbackPatches}, npcPatches={npcPatches}, " +
                $"npcRendererFallback={npcRendererFallback}, npcFallbackPatches={npcFallbackPatches}, " +
                $"worldPropCandidates={worldPropCandidates}, worldPropPatches={worldPropPatches}, " +
                $"totalMaterials={PatchedMaterials.Count}, totalRendererSlots={PatchedRendererSlots.Count}, elapsedMs={elapsedMs:0.0}");

            if (passIndex == 1)
                LogBehaviourTypeSummary();

            return new ScanResult(ready: true, foundVehicleCandidates + fallbackVehicleRenderers + foundNpcCandidates + npcRendererFallback, newCandidateCount, newPatchCount);
        }

        internal static void Restore()
        {
            var restoredMaterialProperties = 0;
            foreach (var snapshot in PatchedMaterials.Values)
            {
                if (snapshot.Material == null)
                    continue;

                foreach (var property in snapshot.Properties)
                {
                    if (snapshot.Material.HasProperty(property.PropertyId))
                    {
                        snapshot.Material.SetColor(property.PropertyId, property.OriginalColor);
                        restoredMaterialProperties++;
                    }
                }
            }

            var restoredRendererSlots = 0;
            foreach (var snapshot in PatchedRendererSlots.Values)
            {
                if (snapshot.Renderer == null)
                    continue;

                snapshot.Renderer.SetPropertyBlock(snapshot.OriginalBlock, snapshot.MaterialIndex);
                restoredRendererSlots++;
            }

            PinkFileLogger.Info(
                $"Restore complete. restoredMaterialProperties={restoredMaterialProperties}, restoredRendererSlots={restoredRendererSlots}.",
                alsoGameLog: true);
        }

        private static IEnumerable<GameObject> EnumerateVehicleRoots()
        {
            var seen = new HashSet<int>();

            foreach (var tag in VehicleTags)
            {
                foreach (var root in FindByTagSafe(tag))
                {
                    var safeRoot = ResolveSafeRoot(root, RootKind.Vehicle, "tag:" + tag);
                    if (safeRoot == null || !seen.Add(safeRoot.GetInstanceID()))
                        continue;

                    yield return safeRoot;
                }
            }

            foreach (var root in EnumerateRootsFromBehaviourTypes(RootKind.Vehicle))
            {
                if (root == null || !seen.Add(root.GetInstanceID()))
                    continue;

                yield return root;
            }
        }

        private static IEnumerable<GameObject> EnumerateNpcRoots()
        {
            var seen = new HashSet<int>();

            // Behaviour roots first: this is more likely to catch normal walking pedestrians.
            foreach (var root in EnumerateRootsFromBehaviourTypes(RootKind.Npc))
            {
                if (root == null || !seen.Add(root.GetInstanceID()))
                    continue;

                yield return root;
            }

            // Tag fallback second. This often finds street vendors/carnival helper humans, so it is intentionally lower priority.
            foreach (var tag in NpcTags)
            {
                foreach (var root in FindByTagSafe(tag))
                {
                    var safeRoot = ResolveSafeRoot(root, RootKind.Npc, "tag:" + tag);
                    if (safeRoot == null || !seen.Add(safeRoot.GetInstanceID()))
                        continue;

                    yield return safeRoot;
                }
            }
        }

        private static IEnumerable<GameObject> EnumerateRootsFromBehaviourTypes(RootKind kind)
        {
            MonoBehaviour[] behaviours;
            try
            {
                behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"FindObjectsOfType<MonoBehaviour> failed for {kind}: {ex.GetType().Name}: {ex.Message}");
                yield break;
            }

            for (var index = 0; index < behaviours.Length; index++)
            {
                var behaviour = behaviours[index];
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                var typeName = type.Name;
                var fullName = type.FullName ?? string.Empty;

                var matches = kind == RootKind.Vehicle
                    ? IsVehicleBehaviourType(typeName, fullName)
                    : IsNpcBehaviourType(typeName, fullName);

                if (!matches)
                    continue;

                CountBehaviourHit(typeName);

                var root = ResolveSafeRoot(behaviour.gameObject, kind, "behaviour:" + typeName);
                if (root == null)
                    continue;

                yield return root;
            }
        }

        private static IEnumerable<Renderer> EnumerateVehicleFallbackRenderers()
        {
            Renderer[] renderers;
            try
            {
                renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(false);
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"FindObjectsOfType<Renderer> fallback failed: {ex.GetType().Name}: {ex.Message}");
                yield break;
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (!LooksLikeVehicleRenderer(renderer))
                    continue;

                yield return renderer;
            }
        }

        private static bool LooksLikeVehicleRenderer(Renderer renderer)
        {
            var rendererName = renderer.name ?? string.Empty;
            var parentName = renderer.transform.parent != null ? renderer.transform.parent.name : string.Empty;
            var materialNames = string.Empty;

            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                    continue;

                materialNames += " " + material.name;
            }

            var combined = rendererName + " " + parentName + " " + materialNames;

            if (LooksLikeTaxiRenderer(renderer, combined))
                return true;

            if (ContainsAnyToken(combined, VehicleDenyTokens))
                return false;

            if (!ContainsAnyToken(combined, VehicleFallbackAllowTokens))
                return false;

            // Avoid obvious UI/preview/editor-ish renderers; actual parked/traffic vehicles can still live under Roads/Street paths.
            var rootName = renderer.transform.root != null ? renderer.transform.root.name : string.Empty;
            if (ContainsAnyToken(rootName, new[] { "canvas", "ui", "preview", "thumbnail" }))
                return false;

            return true;
        }

        private static bool IsVehicleBehaviourType(string typeName, string fullName)
        {
            if (typeName.EndsWith("VehicleController", StringComparison.OrdinalIgnoreCase))
                return true;

            if (typeName.EndsWith("CarController", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(typeName, "TrafficCar", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "AITrafficCar", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "AiTrafficCar", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "ParkedVehicle", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static bool IsNpcBehaviourType(string typeName, string fullName)
        {
            if (string.Equals(typeName, "BaseHuman", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "Pedestrian", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "CarnivalPedestrian", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "WaterPedestrian", StringComparison.OrdinalIgnoreCase))
                return true;

            // Runtime customers often have concrete names like HairdresserCustomer or NightclubCustomer.
            // UI/helper classes like CustomerCapacityShelf should not match.
            if (typeName.EndsWith("Customer", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static IEnumerable<Renderer> EnumerateNpcFallbackRenderers()
        {
            Renderer[] renderers;
            try
            {
                renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(false);
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"FindObjectsOfType<Renderer> NPC fallback failed: {ex.GetType().Name}: {ex.Message}");
                yield break;
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (!LooksLikeNpcClothingRenderer(renderer))
                    continue;

                yield return renderer;
            }
        }

        private static bool LooksLikeNpcClothingRenderer(Renderer renderer)
        {
            var path = GetPath(renderer.transform, 10);
            if (ContainsAnyToken(path, NpcSkipPathTokens) || ContainsAnyToken(path, NpcFallbackPathDenyTokens))
                return false;

            var rootName = renderer.transform.root != null ? renderer.transform.root.name : string.Empty;
            if (ContainsAnyToken(rootName, new[] { "canvas", "ui", "preview", "thumbnail" }))
                return false;

            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                    continue;

                var materialName = material.name ?? string.Empty;
                var shaderName = material.shader != null ? material.shader.name : string.Empty;
                var slotText = materialName + " " + shaderName;

                if (ContainsAnyToken(slotText, NpcDenyTokens))
                    continue;

                // The earlier fallback also accepted generic tokens like top/cloth, which matched
                // rooftops, facade cloth materials, and building props. The fallback now requires
                // actual character clothing shaders or known character clothing material prefixes.
                if (shaderName.IndexOf("CharacterClothes", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (ContainsAnyToken(materialName, NpcFallbackStrictMaterialTokens))
                    return true;
            }

            return false;
        }

        private static IEnumerable<Renderer> EnumerateSimpleWorldPropRenderers()
        {
            Renderer[] renderers;
            try
            {
                renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(false);
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"FindObjectsOfType<Renderer> simple world prop scan failed: {ex.GetType().Name}: {ex.Message}");
                yield break;
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (GetSimpleWorldPropColor(renderer) == null)
                    continue;

                yield return renderer;
            }
        }

        private static int TintSimpleWorldPropRenderer(Renderer renderer)
        {
            var color = GetSimpleWorldPropColor(renderer);
            if (color == null)
                return 0;

            Material[] materials;
            try
            {
                materials = renderer.materials;
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"renderer.materials failed for simple world prop: renderer={GetPath(renderer.transform, 6)}, error={ex.GetType().Name}: {ex.Message}");
                return 0;
            }

            var patched = 0;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null || ShouldSkipSimpleWorldPropSlot(renderer, material))
                    continue;

                var changedMaterial = TryTintMaterial(material, color.Value);
                var changedBlock = TryTintRendererPropertyBlock(renderer, index, material, color.Value);
                if (changedMaterial || changedBlock)
                    patched++;
            }

            return patched;
        }

        private static Color? GetSimpleWorldPropColor(Renderer renderer)
        {
            var text = GetRendererParentAndSharedMaterialNameText(renderer);

            if (ContainsAnyToken(text, new[] { "hydrant", "firehydrant", "fire_hydrant", "fire hydrant" }))
                return FixedBrightPink;

            if (IsTrashCanText(text))
                return IsClosedTrashBinText(text) ? FixedTrashLightPink : FixedTrashPink;

            if (LooksLikeClosedBlueTrashBin(renderer, text))
                return FixedTrashLightPink;

            if (IsMailboxText(text))
                return FixedDarkPink;

            return null;
        }

        private static bool IsTrashCanText(string text)
        {
            if (ContainsAnyToken(text, new[]
                {
                    "trashcan", "trash_can", "trash can",
                    "trashbin", "trash_bin", "trash bin",
                    "garbagecan", "garbage_can", "garbage can",
                    "garbagebin", "garbage_bin", "garbage bin",
                    "wastecan", "waste_can", "waste can",
                    "wastebin", "waste_bin", "waste bin",
                    "recyclebin", "recycle_bin", "recycle bin",
                    "recyclingbin", "recycling_bin", "recycling bin",
                    "dumpster", "dustbin", "dust_bin", "dust bin",
                    "wheeliebin", "wheelie_bin", "wheelie bin",
                    "wheelybin", "wheely_bin", "wheely bin",
                    "wheelbin", "wheel_bin", "wheel bin",
                    "bluebin", "blue_bin", "blue bin",
                    "trashcontainer", "trash_container", "trash container",
                    "garbagecontainer", "garbage_container", "garbage container",
                    "wastecontainer", "waste_container", "waste container",
                    "refusecontainer", "refuse_container", "refuse container"
                }))
            {
                return true;
            }

            // Fallback for closed street bins that are named generically but have blue/recycle/trash hints on material or parent.
            return ContainsAnyToken(text, new[] { "bin", "container" }) &&
                   ContainsAnyToken(text, new[] { "trash", "garbage", "waste", "recycle", "recycling", "refuse", "sanitation", "wheelie", "wheely", "blue" });
        }

        private static bool IsClosedTrashBinText(string text)
        {
            return ContainsAnyToken(text, new[]
                {
                    "sortingbin", "sorting_bin", "sorting bin",
                    "wheeliebin", "wheelie_bin", "wheelie bin",
                    "wheelybin", "wheely_bin", "wheely bin",
                    "bluebin", "blue_bin", "blue bin",
                    "trashcontainer", "trash_container", "trash container",
                    "garbagecontainer", "garbage_container", "garbage container",
                    "wastecontainer", "waste_container", "waste container",
                    "refusecontainer", "refuse_container", "refuse container"
                });
        }

        private static bool LooksLikeClosedBlueTrashBin(Renderer renderer, string text)
        {
            if (ContainsAnyToken(text, new[]
                {
                    "vehicle", "car", "taxi", "truck", "traffic", "pedestrian", "human", "npc", "customer", "employee",
                    "shirt", "pants", "hair", "skin", "building", "facade", "window", "shop", "store", "sign", "billboard",
                    "streetlight", "trafficlight", "lamp", "mailbox", "postbox",
                    "carnival", "ferriswheel", "ferris wheel", "cabin", "ticketbooth", "ticket booth", "booth"
                }))
            {
                return false;
            }

            var size = renderer.bounds.size;
            var maxDimension = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (maxDimension > 4.0f)
                return false;

            var blueInfo = GetBlueMaterialInfo(renderer);
            if (blueInfo == null)
                return false;

            if (ContainsAnyToken(text, new[]
                {
                    "bin", "container", "wheelie", "wheely", "trash", "garbage", "waste", "recycle", "recycling", "refuse", "dustbin", "barrel"
                }))
            {
                LogBlueBinDiagnosticSample(renderer, blueInfo, "accepted");
                return true;
            }

            LogBlueBinDiagnosticSample(renderer, blueInfo, "blue-small-skipped-no-bin-token");
            return false;
        }

        private static string? GetBlueMaterialInfo(Renderer renderer)
        {
            Material[] materials;
            try
            {
                materials = renderer.sharedMaterials;
            }
            catch
            {
                return null;
            }

            for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                var material = materials[materialIndex];
                if (material == null)
                    continue;

                var materialName = material.name ?? string.Empty;
                if (ContainsAnyToken(materialName, new[] { "blue", "bin", "trash", "garbage", "waste", "recycle", "refuse" }))
                    return $"material={materialName}";

                for (var propertyIndex = 0; propertyIndex < CandidateColorPropertyIds.Length; propertyIndex++)
                {
                    var propertyId = CandidateColorPropertyIds[propertyIndex];
                    if (!material.HasProperty(propertyId))
                        continue;

                    try
                    {
                        var color = material.GetColor(propertyId);
                        if (IsLikelyBinBlue(color))
                            return $"material={materialName}, color={color}";
                    }
                    catch
                    {
                        // Ignore unsupported reads.
                    }
                }
            }

            return null;
        }

        private static bool IsLikelyBinBlue(Color color)
        {
            return color.b >= 0.35f && color.b > color.r * 1.25f && color.b > color.g * 1.05f;
        }

        private static void LogBlueBinDiagnosticSample(Renderer renderer, string info, string reason)
        {
            if (blueBinDiagnosticSamples >= MaxBlueBinDiagnosticSamples)
                return;

            blueBinDiagnosticSamples++;
            var size = renderer.bounds.size;
            PinkFileLogger.Info(
                $"BLUE_BIN_DIAG {blueBinDiagnosticSamples}/{MaxBlueBinDiagnosticSamples}: reason={reason}, " +
                $"path={GetPath(renderer.transform, 8)}, renderer={renderer.name}, parent={(renderer.transform.parent == null ? "<null>" : renderer.transform.parent.name)}, " +
                $"size={size}, {info}");
        }

        private static bool IsMailboxText(string text)
        {
            return ContainsAnyToken(text, new[]
                {
                    "mailbox", "mail_box", "mail box",
                    "postbox", "post_box", "post box",
                    "postalbox", "postal_box", "postal box",
                    "maildrop", "mail_drop", "mail drop"
                });
        }

        private static string GetRendererAndParentNameText(Renderer renderer)
        {
            var text = renderer.name ?? string.Empty;

            var parent = renderer.transform.parent;
            if (parent != null)
                text += " " + parent.name;

            return text;
        }

        private static string GetRendererParentAndSharedMaterialNameText(Renderer renderer)
        {
            var text = GetRendererAndParentNameText(renderer);

            try
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null)
                        text += " " + material.name;
                }
            }
            catch
            {
                // Ignore material-name diagnostics for world-prop detection.
            }

            return text;
        }

        private static bool ShouldSkipSimpleWorldPropSlot(Renderer renderer, Material material)
        {
            var rendererName = renderer.name ?? string.Empty;
            var parentName = renderer.transform.parent != null ? renderer.transform.parent.name : string.Empty;
            var materialName = material.name ?? string.Empty;
            var text = rendererName + " " + parentName + " " + materialName;
            var propText = GetRendererParentAndSharedMaterialNameText(renderer);

            if (IsTrashCanText(propText) && ContainsAnyToken(text, new[]
                {
                    "lid", "cover", "top", "cap", "handle", "hinge", "wheel", "tire", "tyre",
                    "rubber", "glass", "window", "label", "sticker", "decal"
                }))
            {
                return true;
            }

            if (IsMailboxText(propText) && ContainsAnyToken(text, new[]
                {
                    "handle", "hinge", "slot", "flag", "label", "sticker", "decal",
                    "glass", "window"
                }))
            {
                return true;
            }

            // Generic safety: do not tint obvious glass/light/wheel/decal slots on world props.
            if (ContainsAnyToken(text, new[]
                {
                    "glass", "window", "wheel", "tire", "tyre", "rim", "plate", "license",
                    "bulb", "emission", "emissive", "glow", "lens",
                    "red", "green", "yellow", "signal", "walk", "dontwalk", "don't walk"
                }))
            {
                return true;
            }

            return false;
        }

        private static IEnumerable<GameObject> FindByTagSafe(string tag)
        {
            try
            {
                return GameObject.FindGameObjectsWithTag(tag);
            }
            catch (UnityException)
            {
                PinkFileLogger.Verbose($"Tag not present in this scene/build: {tag}");
                return Array.Empty<GameObject>();
            }
        }

        private static GameObject? ResolveSafeRoot(GameObject source, RootKind kind, string sourceReason)
        {
            if (source == null)
                return null;

            var current = source.transform;
            var best = current;

            while (current.parent != null)
            {
                var parent = current.parent;
                var parentName = parent.name ?? string.Empty;

                if (ContainsAnyToken(parentName, HardRejectRootNameTokens))
                    break;

                if (kind == RootKind.Vehicle && LooksLikeVehicleRoot(parentName))
                    best = parent;
                else if (kind == RootKind.Npc && LooksLikeNpcRoot(parentName))
                    best = parent;

                current = parent;
            }

            var rootObject = best.gameObject;
            if (IsRejectedRoot(rootObject, kind, sourceReason))
                return null;

            return rootObject;
        }

        private static bool IsRejectedRoot(GameObject root, RootKind kind, string sourceReason)
        {
            var rootName = root.name ?? string.Empty;
            if (ContainsAnyToken(rootName, HardRejectRootNameTokens))
            {
                LogRejectedRoot(root, kind, sourceReason, "hardRejectRootName");
                return true;
            }

            if (kind == RootKind.Npc)
            {
                var path = GetPath(root.transform, 10);
                if (ContainsAnyToken(path, NpcSkipPathTokens))
                {
                    LogRejectedRoot(root, kind, sourceReason, "npcVendorOrCarnivalPath");
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeVehicleRoot(string value)
        {
            return ContainsAnyToken(value, new[] { "vehicle", "car", "truck", "van", "taxi", "bus", "traffic", "flatbed" });
        }

        private static bool LooksLikeNpcRoot(string value)
        {
            return ContainsAnyToken(value, new[] { "npc", "pedestrian", "customer", "employee", "citizen", "person", "human", "character" });
        }

        private static void LogRejectedRoot(GameObject root, RootKind kind, string sourceReason, string rejectReason)
        {
            var id = root.GetInstanceID();
            if (!LoggedRejectedRoots.Add(id))
                return;

            PinkFileLogger.Verbose($"Rejected {kind} root: reason={rejectReason}, source={sourceReason}, path={GetPath(root.transform, 8)}");
        }

        private static int TintVehicleRoot(GameObject root)
        {
            var patched = 0;
            var totalRenderers = 0;
            var skippedRenderers = 0;
            var renderers = root.GetComponentsInChildren<Renderer>(false);

            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                totalRenderers++;
                if (ShouldSkipWholeVehicleRenderer(renderer))
                {
                    skippedRenderers++;
                    continue;
                }

                patched += TintRendererMaterials(renderer, RootKind.Vehicle);
            }

            PinkFileLogger.Verbose(
                $"Vehicle root processed: path={GetPath(root.transform, 6)}, renderers={totalRenderers}, skippedRenderers={skippedRenderers}, patchedSlots={patched}");

            return patched;
        }

        private static int TintNpcRoot(GameObject root)
        {
            var patched = 0;
            var totalRenderers = 0;
            var renderers = root.GetComponentsInChildren<Renderer>(false);

            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                totalRenderers++;
                patched += TintRendererMaterials(renderer, RootKind.Npc);
            }

            PinkFileLogger.Verbose(
                $"NPC root processed: path={GetPath(root.transform, 6)}, renderers={totalRenderers}, patchedSlots={patched}");

            return patched;
        }

        private static int TintRendererMaterials(Renderer renderer, RootKind kind, bool strictVehicleFallback = false)
        {
            var patched = 0;
            Material[] materials;

            try
            {
                materials = renderer.materials;
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"renderer.materials failed: kind={kind}, renderer={GetPath(renderer.transform, 6)}, error={ex.GetType().Name}: {ex.Message}");
                return 0;
            }

            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;

                if (kind == RootKind.Vehicle && !ShouldTintVehicleMaterialSlot(renderer, material, index, materials.Length, strictVehicleFallback))
                    continue;

                if (kind == RootKind.Npc && !ShouldTintNpcMaterialSlot(renderer, material, index))
                    continue;

                var pinkColor = GetPinkColorFor(renderer, material, index, kind, strictVehicleFallback);
                var changedMaterial = TryTintMaterial(material, pinkColor);
                var changedBlock = TryTintRendererPropertyBlock(renderer, index, material, pinkColor);

                if (!changedMaterial && !changedBlock)
                    continue;

                if (kind == RootKind.Npc)
                    LogNpcDiagnosticSample(renderer, material, index, changedMaterial, changedBlock);

                patched++;
                PinkFileLogger.Verbose(
                    $"Patched {kind} material slot: renderer={renderer.name}, slot={index}, material={material.name}, " +
                    $"materialColor={(changedMaterial ? "yes" : "no")}, propertyBlock={(changedBlock ? "yes" : "no")}");
            }

            return patched;
        }

        private static bool LooksLikeTaxiRenderer(Renderer renderer, string combined)
        {
            if (ContainsAnyToken(combined, new[] { "delivery", "truck", "flatbed", "freight", "vanbody", "body_van", "van_body" }))
                return false;

            if (ContainsAnyToken(combined, TaxiAllowTokens))
            {
                LogTaxiDiagnosticSample(renderer, null, combined, "renderer-token");
                return true;
            }

            if (ContainsAnyToken(combined, new[] { "cab" }) && HasLikelyYellowPaint(renderer))
            {
                LogTaxiDiagnosticSample(renderer, null, combined, "yellow-cab-renderer");
                return true;
            }


            return false;
        }

        private static bool IsLikelyTaxiSlot(Renderer renderer, Material material, string slotText)
        {
            if (ContainsAnyToken(slotText, new[] { "delivery", "truck", "flatbed", "freight", "vanbody", "body_van", "van_body" }))
                return false;

            if (ContainsAnyToken(slotText, TaxiAllowTokens))
                return true;

            if (ContainsAnyToken(slotText, new[] { "cab" }) && HasLikelyYellowPaint(material))
                return true;

            if (HasLikelyYellowPaint(material) && ContainsAnyToken(slotText, new[] { "car", "vehicle", "traffic", "saloon", "sedan" }))
                return true;

            return false;
        }

        private static bool HasLikelyYellowPaint(Renderer renderer)
        {
            Material[] materials;
            try
            {
                materials = renderer.sharedMaterials;
            }
            catch
            {
                return false;
            }

            for (var index = 0; index < materials.Length; index++)
            {
                if (HasLikelyYellowPaint(materials[index]))
                    return true;
            }

            return false;
        }

        private static bool HasLikelyYellowPaint(Material? material)
        {
            if (material == null)
                return false;

            var materialName = material.name ?? string.Empty;
            if (ContainsAnyToken(materialName, new[] { "yellow", "taxi", "taxicab", "yellowcab" }))
                return true;

            for (var index = 0; index < CandidateColorPropertyIds.Length; index++)
            {
                var propertyId = CandidateColorPropertyIds[index];
                if (!material.HasProperty(propertyId))
                    continue;

                try
                {
                    if (IsLikelyTaxiYellow(material.GetColor(propertyId)))
                        return true;
                }
                catch
                {
                    // Ignore unsupported color reads.
                }
            }

            return false;
        }

        private static bool IsLikelyTaxiYellow(Color color)
        {
            return color.r >= 0.65f && color.g >= 0.45f && color.b <= 0.35f && color.r >= color.g * 0.85f;
        }

        private static void LogTaxiDiagnosticSample(Renderer renderer, Material? material, string text, string reason)
        {
            if (taxiDiagnosticSamples >= MaxTaxiDiagnosticSamples)
                return;

            taxiDiagnosticSamples++;
            PinkFileLogger.Info(
                $"TAXI_DIAG {taxiDiagnosticSamples}/{MaxTaxiDiagnosticSamples}: reason={reason}, " +
                $"rendererPath={GetPath(renderer.transform, 8)}, renderer={renderer.name}, " +
                $"material={(material == null ? "<shared-scan>" : material.name)}, text={text}");
        }

        private static void LogTrafficLightDiagnosticSample(Renderer renderer, Material material, string text)
        {
            if (trafficLightDiagnosticSamples >= MaxTrafficLightDiagnosticSamples)
                return;

            trafficLightDiagnosticSamples++;
            PinkFileLogger.Info(
                $"TRAFFICLIGHT_DIAG {trafficLightDiagnosticSamples}/{MaxTrafficLightDiagnosticSamples}: " +
                $"rendererPath={GetPath(renderer.transform, 8)}, renderer={renderer.name}, parent={(renderer.transform.parent == null ? "<null>" : renderer.transform.parent.name)}, " +
                $"material={material.name}, text={text}");
        }

        private static bool ShouldSkipWholeVehicleRenderer(Renderer renderer)
        {
            var name = renderer.name;
            if (ContainsAnyToken(name, VehicleDenyTokens))
                return true;

            return false;
        }

        private static bool ShouldTintVehicleMaterialSlot(Renderer renderer, Material material, int materialIndex, int materialCount, bool strictVehicleFallback)
        {
            var rendererName = renderer.name ?? string.Empty;
            var parentName = renderer.transform.parent != null ? renderer.transform.parent.name : string.Empty;
            var materialName = material.name ?? string.Empty;
            var slotText = rendererName + " " + parentName + " " + materialName;

            if (IsLikelyTaxiSlot(renderer, material, slotText))
            {
                LogTaxiDiagnosticSample(renderer, material, slotText, "tint");

                // Taxis are often named YellowCab/TaxiCab/CabBody or only expose a yellow cab material.
                // The normal cab/cabin deny exists for delivery trucks, but would also block taxi bodies.
                if (ContainsAnyToken(slotText, VehicleDenyTokensWithoutCab))
                    return false;

                return true;
            }

            if (ContainsAnyToken(slotText, VehicleDenyTokens))
                return false;

            if (strictVehicleFallback)
            {
                // For global renderer fallback, do not accept generic body/door/panel/exterior names.
                // Those are common on buildings. Require a vehicle-specific renderer/material name.
                return ContainsAnyToken(slotText, VehicleFallbackAllowTokens);
            }

            if (ContainsAnyToken(slotText, VehicleAllowTokens))
                return true;

            // If a vehicle renderer has several material slots and the slot name tells us nothing,
            // do not tint it. Multi-slot renderers often contain glass/lights/plates mixed with body material.
            if (materialCount > 1)
            {
                PinkFileLogger.Verbose(
                    $"Skipped unknown vehicle multi-material slot: renderer={rendererName}, slot={materialIndex}, material={materialName}");
                return false;
            }

            // Single-material vehicle renderers are usually body/exterior meshes. This keeps Flatbed/M_Flatbed working.
            return true;
        }

        private static bool ShouldTintNpcMaterialSlot(Renderer renderer, Material material, int materialIndex)
        {
            var rendererName = renderer.name ?? string.Empty;
            var parentName = renderer.transform.parent != null ? renderer.transform.parent.name : string.Empty;
            var materialName = material.name ?? string.Empty;
            var slotText = rendererName + " " + parentName + " " + materialName;

            // Important: material deny must win over renderer allow. Shirt renderers often include a skin material slot.
            if (ContainsAnyToken(materialName, NpcDenyTokens))
                return false;

            if (ContainsAnyToken(slotText, NpcDenyTokens))
                return false;

            if (ContainsAnyToken(slotText, NpcAllowTokens))
                return true;

            if (AggressiveNpcClothingTint)
            {
                // Once we are inside a confirmed NPC root, most non-skin active renderer slots are clothing/accessories.
                // The old allow-list was too conservative and only patched a few visible pedestrians.
                PinkFileLogger.Verbose(
                    $"Aggressively accepted NPC slot: renderer={rendererName}, slot={materialIndex}, material={materialName}");
                return true;
            }

            PinkFileLogger.Verbose(
                $"Skipped unknown NPC slot: renderer={rendererName}, slot={materialIndex}, material={materialName}");
            return false;
        }

        private static void LogNpcDiagnosticSample(Renderer renderer, Material material, int materialIndex, bool changedMaterial, bool changedBlock)
        {
            if (!EnableNpcDiagnosticSamples || npcDiagnosticSamples >= MaxNpcDiagnosticSamples)
                return;

            npcDiagnosticSamples++;
            PinkFileLogger.Info(
                $"NPC_DIAG patchedSlot={npcDiagnosticSamples}/{MaxNpcDiagnosticSamples}, " +
                $"rendererPath={GetPath(renderer.transform, 8)}, renderer={renderer.name}, slot={materialIndex}, " +
                $"material={material.name}, shader={(material.shader != null ? material.shader.name : "<null>")}, " +
                $"properties={GetSupportedColorPropertyNamesForLog(material)}, materialColor={(changedMaterial ? "yes" : "no")}, propertyBlock={(changedBlock ? "yes" : "no")}");
        }

        private static string GetSupportedColorPropertyNamesForLog(Material material)
        {
            var names = new List<string>();

            try
            {
                var shader = material.shader;
                if (shader != null)
                {
                    var count = shader.GetPropertyCount();
                    for (var index = 0; index < count; index++)
                    {
                        if (shader.GetPropertyType(index) != ShaderPropertyType.Color)
                            continue;

                        var propertyName = shader.GetPropertyName(index);
                        if (ContainsAnyToken(propertyName, ShaderColorPropertyDenyTokens))
                            continue;

                        if (!names.Contains(propertyName))
                            names.Add(propertyName);
                    }
                }
            }
            catch
            {
                // Ignore diagnostic failures.
            }

            var fallbackNames = new[]
            {
                "_BaseColor", "_Color", "_MainColor", "_TintColor", "_VehicleColor", "_PaintColor",
                "_PrimaryColor", "_SecondaryColor", "_Color1", "_Color2", "_AlbedoColor", "_DiffuseColor"
            };

            for (var index = 0; index < fallbackNames.Length; index++)
            {
                var name = fallbackNames[index];
                if (material.HasProperty(Shader.PropertyToID(name)) && !names.Contains(name))
                    names.Add(name);
            }

            return names.Count == 0 ? "<none>" : string.Join("|", names.ToArray());
        }

        private static Color GetPinkColorFor(Renderer renderer, Material material, int materialIndex, RootKind kind, bool strictVehicleFallback)
        {
            var key = GetStablePinkKey(renderer, material, kind, strictVehicleFallback);
            var index = PositiveStableHash(key) % PinkPalette.Length;
            return PinkPalette[index];
        }

        private static string GetStablePinkKey(Renderer renderer, Material material, RootKind kind, bool strictVehicleFallback)
        {
            var materialName = NormalizeName(material.name);

            if (kind == RootKind.Vehicle)
            {
                if (strictVehicleFallback)
                {
                    // Parked/static vehicles appear to switch renderer/LOD while the player drives past.
                    // Use a quantized world-position key so all LOD variants at the same parked location
                    // receive the same pink tone. Do not include renderer/material IDs here.
                    return "vehicle-fallback-position|" + GetQuantizedBoundsPositionKey(renderer);
                }

                return "vehicle-root|" + FindStableVehicleOwnerName(renderer.transform) + "|" + materialName;
            }

            return "npc|" + materialName;
        }

        private static string GetQuantizedBoundsPositionKey(Renderer renderer)
        {
            var center = renderer.bounds.center;
            const float bucketSize = 5f;

            var x = Mathf.RoundToInt(center.x / bucketSize);
            var y = Mathf.RoundToInt(center.y / bucketSize);
            var z = Mathf.RoundToInt(center.z / bucketSize);

            return x + ":" + y + ":" + z;
        }

        private static string FindStableVehicleOwnerName(Transform transform)
        {
            var current = transform;
            while (current != null)
            {
                var name = current.name ?? string.Empty;
                if (ContainsAnyToken(name, new[] { "vehicle", "truck", "flatbed", "taxi", "saloon", "sedan", "suv", "pickup", "delivery", "van" }))
                    return NormalizeName(name);

                current = current.parent;
            }

            if (transform.root != null)
                return NormalizeName(transform.root.name);

            return NormalizeName(transform.name);
        }

        private static string NormalizeName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("(Instance)", string.Empty)
                .Replace("(Clone)", string.Empty)
                .Trim()
                .ToLowerInvariant();
        }

        private static int PositiveStableHash(string value)
        {
            unchecked
            {
                var hash = (int)2166136261;
                for (var index = 0; index < value.Length; index++)
                    hash = (hash ^ value[index]) * 16777619;

                return hash & 0x7fffffff;
            }
        }

        private static bool TryTintMaterial(Material material, Color pinkColor)
        {
            var changedProperties = new List<MaterialColorProperty>();
            for (var index = 0; index < CandidateColorPropertyIds.Length; index++)
            {
                var propertyId = CandidateColorPropertyIds[index];
                if (!material.HasProperty(propertyId))
                    continue;

                changedProperties.Add(new MaterialColorProperty(propertyId, material.GetColor(propertyId)));
            }

            if (changedProperties.Count == 0)
                return false;

            var materialId = material.GetInstanceID();
            var isNewPatch = !PatchedMaterials.ContainsKey(materialId);
            if (isNewPatch)
                PatchedMaterials[materialId] = new MaterialColorSnapshot(material, changedProperties.ToArray());

            foreach (var property in changedProperties)
                material.SetColor(property.PropertyId, pinkColor);

            return isNewPatch;
        }

        private static bool TryTintRendererPropertyBlock(Renderer renderer, int materialIndex, Material material, Color pinkColor)
        {
            var supportedPropertyIds = GetSupportedColorPropertyIds(material);
            if (supportedPropertyIds.Count == 0)
                return false;

            var key = new RendererSlotKey(renderer.GetInstanceID(), materialIndex);
            if (PatchedRendererSlots.ContainsKey(key))
                return false;

            var originalBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(originalBlock, materialIndex);
            PatchedRendererSlots[key] = new RendererPropertyBlockSnapshot(renderer, materialIndex, originalBlock);

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, materialIndex);
            for (var index = 0; index < supportedPropertyIds.Count; index++)
                block.SetColor(supportedPropertyIds[index], pinkColor);

            renderer.SetPropertyBlock(block, materialIndex);
            return true;
        }

        private static List<int> GetSupportedColorPropertyIds(Material material)
        {
            var supported = new List<int>();

            var shader = material.shader;
            if (shader != null)
            {
                var shaderId = shader.GetInstanceID();
                if (!ShaderColorPropertyIdCache.TryGetValue(shaderId, out var shaderPropertyIds))
                {
                    shaderPropertyIds = DiscoverShaderColorPropertyIds(shader);
                    ShaderColorPropertyIdCache[shaderId] = shaderPropertyIds;
                }

                for (var index = 0; index < shaderPropertyIds.Length; index++)
                {
                    var propertyId = shaderPropertyIds[index];
                    if (material.HasProperty(propertyId) && !supported.Contains(propertyId))
                        supported.Add(propertyId);
                }
            }

            // Fallback for shaders where runtime property enumeration is incomplete or unavailable.
            for (var index = 0; index < CandidateColorPropertyIds.Length; index++)
            {
                var propertyId = CandidateColorPropertyIds[index];
                if (material.HasProperty(propertyId) && !supported.Contains(propertyId))
                    supported.Add(propertyId);
            }

            return supported;
        }

        private static int[] DiscoverShaderColorPropertyIds(Shader shader)
        {
            var ids = new List<int>();

            try
            {
                var count = shader.GetPropertyCount();
                for (var index = 0; index < count; index++)
                {
                    if (shader.GetPropertyType(index) != ShaderPropertyType.Color)
                        continue;

                    var propertyName = shader.GetPropertyName(index);
                    if (ContainsAnyToken(propertyName, ShaderColorPropertyDenyTokens))
                        continue;

                    var propertyId = Shader.PropertyToID(propertyName);
                    if (!ids.Contains(propertyId))
                        ids.Add(propertyId);
                }
            }
            catch (Exception ex)
            {
                PinkFileLogger.Verbose($"Shader property discovery failed for shader={shader.name}: {ex.GetType().Name}: {ex.Message}");
            }

            if (ids.Count > 0)
                PinkFileLogger.Verbose($"Shader color properties discovered: shader={shader.name}, count={ids.Count}");

            return ids.ToArray();
        }

        private static bool ContainsAnyToken(string? value, string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            for (var index = 0; index < tokens.Length; index++)
            {
                if (value.IndexOf(tokens[index], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static string GetPath(Transform? transform, int maxDepth)
        {
            if (transform == null)
                return "<null>";

            var names = new List<string>();
            var current = transform;
            var depth = 0;
            while (current != null && depth < maxDepth)
            {
                names.Add(current.name);
                current = current.parent;
                depth++;
            }

            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static void CountBehaviourHit(string typeName)
        {
            if (!BehaviourTypeHitCounts.TryGetValue(typeName, out var count))
                count = 0;

            BehaviourTypeHitCounts[typeName] = count + 1;
        }

        private static void LogBehaviourTypeSummary()
        {
            if (BehaviourTypeHitCounts.Count == 0)
            {
                PinkFileLogger.Info("Behaviour type summary: no vehicle/NPC-like MonoBehaviour type names matched.");
                return;
            }

            var parts = new List<string>();
            foreach (var pair in BehaviourTypeHitCounts)
                parts.Add(pair.Key + "=" + pair.Value);

            PinkFileLogger.Info("Behaviour type summary: " + string.Join(", ", parts.ToArray()));
        }

        internal readonly struct ScanResult
        {
            internal ScanResult(bool ready, int foundTargetCount, int newCandidateCount, int newPatchCount)
            {
                Ready = ready;
                FoundTargetCount = foundTargetCount;
                NewCandidateCount = newCandidateCount;
                NewPatchCount = newPatchCount;
            }

            internal bool Ready { get; }
            internal int FoundTargetCount { get; }
            internal int NewCandidateCount { get; }
            internal int NewPatchCount { get; }
        }

        private enum RootKind
        {
            Vehicle,
            Npc
        }

        private readonly struct MaterialColorSnapshot
        {
            internal MaterialColorSnapshot(Material material, MaterialColorProperty[] properties)
            {
                Material = material;
                Properties = properties;
            }

            internal Material Material { get; }
            internal MaterialColorProperty[] Properties { get; }
        }

        private readonly struct MaterialColorProperty
        {
            internal MaterialColorProperty(int propertyId, Color originalColor)
            {
                PropertyId = propertyId;
                OriginalColor = originalColor;
            }

            internal int PropertyId { get; }
            internal Color OriginalColor { get; }
        }

        private readonly struct RendererSlotKey : IEquatable<RendererSlotKey>
        {
            internal RendererSlotKey(int rendererId, int materialIndex)
            {
                RendererId = rendererId;
                MaterialIndex = materialIndex;
            }

            private int RendererId { get; }
            private int MaterialIndex { get; }

            public bool Equals(RendererSlotKey other)
            {
                return RendererId == other.RendererId && MaterialIndex == other.MaterialIndex;
            }

            public override bool Equals(object? obj)
            {
                return obj is RendererSlotKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (RendererId * 397) ^ MaterialIndex;
                }
            }
        }

        private readonly struct RendererPropertyBlockSnapshot
        {
            internal RendererPropertyBlockSnapshot(Renderer renderer, int materialIndex, MaterialPropertyBlock originalBlock)
            {
                Renderer = renderer;
                MaterialIndex = materialIndex;
                OriginalBlock = originalBlock;
            }

            internal Renderer Renderer { get; }
            internal int MaterialIndex { get; }
            internal MaterialPropertyBlock OriginalBlock { get; }
        }
    }
}
