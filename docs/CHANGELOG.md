# Changelog

Notable project milestones are recorded here. Dates use UTC unless otherwise stated. Current implementation status is summarized in [FEATURES.md](FEATURES.md); historical test counts and old branch names are not statements about the current head.

## 2026-11-07 — ESP32 monitoring firmware and documentation consolidation

### Firmware and verification
- Added ESP32 Arduino C++ monitoring firmware for ESP32-WROOM-class boards with PZEM-004T v3.x acquisition.
- Added HTTPS provisioning, fresh NTP synchronization, HMAC-signed heartbeat and telemetry, NVS credential persistence, staged credential rotation and bounded persistent telemetry queue.
- Added dependency-free USB bench diagnostic sketch for chip/memory/reset reporting without PZEM, Wi-Fi or GPIO output use.
- Extended firmware CI to compile the bench diagnostic alongside the main firmware.
- Added hardware commissioning and production acceptance procedures.
- Relay/contactor control, remote commands and protective trips remain explicitly out of firmware scope.

### Documentation
- Reworked root README and product requirements to reflect the current code rather than older milestone assumptions.
- Added a feature catalogue, API reference, data-model guide and documentation index.
- Refreshed architecture, security, telemetry, frontend, testing, development, Render and firmware guides.
- Added Mermaid component/context, provisioning sequence, telemetry sequence, telemetry flow and household invitation flow diagrams.
- Retained historical Feature 001 test material while correcting stale status statements.

## 2026-11-07 — Household/device security and telemetry ingestion

### Included changes
- Household identity, membership administration, invitations, role management, password recovery and security audit history.
- Device provisioning with single-use codes, HMAC-SHA-256 request authentication, timestamp/nonce replay protection, credential rotation and emergency revocation.
- Versioned telemetry envelope with root measurements, optional L1/L2/L3 phase readings and bounded additional sensor values.
- Signed telemetry ingestion endpoint, JSONB persistence, per-device sample de-duplication and household-scoped history API.
- Dashboard latest-reading aggregation and server-rendered measurement summary.
- Current MFA state: disabled for all accounts by a database migration; MFA must not be represented as an enabled control.

### Validation notes
Historical successful workflow runs for earlier commits are retained in their original GitHub Actions records. Always inspect workflow runs for the exact commit SHA under review. Firmware CI compilation does not replace hardware testing.

## 2026-11-06 — Feature 001 and developer test workflow merged into main

**Merge pull request:** [#1](https://github.com/DARTVADER1-DROID/Energy-Management-System/pull/1)

- Household creation and generated household identifiers.
- Device registration with backend-generated immutable device codes.
- Per-household sequential device numbering and non-reuse after deactivation.
- Device metadata, electrical configuration and optional advisory warning thresholds.
- PostgreSQL 17 test database using Docker Compose and PowerShell test runner.
- Expanded domain, API-boundary, persistence and concurrency regression coverage.

The initial Feature 001 test milestone reported 97 passing tests in its recorded CI run. Later test suite growth and changes are documented in the live workflow history.
