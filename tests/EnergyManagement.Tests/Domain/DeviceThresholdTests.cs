using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Households;
using Xunit;

namespace EnergyManagement.Tests.Domain;

public sealed class DeviceThresholdTests
{
    [Fact]
    public void Constructor_AllowsNoThresholdsAndOptionalTemperature()
    {
        var device = CreateDevice(new DeviceThresholds());

        Assert.Null(device.Thresholds.MaximumTemperature);
        Assert.Null(device.Thresholds.WarningPersistenceSeconds);
    }

    [Fact]
    public void Constructor_AcceptsConfiguredThresholdsWithPersistenceDuration()
    {
        var thresholds = new DeviceThresholds
        {
            MinimumVoltage = 200,
            MaximumVoltage = 250,
            MaximumCurrent = 40,
            MaximumInstantaneousPower = 8000,
            MaximumDailyEnergy = 30,
            MaximumTemperature = null,
            WarningPersistenceSeconds = 10
        };

        var device = CreateDevice(thresholds);

        Assert.Equal(200, device.Thresholds.MinimumVoltage);
        Assert.Equal(10, device.Thresholds.WarningPersistenceSeconds);
        Assert.Null(device.Thresholds.MaximumTemperature);
    }

    [Theory]
    [MemberData(nameof(NegativeThresholds))]
    public void Constructor_RejectsNegativeThresholds(DeviceThresholds thresholds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateDevice(thresholds));
    }

    public static TheoryData<DeviceThresholds> NegativeThresholds => new()
    {
        new DeviceThresholds { MinimumVoltage = -1 },
        new DeviceThresholds { MaximumVoltage = -1 },
        new DeviceThresholds { MaximumCurrent = -1 },
        new DeviceThresholds { MaximumInstantaneousPower = -1 },
        new DeviceThresholds { MaximumDailyEnergy = -1 },
        new DeviceThresholds { MaximumTemperature = -1 }
    };

    [Theory]
    [InlineData(230, 230)]
    [InlineData(250, 230)]
    public void Constructor_RejectsInvalidVoltageRange(decimal minimum, decimal maximum)
    {
        var thresholds = new DeviceThresholds
        {
            MinimumVoltage = minimum,
            MaximumVoltage = maximum,
            WarningPersistenceSeconds = 10
        };

        Assert.Throws<ArgumentException>(() => CreateDevice(thresholds));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(86401)]
    public void Constructor_RejectsInvalidPersistenceDuration(int duration)
    {
        var thresholds = new DeviceThresholds
        {
            MaximumCurrent = 10,
            WarningPersistenceSeconds = duration
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateDevice(thresholds));
    }

    [Fact]
    public void Constructor_RequiresPersistenceDurationWhenAnyThresholdIsConfigured()
    {
        var thresholds = new DeviceThresholds { MaximumCurrent = 10 };

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateDevice(thresholds));
    }

    [Fact]
    public void UpdateThresholds_RejectsInvalidThresholds()
    {
        var device = CreateDevice(new DeviceThresholds());
        var invalid = new DeviceThresholds { MaximumCurrent = 12 };

        Assert.Throws<ArgumentOutOfRangeException>(() => device.UpdateThresholds(invalid));
    }

    private static Device CreateDevice(DeviceThresholds thresholds) =>
        new(
            Household.Rehydrate(1, "Test Household"),
            "Monitor",
            "ESP32",
            "1.0.0",
            "Main DB",
            ElectricalConfiguration.SinglePhase,
            null,
            thresholds,
            1);
}
