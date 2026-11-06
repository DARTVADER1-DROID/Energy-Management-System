using System.ComponentModel.DataAnnotations;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Identity;
using EnergyManagement.Api.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Devices;

public sealed class RegisterModel(
    DashboardReadService dashboardReadService,
    DeviceRegistrationService deviceRegistrationService) : PageModel
{
    [BindProperty]
    public RegistrationInput Input { get; set; } = new();

    public IReadOnlyList<HouseholdOption> Households { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? householdId, CancellationToken cancellationToken)
    {
        Households = await dashboardReadService.GetAdministratorHouseholdsAsync(User.GetUserId(), cancellationToken);
        if (Households.Count == 0) return Forbid();

        Input.HouseholdId = Households.FirstOrDefault(item => item.Id == householdId)?.Id
            ?? Households[0].Id;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Households = await dashboardReadService.GetAdministratorHouseholdsAsync(
            User.GetUserId(), cancellationToken);
        if (!Households.Any(item => item.Id == Input.HouseholdId))
            return Forbid();
        if (!ModelState.IsValid) return Page();

        if (!User.HasRecentAuthentication())
        {
            var returnUrl = Url.Page("/Devices/Register", new { householdId = Input.HouseholdId });
            return RedirectToPage("/Account/Reauthenticate", new { returnUrl });
        }

        var thresholds = new DeviceThresholdsRequest
        {
            MinimumVoltage = Input.MinimumVoltage,
            MaximumVoltage = Input.MaximumVoltage,
            MaximumCurrent = Input.MaximumCurrent,
            MaximumInstantaneousPower = Input.MaximumInstantaneousPower,
            MaximumDailyEnergy = Input.MaximumDailyEnergy,
            MaximumTemperature = Input.MaximumTemperature,
            WarningPersistenceSeconds = Input.WarningPersistenceSeconds
        };

        var request = new RegisterDeviceRequest(
            Input.Name,
            Input.HardwareModel,
            Input.FirmwareVersion,
            Input.InstallationLocation,
            Input.ElectricalConfiguration,
            Input.Description,
            thresholds);

        try
        {
            var device = await deviceRegistrationService.RegisterAsync(
                Input.HouseholdId, request, cancellationToken);
            return RedirectToPage("/Devices/Details", new { deviceCode = device.DeviceCode });
        }
        catch (HouseholdNotFoundException)
        {
            ModelState.AddModelError(nameof(Input.HouseholdId), "Select an existing household.");
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        return Page();
    }

    public sealed class RegistrationInput
    {
        [Range(1, int.MaxValue)]
        public int HouseholdId { get; set; }

        [Required, StringLength(120)]
        public string Name { get; set; } = string.Empty;

        [Required, StringLength(80)]
        public string HardwareModel { get; set; } = string.Empty;

        [Required, StringLength(80)]
        public string FirmwareVersion { get; set; } = string.Empty;

        [Required, StringLength(160)]
        public string InstallationLocation { get; set; } = string.Empty;

        public ElectricalConfiguration ElectricalConfiguration { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        [Range(typeof(decimal), "0", "100000")]
        public decimal? MinimumVoltage { get; set; }

        [Range(typeof(decimal), "0", "100000")]
        public decimal? MaximumVoltage { get; set; }

        [Range(typeof(decimal), "0", "100000")]
        public decimal? MaximumCurrent { get; set; }

        [Range(typeof(decimal), "0", "100000000")]
        public decimal? MaximumInstantaneousPower { get; set; }

        [Range(typeof(decimal), "0", "100000000")]
        public decimal? MaximumDailyEnergy { get; set; }

        [Range(typeof(decimal), "0", "10000")]
        public decimal? MaximumTemperature { get; set; }

        [Range(1, 86400)]
        public int? WarningPersistenceSeconds { get; set; }
    }
}
