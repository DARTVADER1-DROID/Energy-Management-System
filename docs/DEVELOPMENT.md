# Development Workflow

## Working agreements

- Implement one cohesive feature at a time.
- Keep each increment buildable and, once tests exist, testable.
- Prefer clear names and small, focused types and methods.
- Keep business rules out of HTTP endpoints and persistence code.
- Avoid speculative abstractions; introduce interfaces where they establish a meaningful boundary or enable a real alternative/test seam.
- Do not add a dependency without a clear need.
- Update requirements or architecture documentation when a confirmed decision changes.
- Never claim hardware behaviour is validated based only on a software build.

## Suggested branch and commit style

Use focused branches for substantial features and concise commit messages, for example:

- `feat/telemetry-contract`
- `feat/device-registration`
- `fix/telemetry-validation`
- `docs/update-connectivity-policy`

Commit prefixes: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `build:`.

## .NET conventions

- Target .NET 10 for the initial backend.
- Enable nullable reference types and implicit usings.
- Keep compiler warnings visible; treat warnings as errors once the project baseline is clean.
- Prefer built-in platform capabilities before adding packages.
- Add automated tests alongside the first business logic that needs them.

## Firmware conventions

- Keep portable logic separate from ESP32-specific code.
- No cloud/network call may be required for local safety decisions.
- Persist fault-latched state safely and test reboot/recovery scenarios.
- Validate sensor inputs and hardware assumptions.
- Do not use unreviewed sample thresholds for mains protection.

## Safety gate

Do not connect prototype software to mains switching hardware until sensing, isolation, switching ratings, protective coordination, fail-safe behaviour and test procedures have been reviewed by a qualified person.
