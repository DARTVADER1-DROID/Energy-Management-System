using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class TwoFactorLoginModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Account/Login");

    public IActionResult OnPost() => RedirectToPage("/Account/Login");
}
