# ESP32 Arduino firmware

## Scope

Monitoring-only Arduino C++ firmware for an ESP32-WROOM-32-class board and PZEM-004T v3.x. It integrates with EMS device provisioning, HMAC authentication, heartbeat and telemetry schema v1.

**Not implemented:** relay/contactor actuation, remote switching, protective trips, cloud commands or local electrical protection state machines. The firmware is not a certified protective device.

## Features

- Wi-Fi station connection with retry.
- Fresh NTP synchronization; signed requests fail closed when time is invalid.
- HTTPS-only provisioning with trusted root CA validation and exponential retry backoff from 30 seconds to 30 minutes.
- Device credential persisted in ESP32 Preferences/NVS.
- Optional credential rotation; replacement is persisted before the first signed heartbeat so the backend cannot revoke the old key before the new key is durable.
- HMAC-SHA-256 heartbeat and telemetry with 128-bit request nonce and UUIDv4 sample ID.
- Numeric validation against backend telemetry bounds.
- Cumulative energy normalization across meter counter reset/wrap conditions.
- Persistent offline queue capped at eight complete payloads. Original sample IDs and capture timestamps are retained; retries use fresh HMAC timestamps/nonces.
- HTTP 409 duplicate sample is treated as already accepted.
- Queue overflow retains older pending samples and rejects the newest sample.
- Diagnostics avoid logging device secrets and signatures.

## Hardware and pin assumptions

- Target: original ESP32 / ESP32-WROOM-32 family; Arduino FQBN used in CI: esp32:esp32:esp32.
- PZEM UART pins default to GPIO16 (ESP32 RX) and GPIO17 (ESP32 TX).
- Confirm the actual board/module and pinout before wiring. Do not assume these pins map identically on ESP32-S2/S3/C3/C6 variants.
- PZEM-004T v3.x is required for real meter readings. Without the PZEM, use the separate USB bench diagnostic.

## Dependencies

CI currently pins Arduino CLI 1.2.2, Espressif ESP32 Arduino core 3.3.12, ArduinoJson 7.4.3 and PZEM004Tv30 1.2.1.

## Configure

1. Copy Config.example.h to Config.h in the EMS_ESP32 sketch directory.
2. Set Wi-Fi SSID/password, HTTPS EMS API base URL, registered device code, one-time provisioning code and trusted root CA certificate.
3. Keep Config.h local; it is ignored by Git. Never commit credentials.
4. Confirm the CA certificate validates the service certificate chain.
5. Set pin numbers and intervals only after confirming the board and meter interface.

## Arduino IDE

1. Install Espressif ESP32 board support.
2. Select ESP32 Dev Module provisionally for a typical ESP32-WROOM-32 board.
3. Install ArduinoJson 7.4.3 and PZEM004Tv30 1.2.1.
4. Open EMS_ESP32.ino and compile/upload.
5. Open Serial Monitor at 115200 baud.

For a first USB-only test, open ESP32_Bench_Diagnostic/ESP32_Bench_Diagnostic.ino. See its README. That diagnostic requires only the ESP32 Arduino core and does not connect to Wi-Fi or drive GPIO outputs.

## Provisioning and recovery

1. Register the device in the EMS household.
2. Request a provisioning code from the device details page (administrator access and recent authentication may be required).
3. Add device code and one-time provisioning code to local Config.h.
4. Connect the ESP32 to Wi-Fi and allow fresh NTP synchronization.
5. The sketch exchanges the code at POST /api/device-auth/provision over validated HTTPS.
6. The returned secret is stored in NVS. Remove the one-time code from local configuration after successful provisioning.
7. Confirm a signed heartbeat updates device last-seen.

The backend supports bounded recovery for a provisioning exchange whose response was lost before the ESP32 saved the credential. Recovery is limited to the original code validity window. If that expires, an administrator must revoke any orphaned credential and issue a new code.

## Queue limits and energy behavior

- Eight queued samples maximum (about four minutes at the default 30-second sample interval).
- Queue overflow rejects the newest sample and retains existing samples.
- Backend rejects samples older than seven days; extended offline periods require operator review.
- NVS queue and energy metadata are persistent, but flash encryption, secure boot provisioning and physical flash-extraction protection have not been changed and are explicitly out of scope.
- Power-loss and long-duration flash-wear behavior still require physical hardware validation.

## Testing

CI runs Python contract tests, native C++17 edge-case tests and Arduino CLI compilation for both main firmware and the bench diagnostic. CI compilation does not prove sensor accuracy, board pin compatibility or electrical safety.

## Safety

Use USB/approved low-voltage power for initial board checks. Do not connect mains, PZEM mains conductors, CT circuits, relays or loads based on this README. All mains-related installation and testing must be performed by a qualified electrician using properly rated isolation and protective equipment. Cloud telemetry and warning thresholds are not protection.
