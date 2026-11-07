# Energy Management System

See the [project changelog](docs/CHANGELOG.md) for merged features and verified project updates.

A residential energy monitoring and management platform, developed incrementally.

## Current development

- Backend: .NET 10 modular monolith with ASP.NET Core Minimal APIs and server-rendered Razor Pages.
- Persistence: PostgreSQL with EF Core and module-owned migrations.
- Identity and access: email/password authentication, household membership/roles, invitations, administrator controls and recent-authentication gates. MFA is currently disabled for all accounts.
- Device security: single-use provisioning codes, per-device HMAC-SHA-256 credentials, timestamp/nonce replay protection, credential rotation/revocation and security audit history.
- Telemetry: versioned flexible envelope, signed ingestion, JSONB persistence, per-device sample deduplication and household-scoped history queries.
- Dashboard: latest persisted household measurements for power, voltage, current and cumulative energy.

The telemetry contract supports root measurements now and optional L1/L2/L3 phase measurements as hardware evolves. See [telemetry ingestion](docs/TELEMETRY-001-INGESTION.md), [security implementation](docs/SECURITY-001-AUTHENTICATION-AND-DEVICE-IDENTITY.md), and [architecture](docs/ARCHITECTURE.md).

## Not yet implemented

- ESP32 firmware, USB serial / temporary Wi-Fi provisioning client, NTP synchronization and secure local credential storage.
- Physical metering sensor integration and hardware-specific acquisition drivers.
- Local safety state machine, relay/contactor control and physical feedback integration.
- Offline telemetry queue, command polling, alert evaluation/notifications and historical charts.
- Production HTTPS/proxy and physical-device end-to-end validation.

## Run the API and Swagger UI locally

Requirements: .NET 10 SDK and a reachable PostgreSQL database. Configure the
`ConnectionStrings__EnergyManagement` environment variable before starting the API.

Run the API from the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__EnergyManagement = "Host=localhost;Port=5432;Database=energy_management;Username=postgres;Password=YOUR_LOCAL_PASSWORD"
dotnet ef database update --project src/EnergyManagement.Api/EnergyManagement.Api.csproj --startup-project src/EnergyManagement.Api/EnergyManagement.Api.csproj
dotnet run --project src/EnergyManagement.Api/EnergyManagement.Api.csproj
```

Open the Swagger UI at `/swagger` on the URL printed by `dotnet run` (typically
`http://localhost:5000/swagger` or `https://localhost:7000/swagger`). The OpenAPI
document is available at `/swagger/v1/swagger.json`. Swagger UI and its document endpoint are enabled only in the Development environment.
The API also exposes `/health/live` for process liveness and `/health/ready` for a
PostgreSQL connectivity readiness check. Install the EF CLI once with
`dotnet tool install --global dotnet-ef --version 10.0.0` before applying migrations.

## Run the test suite locally

Requirements: .NET 10 SDK, Docker Desktop with Docker Compose, and PowerShell.

From the repository root, run:

```powershell
.\scripts\test.ps1
```

The script starts an isolated PostgreSQL 17 test database, runs the test project in
Release configuration (including restore and build), and stops the database afterward.
No separately installed PostgreSQL server is required.

To keep the database running for debugging:

```powershell
.\scripts\test.ps1 -KeepDatabase
```

The default host port is 55432. Set `EMS_TEST_POSTGRES_PORT` before running the script
to use another available port. The script temporarily sets
`EMS_TEST_CONNECTION_STRING` for the test process and restores its previous value.

See [test coverage and details](docs/TESTING.md),
[Feature 001 test-suite documentation](docs/FEATURE-001-TEST-SUITE.md), and the
[development workflow](docs/DEVELOPMENT.md).

## Safety and deployment note

Authentication, MFA and household role-based authorization are implemented, but this
project is still under active development and is not production-ready. Do not connect
unvalidated hardware or rely on the cloud service for immediate electrical protection.
All time-critical sensing, fault handling and protective shutdown must execute locally
on the firmware; backend telemetry and warning thresholds are advisory only. Use
properly rated breakers, fuses and RCDs independently of this software.

Do not expose a deployment publicly until production HTTPS/proxy behavior, secrets,
database operations, firmware security and end-to-end hardware behavior have been
validated. A successful automated test run is not a substitute for electrical safety
certification or physical-device testing.
