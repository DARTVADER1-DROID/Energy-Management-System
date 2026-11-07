# Product requirements and delivery status

**Product:** Residential Energy Management System  
**Status:** Evolving requirements baseline; implementation status is tracked in [docs/FEATURES.md](docs/FEATURES.md).  
**Delivery principle:** Incremental, testable milestones; never imply planned scope is already implemented.

## 1. Product objective

Provide household energy monitoring and, in later independently reviewed increments, management and automation capabilities. Initial implementation prioritizes secure household/device identity, telemetry ingestion and monitoring.

## 2. Current implemented baseline

- .NET 10 ASP.NET Core modular monolith with Minimal APIs and Razor Pages.
- PostgreSQL and EF Core persistence.
- Human authentication through ASP.NET Core Identity; public self-registration disabled.
- Household membership and Administrator / Household Member / Read-only Viewer roles.
- Administrator-issued invitations, role management, membership removal and security audit events.
- MFA currently disabled for all accounts by the current migration.
- Device registration, generated stable device codes, metadata and optional advisory thresholds.
- One-time device provisioning, HMAC-SHA-256 signed requests, timestamp/nonce replay checks, credential rotation and revocation.
- Schema-v1 telemetry ingestion, validation, JSONB persistence, sample de-duplication and bounded history queries.
- Household dashboard read model using latest telemetry per device.
- ESP32 Arduino C++ monitoring firmware for ESP32-WROOM-class target and PZEM-004T v3.x.
- USB-only ESP32 diagnostic sketch.
- CI for .NET/PostgreSQL, frontend, firmware portable tests, Arduino compilation and Docker image build.

## 3. Initial monitoring requirements

The first hardware target is ESP32 with a PZEM-004T v3.x meter. Intended measurements include voltage (V), current (A), active power (W), cumulative energy (Wh/kWh), frequency and power factor where the sensor provides them. Single-phase acquisition is the first hardware target; telemetry schema allows optional L1/L2/L3 phase data.

The firmware samples on its configured interval (default 30 seconds in current sketch), synchronizes time through NTP, provisions securely and sends signed telemetry/heartbeats. Local buffering is bounded to eight samples.

## 4. Security requirements

- Human and device identities remain separate.
- Household access is enforced server-side from persisted memberships.
- Device credentials are per-device and can be rotated/revoked.
- Signed requests include method, path, timestamp, nonce and body digest.
- Device requests require fresh synchronized time and validated HTTPS.
- Provisioning codes are short-lived and single-use.
- Secrets are not committed to source or written to logs.
- MFA is currently disabled; enabling it requires a separate reviewed implementation and migration plan.

## 5. Future requirements (not implemented)

These remain product direction, not current functionality:
- Local safety state machine, protective trips and physical feedback.
- Relay/contactor control and manual load switching.
- Per-equipment connectivity-loss policies.
- Remote commands, schedules and rule-based automation.
- Durable threshold evaluation, alert events and notification delivery.
- Historical charts, analytics, billing/tariffs and period comparisons.
- PDF/Excel export and mobile push notifications.
- Optional three-phase physical acquisition and additional sensor drivers.
- Long-term local storage beyond the current bounded NVS queue.

Any future switching/protection feature requires independent safety engineering and qualified review. Cloud commands must never override local protection.

## 6. Data and hosting

- Cloud persistence: PostgreSQL / EF Core.
- Current staging: Render Blueprint, one ASP.NET Core Docker web service and PostgreSQL.
- ESP32 local persistence: Preferences/NVS for credentials, energy metadata and bounded queue.
- A local gateway, SQLite, LittleFS or microSD are not current firmware implementations unless separately introduced and tested.

## 7. Release acceptance

A release must identify the exact commit SHA and report CI status for that SHA. Hardware acceptance additionally requires board identification, sensor compatibility, calibrated comparison, network/TLS validation, outage/recovery testing, long-duration soak, enclosure/isolation review and qualified electrician sign-off.

No software release is a substitute for certified breakers, fuses, RCDs or other electrical protection.
