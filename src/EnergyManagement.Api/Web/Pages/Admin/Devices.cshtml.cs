using System.ComponentModel.DataAnnotations;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Web.Pages.Admin;

public sealed class DevicesModel(
    EnergyManagementDbContext energyDb,
    DeviceRegistrationService registrationService) : PageModel
{
    [BindProperty] public DeviceInput Input { get; set; } = new();
    public IReadOnlyList<DeviceRow> Devices { get; private set; } = [];
    public IReadOnlyList<HouseholdOption> Households { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!IsAdministrator()) return Forbid();
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!IsAdministrator()) return Forbid();
        if (!User.HasRecentAuthentication()) return Reauthenticate();
        if (!Enum.IsDefined(Input.ElectricalConfiguration))
            ModelState.AddModelError(nameof(Input.ElectricalConfiguration), "Select a supported electrical configuration.");
        if (!ModelState.IsValid) { await LoadAsync(cancellationToken); return Page(); }

        try
        {
            var request = new RegisterDeviceRequest(Input.Name, Input.HardwareModel,
                Input.FirmwareVersion, Input.InstallationLocation, Input.ElectricalConfiguration,
                Input.Description, Input.ToThresholdsRequest());
            var device = await registrationService.RegisterAsync(Input.HouseholdId, request, cancellationToken);
            TempData["StatusMessage"] = $"Device {device.DeviceCode} registered.";
            return RedirectToPage();
        }
        catch (Exception exception) when (exception is ArgumentException or HouseholdNotFoundException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync(int deviceId, CancellationToken cancellationToken)
    {
        if (!IsAdministrator()) return Forbid();
        if (!User.HasRecentAuthentication()) return Reauthenticate();
        if (!Enum.IsDefined(Input.ElectricalConfiguration))
            ModelState.AddModelError(nameof(Input.ElectricalConfiguration), "Select a supported electrical configuration.");
        if (!ModelState.IsValid) { await LoadAsync(cancellationToken); return Page(); }

        var device = await energyDb.Devices.SingleOrDefaultAsync(item => item.Id == deviceId, cancellationToken);
        if (device is null) return NotFound();
        try
        {
            device.UpdateMetadata(Input.Name, Input.HardwareModel, Input.FirmwareVersion,
                Input.InstallationLocation, Input.ElectricalConfiguration, Input.Description);
            device.UpdateThresholds(Input.ToThresholds());
            await energyDb.SaveChangesAsync(cancellationToken);
            TempData["StatusMessage"] = $"Device {device.DeviceCode} updated.";
            return RedirectToPage();
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeactivateAsync(int deviceId, CancellationToken cancellationToken)
    {
        if (!IsAdministrator()) return Forbid();
        if (!User.HasRecentAuthentication()) return Reauthenticate();
        var device = await energyDb.Devices.SingleOrDefaultAsync(item => item.Id == deviceId, cancellationToken);
        if (device is null) return NotFound();
        device.Deactivate();
        await energyDb.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = $"Device {device.DeviceCode} deactivated. Telemetry and credential audit history were retained.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var householdEntities = await energyDb.Households.AsNoTracking().OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.Name })
            .ToListAsync(cancellationToken);
        Households = householdEntities.Select(item =>
            new HouseholdOption(item.Id, $"H{item.Id:000}", item.Name)).ToArray();

        Devices = await (from device in energyDb.Devices.AsNoTracking()
                         join household in energyDb.Households.AsNoTracking()
                             on device.HouseholdId equals household.Id
                         orderby device.RegisteredAtUtc descending
                         select new DeviceRow(device.Id, device.HouseholdId, device.DeviceCode,
                             device.Name, household.Name, device.HardwareModel, device.FirmwareVersion,
                             device.InstallationLocation, device.ElectricalConfiguration,
                             device.Description, device.IsActive, device.RegisteredAtUtc,
                             device.Thresholds.MinimumVoltage, device.Thresholds.MaximumVoltage,
                             device.Thresholds.MaximumCurrent, device.Thresholds.MaximumInstantaneousPower,
                             device.Thresholds.MaximumDailyEnergy, device.Thresholds.MaximumTemperature,
                             device.Thresholds.WarningPersistenceSeconds)).Take(300).ToListAsync(cancellationToken);
    }

    private bool IsAdministrator() => User.Identity?.IsAuthenticated == true && User.IsInRole("Administrator");
    private IActionResult Reauthenticate() => RedirectToPage("/Account/Reauthenticate",
        new { returnUrl = Url.Page("/Admin/Devices") });

    public sealed class DeviceInput
    {
        [Range(1, int.MaxValue)] public int HouseholdId { get; set; }
        [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
        [Required, StringLength(80)] public string HardwareModel { get; set; } = string.Empty;
        [Required, StringLength(80)] public string FirmwareVersion { get; set; } = string.Empty;
        [Required, StringLength(160)] public string InstallationLocation { get; set; } = string.Empty;
        public ElectricalConfiguration ElectricalConfiguration { get; set; } = ElectricalConfiguration.SinglePhase;
        [StringLength(1000)] public string? Description { get; set; }
        [Range(typeof(decimal), "0", "100000")] public decimal? MinimumVoltage { get; set; }
        [Range(typeof(decimal), "0", "100000")] public decimal? MaximumVoltage { get; set; }
        [Range(typeof(decimal), "0", "100000")] public decimal? MaximumCurrent { get; set; }
        [Range(typeof(decimal), "0", "100000000")] public decimal? MaximumInstantaneousPower { get; set; }
        [Range(typeof(decimal), "0", "100000000")] public decimal? MaximumDailyEnergy { get; set; }
        [Range(typeof(decimal), "0", "10000")] public decimal? MaximumTemperature { get; set; }
        [Range(1, 86400)] public int? WarningPersistenceSeconds { get; set; }

        public DeviceThresholds ToThresholds() => new()
        {
            MinimumVoltage = MinimumVoltage, MaximumVoltage = MaximumVoltage,
            MaximumCurrent = MaximumCurrent, MaximumInstantaneousPower = MaximumInstantaneousPower,
            MaximumDailyEnergy = MaximumDailyEnergy, MaximumTemperature = MaximumTemperature,
            WarningPersistenceSeconds = WarningPersistenceSeconds
        };

        public DeviceThresholdsRequest ToThresholdsRequest() => new()
        {
            MinimumVoltage = MinimumVoltage, MaximumVoltage = MaximumVoltage,
            MaximumCurrent = MaximumCurrent, MaximumInstantaneousPower = MaximumInstantaneousPower,
            MaximumDailyEnergy = MaximumDailyEnergy, MaximumTemperature = MaximumTemperature,
            WarningPersistenceSeconds = WarningPersistenceSeconds
        };
    }

    public sealed record HouseholdOption(int Id, string Code, string Name);
    public sealed record DeviceRow(int Id, int HouseholdId, string Code, string Name, string Household,
        string HardwareModel, string FirmwareVersion, string InstallationLocation,
        ElectricalConfiguration ElectricalConfiguration, string? Description, bool IsActive,
        DateTimeOffset RegisteredAtUtc, decimal? MinimumVoltage, decimal? MaximumVoltage,
        decimal? MaximumCurrent, decimal? MaximumInstantaneousPower, decimal? MaximumDailyEnergy,
        decimal? MaximumTemperature, int? WarningPersistenceSeconds);
}
