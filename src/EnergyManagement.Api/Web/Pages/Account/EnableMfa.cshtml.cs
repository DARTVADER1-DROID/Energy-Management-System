using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EnergyManagement.Api.Web.Pages.Account;

public sealed class EnableMfaModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Account/Profile");

    public IActionResult OnPost() => RedirectToPage("/Account/Profile");
}
