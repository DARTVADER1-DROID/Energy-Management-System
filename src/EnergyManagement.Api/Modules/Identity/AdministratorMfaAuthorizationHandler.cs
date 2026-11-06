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
        if (!context.User.IsInRole("Administrator"))
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
