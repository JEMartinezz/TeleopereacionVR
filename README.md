# Teleoperación de Robot Móvil en Entorno Virtual con Control Háptico Inalámbrico

Práctica de Realidad Virtual, Ingeniería Mecatrónica (UMNG).

```
TeleoperacionVR/
├── TeleoperacionRobotVR/            Proyecto Unity 6 (6000.6.2f1)
│   ├── Assets/Scenes/Teleoperacion.unity
│   ├── Assets/Scripts/              Lógica en tiempo de ejecución
│   ├── Assets/Editor/SceneBuilder.cs  Genera la escena (menú Teleoperación)
│   └── Resultados/                  CSV de cada sesión (se crea al dar Play)
├── CAD/                             Modelos exportados de SolidWorks (STL)
└── Firmware/ESP32_ControlHaptico/   Código del control háptico (ESP32 + MPU6050 + joystick + vibrador)
```

## 1. Qué hay en la escena (Fase 2 de la guía)

| Requisito de la guía | Implementación |
|---|---|
| Escenario diseñado | Arena de 20 × 20 m con piso cuadriculado ([SceneBuilder.cs](TeleoperacionRobotVR/Assets/Editor/SceneBuilder.cs)) |
| Límites físicos | 4 paredes con collider + barreras invisibles altas, para que nada salga del área |
| Obstáculos | 2 cajas, 2 muros, 3 columnas (naranja) |
| Objetos manipulables | 3 cubos y 1 esfera con Rigidbody (masa 0,4 kg) |
| Robot móvil con física | Rigidbody diferencial manejado con el **joystick** ([RobotController.cs](TeleoperacionRobotVR/Assets/Scripts/RobotController.cs)) |
| Cámara que sigue al carro | Va ligada al carro y su ángulo se controla con la **inclinación de la MPU6050** ([ChaseCamera.cs](TeleoperacionRobotVR/Assets/Scripts/ChaseCamera.cs)) |
| Agarre (attach/detach) | Garra que se cierra con el **botón del joystick** ([RobotGripper.cs](TeleoperacionRobotVR/Assets/Scripts/RobotGripper.cs)) |
| Detección de colisiones + háptica | `OnCollisionEnter` + `HapticSurface` → el **motor vibrador (PWM)** vibra con un patrón según el objeto |
| Navegación en primera persona | `Avatar` (tecla **V** para cambiar a su vista; W/S para caminar) |
| Visor VR | OpenXR para PC (Meta Quest 3 por Link) y transmisión al iPhone por Wi‑Fi |
| Tarea medible | Llevar los 4 objetos a la zona verde (esquina NE); se mide el tiempo |

## 2. Control háptico ESP32

Código: [ESP32_ControlHaptico.ino](Firmware/ESP32_ControlHaptico/ESP32_ControlHaptico.ino). En Arduino IDE la placa es **ESP32 Dev Module** y no necesita librerías externas.

### Conexiones

| Componente | Pin del módulo | ESP32 |
|---|---|---|
| **MPU6050** | VCC / GND | 3V3 / GND |
| | SDA / SCL | **GPIO21** / **GPIO22** |
| **Joystick KY-023** | +5V / GND | **3V3** (no 5 V) / GND |
| | VRx (giro) | **GPIO34** |
| | VRy (avance) | **GPIO35** |
| | SW (botón → garra) | **GPIO32** (pull-up interno) |
| **Motor vibrador** | a través de un transistor | **GPIO26** (PWM 5 kHz, 8 bits) |

Para conectar el motor vibrador: GPIO26 → resistencia de 1 kΩ → base de un 2N2222. El emisor va a GND, el colector al (–) del motor, y el (+) del motor a 3V3 o VIN. Pon un diodo 1N4148 en paralelo con el motor. Si tu vibrador es un módulo con driver incluido, conecta IN → GPIO26.

* Al encender el ESP32, deja el control **quieto** y el joystick **sin tocar** durante 1 s, para que calibre el giroscopio y el centro del joystick.
* Si la MPU6050 no aparece, el firmware lo vuelve a intentar cada 2 s. Para diagnosticar, abre el Monitor Serie (115200 baudios, "LF") y escribe `S`: muestra la IMU, los valores crudos del joystick y un escaneo del bus I²C.
* Si el carro avanza o gira al revés, cambia `INVERT_JOY_Y` o `INVERT_JOY_X` en el firmware, o `Invert Forward` / `Invert Turn` en Unity.

### Qué hace cada cosa

| Control ESP32 | En Unity | Teclado (sin ESP32) |
|---|---|---|
| Joystick ↑/↓ | El carro avanza o retrocede | ↑ / ↓ |
| Joystick ←/→ | El carro gira | ← / → |
| Presionar el joystick | Cierra o abre la garra (agarra el objeto que tenga entre los dedos) | Espacio |
| Inclinar la MPU6050 adelante/atrás (pitch) | La cámara sube o baja alrededor del carro | I / K |
| Inclinar la MPU6050 a los lados (roll) | La cámara rodea el carro hacia la izquierda o la derecha | J / L |
| | Calibrar: la postura actual de la MPU pasa a ser el cero | C |
| | Cambiar entre la cámara del carro y la vista del avatar | V |
| | Mostrar u ocultar el HUD | H |

### Vibración al chocar (PWM)

| Choque del carro con… | Patrón |
|---|---|
| Objeto manipulable | 2 toques cortos y suaves (duty 150) |
| Pared o límite | 1 golpe largo (0,4 s) a máxima intensidad (duty 255) |
| Obstáculo | 3 pulsos medios (duty 210) |

### Protocolo (texto, 115200 baudios, por USB o Bluetooth "ControlHaptico_UMNG")

```
ESP32 → PC   D,<pitch>,<roll>,<joyX>,<joyY>,<boton>   50 Hz   (°, °, −1..1, −1..1, 0/1)
             K,<id>                                   respuesta a ping (latencia)
PC → ESP32   V,<1|2|3>                                vibrar
             P,<id>                                   ping
             C                                        recalibrar
```

Unity encuentra el ESP32 automáticamente ([HapticLink.cs](TeleoperacionRobotVR/Assets/Scripts/HapticLink.cs)): prueba primero el último puerto que funcionó y después los puertos USB (CP210x/CH340) antes que los de Bluetooth. **Cierra el Monitor Serie de Arduino antes de pulsar Play**, porque si no el puerto queda ocupado.

## 3. Mapeo matemático

```
Carro (joystick j ∈ [−1, 1]):
  u(j) = sign(j) · clamp01((|j| − j_m) / (1 − j_m))         j_m = 0,12
  v*   = v_max · u(joyY)        v_max = 1,2 m/s
  ω*   = ω_max · u(joyX)        ω_max = 90 °/s
  v_k  = v_{k−1} + (v* − v_{k−1}) · (1 − e^(−λ·Δt))          λ = 6 s⁻¹

Cámara (IMU, filtro complementario α = 0,98 en el ESP32):
  elevación = 22° + 1,2 · pitch          limitada a [3°, 80°]
  azimut    = yaw_carro + 3 · roll        limitado a ±150°
  posición  = carro + R(elevación, azimut) · (0, 0, −2,2 m)
```

El modo *Umbral* del RobotController (u = sign(j) fuera de la zona muerta) sirve para comparar con el control proporcional, tal como pregunta la guía.

## 4. Conexión inalámbrica

```
 ESP32 (batería) ──Bluetooth SPP──► PC con Unity ──Wi‑Fi (MJPEG)──► iPhone en visor Cardboard
  MPU6050, joystick,                 física, cámara, render              gafas de realidad virtual
  vibrador PWM       ◄── vibrar ───   estéreo + lentes
```

### ESP32 ↔ PC por Bluetooth

1. Alimenta el ESP32 con la batería (LiPo + cargador TP4056 → pin VIN/5V), sin cable USB.
2. En Windows ve a **Configuración → Bluetooth y dispositivos → Agregar dispositivo → Bluetooth** y elige **ControlHaptico_UMNG**.
3. Pulsa Play. Unity lee del registro de Windows qué puerto COM pertenece a "ControlHaptico_UMNG" y se conecta solo. Ignora los puertos de audífonos y celulares. Si el USB también está conectado, usa el USB.
4. La conexión Bluetooth tarda unos segundos al principio. El HUD muestra el puerto, los 50 Hz y la latencia RTT.

### Celular como gafas de realidad virtual (Wi‑Fi)

Unity Remote 5 no permite conectar un iPhone a Unity en Windows. Por eso [PhoneStreamer.cs](TeleoperacionRobotVR/Assets/Scripts/PhoneStreamer.cs) hace de visor: renderiza la cámara que sigue al rover **dos veces** (ojo izquierdo y derecho, separados 64 mm), aplica la **pre-distorsión de barril** para los lentes del Cardboard ([CardboardDistortion.shader](TeleoperacionRobotVR/Assets/Shaders/CardboardDistortion.shader)) y lo envía como video.

1. El PC y el iPhone deben estar en la misma red Wi‑Fi (sirve el Hotspot del iPhone).
2. Pulsa Play. El HUD muestra la dirección, por ejemplo `http://172.20.10.8:8765`.
3. Ábrela en **Safari**. Para verla en pantalla completa: **Compartir → Agregar a inicio**, y luego ábrela desde el ícono "Rover VR".
4. Gira el celular en horizontal y colócalo en el visor. Si tocas la pantalla aparecen los ajustes: **Ojos** (IPD), **Lente** (distorsión), **Zoom** y **Campo** de visión.
5. La primera vez: pon la red como *Privada* y permite el puerto en el Firewall (PowerShell como administrador):
   `New-NetFirewallRule -DisplayName "Teleoperacion VR - celular" -Direction Inbound -Protocol TCP -LocalPort 8765 -Action Allow -Profile Private`
6. Desactiva el bloqueo automático del iPhone (Configuración → Pantalla y brillo) para que no se apague.

> La cabeza del usuario la representa la MPU6050: al inclinarla, la cámara de las gafas gira alrededor del rover. Safari en iOS solo entrega el giroscopio del celular en páginas con HTTPS, por eso no se usa.

## 4b. Temática visual: base de exploración en Marte

* **Terreno** low-poly con dunas y montañas, textura de suelo marciano generada por código, cielo color caramelo con halo azul alrededor del Sol y niebla de polvo.
* **Rover** de 6 ruedas con panel solar, mástil con cámara, antena, faros reales y garra. Levanta polvo al moverse y suelta chispas, además de sacudir la cámara, al chocar.
* **Obstáculos**, todos con collider sólido: módulo habitacional (domos y túnel), cajas de suministros, campo de paneles solares, tanques de combustible y 8 rocas con collider convexo.
* **Límites**: barreras con franja de peligro y balizas pulsantes, más un muro invisible que impide salir.
* **Muestras** manipulables: cristales luminosos (cian, verde, magenta) y un orbe ámbar, con luz propia.
* **Zona de entrega**: plataforma con anillo verde y luces pulsantes.
* **Post-procesado**: bloom, tonemapping ACES, oclusión ambiental, viñeta y grano.

El menú **Teleoperación → Diagnóstico** tiene la **prueba de colisiones**: lanza el rover contra cada obstáculo con la física real y guarda `Capturas/prueba_colisiones.csv` (resultado actual: 15 de 15 bloquean). También genera las **capturas** de la escena para el informe.

## 5. Resultados para el informe

Cada sesión de Play crea en `TeleoperacionRobotVR/Resultados/`:

* `eventos_*.csv`: colisiones por tipo, agarres, entregas, inicio y fin de tarea, con la latencia en cada evento.
* `telemetria_*.csv`: a 20 Hz registra pitch y roll de la IMU, joystick X/Y, v, ω, botón, estado de la garra, latencia y fuente (esp32 o teclado).

El HUD muestra en vivo el puerto, la tasa de datos (50 Hz), la latencia RTT (última, promedio y máxima), la IMU, el joystick, el estado de la garra, las colisiones y el tiempo de la tarea.

## 6. Regenerar la escena

Menú **Teleoperación → Construir escena**. La cámara de seguimiento y la transmisión al celular se crean solas al dar Play.
