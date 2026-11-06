# Server-rendered web frontend

The web experience is hosted by the existing ASP.NET Core application and uses Razor Pages. Page models call application-facing read services directly; they do not make HTTP requests back into the same application.

## Frontend assets

Tailwind CSS v4 is built from the repository root:

```bash
npm install
npm run build
dotnet run --project src/EnergyManagement.Api/EnergyManagement.Api.csproj
```

The generated stylesheet is written to `src/EnergyManagement.Api/Web/wwwroot/css/site.css`. The checked-in stylesheet is a small fallback; run the Tailwind build for the complete utility stylesheet.

## Initial pages

- `/` — server-rendered household dashboard.
- `/Devices` — searchable, paginated server-rendered device inventory.
- `/Alerts` — notification-centre foundation and implementation status.
- `/Settings` — appearance preference controls.

The dashboard deliberately displays telemetry as unavailable until ingestion is implemented. A device with a previous check-in is not automatically represented as currently online: an explicit heartbeat/timeout contract is required first.

## Current boundaries

The existing device registration API remains the source of truth. The first frontend increment focuses on the SSR shell, household context, dashboard summary and device inventory. The registration wizard, device operational console, persisted notifications and SignalR event delivery are subsequent increments.

SignalR delivery must be connected only after household-level authorization is available. The current API intentionally has no authentication or household authorization, so broadcasting household data to browser connections now would create a cross-household data exposure risk.
