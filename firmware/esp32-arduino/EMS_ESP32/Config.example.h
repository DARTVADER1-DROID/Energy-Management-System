#pragma once
// Copy to Config.h and fill locally. NEVER commit Config.h or secrets.
#define WIFI_SSID "YOUR_WIFI_SSID"
#define WIFI_PASSWORD "YOUR_WIFI_PASSWORD"
#define EMS_API_BASE_URL "https://YOUR-EMS-HOST"
#define EMS_DEVICE_CODE "YOUR_REGISTERED_DEVICE_CODE"
#define EMS_PROVISIONING_CODE "YOUR_ONE_TIME_PROVISIONING_CODE"
// Optional: paste the one-time replacement secret shown by the EMS credential-rotation screen.
// The firmware validates it with an authenticated heartbeat before replacing the stored key.
#define EMS_ROTATED_DEVICE_SECRET ""
static const char EMS_ROOT_CA_CERT[] = R"PEM(-----BEGIN CERTIFICATE-----
PASTE_TRUSTED_ROOT_CA_CERTIFICATE_HERE
-----END CERTIFICATE-----
)PEM";
#define PZEM_RX_PIN 16
#define PZEM_TX_PIN 17
#define TELEMETRY_INTERVAL_MS 30000UL
#define HEARTBEAT_INTERVAL_MS 60000UL
#define WIFI_CONNECT_TIMEOUT_MS 15000UL
#define NTP_SYNC_TIMEOUT_MS 20000UL
#define NTP_SERVER_1 "pool.ntp.org"
#define NTP_SERVER_2 "time.google.com"
