using System.Security.Claims;

namespace EnergyManagement.Api.Modules.Identity;

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(identifier, out var userId)
            ? userId
            : throw new InvalidOperationException("The authenticated principal has no valid user identifier.");
    }
}
