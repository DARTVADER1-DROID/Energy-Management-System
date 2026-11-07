# EMS ESP32 firmware (Arduino IDE / C++)

Monitoring-only first milestone for an ESP32-WROOM-class board and PZEM-004T. It integrates with the existing EMS provisioning, HMAC authentication, heartbeat, and version-1 telemetry APIs.

## Included
- Wi-Fi station connection and NTP time synchronization.
- One-time HTTPS provisioning and device-secret persistence in ESP32 NVS (Preferences).
- HMAC-SHA256 authenticated heartbeat and telemetry.
- PZEM voltage, current, active power, cumulative energy, frequency, and power-factor readings.
- Version 1 telemetry sent to `POST /api/device-telemetry`.

Relay outputs, remote commands, protective trip logic, persistent offline queue, and captive-portal provisioning are deliberately excluded from this first milestone.

## Arduino IDE setup
1. Install Espressif Systems' ESP32 board package through Boards Manager.
2. Select the matching ESP32-WROOM / ESP32 Dev Module board and serial port.
3. Install ArduinoJson (6.x or 7.x) and the PZEM004Tv30 library (Jakub Mandula) through Library Manager.
4. Copy `EMS_ESP32/Config.example.h` to `EMS_ESP32/Config.h`.
5. Fill in Wi-Fi credentials, API base URL, registered device code, one-time provisioning code, and trusted HTTPS root CA PEM.
6. Open `EMS_ESP32/EMS_ESP32.ino`, compile, and upload.

Never commit `Config.h`. The firmware blocks HTTPS requests if a trusted CA is absent; do not use `setInsecure()`.

## Provisioning
1. Register a device in the EMS web application under the intended household.
2. Generate its single-use provisioning code (valid for 10 minutes).
3. Add device code and provisioning code to local `Config.h`.
4. Flash and open Serial Monitor at 115200 baud.
5. Allow Wi-Fi and NTP synchronization. The sketch exchanges the code at `POST /api/device-auth/provision`.
6. The returned device secret is stored in NVS. Remove the one-time code from local configuration after successful provisioning and keep the configuration private.

Do not create another initial provisioning code for an already provisioned device. Credential rotation is not implemented in this first firmware increment.

## Initial UART wiring
For common PZEM-004T v3.x TTL boards, default ESP32 UART pins are GPIO16 (RX2) and GPIO17 (TX2), configurable in Config.h. Cross-connect meter TX to ESP32 RX and meter RX to ESP32 TX. Use suitable level compatibility/isolation for the exact board. Confirm the PZEM revision and interface before wiring; older revisions differ.

PZEM measurement wiring can expose lethal mains voltage. Mains wiring, CT placement, fusing, enclosure, clearances, and contactor/relay selection require a qualified electrician. Never connect mains to ESP32 GPIO. This firmware is not a certified protective relay or safety controller.

## Current limitations / validation
- Missed samples are not yet persisted for later replay; this version retries on the next scheduled sample.
- HMAC canonicalization matches the backend: uppercase method, exact path/query, Unix timestamp, nonce, and uppercase SHA-256 body digest separated by newline characters. Heartbeat signs the empty body.
- NTP failure blocks signed requests.
- HTTPS certificate validation is mandatory; configure the correct CA chain for the actual EMS hostname.
- Confirm meter revision, CT rating, pinout, and calibration. Physical-device validation is required; a successful compile does not certify measurement accuracy or electrical safety.

## Flashing
Use a USB data cable and the board USB-to-UART interface. Select the serial port and click Upload. If auto-reset fails, hold BOOT while upload begins and release when the IDE starts connecting. Serial Monitor: 115200 baud.
