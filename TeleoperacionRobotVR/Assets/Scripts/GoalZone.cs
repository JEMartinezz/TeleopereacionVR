using System.Collections.Generic;
using UnityEngine;

/// Zona de entrega: cuenta los objetos manipulables depositados (no sujetos por la pinza).
public class GoalZone : MonoBehaviour
{
    readonly HashSet<Rigidbody> inside = new HashSet<Rigidbody>();

    public int Delivered
    {
        get
        {
            inside.RemoveWhere(r => r == null);
            int n = 0;
            foreach (var r in inside) if (!RobotGripper.AllHeld.Contains(r)) n++;
            return n;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        var s = other.GetComponentInParent<HapticSurface>();
        if (s && s.IsManipulable && other.attachedRigidbody) inside.Add(other.attachedRigidbody);
    }

    void OnTriggerExit(Collider other)
    {
        if (other.attachedRigidbody) inside.Remove(other.attachedRigidbody);
    }
}
