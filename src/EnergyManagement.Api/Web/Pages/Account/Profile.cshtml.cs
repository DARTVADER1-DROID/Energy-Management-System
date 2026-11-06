using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class ProfileModel(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext identityDb,
    MfaRecoveryCodeService recoveryCodeService) : PageModel
{
    public string Email { get; private set; } = string.Empty;
    public bool EmailConfirmed { get; private set; }
    public bool TwoFactorEnabled { get; private set; }
    public bool IsAdministrator { get; private set; }
    public int RecoveryCodesRemaining { get; private set; }
    public IReadOnlyList<string> GeneratedRecoveryCodes { get; private set; } = [];

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostRegenerateRecoveryCodesAsync(
        CancellationToken cancellationToken)
    {
        if (!User.HasRecentAuthentication())
        {
            var returnUrl = Url.Page("/Account/Profile");
            return RedirectToPage("/Account/Reauthenticate", new { returnUrl });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!user.TwoFactorEnabled) return Forbid();

        GeneratedRecoveryCodes = await recoveryCodeService.GenerateAsync(user.Id, cancellationToken);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Pragma"] = "no-cache";
        return await LoadAsync(cancellationToken);
    }

    private async Task<IActionResult> LoadAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        Email = user.Email ?? string.Empty;
        EmailConfirmed = user.EmailConfirmed;
        TwoFactorEnabled = user.TwoFactorEnabled;
        IsAdministrator = await userManager.IsInRoleAsync(user, "Administrator");
        RecoveryCodesRemaining = await identityDb.MfaRecoveryCodes
            .AsNoTracking()
            .CountAsync(code => code.UserId == user.Id && code.UsedAtUtc == null, cancellationToken);
        return Page();
    }
}
