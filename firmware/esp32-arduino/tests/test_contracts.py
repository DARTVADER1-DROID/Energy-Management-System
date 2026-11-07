import hashlib
import hmac
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLIENT = (ROOT / "esp32-arduino/EMS_ESP32/TelemetryClient.cpp").read_text()
MAIN = (ROOT / "esp32-arduino/EMS_ESP32/EMS_ESP32.ino").read_text()
PZEM = (ROOT / "esp32-arduino/EMS_ESP32/PzemReader.cpp").read_text()
CORE = (ROOT / "esp32-arduino/EMS_ESP32/FirmwareCore.h").read_text()
API = (ROOT / "../src/EnergyManagement.Api/Modules/Telemetry/TelemetryService.cs").read_text()
AUTH = (ROOT / "../src/EnergyManagement.Api/Modules/Devices/DeviceAuthenticationService.cs").read_text()
CONFIG = (ROOT / "esp32-arduino/EMS_ESP32/Config.example.h").read_text()


class FirmwareContractTests(unittest.TestCase):
    def test_hmac_canonicalization_known_vector(self):
        method, path, timestamp, nonce, body = (
            "POST", "/api/device-telemetry", "1794050000",
            "0123456789ABCDEF", '{"schemaVersion":1}'
        )
        body_hash = hashlib.sha256(body.encode()).hexdigest().upper()
        canonical = "\n".join((method, path, timestamp, nonce, body_hash)).encode()
        actual = hmac.new(bytes(range(32)), canonical, hashlib.sha256).hexdigest().upper()
        self.assertEqual(actual, "70182E9D2ED0457A0003E5B438521FD9AC9DB1A3453227C9BDDE47C5FF85CFB3")
        self.assertIn(r'String canonical=method+"\n"+path+"\n"+timestamp+"\n"+nonce+"\n"+hash', CLIENT)
        self.assertIn("mbedtls_md_hmac", CLIENT)

    def test_nonce_and_sample_id_strength(self):
        self.assertIn("esp_random()", CLIENT)
        self.assertIn("char nonce[33]", CLIENT)
        self.assertIn("b[6]=(b[6]&15)|64", CLIENT)
        self.assertIn("b[8]=(b[8]&63)|128", CLIENT)

    def test_measurement_ranges_and_non_finite_values(self):
        self.assertIn("voltage >= 0.0 && voltage <= 1000.0", CORE)
        self.assertIn("current >= 0.0 && current <= 10000.0", CORE)
        self.assertIn("power >= -10000000.0 && power <= 10000000.0", CORE)
        self.assertIn("std::isfinite(energy)", CORE)
        self.assertIn("ems::validMeasurement", PZEM)

    def test_queue_is_bounded_and_preserves_oldest(self):
        self.assertIn("kMaxPendingSamples = 8", CORE)
        self.assertIn("ems::canEnqueue", CLIENT)
        self.assertIn("Queue capacity reached; retaining existing samples", CLIENT)
        self.assertIn('p.putString("items",encoded)', CLIENT)
        self.assertIn("Permanently rejected HTTP 400", CLIENT)
        self.assertIn("flushPending", MAIN)

    def test_duplicate_and_http_retry_classification(self):
        self.assertIn("HTTP_CODE_CONFLICT", CLIENT)
        self.assertIn("ems::classifyHttpStatus", CLIENT)
        self.assertIn("status == 409", CORE)
        self.assertIn("status == 400", CORE)

    def test_tls_and_clock_fail_closed(self):
        self.assertIn("isHttpsBaseUrl(EMS_API_BASE_URL)", MAIN)
        self.assertIn("isHttpsBaseUrl(baseUrl.c_str())", CLIENT)
        self.assertIn("client.setCACert(rootCa)", CLIENT)
        self.assertNotIn("setInsecure", CLIENT + MAIN)
        self.assertIn("if(now<1700000000)", CLIENT)
        self.assertIn("settimeofday(&resetTime,nullptr)", MAIN)

    def test_provisioning_retry_has_exponential_cap(self):
        self.assertIn("ems::nextProvisionBackoff(provisionBackoffMs)", MAIN)
        self.assertIn("currentMs * 2U", CORE)
        self.assertIn("nextProvisionAttemptMs", MAIN)

    def test_rotation_persists_before_first_authenticated_request(self):
        persist = MAIN.index('preferences.putString("secret",replacement)')
        request = MAIN.index("candidate.heartbeat()", persist)
        self.assertLess(persist, request)
        self.assertIn("Backend rejected replacement but NVS rollback failed", MAIN)

    def test_shared_production_core_is_used_by_firmware(self):
        self.assertIn("ems::validMeasurement", PZEM)
        self.assertIn("ems::normalizeEnergy", MAIN)
        self.assertIn("ems::classifyHttpStatus", CLIENT)
        self.assertIn("ems::canEnqueue", CLIENT)
        self.assertIn("ems::nextProvisionBackoff", MAIN)

    def test_backend_telemetry_contract_matches_firmware(self):
        self.assertIn('"/api/device-telemetry"', CLIENT)
        self.assertIn('doc["schemaVersion"]=1', CLIENT)
        for field in ("sampleId", "capturedAtUtc", "measurements", "voltageVolts",
                      "currentAmps", "activePowerWatts", "energyWattHours",
                      "frequencyHertz", "powerFactor"):
            self.assertIn(field, CLIENT)
        self.assertIn("AddDays(-7)", API)
        self.assertIn("AddMinutes(2)", API)
        self.assertIn("Only telemetry schema version 1", API)

    def test_backend_authentication_contract_matches_firmware(self):
        self.assertIn("/api/device-auth/provision", MAIN)
        self.assertIn("/api/device-auth/heartbeat", CLIENT)
        for header in ("X-EMS-Device", "X-EMS-Timestamp", "X-EMS-Nonce", "X-EMS-Signature"):
            self.assertIn(header, CLIENT)
        self.assertIn("MaximumRequestAge", AUTH)
        self.assertIn("NonceRetention", AUTH)
        self.assertIn("FixedTimeEquals", AUTH)

    def test_provisioning_recovery_is_time_bounded_and_revocable(self):
        self.assertIn("code.UsedAtUtc.HasValue", AUTH)
        self.assertIn("item.ExpiresAtUtc > now", AUTH)
        self.assertIn("DeviceProvisioningRecovered", AUTH)
        self.assertIn("item.RevokedAtUtc == null", AUTH)

    def test_secrets_are_not_committed_in_example_configuration(self):
        self.assertIn("YOUR_WIFI_PASSWORD", CONFIG)
        self.assertIn("YOUR_ONE_TIME_PROVISIONING_CODE", CONFIG)
        self.assertIn("EMS_ROTATED_DEVICE_SECRET", CONFIG)
        self.assertNotIn("setInsecure", CONFIG)

    def test_firmware_remains_monitoring_only(self):
        self.assertNotIn("digitalWrite(", MAIN)
        self.assertNotIn("pinMode(", MAIN)
        self.assertNotIn("relay", MAIN.lower())

    def test_energy_reset_detection_and_monotonicity(self):
        self.assertIn("rawWh + 1.0 < state.lastRawWh", CORE)
        self.assertIn("state.offsetWh += state.lastRawWh", CORE)
        self.assertIn("state.lastRawWh + state.offsetWh", CORE)


if __name__ == "__main__":
    unittest.main(verbosity=2)
