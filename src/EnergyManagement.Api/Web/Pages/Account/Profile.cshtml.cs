using System.ComponentModel.DataAnnotations;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class ProfileModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [MinLength(12)]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [Compare(nameof(NewPassword))]
    [DataType(DataType.Password)]
    public string ConfirmNewPassword { get; set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;
    public bool EmailConfirmed { get; private set; }
    public bool IsAdministrator { get; private set; }

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        LoadAsync();

    public async Task<IActionResult> OnPostChangePasswordAsync(CancellationToken cancellationToken)
    {
        if (!User.HasRecentAuthentication())
        {
            var returnUrl = Url.Page("/Account/Profile");
            return RedirectToPage("/Account/Reauthenticate", new { returnUrl });
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var result = await userManager.ChangePasswordAsync(user, CurrentPassword, NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            await LoadAsync();
            return Page();
        }

        await signInManager.SignOutAsync();
        TempData["StatusMessage"] = "Your password was changed. Sign in again with the new password.";
        return RedirectToPage("/Account/Login");
    }

    private async Task<IActionResult> LoadAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        Email = user.Email ?? string.Empty;
        EmailConfirmed = user.EmailConfirmed;
        IsAdministrator = await userManager.IsInRoleAsync(user, "Administrator");
        return Page();
    }
}
