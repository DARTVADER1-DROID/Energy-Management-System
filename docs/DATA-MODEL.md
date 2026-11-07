# Data model and persistence

## Persistence boundaries

The application uses PostgreSQL and Entity Framework Core. The repository contains separate context/migration ownership for core energy/device data and Identity/security data. Do not manually edit an applied migration; add a forward migration.

## Core entities

| Entity / aggregate | Responsibility |
|---|---|
| Household | Household identity, display name and generated household code. |
| Device | Household-owned device identity, device code, hardware/firmware metadata, installation location, electrical configuration, active state and last-seen time. |
| DeviceThresholds | Optional advisory threshold values and warning persistence duration. These values do not trigger electrical disconnection. |
| TelemetryReading | Device/sample identity, capture and receive timestamps, schema version and complete versioned JSON payload stored in PostgreSQL JSONB. |
| Device security credentials | Device signing credential lifecycle, encrypted secret material, provisioning/recovery and revocation state. |
| Device authentication audit / nonce records | Security audit trail and replay-prevention data. |
| ApplicationUser | ASP.NET Core Identity account. |
| HouseholdMembership | User-to-household relationship and household role. |
| HouseholdInvitation | Hashed single-use invitation token, assigned household/role, expiry, acceptance and revocation timestamps. |
| SecurityAuditEvent | Household-scoped account and membership security events. |

## Identity boundaries

Human users authenticate through ASP.NET Core Identity. Device identities authenticate through a separate HMAC credential system. A device secret does not grant user access; a browser session does not authenticate a device.

Household membership is the basis for household-scoped data access. Global Identity roles and household roles serve different purposes and must not be conflated.

## Telemetry storage

Telemetry payloads are persisted as JSONB to preserve the schema-v1 envelope and optional future-compatible fields. Device/sample uniqueness prevents duplicate accepted samples. A device/capture-time index supports bounded history reads. Capture time is supplied by the device; receipt time is recorded by the server.

## Migrations and operations

- Apply migrations using the EF Core tooling or the explicitly configured staging startup migration path.
- Keep production migration execution controlled and reviewed.
- Back up PostgreSQL and test restoration before production use.
- Preserve the device credential encryption key across deployments and backups. Replacing it without a planned re-encryption/rotation procedure can make stored credentials unreadable.
- Render's current free staging database is disposable and must not hold important data.
