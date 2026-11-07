# Documentation index

Start with the root [README](../README.md), then use this index to navigate the current documentation set.

## Project overview
- [Feature catalogue](FEATURES.md) — implemented, partial, planned and explicitly excluded functionality.
- [Requirements](../REQUIREMENTS.md) — product direction and release acceptance.
- [Changelog](CHANGELOG.md) — milestone history.

## Engineering
- [Architecture](ARCHITECTURE.md) — component boundaries and runtime data paths.
- [API reference](API.md) — routes, authentication boundaries and response behavior.
- [Data model](DATA-MODEL.md) — entities, persistence contexts and migration rules.
- [Telemetry contract](TELEMETRY-001-INGESTION.md) — schema-v1 envelope and ingest/query semantics.
- [Security model](SECURITY-001-AUTHENTICATION-AND-DEVICE-IDENTITY.md) — human and device identity lifecycles.

## Build and operations
- [Development guide](DEVELOPMENT.md) — local setup and engineering conventions.
- [Testing guide](TESTING.md) — test layers, commands and CI interpretation.
- [Web frontend](WEB-FRONTEND.md) — Razor Pages and asset pipeline.
- [Render staging](RENDER-DEPLOYMENT.md) — current staging deployment model.
- [Diagrams](diagrams/README.md) — Mermaid component, sequence and flow diagrams.

## Hardware
- [ESP32 firmware guide](../firmware/esp32-arduino/README.md)
- [USB bench diagnostic](../firmware/esp32-arduino/ESP32_Bench_Diagnostic/README.md)
- [Hardware commissioning](../firmware/esp32-arduino/HARDWARE_COMMISSIONING.md)
- [Production acceptance](../firmware/esp32-arduino/PRODUCTION_ACCEPTANCE.md)

## Documentation maintenance

Update the feature catalogue when implementation status changes. Update API, data model and security docs when contracts or persistence behavior change. Update diagrams when component interactions change. Distinguish CI validation from physical hardware acceptance, and remove obsolete statements rather than appending contradictory status notes.
