# Feature catalogue

This catalogue describes what is present in the repository at the current development baseline. “Implemented” means code and automated tests exist; it does not mean production or physical-hardware acceptance.

## Implemented application capabilities

| Area | Capability | Current behavior and boundary |
|---|---|---|
| Accounts | Sign-in, sign-out, password reset | ASP.NET Core Identity; self-registration is disabled. Initial administrator is deployment-bootstrapped. |
| Household access | Membership and roles | Administrator, Household Member, Read-only Viewer; household-scoped checks are applied to application queries and mutations. |
| Invitations | Invite and accept users | Administrator-issued, single-use hashed tokens; 24-hour expiry, revocation and email delivery. |
| Account security | Recent-authentication gates and audit events | Sensitive account/device actions require reauthentication where configured; household membership changes are audited. |
| MFA | Current deployment state | MFA is disabled for all users by the current migration. Do not describe MFA as an active security control. |
| Households | Create/list/read | Household records and generated household codes are persisted in PostgreSQL. |
| Devices | Register/list/read/deactivate | Device metadata, electrical configuration and warning-threshold configuration; stable generated device codes. |
| Device identity | Provisioning and credentials | Short-lived one-time provisioning codes, device-specific HMAC credentials, credential rotation and revocation. |
| Device authentication | Signed device requests | HMAC-SHA-256 signatures cover method, path, timestamp, nonce and body digest; timestamp and nonce replay checks apply. |
| Telemetry API | Ingest/query | Schema v1 envelopes, numeric sanity validation, per-device sample de-duplication, JSONB persistence and household-scoped history reads. |
| Dashboard | Current household snapshot | Latest sample per device; aggregates power, average voltage/current and cumulative energy where available. Missing values remain unavailable. |
| Web UI | Server-rendered pages | Dashboard, device inventory/details/registration, alerts foundation, settings and account/household administration pages. |
| ESP32 firmware | Monitoring-only client | Arduino C++ sketch for ESP32-WROOM-class boards, PZEM-004T v3.x, HTTPS provisioning, NTP, signed heartbeat/telemetry, NVS credential storage and bounded persistent queue. |
| ESP32 diagnostics | USB bench diagnostic | Dependency-free sketch reports chip/memory/reset details without Wi-Fi, PZEM or GPIO output use. |
| CI | Automated validation | .NET tests, frontend tests, firmware compile and native/Python firmware contract tests are defined in GitHub Actions. |

## Partially implemented / operationally limited

- Device threshold values can be configured, but durable threshold evaluation, alert generation and notification delivery are not yet implemented.
- /Alerts is a foundation/status page, not a complete alert engine.
- Telemetry history can be queried by authorized users, but a complete historical charting and analytics experience is not present.
- The ESP32 offline queue is bounded to eight samples. At the default 30-second interval this is about four minutes; it is not an unlimited store-and-forward system.
- Firmware supports a PZEM driver in code, but real sensor accuracy, wiring, calibration and hardware compatibility have not been verified on the user's physical unit.
- Render Blueprint describes a temporary staging environment. A staging deployment is not production approval.
- MFA is disabled; do not infer that the existence of MFA-related pages or recovery code types means MFA is active.

## Not implemented / explicitly out of current firmware scope

- Relay/contactor actuation, remote switching, load control, protective trips and local electrical protection state machines.
- Cloud commands or remote override of local protection.
- Schedule/rule automation, billing/tariff calculations, PDF/Excel reports and push notifications.
- Production hardware commissioning, calibrated metering acceptance, 72-hour soak and electrician sign-off.

## Safety boundary

This is a monitoring and management software project, not certified protective equipment. Cloud thresholds and telemetry are advisory. Time-critical protection must be provided by independently rated electrical protective devices and, if later developed, a separately engineered local controller. Mains work must be performed by a qualified electrician.
