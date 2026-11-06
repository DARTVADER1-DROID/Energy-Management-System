namespace EnergyManagement.Api.Modules.Devices;

/// <summary>
/// Optional dashboard warning limits. These values are not electrical protection
/// settings and must never be used to control or disconnect mains equipment.
/// </summary>
public sealed class DeviceThresholds
{
    public decimal? MinimumVoltage { get; init; }

    public decimal? MaximumVoltage { get; init; }

    public decimal? MaximumCurrent { get; init; }

    public decimal? MaximumInstantaneousPower { get; init; }

    public decimal? MaximumDailyEnergy { get; init; }

    public decimal? MaximumTemperature { get; init; }

    /// <summary>
    /// A warning is raised only after a configured limit remains violated
    /// for this duration. Null means threshold evaluation is not configured.
    /// </summary>
    public int? WarningPersistenceSeconds { get; init; }
}
