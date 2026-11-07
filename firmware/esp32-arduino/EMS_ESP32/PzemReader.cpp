#include "PzemReader.h"
#include <math.h>
PzemReader::PzemReader(int rxPin, int txPin)
    : serialPort(2), meter(serialPort, rxPin, txPin) {}
void PzemReader::begin() {
  // PZEM004Tv30's HardwareSerial constructor configures UART2 at 9600 baud
  // using the supplied RX/TX pins. Do not reinitialize it with different pins.
}
PzemReading PzemReader::read() {
  PzemReading value{};
  value.voltageVolts = meter.voltage();
  value.currentAmps = meter.current();
  value.activePowerWatts = meter.power();
  value.energyWattHours = meter.energy() * 1000.0f; // Library returns kWh.
  value.frequencyHertz = meter.frequency();
  value.powerFactor = meter.pf();
  value.valid = isfinite(value.voltageVolts) && isfinite(value.currentAmps) &&
    isfinite(value.activePowerWatts) && isfinite(value.energyWattHours) &&
    value.voltageVolts >= 0.0f && value.currentAmps >= 0.0f && value.energyWattHours >= 0.0f;
  return value;
}
