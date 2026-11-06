using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace EnergyManagement.Api.Modules.Identity;

public sealed class AdministratorMfaRequirement : IAuthorizationRequirement
{
}

public sealed class AdministratorMfaAuthorizationHandler(
    UserManager<ApplicationUser> userManager)
    : AuthorizationHandler<AdministratorMfaRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdministratorMfaRequirement requirement)
    {
        var isAdministrator = context.User.Claims.Any(claim =>
            (claim.Type == ClaimTypes.Role || claim.Type == "role") &&
            string.Equals(claim.Value, "Administrator", StringComparison.OrdinalIgnoreCase));

        if (!isAdministrator)
        {
            context.Succeed(requirement);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var user = await userManager.FindByIdAsync(userId);
        if (user?.TwoFactorEnabled == true)
            context.Succeed(requirement);
    }
}
