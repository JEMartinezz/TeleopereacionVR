using UnityEngine;

/// Robot móvil diferencial teleoperado con el joystick del control ESP32.
///   Joystick Y -> velocidad lineal (adelante / atrás)
///   Joystick X -> velocidad angular (giro izquierda / derecha)
///
/// Mapeo (modo proporcional), con j ∈ [-1, 1]:
///   u(j) = sign(j) · clamp01((|j| - j_muerta) / (1 - j_muerta))
///   v* = v_max · u(joyY)      ω* = ω_max · u(joyX)
///   v(k) = v(k-1) + (v* - v(k-1)) · (1 - e^(-λ·Δt))       (filtro de primer orden)
[RequireComponent(typeof(Rigidbody))]
public class RobotController : MonoBehaviour
{
    public enum MappingMode { Proporcional, Umbral }

    [Header("Mapeo de control (joystick)")]
    public MappingMode mode = MappingMode.Proporcional;
    public float maxLinearSpeed = 1.2f;      // m/s
    public float maxAngularSpeed = 90f;      // °/s
    [Tooltip("Zona muerta del joystick (0..1). Evita que el carro se mueva solo por el ruido del centro.")]
    [Range(0f, 0.5f)] public float joystickDeadZone = 0.12f;
    [Tooltip("Constante λ del filtro de primer orden (1/s). Mayor = respuesta más rápida.")]
    public float smoothing = 6f;
    public bool invertForward = false;
    public bool invertTurn = false;

    [Header("Háptica")]
    public float hapticCooldown = 0.35f;

    [Header("Visual")]
    public Transform[] leftWheels;
    public Transform[] rightWheels;
    public float wheelRadius = 0.12f;
    public float trackWidth = 0.5f;

    public float LinearSpeed { get; private set; }
    public float AngularSpeed { get; private set; }
    public bool HasMoved { get; private set; }

    Rigidbody rb;
    float nextHapticTime;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    public float Map(float j)
    {
        float a = Mathf.Abs(j);
        if (a < joystickDeadZone) return 0f;
        if (mode == MappingMode.Umbral) return Mathf.Sign(j);
        return Mathf.Sign(j) * Mathf.Clamp01((a - joystickDeadZone) / (1f - joystickDeadZone));
    }

    void FixedUpdate()
    {
        var link = HapticLink.Instance;
        float jy = link ? link.DriveY : 0f;
        float jx = link ? link.DriveX : 0f;
        if (invertForward) jy = -jy;
        if (invertTurn) jx = -jx;

        float targetV = maxLinearSpeed * Map(jy);
        float targetW = maxAngularSpeed * Map(jx);

        float k = 1f - Mathf.Exp(-smoothing * Time.fixedDeltaTime);
        LinearSpeed += (targetV - LinearSpeed) * k;
        AngularSpeed += (targetW - AngularSpeed) * k;
        if (Mathf.Abs(targetV) > 0.01f || Mathf.Abs(targetW) > 0.01f) HasMoved = true;

        Vector3 vel = transform.forward * LinearSpeed;
        vel.y = rb.linearVelocity.y;
        rb.linearVelocity = vel;
        rb.angularVelocity = Vector3.up * (AngularSpeed * Mathf.Deg2Rad);
    }

    void Update()
    {
        // Cinemática diferencial para animar las ruedas
        float w = AngularSpeed * Mathf.Deg2Rad;
        float vl = LinearSpeed + w * trackWidth * 0.5f;
        float vr = LinearSpeed - w * trackWidth * 0.5f;
        SpinWheels(leftWheels, vl);
        SpinWheels(rightWheels, vr);
    }

    void SpinWheels(Transform[] wheels, float v)
    {
        if (wheels == null) return;
        float deg = v / wheelRadius * Mathf.Rad2Deg * Time.deltaTime;
        foreach (var t in wheels) if (t) t.Rotate(Vector3.up, -deg, Space.Self); // eje local Y del cilindro = eje de la rueda
    }

    void OnCollisionEnter(Collision c)
    {
        var surface = c.collider.GetComponentInParent<HapticSurface>();
        if (surface == null || Time.time < nextHapticTime) return;
        nextHapticTime = Time.time + hapticCooldown;
        if (HapticLink.Instance) HapticLink.Instance.Vibrate(surface.pattern);
        if (TeleopMetrics.Instance) TeleopMetrics.Instance.RegisterCollision(surface.pattern, c.collider.name);
    }
}
