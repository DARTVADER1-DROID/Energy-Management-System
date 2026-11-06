using System.ComponentModel.DataAnnotations;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class EnableMfaModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    MfaRecoveryCodeService recoveryCodeService) : PageModel
{
    [BindProperty]
    public MfaInput Input { get; set; } = new();

    public string AuthenticatorKey { get; private set; } = string.Empty;
    public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        SetNoStoreHeaders();
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToPage("/Account/Login");

        if (!User.HasRecentAuthentication())
        {
            var returnUrl = Url.Page("/Account/EnableMfa");
            return RedirectToPage("/Account/Reauthenticate", new { returnUrl });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login");

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        AuthenticatorKey = FormatKey(key ?? string.Empty);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        SetNoStoreHeaders();
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToPage("/Account/Login");

        if (!User.HasRecentAuthentication())
        {
            var returnUrl = Url.Page("/Account/EnableMfa");
            return RedirectToPage("/Account/Reauthenticate", new { returnUrl });
        }

        if (!ModelState.IsValid)
            return await OnGetAsync();

        var user = await userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login");

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        AuthenticatorKey = FormatKey(key ?? string.Empty);

        var code = Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, code);

        if (!valid)
        {
            ModelState.AddModelError(nameof(Input.Code), "The authenticator code is not valid. Check your device time and try again.");
            return Page();
        }

        var enableResult = await userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enableResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "MFA could not be enabled. Try again.");
            return Page();
        }

        HttpContext.Items["ems_recent_auth_verified"] = true;
        await signInManager.RefreshSignInAsync(user);
        RecoveryCodes = await recoveryCodeService.GenerateAsync(user.Id);
        return Page();
    }

    private void SetNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
    }

    private static string FormatKey(string key) =>
        string.Join(" ", Enumerable.Range(0, (key.Length + 3) / 4)
            .Select(index => key.Substring(index * 4, Math.Min(4, key.Length - index * 4))));

    public sealed class MfaInput
    {
        [Required, StringLength(10, MinimumLength = 6)]
        public string Code { get; set; } = string.Empty;
    }
}
