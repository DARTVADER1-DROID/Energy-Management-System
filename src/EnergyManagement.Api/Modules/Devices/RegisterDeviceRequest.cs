namespace EnergyManagement.Api.Modules.Devices;

public sealed record RegisterDeviceRequest(
    string Name,
    string HardwareModel,
    string FirmwareVersion,
    string InstallationLocation,
    ElectricalConfiguration ElectricalConfiguration,
    string? Description,
    DeviceThresholdsRequest? Thresholds);

public sealed record CreateHouseholdRequest(string Name);
