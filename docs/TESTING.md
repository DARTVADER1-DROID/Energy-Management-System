# Testing and continuous integration

## Test layers

| Layer | Tool / location | What it checks |
|---|---|---|
| .NET domain | xUnit, tests/EnergyManagement.Tests/Domain | Household/device invariants, metadata, thresholds and lifecycle rules. |
| .NET integration | xUnit + PostgreSQL 17, tests/EnergyManagement.Tests/Integration | HTTP behavior, Identity/security, provisioning, signed device auth, telemetry persistence and household boundaries. |
| Frontend | Node built-in test runner, tests/frontend | Client-side resilience and theme behavior. |
| Firmware portable logic | C++17, firmware/esp32-arduino/tests/native | Numeric validation, energy-counter normalization, retry/backoff and queue edge cases. |
| Firmware contracts | Python unittest, firmware/esp32-arduino/tests | Cross-check firmware/API assumptions and contract invariants. |
| Firmware compile | Arduino CLI + ESP32 core 3.3.12 | Main firmware and dependency-free diagnostic compile for generic ESP32 target. |
| Container build | Docker | Production Docker image build. |

## Run .NET integration suite locally

Requirements: .NET 10 SDK, Docker Desktop with Compose, PowerShell.

```powershell
.\scripts\test.ps1
```

The helper starts PostgreSQL 17 on host port 55432, waits for readiness, supplies EMS_TEST_CONNECTION_STRING to the test process and removes the container afterward. Use -KeepDatabase to retain it for debugging. Set EMS_TEST_POSTGRES_PORT to change the host port.

Manual run against a dedicated disposable PostgreSQL 17 database:

```powershell
$env:EMS_TEST_CONNECTION_STRING = "Host=localhost;Port=5432;Database=energy_management_tests;Username=test_user;Password=test_password"
dotnet test tests/EnergyManagement.Tests/EnergyManagement.Tests.csproj --configuration Release
```

The integration suite applies actual EF Core migrations. It intentionally resets test state; never point it at production or a data-bearing database.

## Run frontend tests

```bash
npm install
npm run test:frontend
npm run build
```

## Run firmware portable tests

```bash
python -m unittest discover -s firmware/esp32-arduino/tests -v
g++ -std=c++17 -Wall -Wextra -Werror -pedantic firmware/esp32-arduino/tests/native/test_firmware_core.cpp -o /tmp/ems-firmware-tests
/tmp/ems-firmware-tests
```

Arduino compilation is performed by the GitHub Actions firmware workflow. It installs Arduino CLI 1.2.2, ESP32 Arduino core 3.3.12, ArduinoJson 7.4.3 and PZEM004Tv30 1.2.1, then compiles both the main sketch and the bench diagnostic.

## CI workflows

### .NET tests

.github/workflows/dotnet-tests.yml runs on pushes to main and supported feature/build/deploy/fix branches and pull requests targeting main. It provisions PostgreSQL 17, runs frontend tests, builds Tailwind, runs .NET tests and builds the Docker image.

### ESP32 firmware

.github/workflows/esp32-firmware.yml runs for firmware and related device/telemetry contract changes, plus manual dispatch. It runs Python contract tests, native C++ edge-case tests and Arduino CLI compilation.

## Interpreting results

A green CI run verifies only the checks executed for that exact commit SHA. A workflow that is queued or in progress is not a pass. Documentation-only commits may not trigger every workflow. Always inspect the run's commit SHA and each job's status before reporting validation.

## Physical validation not covered by CI

CI does not test a physical ESP32, USB cable/serial driver, Wi-Fi network, PZEM UART, metering accuracy, TLS behavior through the live Render proxy, NVS power-loss behavior on the actual board, mains isolation, protective devices or electrical safety. Complete the [hardware commissioning](../firmware/esp32-arduino/HARDWARE_COMMISSIONING.md) and [production acceptance](../firmware/esp32-arduino/PRODUCTION_ACCEPTANCE.md) procedures before production consideration.
