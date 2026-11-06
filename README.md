# Energy Management System

A residential energy monitoring and management platform, developed incrementally.

## Current development

- Feature 001: household and device registration / identity management
- Backend: .NET 10 modular monolith
- Persistence: PostgreSQL with EF Core

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

Authentication and role-based authorization are intentionally deferred in this early
development stage. Do not expose the API to the public internet or use it in production.
Dashboard warning thresholds are not electrical protection settings.
