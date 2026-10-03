using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// Convierte el celular en unas gafas de realidad virtual tipo Cardboard por Wi‑Fi.
/// Unity renderiza la cámara que sigue al carro dos veces (ojo izquierdo / derecho, separados por la IPD),
/// aplica la pre-distorsión de los lentes y la envía como video MJPEG por HTTP.
/// En el iPhone: Safari -> http://IP-del-PC:8765  (mejor: "Compartir -> Agregar a inicio" para pantalla completa).
/// El celular solo visualiza: el carro y la cámara se controlan con el ESP32 (joystick + MPU6050).
public class PhoneStreamer : MonoBehaviour
{
    public static PhoneStreamer Instance { get; private set; }

    public int port = 8765;
    public int width = 1280;
    public int height = 720;
    public float fps = 30f;
    [Range(10, 95)] public int jpegQuality = 70;

    [Header("Gafas VR (Cardboard)")]
    [Tooltip("Pantalla dividida estéreo para el visor (también se cambia desde el celular).")]
    public bool stereo = true;
    [Tooltip("Distancia entre ojos (m).")]
    public float ipd = 0.064f;
    [Tooltip("Campo de visión vertical de cada ojo (°).")]
    public float eyeFov = 85f;
    public float k1 = 0.22f;
    public float k2 = 0.24f;
    public float zoom = 1.0f;
    [Tooltip("Cámara desactivada (con post-procesado) que se usa para renderizar los ojos.")]
    public Camera eyeCamera;
    public Material distortionMaterial;

    public string Url { get; private set; } = "-";
    public int Clients => clients;
    public float StreamFps { get; private set; }

    TcpListener listener;
    Thread acceptThread;
    volatile bool running;
    volatile int clients;

    readonly object frameLock = new object();
    byte[] latestJpeg;
    int frameId;

    RenderTexture outRT, leftRT, rightRT;
    bool ownsEyeCam;
    float nextCapture;
    bool readbackPending;
    int framesThisSecond;
    float fpsWindowStart;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<PhoneStreamer>() == null)
            new GameObject("TransmisionCelular").AddComponent<PhoneStreamer>();
    }

    void Awake()
    {
        Instance = this;
        if (distortionMaterial == null)
        {
            var sh = Shader.Find("Hidden/Teleop/CardboardDistortion");
            if (sh) distortionMaterial = new Material(sh);
        }
    }

    void OnEnable()
    {
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
        }
        catch (Exception e)
        {
            Url = "Error: puerto " + port + " ocupado (" + e.Message + ")";
            Debug.LogError("[PhoneStreamer] " + Url);
            return;
        }
        running = true;
        acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "PhoneStreamerAccept" };
        acceptThread.Start();

        var ips = LocalIPv4();
        Url = ips.Count > 0 ? string.Join("  |  ", ips.ConvertAll(ip => $"http://{ip}:{port}")) : $"http://<IP-del-PC>:{port}";
        Debug.Log("[PhoneStreamer] Abre en Safari del iPhone: " + Url);
    }

    void OnDisable()
    {
        running = false;
        try { listener?.Stop(); } catch { }
        lock (frameLock) Monitor.PulseAll(frameLock);
        Release(ref outRT);
        Release(ref leftRT);
        Release(ref rightRT);
        if (ownsEyeCam && eyeCamera) Destroy(eyeCamera.gameObject);
    }

    static void Release(ref RenderTexture rt)
    {
        if (rt) { rt.Release(); Destroy(rt); }
        rt = null;
    }

    static RenderTexture Ensure(ref RenderTexture rt, int w, int h, string name)
    {
        if (rt != null && rt.width == w && rt.height == h) return rt;
        Release(ref rt);
        rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2, name = name };
        return rt;
    }

    // ------------------------------------------------------------------ captura

    void Update()
    {
        if (!running || clients == 0 || readbackPending || Time.unscaledTime < nextCapture) return;
        var src = Camera.main;
        if (!src) return;
        nextCapture = Time.unscaledTime + 1f / fps;

        if (eyeCamera == null)
        {
            var go = new GameObject("CamaraCelular") { hideFlags = HideFlags.HideAndDontSave };
            eyeCamera = go.AddComponent<Camera>();
            ownsEyeCam = true;
        }
        eyeCamera.enabled = false;
        Ensure(ref outRT, width, height, "Celular");

        if (stereo && distortionMaterial)
        {
            int ew = width / 2;
            Ensure(ref leftRT, ew, height, "OjoIzq");
            Ensure(ref rightRT, ew, height, "OjoDer");
            RenderEye(src, -ipd * 0.5f, leftRT, eyeFov);
            RenderEye(src, ipd * 0.5f, rightRT, eyeFov);
            distortionMaterial.SetTexture("_LeftTex", leftRT);
            distortionMaterial.SetTexture("_RightTex", rightRT);
            distortionMaterial.SetFloat("_K1", k1);
            distortionMaterial.SetFloat("_K2", k2);
            distortionMaterial.SetFloat("_Zoom", Mathf.Max(0.3f, zoom));
            distortionMaterial.SetFloat("_EyeAspect", ew / (float)height);
            Graphics.Blit(null, outRT, distortionMaterial);
        }
        else
        {
            RenderEye(src, 0f, outRT, src.fieldOfView);
        }

        readbackPending = true;
        AsyncGPUReadback.Request(outRT, 0, TextureFormat.RGBA32, OnReadback);
    }

    void RenderEye(Camera src, float offset, RenderTexture target, float fov)
    {
        eyeCamera.CopyFrom(src);
        eyeCamera.enabled = false;
        eyeCamera.stereoTargetEye = StereoTargetEyeMask.None;   // no dibujar en el visor de PC
        eyeCamera.targetTexture = target;
        eyeCamera.rect = new Rect(0f, 0f, 1f, 1f);
        eyeCamera.fieldOfView = fov;
        eyeCamera.ResetAspect();
        eyeCamera.transform.SetPositionAndRotation(src.transform.position + src.transform.right * offset, src.transform.rotation);
        eyeCamera.Render();
        eyeCamera.targetTexture = null;
    }

    void OnReadback(AsyncGPUReadbackRequest req)
    {
        readbackPending = false;
        if (req.hasError || !running) return;
        byte[] pixels = req.GetData<byte>().ToArray();
        uint w = (uint)req.width, h = (uint)req.height;
        int quality = jpegQuality;

        framesThisSecond++;
        if (Time.unscaledTime - fpsWindowStart >= 1f)
        {
            StreamFps = framesThisSecond / (Time.unscaledTime - fpsWindowStart);
            framesThisSecond = 0;
            fpsWindowStart = Time.unscaledTime;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            byte[] jpg = ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_UNorm, w, h, 0, quality);
            lock (frameLock)
            {
                latestJpeg = jpg;
                frameId++;
                Monitor.PulseAll(frameLock);
            }
        });
    }

    // ------------------------------------------------------------------ servidor HTTP

    void AcceptLoop()
    {
        while (running)
        {
            try
            {
                var client = listener.AcceptTcpClient();
                new Thread(() => HandleClient(client)) { IsBackground = true, Name = "PhoneStreamerClient" }.Start();
            }
            catch
            {
                if (!running) break;
            }
        }
    }

    void HandleClient(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                stream.ReadTimeout = 5000;
                string path = ReadRequestPath(stream);
                if (path == null) return;

                if (path.StartsWith("/stream")) ServeStream(stream, false);
                else if (path.StartsWith("/frame.jpg")) ServeStream(stream, true);
                else if (path.StartsWith("/mode"))
                {
                    ApplySettings(path);
                    WriteResponse(stream, "200 OK", "application/json", Encoding.UTF8.GetBytes(SettingsJson()));
                }
                else if (path == "/" || path.StartsWith("/?") || path.StartsWith("/index"))
                    WriteResponse(stream, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page()));
                else
                    WriteResponse(stream, "404 Not Found", "text/plain", Encoding.UTF8.GetBytes("404"));
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }
    }

    void ApplySettings(string path)
    {
        int q = path.IndexOf('?');
        if (q < 0) return;
        var inv = CultureInfo.InvariantCulture;
        foreach (var kv in path.Substring(q + 1).Split('&'))
        {
            var p = kv.Split('=');
            if (p.Length != 2 || !float.TryParse(p[1], NumberStyles.Float, inv, out float v)) continue;
            switch (p[0])
            {
                case "stereo": stereo = v > 0.5f; break;
                case "ipd": ipd = Mathf.Clamp(v, 40f, 80f) / 1000f; break;   // mm
                case "k1": k1 = Mathf.Clamp(v, 0f, 1f); break;
                case "zoom": zoom = Mathf.Clamp(v, 0.5f, 2f); break;
                case "fov": eyeFov = Mathf.Clamp(v, 50f, 110f); break;
            }
        }
    }

    string SettingsJson()
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Format(inv, "{{\"stereo\":{0},\"ipd\":{1:F0},\"k1\":{2:F2},\"zoom\":{3:F2},\"fov\":{4:F0}}}",
            stereo ? 1 : 0, ipd * 1000f, k1, zoom, eyeFov);
    }

    static string ReadRequestPath(NetworkStream s)
    {
        var buf = new byte[8192];
        int total = 0;
        while (total < buf.Length)
        {
            int n = s.Read(buf, total, buf.Length - total);
            if (n <= 0) break;
            total += n;
            string sofar = Encoding.ASCII.GetString(buf, 0, total);
            if (sofar.Contains("\r\n\r\n"))
            {
                var first = sofar.Substring(0, sofar.IndexOf("\r\n", StringComparison.Ordinal)).Split(' ');
                return first.Length >= 2 ? first[1] : "/";
            }
        }
        return null;
    }

    static void WriteResponse(NetworkStream s, string status, string type, byte[] body)
    {
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n");
        s.Write(header, 0, header.Length);
        s.Write(body, 0, body.Length);
    }

    void ServeStream(NetworkStream s, bool single)
    {
        Interlocked.Increment(ref clients);
        try
        {
            if (!single)
            {
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: multipart/x-mixed-replace; boundary=frame\r\n" +
                    "Cache-Control: no-cache, no-store\r\nPragma: no-cache\r\nConnection: close\r\n\r\n");
                s.Write(header, 0, header.Length);
            }

            int lastSent = -1;
            int startId;
            lock (frameLock) startId = frameId;
            while (running)
            {
                byte[] jpg;
                int id;
                lock (frameLock)
                {
                    // Espera un cuadro nuevo (para /frame.jpg, uno capturado después de la petición)
                    while (running && (frameId == lastSent || (single && frameId == startId)))
                        if (!Monitor.Wait(frameLock, 2000) && single) return;
                    jpg = latestJpeg;
                    id = frameId;
                }
                if (jpg == null) { lastSent = id; continue; }

                if (single)
                {
                    WriteResponse(s, "200 OK", "image/jpeg", jpg);
                    return;
                }
                var part = Encoding.ASCII.GetBytes($"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {jpg.Length}\r\n\r\n");
                s.Write(part, 0, part.Length);
                s.Write(jpg, 0, jpg.Length);
                s.Write(new byte[] { 13, 10 }, 0, 2);
                lastSent = id;
            }
        }
        finally
        {
            Interlocked.Decrement(ref clients);
        }
    }

    string Page()
    {
        return @"<!doctype html><html lang=es><head><meta charset=utf-8>
<meta name=viewport content='width=device-width,initial-scale=1,viewport-fit=cover,user-scalable=no'>
<meta name=apple-mobile-web-app-capable content=yes>
<meta name=mobile-web-app-capable content=yes>
<meta name=apple-mobile-web-app-status-bar-style content=black-translucent>
<meta name=apple-mobile-web-app-title content='Rover VR'>
<title>Rover VR</title>
<style>
html,body{margin:0;height:100%;background:#000;overflow:hidden;-webkit-user-select:none;touch-action:none;font-family:-apple-system,system-ui,sans-serif}
img{position:fixed;inset:0;width:100vw;height:100vh;object-fit:contain}
#ui{position:fixed;inset:auto 0 0 0;padding:12px max(12px,env(safe-area-inset-right)) max(12px,env(safe-area-inset-bottom)) max(12px,env(safe-area-inset-left));
 background:linear-gradient(transparent,rgba(0,0,0,.85));color:#eee;transition:opacity .4s}
#ui.hide{opacity:0;pointer-events:none}
.row{display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-top:6px}
button{font-size:15px;padding:9px 14px;border:0;border-radius:10px;background:rgba(60,60,60,.9);color:#fff}
button.on{background:#d9622b}
label{font-size:13px;display:flex;align-items:center;gap:6px}
input[type=range]{width:120px;accent-color:#d9622b}
#rot{position:fixed;inset:0;display:none;align-items:center;justify-content:center;color:#fff;font-size:18px;text-align:center;background:#000;padding:30px}
@media (orientation:portrait){#rot{display:flex}}
</style></head><body>
<img id=v src='/stream' alt=''>
<div id=rot>Gira el celular en horizontal y colócalo en el visor.<br><br>Para pantalla completa: Compartir → Agregar a inicio.</div>
<div id=ui>
 <div class=row><button id=c>Gafas VR</button><button id=n>Normal</button><button id=h>Ocultar</button>
  <span style='font-size:12px;opacity:.7'>Toca la pantalla para mostrar estos controles</span></div>
 <div class=row id=vr>
  <label>Ojos <input id=ipd type=range min=50 max=75 step=1> <span id=ipdv></span>mm</label>
  <label>Lente <input id=k1 type=range min=0 max=0.6 step=0.02> <span id=k1v></span></label>
  <label>Zoom <input id=zoom type=range min=0.7 max=1.6 step=0.02> <span id=zoomv></span></label>
  <label>Campo <input id=fov type=range min=60 max=110 step=1> <span id=fovv></span>°</label>
 </div>
</div>
<script>
var ui=document.getElementById('ui'),v=document.getElementById('v'),t;
function $(i){return document.getElementById(i);}
function show(s){$('c').className=s.stereo?'on':'';$('n').className=s.stereo?'':'on';$('vr').style.display=s.stereo?'flex':'none';
 ['ipd','k1','zoom','fov'].forEach(function(k){$(k).value=s[k];$(k+'v').textContent=s[k];});}
function send(q){fetch('/mode?'+q).then(function(r){return r.json();}).then(show);}
function autoHide(){clearTimeout(t);t=setTimeout(function(){ui.classList.add('hide');},4000);}
$('c').onclick=function(e){e.stopPropagation();send('stereo=1');autoHide();};
$('n').onclick=function(e){e.stopPropagation();send('stereo=0');autoHide();};
$('h').onclick=function(e){e.stopPropagation();ui.classList.add('hide');};
['ipd','k1','zoom','fov'].forEach(function(k){$(k).oninput=function(){$(k+'v').textContent=this.value;send(k+'='+this.value);autoHide();};
 $(k).onclick=function(e){e.stopPropagation();};});
ui.onclick=function(e){e.stopPropagation();};
document.body.onclick=function(){ui.classList.remove('hide');autoHide();};
v.onerror=function(){setTimeout(function(){v.src='/stream?'+Date.now();},1000);};
send('');autoHide();
</script></body></html>";
    }

    static List<string> LocalIPv4()
    {
        var wifi = new List<string>();
        var other = new List<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
            string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
            if (d.Contains("virtual") || d.Contains("vethernet") || d.Contains("vmware") || d.Contains("hyper-v") || d.Contains("bluetooth")) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                string ip = ua.Address.ToString();
                if (ip.StartsWith("169.254.")) continue;
                (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? wifi : other).Add(ip);
            }
        }
        wifi.AddRange(other);
        return wifi;
    }
}
