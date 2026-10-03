using UnityEngine;
using UnityEngine.XR;

/// Avatar en primera persona.
///   Orientación de la cabeza: IMU del visor VR (Meta Quest 3), o ratón (clic derecho) / Q / E en modo escritorio.
///   Avance / retroceso: W / S en la dirección a la que se mira. Se usa con la tecla V (vista en primera persona).
[RequireComponent(typeof(CharacterController))]
public class AvatarController : MonoBehaviour
{
    public Transform head;
    public float walkSpeed = 1.4f;
    public float mouseSensitivity = 3f;
    [Tooltip("Inclinación inicial de la vista en modo escritorio (°).")]
    public float initialPitch = 20f;
    public float gravity = -9.81f;
    public bool wallHaptics = true;
    public float hapticCooldown = 1f;

    CharacterController cc;
    float yaw, pitch, verticalSpeed, nextHaptic;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        yaw = transform.eulerAngles.y;
        pitch = initialPitch;

        // El cuerpo del avatar no se dibuja en su propia cámara (sí en vistas externas)
        const int bodyLayer = 30;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.gameObject.layer = bodyLayer;
        var cam = head ? head.GetComponent<Camera>() : null;
        if (cam) cam.cullingMask &= ~(1 << bodyLayer);
    }

    void Update()
    {
        bool vr = XRSettings.isDeviceActive;

        if (!vr)
        {
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -80f, 80f);
            }
            if (Input.GetKey(KeyCode.Q)) yaw -= 90f * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) yaw += 90f * Time.deltaTime;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        var link = HapticLink.Instance;
        float dir = 0f;
        if (link)
        {
            if (link.Forward) dir += 1f;
            if (link.Backward) dir -= 1f;
        }

        Vector3 fwd = head.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : transform.forward;

        verticalSpeed = cc.isGrounded ? -1f : verticalSpeed + gravity * Time.deltaTime;
        cc.Move((fwd * dir * walkSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!wallHaptics || Time.time < nextHaptic) return;
        var s = hit.collider.GetComponentInParent<HapticSurface>();
        if (s == null || s.pattern != HapticPattern.ParedLimite) return;
        nextHaptic = Time.time + hapticCooldown;
        if (HapticLink.Instance) HapticLink.Instance.Vibrate(HapticPattern.ParedLimite);
        if (TeleopMetrics.Instance) TeleopMetrics.Instance.RegisterEvent("avatar_limite", hit.collider.name);
    }
}
