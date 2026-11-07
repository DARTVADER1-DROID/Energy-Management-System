using System.Data;
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

        if (!await householdAccess.IsMemberAsync(
            administratorUserId, device.HouseholdId, cancellationToken))
            throw new KeyNotFoundException("The selected device was not found.");

        if (!await householdAccess.IsAdministratorAsync(
            administratorUserId, device.HouseholdId, cancellationToken))
            throw new UnauthorizedAccessException("Only a household administrator can provision this device.");

        if (!device.IsActive)
            throw new InvalidOperationException("A deactivated device cannot be provisioned.");

        if (await securityDb.Credentials.AnyAsync(
            item => item.DeviceId == device.Id && item.RevokedAtUtc == null, cancellationToken))
            throw new InvalidOperationException("This device already has an active credential. Use the credential rotation workflow instead.");

        var encryptionKey = GetEncryptionKey();
        CryptographicOperations.ZeroMemory(encryptionKey);

        await using var transaction = await securityDb.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await securityDb.ProvisioningCodes
            .Where(code => code.DeviceId == device.Id && code.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(code => code.RevokedAtUtc, now), cancellationToken);

        var rawCode = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18));
        var codeHash = Hash(rawCode);
        var entity = new DeviceProvisioningCode(
            device.Id, codeHash, administratorUserId, now.Add(ProvisioningLifetime));

        securityDb.ProvisioningCodes.Add(entity);
        securityDb.AuditEvents.Add(new DeviceSecurityAuditEvent(
            device.Id, administratorUserId, "ProvisioningCodeCreated",
            $"Single-use provisioning code issued; expires at {entity.ExpiresAtUtc:O}."));
        try
        {
            await securityDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            securityDb.Entry(entity).State = EntityState.Detached;
            throw new InvalidOperationException(
                "A provisioning code was generated concurrently. Refresh the page and try again.");
        }
        catch (DbUpdateException exception) when (
            exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            securityDb.Entry(entity).State = EntityState.Detached;
            throw new InvalidOperationException(
                "Provisioning changed concurrently. Refresh the page and try again.");
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            securityDb.ChangeTracker.Clear();
            throw new InvalidOperationException(
                "Provisioning changed concurrently. Refresh the page and try again.");
        }

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
        await using var transaction = await securityDb.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var code = await securityDb.ProvisioningCodes.AsNoTracking().SingleOrDefaultAsync(item =>
            item.DeviceId == device.Id && item.CodeHash == codeHash &&
            item.RevokedAtUtc == null && item.ExpiresAtUtc > now,
            cancellationToken);
        if (code is null)
            throw new InvalidOperationException("The provisioning request is invalid or expired.");

        // A consumed code remains a narrowly scoped recovery token until its original
        // ten-minute expiry. This makes a lost HTTP response or ESP32 power loss
        // recoverable without creating another credential or revealing any other key.
        if (code.UsedAtUtc.HasValue)
        {
            var currentCredential = await securityDb.Credentials.SingleOrDefaultAsync(item =>
                item.DeviceId == device.Id && !item.IsPendingRotation &&
                item.RevokedAtUtc == null && item.CreatedAtUtc >= code.UsedAtUtc.Value,
                cancellationToken);
            if (currentCredential is null)
                throw new InvalidOperationException("The provisioning request is invalid or expired.");

            var recoveryKey = GetEncryptionKey();
            byte[] recoveredSecret;
            try
            {
                recoveredSecret = Unprotect(currentCredential.ProtectedSigningKey, recoveryKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(recoveryKey);
            }

            try
            {
                var expectedHash = Convert.FromHexString(currentCredential.SecretHash);
                var actualHash = SHA256.HashData(recoveredSecret);
                if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                    throw new InvalidOperationException("The provisioning request is invalid or expired.");

                securityDb.AuditEvents.Add(new DeviceSecurityAuditEvent(
                    device.Id, null, "DeviceProvisioningRecovered",
                    "Device recovered its current credential using the original provisioning code within its validity window."));
                await securityDb.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new DeviceProvisioningResult(
                    deviceCode, WebEncoders.Base64UrlEncode(recoveredSecret),
                    "HMAC-SHA256", "X-EMS-Timestamp", "X-EMS-Nonce", "X-EMS-Signature");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(recoveredSecret);
            }
        }

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
        var encryptionKey = GetEncryptionKey();
        string protectedSecret;
        try
        {
            protectedSecret = Protect(secretBytes, encryptionKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(secretBytes);
        }

        securityDb.Credentials.Add(new DeviceCredential(device.Id, secretHash, protectedSecret));
        securityDb.AuditEvents.Add(new DeviceSecurityAuditEvent(
            device.Id, null, "DeviceProvisioned",
            "Device exchanged a single-use provisioning code for its initial credential."));
        await securityDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Device {DeviceCode} completed initial credential provisioning.", deviceCode);
        return new DeviceProvisioningResult(
            deviceCode, secret, "HMAC-SHA256", "X-EMS-Timestamp", "X-EMS-Nonce", "X-EMS-Signature");
    }

    public async Task<int> RevokeCredentialsAsync(
        string deviceCode,
        int administratorUserId,
        HouseholdAccessService householdAccess,
        CancellationToken cancellationToken = default)
    {
        var device = await energyDb.Devices.SingleOrDefaultAsync(
            item => item.DeviceCode == deviceCode, cancellationToken)
            ?? throw new KeyNotFoundException("The selected device was not found.");

        if (!await householdAccess.IsMemberAsync(
            administratorUserId, device.HouseholdId, cancellationToken))
            throw new KeyNotFoundException("The selected device was not found.");

        if (!await householdAccess.IsAdministratorAsync(
            administratorUserId, device.HouseholdId, cancellationToken))
            throw new UnauthorizedAccessException("Only a household administrator can revoke device credentials.");

        await using var transaction = await securityDb.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var revokedCount = await securityDb.Credentials
            .Where(item => item.DeviceId == device.Id && item.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(item => item.RevokedAtUtc, now), cancellationToken);

        await securityDb.ProvisioningCodes
            .Where(item => item.DeviceId == device.Id && item.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(item => item.RevokedAtUtc, now), cancellationToken);

        securityDb.AuditEvents.Add(new DeviceSecurityAuditEvent(
            device.Id, administratorUserId, "CredentialsRevoked",
            $"{revokedCount} active credential(s) and active provisioning recovery code(s) revoked."));
        await securityDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogWarning(
            "Administrator {AdministratorUserId} revoked {CredentialCount} credential(s) for device {DeviceCode}.",
            administratorUserId, revokedCount, deviceCode);
        return revokedCount;
    }

    public Task<bool> HasActiveCredentialAsync(
        int deviceId, CancellationToken cancellationToken = default) =>
        securityDb.Credentials.AsNoTracking().AnyAsync(item =>
            item.DeviceId == deviceId && !item.IsPendingRotation &&
            item.RevokedAtUtc == null, cancellationToken);

    public async Task<DeviceCredentialRotationResult> CreateCredentialRotationAsync(
        string deviceCode,
        int administratorUserId,
        HouseholdAccessService householdAccess,
        CancellationToken cancellationToken = default)
    {
        var device = await energyDb.Devices.SingleOrDefaultAsync(
            item => item.DeviceCode == deviceCode, cancellationToken)
            ?? throw new KeyNotFoundException("The selected device was not found.");

        if (!await householdAccess.IsMemberAsync(
            administratorUserId, device.HouseholdId, cancellationToken))
            throw new KeyNotFoundException("The selected device was not found.");

        if (!await householdAccess.IsAdministratorAsync(
            administratorUserId, device.HouseholdId, cancellationToken))
            throw new UnauthorizedAccessException("Only a household administrator can rotate device credentials.");

        if (!device.IsActive)
            throw new InvalidOperationException("A deactivated device cannot rotate credentials.");

        await using var transaction = await securityDb.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await securityDb.ProvisioningCodes
            .Where(item => item.DeviceId == device.Id && item.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(item => item.RevokedAtUtc, now), cancellationToken);
        await securityDb.Credentials
            .Where(item => item.DeviceId == device.Id &&
                item.IsPendingRotation && item.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(item => item.RevokedAtUtc, now), cancellationToken);

        var currentCredentials = await securityDb.Credentials
            .Where(item => item.DeviceId == device.Id &&
                !item.IsPendingRotation && item.RevokedAtUtc == null)
            .ToArrayAsync(cancellationToken);
        if (currentCredentials.Length != 1)
            throw new InvalidOperationException(
                currentCredentials.Length == 0
                    ? "This device has not been provisioned yet."
                    : "The device has an inconsistent credential state. Revoke credentials and provision it again.");

        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = WebEncoders.Base64UrlEncode(secretBytes);
        var secretHash = Convert.ToHexString(SHA256.HashData(secretBytes));
        var encryptionKey = GetEncryptionKey();
        string protectedSecret;
        try
        {
            protectedSecret = Protect(secretBytes, encryptionKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(secretBytes);
        }

        var expiresAtUtc = now.AddHours(24);
        var pendingCredential = new DeviceCredential(
            device.Id, secretHash, protectedSecret,
            isPendingRotation: true, rotationExpiresAtUtc: expiresAtUtc);
        securityDb.Credentials.Add(pendingCredential);
        securityDb.AuditEvents.Add(new DeviceSecurityAuditEvent(
            device.Id, administratorUserId, "CredentialRotationStarted",
            $"Replacement credential created; expires at {expiresAtUtc:O}."));
        try
        {
            await securityDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            securityDb.Entry(pendingCredential).State = EntityState.Detached;
            throw new InvalidOperationException(
                "A credential rotation was started concurrently. Refresh the page and try again.");
        }
        catch (DbUpdateException exception) when (
            exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            securityDb.Entry(pendingCredential).State = EntityState.Detached;
            throw new InvalidOperationException(
                "Credential rotation changed concurrently. Refresh the page and try again.");
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            securityDb.ChangeTracker.Clear();
            throw new InvalidOperationException(
                "Credential rotation changed concurrently. Refresh the page and try again.");
        }

        logger.LogInformation(
            "Credential rotation was initiated for device {DeviceCode}.", deviceCode);
        return new DeviceCredentialRotationResult(
            deviceCode, secret, "HMAC-SHA256", expiresAtUtc);
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

        var credentials = await securityDb.Credentials.AsNoTracking()
            .Where(item => item.DeviceId == device.Id && item.RevokedAtUtc == null &&
                (!item.IsPendingRotation || item.RotationExpiresAtUtc > now))
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);

        DeviceCredential? matchedCredential = null;
        foreach (var credential in credentials)
        {
            byte[] signingSecret;
            var encryptionKey = GetEncryptionKey();
            try
            {
                signingSecret = Unprotect(credential.ProtectedSigningKey, encryptionKey);
            }
            catch (CryptographicException exception)
            {
                logger.LogError(exception,
                    "Unable to unprotect a signing key for device {DeviceCode}.", deviceCode);
                continue;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encryptionKey);
            }

            var actualSecretHash = SHA256.HashData(signingSecret);
            byte[] storedSecretHash;
            try
            {
                storedSecretHash = Convert.FromHexString(credential.SecretHash);
            }
            catch (FormatException)
            {
                CryptographicOperations.ZeroMemory(signingSecret);
                continue;
            }

            if (!CryptographicOperations.FixedTimeEquals(actualSecretHash, storedSecretHash))
            {
                CryptographicOperations.ZeroMemory(signingSecret);
                continue;
            }

            var expectedSignature = HMACSHA256.HashData(
                signingSecret, Encoding.UTF8.GetBytes(canonicalRequest));
            CryptographicOperations.ZeroMemory(signingSecret);
            if (CryptographicOperations.FixedTimeEquals(expectedSignature, suppliedSignature))
            {
                matchedCredential = credential;
                break;
            }
        }

        if (matchedCredential is null)
            return null;

        await using var transaction = await securityDb.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        // The signature was checked against a snapshot read above. Lock the active
        // credential rows and revalidate before accepting the nonce so a concurrent
        // administrator revocation or completed key rotation cannot authorize a
        // request with a credential that has since become inactive.
        var activeCredentials = await securityDb.Credentials
            .FromSqlInterpolated($"""
                SELECT * FROM device_credentials
                WHERE "DeviceId" = {device.Id} AND "RevokedAtUtc" IS NULL
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var lockedCredential = activeCredentials.SingleOrDefault(item =>
            item.Id == matchedCredential.Id &&
            (!item.IsPendingRotation || item.RotationExpiresAtUtc > now));
        if (lockedCredential is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

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
            await transaction.RollbackAsync(cancellationToken);
            securityDb.ChangeTracker.Clear();
            return null;
        }

        if (lockedCredential.IsPendingRotation)
        {
            // Revoke the old current key before promoting the pending key so the
            // unique current-credential index remains valid throughout the transaction.
            await securityDb.Credentials
                .Where(item => item.DeviceId == device.Id &&
                    item.Id != matchedCredential.Id && item.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(item => item.RevokedAtUtc, now), cancellationToken);

            var confirmed = await securityDb.Credentials
                .Where(item => item.Id == matchedCredential.Id &&
                    item.DeviceId == device.Id && item.IsPendingRotation &&
                    item.RevokedAtUtc == null && item.RotationExpiresAtUtc > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.IsPendingRotation, false)
                    .SetProperty(item => item.ConfirmedAtUtc, now), cancellationToken);

            if (confirmed != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                securityDb.ChangeTracker.Clear();
                return null;
            }

            securityDb.AuditEvents.Add(new DeviceSecurityAuditEvent(
                device.Id, null, "CredentialRotationConfirmed",
                "Replacement credential confirmed; previous credentials were revoked."));
            await securityDb.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Device {DeviceCode} confirmed its rotated credential; previous credentials were revoked.",
                deviceCode);
        }

        await transaction.CommitAsync(cancellationToken);
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

public sealed record DeviceCredentialRotationResult(
    string DeviceCode,
    string NewDeviceSecret,
    string SignatureAlgorithm,
    DateTimeOffset ExpiresAtUtc);
