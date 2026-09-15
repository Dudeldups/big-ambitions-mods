#nullable enable

using BAModAPI;
using UnityEngine;

/// <summary>
/// Invokes native warehouse entry when the Jesko's low front reaches the
/// physical door before the normal vehicle-layer trigger receives it.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
internal sealed class KoenigseggJeskoWarehouseEntryController : MonoBehaviour
{
    private const float RetryDelaySeconds = 0.5f;

    private VehicleController? vehicle;
    private ModContext? context;
    private BoxCollider? approachTrigger;
    private DriveInEntrance? suppressedEntrance;
    private float nextAttemptTime;
    private float nextSuppressionLogTime;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        EnsureApproachTrigger();
    }

    private void EnsureApproachTrigger()
    {
        if (approachTrigger != null)
            return;

        var vehicleLayer = gameObject.layer;
        foreach (var child in GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(child.name, "BodyCollider", System.StringComparison.Ordinal))
                continue;
            vehicleLayer = child.gameObject.layer;
            break;
        }

        var triggerObject = new GameObject("KoenigseggJesko_WarehouseApproachTrigger")
        {
            hideFlags = HideFlags.DontSave,
            layer = vehicleLayer,
        };
        triggerObject.transform.SetParent(transform, false);
        approachTrigger = triggerObject.AddComponent<BoxCollider>();
        approachTrigger.isTrigger = true;
        approachTrigger.center = new Vector3(0f, 0.58f, 0f);
        approachTrigger.size = new Vector3(1.90f, 1.05f, 6.40f);
    }

    internal void SuppressEntrance(DriveInEntrance entrance, string reason)
    {
        if (entrance == null)
            return;
        suppressedEntrance = entrance;
        KoenigseggJeskoDiagnostics.WarehouseInfo(
            context,
            $"KoenigseggJesko warehouse-entry: suppressed entrance='{entrance.name}', " +
            $"reason={reason}.");
    }

    internal void ClearSuppressedEntrance(DriveInEntrance? entrance, string reason)
    {
        if (suppressedEntrance == null ||
            (entrance != null && suppressedEntrance != entrance))
        {
            return;
        }

        KoenigseggJeskoDiagnostics.WarehouseInfo(
            context,
            $"KoenigseggJesko warehouse-entry: restored entrance='{suppressedEntrance.name}', " +
            $"reason={reason}.");
        suppressedEntrance = null;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (vehicle == null || !vehicle.controlledByPlayer ||
            Time.unscaledTime < nextAttemptTime || collision.collider == null)
        {
            return;
        }

        var entrance = collision.collider.GetComponentInParent<DriveInEntrance>();
        if (entrance == null)
            return;
        if (entrance == suppressedEntrance)
        {
            LogSuppressed(entrance, collision.collider.name);
            return;
        }

        TryEnter(entrance, collision.collider.name, "physical-door collision");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (vehicle == null || !vehicle.controlledByPlayer || other == null)
            return;

        var entrance = other.GetComponentInParent<DriveInEntrance>();
        if (entrance == null)
            return;
        if (entrance == suppressedEntrance)
        {
            LogSuppressed(entrance, other.name);
            return;
        }
        if (Time.unscaledTime < nextAttemptTime)
            return;

        TryEnter(entrance, other.name, "approach trigger");
    }

    private void TryEnter(DriveInEntrance entrance, string colliderName, string source)
    {
        var car = vehicle as CarController ?? vehicle?.GetComponent<CarController>();
        if (car == null || InstanceBehavior<GameManager>.Instance?.selectedVehicle != car)
            return;

        KoenigseggJeskoDiagnostics.WarehouseInfo(
            context,
            $"KoenigseggJesko warehouse-entry: {source} invokes native entry; " +
            $"vehicle={vehicle!.GetInstanceID()}, entrance='{entrance.name}', " +
            $"collider='{colliderName}'.");
        nextAttemptTime = Time.unscaledTime + RetryDelaySeconds;
        entrance.TryToEnterWithCar(car);
    }

    private void LogSuppressed(DriveInEntrance entrance, string colliderName)
    {
        if (Time.unscaledTime < nextSuppressionLogTime)
            return;
        nextSuppressionLogTime = Time.unscaledTime + RetryDelaySeconds;
        KoenigseggJeskoDiagnostics.WarehouseInfo(
            context,
            $"KoenigseggJesko warehouse-entry: ignored during exit guard; " +
            $"vehicle={vehicle!.GetInstanceID()}, entrance='{entrance.name}', " +
            $"collider='{colliderName}'.");
    }

    private void OnDisable()
    {
        nextAttemptTime = 0f;
        nextSuppressionLogTime = 0f;
        suppressedEntrance = null;
    }
}
