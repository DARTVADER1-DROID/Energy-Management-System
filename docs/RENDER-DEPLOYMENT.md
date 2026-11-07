# Render staging deployment

## Purpose and status

The root render.yaml defines a temporary staging environment for synthetic/test data. It is not production infrastructure. Do not connect real household electrical equipment or store important personal/operational data in this environment.

## Blueprint resources

- Docker-based ASP.NET Core .NET 10 web service: energy-management-staging.
- PostgreSQL database: energy-management-staging-db.
- Singapore region; free plans; one web instance.
- Internal database connection mapped to ConnectionStrings__EnergyManagement.
- Readiness path: /health/ready.
- Service tracks deploy/render-staging and deploys after linked checks pass.

Free services can spin down, and the free database is disposable/limited-duration. Confirm current Render plan limits in the Render dashboard before relying on them.

## Container build

The Dockerfile:
1. Builds Tailwind CSS using Node 22.
2. Restores and publishes the .NET 10 application.
3. Runs the app on the .NET 10 ASP.NET runtime as a non-root application user.
4. Listens on port 10000.

## Environment configuration

The Blueprint sets ASPNETCORE_ENVIRONMENT=Production, ASPNETCORE_URLS=http://0.0.0.0:10000 and Database__ApplyMigrationsOnStartup=true for this staging environment.

Initial administrator bootstrap variables:
- EMS_BOOTSTRAP_ADMIN_EMAIL
- EMS_BOOTSTRAP_ADMIN_PASSWORD
- EMS_BOOTSTRAP_HOUSEHOLD_NAME

The bootstrap password must meet the application's configured Identity policy. Remove bootstrap email/password values after the initial administrator has been created. Do not leave bootstrap credentials configured indefinitely.

Device credential encryption:
- EMS_DEVICE_CREDENTIAL_ENCRYPTION_KEY must be a base64 value decoding to exactly 32 bytes.
- Generate a unique cryptographically random key and store it only as a secret environment variable.
- Preserve it securely across deploys and backups. Replacing it without a supported re-encryption/rotation process can make stored device credentials unreadable.

Email invitations and password reset require the configured SMTP settings and the public HTTPS base URL EMS_PUBLIC_BASE_URL. Check the current service configuration and application options before assuming email delivery is active.

## Database migrations

Startup migration is enabled in this staging Blueprint. The application applies its EF Core migration sets before serving. This convenience is staging-only. Production should use a controlled migration/release procedure, backup and restore plan, and compatibility review.

## Deployment checklist

1. Review the Blueprint diff and service/database plan.
2. Confirm required secrets are configured in Render, never in Git.
3. Confirm the database connection is the Render internal connection string.
4. Confirm the service's public URL, HTTPS termination and forwarded-header behavior.
5. Confirm /health/ready passes and /health/live responds.
6. Verify bootstrap account creation and remove bootstrap secrets.
7. Verify SMTP delivery if invitations/password reset are in scope.
8. Inspect logs for startup/migration errors without exposing secrets.
9. Verify the deployed commit SHA and CI checks correspond to the intended release.

## Production prerequisites

- Independent application and device-security review.
- Verified TLS/proxy/cookie/CSRF/rate-limit configuration.
- Durable PostgreSQL plan, backups, restore drills and retention policy.
- Controlled migrations, monitoring, alerting, incident response and rollback.
- Secret rotation and device credential lifecycle runbooks.
- Firmware provisioning, signed publish, queue/recovery and physical-device testing.
- Hardware calibration, environmental/soak testing and qualified electrical sign-off.
- Implement durable alert evaluation, historical analytics and notification delivery if these are required operationally.

A successful Render deploy or health check is not proof of production readiness or electrical safety.
