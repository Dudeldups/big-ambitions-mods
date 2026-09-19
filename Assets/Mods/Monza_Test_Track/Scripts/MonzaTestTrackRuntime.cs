#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using BAModAPI;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace MonzaTestTrack
{
    public sealed class MonzaTestTrackRuntime : MonoBehaviour
    {
        private const string DataRelativePath = "Config/track_surface.mtt";

        // First prototype: isolated placement away from the normal city.
        // The mesh itself is about 2.31 km x 1.43 km.
        private static readonly Vector3 SiteCentre = new Vector3(2800f, 30f, -2000f);
        private static readonly Vector3 SpawnProbeLocal = new Vector3(354f, 80f, 128f);

        private IModLogger? _logger;
        private string? _modRootPath;
        private GameObject? _site;
        private Mesh? _trackMesh;
        private Material? _trackMaterial;
        private Material? _safetyMaterial;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation = Quaternion.Euler(0f, 90f, 0f);

        private bool _hasReturnPosition;
        private Vector3 _returnPosition;
        private Quaternion _returnRotation;

        public void Initialize(string modRootPath, IModLogger logger)
        {
            _modRootPath = modRootPath;
            _logger = logger;

            try
            {
                BuildPrototype();
                _logger.Info(
                    "[MonzaTestTrack] Prototype ready. Drive a vehicle and press F7 to teleport to Monza; " +
                    "press F7 again while on the test site to return.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                Shutdown();
            }
        }

        private void Update()
        {
            if (_site == null || !Input.GetKeyDown(KeyCode.F7))
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

            var dataPath = Path.Combine(
                _modRootPath,
                DataRelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(dataPath))
                throw new FileNotFoundException("Prototype track data is missing.", dataPath);

            _trackMesh = LoadTrackMesh(dataPath);

            _site = new GameObject("MonzaTestTrack.Site");
            _site.transform.SetParent(transform, false);
            _site.transform.position = SiteCentre;

            var track = new GameObject("COL_Track");
            track.transform.SetParent(_site.transform, false);
            track.layer = ResolveLayer("Ground");

            var filter = track.AddComponent<MeshFilter>();
            filter.sharedMesh = _trackMesh;

            _trackMaterial = CreateMaterial(
                "Monza Prototype Asphalt",
                new Color(0.16f, 0.17f, 0.18f, 1f),
                0.23f);

            var renderer = track.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _trackMaterial;

            var collider = track.AddComponent<MeshCollider>();
            collider.sharedMesh = _trackMesh;
            collider.convex = false;
            collider.cookingOptions =
                MeshColliderCookingOptions.CookForFasterSimulation |
                MeshColliderCookingOptions.EnableMeshCleaning |
                MeshColliderCookingOptions.WeldColocatedVertices |
                MeshColliderCookingOptions.UseFastMidphase;

            BuildSafetyBase();
            Physics.SyncTransforms();

            if (!TryResolveSpawn(collider, out _spawnPosition))
                throw new InvalidOperationException("Could not resolve a drivable spawn point on the Monza prototype.");

            _logger?.Info(
                "[MonzaTestTrack] Placed prototype at " + SiteCentre +
                "; mesh=" + _trackMesh.vertexCount + " vertices / " +
                (_trackMesh.triangles.Length / 3) + " triangles; spawn=" + _spawnPosition + ".");
        }

        private void BuildSafetyBase()
        {
            if (_site == null)
                return;

            var safety = GameObject.CreatePrimitive(PrimitiveType.Cube);
            safety.name = "GroundSafetyBase";
            safety.transform.SetParent(_site.transform, false);
            safety.transform.localPosition = new Vector3(0f, -3f, 0f);
            safety.transform.localScale = new Vector3(2400f, 2f, 1500f);
            safety.layer = ResolveLayer("Ground");

            _safetyMaterial = CreateMaterial(
                "Monza Prototype Safety Base",
                new Color(0.08f, 0.085f, 0.09f, 1f),
                0.08f);

            var renderer = safety.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = _safetyMaterial;
        }

        private bool TryResolveSpawn(MeshCollider trackCollider, out Vector3 position)
        {
            var origin = _site!.transform.TransformPoint(SpawnProbeLocal);
            if (trackCollider.Raycast(new Ray(origin, Vector3.down), out var hit, 180f))
            {
                position = hit.point + Vector3.up * 0.15f;
                return true;
            }

            // Fallback: search a small grid around the intended start/finish area.
            for (var radius = 0f; radius <= 240f; radius += 20f)
            {
                for (var step = 0; step < 16; step++)
                {
                    var angle = step * Mathf.PI * 2f / 16f;
                    var local = SpawnProbeLocal +
                                new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    origin = _site.transform.TransformPoint(local);

                    if (trackCollider.Raycast(new Ray(origin, Vector3.down), out hit, 180f))
                    {
                        position = hit.point + Vector3.up * 0.15f;
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

            var onTrack = Vector2.Distance(
                new Vector2(car.transform.position.x, car.transform.position.z),
                new Vector2(SiteCentre.x, SiteCentre.z)) < 1800f;

            if (onTrack && _hasReturnPosition)
            {
                TeleportVehicle(car, _returnPosition, _returnRotation);
                _hasReturnPosition = false;
                _logger?.Info("[MonzaTestTrack] Returned vehicle to city position.");
                return;
            }

            _returnPosition = car.transform.position;
            _returnRotation = Quaternion.Euler(0f, car.transform.eulerAngles.y, 0f);
            _hasReturnPosition = true;

            TeleportVehicle(car, _spawnPosition, _spawnRotation);
            _logger?.Info("[MonzaTestTrack] Teleported current vehicle to Monza prototype.");
        }

        private static void TeleportVehicle(CarController car, Vector3 position, Quaternion rotation)
        {
            var body = car.vehicleController?.vehicleRigidbody;
            if (body != null && !body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            VehicleHelper.TeleportVehicle(car, position, rotation);
            car.UpdateNavMeshTargets();
            car.SavePosition();

            if (body != null && !body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
        }

        private static Mesh LoadTrackMesh(string path)
        {
            var encoded = File.ReadAllText(path).Trim();
            var compressed = Convert.FromBase64String(encoded);

            using var compressedStream = new MemoryStream(compressed, false);
            using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);

            var magic = new string(reader.ReadChars(4));
            if (magic != "MTT1")
                throw new InvalidDataException("Unexpected Monza track data header.");

            var version = reader.ReadUInt16();
            if (version != 1)
                throw new InvalidDataException("Unsupported Monza track data version: " + version);

            var vertexCount = reader.ReadInt32();
            var indexCount = reader.ReadInt32();

            if (vertexCount <= 0 || vertexCount > 65000 || indexCount <= 0 || indexCount % 3 != 0)
                throw new InvalidDataException(
                    "Invalid Monza mesh counts: vertices=" + vertexCount + ", indices=" + indexCount);

            var min = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var max = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var size = max - min;

            var vertices = new Vector3[vertexCount];
            for (var i = 0; i < vertexCount; i++)
            {
                var x = reader.ReadUInt16() / 65535f;
                var y = reader.ReadUInt16() / 65535f;
                var z = reader.ReadUInt16() / 65535f;
                vertices[i] = min + Vector3.Scale(size, new Vector3(x, y, z));
            }

            var triangles = new int[indexCount];
            for (var i = 0; i < indexCount; i++)
            {
                var index = reader.ReadUInt16();
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

        private static Material CreateMaterial(string name, Color color, float smoothness)
        {
            var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
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
            var layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 ? layer : 0;
        }

        public void Shutdown()
        {
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
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
