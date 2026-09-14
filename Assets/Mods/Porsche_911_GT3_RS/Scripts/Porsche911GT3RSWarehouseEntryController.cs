#nullable enable

using UnityEngine;

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
    private float nextAttemptTime;

    internal void Initialize(VehicleController controller)
    {
        vehicle = controller;
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

        var car = vehicle as CarController ?? vehicle.GetComponent<CarController>();
        if (car == null || InstanceBehavior<GameManager>.Instance?.selectedVehicle != car)
            return;

        nextAttemptTime = Time.unscaledTime + RetryDelaySeconds;
        entrance.TryToEnterWithCar(car);
    }

    private void OnDisable()
    {
        nextAttemptTime = 0f;
    }
}
