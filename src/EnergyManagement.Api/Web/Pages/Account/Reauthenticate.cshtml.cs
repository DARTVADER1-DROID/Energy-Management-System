using System.ComponentModel.DataAnnotations;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class ReauthenticateModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string ReturnUrl { get; private set; } = "/";

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        if (!ModelState.IsValid)
            return Page();

        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Challenge();

        var result = await signInManager.PasswordSignInAsync(
            user.UserName!, Password, isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
            return LocalRedirect(ReturnUrl);

        if (result.RequiresTwoFactor)
            return RedirectToPage("/Account/TwoFactorLogin", new { returnUrl = ReturnUrl });

        ModelState.AddModelError(string.Empty,
            "The password could not be verified. Check it and try again.");
        return Page();
    }
}
