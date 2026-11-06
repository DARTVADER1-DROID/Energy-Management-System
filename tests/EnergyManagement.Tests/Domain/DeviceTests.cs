using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Households;
using Xunit;

namespace EnergyManagement.Tests.Domain;

public sealed class DeviceTests
{
    [Fact]
    public void Constructor_TrimsRequiredMetadataAndSetsActive()
    {
        var device = CreateDevice();

        Assert.Equal("Main Energy Monitor", device.Name);
        Assert.Equal("ESP32", device.HardwareModel);
        Assert.Equal("1.0.0", device.FirmwareVersion);
        Assert.Equal("Main Distribution Board", device.InstallationLocation);
        Assert.True(device.IsActive);
        Assert.Null(device.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_RejectsBlankRequiredName(string name)
    {
        Assert.Throws<ArgumentException>(() => CreateDevice(name: name));
    }

    [Fact]
    public void Constructor_AcceptsSingleAndThreePhaseConfigurations()
    {
        Assert.Equal(ElectricalConfiguration.SinglePhase, CreateDevice().ElectricalConfiguration);
        Assert.Equal(
            ElectricalConfiguration.ThreePhase,
            CreateDevice(configuration: ElectricalConfiguration.ThreePhase).ElectricalConfiguration);
    }

    [Fact]
    public void Constructor_GeneratesHouseholdScopedDeviceCode()
    {
        var device = CreateDevice(deviceNumber: 12);

        Assert.Equal("H000-DEV012", device.DeviceCode);
    }

    [Fact]
    public void Deactivate_SetsDeviceInactiveWithoutChangingIdentity()
    {
        var device = CreateDevice();

        device.Deactivate();

        Assert.False(device.IsActive);
        Assert.Equal("H000-DEV001", device.DeviceCode);
    }

    [Fact]
    public void UpdateMetadata_UpdatesEditableFields()
    {
        var device = CreateDevice();

        device.UpdateMetadata(
            "Sub Board Monitor",
            "ESP32-S3",
            "1.1.0",
            "First Floor DB",
            ElectricalConfiguration.ThreePhase,
            "Updated installation");

        Assert.Equal("Sub Board Monitor", device.Name);
        Assert.Equal("ESP32-S3", device.HardwareModel);
        Assert.Equal("1.1.0", device.FirmwareVersion);
        Assert.Equal("First Floor DB", device.InstallationLocation);
        Assert.Equal(ElectricalConfiguration.ThreePhase, device.ElectricalConfiguration);
        Assert.Equal("Updated installation", device.Description);
    }

    [Fact]
    public void UpdateLastSeen_KeepsNewestTimestamp()
    {
        var device = CreateDevice();
        var newest = DateTimeOffset.Parse("2026-11-06T12:00:00+00:00");
        var older = newest.AddMinutes(-1);

        device.UpdateLastSeen(newest);
        device.UpdateLastSeen(older);

        Assert.Equal(newest, device.LastSeenAtUtc);
    }

    [Fact]
    public void UpdateLastSeen_RejectsNonUtcTimestamp()
    {
        var device = CreateDevice();
        var localOffset = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(5.5));

        Assert.Throws<ArgumentException>(() => device.UpdateLastSeen(localOffset));
    }

    private static Device CreateDevice(
        string name = " Main Energy Monitor ",
        ElectricalConfiguration configuration = ElectricalConfiguration.SinglePhase,
        int deviceNumber = 1) =>
        new(
            new Household("Test Household"),
            name,
            " ESP32 ",
            "1.0.0",
            " Main Distribution Board ",
            configuration,
            null,
            null,
            deviceNumber);
}
