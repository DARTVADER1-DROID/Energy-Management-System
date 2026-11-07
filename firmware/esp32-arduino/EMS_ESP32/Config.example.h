#pragma once
// Copy to Config.h and fill locally. NEVER commit Config.h or secrets.
#define WIFI_SSID "YOUR_WIFI_SSID"
#define WIFI_PASSWORD "YOUR_WIFI_PASSWORD"
#define EMS_API_BASE_URL "https://YOUR-EMS-HOST"
#define EMS_DEVICE_CODE "YOUR_REGISTERED_DEVICE_CODE"
// One-time code from the device console; expires after 10 minutes.
#define EMS_PROVISIONING_CODE "YOUR_ONE_TIME_PROVISIONING_CODE"
// Paste the trusted root CA PEM for your EMS HTTPS hostname. No insecure TLS mode.
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
