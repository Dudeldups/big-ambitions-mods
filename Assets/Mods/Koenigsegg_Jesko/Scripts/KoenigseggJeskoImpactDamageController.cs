#nullable enable
using System.Collections.Generic;
using BAModAPI;
using NWH.VehiclePhysics2.Damage;
using UnityEngine;

// Observe contacts independently of the visual-deformation cooldown. Native NWH
// damage accepts only the first Enter in 0.8 seconds and measures its impulse;
// a splitter/bridge scrape can consume that entry before the main impact.
[DisallowMultipleComponent]
internal sealed class KoenigseggJeskoImpactDamageController : MonoBehaviour
{
    private const float Tolerance = 0.0001f;
    private readonly HashSet<Collider> impactColliders = new HashSet<Collider>();
    private VehicleController? vehicle;
    private DamageHandler? handler;
    private Rigidbody? body;
    private ModContext? context;
    private float preStepDamage;
    private float lastObservedDamage;
    private float windowEnd = -1f;
    private float windowBaseline;
    private float strongestImpact;
    private bool pending;
    private int correctionLogs;
    private string contactName = "";
    private float contactSpeed;
    private float contactImpulse;

    internal void Initialize(VehicleController owner, DamageHandler damage, ModContext? modContext)
    {
        vehicle = owner;
        handler = damage;
        body = owner.GetComponent<Rigidbody>();
        context = modContext;
        preStepDamage = lastObservedDamage = damage.Damage;
        ClearImpact();
    }

    private bool CanApplyDamage => vehicle?.vehicleInstance != null &&
        handler != null && handler.isActiveAndEnabled && !handler.visualOnly &&
        handler.damageIntensity > 0f && body != null && !body.isKinematic &&
        !(SaveGameManager.Current != null && SaveGameManager.Current.gameVariables.disableVehicleDamage);

    private void FixedUpdate()
    {
        if (handler == null) return;
        // A repair or reload must discard the old impact target immediately.
        if (!CanApplyDamage || handler.Damage + Tolerance < lastObservedDamage)
            ClearImpact();
        preStepDamage = lastObservedDamage = handler.Damage;
    }

    private void OnCollisionEnter(Collision collision) => Observe(collision, true);
    private void OnCollisionStay(Collision collision) => Observe(collision, false);
    private void OnDisable() => ClearImpact();

    private void Observe(Collision collision, bool entering)
    {
        if (!CanApplyDamage || collision.contactCount == 0 ||
            !DamageHandler.IsCollisionValid(collision)) return;
        foreach (var tag in handler!.collisionIgnoreTags)
            if (collision.collider.CompareTag(tag)) return;

        var now = Time.realtimeSinceStartup;
        if (now > windowEnd)
        {
            // Do not turn resting interpenetration or sustained pushing into
            // repeated damage. Stay can only complete a real Enter's short window.
            if (!entering) return;
            ClearImpact();
            windowBaseline = Mathf.Min(preStepDamage, handler.Damage);
            windowEnd = now + handler.collisionTimeout;
        }
        if (entering) impactColliders.Add(collision.collider);
        else if (!impactColliders.Contains(collision.collider)) return;

        var closingSpeed = 0f;
        for (var i = 0; i < collision.contactCount; i++)
            closingSpeed = Mathf.Max(closingSpeed,
                ClosingSpeed(collision.relativeVelocity, collision.GetContact(i).normal));
        var other = collision.rigidbody;
        var expected = ImpactDamage(closingSpeed, collision.impulse.magnitude,
            body!.mass, other != null && !other.isKinematic ? other.mass : 0f,
            Time.fixedDeltaTime, handler.damageIntensity, handler.decelerationThreshold / 100f);
        if (expected <= strongestImpact) return;
        strongestImpact = expected;
        pending = true;
        contactName = collision.collider.name;
        contactSpeed = closingSpeed;
        contactImpulse = collision.impulse.magnitude;
    }

    private void LateUpdate()
    {
        if (!pending || handler == null) return;
        pending = false;
        if (!CanApplyDamage || handler.Damage + Tolerance < lastObservedDamage)
        {
            ClearImpact();
            return;
        }
        // Native callbacks have now run. Credit ALL damage they already applied,
        // regardless of callback order, and never add the whole impact again.
        var current = handler.Damage;
        var target = ReconcileDamage(current, windowBaseline, strongestImpact);
        if (target > current + Tolerance)
        {
            if (vehicle is CarController car) car.SetDamage(target);
            else
            {
                handler.SetDamage(target);
                vehicle!.vehicleInstance.damage = target;
            }
            GlobalEvents.onVehicleVariablesChanged?.Invoke();
            if (correctionLogs++ < 6)
                context?.Logger.Warn($"KoenigseggJesko impact damage recovered vehicle={vehicle!.GetInstanceID()} " +
                    $"contact='{contactName}' closingSpeed={contactSpeed:0.00}mps impulse={contactImpulse:0.0}Ns " +
                    $"baseline={windowBaseline:0.0000} native={current:0.0000} corrected={target:0.0000}; " +
                    "strongest contact in native cooldown, existing damage credited.");
        }
        lastObservedDamage = handler.Damage;
    }

    // Same native impulse-to-condition scale and the existing 0.8 intensity.
    // When overlap recovery yields a tiny solver impulse, use normal closing
    // speed and reduced mass as a conservative, inelastic impact estimate.
    // Tangential scraping and separating motion do not contribute to that floor.
    internal static float ClosingSpeed(Vector3 relativeVelocity, Vector3 normal) =>
        // Unity's Collision reports other-relative-to-self velocity: approach
        // projects positively onto the receiving collider's contact normal.
        Mathf.Max(0f, Vector3.Dot(relativeVelocity, normal.normalized));

    internal static float ImpactDamage(float closingSpeed, float impulse, float mass,
        float otherDynamicMass, float fixedStep, float intensity, float threshold)
    {
        if (mass <= 0f || fixedStep <= 0f || intensity <= 0f) return 0f;
        var impulseDeltaV = Mathf.Max(0f, impulse) / mass;
        if (closingSpeed < threshold && impulseDeltaV < threshold) return 0f;
        var massFraction = otherDynamicMass > 0f ? otherDynamicMass / (mass + otherDynamicMass) : 1f;
        var deltaV = Mathf.Max(impulseDeltaV, Mathf.Max(0f, closingSpeed) * massFraction);
        return Mathf.Clamp01(deltaV / (fixedStep * 10f) * Mathf.Clamp(intensity, 0f, 0.99f) * 0.005f);
    }

    internal static float ReconcileDamage(float current, float baseline, float strongest) =>
        Mathf.Clamp01(Mathf.Max(current, baseline + strongest));

    private void ClearImpact()
    {
        pending = false;
        windowEnd = -1f;
        strongestImpact = 0f;
        impactColliders.Clear();
    }
}
