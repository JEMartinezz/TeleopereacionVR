using System.Collections.Generic;
using UnityEngine;

/// Garra del robot: el botón del joystick la cierra (agarra / attach el objeto manipulable entre los dedos)
/// y la vuelve a abrir (suelta / detach). Modo Mantener: cerrada solo mientras se presiona.
/// Va en el objeto que tiene el collider "trigger" de la zona de agarre.
public class RobotGripper : MonoBehaviour
{
    public enum GrabMode { Alternar, Mantener }

    [Tooltip("Alternar: una pulsación agarra y otra suelta. Mantener: agarra mientras el botón está presionado.")]
    public GrabMode grabMode = GrabMode.Alternar;
    public Rigidbody robotBody;
    public Transform leftFinger;
    public Transform rightFinger;
    public float fingerOpen = 0.2f;
    public float fingerClosed = 0.12f;
    public float fingerEmpty = 0.03f;

    public Rigidbody Held { get; private set; }
    public bool Closed { get; private set; }
    public static readonly HashSet<Rigidbody> AllHeld = new HashSet<Rigidbody>();

    readonly HashSet<Rigidbody> inZone = new HashSet<Rigidbody>();
    FixedJoint joint;
    bool prevGrab;
    Collider[] robotColliders;

    void Start()
    {
        robotColliders = robotBody.GetComponentsInChildren<Collider>();
    }

    void Update()
    {
        bool g = HapticLink.Instance && HapticLink.Instance.Grab;

        // Botón del joystick: la garra se cierra (y agarra el objeto que esté entre los dedos)
        if (grabMode == GrabMode.Alternar)
        {
            if (g && !prevGrab) Closed = !Closed;
        }
        else Closed = g;
        prevGrab = g;

        if (Closed && !Held) TryGrab();   // también agarra si el objeto entra con la garra cerrada
        else if (!Closed && Held) Release();

        // Por si el objeto se soltó por otra razón (joint roto, objeto destruido)
        if (Held && joint == null) Release();

        // Dedos: abiertos, cerrados sobre el objeto, o cerrados del todo si no hay objeto
        float target = !Closed ? fingerOpen : (Held ? fingerClosed : fingerEmpty);
        if (leftFinger) MoveFinger(leftFinger, -target);
        if (rightFinger) MoveFinger(rightFinger, target);
    }

    void MoveFinger(Transform f, float x)
    {
        var p = f.localPosition;
        p.x = Mathf.MoveTowards(p.x, x, 0.5f * Time.deltaTime);
        f.localPosition = p;
    }

    void TryGrab()
    {
        inZone.RemoveWhere(r => r == null);
        Rigidbody best = null;
        float bestD = float.MaxValue;
        foreach (var r in inZone)
        {
            float d = (r.worldCenterOfMass - transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = r; }
        }
        if (best == null) return;

        Held = best;
        AllHeld.Add(best);
        SetIgnore(best, true);
        joint = best.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = robotBody;
        joint.enablePreprocessing = false;
        if (TeleopMetrics.Instance) TeleopMetrics.Instance.RegisterEvent("agarre", best.name);
    }

    void Release()
    {
        if (joint) Destroy(joint);
        joint = null;
        if (Held)
        {
            SetIgnore(Held, false);
            AllHeld.Remove(Held);
            if (TeleopMetrics.Instance) TeleopMetrics.Instance.RegisterEvent("soltar", Held.name);
        }
        Held = null;
    }

    void SetIgnore(Rigidbody obj, bool ignore)
    {
        foreach (var oc in obj.GetComponentsInChildren<Collider>())
            foreach (var rc in robotColliders)
                if (rc && !rc.isTrigger) Physics.IgnoreCollision(oc, rc, ignore);
    }

    void OnTriggerEnter(Collider other)
    {
        var s = other.GetComponentInParent<HapticSurface>();
        if (s && s.IsManipulable && other.attachedRigidbody) inZone.Add(other.attachedRigidbody);
    }

    void OnTriggerExit(Collider other)
    {
        if (other.attachedRigidbody && other.attachedRigidbody != Held) inZone.Remove(other.attachedRigidbody);
    }
}
