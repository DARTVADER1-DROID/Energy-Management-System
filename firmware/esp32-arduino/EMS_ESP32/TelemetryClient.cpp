#include "TelemetryClient.h"
#include "FirmwareCore.h"
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include <Preferences.h>
#include <mbedtls/base64.h>
#include <mbedtls/md.h>
#include <esp_system.h>
#include <time.h>
#include <math.h>
#include <string.h>

namespace {
constexpr size_t MAX_PENDING_SAMPLES = ems::kMaxPendingSamples;
constexpr size_t MAX_PENDING_BYTES = 3500;
String toUpperHex(const uint8_t* bytes, size_t length) {
  static const char* digits = "0123456789ABCDEF";
  String output; output.reserve(length * 2);
  for (size_t i=0;i<length;++i) { output += digits[(bytes[i]>>4)&15]; output += digits[bytes[i]&15]; }
  return output;
}
bool sha256Hex(const String& value, String& output) {
  uint8_t digest[32];
  const auto* info=mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  if (!info || mbedtls_md(info,reinterpret_cast<const unsigned char*>(value.c_str()),value.length(),digest)!=0) return false;
  output=toUpperHex(digest,sizeof(digest)); return true;
}
}
TelemetryClient::TelemetryClient(const char* url,const char* code,const String& secret,const char* ca)
 : baseUrl(url),deviceCode(code),encodedSecret(secret),rootCa(ca) {
  while(baseUrl.endsWith("/")) baseUrl.remove(baseUrl.length()-1);
}
bool TelemetryClient::decodeSecret(uint8_t* output,size_t& outputLength) const {
  String normalized=encodedSecret; normalized.replace('-','+'); normalized.replace('_','/');
  while(normalized.length()%4) normalized+='=';
  return mbedtls_base64_decode(output,64,&outputLength,
    reinterpret_cast<const unsigned char*>(normalized.c_str()),normalized.length())==0 && outputLength==32;
}
String TelemetryClient::createNonce() const {
  char nonce[33];
  snprintf(nonce,sizeof(nonce),"%08lX%08lX%08lX%08lX",
    (unsigned long)esp_random(),(unsigned long)esp_random(),
    (unsigned long)esp_random(),(unsigned long)esp_random());
  return String(nonce);
}
String TelemetryClient::createSampleId() const {
  uint8_t b[16]; for(size_t i=0;i<16;i+=4){uint32_t r=esp_random();memcpy(b+i,&r,4);}
  b[6]=(b[6]&15)|64; b[8]=(b[8]&63)|128; char id[37];
  snprintf(id,sizeof(id),"%02x%02x%02x%02x-%02x%02x-%02x%02x-%02x%02x-%02x%02x%02x%02x%02x%02x",
    b[0],b[1],b[2],b[3],b[4],b[5],b[6],b[7],b[8],b[9],b[10],b[11],b[12],b[13],b[14],b[15]);
  return String(id);
}
bool TelemetryClient::createSignature(const String& method,const String& path,const String& timestamp,
 const String& nonce,const String& body,String& signature) const {
  uint8_t key[64]; size_t keyLength=0;
  if(!decodeSecret(key,keyLength)) return false;
  String hash; if(!sha256Hex(body,hash)){memset(key,0,sizeof(key));return false;}
  String canonical=method+"\n"+path+"\n"+timestamp+"\n"+nonce+"\n"+hash;
  uint8_t mac[32]; const auto* info=mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  int result=info?mbedtls_md_hmac(info,key,keyLength,reinterpret_cast<const unsigned char*>(canonical.c_str()),canonical.length(),mac):-1;
  memset(key,0,sizeof(key)); if(result) return false;
  signature=toUpperHex(mac,sizeof(mac)); memset(mac,0,sizeof(mac)); return true;
}
bool TelemetryClient::signedPost(const char* path,const String& body,int expectedStatus) {
  lastHttpStatus=0;
  if(!rootCa||!*rootCa||strstr(rootCa,"PASTE_TRUSTED_ROOT_CA")){Serial.println("[tls] Trusted CA missing; request blocked.");return false;}
  time_t now=time(nullptr); if(now<1700000000){Serial.println("[ntp] Invalid clock; signed request blocked.");return false;}
  char ts[16]; snprintf(ts,sizeof(ts),"%lld",(long long)now);
  String timestamp(ts),nonce=createNonce(),signature;
  if(!createSignature("POST",path,timestamp,nonce,body,signature)){Serial.println("[auth] Could not construct request signature.");return false;}
  WiFiClientSecure client; client.setCACert(rootCa); HTTPClient http;
  if(!http.begin(client,baseUrl+path)){Serial.println("[http] Could not initialize HTTPS connection.");return false;}
  http.setTimeout(10000); http.addHeader("Content-Type","application/json");
  http.addHeader("X-EMS-Device",deviceCode); http.addHeader("X-EMS-Timestamp",timestamp);
  http.addHeader("X-EMS-Nonce",nonce); http.addHeader("X-EMS-Signature",signature);
  int status=http.POST(body); lastHttpStatus=status;
  if(status<0) Serial.printf("[http] Transport/TLS failure (%d).\n",status);
  else if(status==expectedStatus || (expectedStatus==HTTP_CODE_ACCEPTED && status==HTTP_CODE_CONFLICT)) {
    if(status==HTTP_CODE_CONFLICT) Serial.println("[telemetry] Duplicate sample already exists; treating as delivered.");
    http.end(); return true;
  } else if(status==HTTP_CODE_UNAUTHORIZED) Serial.println("[auth] HTTP 401: credential rejected; check provisioning/rotation.");
  else if(status==HTTP_CODE_BAD_REQUEST) Serial.println("[telemetry] HTTP 400: payload rejected; inspect firmware/backend schema.");
  else if(status>=500) Serial.printf("[http] Backend HTTP %d; retry will be scheduled.\n",status);
  else Serial.printf("[http] Unexpected HTTP %d.\n",status);
  http.end(); return false;
}
bool TelemetryClient::enqueue(const String& payload) {
  Preferences p; if(!p.begin("ems-queue",false)) return false;
  String current=p.getString("items","[]"); DynamicJsonDocument doc(12288);
  if(deserializeJson(doc,current)){doc.clear();doc.to<JsonArray>();}
  JsonArray a=doc.as<JsonArray>();
  if(!ems::canEnqueue(a.size(),MAX_PENDING_SAMPLES)){p.end();Serial.println("[queue] Queue capacity reached; retaining existing samples and rejecting newest.");return false;}
  a.add(payload); String encoded; serializeJson(doc,encoded);
  bool ok=encoded.length()<=MAX_PENDING_BYTES && p.putString("items",encoded)>0;
  p.end();
  if(!ok) Serial.println("[queue] Persistent storage write failed or queue byte limit reached; existing samples retained.");
  return ok;
}
bool TelemetryClient::flushQueue() {
  Preferences p; if(!p.begin("ems-queue",false)) return false;
  String current=p.getString("items","[]"); p.end();
  DynamicJsonDocument doc(12288);
  if(deserializeJson(doc,current)){Serial.println("[queue] Corrupt queue; clearing invalid queue data.");p.begin("ems-queue",false);p.remove("items");p.end();return false;}
  JsonArray a=doc.as<JsonArray>(); bool delivered=false;
  while(a.size()>0) {
    String payload=a[0].as<String>();
    if(!signedPost("/api/device-telemetry",payload,HTTP_CODE_ACCEPTED)) {
      if(ems::classifyHttpStatus(lastHttpStatus,HTTP_CODE_ACCEPTED)==ems::HttpDisposition::PermanentReject) {
        Serial.println("[queue] Permanently rejected HTTP 400 sample removed so it cannot block newer data.");
        a.remove(0); String rejectedUpdate; serializeJson(doc,rejectedUpdate);
        if(!p.begin("ems-queue",false)) return delivered;
        p.putString("items",rejectedUpdate); p.end(); continue;
      }
      break;
    }
    a.remove(0); delivered=true;
    String updated; serializeJson(doc,updated);
    if(!p.begin("ems-queue",false)) return delivered;
    p.putString("items",updated); p.end();
  }
  return delivered;
}
bool TelemetryClient::publish(const PzemReading& r) {
  if(!r.valid || !isfinite(r.voltageVolts)||!isfinite(r.currentAmps)||
     !isfinite(r.activePowerWatts)||!isfinite(r.energyWattHours)||
     r.voltageVolts<0||r.voltageVolts>1000||r.currentAmps<0||r.currentAmps>10000||
     r.activePowerWatts < -10000000||r.activePowerWatts>10000000||
     r.energyWattHours<0||r.energyWattHours>1000000000000.0) {
    Serial.println("[telemetry] Sample outside backend-supported physical/schema bounds; discarded.");return false;
  }
  flushQueue();
  StaticJsonDocument<512> doc; doc["schemaVersion"]=1; doc["sampleId"]=createSampleId();
  char capturedAt[25]; time_t now=time(nullptr); struct tm utc;
  gmtime_r(&now,&utc); strftime(capturedAt,sizeof(capturedAt),"%Y-%m-%dT%H:%M:%SZ",&utc);
  doc["capturedAtUtc"]=capturedAt; JsonObject m=doc.createNestedObject("measurements");
  m["voltageVolts"]=r.voltageVolts; m["currentAmps"]=r.currentAmps;
  m["activePowerWatts"]=r.activePowerWatts; m["energyWattHours"]=r.energyWattHours;
  if(isfinite(r.frequencyHertz)&&r.frequencyHertz>=0&&r.frequencyHertz<=1000)m["frequencyHertz"]=r.frequencyHertz;
  if(isfinite(r.powerFactor)&&r.powerFactor>=-1&&r.powerFactor<=1)m["powerFactor"]=r.powerFactor;
  String body; serializeJson(doc,body);
  if(signedPost("/api/device-telemetry",body,HTTP_CODE_ACCEPTED)) return true;
  if(ems::classifyHttpStatus(lastHttpStatus,HTTP_CODE_ACCEPTED)==ems::HttpDisposition::PermanentReject) return false;
  if(enqueue(body)){Serial.println("[queue] Sample persisted for retry with its original ID and capture time.");}
  return false;
}
bool TelemetryClient::heartbeat(){return signedPost("/api/device-auth/heartbeat","",HTTP_CODE_OK);}
