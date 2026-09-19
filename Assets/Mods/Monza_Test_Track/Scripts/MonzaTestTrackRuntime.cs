#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using BAModAPI;
using Helpers;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace MonzaTestTrack
{
    public sealed class MonzaTestTrackRuntime : MonoBehaviour
    {
        private const string SurfaceDataPattern = "track_surface.clean.*.txt";
        // First prototype: keep the entire circuit away from the normal city.
        // The extracted driveable asphalt is about 2.31 km x 1.12 km.
        private static readonly Vector3 SiteCentre = new Vector3(5000f, 80f, 0f);

        // Start/finish straight candidate derived from the source GLB.
        private static readonly Vector3 SpawnProbeLocal = new Vector3(618.5f, 80f, 274.05f);
        private static readonly Quaternion SpawnRotation = Quaternion.Euler(0f, 90f, 0f);

        private IModLogger? _logger;
        private string? _modRootPath;
        private GameObject? _site;
        private Mesh? _trackMesh;
        private Material? _trackMaterial;
        private Material? _safetyMaterial;
        private PhysicMaterial? _trackPhysicsMaterial;
        private Vector3 _spawnPosition;

        private bool _hasReturnPosition;
        private Vector3 _returnPosition;
        private Quaternion _returnRotation;
        private bool _shuttingDown;

        public void Initialize(string modRootPath, IModLogger logger)
        {
            _modRootPath = modRootPath;
            _logger = logger;

            try
            {
                BuildPrototype();
                _logger.Info(
                    "[MonzaTestTrack] Prototype ready. Enter a vehicle and press F7 to teleport to Monza; " +
                    "press F7 again to return.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                Shutdown();
            }
        }

        private void Update()
        {
            if (_site == null || Keyboard.current?.f7Key.wasPressedThisFrame != true)
                return;

            try
            {
                ToggleVehicleTeleport();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex);
            }
        }

        private void BuildPrototype()
        {
            if (string.IsNullOrEmpty(_modRootPath))
                throw new InvalidOperationException("Mod root path is unavailable.");

            string configDirectory = Path.Combine(_modRootPath, "Config");
            if (!Directory.Exists(configDirectory))
                throw new DirectoryNotFoundException("Monza prototype Config folder is missing: " + configDirectory);

            string[] parts = Directory.GetFiles(configDirectory, SurfaceDataPattern);
            Array.Sort(parts, StringComparer.OrdinalIgnoreCase);

            if (parts.Length == 0)
                throw new FileNotFoundException(
                    "Cleaned Monza prototype surface data was not installed in " + configDirectory + ".");

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
                "Monza Prototype Asphalt",
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

            BuildSafetyBase();
            Physics.SyncTransforms();

            if (!TryResolveSpawn(collider, out _spawnPosition))
                throw new InvalidOperationException(
                    "Could not resolve a driveable spawn point on the Monza prototype.");

            Bounds bounds = _trackMesh.bounds;
            _logger?.Info(
                "[MonzaTestTrack] Placed prototype at " + SiteCentre +
                "; asphalt=" + _trackMesh.vertexCount + " vertices / " +
                (_trackMesh.triangles.Length / 3) + " triangles" +
                "; size=" + bounds.size.x.ToString("0.0") + " x " +
                bounds.size.z.ToString("0.0") + " m" +
                "; spawn=" + _spawnPosition + ".");
        }

        private void BuildSafetyBase()
        {
            if (_site == null)
                return;

            var safety = GameObject.CreatePrimitive(PrimitiveType.Cube);
            safety.name = "GroundSafetyBase";
            safety.transform.SetParent(_site.transform, false);
            safety.transform.localPosition = new Vector3(0f, -3f, 0f);
            safety.transform.localScale = new Vector3(2500f, 2f, 1250f);
            safety.layer = ResolveLayer("Ground");
            safety.isStatic = true;

            _safetyMaterial = CreateMaterial(
                "Monza Prototype Safety Base",
                new Color(0.08f, 0.085f, 0.09f, 1f),
                0.08f);

            var renderer = safety.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = _safetyMaterial;

            var collider = safety.GetComponent<BoxCollider>();
            if (collider != null)
                collider.sharedMaterial = TrackPhysicsMaterial;
        }

        private bool TryResolveSpawn(MeshCollider trackCollider, out Vector3 position)
        {
            Vector3 origin = _site!.transform.TransformPoint(SpawnProbeLocal);

            if (trackCollider.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, 180f))
            {
                position = hit.point + Vector3.up * 0.35f;
                return true;
            }

            // Fallback around the intended main straight in case the source mesh changes slightly.
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

            if (IsAtTestSite(car.transform.position) && _hasReturnPosition)
            {
                TeleportVehicle(car, _returnPosition, _returnRotation);
                _hasReturnPosition = false;
                _logger?.Info("[MonzaTestTrack] Returned current vehicle to its previous city position.");
                return;
            }

            _returnPosition = car.transform.position;
            _returnRotation = Quaternion.Euler(0f, car.transform.eulerAngles.y, 0f);
            _hasReturnPosition = true;

            TeleportVehicle(car, _spawnPosition, SpawnRotation);
            _logger?.Info("[MonzaTestTrack] Teleported current vehicle to the Monza prototype.");
        }

        private static bool IsAtTestSite(Vector3 position)
        {
            return Vector2.Distance(
                new Vector2(position.x, position.z),
                new Vector2(SiteCentre.x, SiteCentre.z)) < 1700f;
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
                name = "Monza Prototype Surface",
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

                _trackPhysicsMaterial = new PhysicMaterial("Monza Prototype Track Grip")
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
                    if (car != null && IsAtTestSite(car.transform.position))
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

            if (_safetyMaterial != null)
            {
                Destroy(_safetyMaterial);
                _safetyMaterial = null;
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
