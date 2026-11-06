namespace EnergyManagement.Api.Modules.Identity;

public sealed class MfaRecoveryCode
{
    private MfaRecoveryCode() { }

    public MfaRecoveryCode(int userId, string codeHash)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        UserId = userId;
        CodeHash = codeHash;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; }
    public int UserId { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UsedAtUtc { get; private set; }
}
