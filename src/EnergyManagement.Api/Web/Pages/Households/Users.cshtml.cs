using System.ComponentModel.DataAnnotations;
using EnergyManagement.Api.Modules.Identity;
using EnergyManagement.Api.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Households;

public sealed class UsersModel(
    DashboardReadService dashboardReadService,
    HouseholdInvitationService invitationService) : PageModel
{
    [BindProperty]
    public int HouseholdId { get; set; }

    [BindProperty]
    public InviteInput Input { get; set; } = new();

    public IReadOnlyList<HouseholdOption> Households { get; private set; } = [];
    public IReadOnlyList<HouseholdMemberSummary> Members { get; private set; } = [];
    public IReadOnlyList<HouseholdInvitationSummary> Invitations { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? householdId, CancellationToken cancellationToken)
    {
        var result = await LoadAsync(householdId, cancellationToken);
        return result ?? Page();
    }

    public async Task<IActionResult> OnPostInviteAsync(CancellationToken cancellationToken)
    {
        var result = await LoadAsync(HouseholdId, cancellationToken);
        if (result is not null) return result;
        if (!ModelState.IsValid) return Page();

        try
        {
            await invitationService.CreateAsync(
                HouseholdId, User.GetUserId(), Input.Email, Input.Role, cancellationToken);
            TempData["StatusMessage"] = "Invitation sent. The link expires in 24 hours.";
            return RedirectToPage(new { householdId = HouseholdId });
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRevokeAsync(
        int householdId, int invitationId, CancellationToken cancellationToken)
    {
        try
        {
            await invitationService.RevokeAsync(
                householdId, invitationId, User.GetUserId(), cancellationToken);
            TempData["StatusMessage"] = "Invitation revoked.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or UnauthorizedAccessException)
        {
            TempData["ErrorMessage"] = exception.Message;
        }
        return RedirectToPage(new { householdId });
    }

    public async Task<IActionResult> OnPostChangeRoleAsync(
        int householdId, int userId, HouseholdRole role, CancellationToken cancellationToken)
    {
        try
        {
            await invitationService.ChangeRoleAsync(
                householdId, userId, User.GetUserId(), role, cancellationToken);
            TempData["StatusMessage"] = "Household role updated.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            TempData["ErrorMessage"] = exception.Message;
        }
        return RedirectToPage(new { householdId });
    }

    private async Task<IActionResult?> LoadAsync(
        int? requestedHouseholdId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        Households = await dashboardReadService.GetAdministratorHouseholdsAsync(userId, cancellationToken);
        if (Households.Count == 0) return Forbid();

        var selected = requestedHouseholdId.HasValue
            ? Households.FirstOrDefault(item => item.Id == requestedHouseholdId.Value)
            : Households[0];
        if (selected is null) return Forbid();

        HouseholdId = selected.Id;
        Members = await invitationService.GetMembersAsync(HouseholdId, userId, cancellationToken);
        Invitations = await invitationService.GetPendingInvitationsAsync(HouseholdId, userId, cancellationToken);
        return null;
    }

    public sealed class InviteInput
    {
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;

        public HouseholdRole Role { get; set; } = HouseholdRole.HouseholdMember;
    }
}
