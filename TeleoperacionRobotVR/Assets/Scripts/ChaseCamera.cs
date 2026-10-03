using UnityEngine;

/// Cámara que sigue al carro y se orienta con la inclinación de la IMU (MPU6050) del control.
///
///   - Siempre va ligada al carro: se mueve y gira con él (detrás, a cierta distancia y altura).
///   - Pitch de la IMU -> sube / baja la cámara alrededor del carro (ángulo de elevación).
///   - Roll  de la IMU -> rodea el carro hacia la izquierda / derecha (ángulo horizontal).
///   - Si la cámara choca con una pared u obstáculo, se acerca al carro para no atravesarlo.
///
/// Mapeo:  elevación = elevaciónBase + kP · pitch      (limitada a [minElev, maxElev])
///         azimut    = yawCarro      + kR · roll       (limitado a ±maxOrbit)
///         posición  = carro + R(elevación, azimut) · (0, 0, -distancia)
/// Tecla V: alterna entre esta cámara y la vista en primera persona del avatar.
public class ChaseCamera : MonoBehaviour
{
    public Transform target;
    [Tooltip("Altura del punto al que mira la cámara, sobre el carro (m).")]
    public float targetHeight = 0.3f;
    public float distance = 2.2f;

    [Header("Inclinación de la IMU -> cámara")]
    [Tooltip("Elevación de la cámara con la IMU en reposo (°).")]
    public float baseElevation = 22f;
    [Tooltip("Grados de elevación por cada grado de pitch.")]
    public float pitchGain = 1.2f;
    [Tooltip("Grados de giro alrededor del carro por cada grado de roll.")]
    public float rollGain = 3f;
    public float minElevation = 3f;
    public float maxElevation = 80f;
    public float maxOrbit = 150f;
    [Tooltip("Zona muerta de la IMU (°), evita temblor con la mano quieta.")]
    public float imuDeadZone = 2f;
    public bool invertPitch = false;
    public bool invertRoll = false;

    [Header("Suavizado")]
    [Tooltip("Mayor = la cámara responde más rápido a la IMU y al carro.")]
    public float followSharpness = 8f;
    public float collisionRadius = 0.15f;

    public static ChaseCamera Instance { get; private set; }
    public bool Active => cam && cam.enabled;

    Camera cam;
    Camera avatarCam;
    AudioListener avatarListener;
    float elevation, orbit;
    bool placed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<ChaseCamera>() != null) return;
        var robot = FindAnyObjectByType<RobotController>();
        if (robot == null) return;
        var go = new GameObject("CamaraSeguimiento");
        go.AddComponent<Camera>();
        go.AddComponent<ChaseCamera>().target = robot.transform;
    }

    void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.fieldOfView = 65f;
        elevation = baseElevation;

        // La cámara del avatar queda disponible con la tecla V
        var avatar = FindAnyObjectByType<AvatarController>();
        if (avatar && avatar.head)
        {
            avatarCam = avatar.head.GetComponent<Camera>();
            avatarListener = avatar.head.GetComponent<AudioListener>();
        }
        SetActive(true);
    }

    void SetActive(bool chase)
    {
        cam.enabled = chase;
        gameObject.tag = chase ? "MainCamera" : "Untagged";
        var listener = GetComponent<AudioListener>();
        if (chase && listener == null) listener = gameObject.AddComponent<AudioListener>();
        if (listener) listener.enabled = chase;
        if (avatarCam)
        {
            avatarCam.enabled = !chase;
            avatarCam.gameObject.tag = chase ? "Untagged" : "MainCamera";
        }
        if (avatarListener) avatarListener.enabled = !chase;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.V)) SetActive(!cam.enabled);
    }

    static float Dead(float v, float dz) => Mathf.Abs(v) < dz ? 0f : v - Mathf.Sign(v) * dz;

    void LateUpdate()
    {
        if (!target || !cam.enabled) return;

        var link = HapticLink.Instance;
        float p = link ? Dead(link.Pitch, imuDeadZone) : 0f;
        float r = link ? Dead(link.Roll, imuDeadZone) : 0f;
        if (invertPitch) p = -p;
        if (invertRoll) r = -r;

        float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        float targetElev = Mathf.Clamp(baseElevation + pitchGain * p, minElevation, maxElevation);
        float targetOrbit = Mathf.Clamp(rollGain * r, -maxOrbit, maxOrbit);
        elevation = Mathf.Lerp(elevation, targetElev, k);
        orbit = Mathf.Lerp(orbit, targetOrbit, k);

        Vector3 focus = target.position + Vector3.up * targetHeight;
        float yaw = target.eulerAngles.y + orbit;   // ligada al giro del carro
        Quaternion rot = Quaternion.Euler(elevation, yaw, 0f);
        Vector3 desired = focus + rot * new Vector3(0f, 0f, -distance);

        // Evita atravesar paredes / obstáculos (ignora triggers y al propio carro)
        Vector3 dir = desired - focus;
        float d = dir.magnitude;
        foreach (var hit in Physics.SphereCastAll(focus, collisionRadius, dir / d, d, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.rigidbody && hit.rigidbody.transform == target) continue;
            if (hit.transform.IsChildOf(target)) continue;
            if (hit.distance < d) d = Mathf.Max(0.3f, hit.distance);
        }
        desired = focus + dir.normalized * d;

        // Posición casi rígida respecto al carro (va ligada a él); el primer cuadro salta directo
        transform.position = placed ? Vector3.Lerp(transform.position, desired, Mathf.Max(k, 0.35f)) : desired;
        placed = true;
        transform.rotation = Quaternion.LookRotation(focus - transform.position, Vector3.up);

        // Sacudida al chocar (decae rápido)
        if (shake > 0.0005f)
        {
            float t = Time.time * 40f;
            transform.position += (transform.right * (Mathf.PerlinNoise(t, 0f) - 0.5f) +
                                   transform.up * (Mathf.PerlinNoise(0f, t) - 0.5f)) * (2f * shake);
            shake *= Mathf.Exp(-9f * Time.deltaTime);
        }
    }

    float shake;

    /// Sacude la cámara (m). La llama RoverFX cuando el rover choca.
    public void Shake(float amount)
    {
        shake = Mathf.Max(shake, amount);
    }
}
