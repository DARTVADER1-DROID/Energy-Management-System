using EnergyManagement.Api.Web.Services;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages;

public sealed class IndexModel(DashboardReadService dashboardReadService) : PageModel
{
    public DashboardSnapshot Snapshot { get; private set; } = null!;

    public async Task OnGetAsync([FromQuery] int? householdId, CancellationToken cancellationToken)
    {
        Snapshot = await dashboardReadService.GetDashboardAsync(householdId, User.GetUserId(), cancellationToken);
    }
}
