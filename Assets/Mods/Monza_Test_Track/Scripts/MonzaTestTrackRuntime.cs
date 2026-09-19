#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BAModAPI;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace MonzaTestTrack
{
    public sealed class MonzaTestTrackRuntime : MonoBehaviour
    {
        private const string SurfaceDataPattern = "track_surface.clean.*.txt";

        // Keep X/Z inside the normal city/grid footprint so Gley traffic can still
        // resolve its active cells, but place the test facility far above the city.
        private static readonly Vector3 SiteCentre = new Vector3(0f, 600f, 0f);

        // Diagnostic fallback spawn derived from the extracted asphalt component.
        private static readonly Vector3 SpawnProbeLocal = new Vector3(618.5f, 80f, 274.05f);
        private static readonly Quaternion FallbackSpawnRotation = Quaternion.Euler(0f, 90f, 0f);

        private IModLogger? _logger;
        private string? _modRootPath;
        private GameObject? _site;
        private GameObject? _bundledPrefab;
        private Mesh? _trackMesh;
        private Material? _trackMaterial;
        private PhysicMaterial? _trackPhysicsMaterial;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        private bool _hasReturnPosition;
        private Vector3 _returnPosition;
        private Quaternion _returnRotation;
        private bool _shuttingDown;
        private bool _usingVisualBundle;

        private readonly Dictionary<Camera, float> _cameraFarClips = new Dictionary<Camera, float>();
        private Rigidbody? _trackVehicleBody;
        private CollisionDetectionMode? _previousCollisionDetectionMode;

        public void Initialize(string modRootPath, IModLogger logger, GameObject? bundledPrefab)
        {
            _modRootPath = modRootPath;
            _logger = logger;
            _bundledPrefab = bundledPrefab;

            try
            {
                if (_bundledPrefab != null)
                {
                    BuildFromBundle(_bundledPrefab);
                    _usingVisualBundle = true;
                }
                else
                {
                    BuildDiagnosticFallback();
                    _usingVisualBundle = false;
                }

                _logger.Info(
                    "[MonzaTestTrack] Track ready (" +
                    (_usingVisualBundle ? "full visual bundle" : "diagnostic fallback") +
                    "). Enter a vehicle and press F7 to teleport; press F7 again to return.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                Shutdown();
            }
        }

        private void Update()
        {
            if (_site == null)
                return;

            try
            {
                if (_hasReturnPosition)
                {
                    EnsureTrackCameraRange();
                    ApplyOffRoadSurfaceEffects();
                }
                else
                {
                    RestoreCameraRange();
                }

                if (Input.GetKeyDown(KeyCode.F7))
                    ToggleVehicleTeleport();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex);
            }
        }

        private void BuildFromBundle(GameObject prefab)
        {
            _site = Instantiate(prefab, SiteCentre, Quaternion.identity, transform);
            _site.name = "MonzaTestTrack.Site";

            var spawn = FindTransform(_site.transform, "SpawnPoint");
            if (spawn == null)
                throw new InvalidOperationException(
                    "Bundled Monza prefab does not contain the required SpawnPoint.");

            _spawnPosition = spawn.position;
            _spawnRotation = spawn.rotation;

            int renderers = _site.GetComponentsInChildren<Renderer>(true).Length;
            int colliders = _site.GetComponentsInChildren<Collider>(true).Length;
            int meshColliders = _site.GetComponentsInChildren<MeshCollider>(true).Length;

            if (renderers == 0)
                throw new InvalidOperationException("Bundled Monza prefab contains no renderers.");
            if (meshColliders == 0)
                throw new InvalidOperationException("Bundled Monza prefab contains no track MeshColliders.");

            Physics.SyncTransforms();

            _logger?.Info(
                "[MonzaTestTrack] Full visual prefab placed at " + SiteCentre +
                "; renderers=" + renderers +
                ", colliders=" + colliders +
                ", meshColliders=" + meshColliders +
                ", spawn=" + _spawnPosition + ".");
        }

        private void BuildDiagnosticFallback()
        {
            if (string.IsNullOrEmpty(_modRootPath))
                throw new InvalidOperationException("Mod root path is unavailable.");

            string configDirectory = Path.Combine(_modRootPath, "Config");
            if (!Directory.Exists(configDirectory))
                throw new DirectoryNotFoundException(
                    "Monza diagnostic Config folder is missing: " + configDirectory);

            string[] parts = Directory.GetFiles(configDirectory, SurfaceDataPattern);
            Array.Sort(parts, StringComparer.OrdinalIgnoreCase);

            if (parts.Length == 0)
                throw new FileNotFoundException(
                    "Cleaned Monza diagnostic surface data was not installed in " + configDirectory + ".");

            var encoded = new StringBuilder();
            foreach (string part in parts)
                encoded.Append(File.ReadAllText(part).Trim());

            _trackMesh = LoadTrackMesh(encoded.ToString());

            _site = new GameObject("MonzaTestTrack.Site");
            _site.transform.SetParent(transform, false);
            _site.transform.SetPositionAndRotation(SiteCentre, Quaternion.identity);

            var track = new GameObject("COL_Track");
            track.transform.SetParent(_site.transform, false);
            track.layer = ResolveLayer("Ground");
            track.isStatic = true;

            track.AddComponent<MeshFilter>().sharedMesh = _trackMesh;

            _trackMaterial = CreateMaterial(
                "Monza Diagnostic Asphalt",
                new Color(0.16f, 0.17f, 0.18f, 1f),
                0.23f);
            track.AddComponent<MeshRenderer>().sharedMaterial = _trackMaterial;

            var collider = track.AddComponent<MeshCollider>();
            collider.sharedMesh = _trackMesh;
            collider.convex = false;
            collider.cookingOptions =
                MeshColliderCookingOptions.CookForFasterSimulation |
                MeshColliderCookingOptions.EnableMeshCleaning |
                MeshColliderCookingOptions.WeldColocatedVertices |
                MeshColliderCookingOptions.UseFastMidphase;
            collider.sharedMaterial = TrackPhysicsMaterial;

            BuildInvisibleSafetyBase(_trackMesh.bounds);
            Physics.SyncTransforms();

            if (!TryResolveFallbackSpawn(collider, out _spawnPosition))
                throw new InvalidOperationException(
                    "Could not resolve a driveable spawn point on the Monza diagnostic surface.");

            _spawnRotation = FallbackSpawnRotation;

            Bounds bounds = _trackMesh.bounds;
            _logger?.Info(
                "[MonzaTestTrack] Diagnostic surface placed at " + SiteCentre +
                "; asphalt=" + _trackMesh.vertexCount + " vertices / " +
                (_trackMesh.triangles.Length / 3) + " triangles" +
                "; size=" + bounds.size.x.ToString("0.0") + " x " +
                bounds.size.z.ToString("0.0") + " m" +
                "; spawn=" + _spawnPosition + ".");
        }

        private void BuildInvisibleSafetyBase(Bounds trackBounds)
        {
            if (_site == null)
                return;

            var safety = new GameObject("GroundSafetyBase");
            safety.transform.SetParent(_site.transform, false);
            safety.layer = ResolveLayer("Ground");
            safety.isStatic = true;

            var box = safety.AddComponent<BoxCollider>();
            box.center = new Vector3(trackBounds.center.x, trackBounds.min.y - 2.5f, trackBounds.center.z);
            box.size = new Vector3(
                Mathf.Max(trackBounds.size.x + 120f, 2500f),
                1f,
                Mathf.Max(trackBounds.size.z + 120f, 1250f));
            box.sharedMaterial = TrackPhysicsMaterial;
        }

        private bool TryResolveFallbackSpawn(MeshCollider trackCollider, out Vector3 position)
        {
            Vector3 origin = _site!.transform.TransformPoint(SpawnProbeLocal);

            if (trackCollider.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, 180f))
            {
                position = hit.point + Vector3.up * 0.35f;
                return true;
            }

            for (float radius = 20f; radius <= 260f; radius += 20f)
            {
                for (int step = 0; step < 24; step++)
                {
                    float angle = step * Mathf.PI * 2f / 24f;
                    Vector3 local = SpawnProbeLocal +
                                    new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    origin = _site.transform.TransformPoint(local);

                    if (trackCollider.Raycast(new Ray(origin, Vector3.down), out hit, 180f))
                    {
                        position = hit.point + Vector3.up * 0.35f;
                        return true;
                    }
                }
            }

            position = default;
            return false;
        }

        private void ToggleVehicleTeleport()
        {
            if (VehicleHelper.GetCurrentVehicle() == null)
            {
                _logger?.Warn("[MonzaTestTrack] F7 ignored: enter a vehicle first.");
                return;
            }

            var car = VehicleHelper.GetCurrentVehicleBase() as CarController;
            if (car == null)
            {
                _logger?.Warn("[MonzaTestTrack] F7 ignored: current vehicle is not a CarController.");
                return;
            }

            // F7 is a strict two-state toggle. Do not infer the state from height:
            // if the vehicle drives off the elevated circuit and falls below the
            // previous Y threshold, the next F7 must still return to the original
            // city position instead of overwriting it with the fall position.
            if (_hasReturnPosition)
            {
                Vector3 returnPosition = _returnPosition;
                Quaternion returnRotation = _returnRotation;

                TeleportVehicle(car, returnPosition, returnRotation);
                RestoreTrackRuntimeState();

                _hasReturnPosition = false;
                _logger?.Info(
                    "[MonzaTestTrack] Returned current vehicle to its previous city position " +
                    returnPosition + ".");
                return;
            }

            _returnPosition = car.transform.position;
            _returnRotation = Quaternion.Euler(0f, car.transform.eulerAngles.y, 0f);
            _hasReturnPosition = true;

            _logger?.Info(
                "[MonzaTestTrack] Stored city return position " + _returnPosition + ".");

            EnableHighSpeedTrackPhysics(car);
            TeleportVehicle(car, _spawnPosition, _spawnRotation);
            _logger?.Info(
                "[MonzaTestTrack] Teleported current vehicle to Monza " +
                (_usingVisualBundle ? "full visual track." : "diagnostic surface."));
        }

        private void EnableHighSpeedTrackPhysics(CarController car)
        {
            Rigidbody? body = car.vehicleController?.vehicleRigidbody;
            _trackVehicleBody = body;

            if (body == null || body.isKinematic)
                return;

            _previousCollisionDetectionMode = body.collisionDetectionMode;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private void RestoreTrackRuntimeState()
        {
            RestoreCameraRange();

            if (_trackVehicleBody != null &&
                _previousCollisionDetectionMode.HasValue &&
                !_trackVehicleBody.isKinematic)
            {
                _trackVehicleBody.collisionDetectionMode = _previousCollisionDetectionMode.Value;
            }

            _trackVehicleBody = null;
            _previousCollisionDetectionMode = null;
        }

        private void EnsureTrackCameraRange()
        {
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera == null)
                    continue;

                if (!_cameraFarClips.ContainsKey(camera))
                    _cameraFarClips[camera] = camera.farClipPlane;

                if (camera.farClipPlane < 5500f)
                    camera.farClipPlane = 5500f;
            }
        }

        private void RestoreCameraRange()
        {
            if (_cameraFarClips.Count == 0)
                return;

            foreach (var entry in _cameraFarClips)
            {
                if (entry.Key != null)
                    entry.Key.farClipPlane = entry.Value;
            }

            _cameraFarClips.Clear();
        }

        private void ApplyOffRoadSurfaceEffects()
        {
            if (_site == null)
                return;

            var car = VehicleHelper.GetCurrentVehicleBase() as CarController;
            Rigidbody? body = car?.vehicleController?.vehicleRigidbody;
            if (car == null || body == null || body.isKinematic)
                return;

            Vector3 origin = body.worldCenterOfMass + Vector3.up * 1.5f;
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                6f,
                ~0,
                QueryTriggerInteraction.Ignore);

            if (hits == null || hits.Length == 0)
                return;

            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            foreach (RaycastHit hit in hits)
            {
                Collider collider = hit.collider;
                if (collider == null || !collider.transform.IsChildOf(_site.transform))
                    continue;

                string name = collider.gameObject.name ?? string.Empty;

                if (name.StartsWith("COL_Gravel_", StringComparison.Ordinal))
                {
                    ApplyHorizontalDeceleration(body, 4.5f);
                    return;
                }

                if (name.StartsWith("COL_Grass_", StringComparison.Ordinal))
                {
                    ApplyHorizontalDeceleration(body, 1.4f);
                    return;
                }

                // The nearest Monza collider is asphalt, paved runoff, or the
                // safety base. No additional rolling resistance is needed.
                return;
            }
        }

        private static void ApplyHorizontalDeceleration(Rigidbody body, float metresPerSecondSquared)
        {
            Vector3 velocity = body.velocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            float speed = horizontal.magnitude;
            if (speed < 0.05f)
                return;

            float nextSpeed = Mathf.Max(0f, speed - metresPerSecondSquared * Time.deltaTime);
            float scale = nextSpeed / speed;

            body.velocity = new Vector3(
                horizontal.x * scale,
                velocity.y,
                horizontal.z * scale);
        }

        private static bool IsAtTestSite(Vector3 position)
        {
            // X/Z overlap with the real city by design. Height separates the facility.
            return position.y > SiteCentre.y - 120f;
        }

        private static void TeleportVehicle(CarController car, Vector3 position, Quaternion rotation)
        {
            Rigidbody? body = car.vehicleController?.vehicleRigidbody;
            if (body != null && !body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            VehicleHelper.TeleportVehicle(car, position, rotation);

            if (body != null && !body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
        }

        private static Transform? FindTransform(Transform root, string name)
        {
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(candidate.name, name, StringComparison.Ordinal))
                    return candidate;
            return null;
        }

        private static Mesh LoadTrackMesh(string encoded)
        {
            byte[] compressed = Convert.FromBase64String(encoded);

            using var compressedStream = new MemoryStream(compressed, false);
            using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);

            string magic = new string(reader.ReadChars(4));
            if (magic != "MTT1")
                throw new InvalidDataException("Unexpected Monza track data header.");

            ushort version = reader.ReadUInt16();
            if (version != 1)
                throw new InvalidDataException("Unsupported Monza track data version: " + version);

            int vertexCount = reader.ReadInt32();
            int indexCount = reader.ReadInt32();

            if (vertexCount <= 0 || vertexCount > 65535 ||
                indexCount <= 0 || indexCount % 3 != 0)
            {
                throw new InvalidDataException(
                    "Invalid Monza mesh counts: vertices=" + vertexCount + ", indices=" + indexCount);
            }

            var min = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var max = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            Vector3 size = max - min;

            var vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                float x = reader.ReadUInt16() / 65535f;
                float y = reader.ReadUInt16() / 65535f;
                float z = reader.ReadUInt16() / 65535f;
                vertices[i] = min + Vector3.Scale(size, new Vector3(x, y, z));
            }

            var triangles = new int[indexCount];
            for (int i = 0; i < indexCount; i++)
            {
                int index = reader.ReadUInt16();
                if (index >= vertexCount)
                    throw new InvalidDataException("Track index exceeds vertex count.");
                triangles[i] = index;
            }

            var mesh = new Mesh
            {
                name = "Monza Diagnostic Surface",
                indexFormat = IndexFormat.UInt16
            };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private PhysicMaterial TrackPhysicsMaterial
        {
            get
            {
                if (_trackPhysicsMaterial != null)
                    return _trackPhysicsMaterial;

                _trackPhysicsMaterial = new PhysicMaterial("Monza Track Grip")
                {
                    dynamicFriction = 0.8f,
                    staticFriction = 0.8f,
                    bounciness = 0f,
                    frictionCombine = PhysicMaterialCombine.Average,
                    bounceCombine = PhysicMaterialCombine.Minimum
                };
                return _trackPhysicsMaterial;
            }
        }

        private static Material CreateMaterial(string name, Color color, float smoothness)
        {
            Shader? shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("No compatible track shader is available.");

            var material = new Material(shader) { name = name };

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else
                material.color = color;

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);

            return material;
        }

        private static int ResolveLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 ? layer : 0;
        }

        public void Shutdown()
        {
            if (_shuttingDown)
                return;

            _shuttingDown = true;

            try
            {
                if (_hasReturnPosition)
                {
                    var car = VehicleHelper.GetCurrentVehicleBase() as CarController;
                    if (car != null)
                    {
                        TeleportVehicle(car, _returnPosition, _returnRotation);
                        _logger?.Info(
                            "[MonzaTestTrack] Returned current vehicle before unloading the test site.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex);
            }

            RestoreTrackRuntimeState();
            _hasReturnPosition = false;

            if (_site != null)
            {
                Destroy(_site);
                _site = null;
            }

            if (_trackMesh != null)
            {
                Destroy(_trackMesh);
                _trackMesh = null;
            }

            if (_trackMaterial != null)
            {
                Destroy(_trackMaterial);
                _trackMaterial = null;
            }

            if (_trackPhysicsMaterial != null)
            {
                Destroy(_trackPhysicsMaterial);
                _trackPhysicsMaterial = null;
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
