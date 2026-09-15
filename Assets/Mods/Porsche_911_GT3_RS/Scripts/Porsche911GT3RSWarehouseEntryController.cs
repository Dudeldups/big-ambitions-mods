#nullable enable

using UnityEngine;
using BAModAPI;

/// <summary>
/// Invokes the native warehouse-entry path if the physical garage door is
/// reached before the vehicle-layer trigger can receive the Porsche collider.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
internal sealed class Porsche911GT3RSWarehouseEntryController : MonoBehaviour
{
    private const float RetryDelaySeconds = .5f;

    private VehicleController? vehicle;
    private ModContext? context;
    private DriveInEntrance? suppressedEntrance;
    private float nextAttemptTime;

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
        Porsche911GT3RSDiagnostics.WarehouseExitInfo(
            context,
            $"Porsche911GT3RS warehouse-entry: suppressing physical-door fallback for " +
            $"entrance='{entrance.name}', reason={reason}.");
    }

    internal void ClearSuppressedEntrance(DriveInEntrance? entrance, string reason)
    {
        if (suppressedEntrance == null ||
            (entrance != null && suppressedEntrance != entrance))
        {
            return;
        }

        Porsche911GT3RSDiagnostics.WarehouseExitInfo(
            context,
            $"Porsche911GT3RS warehouse-entry: restoring physical-door fallback for " +
            $"entrance='{suppressedEntrance.name}', reason={reason}.");
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
            Porsche911GT3RSDiagnostics.WarehouseExitInfo(
                context,
                $"Porsche911GT3RS warehouse-entry: physical-door fallback suppressed during " +
                $"warehouse exit; instance={vehicle.GetInstanceID()}, entrance='{entrance.name}', " +
                $"vehiclePosition={vehicle.transform.position}, collider='{collision.collider.name}', " +
                $"relativeVelocity={collision.relativeVelocity}.");
            return;
        }

        var car = vehicle as CarController ?? vehicle.GetComponent<CarController>();
        if (car == null || InstanceBehavior<GameManager>.Instance?.selectedVehicle != car)
            return;

        Porsche911GT3RSDiagnostics.WarehouseExitInfo(
            context,
            $"Porsche911GT3RS warehouse-entry: physical door collision invokes native entry; " +
            $"instance={vehicle.GetInstanceID()}, entrance='{entrance.name}', " +
            $"vehiclePosition={vehicle.transform.position}, collider='{collision.collider.name}', " +
            $"relativeVelocity={collision.relativeVelocity}.");
        nextAttemptTime = Time.unscaledTime + RetryDelaySeconds;
        entrance.TryToEnterWithCar(car);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (vehicle == null || !vehicle.controlledByPlayer || other == null)
            return;

        var enterTrigger = other.GetComponentInParent<DriveInEntranceEnterTrigger>();
        if (enterTrigger == null)
            return;

        Porsche911GT3RSDiagnostics.WarehouseExitInfo(
            context,
            $"Porsche911GT3RS warehouse-entry: Porsche received entry-trigger contact; " +
            $"instance={vehicle.GetInstanceID()}, trigger='{other.name}', " +
            $"vehiclePosition={vehicle.transform.position}.");
    }

    private void OnDisable()
    {
        nextAttemptTime = 0f;
        suppressedEntrance = null;
    }
}
