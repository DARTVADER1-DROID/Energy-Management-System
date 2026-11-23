using System.ComponentModel.DataAnnotations;
using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Web.Pages.Admin;

public sealed class ClientsModel(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext identityDb,
    DeviceSecurityDbContext deviceSecurityDb) : PageModel
{
    [BindProperty] public CreateClientInput CreateInput { get; set; } = new();
    [BindProperty] public EditClientInput EditInput { get; set; } = new();
    public IReadOnlyList<ClientRow> Clients { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!IsAdministrator()) return Forbid();
        var users = await userManager.Users.AsNoTracking()
            .OrderByDescending(user => user.CreatedAtUtc)
            .Take(250)
            .ToListAsync(cancellationToken);
        var rows = new List<ClientRow>(users.Count);
        foreach (var user in users)
        {
            var isAdministrator = await userManager.IsInRoleAsync(user, "Administrator");
            var isDisabled = user.LockoutEnd.HasValue &&
                user.LockoutEnd.Value > DateTimeOffset.UtcNow;
            rows.Add(new ClientRow(user.Id, user.Email ?? user.UserName ?? "(no email)",
                user.EmailConfirmed, user.CreatedAtUtc, isDisabled, isAdministrator));
        }
        Clients = rows;
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!IsAdministrator()) return Forbid();
        if (!User.HasRecentAuthentication()) return Reauthenticate();
        RemoveModelStatePrefix(nameof(EditInput));
        if (!ModelState.IsValid) return await ReloadAsync(cancellationToken);

        var email = CreateInput.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true
        };
        var result = await userManager.CreateAsync(user, CreateInput.Password);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return await ReloadAsync(cancellationToken);
        }

        TempData["StatusMessage"] = "Client account created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(CancellationToken cancellationToken)
    {
        if (!IsAdministrator()) return Forbid();
        if (!User.HasRecentAuthentication()) return Reauthenticate();
        RemoveModelStatePrefix(nameof(CreateInput));
        if (!ModelState.IsValid) return await ReloadAsync(cancellationToken);

        var user = await userManager.FindByIdAsync(EditInput.UserId.ToString());
        if (user is null) return NotFound();
        if (await userManager.IsInRoleAsync(user, "Administrator") ||
            user.Id == User.GetUserId())
        {
            TempData["ErrorMessage"] = "Administrator accounts and your own account cannot be modified here.";
            return RedirectToPage();
        }

        var email = EditInput.Email.Trim();
        var existingEmail = await userManager.FindByEmailAsync(email);
        var existingUserName = await userManager.FindByNameAsync(email);
        if ((existingEmail is not null && existingEmail.Id != user.Id) ||
            (existingUserName is not null && existingUserName.Id != user.Id))
        {
            ModelState.AddModelError(string.Empty, "That email address is already assigned to another account.");
            return await ReloadAsync(cancellationToken);
        }

        var userNameResult = await userManager.SetUserNameAsync(user, email);
        if (!userNameResult.Succeeded)
        {
            AddIdentityErrors(userNameResult);
            return await ReloadAsync(cancellationToken);
        }

        var emailResult = await userManager.SetEmailAsync(user, email);
        if (!emailResult.Succeeded)
        {
            AddIdentityErrors(emailResult);
            return await ReloadAsync(cancellationToken);
        }

        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user,
            EditInput.IsDisabled ? DateTimeOffset.MaxValue : null);
        await userManager.UpdateSecurityStampAsync(user);
        TempData["StatusMessage"] = EditInput.IsDisabled
            ? "Client details saved and account disabled."
            : "Client details saved and account enabled.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int userId, CancellationToken cancellationToken)
    {
        if (!IsAdministrator()) return Forbid();
        if (!User.HasRecentAuthentication()) return Reauthenticate();

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound();
        if (user.Id == User.GetUserId() || await userManager.IsInRoleAsync(user, "Administrator"))
        {
            TempData["ErrorMessage"] = "You cannot delete your own account or another administrator.";
            return RedirectToPage();
        }

        var hasIdentityAuditHistory = await identityDb.SecurityAuditEvents.AsNoTracking()
            .AnyAsync(item => item.ActorUserId == userId || item.TargetUserId == userId, cancellationToken);
        var createdInvitations = await identityDb.HouseholdInvitations.AsNoTracking()
            .AnyAsync(item => item.CreatedByUserId == userId, cancellationToken);
        var hasDeviceAuditHistory = await deviceSecurityDb.AuditEvents.AsNoTracking()
            .AnyAsync(item => item.ActorUserId == userId, cancellationToken);
        var createdProvisioningCodes = await deviceSecurityDb.ProvisioningCodes.AsNoTracking()
            .AnyAsync(item => item.CreatedByUserId == userId, cancellationToken);
        if (hasIdentityAuditHistory || createdInvitations ||
            hasDeviceAuditHistory || createdProvisioningCodes)
        {
            TempData["ErrorMessage"] = "This account has retained security, device-provisioning, or invitation history. Disable it instead of deleting it.";
            return RedirectToPage();
        }

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(error => error.Description));
            return RedirectToPage();
        }

        TempData["StatusMessage"] = "Client account deleted. Its household memberships were removed by Identity.";
        return RedirectToPage();
    }

    private void RemoveModelStatePrefix(string prefix)
    {
        foreach (var key in ModelState.Keys
                     .Where(key => key.StartsWith(prefix + ".", StringComparison.Ordinal))
                     .ToArray())
            ModelState.Remove(key);
    }

    private async Task<IActionResult> ReloadAsync(CancellationToken cancellationToken)
    {
        await OnGetAsync(cancellationToken);
        return Page();
    }

    private IActionResult Reauthenticate()
    {
        var returnUrl = Url.Page("/Admin/Clients");
        return RedirectToPage("/Account/Reauthenticate", new { returnUrl });
    }

    private bool IsAdministrator() => User.Identity?.IsAuthenticated == true && User.IsInRole("Administrator");

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
    }

    public sealed class CreateClientInput
    {
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(128, MinimumLength = 12)]
        public string Password { get; set; } = string.Empty;
    }

    public sealed class EditClientInput
    {
        [Range(1, int.MaxValue)] public int UserId { get; set; }
        [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
        public bool IsDisabled { get; set; }
    }

    public sealed record ClientRow(int Id, string Email, bool EmailConfirmed,
        DateTimeOffset CreatedAtUtc, bool IsDisabled, bool IsAdministrator);
}
