# EMS ESP32 firmware (Arduino IDE / C++)

Monitoring-only firmware for an ESP32-WROOM-class board and PZEM-004T v3.x. It integrates with the EMS provisioning, HMAC authentication, heartbeat, and version-1 telemetry APIs.

## Included

- Wi-Fi station connection and NTP time synchronization; signed requests fail closed when time is invalid.
- HTTPS-only provisioning with CA validation and exponential retry backoff (30 seconds to 30 minutes).
- Device credential persisted in ESP32 NVS. Optional credential rotation: paste the one-time replacement secret from the EMS device details page into `EMS_ROTATED_DEVICE_SECRET`; firmware persists it before the first signed heartbeat so the backend cannot revoke the old key before the new key is durable.
- HMAC-SHA256 heartbeat and telemetry with a 128-bit random nonce and UUIDv4 sample IDs.
- PZEM voltage, current, active power, cumulative energy, frequency, and power-factor readings.
- Numeric validation against the backend's telemetry bounds before transmission.
- Bounded persistent offline queue: up to 8 complete telemetry payloads, retaining their original sample IDs and capture timestamps. Retries use fresh HMAC timestamps/nonces. HTTP 409 is treated as an already-accepted duplicate. When full, the queue retains existing samples and rejects the newest sample.
- HTTP/TLS/authentication diagnostics without logging secrets or signatures.
- Cumulative meter-counter reset detection and an offset to keep the reported total monotonic across detected resets.

## Arduino IDE setup

1. Install Espressif Systems' ESP32 board package through Boards Manager.
2. Select the matching ESP32-WROOM / ESP32 Dev Module board and serial port.
3. Install ArduinoJson 7.4.3 and PZEM004Tv30 1.2.1 (Jakub Mandula) through Library Manager.
4. Copy `EMS_ESP32/Config.example.h` to `EMS_ESP32/Config.h`.
5. Fill in Wi-Fi credentials, API base URL, registered device code, one-time provisioning code, and trusted HTTPS root CA PEM.
6. Open `EMS_ESP32/EMS_ESP32.ino`, compile, and upload.

Never commit `Config.h`. The firmware blocks HTTPS requests if a trusted CA is absent; do not use `setInsecure()`.

## Provisioning and recovery

1. Register a device in the EMS web application under the intended household.
2. Generate its single-use provisioning code (valid for 10 minutes).
3. Add device code and provisioning code to local `Config.h`.
4. Flash and open Serial Monitor at 115200 baud.
5. Allow Wi-Fi and NTP synchronization. The sketch exchanges the code at `POST /api/device-auth/provision`.
6. The returned device secret is stored in NVS. Remove the one-time code from local configuration after successful provisioning.

**Power-loss recovery:** the backend now makes the provisioning exchange idempotent for the original code's remaining validity window. If the server created the credential but the ESP32 lost power or the HTTP response before saving it, the device can retry the same code and receive the same active credential. The recovery code expires at its original 10-minute deadline. If that deadline has passed, an administrator must revoke the orphaned credential and issue a fresh provisioning code. Issuing a new code revokes older recovery tokens, including already-used ones.

## Credential rotation

1. Open the device details page and initiate credential rotation.
2. Copy the replacement secret once.
3. Set `EMS_ROTATED_DEVICE_SECRET` in local `Config.h`.
4. Flash/restart. Firmware stores the replacement first, then sends an authenticated heartbeat. The backend adopts the replacement on that request and revokes the old key.
5. After successful rotation, clear the replacement secret from `Config.h`; the active key remains in NVS.

If the replacement is expired or rejected, firmware restores the previously stored key. Do not revoke the old credential manually before the replacement has been confirmed.

## UART wiring and meter compatibility

For common PZEM-004T v3.x TTL boards, default ESP32 UART pins are GPIO16 (RX2) and GPIO17 (TX2), configurable in Config.h. Cross-connect meter TX to ESP32 RX and meter RX to ESP32 TX. Use suitable level compatibility/isolation for the exact board. Confirm the PZEM revision and interface before wiring; older revisions differ and are not supported by the V3 library.

## Electrical safety

PZEM measurement wiring can expose lethal mains voltage. Mains wiring, CT placement, fusing, enclosure, clearances, and contactor/relay selection require a qualified electrician. Never connect mains to ESP32 GPIO. This firmware is not a certified protective relay or safety controller. Relay outputs, remote switching, protective trip logic, and command polling are intentionally absent.

## Queue and energy-counter limitations

- The queue holds 8 samples (approximately four minutes at the default 30-second interval). It is bounded to protect device storage. Longer outages can overflow it; the newest sample is rejected rather than deleting older pending samples.
- The backend accepts capture timestamps no older than seven days. Extended offline periods therefore require operator review; queued samples can become too old for ingestion.
- A meter reset is detected when its reported cumulative energy falls below the last saved raw counter. The prior total is retained as an offset, but energy consumed during the interval in which the meter was reset cannot be reconstructed.
- The queue and energy metadata use Preferences/NVS. Flash encryption, secure boot provisioning, and physical flash-extraction protection are deliberately not changed in this work.
- A successful CI compile and source-level regression suite do not replace physical hardware-in-loop testing or prove PZEM measurement accuracy.

## Validation and production gate

The GitHub Actions workflow pins ESP32 core 3.3.12, ArduinoJson 7.4.3, and PZEM004Tv30 1.2.1. It runs Python backend/firmware contract checks, native C++17 edge-case tests, the backend .NET suite, and an Arduino CLI compile.

Run host tests locally from the repository root:

```sh
python -m unittest discover -s firmware/esp32-arduino/tests -v
g++ -std=c++17 -Wall -Wextra -Werror -pedantic firmware/esp32-arduino/tests/native/test_firmware_core.cpp -o /tmp/ems-firmware-tests
/tmp/ems-firmware-tests
```

See [PRODUCTION_ACCEPTANCE.md](PRODUCTION_ACCEPTANCE.md) for the complete hardware-in-the-loop test matrix and release gates. A successful CI compile and source-level regression suite do not replace physical hardware-in-loop testing or prove PZEM measurement accuracy. The firmware must remain supervised monitoring-only until the hardware acceptance matrix is completed.

## Flashing

Use a USB data cable and the board USB-to-UART interface. Select the serial port and click Upload. If auto-reset fails, hold BOOT while upload begins and release when the IDE starts connecting. Serial Monitor: 115200 baud.
