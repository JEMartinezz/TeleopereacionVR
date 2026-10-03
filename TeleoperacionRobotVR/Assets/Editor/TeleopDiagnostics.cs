using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Herramientas de verificación de la escena:
///   - Prueba de colisiones: lanza el rover a máxima velocidad contra cada obstáculo y límite con la física real
///     y verifica que no lo atraviese.
///   - Capturas: guarda imágenes de la escena (vista del rover, vista general, gafas VR) en la carpeta Capturas.
public static class TeleopDiagnostics
{
    const string ScenePath = "Assets/Scenes/Teleoperacion.unity";

    public static string CapturesDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Capturas"));

    // -executeMethod TeleopDiagnostics.RunAll
    public static void RunAll()
    {
        CollisionTest();
        Screenshots();
    }

    [MenuItem("Teleoperación/Diagnóstico/Probar colisiones")]
    public static void CollisionTest()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var robot = Object.FindAnyObjectByType<RobotController>();
        var rb = robot.GetComponent<Rigidbody>();
        var chassis = robot.GetComponent<BoxCollider>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.None;

        // Objetos manipulables fuera del camino durante la prueba
        foreach (var s in Object.FindObjectsByType<HapticSurface>())
            if (s.IsManipulable && s.TryGetComponent(out Rigidbody orb)) { orb.isKinematic = true; s.transform.position += Vector3.down * 50f; }

        var prevMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        var report = new StringBuilder();
        report.AppendLine("Prueba de colisiones del rover (v = 1.2 m/s durante 3 s contra cada obstáculo)");
        report.AppendLine("obstaculo,tipo,distancia_libre_m,avance_m,penetracion_m,resultado");
        int ok = 0, fail = 0, skipped = 0;

        var targets = new List<HapticSurface>();
        foreach (var s in Object.FindObjectsByType<HapticSurface>())
            if (!s.IsManipulable && s.GetComponentInChildren<Collider>() && !s.name.StartsWith("BarreraInvisible")) targets.Add(s);

        foreach (var target in targets)
        {
            var cols = target.GetComponentsInChildren<Collider>();
            var b = cols[0].bounds;
            foreach (var c in cols) b.Encapsulate(c.bounds);
            Vector3 center = new Vector3(b.center.x, 0f, b.center.z);
            if (target.pattern == HapticPattern.ParedLimite)   // los límites: acercarse desde dentro de la arena
                center = new Vector3(Mathf.Clamp(center.x, -10.3f, 10.3f), 0, Mathf.Clamp(center.z, -10.3f, 10.3f));

            bool tested = false;
            for (int k = 0; k < 16 && !tested; k++)
            {
                Vector3 dir = Quaternion.Euler(0, k * 22.5f, 0) * Vector3.forward;   // dirección de avance hacia el obstáculo
                if (target.pattern == HapticPattern.ParedLimite) dir = -new Vector3(Mathf.Round(center.x / 10f), 0, Mathf.Round(center.z / 10f)).normalized * -1f;
                float reach = Mathf.Max(b.extents.x, b.extents.z);
                Vector3 start = center - dir * (reach + 1.6f);
                if (target.pattern == HapticPattern.ParedLimite) start = center - dir * 2.2f;
                start.y = 0.02f;
                if (Mathf.Abs(start.x) > 9.4f || Mathf.Abs(start.z) > 9.4f) continue;

                var rot = Quaternion.LookRotation(dir);
                Vector3 boxCenter = start + rot * chassis.center;
                if (Physics.CheckBox(boxCenter, chassis.size * 0.5f, rot, ~0, QueryTriggerInteraction.Ignore))
                {
                    // ¿solo toca el piso? (el piso está debajo; el chasis empieza a 0.04 m)
                    bool blocked = false;
                    foreach (var h in Physics.OverlapBox(boxCenter, chassis.size * 0.5f, rot, ~0, QueryTriggerInteraction.Ignore))
                        if (h.name != "PisoFisico" && !h.transform.IsChildOf(robot.transform)) blocked = true;
                    if (blocked) continue;
                }
                // Lo primero que encuentra el rover en esa dirección debe ser el obstáculo objetivo
                var hits = Physics.BoxCastAll(boxCenter, chassis.size * 0.5f * 0.98f, dir, rot, 6f, ~0, QueryTriggerInteraction.Ignore);
                float first = float.MaxValue; Collider firstCol = null;
                foreach (var h in hits)
                {
                    if (h.collider.name == "PisoFisico" || h.collider.transform.IsChildOf(robot.transform)) continue;
                    if (h.distance < first) { first = h.distance; firstCol = h.collider; }
                }
                if (firstCol == null || firstCol.GetComponentInParent<HapticSurface>() != target) continue;

                // Simulación real
                rb.position = start; rb.rotation = rot;
                robot.transform.SetPositionAndRotation(start, rot);
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
                for (int i = 0; i < 150; i++)
                {
                    Vector3 v = robot.transform.forward * robot.maxLinearSpeed;
                    v.y = rb.linearVelocity.y;
                    rb.linearVelocity = v;
                    rb.angularVelocity = Vector3.zero;
                    Physics.Simulate(0.02f);
                }
                float advance = Vector3.Dot(rb.position - start, dir);
                float penetration = advance - first;
                bool passed = penetration < 0.08f;
                if (passed) ok++; else fail++;
                report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2:F2},{3:F2},{4:F3},{5}", target.name, target.pattern, first, advance, penetration, passed ? "BLOQUEA" : "ATRAVIESA"));
                tested = true;
            }
            if (!tested) { skipped++; report.AppendLine($"{target.name},{target.pattern},,,,sin acceso libre"); }
        }
        Physics.simulationMode = prevMode;
        report.AppendLine($"Resumen: {ok} bloquean, {fail} atraviesan, {skipped} sin acceso");

        Directory.CreateDirectory(CapturesDir);
        File.WriteAllText(Path.Combine(CapturesDir, "prueba_colisiones.csv"), report.ToString(), Encoding.UTF8);
        Debug.Log("[TeleopDiagnostics]\n" + report);
        EditorSceneManager.OpenScene(ScenePath);   // descarta los cambios de la prueba
    }

    [MenuItem("Teleoperación/Diagnóstico/Capturas de la escena")]
    public static void Screenshots()
    {
        EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory(CapturesDir);
        var robot = Object.FindAnyObjectByType<RobotController>().transform;
        var chase = Object.FindAnyObjectByType<ChaseCamera>().GetComponent<Camera>();

        // 1) Vista de la cámara que sigue al rover
        Vector3 focus = robot.position + Vector3.up * 0.3f;
        chase.transform.position = focus + Quaternion.Euler(22f, robot.eulerAngles.y, 0) * new Vector3(0, 0, -2.2f);
        chase.transform.LookAt(focus);
        Capture(chase, 1920, 1080, "1_vista_rover.png");

        // 2) Vista general de la base
        chase.transform.position = new Vector3(-13f, 9f, -14f);
        chase.transform.LookAt(new Vector3(0, 0, 1f));
        Capture(chase, 1920, 1080, "2_vista_general.png");

        // 3) Rover de cerca
        chase.transform.position = robot.position + new Vector3(1.1f, 0.55f, 1.2f);
        chase.transform.LookAt(robot.position + Vector3.up * 0.25f);
        Capture(chase, 1920, 1080, "3_rover.png");

        // 4) Plataforma de entrega y cristales
        chase.transform.position = new Vector3(3.5f, 2.2f, 2.5f);
        chase.transform.LookAt(new Vector3(7.5f, 0.3f, 7.5f));
        Capture(chase, 1920, 1080, "4_zona_entrega.png");

        // 5) Gafas VR (lo que recibe el celular)
        chase.transform.position = focus + Quaternion.Euler(22f, robot.eulerAngles.y, 0) * new Vector3(0, 0, -2.2f);
        chase.transform.LookAt(focus);
        var streamer = Object.FindAnyObjectByType<PhoneStreamer>();
        if (streamer && streamer.eyeCamera && streamer.distortionMaterial)
        {
            var l = Eye(chase, streamer, -streamer.ipd / 2f);
            var r = Eye(chase, streamer, streamer.ipd / 2f);
            var outRT = new RenderTexture(1280, 720, 0);
            var m = streamer.distortionMaterial;
            m.SetTexture("_LeftTex", l); m.SetTexture("_RightTex", r);
            m.SetFloat("_K1", streamer.k1); m.SetFloat("_K2", streamer.k2); m.SetFloat("_Zoom", streamer.zoom);
            m.SetFloat("_EyeAspect", 640f / 720f);
            Graphics.Blit(null, outRT, m);
            Save(outRT, "5_gafas_vr.png");
            Object.DestroyImmediate(l); Object.DestroyImmediate(r); Object.DestroyImmediate(outRT);
        }
        Debug.Log("[TeleopDiagnostics] Capturas guardadas en " + CapturesDir);
        EditorSceneManager.OpenScene(ScenePath);
    }

    static RenderTexture Eye(Camera src, PhoneStreamer s, float offset)
    {
        var rt = new RenderTexture(640, 720, 24) { antiAliasing = 2 };
        var cam = s.eyeCamera;
        cam.CopyFrom(src);
        cam.enabled = false;
        cam.targetTexture = rt;
        cam.fieldOfView = s.eyeFov;
        cam.ResetAspect();
        cam.transform.SetPositionAndRotation(src.transform.position + src.transform.right * offset, src.transform.rotation);
        cam.Render();
        cam.targetTexture = null;
        return rt;
    }

    static void Capture(Camera cam, int w, int h, string file)
    {
        var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;
        Save(rt, file);
        Object.DestroyImmediate(rt);
    }

    static void Save(RenderTexture rt, string file)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(CapturesDir, file), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
