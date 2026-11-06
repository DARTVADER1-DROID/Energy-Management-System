using System.Security.Claims;

namespace EnergyManagement.Api.Modules.Identity;

public static class ClaimsPrincipalExtensions
{
    private static readonly TimeSpan RecentAuthenticationWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(1);

    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(identifier, out var userId)
            ? userId
            : throw new InvalidOperationException("The authenticated principal has no valid user identifier.");
    }

    public static bool HasRecentAuthentication(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("ems_recent_auth");
        if (!DateTimeOffset.TryParse(value, out var authenticatedAt))
            return false;

        var age = DateTimeOffset.UtcNow - authenticatedAt;
        return age >= -MaximumClockSkew && age <= RecentAuthenticationWindow;
    }
}
