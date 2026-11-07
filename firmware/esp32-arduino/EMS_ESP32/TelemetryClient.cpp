#include "TelemetryClient.h"
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include <mbedtls/base64.h>
#include <mbedtls/md.h>
#include <mbedtls/sha256.h>
#include <esp_system.h>
#include <time.h>
#include <math.h>

namespace {
String toUpperHex(const uint8_t* bytes, size_t length) {
  static const char* digits = "0123456789ABCDEF";
  String output; output.reserve(length * 2);
  for (size_t i = 0; i < length; ++i) { output += digits[(bytes[i] >> 4) & 0x0F]; output += digits[bytes[i] & 0x0F]; }
  return output;
}
bool sha256Hex(const String& value, String& output) {
  uint8_t digest[32];
  if (mbedtls_sha256_ret(reinterpret_cast<const unsigned char*>(value.c_str()), value.length(), digest, 0) != 0) return false;
  output = toUpperHex(digest, sizeof(digest)); return true;
}
}
TelemetryClient::TelemetryClient(const char* url, const char* code, const String& secret, const char* ca)
    : baseUrl(url), deviceCode(code), encodedSecret(secret), rootCa(ca) {
  while (baseUrl.endsWith("/")) baseUrl.remove(baseUrl.length() - 1);
}
bool TelemetryClient::decodeSecret(uint8_t* output, size_t& outputLength) const {
  String normalized = encodedSecret; normalized.replace('-', '+'); normalized.replace('_', '/');
  while (normalized.length() % 4 != 0) normalized += '=';
  return mbedtls_base64_decode(output, 64, &outputLength,
    reinterpret_cast<const unsigned char*>(normalized.c_str()), normalized.length()) == 0 && outputLength == 32;
}
String TelemetryClient::createNonce() const {
  char nonce[17]; snprintf(nonce, sizeof(nonce), "%08lX%08lX",
    (unsigned long)esp_random(), (unsigned long)esp_random()); return String(nonce);
}
String TelemetryClient::createSampleId() const {
  uint8_t b[16];
  for (size_t i = 0; i < 16; i += 4) { uint32_t r = esp_random(); memcpy(b + i, &r, 4); }
  b[6] = (b[6] & 0x0F) | 0x40; b[8] = (b[8] & 0x3F) | 0x80;
  char id[37];
  snprintf(id, sizeof(id), "%02x%02x%02x%02x-%02x%02x-%02x%02x-%02x%02x-%02x%02x%02x%02x%02x%02x",
    b[0],b[1],b[2],b[3],b[4],b[5],b[6],b[7],b[8],b[9],b[10],b[11],b[12],b[13],b[14],b[15]);
  return String(id);
}
bool TelemetryClient::createSignature(const String& method, const String& path, const String& timestamp,
    const String& nonce, const String& body, String& signature) const {
  uint8_t key[64]; size_t keyLength = 0;
  if (!decodeSecret(key, keyLength)) return false;
  String bodyHash;
  if (!sha256Hex(body, bodyHash)) { memset(key, 0, sizeof(key)); return false; }
  const String canonical = method + "\n" + path + "\n" + timestamp + "\n" + nonce + "\n" + bodyHash;
  uint8_t mac[32];
  const mbedtls_md_info_t* info = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  const int result = info ? mbedtls_md_hmac(info, key, keyLength,
    reinterpret_cast<const unsigned char*>(canonical.c_str()), canonical.length(), mac) : -1;
  memset(key, 0, sizeof(key));
  if (result != 0) return false;
  signature = toUpperHex(mac, sizeof(mac)); return true;
}
bool TelemetryClient::signedPost(const char* path, const String& body, int expectedStatus) {
  if (!rootCa || !*rootCa || strstr(rootCa, "PASTE_TRUSTED_ROOT_CA") != nullptr) {
    Serial.println("[tls] Trusted CA missing; refusing request."); return false;
  }
  const time_t now = time(nullptr);
  if (now < 1700000000) { Serial.println("[ntp] Clock invalid; refusing signed request."); return false; }
  char timestampBuffer[16]; snprintf(timestampBuffer, sizeof(timestampBuffer), "%lld", (long long)now);
  const String timestamp(timestampBuffer), nonce = createNonce();
  String signature;
  if (!createSignature("POST", path, timestamp, nonce, body, signature)) return false;
  WiFiClientSecure client; client.setCACert(rootCa);
  HTTPClient http;
  if (!http.begin(client, baseUrl + path)) return false;
  http.setTimeout(10000); http.addHeader("Content-Type", "application/json");
  http.addHeader("X-EMS-Device", deviceCode); http.addHeader("X-EMS-Timestamp", timestamp);
  http.addHeader("X-EMS-Nonce", nonce); http.addHeader("X-EMS-Signature", signature);
  const int status = http.POST(body); http.end(); return status == expectedStatus;
}
bool TelemetryClient::publish(const PzemReading& r) {
  if (!r.valid) return false;
  StaticJsonDocument<512> doc;
  doc["schemaVersion"] = 1; doc["sampleId"] = createSampleId();
  char capturedAt[25]; const time_t now = time(nullptr); struct tm utc;
  gmtime_r(&now, &utc); strftime(capturedAt, sizeof(capturedAt), "%Y-%m-%dT%H:%M:%SZ", &utc);
  doc["capturedAtUtc"] = capturedAt;
  JsonObject m = doc.createNestedObject("measurements");
  m["voltageVolts"] = r.voltageVolts; m["currentAmps"] = r.currentAmps;
  m["activePowerWatts"] = r.activePowerWatts; m["energyWattHours"] = r.energyWattHours;
  if (isfinite(r.frequencyHertz)) m["frequencyHertz"] = r.frequencyHertz;
  if (isfinite(r.powerFactor)) m["powerFactor"] = r.powerFactor;
  String body; serializeJson(doc, body);
  return signedPost("/api/device-telemetry", body, HTTP_CODE_ACCEPTED);
}
bool TelemetryClient::heartbeat() { return signedPost("/api/device-auth/heartbeat", "", HTTP_CODE_OK); }
