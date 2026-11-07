#include "../EMS_ESP32/FirmwareCore.h"
#include <cassert>
#include <cmath>
#include <cstdint>
#include <limits>
#include <iostream>

int main() {
    using namespace ems;

    // Measurement boundaries and invalid numeric values.
    assert(validMeasurement(0, 0, 0, 0, 50, 1));
    assert(validMeasurement(1000, 10000, 10000000, 1.0e12, 1000, -1));
    assert(!validMeasurement(-0.01, 1, 1, 1, 50, 1));
    assert(!validMeasurement(1000.01, 1, 1, 1, 50, 1));
    assert(!validMeasurement(230, -0.01, 1, 1, 50, 1));
    assert(!validMeasurement(230, 1, std::numeric_limits<double>::infinity(), 1, 50, 1));
    assert(!validMeasurement(230, 1, 1, -1, 50, 1));
    assert(!validMeasurement(230, 1, 1, 1, 1000.1, 1));
    assert(!validMeasurement(230, 1, 1, 1, 50, 1.01));
    // Optional PZEM fields may be unavailable (NaN), but required fields may not.
    assert(validMeasurement(230, 1, 200, 300, std::numeric_limits<double>::quiet_NaN(),
                            std::numeric_limits<double>::quiet_NaN()));
    assert(!validMeasurement(std::numeric_limits<double>::quiet_NaN(), 1, 1, 1, 50, 1));

    // Energy counter: monotonic readings, reset, small jitter, invalid samples.
    EnergyCounterState state;
    double total = 0;
    assert(!normalizeEnergy(100.0, state, total) && total == 100.0);
    assert(!normalizeEnergy(101.0, state, total) && total == 101.0);
    assert(!normalizeEnergy(100.5, state, total) && total == 100.5); // within 1 Wh tolerance
    assert(normalizeEnergy(2.0, state, total) && total == 103.5);
    assert(!normalizeEnergy(3.0, state, total) && total == 104.5);
    const double prior = total;
    assert(!normalizeEnergy(-1.0, state, total) && total == prior);

    // Backoff boundaries and overflow protection.
    assert(nextProvisionBackoff(0) == 30000U);
    assert(nextProvisionBackoff(30000U) == 60000U);
    assert(nextProvisionBackoff(900000U) == 1800000U);
    assert(nextProvisionBackoff(1800000U) == 1800000U);
    assert(nextProvisionBackoff(UINT32_MAX) == 1800000U);

    // HTTP semantics: duplicate telemetry is idempotent; bad payload is dropped;
    // auth, rate-limit, server and transport errors remain retryable.
    assert(classifyHttpStatus(202, 202) == HttpDisposition::Success);
    assert(classifyHttpStatus(409, 202) == HttpDisposition::Success);
    assert(classifyHttpStatus(200, 200) == HttpDisposition::Success);
    assert(classifyHttpStatus(400, 202) == HttpDisposition::PermanentReject);
    for (int status : {401, 403, 408, 429, 500, 503, -1})
        assert(classifyHttpStatus(status, 202) == HttpDisposition::Retry);

    // Bounded queue must preserve existing data rather than evicting oldest.
    assert(canEnqueue(0));
    assert(canEnqueue(7));
    assert(!canEnqueue(8));
    assert(!canEnqueue(100));

    std::cout << "FirmwareCore native tests passed\n";
    return 0;
}
