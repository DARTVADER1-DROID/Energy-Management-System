using System.ComponentModel.DataAnnotations;
using System.Text;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class ResetPasswordModel(UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty]
    public ResetPasswordInput Input { get; set; } = new();

    public void OnGet(string? email, string? code)
    {
        Input.Email = email ?? string.Empty;
        Input.Code = code ?? string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code));
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "This password-reset link is invalid or has expired.");
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email.Trim());
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "This password-reset link is invalid or has expired.");
            return Page();
        }

        var result = await userManager.ResetPasswordAsync(user, token, Input.Password);
        if (result.Succeeded)
        {
            TempData["StatusMessage"] = "Your password has been reset. Sign in with your new password.";
            return RedirectToPage("/Account/Login");
        }

        ModelState.AddModelError(string.Empty, "This password-reset link is invalid or has expired, or the new password does not meet the security requirements.");
        return Page();
    }

    public sealed class ResetPasswordInput
    {
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Code { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), MinLength(12)]
        public string Password { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
