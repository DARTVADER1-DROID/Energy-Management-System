# Web application

## Technology and structure

The web application is hosted by the .NET 10 ASP.NET Core service and uses Razor Pages with server-side rendering. Page models call application-facing read services directly; they do not make HTTP requests back into the same process. Tailwind CSS is compiled from the repository's stylesheet entry point.

## Page inventory

| Route | Purpose | Status |
|---|---|---|
| / | Household dashboard; device inventory summary and latest persisted telemetry aggregates. | Implemented |
| /Devices | Searchable, paginated device inventory. | Implemented |
| /Devices/Register | Device registration wizard and configuration. | Implemented |
| /Devices/{deviceCode} | Device details, thresholds, provisioning and credential lifecycle. | Implemented |
| /Households/Create | Household creation. | Implemented |
| /Households/Users | Household membership, invitations and role administration. | Implemented |
| /Account/Login and account pages | Sign-in, password recovery, invitation acceptance, profile and reauthentication. | Implemented |
| /Alerts | Notification-centre foundation/status view. | Partial; no complete alert evaluation/delivery engine |
| /Settings | Appearance and application settings. | Implemented foundation |

## Asset workflow

From repository root:

```bash
npm install
npm run build
npm run test:frontend
```

Input: src/EnergyManagement.Api/Web/Styles/tailwind.css. Output: src/EnergyManagement.Api/wwwroot/css/site.css. The Dockerfile builds Tailwind in a Node 22 stage and copies the generated output into the .NET publish stage.

## Data and authorization

Razor Pages use server-side identity and household access services. Household and device data must be filtered using server-side membership checks. Do not trust household IDs or device codes supplied by the browser as proof of access.

Dashboard measurements are derived from the latest persisted sample per device. Missing metrics remain unavailable, not zero. Cumulative meter energy is not a daily energy calculation. A telemetry sample is not by itself a verified online status indicator.

## Current limitations

- No SignalR/live push updates.
- No complete historical charting/analytics experience.
- No durable threshold alert evaluation or notification delivery.
- No remote relay/load control UI or backend command channel.
- MFA is disabled for all accounts by the current migration.
