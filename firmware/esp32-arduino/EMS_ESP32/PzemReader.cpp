#include "PzemReader.h"
#include <math.h>
PzemReader::PzemReader(int rxPin,int txPin):serialPort(2),meter(serialPort,rxPin,txPin){}
void PzemReader::begin(){ /* PZEM004Tv30 configures UART2 and the supplied pins in its constructor. */ }
PzemReading PzemReader::read(){
  PzemReading v{};
  v.voltageVolts=meter.voltage(); v.currentAmps=meter.current();
  v.activePowerWatts=meter.power(); v.energyWattHours=meter.energy()*1000.0f;
  v.frequencyHertz=meter.frequency(); v.powerFactor=meter.pf();
  v.valid=isfinite(v.voltageVolts)&&isfinite(v.currentAmps)&&isfinite(v.activePowerWatts)&&
    isfinite(v.energyWattHours)&&v.voltageVolts>=0&&v.voltageVolts<=1000&&
    v.currentAmps>=0&&v.currentAmps<=10000&&v.activePowerWatts>=-10000000&&
    v.activePowerWatts<=10000000&&v.energyWattHours>=0&&v.energyWattHours<=1.0e12f&&
    (!isfinite(v.frequencyHertz)||(v.frequencyHertz>=0&&v.frequencyHertz<=1000))&&
    (!isfinite(v.powerFactor)||(v.powerFactor>=-1&&v.powerFactor<=1));
  return v;
}