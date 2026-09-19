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
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;

                RemoveImportedLightsAndCameras(visual);

                var roadRenderers = FindRoadRenderers(visual).ToArray();
                if (roadRenderers.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Could not identify the Monza asphalt meshes. Expected material 'logoansa_95' " +
                        "or source meshes Object_105/Object_106.");
                }

                DisableOutlierRenderers(visual, roadRenderers);

                var roadBounds = CombinedBounds(roadRenderers);
                var recenter = new Vector3(
                    -roadBounds.center.x,
                    -roadBounds.min.y,
                    -roadBounds.center.z);
                visual.transform.position += recenter;

                Physics.SyncTransforms();
                roadBounds = CombinedBounds(roadRenderers);

                var grip = GetOrCreateTrackGrip();
                var colliders = CreateRoadColliders(roadRenderers, grip);
                if (colliders.Count == 0)
                    throw new InvalidOperationException("No MeshColliders were created for the Monza asphalt.");

                Physics.SyncTransforms();
                CreateInvisibleSafetyBase(root.transform, roadBounds, grip);
                var spawn = CreateSpawnPoint(root.transform, roadRenderers, roadBounds);

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
            IEnumerable<MeshRenderer> roadRenderers,
            PhysicMaterial grip)
        {
            var result = new List<MeshCollider>();
            int groundLayer = RequireLayer("Ground");

            foreach (var renderer in roadRenderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                var existing = renderer.GetComponent<MeshCollider>();
                if (existing != null)
                    UnityEngine.Object.DestroyImmediate(existing);

                var collider = renderer.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
                collider.isTrigger = false;
                collider.cookingOptions =
                    MeshColliderCookingOptions.CookForFasterSimulation |
                    MeshColliderCookingOptions.EnableMeshCleaning |
                    MeshColliderCookingOptions.WeldColocatedVertices |
                    MeshColliderCookingOptions.UseFastMidphase;
                collider.sharedMaterial = grip;
                renderer.gameObject.layer = groundLayer;
                result.Add(collider);
            }

            return result;
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
            IReadOnlyList<MeshRenderer> roadRenderers,
            Bounds roadBounds)
        {
            var spawn = new GameObject("SpawnPoint").transform;
            spawn.SetParent(parent, false);

            // Pick a point from the actual asphalt triangles instead of sampling a
            // coarse X/Z grid across a 2+ km circuit. A narrow road can easily fall
            // between grid points even when its collider is perfectly valid.
            var preferred = new Vector3(
                roadBounds.center.x + roadBounds.size.x * 0.267f,
                roadBounds.center.y,
                roadBounds.center.z + roadBounds.size.z * 0.245f);

            bool found = false;
            Vector3 bestPoint = default;
            Vector3 bestForward = Vector3.forward;
            float bestScore = float.NegativeInfinity;
            string bestRenderer = string.Empty;
            int sampledTriangles = 0;

            foreach (var renderer in roadRenderers)
            {
                if (renderer == null)
                    continue;

                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;

                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                var transform = renderer.transform;

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

                    // Prefer a sensible location near the source model's known
                    // start/finish region, but any genuine upward asphalt triangle
                    // is valid as a fallback.
                    float planarDistance = Vector2.Distance(
                        new Vector2(center.x, center.z),
                        new Vector2(preferred.x, preferred.z));

                    // A slightly larger triangle is preferable to tiny seam/decal
                    // geometry, without allowing huge polygons to dominate.
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
                    bestRenderer = renderer.name;
                    found = true;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    "Could not find an upward-facing driveable triangle on the bundled Monza asphalt. " +
                    "roadRenderers=" + roadRenderers.Count +
                    ", sampledTriangles=" + sampledTriangles + ".");
            }

            spawn.position = bestPoint + Vector3.up * 0.45f;
            spawn.rotation = Quaternion.LookRotation(bestForward, Vector3.up);

            Debug.Log(
                "MonzaTestTrack spawn selected from asphalt geometry: renderer=" +
                bestRenderer +
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

        private static PhysicMaterial GetOrCreateTrackGrip()
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(PhysicsMaterialPath);
            if (material == null)
            {
                material = new PhysicMaterial("MonzaTrackGrip");
                AssetDatabase.CreateAsset(material, PhysicsMaterialPath);
            }

            material.dynamicFriction = 0.8f;
            material.staticFriction = 0.8f;
            material.bounciness = 0f;
            material.frictionCombine = PhysicMaterialCombine.Average;
            material.bounceCombine = PhysicMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
            return material;
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
