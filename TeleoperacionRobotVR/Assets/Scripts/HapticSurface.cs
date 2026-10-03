using UnityEngine;

/// Marca un objeto del entorno con el patrón de vibración que produce al chocar con el robot.
public class HapticSurface : MonoBehaviour
{
    public HapticPattern pattern = HapticPattern.ParedLimite;

    public bool IsManipulable => pattern == HapticPattern.ObjetoManipulable;
}
