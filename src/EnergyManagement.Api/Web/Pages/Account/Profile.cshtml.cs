using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class ProfileModel(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext identityDb) : PageModel
{
    public string Email { get; private set; } = string.Empty;
    public bool EmailConfirmed { get; private set; }
    public bool TwoFactorEnabled { get; private set; }
    public bool IsAdministrator { get; private set; }
    public int RecoveryCodesRemaining { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Challenge();

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
