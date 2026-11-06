# Render staging deployment

## Scope

The initial Render environment is a staging deployment for synthetic/test data only. It is not an approved production environment and must not be connected to real household devices or used for real household data.

The current application does not yet implement user authentication, household-level authorization, or device authentication. The staging web service is internet-reachable; do not enter sensitive information or expose real device-control workflows.

## Infrastructure

The root `render.yaml` defines:

- One Docker-based ASP.NET Core .NET 10 web service: `energy-management-staging`.
- One Render PostgreSQL database: `energy-management-staging-db`.
- Singapore region for both resources.
- Free compute plans for temporary staging.
- Render's internal database connection string, supplied as `ConnectionStrings__EnergyManagement`.
- HTTP readiness checks at `/health/ready`.
- Automatic deployments only after the linked GitHub checks pass.
- A single web instance.

Render's free PostgreSQL plan has 1 GB of storage and expires after 30 days. Treat the database as disposable. Do not store data that must be retained; export any test data you need before expiry. See [Render's Postgres plan documentation](https://render.com/docs/postgresql-refresh).

The free web service may spin down when idle. First requests after inactivity can be delayed.

## Container build

The Dockerfile:

1. Builds Tailwind CSS using Node.js 22.
2. Publishes the ASP.NET Core application with the .NET 10 SDK.
3. Runs the published app on the .NET 10 ASP.NET runtime.
4. Listens on port 10000.

The application reads its database connection from `ConnectionStrings__EnergyManagement`, which ASP.NET Core maps to `ConnectionStrings:EnergyManagement`.

## Database migrations

The application supports opt-in startup migrations through `Database:ApplyMigrationsOnStartup`. The staging Blueprint sets this to `true`, so the existing EF Core migrations are applied before the app begins serving requests.

This is intentionally disabled by default in other environments. A production rollout must use a controlled migration process and account for compatibility between old and new application versions.

## Deployment workflow

The staging Blueprint is linked to `deploy/render-staging`. It is configured to deploy only after the repository's GitHub checks pass. Changes are proposed through pull requests; do not merge or approve infrastructure changes without reviewing the PR and Render Blueprint plan.

## Production prerequisites

Before any production release:

- Implement authentication and household-level authorization.
- Implement device authentication, provisioning, and credential rotation.
- Review and protect all device registration and control endpoints.
- Add request rate limiting, secure cookie/CSRF protections where applicable, and security-focused tests.
- Establish persistent paid PostgreSQL, backups, restore drills, and a data-retention policy.
- Establish a controlled database migration/release procedure.
- Verify logging, monitoring, incident response, and rollback compatibility.
- Complete a security review before connecting physical ESP32 devices.
