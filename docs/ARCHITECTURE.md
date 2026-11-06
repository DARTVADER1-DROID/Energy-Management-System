# Architecture Overview

## Architectural style

The backend uses a **modular monolith**: a single ASP.NET Core deployment containing cohesive business modules. Modules own their business rules and data boundaries. Cross-module interaction should use explicit contracts rather than reaching into another module's implementation.

We will not split modules into services unless a measured operational or scaling need justifies that decision.

## High-level topology

```text
Sensors and switching hardware
          |
          v
      ESP32 firmware
      - acquisition
      - local safety logic
      - local state/fault log
      - offline queue
          |
          | Wi-Fi + internet
          v
     Cloud .NET backend
      - device/telemetry modules
      - PostgreSQL
      - application services
          |
          v
      SSR web application
      - dashboards
      - controls
      - notifications
```

No always-on home gateway is required in the initial deployment. The ESP32 is the edge device. Local safety decisions must not depend on the cloud.

## Backend module candidates

These are planned boundaries, not all implemented modules:

- **Households and Access:** installations, users, roles and permissions.
- **Devices:** registration, device metadata, connectivity state and configuration.
- **Telemetry:** measurement contracts, validation, ingestion and querying.
- **Protection and Events:** received fault events and audit history. The actual time-critical protection logic remains in firmware.
- **Load Control:** commands, command outcomes and observed load state.
- **Automation:** schedules and rule evaluation.
- **Notifications:** notification records and delivery abstraction.
- **Analytics:** aggregates, comparisons and consumption summaries.
- **Billing:** tariff configuration and estimated cost calculations.
- **Reporting:** dashboard reporting initially; exports later.

Create a module when there is a real cohesive responsibility to isolate. Do not create empty projects or generic abstractions solely to make the folder tree look layered.

## Dependency rules

1. Business rules must not depend on ASP.NET Core endpoint types.
2. Modules should expose narrow contracts for cross-module communication.
3. Infrastructure details (PostgreSQL, external transports, filesystem providers) stay at module boundaries.
4. Hardware-specific firmware code stays separate from portable firmware logic.
5. Dashboard and API contracts must not become the domain model.
6. Local protection remains authoritative over cloud commands and automation.

## Persistence

- PostgreSQL is the initial cloud relational database.
- EF Core is the intended data-access technology.
- Module ownership of tables and migrations must be explicit.
- A future local server may use SQLite if that deployment is selected.
- ESP32 persistence is a separate concern: LittleFS initially, optional microSD behind a storage abstraction.

## Firmware boundaries

The firmware should distinguish:
- Portable application logic (measurement processing, state machines, watchdog policy).
- Hardware abstraction (sensor, relay/contactor feedback, clock/timer, storage).
- ESP32-specific drivers and board configuration.
- Network transport and cloud synchronisation.

Safety-critical state transitions must be deterministic, locally testable and independent of network callbacks.

## Frontend

A responsive web application with SSR is required. The framework and hosting model remain open until a dedicated frontend decision is made. It will consume backend contracts and must not own electrical safety decisions.

## Initial API host

The first backend increment is intentionally small:
- ASP.NET Core host.
- Liveness endpoint at `GET /health/live`.
- Readiness endpoint at `GET /health/ready`, which checks PostgreSQL connectivity.
- No database, authentication, telemetry ingestion or hardware-control endpoints yet.

The liveness endpoint only reports that the API process is responding. It is not a readiness or end-to-end health check.
