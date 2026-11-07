# Development guide

## Prerequisites

- .NET 10 SDK
- PostgreSQL 17 for integration tests (Docker Compose helper is recommended)
- Node.js 22 and npm for Tailwind/frontend tests
- Arduino IDE or Arduino CLI with Espressif ESP32 core 3.3.12 for firmware
- Python 3 for firmware contract tests
- A C++17 compiler for native firmware-core tests

## Local application setup

Set a local PostgreSQL connection string as ConnectionStrings__EnergyManagement. Do not commit credentials or paste production secrets into issues/chat.

PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__EnergyManagement = "Host=localhost;Port=5432;Database=energy_management;Username=postgres;Password=YOUR_LOCAL_PASSWORD"
dotnet run --project src/EnergyManagement.Api/EnergyManagement.Api.csproj
```

Apply migrations deliberately with dotnet-ef where required. Install the tool if missing:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.0
dotnet ef database update --project src/EnergyManagement.Api/EnergyManagement.Api.csproj --startup-project src/EnergyManagement.Api/EnergyManagement.Api.csproj
```

Swagger is Development-only at /swagger. Liveness and readiness are /health/live and /health/ready.

## Frontend assets

```bash
npm install
npm run build
npm run test:frontend
```

Tailwind input: src/EnergyManagement.Api/Web/Styles/tailwind.css. Generated output: src/EnergyManagement.Api/wwwroot/css/site.css.

## Test commands

```powershell
.\scripts\test.ps1
```

See [TESTING.md](TESTING.md) for test database behavior and CI details.

## Firmware development

Main sketch: firmware/esp32-arduino/EMS_ESP32. Use Config.example.h as a template for a local Config.h, which is ignored by Git. Never commit Wi-Fi passwords, provisioning codes, device secrets or private CA material.

For an ESP32-WROOM-32 class board, ESP32 Dev Module is a provisional Arduino IDE selection. The main firmware expects a PZEM-004T v3.x; without the meter, use firmware/esp32-arduino/ESP32_Bench_Diagnostic for USB-only chip diagnostics. The bench sketch validates basic boot/serial behavior only.

CI pins Arduino CLI, ESP32 core and library versions. Run native and Python contract tests before changing portable firmware logic. Do not claim hardware validation from CI compilation.

## Working agreements

- Make one cohesive change per PR and keep main buildable.
- Read the current implementation before editing documentation or behavior.
- Update feature catalogue, API/data model/architecture docs and diagrams when contracts change.
- Keep implementation status explicit: implemented, partial, planned or out of scope.
- Add regression tests for fixes and boundary behavior.
- Never weaken security or tests merely to get a green check.
- Keep secrets out of source, logs and commit messages.
- Avoid unrelated formatting churn and speculative dependencies.
- Do not delete migrations, tests, configuration or operational files without proving they are unused.

## Branches and commits

Use focused feature/fix/docs branches. Commit prefixes: feat:, fix:, docs:, test:, refactor:, build:. Prefer small reviewable commits.

## Electrical safety gate

No mains connection, CT wiring, energized measurement, relay or contactor integration should be performed from software instructions alone. A qualified electrician must assess isolation, fusing, enclosure, ratings, protective coordination and test procedures. This software is not a protective device.
