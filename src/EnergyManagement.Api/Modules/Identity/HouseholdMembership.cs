namespace EnergyManagement.Api.Modules.Identity;

public enum HouseholdRole
{
    Administrator,
    HouseholdMember,
    ReadOnlyViewer
}

public sealed class HouseholdMembership
{
    private HouseholdMembership() { }

    public HouseholdMembership(int userId, int householdId, HouseholdRole role)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(householdId);
        UserId = userId;
        HouseholdId = householdId;
        Role = role.ToString();
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; }
    public int UserId { get; private set; }
    public int HouseholdId { get; private set; }
    public string Role { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool HasRole(HouseholdRole role) =>
        string.Equals(Role, role.ToString(), StringComparison.Ordinal);
}
