#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MonzaTestTrack.Editor
{
    public static class MonzaTestTrackAssetBuilder
    {
        private const string Root = "Assets/Mods/Monza_Test_Track";
        private const string ModelPath = Root + "/Models/monza_circuit_1998_layout.glb";
        private const string PrefabPath = Root + "/Prefabs/MonzaTestTrack.prefab";
        private const string GeneratedFolder = Root + "/Generated";
        private const string PhysicsMaterialPath = GeneratedFolder + "/MonzaTrackGrip.physicMaterial";
        private const string GrassPhysicsMaterialPath = GeneratedFolder + "/MonzaGrass.physicMaterial";
        private const string GravelPhysicsMaterialPath = GeneratedFolder + "/MonzaGravel.physicMaterial";
        private const string PavedRunoffPhysicsMaterialPath = GeneratedFolder + "/MonzaPavedRunoff.physicMaterial";
        private const string SceneryPhysicsMaterialPath = GeneratedFolder + "/MonzaScenery.physicMaterial";
        private const string BundleName = "monzatesttrack.unity3d";
        private const string BootstrapScenePath = Root + "/Scenes/MonzaTestTrack_Bootstrap.unity";

        [MenuItem("Big Ambitions Mods/Monza Test Track/Build Full Visual AssetBundle")]
        public static void BuildFromMenu()
        {
            BuildInternal();
        }

        public static void BuildBatch()
        {
            try
            {
                BuildInternal();
                Debug.Log("MONZA_TEST_TRACK_BUILD_PASS");
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                else
                    throw;
            }
        }

        private static void BuildInternal()
        {
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(GeneratedFolder);
            EnsureFolder(Root + "/Scenes");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Monza GLB did not import at '" + ModelPath + "'. " +
                    "Run tools/BuildMonzaTestTrackAssets.ps1 so the source GLB is copied into the project first.");
            }

            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || string.IsNullOrEmpty(activeScene.path))
            {
                var bootstrap = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(bootstrap, BootstrapScenePath))
                    throw new InvalidOperationException("Could not save Monza bootstrap scene.");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject root = null;

            try
            {
                root = new GameObject("MonzaTestTrack");
                SceneManager.MoveGameObjectToScene(root, scene);

                var visual = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject;
                if (visual == null)
                    throw new InvalidOperationException("Could not instantiate the imported Monza GLB.");

                PrefabUtility.UnpackPrefabInstance(
                    visual,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);

                visual.name = "Visual";
                visual.transform.localPosition = Vector3.zero;

                // The source model's circuit lies in X/Y with Z as elevation.
                // Convert source Z-up to Unity Y-up with -90 degrees around X.
                // +90 degrees makes the road horizontal too, but inverts elevation
                // and places grandstands/trees underneath the circuit.
                visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                visual.transform.localScale = Vector3.one;

                RemoveImportedLightsAndCameras(visual);
                PrepareVisualRenderers(visual);

                var roadRenderers = FindRoadRenderers(visual).ToArray();
                if (roadRenderers.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Could not identify the Monza asphalt meshes. Expected material 'logoansa_95' " +
                        "or source meshes Object_105/Object_106.");
                }

                var runoffRenderers = FindRunoffRenderers(visual, roadRenderers).ToArray();
                var roadSupportRenderers = FindRoadSupportRenderers(
                    visual, roadRenderers, runoffRenderers).ToArray();
                FixRunoffVisuals(runoffRenderers);

                DisableOutlierRenderers(visual, roadRenderers);

                var roadBounds = CombinedBounds(roadRenderers);
                var recenter = new Vector3(
                    -roadBounds.center.x,
                    -roadBounds.min.y,
                    -roadBounds.center.z);
                visual.transform.position += recenter;

                Physics.SyncTransforms();
                roadBounds = CombinedBounds(roadRenderers);

                Debug.Log(
                    "MonzaTestTrack corrected road bounds: min=" + roadBounds.min +
                    ", max=" + roadBounds.max +
                    ", size=" + roadBounds.size + ".");

                // A valid Monza road surface must be kilometres wide/long in X/Z
                // while remaining only a few tens of metres tall in Y. Fail fast
                // if a future importer/transform change rotates it upright again.
                if (roadBounds.size.y > 100f ||
                    roadBounds.size.x < 1000f ||
                    roadBounds.size.z < 500f)
                {
                    throw new InvalidOperationException(
                        "Monza road orientation sanity check failed. Expected a flat X/Z circuit, got size=" +
                        roadBounds.size + ".");
                }

                var trackGrip = GetOrCreateSurfaceMaterial(
                    PhysicsMaterialPath, "MonzaTrackGrip", 0.95f, 1.00f, PhysicMaterialCombine.Maximum);
                var grassGrip = GetOrCreateSurfaceMaterial(
                    GrassPhysicsMaterialPath, "MonzaGrass", 0.35f, 0.40f, PhysicMaterialCombine.Minimum);
                var gravelGrip = GetOrCreateSurfaceMaterial(
                    GravelPhysicsMaterialPath, "MonzaGravel", 0.18f, 0.22f, PhysicMaterialCombine.Minimum);
                var pavedRunoffGrip = GetOrCreateSurfaceMaterial(
                    PavedRunoffPhysicsMaterialPath, "MonzaPavedRunoff", 0.75f, 0.80f, PhysicMaterialCombine.Average);
                var sceneryPhysics = GetOrCreateSurfaceMaterial(
                    SceneryPhysicsMaterialPath, "MonzaScenery", 0.55f, 0.60f, PhysicMaterialCombine.Average);

                var colliders = CreateRoadColliders(root.transform, roadRenderers, trackGrip);
                if (colliders.Count == 0)
                    throw new InvalidOperationException("No MeshColliders were created for the Monza asphalt.");

                var runoffColliders = CreateRunoffColliders(
                    root.transform, runoffRenderers, grassGrip, gravelGrip, pavedRunoffGrip);
                var roadSupportColliders = CreateRoadSupportColliders(
                    root.transform, roadSupportRenderers, trackGrip);
                int sceneryColliders = CreateSceneryColliders(
                    visual,
                    roadRenderers,
                    runoffRenderers,
                    roadSupportRenderers,
                    sceneryPhysics);

                Physics.SyncTransforms();
                CreateInvisibleSafetyBase(root.transform, roadBounds, grassGrip);
                var spawn = CreateSpawnPoint(root.transform, colliders, roadBounds);

                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform == spawn)
                        continue;
                    transform.gameObject.isStatic = true;
                }

                var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (saved == null)
                    throw new InvalidOperationException("Could not save MonzaTestTrack.prefab.");

                Debug.Log(
                    "MonzaTestTrack prefab prepared: renderers=" +
                    saved.GetComponentsInChildren<Renderer>(true).Length +
                    ", roadColliders=" + colliders.Count +
                    ", runoffColliders=" + runoffColliders.Count +
                    ", roadSupportColliders=" + roadSupportColliders.Count +
                    ", sceneryColliders=" + sceneryColliders +
                    ", roadSize=" + roadBounds.size +
                    ", spawn=" + spawn.position + ".");
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            BuildWindowsBundle();
            ValidateBundle();
        }

        private static IEnumerable<MeshRenderer> FindRoadRenderers(GameObject visual)
        {
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                bool materialMatch = renderer.sharedMaterials.Any(
                    material => material != null &&
                                material.name.IndexOf("logoansa_95", StringComparison.OrdinalIgnoreCase) >= 0);

                string meshName = filter.sharedMesh.name ?? string.Empty;
                bool meshMatch =
                    string.Equals(meshName, "Object_105", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(meshName, "Object_106", StringComparison.OrdinalIgnoreCase);

                string objectName = renderer.gameObject.name ?? string.Empty;
                bool objectMatch =
                    string.Equals(objectName, "Object_109", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(objectName, "Object_110", StringComparison.OrdinalIgnoreCase);

                if (materialMatch || meshMatch || objectMatch)
                    yield return renderer;
            }
        }

        private static IEnumerable<MeshRenderer> FindRunoffRenderers(
            GameObject visual,
            IReadOnlyCollection<MeshRenderer> roadRenderers)
        {
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (roadRenderers.Contains(renderer))
                    continue;

                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                bool isRunoffSurface = renderer.sharedMaterials.Any(material =>
                {
                    if (material == null)
                        return false;

                    string name = material.name ?? string.Empty;
                    return string.Equals(name, "logoansa", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(name, "logoansa_78", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(name, "logoansa_82", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(name, "logoansa_84", StringComparison.OrdinalIgnoreCase);
                });

                if (isRunoffSurface)
                    yield return renderer;
            }
        }

        private static IEnumerable<MeshRenderer> FindRoadSupportRenderers(
            GameObject visual,
            IReadOnlyCollection<MeshRenderer> roadRenderers,
            IReadOnlyCollection<MeshRenderer> runoffRenderers)
        {
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (roadRenderers.Contains(renderer) || runoffRenderers.Contains(renderer))
                    continue;

                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                // The start-grid / start-finish detail pieces are separate flat
                // road meshes in this GLB. The main logoansa_95 road has holes
                // where these pieces live, so they need real support colliders.
                bool supportMaterial = renderer.sharedMaterials.Any(material =>
                {
                    if (material == null)
                        return false;

                    string name = material.name ?? string.Empty;
                    return string.Equals(name, "logoansa_79", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(name, "logoansa_80", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(name, "logoansa_81", StringComparison.OrdinalIgnoreCase);
                });

                if (supportMaterial)
                    yield return renderer;
            }
        }

        private static void FixRunoffVisuals(IEnumerable<MeshRenderer> runoffRenderers)
        {
            int gravelRenderers = 0;
            int materialsTouched = 0;

            foreach (var renderer in runoffRenderers)
            {
                bool gravel = renderer.sharedMaterials.Any(material =>
                    material != null &&
                    string.Equals(material.name, "logoansa_82", StringComparison.OrdinalIgnoreCase));

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                        continue;

                    string name = material.name ?? string.Empty;
                    bool surfaceMaterial =
                        string.Equals(name, "logoansa", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "logoansa_78", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "logoansa_82", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "logoansa_84", StringComparison.OrdinalIgnoreCase);

                    if (!surfaceMaterial)
                        continue;

                    material.doubleSidedGI = true;
                    if (material.HasProperty("_DoubleSidedEnable"))
                        material.SetFloat("_DoubleSidedEnable", 1f);
                    if (material.HasProperty("_CullMode"))
                        material.SetFloat("_CullMode", 0f);
                    if (material.HasProperty("_CullModeForward"))
                        material.SetFloat("_CullModeForward", 0f);
                    material.EnableKeyword("_DOUBLESIDED_ON");
                    EditorUtility.SetDirty(material);
                    materialsTouched++;
                }

                // Several gravel/runoff polygons in the source model are almost
                // coplanar with the surrounding ground. Lift only those visual
                // surfaces a few centimetres to avoid intermittent depth fighting.
                if (gravel)
                {
                    renderer.transform.position += Vector3.up * 0.025f;
                    gravelRenderers++;
                }
            }

            Debug.Log(
                "MonzaTestTrack runoff visuals: gravelRenderers=" + gravelRenderers +
                ", materialsTouched=" + materialsTouched + ".");
        }

        private static void DisableOutlierRenderers(GameObject visual, IReadOnlyCollection<MeshRenderer> roadRenderers)
        {
            int disabled = 0;
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (roadRenderers.Contains(renderer))
                    continue;

                var size = renderer.bounds.size;
                if (size.x <= 3500f && size.z <= 3500f)
                    continue;

                renderer.enabled = false;
                disabled++;
            }

            Debug.Log("MonzaTestTrack: disabled " + disabled + " oversized source renderer(s).");
        }

        private static List<MeshCollider> CreateRoadColliders(
            Transform parent,
            IEnumerable<MeshRenderer> roadRenderers,
            PhysicMaterial grip)
        {
            var result = new List<MeshCollider>();
            int groundLayer = RequireLayer("Ground");

            var collisionsRoot = new GameObject("Collisions");
            collisionsRoot.transform.SetParent(parent, false);
            collisionsRoot.layer = groundLayer;
            collisionsRoot.isStatic = true;

            int colliderIndex = 0;
            int totalTriangles = 0;
            int flippedTriangles = 0;

            foreach (var renderer in roadRenderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var sourceMesh = filter != null ? filter.sharedMesh : null;
                if (sourceMesh == null)
                    continue;

                var sourceVertices = sourceMesh.vertices;
                var sourceTriangles = sourceMesh.triangles;
                if (sourceVertices.Length < 3 || sourceTriangles.Length < 3)
                    continue;

                var bakedVertices = new Vector3[sourceVertices.Length];
                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    var world = renderer.transform.TransformPoint(sourceVertices[i]);
                    bakedVertices[i] = parent.InverseTransformPoint(world);
                }

                var bakedTriangles = (int[])sourceTriangles.Clone();
                for (int i = 0; i + 2 < bakedTriangles.Length; i += 3)
                {
                    int ia = bakedTriangles[i];
                    int ib = bakedTriangles[i + 1];
                    int ic = bakedTriangles[i + 2];

                    var a = bakedVertices[ia];
                    var b = bakedVertices[ib];
                    var c = bakedVertices[ic];
                    var cross = Vector3.Cross(b - a, c - a);

                    // glTF import can leave the complete road surface with the
                    // opposite winding after axis conversion / mirrored transforms.
                    // Make the drivable side consistently face upward for PhysX.
                    if (cross.y < 0f)
                    {
                        bakedTriangles[i + 1] = ic;
                        bakedTriangles[i + 2] = ib;
                        flippedTriangles++;
                    }

                    totalTriangles++;
                }

                string meshPath = GeneratedFolder + "/COL_Track_" + colliderIndex + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
                    AssetDatabase.DeleteAsset(meshPath);

                var collisionMesh = new Mesh
                {
                    name = "COL_Track_" + colliderIndex,
                    indexFormat = bakedVertices.Length > 65535
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
                };
                collisionMesh.vertices = bakedVertices;
                collisionMesh.triangles = bakedTriangles;
                collisionMesh.RecalculateNormals();
                collisionMesh.RecalculateBounds();
                AssetDatabase.CreateAsset(collisionMesh, meshPath);

                var collisionObject = new GameObject("COL_Track_" + colliderIndex);
                collisionObject.transform.SetParent(collisionsRoot.transform, false);
                collisionObject.layer = groundLayer;
                collisionObject.isStatic = true;

                var collider = collisionObject.AddComponent<MeshCollider>();
                collider.sharedMesh = collisionMesh;
                collider.convex = false;
                collider.isTrigger = false;
                collider.cookingOptions =
                    MeshColliderCookingOptions.CookForFasterSimulation |
                    MeshColliderCookingOptions.EnableMeshCleaning |
                    MeshColliderCookingOptions.WeldColocatedVertices |
                    MeshColliderCookingOptions.UseFastMidphase;
                collider.sharedMaterial = grip;

                // High-speed safety layer: a second copy slightly below the
                // authored road catches a vehicle that would otherwise tunnel
                // through a thin MeshCollider between physics steps.
                var backupObject = new GameObject("COL_Track_Backup_" + colliderIndex);
                backupObject.transform.SetParent(collisionsRoot.transform, false);
                backupObject.transform.localPosition = Vector3.down * 0.25f;
                backupObject.layer = groundLayer;
                backupObject.isStatic = true;

                var backupCollider = backupObject.AddComponent<MeshCollider>();
                backupCollider.sharedMesh = collisionMesh;
                backupCollider.convex = false;
                backupCollider.isTrigger = false;
                backupCollider.cookingOptions = collider.cookingOptions;
                backupCollider.sharedMaterial = grip;

                result.Add(collider);
                colliderIndex++;
            }

            AssetDatabase.SaveAssets();

            Debug.Log(
                "MonzaTestTrack collision geometry: colliders=" + result.Count +
                ", triangles=" + totalTriangles +
                ", flippedWinding=" + flippedTriangles + ".");

            return result;
        }

        private static List<MeshCollider> CreateRunoffColliders(
            Transform parent,
            IEnumerable<MeshRenderer> runoffRenderers,
            PhysicMaterial grassGrip,
            PhysicMaterial gravelGrip,
            PhysicMaterial pavedRunoffGrip)
        {
            var result = new List<MeshCollider>();
            int groundLayer = RequireLayer("Ground");

            var collisionsRoot = new GameObject("RunoffCollisions");
            collisionsRoot.transform.SetParent(parent, false);
            collisionsRoot.layer = groundLayer;
            collisionsRoot.isStatic = true;

            int colliderIndex = 0;
            int totalTriangles = 0;
            int flippedTriangles = 0;

            foreach (var renderer in runoffRenderers)
            {
                bool isGrass = renderer.sharedMaterials.Any(material =>
                    material != null &&
                    string.Equals(material.name, "logoansa", StringComparison.OrdinalIgnoreCase));
                bool isGravel = renderer.sharedMaterials.Any(material =>
                    material != null &&
                    string.Equals(material.name, "logoansa_82", StringComparison.OrdinalIgnoreCase));

                string surfaceName = isGravel ? "Gravel" : (isGrass ? "Grass" : "PavedRunoff");
                PhysicMaterial surfaceMaterial =
                    isGravel ? gravelGrip : (isGrass ? grassGrip : pavedRunoffGrip);

                var filter = renderer.GetComponent<MeshFilter>();
                var sourceMesh = filter != null ? filter.sharedMesh : null;
                if (sourceMesh == null)
                    continue;

                var sourceVertices = sourceMesh.vertices;
                var sourceTriangles = sourceMesh.triangles;
                if (sourceVertices.Length < 3 || sourceTriangles.Length < 3)
                    continue;

                var bakedVertices = new Vector3[sourceVertices.Length];
                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    var world = renderer.transform.TransformPoint(sourceVertices[i]);
                    bakedVertices[i] = parent.InverseTransformPoint(world);
                }

                var acceptedTriangles = new List<int>(sourceTriangles.Length);

                for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
                {
                    int ia = sourceTriangles[i];
                    int ib = sourceTriangles[i + 1];
                    int ic = sourceTriangles[i + 2];

                    var a = bakedVertices[ia];
                    var b = bakedVertices[ib];
                    var c = bakedVertices[ic];
                    var cross = Vector3.Cross(b - a, c - a);
                    float magnitude = cross.magnitude;
                    if (magnitude < 0.01f)
                        continue;

                    // These renderers are selected by known ground-surface
                    // materials. Keep every non-degenerate triangle so grass and
                    // gravel cannot silently lose large sections due to slope or
                    // imported-normal thresholds.
                    if (cross.y < 0f)
                    {
                        acceptedTriangles.Add(ia);
                        acceptedTriangles.Add(ic);
                        acceptedTriangles.Add(ib);
                        flippedTriangles++;
                    }
                    else
                    {
                        acceptedTriangles.Add(ia);
                        acceptedTriangles.Add(ib);
                        acceptedTriangles.Add(ic);
                    }

                    totalTriangles++;
                }

                if (acceptedTriangles.Count < 3)
                    continue;

                string meshPath = GeneratedFolder + "/COL_" + surfaceName + "_" + colliderIndex + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
                    AssetDatabase.DeleteAsset(meshPath);

                var collisionMesh = new Mesh
                {
                    name = "COL_" + surfaceName + "_" + colliderIndex,
                    indexFormat = bakedVertices.Length > 65535
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
                };
                collisionMesh.vertices = bakedVertices;
                collisionMesh.triangles = acceptedTriangles.ToArray();
                collisionMesh.RecalculateNormals();
                collisionMesh.RecalculateBounds();
                AssetDatabase.CreateAsset(collisionMesh, meshPath);

                var collisionObject = new GameObject("COL_" + surfaceName + "_" + colliderIndex);
                collisionObject.transform.SetParent(collisionsRoot.transform, false);
                collisionObject.layer = groundLayer;
                collisionObject.isStatic = true;

                var collider = collisionObject.AddComponent<MeshCollider>();
                collider.sharedMesh = collisionMesh;
                collider.convex = false;
                collider.isTrigger = false;
                collider.cookingOptions =
                    MeshColliderCookingOptions.CookForFasterSimulation |
                    MeshColliderCookingOptions.EnableMeshCleaning |
                    MeshColliderCookingOptions.WeldColocatedVertices |
                    MeshColliderCookingOptions.UseFastMidphase;
                collider.sharedMaterial = surfaceMaterial;

                result.Add(collider);
                colliderIndex++;
            }

            AssetDatabase.SaveAssets();

            Debug.Log(
                "MonzaTestTrack runoff collision geometry: colliders=" + result.Count +
                ", groundTriangles=" + totalTriangles +
                ", flippedWinding=" + flippedTriangles + ".");

            return result;
        }

        private static List<MeshCollider> CreateRoadSupportColliders(
            Transform parent,
            IEnumerable<MeshRenderer> supportRenderers,
            PhysicMaterial trackGrip)
        {
            var result = new List<MeshCollider>();
            int groundLayer = RequireLayer("Ground");
            int colliderIndex = 0;
            int totalTriangles = 0;

            var root = new GameObject("RoadSupportCollisions");
            root.transform.SetParent(parent, false);
            root.layer = groundLayer;
            root.isStatic = true;

            foreach (var renderer in supportRenderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var sourceMesh = filter != null ? filter.sharedMesh : null;
                if (sourceMesh == null)
                    continue;

                var sourceVertices = sourceMesh.vertices;
                var sourceTriangles = sourceMesh.triangles;
                if (sourceVertices.Length < 3 || sourceTriangles.Length < 3)
                    continue;

                var bakedVertices = new Vector3[sourceVertices.Length];
                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    var world = renderer.transform.TransformPoint(sourceVertices[i]);
                    bakedVertices[i] = parent.InverseTransformPoint(world) + Vector3.down * 0.035f;
                }

                var triangles = (int[])sourceTriangles.Clone();
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int ia = triangles[i];
                    int ib = triangles[i + 1];
                    int ic = triangles[i + 2];

                    Vector3 a = bakedVertices[ia];
                    Vector3 b = bakedVertices[ib];
                    Vector3 c = bakedVertices[ic];

                    if (Vector3.Cross(b - a, c - a).y < 0f)
                    {
                        triangles[i + 1] = ic;
                        triangles[i + 2] = ib;
                    }

                    totalTriangles++;
                }

                string meshPath = GeneratedFolder + "/COL_TrackSupport_" + colliderIndex + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
                    AssetDatabase.DeleteAsset(meshPath);

                var collisionMesh = new Mesh
                {
                    name = "COL_TrackSupport_" + colliderIndex,
                    indexFormat = bakedVertices.Length > 65535
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
                };
                collisionMesh.vertices = bakedVertices;
                collisionMesh.triangles = triangles;
                collisionMesh.RecalculateNormals();
                collisionMesh.RecalculateBounds();
                AssetDatabase.CreateAsset(collisionMesh, meshPath);

                var collisionObject = new GameObject("COL_TrackSupport_" + colliderIndex);
                collisionObject.transform.SetParent(root.transform, false);
                collisionObject.layer = groundLayer;
                collisionObject.isStatic = true;

                var collider = collisionObject.AddComponent<MeshCollider>();
                collider.sharedMesh = collisionMesh;
                collider.convex = false;
                collider.isTrigger = false;
                collider.cookingOptions =
                    MeshColliderCookingOptions.CookForFasterSimulation |
                    MeshColliderCookingOptions.EnableMeshCleaning |
                    MeshColliderCookingOptions.WeldColocatedVertices |
                    MeshColliderCookingOptions.UseFastMidphase;
                collider.sharedMaterial = trackGrip;

                result.Add(collider);
                colliderIndex++;
            }

            AssetDatabase.SaveAssets();

            Debug.Log(
                "MonzaTestTrack road-support collision geometry: colliders=" +
                result.Count + ", triangles=" + totalTriangles + ".");

            return result;
        }

        private static int CreateSceneryColliders(
            GameObject visual,
            IReadOnlyCollection<MeshRenderer> roadRenderers,
            IReadOnlyCollection<MeshRenderer> runoffRenderers,
            IReadOnlyCollection<MeshRenderer> roadSupportRenderers,
            PhysicMaterial sceneryMaterial)
        {
            int wallsLayer = RequireLayer("BuildingWalls");
            int created = 0;
            int skippedFlat = 0;
            int totalTriangles = 0;

            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled ||
                    roadRenderers.Contains(renderer) ||
                    runoffRenderers.Contains(renderer) ||
                    roadSupportRenderers.Contains(renderer))
                {
                    continue;
                }

                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || mesh.triangles == null || mesh.triangles.Length < 3)
                    continue;

                // Do not turn road paint / decals into coplanar collision sheets.
                // Actual start-grid sheets are handled explicitly as TrackSupport.
                if (renderer.bounds.size.y < 0.12f &&
                    renderer.bounds.size.x > 1f &&
                    renderer.bounds.size.z > 1f)
                {
                    skippedFlat++;
                    continue;
                }

                var collisionObject = new GameObject("COL_Scenery_" + created);
                collisionObject.transform.SetParent(renderer.transform, false);
                collisionObject.layer = wallsLayer;
                collisionObject.isStatic = true;

                var collider = collisionObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
                collider.isTrigger = false;
                collider.cookingOptions =
                    MeshColliderCookingOptions.CookForFasterSimulation |
                    MeshColliderCookingOptions.EnableMeshCleaning |
                    MeshColliderCookingOptions.WeldColocatedVertices |
                    MeshColliderCookingOptions.UseFastMidphase;
                collider.sharedMaterial = sceneryMaterial;

                created++;
                totalTriangles += mesh.triangles.Length / 3;
            }

            Debug.Log(
                "MonzaTestTrack scenery collision geometry: colliders=" + created +
                ", triangles=" + totalTriangles +
                ", skippedFlatDecals=" + skippedFlat + ".");

            return created;
        }

        private static void CreateInvisibleSafetyBase(
            Transform parent,
            Bounds roadBounds,
            PhysicMaterial grip)
        {
            int groundLayer = RequireLayer("Ground");

            var safety = new GameObject("GroundSafetyBase");
            safety.transform.SetParent(parent, false);
            safety.layer = groundLayer;
            safety.isStatic = true;

            var box = safety.AddComponent<BoxCollider>();
            box.center = new Vector3(roadBounds.center.x, roadBounds.min.y - 3f, roadBounds.center.z);
            box.size = new Vector3(
                roadBounds.size.x + 160f,
                1f,
                roadBounds.size.z + 160f);
            box.sharedMaterial = grip;
        }

        private static Transform CreateSpawnPoint(
            Transform parent,
            IReadOnlyList<MeshCollider> roadColliders,
            Bounds roadBounds)
        {
            var spawn = new GameObject("SpawnPoint").transform;
            spawn.SetParent(parent, false);

            var preferred = new Vector3(
                roadBounds.center.x + roadBounds.size.x * 0.267f,
                roadBounds.center.y,
                roadBounds.center.z + roadBounds.size.z * 0.245f);

            bool found = false;
            Vector3 bestPoint = default;
            Vector3 bestForward = Vector3.forward;
            float bestScore = float.NegativeInfinity;
            string bestCollider = string.Empty;
            int sampledTriangles = 0;

            foreach (var collider in roadColliders)
            {
                var mesh = collider != null ? collider.sharedMesh : null;
                if (mesh == null)
                    continue;

                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                var transform = collider.transform;

                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var a = transform.TransformPoint(vertices[triangles[i]]);
                    var b = transform.TransformPoint(vertices[triangles[i + 1]]);
                    var c = transform.TransformPoint(vertices[triangles[i + 2]]);

                    var cross = Vector3.Cross(b - a, c - a);
                    float doubleArea = cross.magnitude;
                    if (doubleArea < 0.05f)
                        continue;

                    var normal = cross / doubleArea;
                    if (normal.y < 0.70f)
                        continue;

                    sampledTriangles++;

                    var center = (a + b + c) / 3f;
                    float planarDistance = Vector2.Distance(
                        new Vector2(center.x, center.z),
                        new Vector2(preferred.x, preferred.z));

                    float areaBonus = Mathf.Min(doubleArea * 0.5f, 20f);
                    float score = -planarDistance + areaBonus + normal.y * 10f;
                    if (score <= bestScore)
                        continue;

                    Vector3 edgeAB = b - a;
                    Vector3 edgeBC = c - b;
                    Vector3 edgeCA = a - c;
                    Vector3 forward = edgeAB;

                    if (edgeBC.sqrMagnitude > forward.sqrMagnitude)
                        forward = edgeBC;
                    if (edgeCA.sqrMagnitude > forward.sqrMagnitude)
                        forward = edgeCA;

                    forward.y = 0f;
                    if (forward.sqrMagnitude < 0.01f)
                        forward = Vector3.forward;
                    else
                        forward.Normalize();

                    bestScore = score;
                    bestPoint = center;
                    bestForward = forward;
                    bestCollider = collider.name;
                    found = true;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    "Could not find an upward-facing driveable triangle on the corrected Monza collision mesh. " +
                    "roadColliders=" + roadColliders.Count +
                    ", sampledTriangles=" + sampledTriangles + ".");
            }

            spawn.position = bestPoint + Vector3.up * 0.45f;
            spawn.rotation = Quaternion.LookRotation(bestForward, Vector3.up);

            Debug.Log(
                "MonzaTestTrack spawn selected from corrected collision geometry: collider=" +
                bestCollider +
                ", sampledTriangles=" + sampledTriangles +
                ", position=" + spawn.position +
                ", forward=" + bestForward + ".");

            return spawn;
        }

        private static Bounds CombinedBounds(IReadOnlyList<MeshRenderer> renderers)
        {
            if (renderers == null || renderers.Count == 0)
                throw new InvalidOperationException("No renderers supplied for bounds calculation.");

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void RemoveImportedLightsAndCameras(GameObject root)
        {
            foreach (var light in root.GetComponentsInChildren<Light>(true))
                UnityEngine.Object.DestroyImmediate(light.gameObject);

            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }

        private static PhysicMaterial GetOrCreateSurfaceMaterial(
            string assetPath,
            string name,
            float dynamicFriction,
            float staticFriction,
            PhysicMaterialCombine frictionCombine)
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(assetPath);
            if (material == null)
            {
                material = new PhysicMaterial(name);
                AssetDatabase.CreateAsset(material, assetPath);
            }

            material.dynamicFriction = dynamicFriction;
            material.staticFriction = staticFriction;
            material.bounciness = 0f;
            material.frictionCombine = frictionCombine;
            material.bounceCombine = PhysicMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void PrepareVisualRenderers(GameObject visual)
        {
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                // Runtime-added Monza renderers are not part of the city's baked
                // occlusion data. Keep them out of dynamic occlusion culling;
                // camera far-clip distance is raised while the player is on track.
                renderer.allowOcclusionWhenDynamic = false;
            }
        }

        private static void BuildWindowsBundle()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve Unity project root.");
            string outputDirectory = Path.Combine(
                projectRoot,
                "Assets",
                "Mods",
                "Monza_Test_Track",
                "AssetBundles",
                "Windows");

            Directory.CreateDirectory(outputDirectory);

            string producedPath = Path.Combine(outputDirectory, BundleName);
            if (File.Exists(producedPath))
                File.Delete(producedPath);
            if (File.Exists(producedPath + ".manifest"))
                File.Delete(producedPath + ".manifest");

            var build = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = new[] { PrefabPath }
            };

            var manifest = BuildPipeline.BuildAssetBundles(
                outputDirectory,
                new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64);

            if (manifest == null)
                throw new InvalidOperationException("Unity returned null while building the Monza AssetBundle.");

            if (!File.Exists(producedPath))
                throw new FileNotFoundException("Expected Monza AssetBundle was not produced.", producedPath);

            long length = new FileInfo(producedPath).Length;
            if (length < 1024)
                throw new InvalidOperationException(
                    "Built Monza AssetBundle is unexpectedly small: " + length + " bytes.");

            Debug.Log(
                "MonzaTestTrack AssetBundle built: " + producedPath +
                " (" + length.ToString("N0") + " bytes).");
        }

        private static void ValidateBundle()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve Unity project root.");
            string bundlePath = Path.Combine(
                projectRoot,
                "Assets",
                "Mods",
                "Monza_Test_Track",
                "AssetBundles",
                "Windows",
                BundleName);

            var bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
                throw new InvalidOperationException("Could not reload the built Monza AssetBundle.");

            try
            {
                var prefab = bundle.LoadAsset<GameObject>(PrefabPath);
                if (prefab == null)
                    throw new InvalidOperationException("Built bundle does not contain MonzaTestTrack.prefab.");

                int renderers = prefab.GetComponentsInChildren<Renderer>(true).Length;
                int colliders = prefab.GetComponentsInChildren<MeshCollider>(true).Length;
                var spawn = prefab.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform => transform.name == "SpawnPoint");

                if (renderers < 10)
                    throw new InvalidOperationException(
                        "Bundled Monza visual hierarchy looks incomplete; renderers=" + renderers + ".");
                if (colliders < 1)
                    throw new InvalidOperationException("Bundled Monza track has no MeshCollider.");
                if (spawn == null)
                    throw new InvalidOperationException("Bundled Monza track has no SpawnPoint.");

                Debug.Log(
                    "MONZA_BUNDLE_VERIFY_PASS renderers=" + renderers +
                    " meshColliders=" + colliders +
                    " spawn=" + spawn.localPosition);
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static int RequireLayer(string name)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer < 0)
                throw new InvalidOperationException("Required Big Ambitions layer is missing: " + name);
            return layer;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            int slash = folder.LastIndexOf('/');
            if (slash <= 0)
                throw new InvalidOperationException("Invalid asset folder: " + folder);

            string parent = folder.Substring(0, slash);
            string leaf = folder.Substring(slash + 1);
            EnsureFolder(parent);

            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
