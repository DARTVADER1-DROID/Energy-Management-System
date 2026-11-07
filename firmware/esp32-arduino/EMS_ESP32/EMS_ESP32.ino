#include <Arduino.h>
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <Preferences.h>
#include <ArduinoJson.h>
#include <time.h>
#include "Config.h"
#include "PzemReader.h"
#include "TelemetryClient.h"

namespace {
Preferences preferences;
PzemReader meter(PZEM_RX_PIN, PZEM_TX_PIN);
TelemetryClient* telemetry = nullptr;
String deviceSecret;
unsigned long lastTelemetryMs = 0;
unsigned long lastHeartbeatMs = 0;

bool connectWiFi() {
  if (WiFi.status() == WL_CONNECTED) return true;
  WiFi.mode(WIFI_STA); WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  const unsigned long started = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - started < WIFI_CONNECT_TIMEOUT_MS) delay(250);
  return WiFi.status() == WL_CONNECTED;
}
bool synchronizeClock() {
  configTime(0, 0, NTP_SERVER_1, NTP_SERVER_2);
  const unsigned long started = millis();
  while (time(nullptr) < 1700000000 && millis() - started < NTP_SYNC_TIMEOUT_MS) delay(250);
  return time(nullptr) >= 1700000000;
}
bool provisionDevice() {
  if (strlen(EMS_PROVISIONING_CODE) == 0 || strlen(EMS_ROOT_CA_CERT) == 0 ||
      strstr(EMS_ROOT_CA_CERT, "PASTE_TRUSTED_ROOT_CA") != nullptr) {
    Serial.println("[provision] Provisioning code or trusted root CA is not configured."); return false;
  }
  WiFiClientSecure client; client.setCACert(EMS_ROOT_CA_CERT);
  HTTPClient http;
  if (!http.begin(client, String(EMS_API_BASE_URL) + "/api/device-auth/provision")) return false;
  http.addHeader("Content-Type", "application/json");
  StaticJsonDocument<256> request;
  request["deviceCode"] = EMS_DEVICE_CODE; request["provisioningCode"] = EMS_PROVISIONING_CODE;
  String body; serializeJson(request, body);
  const int status = http.POST(body); const String response = http.getString(); http.end();
  if (status != HTTP_CODE_OK) { Serial.printf("[provision] Backend returned HTTP %d\n", status); return false; }
  StaticJsonDocument<512> result;
  if (deserializeJson(result, response) || !result["deviceSecret"].is<const char*>()) {
    Serial.println("[provision] Invalid provisioning response."); return false;
  }
  deviceSecret = result["deviceSecret"].as<String>();
  preferences.begin("ems-device", false);
  const size_t stored = preferences.putString("secret", deviceSecret);
  preferences.end();
  if (stored == 0) { Serial.println("[provision] NVS credential write failed."); deviceSecret = ""; return false; }
  Serial.println("[provision] Device credential stored in ESP32 NVS."); return true;
}
void loadCredential() {
  preferences.begin("ems-device", true); deviceSecret = preferences.getString("secret", ""); preferences.end();
}
void createTelemetryClient() {
  if (telemetry == nullptr && deviceSecret.length() > 0)
    telemetry = new TelemetryClient(EMS_API_BASE_URL, EMS_DEVICE_CODE, deviceSecret, EMS_ROOT_CA_CERT);
}
}

void setup() {
  Serial.begin(115200); delay(500);
  Serial.println("\nEMS ESP32 firmware booting (monitoring-only build).");
  meter.begin(); loadCredential();
  if (!connectWiFi()) { Serial.println("[wifi] Connection failed; will retry."); return; }
  if (!synchronizeClock()) { Serial.println("[ntp] Clock sync failed; signed requests are disabled."); return; }
  if (deviceSecret.length() == 0 && !provisionDevice()) {
    Serial.println("[provision] Not completed; check configuration and reboot."); return;
  }
  createTelemetryClient(); Serial.println("[boot] Initialization complete.");
}
void loop() {
  if (!connectWiFi()) { delay(1000); return; }
  if (time(nullptr) < 1700000000 && !synchronizeClock()) { delay(1000); return; }
  if (deviceSecret.length() == 0) {
    loadCredential();
    if (deviceSecret.length() == 0) { provisionDevice(); delay(1000); return; }
  }
  createTelemetryClient();
  if (telemetry == nullptr) { delay(1000); return; }
  const unsigned long now = millis();
  if (now - lastTelemetryMs >= TELEMETRY_INTERVAL_MS) {
    lastTelemetryMs = now;
    const PzemReading reading = meter.read();
    if (reading.valid) {
      if (!telemetry->publish(reading)) Serial.println("[telemetry] Publish failed; next sample will retry.");
    } else Serial.println("[pzem] Invalid meter sample; not sending fabricated zero values.");
  }
  if (now - lastHeartbeatMs >= HEARTBEAT_INTERVAL_MS) {
    lastHeartbeatMs = now;
    if (!telemetry->heartbeat()) Serial.println("[heartbeat] Heartbeat failed.");
  }
  delay(20);
}
