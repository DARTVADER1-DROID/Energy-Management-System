# Product Requirements

**Project:** Residential Energy Management System  
**Status:** Initial requirements baseline  
**Development approach:** Incremental, independently testable milestones

This document records requirements explicitly confirmed during discovery. Items marked as future scope are not commitments for the first release. Unresolved engineering details remain open rather than being silently assumed.

## 1. Product scope and principles

1. Build a complete residential energy management platform over multiple increments.
2. Keep the codebase highly organised, readable, modular and decoupled.
3. Implement and validate features incrementally. Each milestone should leave the repository in a buildable, testable state.
4. Keep business logic independent from hardware drivers, persistence providers, transport protocols and presentation concerns where practical.
5. Design extension points for future capabilities without implementing speculative abstractions prematurely.

## 2. Initial target environment

- Initial target: residential homes and household electrical installations.
- The initial product focuses on whole-house monitoring.
- Commercial and industrial support are possible future extensions, not initial requirements.

## 3. Electrical monitoring

The initial monitoring scope is basic whole-house measurement: voltage (V), current (A), instantaneous power (W), and accumulated energy (kWh).
Both single-phase and three-phase supply configurations must be accommodated by the product model. Implement and validate single-phase hardware support first.

## 4. Edge hardware and firmware

- ESP32 is the first supported platform.
- Firmware owns local monitoring, buffering and time-critical safety decisions.
- Wi-Fi and direct cloud communication; no mandatory gateway initially.
- Sensor acquisition initially defaults to one second, independently of safety evaluation, persistence and cloud transmission timing.

## 5. Local safety and protection

- Local firmware protection is authoritative and independent of cloud availability.
- Protective shutdowns latch; manual reset is allowed only after the triggering condition clears.
- Remote commands cannot override local safety lockouts.
- Software protection supplements certified, correctly installed electrical protection; it does not replace it.
- Electrical protection thresholds and response times require hardware-specific analysis and qualified review.

## 6. Connectivity-loss policy per equipment

- Connectivity-loss policy is independently configurable per controllable equipment item and disabled by default.
- An enabled policy has a user-configured timeout.
- Firmware starts the countdown locally after verified backend connectivity is lost; Wi-Fi association alone is insufficient proof of cloud reachability.
- Verified backend recovery before expiry cancels the pending action; otherwise firmware disconnects the covered equipment and records the event locally.
- The countdown must survive restart without reboot extending or bypassing it.
- Essential or hazardous-to-shutdown equipment is excluded unless a documented safety assessment approves the action.

## 7. Equipment control and automation

- Support main-supply and independent load control incrementally.
- Support dashboard manual control first; schedules and rules are later increments.
- Requested state and observed physical state must remain distinct.
- Local safety interlocks always take precedence.

## 8. Web application and notifications

- Responsive web application with server-side rendering is required.
- Initial notifications appear in the dashboard, with history and read/unread state.
- Keep notification generation separate from delivery channels for future email/mobile push support.

## 9. Deployment and data flow

- Hybrid edge/cloud model; ESP32 operates locally and synchronises with the cloud when available.
- PostgreSQL is the cloud database; EF Core is the intended persistence technology.
- ESP32 uses LittleFS initially with a storage abstraction and optional microSD future support.
- SQLite is only a future option for a local computer/server/gateway, not ESP32 storage.

## 10. Users and access control

- Multiple household users and role-based access control.
- Initial roles: Administrator, Household Member, Read-only Viewer.
- Detailed permission matrix and onboarding remain open.

## 11. Device registration and identity

- Initial registration is manual.
- Household IDs are backend-generated sequential identifiers: `H001`, `H002`, etc.
- Device IDs are backend-generated sequential identifiers within a household: `H001-DEV001`, `H001-DEV002`, etc.
- Device identifiers are globally unique, immutable and never reused after deactivation.
- Only the Household Administrator may register, edit or deactivate devices.
- Registration metadata: device name, hardware model, firmware version, installation location, electrical configuration (single-phase or three-phase), and optional description.
- Store active/deactivated state and last communication time.
- Automatic discovery is future scope.

### 11.1 Device warning thresholds

- Initial configurable limits: minimum and maximum voltage, maximum current, maximum instantaneous power, maximum daily energy, and optional maximum temperature.
- Use one threshold level per configured limit; do not introduce separate warning and critical levels in this increment.
- A warning is generated only when a configured limit remains violated for a user-configured duration.
- Threshold values are warning/notification settings only. They are not electrical protection limits and do not initiate equipment disconnection.
- No universal electrical defaults are assumed; values must be configured for the installation and equipment.

### 11.2 Device security

- Device authentication is deferred initially, with an extension point reserved.
- HTTPS should be used where supported.
- Unauthenticated device or administration endpoints must not be exposed to the public internet or used in production. Authentication, secure provisioning, authorization and threat modelling are production prerequisites.

## 12. Analytics and billing

Initial analytics: current power, daily/monthly energy, historical graphs, custom date ranges and period comparisons.
Initial billing: configurable flat tariff per kWh and estimated daily/monthly cost. Advanced tariffs are future scope.

## 13. Reporting

- Initial version: dashboard and on-screen reports.
- PDF and Excel exports are a later increment.

## 14. Backend architecture and technology

- C# / .NET 10 backend, modular monolith, one deployable application.
- PostgreSQL cloud database and EF Core.
- No microservices initially.

## 15. Incremental delivery direction

1. Repository and application foundation.
2. Device identity/registration model and telemetry contract.
3. Telemetry ingestion and validation.
4. ESP32 sensor acquisition and local buffering.
5. Cloud synchronisation and historical persistence.
6. Monitoring dashboard and SSR frontend.
7. Local protection hardware integration and fault-event flow.
8. Manual load control with interlocks.
9. Per-equipment connectivity-loss policies.
10. Notifications, analytics and basic bill estimation.
11. Scheduling and rule-based automation.
12. Security and production readiness.
13. PDF/Excel export in a subsequent version.

## 16. Open engineering decisions

Sensor modules/calibration, ESP32 board/storage sizing, switching topology, protection limits, telemetry protocol/schema, frontend framework, cloud hosting, authentication/provisioning, exact role permissions, retention, sampling/transmission defaults, tariff rules, connectivity timeout bounds, test hardware and feature acceptance criteria remain to be decided.

## 17. Safety and release constraint

No software in this repository is a certified electrical protection product. Mains-connected development and field testing require isolation, correctly rated protective hardware, fail-safe design, documented test procedures and qualified electrical review.
