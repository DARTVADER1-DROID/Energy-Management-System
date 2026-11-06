using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Households;
using Xunit;

namespace EnergyManagement.Tests.Domain;

public sealed class DeviceInvariantTests
{
    [Fact]
    public void Constructor_InitialisesIdentityAndLifecycleTimestamps()
    {
        var before = DateTimeOffset.UtcNow;
        var device = CreateDevice();
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(1, device.DeviceNumber);
        Assert.Equal("H000-DEV001", device.DeviceCode);
        Assert.True(device.IsActive);
        Assert.InRange(device.RegisteredAtUtc, before, after);
        Assert.Null(device.LastSeenAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NormalisesOptionalDescription(string? description)
    {
        var device = CreateDevice(description: description);
        Assert.Null(device.Description);
    }

    [Fact]
    public void Constructor_TrimsDescription()
    {
        var device = CreateDevice(description: "  Main panel  ");
        Assert.Equal("Main panel", device.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_RejectsMissingRequiredHardwareModel(string? value) =>
        Assert.Throws<ArgumentException>(() => CreateDevice(hardwareModel: value!));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_RejectsMissingRequiredFirmwareVersion(string? value) =>
        Assert.Throws<ArgumentException>(() => CreateDevice(firmwareVersion: value!));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_RejectsMissingRequiredInstallationLocation(string? value) =>
        Assert.Throws<ArgumentException>(() => CreateDevice(location: value!));

    [Fact]
    public void Constructor_RejectsNullHousehold() =>
        Assert.Throws<ArgumentNullException>(() => new Device(
            null!, "Monitor", "ESP32", "1.0", "Main DB",
            ElectricalConfiguration.SinglePhase, null, null, 1));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_RejectsNonPositiveDeviceNumber(int number) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateDevice(number: number));

    [Fact]
    public void Deactivate_IsIdempotent()
    {
        var device = CreateDevice();
        device.Deactivate();
        device.Deactivate();

        Assert.False(device.IsActive);
        Assert.Equal("H000-DEV001", device.DeviceCode);
    }

    [Fact]
    public void UpdateLastSeen_AcceptsFirstUtcTimestamp()
    {
        var device = CreateDevice();
        var timestamp = DateTimeOffset.Parse("2026-11-06T10:00:00+00:00");

        device.UpdateLastSeen(timestamp);

        Assert.Equal(timestamp, device.LastSeenAtUtc);
    }

    [Fact]
    public void UpdateLastSeen_DoesNotMoveTimestampBackwardsOrChangeOnEquality()
    {
        var device = CreateDevice();
        var timestamp = DateTimeOffset.Parse("2026-11-06T10:00:00+00:00");

        device.UpdateLastSeen(timestamp);
        device.UpdateLastSeen(timestamp);
        device.UpdateLastSeen(timestamp.AddTicks(-1));

        Assert.Equal(timestamp, device.LastSeenAtUtc);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-5)]
    [InlineData(5)]
    public void UpdateLastSeen_RejectsAnyNonZeroOffset(int offsetHours)
    {
        var device = CreateDevice();
        var timestamp = new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.FromHours(offsetHours));

        Assert.Throws<ArgumentException>(() => device.UpdateLastSeen(timestamp));
    }

    [Fact]
    public void UpdateMetadata_NormalisesOptionalDescriptionToNull()
    {
        var device = CreateDevice();

        device.UpdateMetadata(
            "Updated monitor", "ESP32-S3", "2.0", "Garage", 
            ElectricalConfiguration.ThreePhase, "  ");

        Assert.Null(device.Description);
        Assert.Equal("Updated monitor", device.Name);
        Assert.Equal(ElectricalConfiguration.ThreePhase, device.ElectricalConfiguration);
    }

    [Fact]
    public void UpdateMetadata_RejectsBlankRequiredValueWithoutReplacingIt()
    {
        var device = CreateDevice();
        var originalName = device.Name;

        Assert.Throws<ArgumentException>(() => device.UpdateMetadata(
            " ", "ESP32-S3", "2.0", "Garage",
            ElectricalConfiguration.SinglePhase, null));

        Assert.Equal(originalName, device.Name);
    }

    [Fact]
    public void UpdateThresholds_AcceptsClearingAllThresholds()
    {
        var device = CreateDevice(thresholds: new DeviceThresholds
        {
            MaximumCurrent = 20,
            WarningPersistenceSeconds = 30
        });

        device.UpdateThresholds(new DeviceThresholds());

        Assert.Null(device.Thresholds.MaximumCurrent);
        Assert.Null(device.Thresholds.WarningPersistenceSeconds);
    }

    private static Device CreateDevice(
        string name = "Monitor",
        string hardwareModel = "ESP32",
        string firmwareVersion = "1.0.0",
        string location = "Main DB",
        string? description = null,
        int number = 1,
        DeviceThresholds? thresholds = null) =>
        new(
            new Household("Test Home"),
            name,
            hardwareModel,
            firmwareVersion,
            location,
            ElectricalConfiguration.SinglePhase,
            description,
            thresholds,
            number);
}
