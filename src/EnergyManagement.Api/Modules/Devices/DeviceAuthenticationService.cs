using System.Security.Cryptography;
using System.Text;
using EnergyManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;

namespace EnergyManagement.Api.Modules.Devices;

public sealed class DeviceAuthenticationService(
    DeviceSecurityDbContext securityDb,
    EnergyManagementDbContext energyDb,
    IConfiguration configuration,
    ILogger<DeviceAuthenticationService> logger)
{
    private static readonly TimeSpan ProvisioningLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaximumRequestAge = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan NonceRetention = TimeSpan.FromMinutes(10);
    private const int MaximumBodyBytes = 1024 * 1024;

    public async Task<ProvisioningCodeResult> CreateProvisioningCodeAsync(
        string deviceCode,
        int administratorUserId,
        HouseholdAccessService householdAccess,
        CancellationToken cancellationToken = default)
    {
        var device = await energyDb.Devices.SingleOrDefaultAsync(
            item => item.DeviceCode == deviceCode, cancellationToken)
            ?? throw new KeyNotFoundException("The selected device was not found.");

        if (!device.IsActive)
            throw new InvalidOperationException("A deactivated device cannot be provisioned.");

        if (!await householdAccess.IsAdministratorAsync(
            administratorUserId, device.HouseholdId, cancellationToken))
            throw new UnauthorizedAccessException("Only a household administrator can provision this device.");

        var now = DateTimeOffset.UtcNow;
        await securityDb.ProvisioningCodes
            .Where(code => code.DeviceId == device.Id && code.UsedAtUtc == null && code.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(code => code.RevokedAtUtc, now), cancellationToken);

        var rawCode = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18));
        var codeHash = Hash(rawCode);
        var entity = new DeviceProvisioningCode(
            device.Id, codeHash, administratorUserId, now.Add(ProvisioningLifetime));

        securityDb.ProvisioningCodes.Add(entity);
        await securityDb.SaveChangesAsync(cancellationToken);
        return new ProvisioningCodeResult(device.DeviceCode, rawCode, entity.ExpiresAtUtc);
    }

    public async Task<DeviceProvisioningResult> ExchangeProvisioningCodeAsync(
        string deviceCode,
        string provisioningCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode) || deviceCode.Length > 32 ||
            string.IsNullOrWhiteSpace(provisioningCode) || provisioningCode.Length > 128)
            throw new InvalidOperationException("The provisioning request is invalid or expired.");

        var device = await energyDb.Devices.SingleOrDefaultAsync(
            item => item.DeviceCode == deviceCode, cancellationToken)
            ?? throw new InvalidOperationException("The provisioning request is invalid or expired.");
        if (!device.IsActive)
            throw new InvalidOperationException("The provisioning request is invalid or expired.");

        var now = DateTimeOffset.UtcNow;
        var codeHash = Hash(provisioningCode);
        await using var transaction = await securityDb.Database.BeginTransactionAsync(cancellationToken);

        var code = await securityDb.ProvisioningCodes.SingleOrDefaultAsync(item =>
            item.DeviceId == device.Id && item.CodeHash == codeHash &&
            item.UsedAtUtc == null && item.RevokedAtUtc == null && item.ExpiresAtUtc > now,
            cancellationToken);
        if (code is null)
            throw new InvalidOperationException("The provisioning request is invalid or expired.");

        if (await securityDb.Credentials.AnyAsync(
            item => item.DeviceId == device.Id && item.RevokedAtUtc == null, cancellationToken))
            throw new InvalidOperationException("This device already has an active credential. Ask an administrator to rotate it.");

        var consumed = await securityDb.ProvisioningCodes
            .Where(item => item.Id == code.Id && item.UsedAtUtc == null &&
                item.RevokedAtUtc == null && item.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(item => item.UsedAtUtc, now), cancellationToken);
        if (consumed != 1)
            throw new InvalidOperationException("The provisioning request is invalid or expired.");

        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = WebEncoders.Base64UrlEncode(secretBytes);
        var secretHash = Convert.ToHexString(SHA256.HashData(secretBytes));
        var protectedSecret = Protect(secretBytes, GetEncryptionKey());

        securityDb.Credentials.Add(new DeviceCredential(device.Id, secretHash, protectedSecret));
        await securityDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Device {DeviceCode} completed initial credential provisioning.", deviceCode);
        return new DeviceProvisioningResult(
            deviceCode, secret, "HMAC-SHA256", "X-EMS-Timestamp", "X-EMS-Nonce", "X-EMS-Signature");
    }

    public async Task<Device?> AuthenticateRequestAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var deviceCode = context.Request.Headers["X-EMS-Device"].ToString();
        var timestampValue = context.Request.Headers["X-EMS-Timestamp"].ToString();
        var nonce = context.Request.Headers["X-EMS-Nonce"].ToString();
        var signatureValue = context.Request.Headers["X-EMS-Signature"].ToString();

        if (string.IsNullOrWhiteSpace(deviceCode) || deviceCode.Length > 32 ||
            !long.TryParse(timestampValue, out var unixTimestamp) ||
            string.IsNullOrWhiteSpace(nonce) || nonce.Length is < 16 or > 64 ||
            nonce.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) ||
            signatureValue.Length != 64)
            return null;

        DateTimeOffset requestTime;
        try
        {
            requestTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if ((now - requestTime).Duration() > MaximumRequestAge)
            return null;

        var device = await energyDb.Devices.SingleOrDefaultAsync(
            item => item.DeviceCode == deviceCode && item.IsActive, cancellationToken);
        if (device is null)
            return null;

        var credential = await securityDb.Credentials
            .Where(item => item.DeviceId == device.Id && item.RevokedAtUtc == null)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (credential is null)
            return null;

        byte[] suppliedSignature;
        try
        {
            suppliedSignature = Convert.FromHexString(signatureValue);
        }
        catch (FormatException)
        {
            return null;
        }

        context.Request.EnableBuffering();
        if (context.Request.ContentLength is > MaximumBodyBytes)
            return null;

        using var bodyHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        var totalBytes = 0;
        int bytesRead;
        while ((bytesRead = await context.Request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > MaximumBodyBytes)
            {
                context.Request.Body.Position = 0;
                return null;
            }
            bodyHash.AppendData(buffer, 0, bytesRead);
        }
        context.Request.Body.Position = 0;

        var bodyHashHex = Convert.ToHexString(bodyHash.GetHashAndReset());
        var requestPath = context.Request.Path + context.Request.QueryString;
        var canonicalRequest = string.Join("\n",
            context.Request.Method.ToUpperInvariant(),
            requestPath,
            timestampValue,
            nonce,
            bodyHashHex);

        byte[] signingSecret;
        try
        {
            signingSecret = Unprotect(credential.ProtectedSigningKey, GetEncryptionKey());
        }
        catch (CryptographicException exception)
        {
            logger.LogError(exception, "Unable to unprotect the signing key for device {DeviceCode}.", deviceCode);
            return null;
        }

        var expectedSignature = HMACSHA256.HashData(
            signingSecret, Encoding.UTF8.GetBytes(canonicalRequest));
        CryptographicOperations.ZeroMemory(signingSecret);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, suppliedSignature))
            return null;

        await securityDb.RequestNonces
            .Where(item => item.DeviceId == device.Id && item.CreatedAtUtc < now.Subtract(NonceRetention))
            .ExecuteDeleteAsync(cancellationToken);

        securityDb.RequestNonces.Add(new DeviceRequestNonce(device.Id, nonce, now));
        try
        {
            await securityDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            securityDb.ChangeTracker.Clear();
            return null;
        }

        device.UpdateLastSeen(now);
        await energyDb.SaveChangesAsync(cancellationToken);
        return device;
    }

    private byte[] GetEncryptionKey()
    {
        var configured = configuration["EMS_DEVICE_CREDENTIAL_ENCRYPTION_KEY"];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "Configure EMS_DEVICE_CREDENTIAL_ENCRYPTION_KEY as a base64-encoded 32-byte secret before provisioning devices.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "EMS_DEVICE_CREDENTIAL_ENCRYPTION_KEY must be valid base64.", exception);
        }

        if (key.Length != 32)
            throw new InvalidOperationException(
                "EMS_DEVICE_CREDENTIAL_ENCRYPTION_KEY must decode to exactly 32 bytes.");
        return key;
    }

    private static string Protect(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var envelope = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, envelope, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, envelope, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, envelope, nonce.Length + tag.Length, ciphertext.Length);
        return Convert.ToBase64String(envelope);
    }

    private static byte[] Unprotect(string protectedValue, byte[] key)
    {
        var envelope = Convert.FromBase64String(protectedValue);
        if (envelope.Length < 28)
            throw new CryptographicException("The protected device credential is malformed.");

        var nonce = envelope.AsSpan(0, 12);
        var tag = envelope.AsSpan(12, 16);
        var ciphertext = envelope.AsSpan(28);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record ProvisioningCodeResult(
    string DeviceCode, string Code, DateTimeOffset ExpiresAtUtc);

public sealed record DeviceProvisioningResult(
    string DeviceCode,
    string DeviceSecret,
    string SignatureAlgorithm,
    string TimestampHeader,
    string NonceHeader,
    string SignatureHeader);
