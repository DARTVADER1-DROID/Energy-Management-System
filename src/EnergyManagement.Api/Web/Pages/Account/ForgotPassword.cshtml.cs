using System.ComponentModel.DataAnnotations;
using System.Text;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class ForgotPasswordModel(
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<ForgotPasswordModel> logger) : PageModel
{
    [BindProperty]
    public ForgotPasswordInput Input { get; set; } = new();

    public bool RequestSubmitted { get; private set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();

        var user = await userManager.FindByEmailAsync(Input.Email.Trim());
        if (user is not null && user.EmailConfirmed)
        {
            try
            {
                var publicBaseUrl = configuration["EMS_PUBLIC_BASE_URL"];
                if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var baseUri) ||
                    baseUri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException(
                        "EMS_PUBLIC_BASE_URL must be an absolute HTTPS URL.");

                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var relativeUrl = Url.Page("/Account/ResetPassword", values: new
                {
                    email = user.Email,
                    code
                });
                var resetUrl = new Uri(baseUri.ToString().TrimEnd('/') + "/" +
                    relativeUrl!.TrimStart('/'));

                await emailSender.SendAsync(
                    user.Email!,
                    "Reset your Energy Management System password",
                    $"Use this one-time link to reset your password: {resetUrl}",
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Password reset email could not be sent.");
            }
        }

        // Always return the same result to avoid disclosing account existence.
        RequestSubmitted = true;
        return Page();
    }

    public sealed class ForgotPasswordInput
    {
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;
    }
}
