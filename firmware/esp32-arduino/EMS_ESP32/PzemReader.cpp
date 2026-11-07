#include "PzemReader.h"
#include <math.h>
#include "FirmwareCore.h"
PzemReader::PzemReader(int rxPin,int txPin):serialPort(2),meter(serialPort,rxPin,txPin){}
void PzemReader::begin(){ /* PZEM004Tv30 configures UART2 and the supplied pins in its constructor. */ }
PzemReading PzemReader::read(){
  PzemReading v{};
  v.voltageVolts=meter.voltage(); v.currentAmps=meter.current();
  v.activePowerWatts=meter.power(); v.energyWattHours=meter.energy()*1000.0f;
  v.frequencyHertz=meter.frequency(); v.powerFactor=meter.pf();
  v.valid=ems::validMeasurement(v.voltageVolts,v.currentAmps,v.activePowerWatts,
    v.energyWattHours,v.frequencyHertz,v.powerFactor);
  return v;
}
