using System.ComponentModel.DataAnnotations;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class AcceptInvitationModel(
    HouseholdInvitationService invitationService,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty]
    public InvitationInput Input { get; set; } = new();

    [BindProperty]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;

    public HouseholdInvitationDetails? Invitation { get; private set; }
    public bool ExistingAccount { get; private set; }
    public bool CanAcceptAsSignedInUser { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return NotFound();
        Input.Token = token;
        Invitation = await invitationService.GetInvitationDetailsAsync(token, cancellationToken);
        if (Invitation is null) return NotFound();

        ExistingAccount = await userManager.FindByEmailAsync(Invitation.Email) is not null;
        if (User.Identity?.IsAuthenticated == true)
        {
            var signedInUser = await userManager.GetUserAsync(User);
            if (signedInUser is null) return Challenge();
            if (!string.Equals(
                userManager.NormalizeEmail(signedInUser.Email),
                userManager.NormalizeEmail(Invitation.Email),
                StringComparison.Ordinal))
                return Forbid();
            CanAcceptAsSignedInUser = true;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Input.Token)) return NotFound();
        Invitation = await invitationService.GetInvitationDetailsAsync(Input.Token, cancellationToken);
        if (Invitation is null) return NotFound();

        var signedInUser = User.Identity?.IsAuthenticated == true
            ? await userManager.GetUserAsync(User)
            : null;

        if (signedInUser is not null)
        {
            try
            {
                await invitationService.AcceptForExistingUserAsync(Input.Token, signedInUser, cancellationToken);
                await signInManager.RefreshSignInAsync(signedInUser);
                TempData["StatusMessage"] = "You have joined the household.";
                return RedirectToPage("/Index");
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or UnauthorizedAccessException)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                CanAcceptAsSignedInUser = true;
                return Page();
            }
        }

        if (await userManager.FindByEmailAsync(Invitation.Email) is not null)
        {
            var returnUrl = Url.Page("/Account/AcceptInvitation", values: new { token = Input.Token });
            return RedirectToPage("/Account/Login", new { returnUrl });
        }

        if (string.IsNullOrWhiteSpace(Password) || Password.Length < 12)
            ModelState.AddModelError(nameof(Password), "Use a password with at least 12 characters.");
        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
            ModelState.AddModelError(nameof(ConfirmPassword), "The passwords do not match.");
        if (!ModelState.IsValid) return Page();

        try
        {
            var newUser = await invitationService.AcceptForNewUserAsync(
                Input.Token, Password, cancellationToken);
            await signInManager.SignInAsync(newUser, isPersistent: false);
            TempData["StatusMessage"] = "Your account is ready and you have joined the household.";
            return RedirectToPage("/Index");
        }
        catch (Exception exception) when (exception is InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public sealed class InvitationInput
    {
        [Required]
        public string Token { get; set; } = string.Empty;
    }
}
