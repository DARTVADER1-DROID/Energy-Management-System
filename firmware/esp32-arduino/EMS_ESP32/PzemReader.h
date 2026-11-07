#pragma once
#include <Arduino.h>
#include <PZEM004Tv30.h>
struct PzemReading {
  float voltageVolts, currentAmps, activePowerWatts, energyWattHours;
  float frequencyHertz, powerFactor;
  bool valid;
};
class PzemReader {
 public:
  PzemReader(int rxPin, int txPin);
  void begin();
  PzemReading read();
 private:
  HardwareSerial serialPort;
  PZEM004Tv30 meter;
};
