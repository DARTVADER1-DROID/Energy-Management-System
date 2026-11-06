# Changelog

Notable project updates are recorded here. Dates use UTC unless otherwise stated.

## 2026-11-06 — Feature 001 and developer test workflow merged into main

**Merge pull request:** [#1](https://github.com/DARTVADER1-DROID/Energy-Management-System/pull/1)  
**Merge commit:** [fcf3f44](https://github.com/DARTVADER1-DROID/Energy-Management-System/commit/fcf3f44e0705d297ac51e2b8cd73d7b7efb2b713)

The build/local-test-workflow branch was merged into main. It already contained the complete history of feat/device-registration, so one merge brought both development branches into the primary branch without duplicating commits.

### Included changes

- **Feature 001 — Device Registration and Identity Management**
  - Household creation and generated household identifiers.
  - Device registration with backend-generated, immutable device codes.
  - Per-household sequential device numbering, with identifiers not reused after deactivation.
  - Device metadata, electrical configuration, optional description and configurable warning thresholds.
  - Household and device listing, retrieval and deactivation API endpoints.
- **Local developer test workflow**
  - PostgreSQL 17 test database using Docker Compose.
  - PowerShell test runner with automatic database startup, cleanup and connection-string restoration.
  - Optional database retention for debugging and configurable host port.
- **Regression tests and documentation**
  - Expanded domain, API-boundary, persistence and concurrency coverage.
  - Feature 001 test-suite documentation and updated development/testing instructions.

### Validation

The pull-request CI workflow completed successfully before merge:

- .NET 10 and PostgreSQL 17.
- **97 tests passed; 0 failed; 0 skipped.**
- Test execution duration: 7 seconds.
- [GitHub Actions run](https://github.com/DARTVADER1-DROID/Energy-Management-System/actions/runs/42249562360).

The source branches were retained after merging. Authentication and role-based authorization remain deferred; the API must not be exposed to the public internet or used in production until the security requirements documented for Feature 001 are implemented.


## 2026-11-07 — Household/device security and telemetry ingestion

### Included changes

- Household identity, membership administration, invitations, role management, MFA recovery controls and security activity.
- Device provisioning with single-use codes, HMAC-SHA-256 request authentication, timestamp/nonce replay protection, credential rotation and emergency revocation.
- Persistent device security audit history with administrator-only visibility.
- Versioned flexible telemetry envelope with root measurements, optional L1/L2/L3 phase readings and bounded additional sensor values.
- Signed telemetry ingestion endpoint, JSONB persistence, per-device sample deduplication and household-scoped history API.
- PostgreSQL migration and integration coverage for valid ingestion, duplicate samples and invalid measurement rejection.

### Validation status

The telemetry implementation commit `339d0c8` passed the full GitHub Actions .NET test workflow with PostgreSQL 17 (run [#267](https://github.com/DARTVADER1-DROID/Energy-Management-System/actions/runs/42283344008)). The subsequent architecture/documentation commit `65cec3c` also passed CI (run [#269](https://github.com/DARTVADER1-DROID/Energy-Management-System/actions/runs/42283400780)). The dashboard latest-reading aggregation and server-rendered metrics integration also passed CI (run [#277](https://github.com/DARTVADER1-DROID/Energy-Management-System/actions/runs/42283808112)). The full suite subsequently passed with **153 tests, 0 failures and 0 skips** after the telemetry-history and dashboard-label regression coverage was added (run [#281](https://github.com/DARTVADER1-DROID/Energy-Management-System/actions/runs/42283966686)). Firmware publishing, dashboard measurement cards, threshold alert evaluation and physical-device validation remain outstanding.
