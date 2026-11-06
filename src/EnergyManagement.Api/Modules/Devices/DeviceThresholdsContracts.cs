namespace EnergyManagement.Api.Modules.Devices;

/// <summary>
/// HTTP request representation of optional dashboard warning thresholds.
/// Domain validation is performed when these values are mapped into the domain model.
/// </summary>
public sealed record DeviceThresholdsRequest
{
    public decimal? MinimumVoltage { get; init; }
    public decimal? MaximumVoltage { get; init; }
    public decimal? MaximumCurrent { get; init; }
    public decimal? MaximumInstantaneousPower { get; init; }
    public decimal? MaximumDailyEnergy { get; init; }
    public decimal? MaximumTemperature { get; init; }
    public int? WarningPersistenceSeconds { get; init; }

    public DeviceThresholds ToDomain() => new()
    {
        MinimumVoltage = MinimumVoltage,
        MaximumVoltage = MaximumVoltage,
        MaximumCurrent = MaximumCurrent,
        MaximumInstantaneousPower = MaximumInstantaneousPower,
        MaximumDailyEnergy = MaximumDailyEnergy,
        MaximumTemperature = MaximumTemperature,
        WarningPersistenceSeconds = WarningPersistenceSeconds
    };
}

/// <summary>
/// HTTP response representation of dashboard warning thresholds.
/// </summary>
public sealed record DeviceThresholdsResponse(
    decimal? MinimumVoltage,
    decimal? MaximumVoltage,
    decimal? MaximumCurrent,
    decimal? MaximumInstantaneousPower,
    decimal? MaximumDailyEnergy,
    decimal? MaximumTemperature,
    int? WarningPersistenceSeconds);
