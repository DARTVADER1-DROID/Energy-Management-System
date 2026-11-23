using System.ComponentModel.DataAnnotations;
using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Households;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Web.Pages.Admin;

public sealed class HouseholdsModel(
    EnergyManagementDbContext energyDb,
    IdentityDbContext identityDb,
    DeviceRegistrationService registrationService,
    HouseholdAccessService householdAccess) : PageModel
{
    [BindProperty] public HouseholdInput Input { get; set; } = new();
    public IReadOnlyList<HouseholdRow> Households { get; private set; } = [];

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
        if (!ModelState.IsValid) { await LoadAsync(cancellationToken); return Page(); }

        var household = await registrationService.CreateHouseholdAsync(Input.Name, cancellationToken);
        await householdAccess.AddMembershipAsync(User.GetUserId(), household.Id,
            HouseholdRole.Administrator, cancellationToken);
        TempData["StatusMessage"] = $"Household {household.HouseholdCode} created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int householdId, CancellationToken cancellationToken)
    {
        if (!IsAdministrator()) return Forbid();
        if (!User.HasRecentAuthentication()) return Reauthenticate();
        if (!ModelState.IsValid) { await LoadAsync(cancellationToken); return Page(); }

        var household = await energyDb.Households.SingleOrDefaultAsync(item => item.Id == householdId, cancellationToken);
        if (household is null) return NotFound();
        household.Rename(Input.Name);
        await energyDb.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = $"Household {household.HouseholdCode} updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int householdId, CancellationToken cancellationToken)
    {
        if (!IsAdministrator()) return Forbid();
        if (!User.HasRecentAuthentication()) return Reauthenticate();

        var household = await energyDb.Households.SingleOrDefaultAsync(item => item.Id == householdId, cancellationToken);
        if (household is null) return NotFound();

        var hasDevices = await energyDb.Devices.AsNoTracking()
            .AnyAsync(item => item.HouseholdId == householdId, cancellationToken);
        var hasInvitations = await identityDb.HouseholdInvitations.AsNoTracking()
            .AnyAsync(item => item.HouseholdId == householdId, cancellationToken);
        var hasAuditHistory = await identityDb.SecurityAuditEvents.AsNoTracking()
            .AnyAsync(item => item.HouseholdId == householdId, cancellationToken);
        if (hasDevices || hasInvitations || hasAuditHistory)
        {
            TempData["ErrorMessage"] = "This household has devices, invitations, or retained security history. It cannot be deleted; keep it or remove eligible dependencies first.";
            return RedirectToPage();
        }

        // The household's membership rows are configured with ON DELETE CASCADE.
        // Let PostgreSQL remove them in the same atomic delete as the household.
        energyDb.Households.Remove(household);
        await energyDb.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = $"Household {household.HouseholdCode} deleted.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var households = await energyDb.Households.AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.Name })
            .ToListAsync(cancellationToken);
        var rows = new List<HouseholdRow>(households.Count);
        foreach (var household in households)
        {
            var deviceCount = await energyDb.Devices.AsNoTracking()
                .CountAsync(item => item.HouseholdId == household.Id, cancellationToken);
            var memberCount = await identityDb.HouseholdMemberships.AsNoTracking()
                .CountAsync(item => item.HouseholdId == household.Id, cancellationToken);
            rows.Add(new HouseholdRow(household.Id, $"H{household.Id:000}",
                household.Name, deviceCount, memberCount));
        }
        Households = rows;
    }

    private bool IsAdministrator() => User.Identity?.IsAuthenticated == true && User.IsInRole("Administrator");
    private IActionResult Reauthenticate() => RedirectToPage("/Account/Reauthenticate",
        new { returnUrl = Url.Page("/Admin/Households") });

    public sealed class HouseholdInput
    {
        [Required, StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;
    }

    public sealed record HouseholdRow(int Id, string Code, string Name, int DeviceCount, int MemberCount);
}
