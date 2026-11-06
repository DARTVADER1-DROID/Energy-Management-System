# Energy Management System

A residential energy monitoring and management platform, developed incrementally.

## Current development

- Feature 001: household and device registration / identity management
- Backend: .NET 10 modular monolith
- Persistence: PostgreSQL with EF Core

## Test suite

See [Feature 001 test instructions](docs/TESTING.md) for prerequisites, coverage, and
the command to run the unit and PostgreSQL integration tests.

## Safety and deployment note

Authentication and role-based authorization are intentionally deferred in this early
development stage. Do not expose the API to the public internet or use it in production.
Dashboard warning thresholds are not electrical protection settings.
