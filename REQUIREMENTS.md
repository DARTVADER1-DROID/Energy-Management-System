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

The initial monitoring scope is basic whole-house measurement:

- Voltage (V)
- Current (A)
- Instantaneous power (W)
- Accumulated energy (kWh)

Future expansion may include power factor, frequency, power quality, estimated cost, room-level monitoring and appliance-level monitoring.

Both single-phase and three-phase supply configurations must be accommodated by the product model. Implement and validate single-phase hardware support first; design the measurement model so phase-specific values can be represented.

## 4. Edge hardware and firmware

- Microcontroller firmware is part of this project and must be developed alongside the backend.
- ESP32 is the first supported platform.
- Firmware should be structured so additional microcontrollers can be supported later without rewriting the application logic.
- Keep hardware-specific sensor and switching drivers behind clear interfaces where useful.
- The ESP32 communicates over Wi-Fi and reaches the cloud backend over the internet.
- No separate home gateway is required for the initial deployment.
- Firmware must continue local monitoring and local protective behaviour when Wi-Fi, internet or cloud services are unavailable.

### 4.1 Sampling

- Sensor acquisition interval is configurable.
- Initial acquisition default: 1 second.
- Acquisition, safety evaluation, local persistence and cloud transmission use separate timing controls.
- Cloud transmission may aggregate or batch readings; it must not determine the cadence of local safety evaluation.
- Configured intervals must be validated against actual sensor and hardware capabilities.

## 5. Local safety and protection

- Firmware owns time-critical electrical safety decisions. Cloud or web application availability must never be a prerequisite for local protection.
- Firmware must validate sensor readings, detect configured abnormal electrical conditions, initiate supported local protective actions and record fault events.
- The system must provide monitoring and alerting as well as automatic disconnection where suitable, independently validated switching hardware exists.
- A protective shutdown enters a latched fault state.
- Recovery from a protective shutdown requires manual reset. Firmware must verify that the triggering condition has cleared before allowing a reset/reconnection.
- Remote commands must never override an active local safety lockout.
- Software protection supplements, and does not replace, correctly selected and installed breakers, fuses, RCD/RCCB/RCBO devices, contactors and other required electrical protections.
- Threshold values, response times, sensor accuracy, contactor ratings and the safe reset procedure are open engineering decisions requiring hardware-specific analysis and qualified electrical review.

## 6. Connectivity-loss policy per equipment

- Users can configure a separate connectivity-loss policy for each controllable equipment item.
- A policy is explicitly enabled per item; it is disabled by default.
- Each enabled policy has a user-configured timeout.
- When cloud/backend connectivity is lost, firmware starts the applicable local countdown.
- If verified backend connectivity is restored before expiry, the pending connectivity-loss action is cancelled.
- If connectivity is not restored before expiry, firmware disconnects the equipment covered by that policy and records the event locally.
- The watchdog and countdown operate locally and must survive a device restart without being extended or bypassed by rebooting.
- Wi-Fi association alone does not prove cloud connectivity; recovery must verify reachability of the configured backend.
- The policy must be independently configurable per load, including its classification and timeout.
- Essential services and equipment whose shutdown could create a hazard must be excluded unless a documented safety assessment explicitly establishes a safe action. This policy is not a substitute for electrical fault protection.
- The exact meaning of connectivity loss (internet, backend endpoint, or both), timeout bounds, policy configuration authority and reset flow remain to be specified in the detailed design.

## 7. Equipment control and automation

The product must support both main-supply control and independently controlled loads, introduced incrementally.

- Main supply control uses appropriately rated switching hardware, such as a correctly selected contactor.
- Individual loads/circuits can have independent states and control policies.
- Manual ON/OFF control is available through the web dashboard.
- The system reports the observed equipment state; it must not confuse a requested state with confirmed physical state.
- Commands are validated and recorded.
- Automation is supported as a later increment: time schedules and configurable rule-based actions.
- Local safety interlocks always take precedence over manual commands, schedules and automation.
- The first hardware implementation and exact controllable-load count are open decisions.

## 8. Web application and notifications

- Web-first product, accessible from desktop, tablet and mobile browsers.
- Server-side rendering (SSR) capability is required.
- Frontend framework and SSR hosting approach are not yet selected.
- Initial notifications are shown in the web dashboard only.
- Dashboard notifications include faults, connectivity loss, equipment shutdown/recovery and relevant system events.
- Notification history and read/unread state are required.
- Keep notification creation separate from delivery channels so email and mobile push can be added later.

## 9. Deployment and data flow

- Use a hybrid edge/cloud model, but do not require a separate local home server or gateway initially.
- ESP32 performs local acquisition, safety evaluation and buffering.
- ESP32 sends data directly to the cloud .NET backend over Wi-Fi/internet.
- Cloud services provide remote access, long-term storage, analytics, reporting and dashboard data.
- During outages, the ESP32 continues local operation and buffers records; synchronisation resumes when connectivity returns.
- A future local server/gateway may be considered, but it is not part of the initial deployment.

## 10. Device storage and synchronisation

- ESP32 internal flash using a suitable filesystem (initial candidate: LittleFS) stores configuration, device state, critical events and a bounded offline queue.
- Optional microSD storage is supported as a future/optional capacity expansion.
- Storage access must be abstracted so internal flash and microSD implementations can be changed without altering monitoring or safety logic.
- Cloud database is the long-term source for historical telemetry and analytics.
- Synchronisation must handle acknowledgements, retries, duplicate delivery and recovery after restart.
- Flash write endurance and bounded storage capacity must be considered; high-frequency samples must not be written indiscriminately.
- SQLite is an option only if a future local computer/server/gateway is introduced and needs a local relational database. SQLite is not the initial ESP32 storage engine.

## 11. Users and access control

- Support multiple users associated with a household installation.
- Use role-based access control.
- Initial roles:
  - **Administrator:** manage installation, users, devices and configuration.
  - **Household Member:** view dashboards, energy usage and reports.
  - **Read-only Viewer:** view permitted information without changing settings.
- Exact permissions and account onboarding flows are to be detailed before implementation.

## 12. Device registration and security

- Initial device registration is manual through the web application.
- Each device has a unique device identifier and is associated with a household installation.
- Show device connection status and last communication time.
- Automatic discovery is future scope.
- Device authentication is intentionally not implemented in the initial version, but the communication boundary must leave room for it.
- HTTPS should be used for cloud transport where supported.
- **Security gate:** an unauthenticated device endpoint must not be exposed to the public internet or used in production. Device authentication, secure provisioning, credential lifecycle, authorization and threat modelling must be completed before production connectivity is enabled.

## 13. Analytics and billing

Initial analytics:

- Current power consumption.
- Daily and monthly energy totals.
- Historical consumption graphs.
- User-selected date ranges.
- Comparison between selected periods.

Future analytics may include peak identification, trend analysis and energy-saving recommendations.

Initial billing:

- User-configurable flat tariff per kWh.
- Estimated daily and monthly electricity cost.
- Keep tariff calculation separate from telemetry and analytics.
- Future scope: tariff slabs, fixed charges, taxes, time-of-use pricing and configurable billing cycles.
- Currency, tariff effective dates and billing-period rules remain open decisions.

## 14. Reporting

- Initial version: interactive dashboard and on-screen reports only.
- Next version: downloadable PDF and Excel reports.
- Reporting/export implementation must reuse domain calculations rather than duplicate them.

## 15. Backend architecture and technology

- Backend language/platform: C# and .NET.
- Architecture: **Modular Monolith**.
- One deployable backend application, internally divided into business modules with explicit boundaries.
- Do not introduce microservices in the initial design.
- Primary cloud relational database: PostgreSQL.
- Entity Framework Core is the intended .NET persistence technology.
- SQLite may be used if a future local server/gateway deployment explicitly requires local relational storage.
- .NET target version for the initial scaffold: .NET 10.
- Module boundaries should allow independent evolution while keeping deployment and operations simple.

## 16. Incremental delivery direction

Suggested order; detailed acceptance criteria will be agreed before each milestone:

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
12. Role/permission hardening, security and production readiness.
13. PDF/Excel export in a subsequent version.

The exact order may change when hardware dependencies and safety validation are understood.

## 17. Open decisions

The following are deliberately not fixed yet:

- Electrical sensing module, sensor accuracy and calibration approach.
- ESP32 board variant, storage sizing and microSD hardware.
- Switching topology, relay/contactor selection and feedback sensing.
- Electrical protection thresholds and response-time requirements.
- Device-to-cloud application protocol and telemetry schema.
- Frontend framework and SSR implementation.
- Cloud hosting provider, deployment topology and operating cost.
- Device authentication and secure provisioning design.
- User identity provider and exact RBAC permission matrix.
- Retention period, sampling/transmission defaults and offline queue capacity.
- Currency, tariff effective dates and billing-cycle rules.
- Exact connectivity watchdog semantics, allowed timeout range and safe equipment classification.
- Test hardware, simulation strategy and acceptance criteria for each release.

## 18. Safety and release constraint

No software in this repository should be treated as a certified or validated electrical protection product. Mains-connected development and field testing require appropriate isolation, correctly rated protective hardware, fail-safe design, documented test procedures and qualified electrical review. A software milestone is not considered field-ready merely because it builds or passes unit tests.
