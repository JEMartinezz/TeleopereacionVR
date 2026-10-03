/*
  Control háptico – Teleoperación de robot móvil en entorno virtual (UMNG, Realidad Virtual)

  Funciones:
    - MPU6050 (inclinación pitch / roll)  -> mueve la CÁMARA que sigue al carro en Unity
    - Joystick analógico (KY-023)          -> mueve el CARRO (Y = avance/retroceso, X = giro)
    - Botón del joystick (presionar)        -> cierra / abre la GARRA
    - Motor vibrador por PWM                -> vibra cuando el carro choca (patró
    n distinto según el objeto)

  ======================= CONEXIONES (ESP32 DevKit, 3.3 V) =======================
    MPU6050      VCC -> 3V3      GND -> GND      SDA -> GPIO21     SCL -> GPIO22
    Joystick     +5V -> 3V3 (¡no a 5 V!)  GND -> GND
                 VRx -> GPIO34   VRy -> GPIO35   SW -> GPIO32  (pull-up interno)
    Vibrador     GPIO26 -> resistencia 1 kΩ -> base transistor NPN 2N2222 (o S8050)
                 emisor -> GND ; colector -> (–) del motor ; (+) del motor -> 3V3 o 5V(VIN)
                 diodo 1N4148 en paralelo al motor (cátodo hacia +)
                 * Si usas un MÓDULO vibrador con driver: IN -> GPIO26, VCC -> 3V3, GND -> GND
    LED de la placa: GPIO2 (parpadea al calibrar, encendido con cliente Bluetooth)
  ================================================================================

  Al encender: dejar el control QUIETO y el joystick SIN TOCAR ~1 s (calibra giroscopio y centro del joystick).

  Protocolo con Unity (texto, 115200 baudios, por USB y también por Bluetooth "ControlHaptico_UMNG"):
    ESP32 -> PC : D,<pitch>,<roll>,<joyX>,<joyY>,<boton>     50 Hz  (ángulos en °, joystick -1.00..1.00, botón 0/1)
                  K,<id>                                     respuesta a ping (latencia)
    PC -> ESP32 : V,<patrón>   1 = objeto manipulable, 2 = pared/límite, 3 = obstáculo
                  P,<id>       ping

  Placa: "ESP32 Dev Module". Núcleo Arduino-ESP32 2.x o 3.x. Sin librerías externas.
*/

#include <Wire.h>

// Bluetooth Classic solo existe en el ESP32 original (no en S2/S3/C3). Si no está, funciona solo por USB.
#if defined(CONFIG_BT_ENABLED) && defined(CONFIG_BLUEDROID_ENABLED) && !defined(CONFIG_IDF_TARGET_ESP32S3) && !defined(CONFIG_IDF_TARGET_ESP32C3) && !defined(CONFIG_IDF_TARGET_ESP32S2)
#define USE_BT 1
#include "BluetoothSerial.h"
BluetoothSerial SerialBT;
#else
#define USE_BT 0
#endif

// ---------------- Pines
const int PIN_SDA = 21, PIN_SCL = 22;
const int PIN_JOY_X = 34, PIN_JOY_Y = 35, PIN_JOY_SW = 32;
const int PIN_MOTOR = 26;
const int PIN_LED = 2;

// ---------------- Ajustes
const bool INVERT_JOY_X = false;   // cambia a true si el giro sale al revés
const bool INVERT_JOY_Y = true;    // en el KY-023, empujar hacia delante BAJA el valor -> se invierte
const float ALPHA = 0.98f;         // filtro complementario de la IMU
const unsigned long SEND_PERIOD_MS = 20;   // 50 Hz

// ---------------- PWM del vibrador
const int PWM_FREQ = 5000;
const int PWM_RES = 8;   // duty 0..255
const int PWM_CH = 0;    // solo núcleo 2.x

// ---------------- Estado IMU
uint8_t MPU = 0x68;   // 0x69 si el pin AD0 del módulo está en alto (se detecta solo)
bool imuOk = false;
float gyroBiasX = 0, gyroBiasY = 0;
float pitch = 0, roll = 0;
unsigned long lastImuMicros = 0;

// ---------------- Estado joystick
int joyCenterX = 2048, joyCenterY = 2048;
float joyX = 0, joyY = 0;

unsigned long lastSend = 0;
String rxUsb, rxBt;

// ---------------- Patrones de vibración {duty 0..255, ms}, terminan en {0,0}
struct Step { uint8_t duty; uint16_t ms; };
const Step PAT_OBJ[]  = { {150, 70}, {0, 60}, {150, 70}, {0, 0} };                       // 2 toques suaves
const Step PAT_WALL[] = { {255, 400}, {0, 0} };                                          // 1 golpe largo e intenso
const Step PAT_OBS[]  = { {210, 50}, {0, 40}, {210, 50}, {0, 40}, {210, 50}, {0, 0} };   // 3 pulsos medios
const Step* curPattern = nullptr;
int curStep = 0;
unsigned long stepStart = 0;

// =============================================================== vibración

void motorWrite(uint8_t duty) {
#if ESP_ARDUINO_VERSION_MAJOR >= 3
  ledcWrite(PIN_MOTOR, duty);
#else
  ledcWrite(PWM_CH, duty);
#endif
}

void startPattern(int n) {
  switch (n) {
    case 1: curPattern = PAT_OBJ; break;
    case 2: curPattern = PAT_WALL; break;
    case 3: curPattern = PAT_OBS; break;
    default: return;
  }
  curStep = 0;
  stepStart = millis();
  motorWrite(curPattern[0].duty);
}

void updatePattern() {
  if (!curPattern) return;
  if (millis() - stepStart < curPattern[curStep].ms) return;
  curStep++;
  stepStart = millis();
  if (curPattern[curStep].ms == 0) { motorWrite(0); curPattern = nullptr; return; }
  motorWrite(curPattern[curStep].duty);
}

// =============================================================== MPU6050

void mpuWrite(uint8_t reg, uint8_t val) {
  Wire.beginTransmission(MPU); Wire.write(reg); Wire.write(val); Wire.endTransmission();
}

bool mpuRead(int16_t& ax, int16_t& ay, int16_t& az, int16_t& gx, int16_t& gy, int16_t& gz) {
  Wire.beginTransmission(MPU);
  Wire.write(0x3B);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(MPU, (uint8_t)14) != 14) return false;
  ax = Wire.read() << 8 | Wire.read();
  ay = Wire.read() << 8 | Wire.read();
  az = Wire.read() << 8 | Wire.read();
  Wire.read(); Wire.read();   // temperatura
  gx = Wire.read() << 8 | Wire.read();
  gy = Wire.read() << 8 | Wire.read();
  gz = Wire.read() << 8 | Wire.read();
  return true;
}

bool initImu() {
  imuOk = false;
  for (uint8_t a : { (uint8_t)0x68, (uint8_t)0x69 }) {
    Wire.beginTransmission(a);
    if (Wire.endTransmission() == 0) { MPU = a; imuOk = true; break; }
  }
  if (imuOk) {
    mpuWrite(0x6B, 0x00);   // despertar
    mpuWrite(0x1A, 0x03);   // filtro DLPF ~44 Hz
    mpuWrite(0x1B, 0x08);   // giroscopio ±500 °/s
    mpuWrite(0x1C, 0x00);   // acelerómetro ±2 g
    delay(100);
  }
  return imuOk;
}

void accelAngles(int16_t ax, int16_t ay, int16_t az, float& p, float& r) {
  float fax = ax / 16384.0f, fay = ay / 16384.0f, faz = az / 16384.0f;   // ±2 g
  p = atan2f(-fax, sqrtf(fay * fay + faz * faz)) * RAD_TO_DEG;
  r = atan2f(fay, faz) * RAD_TO_DEG;
}

void updateImu() {
  if (!imuOk) return;
  int16_t ax, ay, az, gx, gy, gz;
  if (!mpuRead(ax, ay, az, gx, gy, gz)) return;
  unsigned long now = micros();
  float dt = (now - lastImuMicros) * 1e-6f;
  lastImuMicros = now;
  if (dt <= 0 || dt > 0.1f) dt = 0.005f;

  float pa, ra;
  accelAngles(ax, ay, az, pa, ra);
  float gxd = (gx - gyroBiasX) / 65.5f;   // ±500 °/s
  float gyd = (gy - gyroBiasY) / 65.5f;

  // Filtro complementario: giroscopio (rápido) + acelerómetro (sin deriva)
  pitch = ALPHA * (pitch + gyd * dt) + (1 - ALPHA) * pa;
  roll  = ALPHA * (roll  + gxd * dt) + (1 - ALPHA) * ra;
}

// =============================================================== joystick

float readAxis(int pin, int center, bool invert) {
  int raw = analogRead(pin);
  float v = (raw >= center) ? (raw - center) / float(4095 - center) : (raw - center) / float(center);
  v = constrain(v, -1.0f, 1.0f);
  return invert ? -v : v;
}

void calibrate() {
  // Giroscopio y centro del joystick: control quieto, joystick suelto
  long sgx = 0, sgy = 0, sjx = 0, sjy = 0; int ng = 0;
  int16_t ax, ay, az, gx, gy, gz;
  for (int i = 0; i < 300; i++) {
    if (imuOk && mpuRead(ax, ay, az, gx, gy, gz)) { sgx += gx; sgy += gy; ng++; }
    sjx += analogRead(PIN_JOY_X);
    sjy += analogRead(PIN_JOY_Y);
    digitalWrite(PIN_LED, (i / 30) % 2);
    delay(3);
  }
  if (ng) { gyroBiasX = sgx / (float)ng; gyroBiasY = sgy / (float)ng; }
  joyCenterX = sjx / 300;
  joyCenterY = sjy / 300;
  if (imuOk && mpuRead(ax, ay, az, gx, gy, gz)) accelAngles(ax, ay, az, pitch, roll);
  lastImuMicros = micros();
  digitalWrite(PIN_LED, LOW);
}

// =============================================================== comandos de Unity

void handleLine(const String& l, Stream& out) {
  if (l.startsWith("V,")) startPattern(l.substring(2).toInt());
  else if (l.startsWith("P,")) { out.print("K,"); out.print(l.substring(2)); out.print('\n'); }
  else if (l == "C") calibrate();
  else if (l == "S") printStatus(out);
}

// Diagnóstico: escribe "S" en el Monitor Serie (115200, salto de línea "LF")
void printStatus(Stream& out) {
  out.printf("# IMU %s (0x%02X)  pitch=%.1f roll=%.1f\n", imuOk ? "OK" : "NO RESPONDE", MPU, pitch, roll);
  out.printf("# Joystick crudo X=%d Y=%d (centro %d/%d)  SW=%d\n",
             analogRead(PIN_JOY_X), analogRead(PIN_JOY_Y), joyCenterX, joyCenterY, digitalRead(PIN_JOY_SW) == LOW);
  out.print("# I2C encontrados:");
  int n = 0;
  for (uint8_t a = 1; a < 127; a++) {
    Wire.beginTransmission(a);
    if (Wire.endTransmission() == 0) { out.printf(" 0x%02X", a); n++; }
  }
  out.println(n ? "" : " ninguno (revisa cables SDA/SCL y alimentacion)");
}

void readStream(Stream& s, String& line) {
  while (s.available()) {
    char c = s.read();
    if (c == '\n') { line.trim(); handleLine(line, s); line = ""; }
    else if (line.length() < 32) line += c;
  }
}

// =============================================================== setup / loop

void setup() {
  Serial.begin(115200);
  pinMode(PIN_JOY_SW, INPUT_PULLUP);
  pinMode(PIN_LED, OUTPUT);
  analogReadResolution(12);
  analogSetAttenuation(ADC_11db);   // rango completo 0..3.3 V

#if ESP_ARDUINO_VERSION_MAJOR >= 3
  ledcAttach(PIN_MOTOR, PWM_FREQ, PWM_RES);
#else
  ledcSetup(PWM_CH, PWM_FREQ, PWM_RES);
  ledcAttachPin(PIN_MOTOR, PWM_CH);
#endif
  motorWrite(0);

  Wire.begin(PIN_SDA, PIN_SCL);
  Wire.setClock(400000);
  initImu();
  calibrate();

#if USE_BT
  SerialBT.begin("ControlHaptico_UMNG");
#endif
  Serial.println(imuOk ? "# MPU6050 OK" : "# ERROR: MPU6050 no responde (revisa SDA=21, SCL=22, 3V3, GND)");
  startPattern(1);   // vibración de bienvenida
}

unsigned long lastImuRetry = 0;

void loop() {
  // Si la IMU no respondió al encender, reintenta cada 2 s (por si se conecta después)
  if (!imuOk && millis() - lastImuRetry > 2000) {
    lastImuRetry = millis();
    if (initImu()) { calibrate(); Serial.println("# MPU6050 detectada"); }
  }
  updateImu();
  updatePattern();
  readStream(Serial, rxUsb);
#if USE_BT
  readStream(SerialBT, rxBt);
#endif

  unsigned long now = millis();
  if (now - lastSend >= SEND_PERIOD_MS) {
    lastSend = now;
    joyX = readAxis(PIN_JOY_X, joyCenterX, INVERT_JOY_X);
    joyY = readAxis(PIN_JOY_Y, joyCenterY, INVERT_JOY_Y);
    int sw = digitalRead(PIN_JOY_SW) == LOW;

    char buf[64];
    snprintf(buf, sizeof(buf), "D,%.2f,%.2f,%.2f,%.2f,%d\n", pitch, roll, joyX, joyY, sw);
    Serial.print(buf);
#if USE_BT
    if (SerialBT.hasClient()) SerialBT.print(buf);
    digitalWrite(PIN_LED, SerialBT.hasClient());
#endif
  }
  delay(2);
}
