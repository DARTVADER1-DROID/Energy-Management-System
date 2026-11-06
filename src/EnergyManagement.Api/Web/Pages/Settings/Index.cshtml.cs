using EnergyManagement.Api.Modules.Identity;
using EnergyManagement.Api.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Settings;

public sealed class IndexModel(DashboardReadService dashboardReadService) : PageModel
{
    public int? DefaultAdministratorHouseholdId { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var households = await dashboardReadService.GetAdministratorHouseholdsAsync(
            User.GetUserId(), cancellationToken);
        DefaultAdministratorHouseholdId = households.FirstOrDefault()?.Id;
    }
}
