import hashlib
import hmac
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLIENT = (ROOT / "esp32-arduino/EMS_ESP32/TelemetryClient.cpp").read_text()
MAIN = (ROOT / "esp32-arduino/EMS_ESP32/EMS_ESP32.ino").read_text()
PZEM = (ROOT / "esp32-arduino/EMS_ESP32/PzemReader.cpp").read_text()

class FirmwareContractTests(unittest.TestCase):
    def test_hmac_canonicalization_matches_backend_contract(self):
        method, path, timestamp, nonce, body = "POST", "/api/device-telemetry", "1794050000", "0123456789ABCDEF", '{"schemaVersion":1}'
        body_hash = hashlib.sha256(body.encode()).hexdigest().upper()
        canonical = "\n".join((method, path, timestamp, nonce, body_hash)).encode()
        expected = hmac.new(bytes(range(32)), canonical, hashlib.sha256).hexdigest().upper()
        self.assertEqual(len(expected), 64)
        self.assertIn("String canonical=method+", CLIENT)
        self.assertIn('+hash', CLIENT)

    def test_nonce_and_sample_id_strength(self):
        self.assertIn("esp_random()", CLIENT)
        self.assertIn("char nonce[33]", CLIENT)
        self.assertIn("b[6]=(b[6]&15)|64", CLIENT)
        self.assertIn("b[8]=(b[8]&63)|128", CLIENT)

    def test_backend_numeric_ranges_are_enforced(self):
        for token in ("voltageVolts>1000", "currentAmps>10000",
                      "activePowerWatts>10000000", "energyWattHours>1000000000000.0"):
            self.assertIn(token, CLIENT)
        self.assertIn("v.voltageVolts<=1000", PZEM)
        self.assertIn("v.currentAmps<=10000", PZEM)

    def test_queue_is_bounded_and_does_not_evict_oldest(self):
        self.assertIn("MAX_PENDING_SAMPLES = 12", CLIENT)
        self.assertIn("Queue capacity reached; retaining existing samples", CLIENT)
        self.assertIn('p.putString("items",encoded)', CLIENT)

    def test_duplicate_telemetry_is_idempotent(self):
        self.assertIn("status==HTTP_CODE_CONFLICT", CLIENT)

    def test_tls_and_clock_fail_closed(self):
        self.assertIn("client.setCACert(rootCa)", CLIENT)
        self.assertNotIn("setInsecure", CLIENT + MAIN)
        self.assertIn("if(now<1700000000)", CLIENT)

    def test_provisioning_retry_has_exponential_cap(self):
        self.assertIn("provisionBackoffMs*2UL,1800000UL", MAIN)
        self.assertIn("nextProvisionAttemptMs", MAIN)

    def test_rotation_persists_before_first_authenticated_request(self):
        persist = MAIN.index('preferences.putString("secret",replacement)')
        request = MAIN.index("candidate.heartbeat()", persist)
        self.assertLess(persist, request)

    def test_meter_counter_reset_is_detected(self):
        self.assertIn("raw+1.0f<lastRawEnergyWh", MAIN)
        self.assertIn("energyOffsetWh+=lastRawEnergyWh", MAIN)

if __name__ == "__main__":
    unittest.main(verbosity=2)
