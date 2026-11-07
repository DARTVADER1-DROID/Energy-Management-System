# ESP32 + PZEM-004T hardware commissioning procedure

## Scope and safety

This procedure covers the monitoring-only EMS firmware on an ESP32-WROOM-class board and PZEM-004T V3.x. It does not commission relays, remote switching, or protective trip functions.

**Mains safety:** All mains-side design, wiring, CT installation, fusing, enclosure, insulation, clearances, earthing, and energized tests must be performed/approved by a qualified electrician under applicable local rules. Software checks should use only the isolated low-voltage ESP32/USB side. Never connect mains conductors to ESP32 GPIO, USB, breadboards, or exposed low-voltage test points. Do not improvise wiring from this document; follow the exact meter documentation and electrician-approved drawing.

Passing this procedure is not production or electrical-safety certification.

## 1. Record configuration

Record date/operator, exact firmware commit SHA, ESP32 module/board revision, USB-UART interface, exact PZEM model/revision/interface, isolation and level-translation hardware, low-voltage supply, backend environment, and trusted CA identity/expiry.

Never record Wi-Fi passwords, provisioning codes, device secrets, HMAC values, or private keys. Stop if the board, meter revision, interface, logic levels, isolation, or supply cannot be positively identified.

## 2. Unpowered checks

With all equipment de-energized and disconnected from mains:

1. Inspect the ESP32 for damage, shorts, loose headers, and incorrect orientation.
2. Confirm PZEM-004T V3.x TTL. Older revisions may use different interfaces and are not covered by this baseline.
3. Verify UART logic levels against the exact interface documentation. Do not connect a 5 V UART signal directly to an ESP32 GPIO unless safe compatibility is explicitly established; use suitable level translation/isolation.
4. Confirm UART cross-connection: PZEM TX to ESP32 RX (default GPIO16), PZEM RX to ESP32 TX (default GPIO17), with reference/isolation exactly as specified for the interface.
5. Have a qualified electrician inspect the mains-side design, protective devices, CT placement, enclosure, insulation, clearances, earthing, strain relief, and separation from SELV/USB circuits before energized tests.

## 3. Arduino IDE build and flash

1. Install Espressif ESP32 board package and select the board matching the module. CI baseline is ESP32 Arduino core 3.3.12 and generic ESP32 target; other modules need separate compatibility checks.
2. Install ArduinoJson 7.4.3 and PZEM004Tv30 1.2.1.
3. Copy EMS_ESP32/Config.example.h to EMS_ESP32/Config.h.
4. Set local Wi-Fi credentials, HTTPS API URL, registered device code, one-time provisioning code if needed, and correct trusted root CA PEM. Leave optional rotation secret empty unless performing a planned rotation.
5. Never commit Config.h or include credentials in screenshots, logs, or test reports.
6. Compile with verbose output. Resolve errors and review warnings; do not suppress warnings to force a build.
7. Connect with a known-good USB data cable, select the correct board/port, and upload.
8. Open Serial Monitor at 115200 baud. If auto-reset fails, use the board's documented BOOT/reset procedure.

Expected initial log is the firmware boot banner. Secrets and signatures must never be printed.

## 4. Low-voltage startup (no mains connected)

1. Power ESP32 only from its approved low-voltage USB/supply input.
2. Confirm stable boot without repeated resets, watchdog events, brownout warnings, or exceptions.
3. Confirm Wi-Fi connection and fresh NTP synchronization.
4. If NTP is unavailable, authenticated HTTPS requests must remain blocked. Never bypass this.
5. Confirm API URL uses HTTPS and trusted CA matches the server hostname.
6. Confirm missing/wrong CA fails closed. Never use setInsecure() or disable certificate validation.
7. Confirm firmware has no relay outputs or remote command polling.

## 5. Provisioning and backend checks

Use a disposable test device/environment. Register the device in EMS, generate a single-use provisioning code, enter it locally, flash, and observe serial output.

Pass: HTTPS provisioning succeeds only with valid time and CA; returned credential is stored in NVS; device appears under the intended household; signed heartbeat is accepted; no secret is logged. Remove the one-time provisioning code from local configuration after success.

### Provisioning response-loss test

1. Interrupt network after server commits provisioning but before ESP32 receives/persists response.
2. Restart and retry same code within original 10-minute validity.
3. Confirm same active credential is recovered, with no duplicate active credential.
4. Retry after original deadline and confirm rejection; issue a fresh code through administrator workflow.

## 6. PZEM UART and measurement validation

Any energized measurement test requires prior approval and operation by a qualified electrician using the approved installation.

1. Verify UART pins/direction with the approved interface.
2. Confirm communication with exact PZEM V3.x revision without repeated read errors.
3. Compare voltage, current, active power, frequency, power factor, and cumulative energy against a calibrated reference at representative approved test points.
4. Record reference/device values, differences, load conditions, instrument identity/calibration status, and repeats.
5. Define project tolerances before testing. Fail readings outside manufacturer specifications or predeclared project limits.
6. Confirm invalid, NaN, infinite, or out-of-range mandatory readings are discarded, not transmitted as zeros.
7. Confirm unavailable optional frequency/power-factor values are omitted, not fabricated.

A firmware compile is not calibration.

## 7. Telemetry, security, and recovery

Use a test device and backend:

- Confirm accepted telemetry appears once with correct device, schema version, sample ID, UTC capture time, and measurements.
- Simulate lost response and resend same sample ID; verify backend idempotency and HTTP 409 handling.
- Verify HTTP 400 samples are discarded rather than retried forever.
- Verify 401/403, transport/TLS failures, 429, and 5xx do not silently delete pending samples.
- Verify wrong CA, plaintext HTTP, and invalid clock fail closed.
- Verify replay of a captured signed request is rejected. Never use production credentials for replay tests.
- Verify offline buffering preserves order and original IDs/timestamps for up to eight samples.
- Verify overflow preserves existing samples and rejects newest with a diagnostic.
- Eight samples is about four minutes at the default 30-second interval; longer outages can lose readings.
- Verify provisioning retry backoff and original 10-minute code expiry.
- Test credential rotation with network/power interruption at each stage. Confirm old credential remains usable or replacement is durably adopted. Never manually revoke old credential before confirming the new one.

Do not inject faults into live production devices or credentials.

## 8. Meter reset and NVS recovery

On a controlled test device:

1. Record raw meter energy and reported cumulative energy.
2. Simulate meter reset only using manufacturer's approved test procedure.
3. Confirm reported cumulative total does not decrease.
4. Confirm reset detection persists checkpoint promptly.
5. Power-cycle ESP32 and verify offset/checkpoint restoration.
6. Record that energy between the last five-minute checkpoint and a meter reset may be unrecoverable.

Do not erase NVS on a field device as casual troubleshooting; credentials and queued samples may be lost.

## 9. 72-hour soak and controlled power interruption

Run only with electrician approval for any energized installation. Record firmware SHA, start/end times, resets/watchdog/brownout events, uptime/heap, Wi-Fi outages, NTP availability, heartbeat/telemetry counts, queue depth/overflow, NVS errors, rotation state, and reference-meter deviations.

Pass only with no unexplained resets, corrupted credentials/queue, TLS bypass, unbounded memory decline, or measurement deviations outside predeclared limits. Perform controlled power interruptions at documented points. Do not use uncontrolled mains switching as a power-cut test.

## 10. Acceptance record

Mark PASS, FAIL, BLOCKED, or NOT TESTED. Attach redacted serial logs, backend evidence, calibration records, unpowered low-voltage photos, and electrician's separate installation approval.

| Gate | Initial status | Evidence / notes |
|---|---|---|
| Hardware and PZEM revisions identified | NOT TESTED | |
| Unpowered visual/interface checks | NOT TESTED | |
| Arduino IDE build and flash | NOT TESTED | |
| Low-voltage boot, Wi-Fi, fresh NTP | NOT TESTED | |
| TLS trusted-CA and fail-closed tests | NOT TESTED | |
| Provisioning and response-loss recovery | NOT TESTED | |
| PZEM UART compatibility | NOT TESTED | |
| Measurement calibration against reference | NOT TESTED | |
| Telemetry/idempotency/offline queue | NOT TESTED | |
| Credential rotation and rollback | NOT TESTED | |
| Meter reset/NVS recovery | NOT TESTED | |
| 72-hour soak and controlled interruption | NOT TESTED | |
| Qualified electrician sign-off | NOT TESTED | |

## Known limitations and release decision

- Monitoring-only: no relay outputs, remote switching, command polling, or protective trips.
- Offline queue holds at most eight samples; long outages can lose readings.
- Backend age limits can reject old queued samples.
- Meter reset gaps cannot be reconstructed.
- Credentials, queue, and energy state use Preferences/NVS.
- Flash encryption, secure boot provisioning, and physical flash-extraction protection remain explicitly out of scope.
- CI success does not imply hardware-in-loop, calibration, soak, or electrician sign-off.

Until all applicable hardware acceptance gates are completed and reviewed, classify firmware as an engineering preview for supervised monitoring. Do not call it production-certified or use it as an electrical protection device.
