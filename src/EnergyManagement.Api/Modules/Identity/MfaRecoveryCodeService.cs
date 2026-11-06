using System.Security.Cryptography;
using System.Text;
using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Modules.Identity;

public sealed class MfaRecoveryCodeService(IdentityDbContext identityDb)
{
    private const int RecoveryCodeCount = 10;

    public async Task<IReadOnlyList<string>> GenerateAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var codes = Enumerable.Range(0, RecoveryCodeCount)
            .Select(_ => FormatCode(Convert.ToHexString(RandomNumberGenerator.GetBytes(16))))
            .ToArray();

        await using var transaction = await identityDb.Database.BeginTransactionAsync(cancellationToken);
        await identityDb.MfaRecoveryCodes
            .Where(code => code.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        identityDb.MfaRecoveryCodes.AddRange(codes.Select(code =>
            new MfaRecoveryCode(userId, HashCode(code))));

        await identityDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return codes;
    }

    public async Task<bool> RedeemAsync(
        int userId,
        string suppliedCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(suppliedCode))
            return false;

        var hash = HashCode(suppliedCode);
        var now = DateTimeOffset.UtcNow;

        var updated = await identityDb.MfaRecoveryCodes
            .Where(code =>
                code.UserId == userId &&
                code.CodeHash == hash &&
                code.UsedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(code => code.UsedAtUtc, now),
                cancellationToken);

        return updated == 1;
    }

    private static string HashCode(string code)
    {
        var normalized = code.Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string FormatCode(string code) =>
        string.Join("-", Enumerable.Range(0, 4)
            .Select(index => code.Substring(index * 8, 8)));
}
