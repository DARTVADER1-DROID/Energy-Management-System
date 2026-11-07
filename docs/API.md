# API reference

The API is hosted by the ASP.NET Core application. Swagger UI and the OpenAPI document are enabled only in the Development environment at /swagger and /swagger/v1/swagger.json.

## Health

| Method | Route | Purpose |
|---|---|---|
| GET | /health/live | Process liveness only. |
| GET | /health/ready | Readiness check including PostgreSQL connectivity. |

## Human-authenticated API

All household and device management routes require a signed-in user unless noted otherwise. Household authorization is evaluated from the authenticated user's memberships; a caller-supplied household ID is not proof of access.

| Method | Route | Purpose / access |
|---|---|---|
| POST | /api/households/ | Create a household; Household Administrator policy. |
| GET | /api/households/ | List households accessible to caller. |
| GET | /api/households/{householdId} | Read accessible household. |
| POST | /api/devices/{householdId} | Register a device in an authorized household. |
| GET | /api/devices/ | List accessible devices; supports bounded paging/filtering. |
| GET | /api/devices/{deviceCode} | Read accessible device details. |
| POST | /api/devices/{deviceCode}/provisioning-codes | Issue a short-lived provisioning code; administrator and recent-authentication gate. |
| POST | /api/devices/{deviceCode}/credential-rotations | Prepare a device credential rotation. |
| POST | /api/devices/{deviceCode}/credentials/revoke | Revoke device credentials. |
| GET | /api/devices/{deviceCode}/telemetry | Read telemetry history for a device in caller's household. |

Device management endpoint authorization and exact request contracts are defined in DeviceEndpoints.cs and the generated Development OpenAPI document. Do not use this summary as a substitute for the live schema.

## Device-authenticated API

These endpoints allow anonymous HTTP access at the ASP.NET authorization layer because the device itself authenticates with provisioning material or HMAC request headers. They are rate-limited and must not be treated as unauthenticated public functionality.

| Method | Route | Authentication |
|---|---|---|
| POST | /api/device-auth/provision | Device code plus valid one-time provisioning code over HTTPS. |
| POST | /api/device-auth/heartbeat | HMAC-signed device request; updates device last-seen state. |
| POST | /api/device-telemetry | HMAC-signed device request; accepts schema-v1 telemetry. |

Signed device requests use X-EMS-Device, X-EMS-Timestamp, X-EMS-Nonce and X-EMS-Signature. The signature binds the HTTP method, exact path/query, timestamp, nonce and SHA-256 body digest. Firmware refuses signed traffic until NTP has provided a valid clock.

## Telemetry schema

See [Telemetry ingestion](TELEMETRY-001-INGESTION.md) for the full JSON envelope, validation ranges, de-duplication and history constraints. Key fields are schemaVersion, sampleId, capturedAtUtc, measurements, optional phases (L1/L2/L3), and optional scalar sensors.

## Error behavior

Typical outcomes include 400 for malformed or out-of-range payloads, 401 for device authentication failure, 403 for authorization denial, 404 when a device is not visible to the caller, 409 for duplicate sample IDs, 202 for accepted telemetry and 200 for successful reads/heartbeat. Exact endpoint result unions remain authoritative.

## Security notes

- Do not publish provisioning codes or device secrets in logs, issue trackers or screenshots.
- Device request authentication and human cookie authentication are separate identities.
- Do not disable TLS certificate validation.
- Swagger is Development-only.
- No remote relay/control endpoint is currently implemented.
