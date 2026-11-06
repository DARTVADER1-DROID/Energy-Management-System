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
