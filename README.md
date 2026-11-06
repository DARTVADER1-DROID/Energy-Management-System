# Energy Management System

A residential energy management platform built incrementally with a .NET backend and ESP32-based edge firmware.

## Project goals

- Monitor household voltage, current, instantaneous power and accumulated energy.
- Keep electrical monitoring and protective logic local to the microcontroller.
- Provide a responsive web application with server-side rendering (SSR).
- Support remote access, analytics, basic bill estimation and controllable loads.
- Keep the codebase modular, readable, testable and suitable for incremental development.

## Repository layout

```text
docs/                         Architecture and engineering documentation
firmware/                     ESP32 firmware (introduced in a later milestone)
src/
  EnergyManagement.Api/       ASP.NET Core application host
tests/                        Automated tests (introduced with the first testable module)
REQUIREMENTS.md               Confirmed product requirements and open decisions
```

The backend is being developed as a **modular monolith**: one deployable application with clearly separated business modules. Microservices are not part of the initial plan.

## Current development status

**Milestone 0 — Foundation**

- [x] Capture confirmed requirements.
- [x] Document architecture direction.
- [x] Create the ASP.NET Core API host.
- [x] Add a lightweight liveness endpoint.
- [ ] Add automated tests and CI.
- [ ] Implement the first business module (device/telemetry ingestion).

No electrical control, device authentication, telemetry ingestion, or production safety behaviour is implemented yet.

## Getting started

Prerequisite: .NET 10 SDK.

Run the API:

```bash
dotnet run --project src/EnergyManagement.Api
```

Check liveness at:

```text
GET /health/live
```

The endpoint confirms that the API process is running. It does not yet check databases, devices, cloud dependencies or end-to-end system health.

## Important safety and security notice

This repository is an early software foundation, not a certified electrical protection system. Do not connect it to mains switching hardware or rely on it for electrical protection. Any eventual hardware implementation must use appropriately rated protection devices, validated sensing and switching circuits, fail-safe design, and qualified electrical review.

Device authentication is intentionally deferred by the current product requirements. An unauthenticated device-ingestion endpoint must not be exposed to the public internet. Production deployment is blocked until an appropriate authentication and security design is implemented.
