#pragma once

#include <cmath>
#include <cstddef>
#include <cstdint>

namespace ems {

constexpr std::size_t kMaxPendingSamples = 8;
constexpr uint32_t kInitialProvisionBackoffMs = 30000U;
constexpr uint32_t kMaxProvisionBackoffMs = 1800000U;

inline bool validMeasurement(double voltage, double current, double power,
                             double energy, double frequency, double powerFactor) {
    return std::isfinite(voltage) && std::isfinite(current) &&
           std::isfinite(power) && std::isfinite(energy) &&
           voltage >= 0.0 && voltage <= 1000.0 &&
           current >= 0.0 && current <= 10000.0 &&
           power >= -10000000.0 && power <= 10000000.0 &&
           energy >= 0.0 && energy <= 1000000000000.0 &&
           (!std::isfinite(frequency) || (frequency >= 0.0 && frequency <= 1000.0)) &&
           (!std::isfinite(powerFactor) || (powerFactor >= -1.0 && powerFactor <= 1.0));
}

struct EnergyCounterState {
    double offsetWh = 0.0;
    double lastRawWh = -1.0;
    bool initialized = false;
};

// Returns true when a reset/drop was detected. The energy used during the reset
// interval cannot be reconstructed; callers must not represent it as measured.
inline bool normalizeEnergy(double rawWh, EnergyCounterState& state, double& totalWh) {
    if (!std::isfinite(rawWh) || rawWh < 0.0) return false;
    bool reset = false;
    if (state.initialized && rawWh + 1.0 < state.lastRawWh) {
        state.offsetWh += state.lastRawWh;
        reset = true;
    }
    state.lastRawWh = rawWh;
    state.initialized = true;
    totalWh = rawWh + state.offsetWh;
    return reset;
}

inline uint32_t nextProvisionBackoff(uint32_t currentMs) {
    if (currentMs == 0U) return kInitialProvisionBackoffMs;
    if (currentMs >= kMaxProvisionBackoffMs / 2U) return kMaxProvisionBackoffMs;
    return currentMs * 2U;
}

enum class HttpDisposition { Success, PermanentReject, Retry };

inline HttpDisposition classifyHttpStatus(int status, int expectedStatus) {
    if (status == expectedStatus || (expectedStatus == 202 && status == 409))
        return HttpDisposition::Success;
    if (status == 400) return HttpDisposition::PermanentReject;
    return HttpDisposition::Retry;
}

inline bool canEnqueue(std::size_t pending, std::size_t capacity = kMaxPendingSamples) {
    return pending < capacity;
}

} // namespace ems
