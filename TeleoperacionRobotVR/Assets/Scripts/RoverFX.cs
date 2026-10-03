using UnityEngine;

/// Efectos visuales del rover: polvo en las ruedas según la velocidad, chispas y sacudida de cámara al chocar.
[RequireComponent(typeof(RobotController))]
public class RoverFX : MonoBehaviour
{
    public ParticleSystem[] wheelDust;
    public float dustPerSpeed = 40f;
    public ParticleSystem impactSparks;
    public Light[] headlights;

    RobotController robot;

    void Awake()
    {
        robot = GetComponent<RobotController>();
    }

    void Update()
    {
        float speed = Mathf.Abs(robot.LinearSpeed) + Mathf.Abs(robot.AngularSpeed) * 0.004f;
        foreach (var ps in wheelDust)
        {
            if (!ps) continue;
            var em = ps.emission;
            em.rateOverTime = speed * dustPerSpeed;
        }
    }

    void OnCollisionEnter(Collision c)
    {
        var surface = c.collider.GetComponentInParent<HapticSurface>();
        if (surface == null || c.contactCount == 0) return;

        var contact = c.GetContact(0);
        if (impactSparks)
        {
            impactSparks.transform.SetPositionAndRotation(contact.point, Quaternion.LookRotation(contact.normal));
            impactSparks.Emit(surface.pattern == HapticPattern.ParedLimite ? 40 : 22);
        }

        float strength = surface.pattern == HapticPattern.ParedLimite ? 0.12f
                       : surface.pattern == HapticPattern.Obstaculo ? 0.08f : 0.03f;
        if (ChaseCamera.Instance) ChaseCamera.Instance.Shake(strength);
    }
}
