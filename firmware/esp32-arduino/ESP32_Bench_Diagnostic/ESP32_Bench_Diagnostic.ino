/*
  EMS ESP32-WROOM bench diagnostic
  Dependency-free: ESP32 Arduino core only.
  No Wi-Fi credentials, backend secrets, PZEM, relays, or mains wiring required.

  Arduino IDE baseline:
    Board: ESP32 Dev Module
    Upload speed: 115200 (reduce if upload is unstable)
    Serial Monitor: 115200 baud
*/
#include <Arduino.h>
#include <esp_system.h>

namespace {
unsigned long lastReportMs = 0;

const char* resetReasonName(esp_reset_reason_t reason) {
  switch (reason) {
    case ESP_RST_POWERON: return "power-on";
    case ESP_RST_EXT: return "external";
    case ESP_RST_SW: return "software";
    case ESP_RST_PANIC: return "panic";
    case ESP_RST_INT_WDT: return "interrupt-watchdog";
    case ESP_RST_TASK_WDT: return "task-watchdog";
    case ESP_RST_WDT: return "watchdog";
    case ESP_RST_DEEPSLEEP: return "deep-sleep";
    case ESP_RST_BROWNOUT: return "brownout";
    case ESP_RST_SDIO: return "SDIO";
    default: return "unknown";
  }
}

void printDiagnostics() {
  Serial.println();
  Serial.println(F("=== EMS ESP32 BENCH DIAGNOSTIC ==="));
  Serial.printf("Chip model: %s\n", ESP.getChipModel());
  Serial.printf("Chip revision: %u\n", ESP.getChipRevision());
  Serial.printf("CPU cores: %u\n", ESP.getChipCores());
  Serial.printf("CPU frequency: %u MHz\n", ESP.getCpuFreqMHz());
  Serial.printf("Flash size: %u bytes\n", ESP.getFlashChipSize());
  Serial.printf("Flash speed: %u Hz\n", ESP.getFlashChipSpeed());
  Serial.printf("Sketch size: %u bytes\n", ESP.getSketchSize());
  Serial.printf("Free sketch space: %u bytes\n", ESP.getFreeSketchSpace());
  Serial.printf("Heap total: %u bytes\n", ESP.getHeapSize());
  Serial.printf("Heap free: %u bytes\n", ESP.getFreeHeap());
  Serial.printf("Largest free heap block: %u bytes\n", ESP.getMaxAllocHeap());
  Serial.printf("PSRAM size: %u bytes\n", ESP.getPsramSize());
  Serial.printf("Reset reason: %s\n", resetReasonName(esp_reset_reason()));
  Serial.println(F("Diagnostic status: firmware is running; no sensor test performed."));
  Serial.println(F("No Wi-Fi connection, credentials, backend requests, or GPIO outputs used."));
  Serial.println(F("==================================="));
}
}  // namespace

void setup() {
  Serial.begin(115200);
  delay(800);
  printDiagnostics();
  lastReportMs = millis();
}

void loop() {
  if (millis() - lastReportMs >= 10000UL) {
    lastReportMs = millis();
    Serial.printf("[alive] uptime=%lu s, free_heap=%u bytes\n",
                  millis() / 1000UL, ESP.getFreeHeap());
  }
  delay(20);
}
