using EnergyManagement.Api.Modules.Households;

namespace EnergyManagement.Api.Modules.Devices;

public sealed class Device
{
    private Device()
    {
    }

    public Device(
        Household household,
        string name,
        string hardwareModel,
        string firmwareVersion,
        string installationLocation,
        ElectricalConfiguration electricalConfiguration,
        string? description,
        DeviceThresholds? thresholds,
        int deviceNumber)
    {
        ArgumentNullException.ThrowIfNull(household);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(household.Id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deviceNumber);

        Name = Required(name, nameof(name));
        HardwareModel = Required(hardwareModel, nameof(hardwareModel));
        FirmwareVersion = Required(firmwareVersion, nameof(firmwareVersion));
        InstallationLocation = Required(installationLocation, nameof(installationLocation));
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ElectricalConfiguration = electricalConfiguration;
        HouseholdId = household.Id;
        DeviceNumber = deviceNumber;
        DeviceCode = $"{household.HouseholdCode}-DEV{deviceNumber:000}";
        Thresholds = thresholds ?? new DeviceThresholds();
        ValidateThresholds(Thresholds);
        IsActive = true;
        RegisteredAtUtc = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; }
    public int HouseholdId { get; private set; }
    public string DeviceCode { get; private set; } = string.Empty;
    public int DeviceNumber { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string HardwareModel { get; private set; } = string.Empty;
    public string FirmwareVersion { get; private set; } = string.Empty;
    public string InstallationLocation { get; private set; } = string.Empty;
    public ElectricalConfiguration ElectricalConfiguration { get; private set; }
    public string? Description { get; private set; }
    public DeviceThresholds Thresholds { get; private set; } = new();
    public bool IsActive { get; private set; }
    public DateTimeOffset RegisteredAtUtc { get; private set; }
    public DateTimeOffset? LastSeenAtUtc { get; private set; }

    public void UpdateLastSeen(DateTimeOffset timestampUtc)
    {
        if (timestampUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be UTC.", nameof(timestampUtc));
        }

        if (LastSeenAtUtc is null || timestampUtc > LastSeenAtUtc)
        {
            LastSeenAtUtc = timestampUtc;
        }
    }

    public void Deactivate() => IsActive = false;

    public void UpdateMetadata(
        string name,
        string hardwareModel,
        string firmwareVersion,
        string installationLocation,
        ElectricalConfiguration electricalConfiguration,
        string? description)
    {
        Name = Required(name, nameof(name));
        HardwareModel = Required(hardwareModel, nameof(hardwareModel));
        FirmwareVersion = Required(firmwareVersion, nameof(firmwareVersion));
        InstallationLocation = Required(installationLocation, nameof(installationLocation));
        ElectricalConfiguration = electricalConfiguration;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public void UpdateThresholds(DeviceThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        ValidateThresholds(thresholds);
        Thresholds = thresholds;
    }

    private static void ValidateThresholds(DeviceThresholds thresholds)
    {
        if (thresholds.MinimumVoltage < 0 ||
            thresholds.MaximumVoltage < 0 ||
            thresholds.MaximumCurrent < 0 ||
            thresholds.MaximumInstantaneousPower < 0 ||
            thresholds.MaximumDailyEnergy < 0 ||
            thresholds.MaximumTemperature < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholds), "Threshold values cannot be negative.");
        }

        if (thresholds.MinimumVoltage is not null &&
            thresholds.MaximumVoltage is not null &&
            thresholds.MinimumVoltage >= thresholds.MaximumVoltage)
        {
            throw new ArgumentException("Minimum voltage must be lower than maximum voltage.", nameof(thresholds));
        }

        var hasThreshold = thresholds.MinimumVoltage is not null ||
                           thresholds.MaximumVoltage is not null ||
                           thresholds.MaximumCurrent is not null ||
                           thresholds.MaximumInstantaneousPower is not null ||
                           thresholds.MaximumDailyEnergy is not null ||
                           thresholds.MaximumTemperature is not null;

        if (hasThreshold && thresholds.WarningPersistenceSeconds is null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thresholds),
                "A warning persistence duration is required when thresholds are configured.");
        }

        if (thresholds.WarningPersistenceSeconds is <= 0 or > 86400)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thresholds),
                "Warning persistence duration must be between 1 and 86400 seconds.");
        }
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        return value.Trim();
    }
}
