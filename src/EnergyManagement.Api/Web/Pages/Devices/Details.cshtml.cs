using EnergyManagement.Api.Web.Services;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Devices;

public sealed class DetailsModel(DashboardReadService dashboardReadService) : PageModel
{
    public DeviceDetails Device { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string deviceCode, CancellationToken cancellationToken)
    {
        var device = await dashboardReadService.GetDeviceDetailsAsync(deviceCode, User.GetUserId(), cancellationToken);
        if (device is null) return NotFound();

        Device = device;
        return Page();
    }
}
