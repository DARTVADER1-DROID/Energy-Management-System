# Feature 001 test suite

The test project validates the household and device registration domain, HTTP API,
PostgreSQL persistence, and concurrent device-number allocation.

## Prerequisites

- .NET 10 SDK
- Docker Engine / Docker Desktop running (the PostgreSQL integration fixture starts
  an isolated `postgres:17-alpine` container through Testcontainers)

## Run all tests

From the repository root:

```bash
dotnet test tests/EnergyManagement.Tests/EnergyManagement.Tests.csproj
```

The fixture creates an isolated test database container. Tests apply the real EF Core
migration; they do not use EF Core's in-memory provider as a substitute for PostgreSQL.

## Test coverage

### Domain

- Household name validation and trimming
- Sequential device-number allocation
- Required device metadata and metadata updates
- Single-phase and three-phase configurations
- Stable household-scoped device codes
- Soft deactivation
- Last-seen timestamp ordering and UTC validation
- Optional thresholds and optional temperature
- Negative thresholds, invalid voltage ranges, and invalid warning persistence
- Required persistence duration when a warning threshold is configured

### API and persistence

- Household creation and validation
- Device registration, metadata persistence, and generated identifiers
- Missing household and invalid request handling
- Threshold validation at the HTTP boundary
- Device listing by household
- Soft deactivation while retaining device history

### Concurrency

- Twenty simultaneous registrations through independent dependency-injection scopes
- Unique, sequential identifiers under concurrent registration
- Deactivated identifiers are not reused

## Known scope boundaries

Authentication and role-based authorization are not implemented yet, so the tests do not
assert administrator/member/viewer access rules. The API must not be exposed publicly
until authentication, authorization, and secure device provisioning are implemented.

These tests do not validate physical electrical protection, firmware behavior, telemetry,
or warning evaluation. Dashboard warning thresholds are not protection settings.
