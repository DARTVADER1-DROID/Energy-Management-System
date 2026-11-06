using Microsoft.AspNetCore.Identity;

namespace EnergyManagement.Api.Modules.Identity;

public sealed class ApplicationUser : IdentityUser<int>
{
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
