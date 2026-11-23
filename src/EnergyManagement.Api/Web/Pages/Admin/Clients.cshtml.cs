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
        var accessResult = AdminAccessResult();
        if (accessResult is not null) return accessResult;
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
        var accessResult = AdminAccessResult();
        if (accessResult is not null) return accessResult;
        if (!User.HasRecentAuthentication()) return Reauthenticate();
        CreateInput.Email = CreateInput.Email?.Trim() ?? string.Empty;
        // The page contains two independently bound forms. Validate only the active
        // form; otherwise absent fields from the other form can invalidate this POST.
        ModelState.Clear();
        TryValidateModel(CreateInput, nameof(CreateInput));
        if (!ModelState.IsValid) return await ReloadAsync(cancellationToken);

        var email = CreateInput.Email;
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
        var accessResult = AdminAccessResult();
        if (accessResult is not null) return accessResult;
        if (!User.HasRecentAuthentication()) return Reauthenticate();
        EditInput.Email = EditInput.Email?.Trim() ?? string.Empty;
        ModelState.Clear();
        TryValidateModel(EditInput, nameof(EditInput));
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

        // Update username, normalized username, email, normalized email and lockout
        // state through one Identity update so a failed uniqueness check cannot leave
        // the account with a half-updated login identity.
        user.UserName = email;
        user.Email = email;
        user.LockoutEnabled = true;
        user.LockoutEnd = EditInput.IsDisabled ? DateTimeOffset.MaxValue : null;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            AddIdentityErrors(updateResult);
            return await ReloadAsync(cancellationToken);
        }

        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            AddIdentityErrors(stampResult);
            return await ReloadAsync(cancellationToken);
        }
        TempData["StatusMessage"] = EditInput.IsDisabled
            ? "Client details saved and account disabled."
            : "Client details saved and account enabled.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int userId, CancellationToken cancellationToken)
    {
        var accessResult = AdminAccessResult();
        if (accessResult is not null) return accessResult;
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

        // Remove memberships and the user in one Identity transaction. This avoids
        // depending on provider cascade behavior and preserves memberships if deletion fails.
        await using var transaction = await identityDb.Database.BeginTransactionAsync(cancellationToken);
        var memberships = await identityDb.HouseholdMemberships
            .Where(item => item.UserId == userId)
            .ToListAsync(cancellationToken);
        identityDb.HouseholdMemberships.RemoveRange(memberships);
        await identityDb.SaveChangesAsync(cancellationToken);

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(error => error.Description));
            return RedirectToPage();
        }

        await transaction.CommitAsync(cancellationToken);
        TempData["StatusMessage"] = "Client account deleted. Its household memberships were removed.";
        return RedirectToPage();
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

    private IActionResult? AdminAccessResult()
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        return User.IsInRole("Administrator") ? null : Forbid();
    }

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
