# ESP32 firmware production acceptance plan

## Release status

**Not production-certified.** CI covers native logic, source contracts, backend integration tests, and Arduino compilation. It does not prove electrical safety, PZEM calibration, RF/network reliability, NVS endurance, or behavior on the target board. Do not enable relay switching with this firmware.

## Supported baseline

- ESP32-WROOM-class target, compiled as `esp32:esp32:esp32` with ESP32 Arduino core 3.3.12.
- PZEM-004T V3.x TTL interface using PZEM004Tv30 1.2.1.
- UART2 defaults: ESP32 GPIO16 RX, GPIO17 TX; TX/RX are cross-connected.
- ArduinoJson 7.4.3.
- Monitoring and telemetry only.

Older PZEM revisions and unverified relay boards are not supported by this baseline.

## Automated CI gates

1. Python firmware/backend source-contract regression tests.
2. Native C++17 tests compiled with `-Wall -Wextra -Werror -pedantic`.
3. Backend .NET test suite, including provisioning recovery, expiry and invalidation tests.
4. Arduino CLI compile for the pinned ESP32 core and library versions.

All four must pass on the exact PR head SHA before release consideration.

## Required hardware-in-the-loop matrix

Record board revision, ESP32 module, PZEM model/revision, wiring, power supply, firmware SHA, test date, and operator for every run.

| Test | Procedure | Pass condition |
|---|---|---|
| Board identification | Confirm ESP32 module and USB-UART interface | Exact board recorded; correct target selected |
| PZEM compatibility | Confirm V3.x marking and TTL interface | No unsupported older PZEM revision |
| UART | Verify cross-connected TX/RX at configured pins | Stable readings without UART framing errors |
| TLS | Use production hostname and trusted CA | Valid CA succeeds; wrong CA and HTTP URL fail closed |
| Provisioning | Provision a fresh device | Secret persists and heartbeat is accepted |
| Lost provisioning response | Interrupt power/network after server commit, then retry same code before expiry | Same credential recovered; no second active credential |
| Provisioning expiry | Retry after original 10-minute deadline | Rejected; administrator re-provisions |
| HMAC/replay | Send valid request, then replay identical nonce | First accepted; replay rejected |
| Telemetry | Submit normal samples | Backend accepts HTTP 202 and stores correct UTC/sample ID/values |
| Duplicate delivery | Re-submit same sample ID after simulated lost response | HTTP 409 treated as delivered; no duplicate row |
| Bad payload | Deliberately submit invalid sample through test harness | HTTP 400 discarded without blocking queue |
| Offline queue | Disconnect network for up to eight intervals, reconnect | Stored samples delivered in original order with unchanged IDs/timestamps |
| Queue overflow | Remain offline beyond eight intervals | Existing queued samples retained; overflow logged and newest sample rejected |
| Clock failure | Block NTP at boot | No provisioning/authenticated requests until clock is valid |
| Credential rotation | Rotate with network interruptions and power cuts at each persistence/network phase | Either old credential remains valid or new credential is adopted; no silent loss of both |
| Meter reset | Reset PZEM energy counter under controlled test | Total remains monotonic; reset gap is documented as unrecoverable |
| Accuracy | Compare against a calibrated reference at several load/power-factor points | Error is within the PZEM manufacturer's and project-defined tolerance |
| Soak | Run continuously for at least 72 hours with planned Wi-Fi outages | No crash, watchdog reset, unbounded heap decline, or corrupted queue |
| Power interruption | Cut/reapply low-voltage ESP32 supply during queue and meter-state writes | No unsafe output state; state recovers within documented limits |

**Mains warning:** PZEM mains terminals and CT wiring can be lethal. Only a qualified electrician may install or run mains-connected tests using suitable isolation, fusing, enclosure, clearances and a controlled load. Never use a breadboard for mains wiring. Do not perform mains tests as an unsupervised firmware check.

## Security and operational constraints

- HTTPS and trusted CA validation are mandatory; no insecure TLS fallback.
- Device credentials and queue/meter state are stored in Preferences/NVS. This work deliberately does not enable flash encryption, secure boot provisioning or physical flash-extraction protection.
- Keep `Config.h` out of Git; remove the provisioning and rotation one-time secrets from it after successful use.
- The server's device credential encryption key must remain stable and backed up securely.
- The bounded queue stores eight samples only. Longer outages can lose new readings, and queued readings older than the backend's seven-day acceptance window cannot be ingested.
- Energy consumed during a PZEM counter-reset gap cannot be reconstructed.
- Firmware contains no relay output, command polling, remote switching, or protective trip logic.

## Release checklist

- [ ] All CI checks pass on the exact release commit.
- [ ] Hardware-in-the-loop matrix completed and evidence attached.
- [ ] PZEM accuracy characterized against a calibrated reference.
- [ ] 72-hour soak and power-interruption tests pass.
- [ ] Security review covers provisioning recovery and rotation.
- [ ] Qualified electrician signs off installation and enclosure.
- [ ] Release notes include board/PZEM revisions and known limitations.

Until every applicable item is complete, classify the firmware as **engineering preview / supervised monitoring**, not production-certified equipment.
