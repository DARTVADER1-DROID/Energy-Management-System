using EnergyManagement.Api.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Devices;

public sealed class IndexModel(DashboardReadService dashboardReadService) : PageModel
{
    public DeviceInventoryPage Inventory { get; private set; } = null!;
    public string? Search { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] int? householdId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) return BadRequest();

        Search = search;
        Inventory = await dashboardReadService.GetDevicesAsync(
            householdId, search, page, 25, cancellationToken);
        return Page();
    }
}
