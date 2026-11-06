using System.ComponentModel.DataAnnotations;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Households;

public sealed class CreateModel(
    DeviceRegistrationService deviceRegistrationService,
    HouseholdAccessService householdAccess) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IActionResult OnGet()
    {
        return User.IsInRole("Administrator") ? Page() : Forbid();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Administrator"))
            return Forbid();

        if (!User.HasRecentAuthentication())
        {
            var returnUrl = Url.Page("/Households/Create");
            return RedirectToPage("/Account/Reauthenticate", new { returnUrl });
        }

        if (!ModelState.IsValid)
            return Page();

        var household = await deviceRegistrationService.CreateHouseholdAsync(
            Input.Name.Trim(), cancellationToken);

        await householdAccess.AddMembershipAsync(
            User.GetUserId(),
            household.Id,
            HouseholdRole.Administrator,
            cancellationToken);

        TempData["StatusMessage"] = $"Household {household.HouseholdCode} was created.";
        return RedirectToPage("/Index", new { householdId = household.Id });
    }

    public sealed class InputModel
    {
        [Required]
        [StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;
    }
}
