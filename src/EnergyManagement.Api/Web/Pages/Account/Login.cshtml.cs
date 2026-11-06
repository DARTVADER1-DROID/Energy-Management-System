using System.ComponentModel.DataAnnotations;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public string? ReturnUrl { get; private set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        if (!ModelState.IsValid) return Page();

        var result = await signInManager.PasswordSignInAsync(
            Input.Email.Trim(),
            Input.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await userManager.FindByEmailAsync(Input.Email.Trim());
            if (user is not null &&
                await userManager.IsInRoleAsync(user, "Administrator") &&
                !user.TwoFactorEnabled)
                return RedirectToPage("/Account/EnableMfa");

            return LocalRedirect(ReturnUrl);
        }

        if (result.RequiresTwoFactor)
            return RedirectToPage("/Account/TwoFactorLogin", new { returnUrl = ReturnUrl });

        ModelState.AddModelError(string.Empty, "The email or password is incorrect, or the account is not yet eligible to sign in.");
        return Page();
    }

    public sealed class LoginInput
    {
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
