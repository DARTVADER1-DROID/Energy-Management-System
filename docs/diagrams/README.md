# Architecture and flow diagrams

The diagrams in this folder are maintained as Mermaid source so they can be reviewed in GitHub and edited alongside implementation.

| Diagram | Purpose |
|---|---|
| [System context and components](system-context.mmd) | UML-style component/context view of ESP32, API, PostgreSQL and Razor Pages. |
| [Provisioning sequence](device-provisioning-sequence.mmd) | Sequence diagram for administrator-issued provisioning and ESP32 credential exchange. |
| [Telemetry sequence](telemetry-ingestion-sequence.mmd) | Signed publish, validation, de-duplication and persistence sequence. |
| [Telemetry flow](telemetry-flow.mmd) | End-to-end firmware sampling and offline retry decision flow. |
| [Household invitation flow](household-invitation-flow.mmd) | Administrator invitation and user acceptance flow. |

Keep diagrams synchronized with code. They describe the current implemented path, not planned relay control, alerts, automation or other future features.
