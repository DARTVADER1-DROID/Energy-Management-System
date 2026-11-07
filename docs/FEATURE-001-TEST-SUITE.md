# Feature 001 — Registration regression coverage

This document preserves the original registration test scope as a focused reference. For the current full test strategy, commands, CI workflows and validation interpretation, see [TESTING.md](TESTING.md).

## Covered behaviors

- Household name validation, normalization and generated identifiers.
- Device creation, required metadata, stable identity and lifecycle.
- Optional descriptions and metadata updates.
- Single-phase and three-phase electrical configuration.
- Threshold configuration boundaries and clearing.
- Device registration API validation and HTTP responses.
- Device listing, filtering, pagination and deactivation.
- Concurrent per-household device-number allocation and non-reuse after deactivation.
- PostgreSQL-backed EF Core migrations and persistence behavior.

## Test implementation

The xUnit project is tests/EnergyManagement.Tests. Domain tests live under Domain; API and persistence tests live under Integration; shared host and PostgreSQL fixtures live under Infrastructure. Integration tests use PostgreSQL rather than relying on an in-memory provider for relational semantics.

## Historical validation

The original Feature 001 milestone reported 102 passing tests in a specific CI run. That number is historical and is not a statement about the current test count or current branch head. Always inspect the latest GitHub Actions runs for the exact commit being reviewed.

## Scope boundary

The original registration milestone did not include authentication or authorization. Those capabilities were added in later increments. Current security behavior is described in [SECURITY-001](SECURITY-001-AUTHENTICATION-AND-DEVICE-IDENTITY.md).
