# Feature 001 — Household and device registration

## Current status

This original feature is implemented and has since been extended by household authorization and device-security work. The original design notes below are historical context; current route authorization and behavior are defined by [API.md](API.md), [Security model](SECURITY-001-AUTHENTICATION-AND-DEVICE-IDENTITY.md) and the implementation in Modules/Devices.

## Implemented behavior

- Household records with backend-generated household codes.
- Device registration under a household with backend-generated stable device codes.
- Device metadata: name, hardware model, firmware version, installation location, electrical configuration and optional description.
- Single-phase and three-phase configuration values.
- Optional advisory thresholds: minimum/maximum voltage, maximum current, maximum instantaneous power, maximum daily energy, optional maximum temperature and warning persistence duration.
- Device listing, retrieval and deactivation.
- Household-scoped authorization is now enforced by the current application security services; the original feature baseline predated that security implementation.
- Device provisioning-code, credential rotation and credential revocation operations are implemented in the device module.

## Current API

See [API reference](API.md) for the complete current route inventory. The main management routes are under /api/households and /api/devices. Management endpoints require human authentication and apply household authorization. Device provisioning and device-authenticated routes use separate device identity controls.

## Threshold semantics

Threshold configuration is advisory metadata only. Durable threshold evaluation, alert generation and notification delivery are not implemented. Threshold values must not be treated as electrical protection limits and do not trigger relay/contactor disconnection.

## Verification

Domain and PostgreSQL-backed integration coverage is maintained in tests/EnergyManagement.Tests. Current CI status must be checked against the exact commit SHA; historical test counts in old milestone reports do not describe later commits.
