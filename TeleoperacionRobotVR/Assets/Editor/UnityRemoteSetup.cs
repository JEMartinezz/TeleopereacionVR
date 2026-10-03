using UnityEditor;
using UnityEngine;

/// Configura Unity Remote 5 para ver la escena en un iPhone conectado por USB.
/// El celular solo muestra la imagen: los joysticks se leen del PC (control de Xbox), no del celular.
public static class UnityRemoteSetup
{
    [MenuItem("Teleoperación/Configurar Unity Remote (iPhone)")]
    public static void Configure()
    {
        EditorSettings.unityRemoteDevice = "Any iOS Device";
        EditorSettings.unityRemoteCompression = "JPEG";
        EditorSettings.unityRemoteResolution = "Normal";
        EditorSettings.unityRemoteJoystickSource = "Local";
        AssetDatabase.SaveAssets();
        Debug.Log("[UnityRemoteSetup] Unity Remote: dispositivo = " + EditorSettings.unityRemoteDevice +
                  ", joystick = " + EditorSettings.unityRemoteJoystickSource);
    }
}
