#nullable enable

using BAModAPI;
using BusinessLayoutSets;
using UnityEngine;

/// <summary>
/// Starts native warehouse entry when the Jesko's low front reaches the
/// physical door before the native vehicle trigger. Unlike the former Porsche
/// approach trigger, this reacts only to the exact entrance collider touched
/// by the car and therefore cannot select an adjacent garage door.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
internal sealed class KoenigseggJeskoWarehouseEntryController : MonoBehaviour
{
    private const float RetryDelaySeconds = 0.5f;

    private VehicleController? vehicle;
    private ModContext? context;
    private DriveInEntrance? suppressedEntrance;
    private float nextAttemptTime;
    private float nextSuppressionLogTime;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
    }

    internal void SuppressEntrance(DriveInEntrance entrance, string reason)
    {
        if (entrance == null)
            return;

        suppressedEntrance = entrance;
        KoenigseggJeskoDiagnostics.WarehouseInfo(
            context,
            $"KoenigseggJesko warehouse-entry: suppressing exact-door fallback for " +
            $"entrance='{entrance.name}', reason={reason}.");
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
            $"KoenigseggJesko warehouse-entry: restoring exact-door fallback for " +
            $"entrance='{suppressedEntrance.name}', reason={reason}.");
        suppressedEntrance = null;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (vehicle == null || !vehicle.controlledByPlayer ||
            collision == null || collision.collider == null ||
            Time.unscaledTime < nextAttemptTime)
        {
            return;
        }

        var entrance = collision.collider.GetComponentInParent<DriveInEntrance>();
        if (entrance == null)
            return;

        if (entrance == suppressedEntrance)
        {
            if (Time.unscaledTime >= nextSuppressionLogTime)
            {
                nextSuppressionLogTime = Time.unscaledTime + RetryDelaySeconds;
                KoenigseggJeskoDiagnostics.WarehouseInfo(
                    context,
                    $"KoenigseggJesko warehouse-entry: exact-door fallback suppressed during " +
                    $"exit; vehicle={vehicle.GetInstanceID()}, entrance='{entrance.name}', " +
                    $"collider='{collision.collider.name}'.");
            }
            return;
        }

        var car = vehicle as CarController ?? vehicle.GetComponent<CarController>();
        if (car == null || InstanceBehavior<GameManager>.Instance?.selectedVehicle != car)
            return;

        nextAttemptTime = Time.unscaledTime + RetryDelaySeconds;
        KoenigseggJeskoDiagnostics.WarehouseInfo(
            context,
            $"KoenigseggJesko warehouse-entry: exact physical door collision invokes native " +
            $"entry; vehicle={vehicle.GetInstanceID()}, entrance='{entrance.name}', " +
            $"collider='{collision.collider.name}', relativeVelocity={collision.relativeVelocity}.");
        entrance.TryToEnterWithCar(car);
    }

    private void OnDisable()
    {
        nextAttemptTime = 0f;
        nextSuppressionLogTime = 0f;
        suppressedEntrance = null;
    }
}
