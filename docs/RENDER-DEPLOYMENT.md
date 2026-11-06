# Render staging deployment

## Scope

The initial Render environment is a staging deployment for synthetic/test data only. It is not an approved production environment and must not be connected to real household devices or used for real household data.

The application now includes account authentication, MFA, household membership and role-based authorization, device provisioning, HMAC-SHA-256 device request authentication, replay protection, credential rotation/revocation, and signed telemetry ingestion. The staging web service is internet-reachable and remains a test environment only. Use synthetic data, do not connect real electrical equipment, and do not treat cloud telemetry or alerts as protective controls.

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

The application supports opt-in startup migrations through `Database:ApplyMigrationsOnStartup`. The staging Blueprint sets this to `true`, so the energy, identity, and device-security EF Core migration sets are applied before the app begins serving requests. The bootstrapper then ensures the application roles exist and can create the initial administrator and household when the bootstrap variables are supplied.

This is intentionally disabled by default in other environments. Production must use a controlled migration/release process and account for compatibility between old and new application versions.

## Required staging secrets and first administrator

Configure these environment variables on the Render web service before the first authenticated staging session:

- `EMS_BOOTSTRAP_ADMIN_EMAIL`: email address for the initial staging administrator.
- `EMS_BOOTSTRAP_ADMIN_PASSWORD`: initial password (minimum 12 characters, including uppercase, lowercase, digit, and non-alphanumeric character).
- `EMS_BOOTSTRAP_HOUSEHOLD_NAME`: initial household display name.
- `EMS_DEVICE_CREDENTIAL_ENCRYPTION_KEY`: base64-encoded cryptographic key that decodes to exactly 32 bytes. Generate a unique random value and keep it in Render's secret environment settings. Do not commit it to the repository or reuse a test key.

The bootstrapper creates the administrator only when the installation contains no users. Once the administrator and household have been created, remove the bootstrap email/password variables from the service environment and redeploy; do not leave bootstrap credentials configured indefinitely. If no bootstrap account is configured, the login page can load but there will be no initial user able to sign in.

The device encryption key is required when provisioning device credentials. Changing it after credentials have been stored makes previously encrypted device signing keys unreadable, so preserve it across deploys and backups.

## Deployment workflow

The staging Blueprint is linked to `deploy/render-staging`. It is configured to deploy only after the repository's GitHub checks pass. Changes are proposed through pull requests; do not merge or approve infrastructure changes without reviewing the PR and Render Blueprint plan.

## Production prerequisites

Before any production release:

- Complete an independent security review of authentication, MFA, household authorization, device provisioning, HMAC authentication, and credential rotation.
- Complete ESP32 firmware provisioning, secure credential storage, signed publishing, command polling, and offline queue behavior.
- Review and protect all future device-control endpoints; keep time-critical fault handling and protective shutdown local to the device.
- Validate proxy/TLS forwarding, secure cookies, CSRF protections, rate limiting, secret rotation, backups, and recovery under the actual production topology.
- Establish persistent paid PostgreSQL, backups, restore drills, and a data-retention policy.
- Establish a controlled database migration/release procedure.
- Verify logging, monitoring, incident response, and rollback compatibility.
- Complete physical-device end-to-end validation and electrical safety review before connecting ESP32 devices to real equipment.
- Implement durable alert evaluation, notification delivery, historical telemetry charts, and command workflows before treating the dashboard as an operational monitoring system.
