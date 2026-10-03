using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

/// Activa OpenXR para PC (Standalone) para usar el Meta Quest 3 mediante Quest Link / Air Link.
public static class XRProjectSetup
{
    [MenuItem("Teleoperación/Configurar OpenXR (Meta Quest 3 por Link)")]
    public static void Configure()
    {
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
        if (perTarget == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
        }

        var group = BuildTargetGroup.Standalone;
        if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
        if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

        var settings = perTarget.SettingsForBuildTarget(group);
        settings.InitManagerOnStart = true;
        bool ok = XRPackageMetadataStore.AssignLoader(settings.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);

        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(perTarget);
        AssetDatabase.SaveAssets();
        Debug.Log("[XRProjectSetup] OpenXR para Standalone: " + (ok ? "activado" : "no se pudo asignar el loader"));
    }
}
