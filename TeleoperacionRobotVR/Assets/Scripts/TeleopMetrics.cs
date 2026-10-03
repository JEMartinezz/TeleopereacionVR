using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// Métricas para la evaluación experimental: colisiones por tipo, latencia Bluetooth,
/// tiempo de tarea (entregar todos los objetos en la zona meta) y telemetría del control.
/// Genera dos CSV por sesión en la carpeta "Resultados" junto al proyecto / ejecutable:
///   eventos_<fecha>.csv     colisiones, agarres, entregas, fin de tarea
///   telemetria_<fecha>.csv  pitch, roll, v, ω, latencia a 20 Hz (para analizar estabilidad)
public class TeleopMetrics : MonoBehaviour
{
    public static TeleopMetrics Instance { get; private set; }

    public RobotController robot;
    public GoalZone goal;
    public int objectsToDeliver = 4;
    public TextMesh worldPanel;
    public float telemetryRate = 20f;
    public bool showHud = true;

    public int CollisionsObject { get; private set; }
    public int CollisionsWall { get; private set; }
    public int CollisionsObstacle { get; private set; }
    public float TaskTime { get; private set; }
    public bool TaskDone { get; private set; }

    float taskStart = -1f;
    float nextSample;
    float flashUntil;
    string flashText = "";
    int lastDelivered;
    StreamWriter events, telemetry;
    readonly CultureInfo inv = CultureInfo.InvariantCulture;

    void Awake()
    {
        Instance = this;
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Resultados");
        Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        events = new StreamWriter(Path.Combine(dir, $"eventos_{stamp}.csv"), false, Encoding.UTF8);
        telemetry = new StreamWriter(Path.Combine(dir, $"telemetria_{stamp}.csv"), false, Encoding.UTF8);
        events.WriteLine("t_s,evento,detalle,latencia_ms");
        telemetry.WriteLine("t_s,imu_pitch_deg,imu_roll_deg,joy_x,joy_y,v_mps,w_degps,boton,garra_cerrada,latencia_ms,fuente");
        Debug.Log("[TeleopMetrics] Guardando resultados en " + dir);
    }

    RobotGripper gripper;

    void Start()
    {
        gripper = FindAnyObjectByType<RobotGripper>();
        if (HapticLink.Instance) HapticLink.Instance.Vibrated += OnVibrated;
    }

    void OnVibrated(HapticPattern p)
    {
        flashText = "Vibración: " + p;
        flashUntil = Time.time + 0.6f;
    }

    public void RegisterCollision(HapticPattern p, string what)
    {
        switch (p)
        {
            case HapticPattern.ObjetoManipulable: CollisionsObject++; break;
            case HapticPattern.ParedLimite: CollisionsWall++; break;
            case HapticPattern.Obstaculo: CollisionsObstacle++; break;
        }
        RegisterEvent("colision_" + p, what);
    }

    public void RegisterEvent(string evt, string detail)
    {
        var l = HapticLink.Instance;
        float lat = l ? l.LastLatencyMs : -1f;
        events?.WriteLine(string.Format(inv, "{0:F3},{1},{2},{3:F1}", Time.time, evt, detail, lat));
        events?.Flush();
    }

    void Update()
    {
        if (taskStart < 0f && robot && robot.HasMoved) { taskStart = Time.time; RegisterEvent("inicio_tarea", ""); }
        if (taskStart >= 0f && !TaskDone) TaskTime = Time.time - taskStart;

        int delivered = goal ? goal.Delivered : 0;
        if (delivered != lastDelivered) { RegisterEvent("entregados", delivered.ToString()); lastDelivered = delivered; }
        if (!TaskDone && delivered >= objectsToDeliver)
        {
            TaskDone = true;
            RegisterEvent("fin_tarea", TaskTime.ToString("F2", inv));
        }

        if (Input.GetKeyDown(KeyCode.H)) showHud = !showHud;

        if (Time.time >= nextSample)
        {
            nextSample = Time.time + 1f / telemetryRate;
            var l = HapticLink.Instance;
            if (l && robot)
                telemetry.WriteLine(string.Format(inv, "{0:F3},{1:F2},{2:F2},{3:F2},{4:F2},{5:F3},{6:F2},{7},{8},{9:F1},{10}",
                    Time.time, l.Pitch, l.Roll, l.DriveX, l.DriveY, robot.LinearSpeed, robot.AngularSpeed,
                    l.Grab ? 1 : 0, gripper && gripper.Closed ? 1 : 0, l.LastLatencyMs,
                    l.UsingKeyboard ? "teclado" : "esp32"));
        }

        if (worldPanel) worldPanel.text = BuildText(delivered);
    }

    string BuildText(int delivered)
    {
        var l = HapticLink.Instance;
        var sb = new StringBuilder();
        if (l)
        {
            sb.AppendLine(l.UsingKeyboard ? "Control: SIMULADO (teclado)" : $"Control: ESP32 en {l.ActivePort}  {l.DataRateHz:F0} Hz");
            sb.AppendLine($"IMU  pitch {l.Pitch,6:F1}°  roll {l.Roll,6:F1}°   Joystick X {l.DriveX,5:F2}  Y {l.DriveY,5:F2}");
            sb.AppendLine(l.LatencySamples > 0
                ? $"Latencia RTT {l.LastLatencyMs:F0} ms (prom {l.AvgLatencyMs:F0}, máx {l.MaxLatencyMs:F0})"
                : "Latencia RTT: sin datos");
        }
        if (gripper) sb.AppendLine("Garra: " + (gripper.Closed ? (gripper.Held ? "CERRADA (sujetando " + gripper.Held.name + ")" : "CERRADA") : "abierta"));
        if (robot) sb.AppendLine($"v {robot.LinearSpeed:F2} m/s   ω {robot.AngularSpeed:F0} °/s");
        sb.AppendLine($"Colisiones  obj {CollisionsObject}  pared {CollisionsWall}  obst {CollisionsObstacle}");
        sb.AppendLine($"Entregados {delivered}/{objectsToDeliver}   Tiempo {TaskTime:F1} s{(TaskDone ? "  ¡COMPLETADO!" : "")}");
        if (Time.time < flashUntil) sb.AppendLine(flashText);
        return sb.ToString();
    }

    void OnGUI()
    {
        if (!showHud) return;
        var l = HapticLink.Instance;
        string text = BuildText(goal ? goal.Delivered : 0);
        if (l) text += "Estado: " + l.Status + "\n";
        var ps = PhoneStreamer.Instance;
        if (ps) text += $"Celular (Safari): {ps.Url}  ·  {ps.Clients} conectado(s){(ps.Clients > 0 ? $", {ps.StreamFps:F0} FPS" : "")}{(ps.stereo ? ", Cardboard" : "")}\n";
        text += "\nESP32: joystick = carro · botón joystick = garra · inclinar MPU6050 = cámara · C calibrar\n" +
                "Teclado: flechas carro · Espacio garra · I/K/J/L cámara · V vista avatar · W/S avatar · H ocultar";
        var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = true };
        style.normal.textColor = Color.white;
        GUI.Box(new Rect(10, 10, 560, 270), text, style);
    }

    void OnDestroy()
    {
        if (HapticLink.Instance) HapticLink.Instance.Vibrated -= OnVibrated;
        RegisterEvent("fin_sesion", "");
        events?.Close();
        telemetry?.Close();
        events = telemetry = null;
    }
}
