namespace EnergyManagement.Api.Modules.Devices;

public sealed class DeviceProvisioningCode
{
    private DeviceProvisioningCode() { }

    public DeviceProvisioningCode(
        int deviceId,
        string codeHash,
        int createdByUserId,
        DateTimeOffset expiresAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deviceId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(createdByUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);

        DeviceId = deviceId;
        CodeHash = codeHash;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        ExpiresAtUtc = expiresAtUtc;
    }

    public long Id { get; private set; }
    public int DeviceId { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public int CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? UsedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
}

public sealed class DeviceCredential
{
    private DeviceCredential() { }

    public DeviceCredential(
        int deviceId,
        string secretHash,
        string protectedSigningKey)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSigningKey);

        DeviceId = deviceId;
        SecretHash = secretHash;
        ProtectedSigningKey = protectedSigningKey;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public long Id { get; private set; }
    public int DeviceId { get; private set; }
    public string SecretHash { get; private set; } = string.Empty;
    public string ProtectedSigningKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public void Revoke(DateTimeOffset revokedAtUtc) => RevokedAtUtc = revokedAtUtc;
}

public sealed class DeviceRequestNonce
{
    private DeviceRequestNonce() { }

    public DeviceRequestNonce(int deviceId, string nonce, DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        DeviceId = deviceId;
        Nonce = nonce;
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }
    public int DeviceId { get; private set; }
    public string Nonce { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
