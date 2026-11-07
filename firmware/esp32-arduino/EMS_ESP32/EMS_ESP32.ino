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
PzemReader meter(PZEM_RX_PIN,PZEM_TX_PIN);
TelemetryClient* telemetry=nullptr;
String deviceSecret, previousDeviceSecret;
bool rotationPending=false;
unsigned long lastTelemetryMs=0,lastHeartbeatMs=0;
unsigned long nextProvisionAttemptMs=0,provisionBackoffMs=30000UL;
float energyOffsetWh=0,lastRawEnergyWh=-1;
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
  provisionBackoffMs=min(provisionBackoffMs*2UL,1800000UL);
  Serial.printf("[provision] Retry delayed; next attempt in %lu seconds.\n",nextProvisionAttemptMs>millis()?(nextProvisionAttemptMs-millis())/1000:0);
}
bool provisionDevice(){
  if(strlen(EMS_PROVISIONING_CODE)==0||strlen(EMS_ROOT_CA_CERT)==0||
     strstr(EMS_ROOT_CA_CERT,"PASTE_TRUSTED_ROOT_CA")!=nullptr){
    Serial.println("[provision] Missing provisioning code or trusted CA.");return false;
  }
  WiFiClientSecure client; client.setCACert(EMS_ROOT_CA_CERT); HTTPClient http;
  if(!http.begin(client,String(EMS_API_BASE_URL)+"/api/device-auth/provision")){Serial.println("[provision] HTTPS initialization failed.");return false;}
  http.setTimeout(10000);http.addHeader("Content-Type","application/json");
  StaticJsonDocument<256> request;request["deviceCode"]=EMS_DEVICE_CODE;request["provisioningCode"]=EMS_PROVISIONING_CODE;
  String body;serializeJson(request,body);int status=http.POST(body);String response=http.getString();http.end();
  if(status!=HTTP_CODE_OK){Serial.printf("[provision] HTTP %d; check code validity and device registration.\n",status);return false;}
  StaticJsonDocument<512> result;
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
  preferences.putFloat("offsetWh",energyOffsetWh);
  if(lastRawEnergyWh>=0)preferences.putFloat("lastRawWh",lastRawEnergyWh);
  preferences.end();lastMeterPersistMs=millis();
}
void normalizeCumulativeEnergy(PzemReading& reading){
  const float raw=reading.energyWattHours;
  bool reset=false;
  if(lastRawEnergyWh<0){
    if(preferences.begin("ems-meter",true)){
      energyOffsetWh=preferences.getFloat("offsetWh",0);
      lastRawEnergyWh=preferences.getFloat("lastRawWh",-1);
      preferences.end();
    }
  }
  if(lastRawEnergyWh>=0&&raw+1.0f<lastRawEnergyWh){
    energyOffsetWh+=lastRawEnergyWh;reset=true;
    Serial.println("[pzem] Meter energy counter reset detected; preserving prior cumulative total. Energy during reset gap cannot be reconstructed.");
  }
  lastRawEnergyWh=raw;reading.energyWattHours=raw+energyOffsetWh;
  if(reset)persistMeterState(true);else persistMeterState(false);
}
void tryCredentialRotation(){
  String replacement=rotationPending?deviceSecret:String(EMS_ROTATED_DEVICE_SECRET);
  if(replacement.length()==0||(!rotationPending&&replacement==deviceSecret))return;
  if(!rotationPending){
    if(!preferences.begin("ems-device",false))return;
    preferences.putString("previous",deviceSecret);
    preferences.putBool("rotationPending",true);
    size_t saved=preferences.putString("secret",replacement);
    preferences.end();
    if(saved==0){Serial.println("[rotation] Could not persist replacement; old credential remains active.");return;}
    previousDeviceSecret=deviceSecret;deviceSecret=replacement;rotationPending=true;
  }
  TelemetryClient candidate(EMS_API_BASE_URL,EMS_DEVICE_CODE,replacement,EMS_ROOT_CA_CERT);
  if(!candidate.heartbeat()){
    if(candidate.lastStatus()==HTTP_CODE_UNAUTHORIZED){
      if(preferences.begin("ems-device",false)){
        preferences.putString("secret",previousDeviceSecret);
        preferences.putBool("rotationPending",false);
        preferences.remove("previous");preferences.end();
      }
      deviceSecret=previousDeviceSecret;rotationPending=false;
      Serial.println("[rotation] Backend rejected replacement; restored prior key.");
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
    if(provisionDevice())provisionBackoffMs=30000UL;
    else scheduleProvisionRetry();
  }else tryCredentialRotation();
  createTelemetryClient();
  Serial.println("[boot] Initialization complete.");
}
void loop(){
  if(!connectWiFi()){delay(1000);return;}
  if(time(nullptr)<1700000000&&!synchronizeClock()){delay(1000);return;}
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
    lastTelemetryMs=now;PzemReading reading=meter.read();
    if(reading.valid){normalizeCumulativeEnergy(reading);if(!telemetry->publish(reading))Serial.println("[telemetry] Sample queued or rejected; see preceding diagnostic.");}
    else Serial.println("[pzem] Invalid/out-of-range reading; no zero-value substitution.");
  }
  if(now-lastHeartbeatMs>=HEARTBEAT_INTERVAL_MS){
    lastHeartbeatMs=now;if(!telemetry->heartbeat())Serial.println("[heartbeat] Failed; retry on next interval.");
  }
  delay(20);
}
