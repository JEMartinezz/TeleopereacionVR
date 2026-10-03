using UnityEngine;
using UnityEngine.XR;

/// Aplica la orientación (IMU) y el desplazamiento del visor VR a la cámara del avatar.
/// R = recentrar la vista hacia delante.
[RequireComponent(typeof(Camera))]
public class XRHeadTracker : MonoBehaviour
{
    public bool trackPosition = true;

    Camera cam;
    Vector3 basePosition;
    Vector3 positionOrigin;
    float yawOffset;
    bool originSet;

    void Start()
    {
        cam = GetComponent<Camera>();
        basePosition = transform.localPosition;
    }

    void Update()
    {
        if (!XRSettings.isDeviceActive) return;
        var head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!head.isValid) return;

        if (head.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
        {
            if (HapticLink.Instance ? HapticLink.Instance.RecenterPressed : Input.GetKeyDown(KeyCode.R)) { yawOffset = rot.eulerAngles.y; originSet = false; }
            transform.localRotation = Quaternion.Euler(0f, -yawOffset, 0f) * rot;
        }

        if (trackPosition && head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
        {
            if (!originSet) { positionOrigin = pos; originSet = true; }
            transform.localPosition = basePosition + Quaternion.Euler(0f, -yawOffset, 0f) * (pos - positionOrigin);
        }
    }
}
