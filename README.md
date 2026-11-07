# Energy Management System (EMS)

Residential energy monitoring platform with an ESP32 edge device, .NET 10 backend, PostgreSQL persistence and server-rendered web application.

> **Development status:** active development / staging. This repository is not a certified electrical protection system and is not approved for production mains control.

## What is in the repository

- **Backend:** .NET 10 ASP.NET Core modular monolith; Minimal APIs and Razor Pages in one deployable application.
- **Persistence:** PostgreSQL through EF Core; separate energy/device and Identity/security persistence boundaries.
- **Human identity:** ASP.NET Core Identity sign-in, password reset, household membership, invitations and role checks. MFA is currently disabled for all users by the current migration.
- **Device identity:** one-time provisioning codes, per-device HMAC-SHA-256 credentials, replay protection, rotation and revocation.
- **Telemetry:** versioned schema-v1 envelope, validated ingestion, JSONB storage, sample de-duplication and household-scoped history queries.
- **Dashboard:** latest stored readings per device, with available power, voltage, current and cumulative-energy aggregates.
- **Firmware:** Arduino C++ monitoring client for ESP32-WROOM-class boards and PZEM-004T v3.x; HTTPS provisioning, NTP, signed heartbeat/telemetry, NVS credential storage and a bounded offline queue.
- **Bench diagnostic:** independent USB-only ESP32 chip/memory diagnostic sketch.
- **Validation:** .NET/PostgreSQL integration tests, frontend Node tests, firmware native C++ and Python contract tests, Arduino CLI compilation and Docker image build in CI.

See [Feature catalogue](docs/FEATURES.md) for implemented, partial and planned capabilities.

## Repository map

```text
.
├── src/EnergyManagement.Api/       # ASP.NET Core host, modules, Razor Pages and static assets
├── tests/
│   ├── EnergyManagement.Tests/     # .NET domain and PostgreSQL integration tests
│   └── frontend/                   # Node frontend regression tests
├── firmware/esp32-arduino/
│   ├── EMS_ESP32/                  # Main monitoring firmware
│   ├── ESP32_Bench_Diagnostic/     # USB-only board diagnostic
│   └── tests/                      # Native C++ and Python contract tests
├── docs/                           # Architecture, API, security, operations and diagrams
├── scripts/                        # Local test helpers
├── .github/workflows/              # CI workflows
├── Dockerfile
└── render.yaml                     # Temporary staging Blueprint
```

## Documentation

| Document | Description |
|---|---|
| [Feature catalogue](docs/FEATURES.md) | What exists now, partial features and explicit exclusions. |
| [Architecture](docs/ARCHITECTURE.md) | Component boundaries, data flow and design rules. |
| [API reference](docs/API.md) | Health, human-authenticated and device-authenticated routes. |
| [Data model](docs/DATA-MODEL.md) | Main entities, persistence and migration ownership. |
| [Security](docs/SECURITY-001-AUTHENTICATION-AND-DEVICE-IDENTITY.md) | Human/device identities, authorization and credential lifecycle. |
| [Telemetry contract](docs/TELEMETRY-001-INGESTION.md) | Schema v1, validation, signing and storage. |
| [Development](docs/DEVELOPMENT.md) | Local setup, coding workflow and contribution rules. |
| [Testing](docs/TESTING.md) | Test layers, commands and CI jobs. |
| [Frontend](docs/WEB-FRONTEND.md) | Razor Pages, Tailwind build and page inventory. |
| [Render staging](docs/RENDER-DEPLOYMENT.md) | Staging infrastructure, configuration and operational cautions. |
| [Firmware guide](firmware/esp32-arduino/README.md) | Main ESP32 firmware configuration and behavior. |
| [Hardware commissioning](firmware/esp32-arduino/HARDWARE_COMMISSIONING.md) | Qualified-hardware commissioning procedure. |
| [Diagrams](docs/diagrams/README.md) | UML-style component, sequence and flow diagrams. |
| [Changelog](docs/CHANGELOG.md) | Notable project milestones. |

## Run locally

### Backend

Requirements: .NET 10 SDK and PostgreSQL. Set the connection string using an environment variable; never commit credentials.

PowerShell example:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__EnergyManagement = "Host=localhost;Port=5432;Database=energy_management;Username=postgres;Password=YOUR_LOCAL_PASSWORD"
dotnet run --project src/EnergyManagement.Api/EnergyManagement.Api.csproj
```

Apply migrations with the EF Core CLI when needed. See [Development](docs/DEVELOPMENT.md). In Development, Swagger is available at /swagger. Health endpoints are /health/live and /health/ready.

### Frontend assets

```bash
npm install
npm run build
npm run test:frontend
```

### Automated tests

On Windows with .NET 10, Docker Desktop / Compose and PowerShell:

```powershell
.\scripts\test.ps1
```

This creates an isolated disposable PostgreSQL 17 test database and runs the .NET suite. Firmware CI can be run through GitHub Actions; local instructions are in [Testing](docs/TESTING.md).

### ESP32 firmware

Use Arduino IDE with the Espressif ESP32 core. Select **ESP32 Dev Module** provisionally for a typical ESP32-WROOM-32. Main firmware requires a PZEM-004T v3.x and local-only Config.h derived from Config.example.h. For a first board-only test, use the dependency-free [USB bench diagnostic](firmware/esp32-arduino/ESP32_Bench_Diagnostic/README.md).

## Safety and release gate

Current firmware is monitoring-only. Relay/contactor control, remote switching, protective trips and cloud commands are not implemented. Backend threshold settings are advisory and threshold evaluation/notification delivery are incomplete. Do not connect mains or switching hardware based on this software. A qualified electrician must perform all mains work, and independent certified protective devices remain mandatory.

A successful CI build does not establish hardware compatibility, metering accuracy, electrical safety or production readiness. Production requires security review, TLS/proxy and database operational validation, physical commissioning, calibration, soak testing and qualified sign-off.
