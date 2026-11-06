using System.ComponentModel.DataAnnotations;

namespace EnergyManagement.Api.Modules.Telemetry;

public sealed record TelemetryEnvelope(
    [property: Required] int SchemaVersion,
    [property: Required] Guid SampleId,
    [property: Required] DateTimeOffset CapturedAtUtc,
    [property: Required] TelemetryMeasurements Measurements,
    IReadOnlyDictionary<string, PhaseTelemetryMeasurements>? Phases = null,
    IReadOnlyDictionary<string, decimal>? Sensors = null);

public sealed record TelemetryMeasurements(
    decimal? VoltageVolts = null,
    decimal? CurrentAmps = null,
    decimal? ActivePowerWatts = null,
    decimal? EnergyWattHours = null,
    decimal? FrequencyHertz = null,
    decimal? PowerFactor = null,
    decimal? TemperatureCelsius = null);

public sealed record PhaseTelemetryMeasurements(
    decimal? VoltageVolts = null,
    decimal? CurrentAmps = null,
    decimal? ActivePowerWatts = null,
    decimal? EnergyWattHours = null,
    decimal? PowerFactor = null);

public sealed record TelemetryAcceptedResponse(
    Guid SampleId,
    string DeviceCode,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset ReceivedAtUtc);

public sealed record TelemetryReadingResponse(
    Guid SampleId,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset ReceivedAtUtc,
    int SchemaVersion,
    TelemetryEnvelope Data);
