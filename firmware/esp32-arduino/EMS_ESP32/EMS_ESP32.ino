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
#include "FirmwareCore.h"

namespace {
Preferences preferences;
PzemReader meter(PZEM_RX_PIN,PZEM_TX_PIN);
TelemetryClient* telemetry=nullptr;
String deviceSecret, previousDeviceSecret;
bool rotationPending=false,rotationRejected=false;
unsigned long lastTelemetryMs=0,lastHeartbeatMs=0,lastRotationAttemptMs=0;
unsigned long nextProvisionAttemptMs=0,provisionBackoffMs=ems::kInitialProvisionBackoffMs;
ems::EnergyCounterState energyCounter;
unsigned long lastMeterPersistMs=0;

bool connectWiFi(){
  if(WiFi.status()==WL_CONNECTED)return true;
  WiFi.mode(WIFI_STA); WiFi.begin(WIFI_SSID,WIFI_PASSWORD);
  const unsigned long started=millis();
  while(WiFi.status()!=WL_CONNECTED&&millis()-started<WIFI_CONNECT_TIMEOUT_MS)delay(250);
  if(WiFi.status()!=WL_CONNECTED)Serial.println("[wifi] Offline; reconnect will be retried.");
  return WiFi.status()==WL_CONNECTED;
}
bool synchronizeClock(){
  configTime(0,0,NTP_SERVER_1,NTP_SERVER_2);
  const unsigned long started=millis();
  while(time(nullptr)<1700000000&&millis()-started<NTP_SYNC_TIMEOUT_MS)delay(250);
  return time(nullptr)>=1700000000;
}
void scheduleProvisionRetry(){
  nextProvisionAttemptMs=millis()+provisionBackoffMs;
  provisionBackoffMs=ems::nextProvisionBackoff(provisionBackoffMs);
  Serial.printf("[provision] Retry delayed; next attempt in %lu seconds.\n",nextProvisionAttemptMs>millis()?(nextProvisionAttemptMs-millis())/1000:0);
}
bool provisionDevice(){
  if(!ems::isHttpsBaseUrl(EMS_API_BASE_URL)||strlen(EMS_PROVISIONING_CODE)==0||
     strncmp(EMS_ROOT_CA_CERT + sizeof("-----BEGIN CERTIFICATE-----\n") - 1,
       "PASTE_TRUSTED_ROOT_CA_CERTIFICATE_HERE", sizeof("PASTE_TRUSTED_ROOT_CA_CERTIFICATE_HERE") - 1) == 0){
    Serial.println("[provision] HTTPS API URL, provisioning code, or trusted CA missing/invalid.");return false;
  }
  WiFiClientSecure client; client.setCACert(EMS_ROOT_CA_CERT); HTTPClient http;
  if(!http.begin(client,String(EMS_API_BASE_URL)+"/api/device-auth/provision")){Serial.println("[provision] HTTPS initialization failed.");return false;}
  http.setTimeout(10000);http.addHeader("Content-Type","application/json");
  JsonDocument request;request["deviceCode"]=EMS_DEVICE_CODE;request["provisioningCode"]=EMS_PROVISIONING_CODE;
  String body;serializeJson(request,body);int status=http.POST(body);String response=http.getString();http.end();
  if(status!=HTTP_CODE_OK){Serial.printf("[provision] HTTP %d; check code validity and device registration.\n",status);return false;}
  JsonDocument result;
  if(deserializeJson(result,response)||!result["deviceSecret"].is<const char*>()){
    Serial.println("[provision] Malformed response; credential was not stored.");return false;
  }
  String candidate=result["deviceSecret"].as<String>();
  if(candidate.length()<40||candidate.length()>64){Serial.println("[provision] Secret length invalid; refusing storage.");return false;}
  if(!preferences.begin("ems-device",false)){Serial.println("[provision] NVS namespace unavailable.");return false;}
  size_t stored=preferences.putString("secret",candidate);preferences.end();
  if(stored==0){Serial.println("[provision] NVS write failed. Admin recovery may be required if server consumed the code.");return false;}
  deviceSecret=candidate;Serial.println("[provision] Credential stored.");return true;
}
void loadCredential(){
  if(preferences.begin("ems-device",true)){
    deviceSecret=preferences.getString("secret","");
    previousDeviceSecret=preferences.getString("previous","");
    rotationPending=preferences.getBool("rotationPending",false);
    preferences.end();
  }
}
void persistMeterState(bool force){
  if(!force&&millis()-lastMeterPersistMs<300000UL)return;
  if(!preferences.begin("ems-meter",false))return;
  preferences.putDouble("offsetWh",energyCounter.offsetWh);
  if(energyCounter.initialized)preferences.putDouble("lastRawWh",energyCounter.lastRawWh);
  preferences.end();lastMeterPersistMs=millis();
}
void normalizeCumulativeEnergy(PzemReading& reading){
  if(!energyCounter.initialized){
    if(preferences.begin("ems-meter",true)){
      energyCounter.offsetWh=preferences.getDouble("offsetWh",0);
      energyCounter.lastRawWh=preferences.getDouble("lastRawWh",-1);
      preferences.end();
      energyCounter.initialized=energyCounter.lastRawWh>=0;
    }
  }
  double totalWh=0;
  bool reset=ems::normalizeEnergy(reading.energyWattHours,energyCounter,totalWh);
  reading.energyWattHours=static_cast<float>(totalWh);
  // Batch NVS writes to limit flash wear; a detected reset is persisted immediately.
  // The last checkpoint can be up to five minutes old when a meter reset occurs.
  persistMeterState(reset);
}
void tryCredentialRotation(){
  lastRotationAttemptMs=millis();
  String replacement=rotationPending?(deviceSecret==previousDeviceSecret&&strlen(EMS_ROTATED_DEVICE_SECRET)>0?String(EMS_ROTATED_DEVICE_SECRET):deviceSecret):String(EMS_ROTATED_DEVICE_SECRET);
  if(replacement.length()==0||(!rotationPending&&replacement==deviceSecret))return;
  if(!rotationPending){
    if(!preferences.begin("ems-device",false))return;
    size_t previousSaved=preferences.putString("previous",deviceSecret);
    size_t pendingSaved=previousSaved>0?preferences.putBool("rotationPending",true):0;
    size_t replacementSaved=pendingSaved>0?preferences.putString("secret",replacement):0;
    if(replacementSaved==0){preferences.putBool("rotationPending",false);preferences.remove("previous");}
    preferences.end();
    if(previousSaved==0||pendingSaved==0||replacementSaved==0){
      Serial.println("[rotation] Could not atomically prepare replacement in NVS; old credential remains active.");return;
    }
    previousDeviceSecret=deviceSecret;deviceSecret=replacement;rotationPending=true;
  }else if(replacement!=deviceSecret){
    if(!preferences.begin("ems-device",false))return;
    size_t saved=preferences.putString("secret",replacement);preferences.end();
    if(saved==0){Serial.println("[rotation] Could not persist replacement after interrupted preparation.");return;}
    deviceSecret=replacement;
  }
  TelemetryClient candidate(EMS_API_BASE_URL,EMS_DEVICE_CODE,replacement,EMS_ROOT_CA_CERT);
  if(!candidate.heartbeat()){
    if(candidate.lastStatus()==HTTP_CODE_UNAUTHORIZED){
      bool restored=false;
      if(preferences.begin("ems-device",false)){
        size_t bytes=preferences.putString("secret",previousDeviceSecret);
        if(bytes>0){
          preferences.putBool("rotationPending",false);
          preferences.remove("previous");restored=true;
        }
        preferences.end();
      }
      if(restored){
        deviceSecret=previousDeviceSecret;rotationPending=false;rotationRejected=true;
        Serial.println("[rotation] Backend rejected replacement; prior key restored. Update or clear the configured replacement secret.");
      }else{
        Serial.println("[rotation] Backend rejected replacement but NVS rollback failed; retaining pending state for recovery.");
      }
    }else Serial.println("[rotation] Result uncertain (network/TLS/server); keeping both keys and retrying safely.");
    return;
  }
  if(preferences.begin("ems-device",false)){
    preferences.putBool("rotationPending",false);preferences.remove("previous");preferences.end();
  }
  deviceSecret=replacement;previousDeviceSecret="";rotationPending=false;
  delete telemetry;telemetry=nullptr;
  Serial.println("[rotation] Replacement credential confirmed and stored.");
}
void createTelemetryClient(){
  if(telemetry==nullptr&&deviceSecret.length()>0)
    telemetry=new TelemetryClient(EMS_API_BASE_URL,EMS_DEVICE_CODE,deviceSecret,EMS_ROOT_CA_CERT);
}
}

void setup(){
  Serial.begin(115200);delay(500);
  Serial.println("\nEMS ESP32 firmware booting (monitoring-only).");
  meter.begin();loadCredential();
  if(!connectWiFi()){Serial.println("[boot] Wi-Fi unavailable; background retry enabled.");return;}
  if(!synchronizeClock()){Serial.println("[ntp] Clock sync failed; authenticated traffic disabled until sync.");return;}
  if(deviceSecret.length()==0){
    if(provisionDevice())provisionBackoffMs=ems::kInitialProvisionBackoffMs;
    else scheduleProvisionRetry();
  }else tryCredentialRotation();
  createTelemetryClient();
  Serial.println("[boot] Initialization complete.");
}
void loop(){
  if(!connectWiFi()){delay(1000);return;}
  if(time(nullptr)<1700000000&&!synchronizeClock()){delay(1000);return;}
  if((rotationPending||(!rotationRejected&&strlen(EMS_ROTATED_DEVICE_SECRET)>0&&String(EMS_ROTATED_DEVICE_SECRET)!=deviceSecret))&&millis()-lastRotationAttemptMs>=HEARTBEAT_INTERVAL_MS)tryCredentialRotation();
  if(deviceSecret.length()==0){
    loadCredential();
    if(deviceSecret.length()==0){
      if((long)(millis()-nextProvisionAttemptMs)>=0){
        if(provisionDevice()){provisionBackoffMs=30000UL;nextProvisionAttemptMs=0;}
        else scheduleProvisionRetry();
      }
      delay(100);return;
    }
  }
  createTelemetryClient();if(telemetry==nullptr){delay(1000);return;}
  unsigned long now=millis();
  if(now-lastTelemetryMs>=TELEMETRY_INTERVAL_MS){
    lastTelemetryMs=now;telemetry->flushPending();PzemReading reading=meter.read();
    if(reading.valid){normalizeCumulativeEnergy(reading);if(!telemetry->publish(reading))Serial.println("[telemetry] Sample queued or rejected; see preceding diagnostic.");}
    else Serial.println("[pzem] Invalid/out-of-range reading; no zero-value substitution.");
  }
  if(now-lastHeartbeatMs>=HEARTBEAT_INTERVAL_MS){
    lastHeartbeatMs=now;if(!telemetry->heartbeat())Serial.println("[heartbeat] Failed; retry on next interval.");
  }
  delay(20);
}
