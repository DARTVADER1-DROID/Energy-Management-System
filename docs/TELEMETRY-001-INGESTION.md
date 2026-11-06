# TELEMETRY-001: Versioned Device Telemetry Ingestion

## Status

Initial server-side ingestion slice implemented on `feat/household-device-security`. Firmware publishing, alert evaluation, dashboard visualization, and production hardware validation remain future work.

## Contract

The initial envelope uses schema version 1 and is deliberately extensible:

```json
{
  "schemaVersion": 1,
  "sampleId": "f5d3f540-8a0d-4d3c-8c90-3a20e9c81020",
  "capturedAtUtc": "2026-11-07T10:15:30Z",
  "measurements": {
    "voltageVolts": 231.4,
    "currentAmps": 4.2,
    "activePowerWatts": 920.0,
    "energyWattHours": 12540.0,
    "frequencyHertz": 50.0,
    "powerFactor": 0.95,
    "temperatureCelsius": 34.5
  },
  "phases": {
    "L1": {
      "voltageVolts": 231.4,
      "currentAmps": 4.2,
      "activePowerWatts": 920.0,
      "energyWattHours": 12540.0,
      "powerFactor": 0.95
    }
  },
  "sensors": {
    "boardTemperatureCelsius": 34.5
  }
}
```

The root `measurements` object represents the primary aggregate/current installation reading. `phases` is optional and accepts L1, L2, and L3, allowing a single-phase device to publish only root measurements today and a three-phase device to add per-phase readings later. `sensors` is an optional bounded dictionary for additional scalar sensors.

## Authentication and replay protection

- Device publishing uses the existing HMAC-SHA-256 request authentication.
- The signature covers the HTTP method, exact request path/query, timestamp, nonce, and SHA-256 body digest.
- The existing one-minute timestamp window and persisted nonce uniqueness check apply.
- Revoked credentials, inactive devices, invalid signatures, stale timestamps, and replayed nonces are rejected.
- The API limits signed request bodies to the existing 1 MiB device-authentication ceiling and applies the existing per-device/IP rate limit.
- A `sampleId` is separately unique per device. This makes retrying an already accepted sample idempotently detectable even when the retry uses a fresh request nonce.

## Validation

Schema version 1 is accepted. Capture timestamps must be UTC and within seven days in the past or two minutes in the future. Numeric fields have conservative sanity bounds; at least one root or phase measurement is required. Only L1/L2/L3 phase keys are accepted, and additional sensors are limited to 16 entries with bounded names and values.

The limits are ingestion sanity checks, not electrical protection thresholds. Firmware remains responsible for local safety decisions.

## Persistence and retrieval

- PostgreSQL stores the full versioned envelope as JSONB.
- Device ID and sample ID have a unique index.
- Device ID and capture timestamp have an index for history queries.
- Server receipt time is recorded separately from device capture time.
- Authenticated household members can query their own device history; another household's device is returned as not found.
- History queries are bounded to 31 days and 500 rows.

## API

### Publish

`POST /api/device-telemetry`

Headers: `X-EMS-Device`, `X-EMS-Timestamp`, `X-EMS-Nonce`, `X-EMS-Signature`.

Body: versioned JSON envelope above. Returns 202 Accepted after persistence. Invalid payloads return 400, duplicate sample IDs return 409, and device-authentication failures return 401.

### Query history

`GET /api/devices/{deviceCode}/telemetry?fromUtc={ISO-8601}&toUtc={ISO-8601}&limit=200`

Requires an authenticated household member. The time range must be UTC, ordered, and no longer than 31 days.

## Next increments

1. Validate schema and persistence behavior with CI and PostgreSQL integration tests.
2. Add dashboard read models and real measurement cards/history charts.
3. Evaluate configured device thresholds into durable alert events with persistence/debounce semantics.
4. Implement ESP32 acquisition, NTP synchronization, encrypted NVS credential storage, offline queue, and signed publishing.
5. Validate end-to-end behavior on physical hardware over production HTTPS/proxy infrastructure.
