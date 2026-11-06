namespace EnergyManagement.Api.Modules.Identity;

public sealed class HouseholdInvitation
{
    private HouseholdInvitation() { }

    public HouseholdInvitation(int householdId, string email, string normalizedEmail,
        HouseholdRole role, string tokenHash, int createdByUserId, DateTimeOffset expiresAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(householdId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(createdByUserId);
        HouseholdId = householdId;
        Email = email;
        NormalizedEmail = normalizedEmail;
        Role = role.ToString();
        TokenHash = tokenHash;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        ExpiresAtUtc = expiresAtUtc;
    }

    public int Id { get; private set; }
    public int HouseholdId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public int CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
}
