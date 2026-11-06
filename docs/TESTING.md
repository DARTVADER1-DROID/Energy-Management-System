# Automated test suite

The test project validates the household and device registration domain, HTTP API,
PostgreSQL persistence, and concurrent device-number allocation.

## Run locally (recommended)

### Prerequisites

- .NET 10 SDK
- Docker Desktop with Docker Compose
- PowerShell

From the repository root:

```powershell
.\scripts\test.ps1
```

The script starts PostgreSQL 17 using `docker-compose.test.yml`, waits for its
health check, sets `EMS_TEST_CONNECTION_STRING`, and runs the test project in Release
configuration. `dotnet test` performs restore and build automatically. The database
container is stopped and removed afterward.

To leave PostgreSQL running for debugging, use:

```powershell
.\scripts\test.ps1 -KeepDatabase
```

The default host port is 55432. If that port is occupied, choose another:

```powershell
$env:EMS_TEST_POSTGRES_PORT = "55433"
.\scripts\test.ps1
Remove-Item Env:EMS_TEST_POSTGRES_PORT
```

The script temporarily overrides `EMS_TEST_CONNECTION_STRING` for the test run and
restores the caller's previous value afterward. The database uses disposable local
development credentials and must not be reused for production data.

## Run manually (without the helper script)

If PostgreSQL 17 is already available, supply a test connection string and run:

```powershell
$env:EMS_TEST_CONNECTION_STRING = "Host=localhost;Port=5432;Database=energy_management_tests;Username=test_user;Password=test_password"
dotnet test tests/EnergyManagement.Tests/EnergyManagement.Tests.csproj --configuration Release
```

The tests apply the real EF Core migration against PostgreSQL; they do not use EF Core's
in-memory provider as a substitute. GitHub Actions provisions an isolated PostgreSQL 17
service container for every workflow run.

## Test coverage

### Domain

- Household name validation, trimming and maximum-length boundaries
- Device construction, required metadata and positive device-number invariants
- Device identity, initial state and registration timestamp
- Optional description normalisation
- Metadata update validation and threshold replacement/clearing
- Single-phase and three-phase configuration
- Soft deactivation and stable device identity
- Last-seen timestamp UTC validation and monotonic ordering
- Optional thresholds and optional temperature
- Negative thresholds, invalid voltage ranges, and invalid warning persistence
- Required persistence duration when a warning threshold is configured

### API and persistence

- Household creation, trimming, generated code, Location header and validation
- Household listing and deterministic ordering
- Device registration, metadata persistence, and generated identifiers
- Required-field validation and maximum-length boundaries
- Unsupported electrical configuration and threshold boundary validation
- Missing household/device and invalid request handling
- Invalid registration does not consume a device number
- Device listing across households and by household filter
- Soft deactivation is idempotent and inactive devices remain retrievable/listed
- Optional temperature threshold remains optional

### Concurrency

- Twenty simultaneous registrations through independent dependency-injection scopes
- Unique, sequential identifiers under concurrent registration
- Persisted device count verified after concurrent registration
- Deactivated identifiers are not reused
- Device numbering is independent per household

## Latest validation

The expanded Feature 001 suite completed successfully in GitHub Actions on 6 November 2026:

- **97 passed**
- **0 failed**
- **0 skipped**
- **9 seconds** test execution time
- .NET 10 with PostgreSQL 17

See [the successful workflow run](https://github.com/DARTVADER1-DROID/Energy-Management-System/actions/runs/42248843753)
and the [comprehensive test-suite document](FEATURE-001-TEST-SUITE.md) for detailed
coverage, scope exclusions and maintenance expectations.

## Known scope boundaries

Authentication and role-based authorization are not implemented yet, so the tests do not
assert administrator/member/viewer access rules. The API must not be exposed publicly
until authentication, authorization, and secure device provisioning are implemented.

These tests do not validate physical electrical protection, firmware behavior, telemetry,
or warning evaluation. Dashboard warning thresholds are not protection settings.
