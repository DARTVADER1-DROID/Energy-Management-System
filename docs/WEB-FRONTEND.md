# Server-rendered web frontend

The web experience is hosted by the existing ASP.NET Core application and uses Razor Pages. Page models call application-facing read services directly; they do not make HTTP requests back into the same application.

## Frontend assets

Build Tailwind CSS from the repository root:

```bash
npm install
npm run build
dotnet run --project src/EnergyManagement.Api/EnergyManagement.Api.csproj
```

The generated stylesheet is written to `src/EnergyManagement.Api/wwwroot/css/site.css`. The checked-in stylesheet is a small fallback; run the Tailwind build for the complete utility stylesheet.

## Initial pages

- `/` — server-rendered household dashboard.
- `/Devices` — searchable, paginated server-rendered device inventory.
- `/Devices/Register` — four-step device registration wizard.
- `/Devices/{deviceCode}` — operational device console.
- `/Alerts` — notification-centre foundation and implementation status.
- `/Settings` — appearance preference controls.

The dashboard deliberately displays telemetry as unavailable until ingestion is implemented. A device with a previous check-in is not automatically represented as currently online: an explicit heartbeat/timeout contract is required first.

## Current boundaries

The existing device registration service remains the source of truth. Razor Pages use a dedicated read service for queries. The REST API remains available for external clients.

SignalR delivery is intentionally not enabled yet. Household broadcasts must be connected only after household-level authorization exists. The current API intentionally has no authentication or household authorization, so unauthenticated household subscriptions would create a cross-household data exposure risk.
