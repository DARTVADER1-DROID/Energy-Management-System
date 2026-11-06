using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class TwoFactorLoginModel(SignInManager<EnergyManagement.Api.Modules.Identity.ApplicationUser> signInManager) : PageModel
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

        var result = string.IsNullOrWhiteSpace(Input.RecoveryCode)
            ? await signInManager.TwoFactorAuthenticatorSignInAsync(
                Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty),
                isPersistent: false,
                rememberClient: false)
            : await signInManager.TwoFactorRecoveryCodeSignInAsync(
                Input.RecoveryCode.Replace(" ", string.Empty));

        if (result.Succeeded) return LocalRedirect(ReturnUrl);

        ModelState.AddModelError(string.Empty, "The authenticator or recovery code is invalid.");
        return Page();
    }

    public sealed class TwoFactorInput
    {
        [StringLength(10)]
        public string Code { get; set; } = string.Empty;

        [StringLength(32)]
        public string RecoveryCode { get; set; } = string.Empty;
    }
}
