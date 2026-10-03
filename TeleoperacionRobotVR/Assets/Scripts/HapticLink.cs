using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// Patrones de vibración del control háptico (ver firmware ESP32).
public enum HapticPattern
{
    ObjetoManipulable = 1,
    ParedLimite = 2,
    Obstaculo = 3
}

/// Enlace con el control háptico ESP32 (USB o Bluetooth SPP; ambos aparecen como puerto COM en Windows).
///
///   MPU6050 (pitch / roll) -> Pitch, Roll   -> mueve la cámara que sigue al carro (ChaseCamera)
///   Joystick X / Y         -> DriveX, DriveY -> mueve el carro (RobotController)
///   Botón del joystick     -> Grab          -> cierra / abre la garra (RobotGripper)
///   Vibrate(patrón)        -> motor vibrador por PWM al chocar
///
/// Protocolo (líneas de texto terminadas en '\n', 115200 baudios):
///   ESP32 -> PC : "D,<pitch>,<roll>,<joyX>,<joyY>,<boton>"   (°, °, -1..1, -1..1, 0/1)
///                 "K,<id>"                                  (respuesta a ping)
///   PC -> ESP32 : "V,<patrón>"  1 = objeto manipulable, 2 = pared/límite, 3 = obstáculo
///                 "P,<id>"      ping para medir latencia
///                 "C"           recalibrar (giroscopio y centro del joystick)
///
/// Sin ESP32 se usa el teclado: flechas = joystick, Espacio = botón, I/K/J/L = inclinar la IMU.
public class HapticLink : MonoBehaviour
{
    public static HapticLink Instance { get; private set; }

    [Header("Puerto serie")]
    [Tooltip("Puerto del ESP32 (ej. COM10) o AUTO para buscarlo. Por USB suele ser 'Silicon Labs CP210x' o 'CH340'.")]
    public string portName = "AUTO";
    public int baudRate = 115200;
    [Tooltip("Segundos sin datos para considerar el control desconectado.")]
    public float dataTimeout = 0.5f;
    [Tooltip("Intervalo entre pings de latencia (s).")]
    public float pingInterval = 1f;

    [Header("Simulación con teclado")]
    public bool keyboardFallback = true;
    public float simTiltSpeed = 80f;
    public float simMaxAngle = 35f;

    // ---- Estado del control
    /// Inclinación de la IMU (°), ya calibrada. Mueve la cámara.
    public float Pitch { get; private set; }
    public float Roll { get; private set; }
    /// Joystick (-1..1). Y = avance/retroceso, X = giro del carro.
    public float DriveX { get; private set; }
    public float DriveY { get; private set; }
    /// Botón del joystick: garra.
    public bool Grab { get; private set; }
    /// Avance/retroceso del avatar (solo teclado W/S).
    public bool Forward { get; private set; }
    public bool Backward { get; private set; }
    public bool RecenterPressed { get; private set; }

    public bool DeviceConnected => stopwatch.Elapsed.TotalSeconds - lastDataTime < dataTimeout;
    public bool UsingKeyboard { get; private set; }
    public string Status { get; private set; } = "Iniciando";
    public string ActivePort { get; private set; } = "-";
    public float LastLatencyMs { get; private set; } = -1f;
    public float AvgLatencyMs { get; private set; } = -1f;
    public float MaxLatencyMs { get; private set; } = -1f;
    public int LatencySamples { get; private set; }
    public float DataRateHz { get; private set; }

    public float pitchOffset, rollOffset;

    public event Action<HapticPattern> Vibrated;

    const string LastPortKey = "HapticLink.LastPort";

    readonly Stopwatch stopwatch = Stopwatch.StartNew();
    readonly object sync = new object();
    readonly Dictionary<int, double> pendingPings = new Dictionary<int, double>();
    Thread readerThread;
    volatile bool running;
    SerialPort port;
    double lastDataTime = -10;
    float rawPitch, rawRoll, rawJoyX, rawJoyY;
    bool rawButton;
    int linesThisSecond;
    double rateWindowStart;
    int pingId;
    float nextPing;
    float latencySum;
    float simPitch, simRoll;
    string preferredPort;

    void Awake()
    {
        Instance = this;
        preferredPort = PlayerPrefs.GetString(LastPortKey, "");
    }

    void OnEnable()
    {
        running = true;
        readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = "HapticLinkReader" };
        readerThread.Start();
    }

    void OnDisable()
    {
        running = false;
        ClosePort();
        if (readerThread != null && readerThread.IsAlive) readerThread.Join(500);
    }

    void Update()
    {
        if (DeviceConnected)
        {
            UsingKeyboard = false;
            lock (sync)
            {
                Pitch = rawPitch - pitchOffset;
                Roll = rawRoll - rollOffset;
                DriveX = rawJoyX;
                DriveY = rawJoyY;
                Grab = rawButton;
            }
            if (!string.IsNullOrEmpty(ActivePort) && ActivePort != preferredPort)
            {
                preferredPort = ActivePort;
                PlayerPrefs.SetString(LastPortKey, ActivePort);
            }
        }
        else if (keyboardFallback)
        {
            UsingKeyboard = true;
            SimulateWithKeyboard();
        }
        else
        {
            UsingKeyboard = false;
            Pitch = Roll = DriveX = DriveY = 0f;
            Grab = false;
        }

        Forward = Input.GetKey(KeyCode.W);
        Backward = Input.GetKey(KeyCode.S);
        RecenterPressed = Input.GetKeyDown(KeyCode.R);
        if (Input.GetKeyDown(KeyCode.C)) Calibrate();

        if (port != null && port.IsOpen && Time.unscaledTime >= nextPing)
        {
            nextPing = Time.unscaledTime + pingInterval;
            SendPing();
        }
    }

    void SimulateWithKeyboard()
    {
        float dt = Time.deltaTime;
        // Joystick simulado: flechas
        DriveY = (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
        DriveX = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
        Grab = Input.GetKey(KeyCode.Space);
        // IMU simulada: I/K = pitch, J/L = roll
        float tp = (Input.GetKey(KeyCode.I) ? 1 : 0) - (Input.GetKey(KeyCode.K) ? 1 : 0);
        float tr = (Input.GetKey(KeyCode.L) ? 1 : 0) - (Input.GetKey(KeyCode.J) ? 1 : 0);
        simPitch = Mathf.MoveTowards(simPitch, tp * simMaxAngle, simTiltSpeed * dt);
        simRoll = Mathf.MoveTowards(simRoll, tr * simMaxAngle, simTiltSpeed * dt);
        Pitch = simPitch;
        Roll = simRoll;
    }

    /// Toma la postura actual de la IMU como "cero" y recalibra el ESP32 (giroscopio y centro del joystick). Tecla C.
    public void Calibrate()
    {
        lock (sync)
        {
            pitchOffset = rawPitch;
            rollOffset = rawRoll;
        }
        WriteLine("C");
        Debug.Log($"[HapticLink] Calibrado: offset pitch={pitchOffset:F1}° roll={rollOffset:F1}°");
    }

    public void Vibrate(HapticPattern pattern)
    {
        WriteLine("V," + (int)pattern);
        Vibrated?.Invoke(pattern);
    }

    void SendPing()
    {
        int id = ++pingId;
        lock (sync)
        {
            pendingPings[id] = stopwatch.Elapsed.TotalMilliseconds;
            if (pendingPings.Count > 20) pendingPings.Clear();
        }
        WriteLine("P," + id);
    }

    void WriteLine(string msg)
    {
        var p = port;
        if (p == null || !p.IsOpen) return;
        try { p.Write(msg + "\n"); }
        catch (Exception e) { Status = "Error al escribir: " + e.Message; }
    }

    // ------------------------------------------------------------------ hilo de lectura

    void ReaderLoop()
    {
        while (running)
        {
            if (port == null || !port.IsOpen)
            {
                if (!TryConnect()) { Sleep(1000); continue; }
            }

            try
            {
                string line = port.ReadLine();
                ParseLine(line.Trim());
            }
            catch (TimeoutException)
            {
                if (!DeviceConnected) Status = "Conectado a " + ActivePort + " pero sin datos";
            }
            catch (Exception e)
            {
                if (!running) break;
                Status = "Conexión perdida: " + e.Message;
                ClosePort();
                Sleep(1000);
            }
        }
    }

    bool TryConnect()
    {
        var candidates = new List<string>();
        var bt = BluetoothPorts();
        if (portName.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
        {
            // Orden: último puerto que funcionó, USB (CP210x/CH340), y el Bluetooth del ESP32 ("ControlHaptico_UMNG").
            // Se saltan los puertos Bluetooth de otros aparatos (audífonos, celulares) y los "entrantes" de Windows.
            var usb = new List<string>();
            var esp = new List<string>();
            foreach (var name in SerialPort.GetPortNames())
            {
                if (!bt.TryGetValue(name, out string device)) usb.Add(name);
                else if (device.IndexOf("ControlHaptico", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         device.IndexOf("ESP32", StringComparison.OrdinalIgnoreCase) >= 0) esp.Add(name);
            }
            if (!string.IsNullOrEmpty(preferredPort) && (usb.Contains(preferredPort) || esp.Contains(preferredPort)))
                candidates.Add(preferredPort);
            foreach (var n in usb) if (!candidates.Contains(n)) candidates.Add(n);
            foreach (var n in esp) if (!candidates.Contains(n)) candidates.Add(n);
        }
        else candidates.Add(portName);

        if (candidates.Count == 0)
        {
            Status = "ESP32 no encontrado: empareja 'ControlHaptico_UMNG' por Bluetooth en Windows (o conéctalo por USB)";
            return false;
        }

        foreach (var name in candidates)
        {
            if (!running) return false;
            Status = "Buscando ESP32 en " + name + "...";
            SerialPort p = null;
            try
            {
                // DTR/RTS en falso: no reinicia el ESP32 al abrir el puerto
                p = new SerialPort(name, baudRate) { ReadTimeout = 300, WriteTimeout = 200, NewLine = "\n", DtrEnable = false, RtsEnable = false };
                p.Open();
                var t0 = stopwatch.Elapsed.TotalSeconds;
                double wait = bt.ContainsKey(name) ? 8.0 : 3.5;   // Bluetooth tarda en enlazar; USB: margen por si el ESP32 se reinicia
                while (stopwatch.Elapsed.TotalSeconds - t0 < wait && running)
                {
                    try
                    {
                        string line = p.ReadLine().Trim();
                        if (line.StartsWith("D,"))
                        {
                            p.ReadTimeout = 100;
                            port = p;
                            ActivePort = name;
                            Status = "Conectado al ESP32 en " + name;
                            ParseLine(line);
                            return true;
                        }
                    }
                    catch (TimeoutException) { }
                }
                p.Close();
            }
            catch (Exception)
            {
                try { p?.Close(); } catch { }
            }
        }
        Status = "ESP32 no encontrado en: " + string.Join(", ", candidates) + " (¿Monitor Serie de Arduino abierto?)";
        return false;
    }

    /// Puertos COM de Bluetooth -> nombre del aparato emparejado ("" para los puertos entrantes o desconocidos).
    /// Usa el registro de Windows: Enum\BTHENUM (COM -> MAC) y BTHPORT\Parameters\Devices (MAC -> nombre).
    static Dictionary<string, string> BluetoothPorts()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reg = Microsoft.Win32.Registry.LocalMachine;
        try
        {
            // Todos los COM de Bluetooth (dispositivo \Device\BthModem*)
            using (var key = reg.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
                if (key != null)
                    foreach (var dev in key.GetValueNames())
                        if (dev.IndexOf("Bth", StringComparison.OrdinalIgnoreCase) >= 0 && key.GetValue(dev) is string com)
                            result[com] = "";

            using (var enumKey = reg.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\BTHENUM"))
            using (var devices = reg.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices"))
            {
                if (enumKey == null) return result;
                foreach (var service in enumKey.GetSubKeyNames())
                {
                    if (service.IndexOf("00001101", StringComparison.OrdinalIgnoreCase) < 0) continue;   // perfil SPP
                    using (var sk = enumKey.OpenSubKey(service))
                        foreach (var inst in sk.GetSubKeyNames())
                            using (var p = sk.OpenSubKey(inst + @"\Device Parameters"))
                            {
                                if (!(p?.GetValue("PortName") is string com)) continue;
                                // instancia "7&...&0&A4056EF78D2E_C00000000" -> MAC
                                var parts = inst.Split('&');
                                string mac = parts[parts.Length - 1].Split('_')[0].ToLowerInvariant();
                                string name = "";
                                if (mac != "000000000000" && devices != null)
                                    using (var d = devices.OpenSubKey(mac))
                                        if (d?.GetValue("Name") is byte[] raw)
                                            name = System.Text.Encoding.UTF8.GetString(raw).TrimEnd('\0');
                                result[com] = name;
                            }
                }
            }
        }
        catch { }
        return result;
    }

    void ParseLine(string line)
    {
        if (line.Length < 2 || line[0] == '#') return;
        var parts = line.Split(',');
        var inv = CultureInfo.InvariantCulture;
        double now = stopwatch.Elapsed.TotalMilliseconds;

        if (parts[0] == "D" && parts.Length >= 6)
        {
            if (!float.TryParse(parts[1], NumberStyles.Float, inv, out float p)) return;
            if (!float.TryParse(parts[2], NumberStyles.Float, inv, out float r)) return;
            if (!float.TryParse(parts[3], NumberStyles.Float, inv, out float jx)) return;
            if (!float.TryParse(parts[4], NumberStyles.Float, inv, out float jy)) return;
            lock (sync)
            {
                rawPitch = p;
                rawRoll = r;
                rawJoyX = Mathf.Clamp(jx, -1f, 1f);
                rawJoyY = Mathf.Clamp(jy, -1f, 1f);
                rawButton = parts[5] == "1";
            }
            lastDataTime = now / 1000.0;
            Status = "Conectado al ESP32 en " + ActivePort;

            linesThisSecond++;
            if (now - rateWindowStart >= 1000)
            {
                DataRateHz = linesThisSecond * 1000f / (float)(now - rateWindowStart);
                linesThisSecond = 0;
                rateWindowStart = now;
            }
        }
        else if (parts[0] == "K" && parts.Length >= 2 && int.TryParse(parts[1], out int id))
        {
            lock (sync)
            {
                if (pendingPings.TryGetValue(id, out double sent))
                {
                    pendingPings.Remove(id);
                    float rtt = (float)(now - sent);
                    LastLatencyMs = rtt;
                    LatencySamples++;
                    latencySum += rtt;
                    AvgLatencyMs = latencySum / LatencySamples;
                    MaxLatencyMs = Mathf.Max(MaxLatencyMs, rtt);
                }
            }
        }
    }

    void ClosePort()
    {
        var p = port;
        port = null;
        ActivePort = "-";
        try { p?.Close(); } catch { }
    }

    void Sleep(int ms)
    {
        for (int i = 0; i < ms / 50 && running; i++) Thread.Sleep(50);
    }
}
