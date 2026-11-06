using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class TwoFactorLoginModel(
    SignInManager<ApplicationUser> signInManager,
    MfaRecoveryCodeService recoveryCodeService) : PageModel
{
    [BindProperty]
    public TwoFactorInput Input { get; set; } = new();

    public string? ReturnUrl { get; private set; }

    public void OnGet(string? returnUrl = null) =>
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        if (!ModelState.IsValid) return Page();

        if (!string.IsNullOrWhiteSpace(Input.RecoveryCode))
        {
            var pendingUser = await signInManager.GetTwoFactorAuthenticationUserAsync();
            if (pendingUser is not null &&
                await recoveryCodeService.RedeemAsync(pendingUser.Id, Input.RecoveryCode))
            {
                await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
                HttpContext.Items["ems_recent_auth_verified"] = true;
                await signInManager.SignInAsync(pendingUser, isPersistent: false);
                return LocalRedirect(ReturnUrl);
            }
        }
        else
        {
            HttpContext.Items["ems_recent_auth_verified"] = true;
            var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
                Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty),
                isPersistent: false,
                rememberClient: false);
            if (result.Succeeded) return LocalRedirect(ReturnUrl);
        }

        ModelState.AddModelError(string.Empty, "The authenticator or recovery code is invalid.");
        return Page();
    }

    public sealed class TwoFactorInput
    {
        [StringLength(10)]
        public string Code { get; set; } = string.Empty;

        [StringLength(40)]
        public string RecoveryCode { get; set; } = string.Empty;
    }
}
