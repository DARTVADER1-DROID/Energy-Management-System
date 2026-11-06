using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Identity;
using EnergyManagement.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace EnergyManagement.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class DeviceAuthenticationTests(PostgreSqlFixture postgres) : IAsyncLifetime
{
    private EnergyManagementApiFactory _factory = null!;
    private HttpClient _client = null!;
    private int _householdId;
    private string _deviceCode = string.Empty;

    public async Task InitializeAsync()
    {
        _factory = new EnergyManagementApiFactory(postgres.ConnectionString);
        _client = _factory.CreateClient();

        await using var scope = _factory.Services.CreateAsyncScope();
        var domainDb = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        await domainDb.Database.EnsureDeletedAsync();

        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var deviceSecurityDb = scope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
        await domainDb.Database.MigrateAsync();
        await identityDb.Database.MigrateAsync();
        await deviceSecurityDb.Database.MigrateAsync();

        identityDb.Users.AddRange(
            new ApplicationUser
            {
                Id = 1,
                UserName = "device-admin@example.test",
                Email = "device-admin@example.test",
                EmailConfirmed = true,
                TwoFactorEnabled = true
            },
            new ApplicationUser
            {
                Id = 2,
                UserName = "device-member@example.test",
                Email = "device-member@example.test",
                EmailConfirmed = true
            });
        await identityDb.SaveChangesAsync();

        var registration = scope.ServiceProvider.GetRequiredService<DeviceRegistrationService>();
        var household = await registration.CreateHouseholdAsync(
            "Device Authentication Home", CancellationToken.None);
        _householdId = household.Id;

        identityDb.HouseholdMemberships.AddRange(
            new HouseholdMembership(1, _householdId, HouseholdRole.Administrator),
            new HouseholdMembership(2, _householdId, HouseholdRole.HouseholdMember));
        await identityDb.SaveChangesAsync();

        var device = await registration.RegisterAsync(
            _householdId,
            new RegisterDeviceRequest(
                "Main Energy Monitor", "ESP32", "1.0.0", "Distribution Board",
                ElectricalConfiguration.SinglePhase, null, new DeviceThresholdsRequest()),
            CancellationToken.None);
        _deviceCode = device.DeviceCode;
    }

    [Fact]
    public async Task AdministratorCanProvisionDeviceAndExchangeCodeOnlyOnce()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DeviceAuthenticationService>();
        var access = scope.ServiceProvider.GetRequiredService<HouseholdAccessService>();

        var code = await service.CreateProvisioningCodeAsync(_deviceCode, 1, access);
        Assert.Equal(_deviceCode, code.DeviceCode);
        Assert.True(code.ExpiresAtUtc > DateTimeOffset.UtcNow);
        Assert.True(code.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(10));

        var result = await service.ExchangeProvisioningCodeAsync(_deviceCode, code.Code);
        Assert.Equal(_deviceCode, result.DeviceCode);
        Assert.Equal("HMAC-SHA256", result.SignatureAlgorithm);
        Assert.False(string.IsNullOrWhiteSpace(result.DeviceSecret));

        var securityDb = scope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
        var credential = await securityDb.Credentials.SingleAsync();
        Assert.NotEqual(result.DeviceSecret, credential.SecretHash);
        Assert.NotEqual(result.DeviceSecret, credential.ProtectedSigningKey);
        Assert.Equal(64, credential.SecretHash.Length);
        var initialAuditEvents = await securityDb.AuditEvents
            .OrderBy(item => item.CreatedAtUtc)
            .Select(item => item.EventType)
            .ToArrayAsync();
        Assert.Equal(
            new[] { "ProvisioningCodeCreated", "DeviceProvisioned" },
            initialAuditEvents);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExchangeProvisioningCodeAsync(_deviceCode, code.Code));
    }

    [Fact]
    public async Task ProvisioningEndpointsDoNotCacheOneTimeSecrets()
    {
        using var codeResponse = await _client.PostAsync(
            $"/api/devices/{_deviceCode}/provisioning-codes", content: null);

        Assert.Equal(HttpStatusCode.OK, codeResponse.StatusCode);
        Assert.True(codeResponse.Headers.CacheControl?.NoStore);
        var code = await codeResponse.Content.ReadFromJsonAsync<ProvisioningCodeResult>();
        Assert.NotNull(code);

        using var exchangeResponse = await _client.PostAsJsonAsync(
            "/api/device-auth/provision",
            new ProvisionDeviceRequest(_deviceCode, code.Code));

        Assert.Equal(HttpStatusCode.OK, exchangeResponse.StatusCode);
        Assert.True(exchangeResponse.Headers.CacheControl?.NoStore);
        var credentials = await exchangeResponse.Content.ReadFromJsonAsync<DeviceProvisioningResult>();
        Assert.NotNull(credentials);
        Assert.False(string.IsNullOrWhiteSpace(credentials.DeviceSecret));
    }

    [Fact]
    public async Task DatabasePreventsMultipleCurrentCredentialsForOneDevice()
    {
        await ProvisionDeviceAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var domainDb = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        var device = await domainDb.Devices.SingleAsync(item => item.DeviceCode == _deviceCode);
        var securityDb = scope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
        securityDb.Credentials.Add(new DeviceCredential(
            device.Id, new string('A', 64), Convert.ToBase64String(RandomNumberGenerator.GetBytes(60))));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => securityDb.SaveChangesAsync());
        Assert.Equal("23505", exception.GetBaseException() is Npgsql.PostgresException postgres
            ? postgres.SqlState
            : null);
    }

    [Fact]
    public async Task NonAdministratorCannotCreateDeviceProvisioningCode()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DeviceAuthenticationService>();
        var access = scope.ServiceProvider.GetRequiredService<HouseholdAccessService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateProvisioningCodeAsync(_deviceCode, 2, access));
    }

    [Fact]
    public async Task DeviceHeartbeatAcceptsValidHmacAndRejectsReplayedNonce()
    {
        var secret = await ProvisionDeviceAsync();
        var secretBytes = WebEncoders.Base64UrlDecode(secret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var signature = SignHeartbeat(secretBytes, timestamp, nonce);

        var firstResponse = await SendHeartbeatAsync(timestamp, nonce, signature);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var replayResponse = await SendHeartbeatAsync(timestamp, nonce, signature);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var securityDb = scope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
        Assert.Single(await securityDb.RequestNonces.ToArrayAsync());
    }

    [Fact]
    public async Task DeviceHeartbeatRejectsStaleTimestampAndInvalidSignature()
    {
        var secret = await ProvisionDeviceAsync();
        var secretBytes = WebEncoders.Base64UrlDecode(secret);
        var staleTimestamp = DateTimeOffset.UtcNow.AddMinutes(-3).ToUnixTimeSeconds().ToString();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var signature = SignHeartbeat(secretBytes, staleTimestamp, nonce);

        var staleResponse = await SendHeartbeatAsync(staleTimestamp, nonce, signature);
        Assert.Equal(HttpStatusCode.Unauthorized, staleResponse.StatusCode);

        var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var invalidSignature = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var invalidResponse = await SendHeartbeatAsync(
            currentTimestamp, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), invalidSignature);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task CredentialRotationKeepsOldKeyUntilNewKeyIsConfirmed()
    {
        var originalSecret = await ProvisionDeviceAsync();
        var originalKey = WebEncoders.Base64UrlDecode(originalSecret);

        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DeviceAuthenticationService>();
        var access = scope.ServiceProvider.GetRequiredService<HouseholdAccessService>();
        var rotation = await service.CreateCredentialRotationAsync(_deviceCode, 1, access);
        var replacementKey = WebEncoders.Base64UrlDecode(rotation.NewDeviceSecret);

        var originalTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var originalNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        using var originalResponse = await SendHeartbeatAsync(
            originalTimestamp, originalNonce,
            SignHeartbeat(originalKey, originalTimestamp, originalNonce));
        Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);

        var securityDb = scope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
        var duringRotation = await securityDb.Credentials.AsNoTracking()
            .Where(item => item.DeviceId > 0 && item.RevokedAtUtc == null)
            .ToArrayAsync();
        Assert.Equal(2, duringRotation.Length);
        Assert.Single(duringRotation, item => item.IsPendingRotation);

        var replacementTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var replacementNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        using var replacementResponse = await SendHeartbeatAsync(
            replacementTimestamp, replacementNonce,
            SignHeartbeat(replacementKey, replacementTimestamp, replacementNonce));
        Assert.Equal(HttpStatusCode.OK, replacementResponse.StatusCode);

        var afterConfirmation = await securityDb.Credentials.AsNoTracking()
            .Where(item => item.DeviceId > 0)
            .ToArrayAsync();
        Assert.Single(afterConfirmation, item => item.RevokedAtUtc == null);
        Assert.False(Assert.Single(afterConfirmation, item => item.RevokedAtUtc == null).IsPendingRotation);
        Assert.Contains(afterConfirmation, item => item.RevokedAtUtc != null && !item.IsPendingRotation);

        var oldTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var oldNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        using var oldKeyResponse = await SendHeartbeatAsync(
            oldTimestamp, oldNonce, SignHeartbeat(originalKey, oldTimestamp, oldNonce));
        Assert.Equal(HttpStatusCode.Unauthorized, oldKeyResponse.StatusCode);
    }

    [Fact]
    public async Task ExpiredCredentialRotationLeavesOriginalKeyUsable()
    {
        var originalSecret = await ProvisionDeviceAsync();
        var originalKey = WebEncoders.Base64UrlDecode(originalSecret);

        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DeviceAuthenticationService>();
        var access = scope.ServiceProvider.GetRequiredService<HouseholdAccessService>();
        var rotation = await service.CreateCredentialRotationAsync(_deviceCode, 1, access);
        var replacementKey = WebEncoders.Base64UrlDecode(rotation.NewDeviceSecret);

        var securityDb = scope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
        await securityDb.Credentials
            .Where(item => item.DeviceId > 0 && item.IsPendingRotation)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                item => item.RotationExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));

        var expiredTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var expiredNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        using var expiredResponse = await SendHeartbeatAsync(
            expiredTimestamp, expiredNonce,
            SignHeartbeat(replacementKey, expiredTimestamp, expiredNonce));
        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);

        var originalTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var originalNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        using var originalResponse = await SendHeartbeatAsync(
            originalTimestamp, originalNonce,
            SignHeartbeat(originalKey, originalTimestamp, originalNonce));
        Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);

        var activeOriginal = await securityDb.Credentials.AsNoTracking()
            .SingleAsync(item => item.DeviceId > 0 && !item.IsPendingRotation && item.RevokedAtUtc == null);
        Assert.False(activeOriginal.IsPendingRotation);
    }

    [Fact]
    public async Task HeartbeatRequestsAreRateLimitedPerDeviceAndIp()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var responses = new List<HttpStatusCode>();

        for (var index = 0; index < 61; index++)
        {
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var response = await SendHeartbeatAsync(
                timestamp, nonce, new string('0', 64));
            responses.Add(response.StatusCode);
            response.Dispose();
        }

        Assert.Equal(60, responses.Count(status => status == HttpStatusCode.Unauthorized));
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[^1]);
    }

    [Fact]
    public async Task RotationKeepsOldCredentialUntilDeviceAuthenticatesWithNewSecret()
    {
        var originalSecret = await ProvisionDeviceAsync();
        DeviceCredentialRotationResult rotation;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<DeviceAuthenticationService>();
            var access = scope.ServiceProvider.GetRequiredService<HouseholdAccessService>();
            rotation = await service.CreateCredentialRotationAsync(_deviceCode, 1, access);
        }

        var oldSecretBytes = WebEncoders.Base64UrlDecode(originalSecret);
        var newSecretBytes = WebEncoders.Base64UrlDecode(rotation.NewDeviceSecret);
        var oldKeyTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var oldKeyNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var oldKeyResponse = await SendHeartbeatAsync(
            oldKeyTimestamp, oldKeyNonce, SignHeartbeat(oldSecretBytes, oldKeyTimestamp, oldKeyNonce));
        Assert.Equal(HttpStatusCode.OK, oldKeyResponse.StatusCode);

        var newKeyTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var newKeyNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var newKeyResponse = await SendHeartbeatAsync(
            newKeyTimestamp, newKeyNonce, SignHeartbeat(newSecretBytes, newKeyTimestamp, newKeyNonce));
        Assert.Equal(HttpStatusCode.OK, newKeyResponse.StatusCode);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var securityDb = verifyScope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
        var credentials = await securityDb.Credentials.OrderBy(item => item.CreatedAtUtc).ToArrayAsync();
        Assert.Equal(2, credentials.Length);
        Assert.NotNull(credentials[0].RevokedAtUtc);
        Assert.False(credentials[1].IsPendingRotation);
        Assert.NotNull(credentials[1].ConfirmedAtUtc);
        var rotationEvents = await securityDb.AuditEvents
            .OrderBy(item => item.CreatedAtUtc)
            .Select(item => item.EventType)
            .ToArrayAsync();
        Assert.Contains("CredentialRotationStarted", rotationEvents);
        Assert.Contains("CredentialRotationConfirmed", rotationEvents);

        var oldKeyAfterConfirmationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var oldKeyAfterConfirmationNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var revokedKeyResponse = await SendHeartbeatAsync(
            oldKeyAfterConfirmationTimestamp,
            oldKeyAfterConfirmationNonce,
            SignHeartbeat(oldSecretBytes, oldKeyAfterConfirmationTimestamp, oldKeyAfterConfirmationNonce));
        Assert.Equal(HttpStatusCode.Unauthorized, revokedKeyResponse.StatusCode);
    }

    [Fact]
    public async Task ExpiredRotationLeavesOriginalCredentialUsable()
    {
        var originalSecret = await ProvisionDeviceAsync();
        DeviceCredentialRotationResult rotation;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<DeviceAuthenticationService>();
            var access = scope.ServiceProvider.GetRequiredService<HouseholdAccessService>();
            rotation = await service.CreateCredentialRotationAsync(_deviceCode, 1, access);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var securityDb = scope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
            await securityDb.Credentials
                .Where(item => item.IsPendingRotation)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    item => item.RotationExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        var newSecretBytes = WebEncoders.Base64UrlDecode(rotation.NewDeviceSecret);
        var newTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var newNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var expiredKeyResponse = await SendHeartbeatAsync(
            newTimestamp, newNonce, SignHeartbeat(newSecretBytes, newTimestamp, newNonce));
        Assert.Equal(HttpStatusCode.Unauthorized, expiredKeyResponse.StatusCode);

        var originalSecretBytes = WebEncoders.Base64UrlDecode(originalSecret);
        var oldTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var oldNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var originalKeyResponse = await SendHeartbeatAsync(
            oldTimestamp, oldNonce, SignHeartbeat(originalSecretBytes, oldTimestamp, oldNonce));
        Assert.Equal(HttpStatusCode.OK, originalKeyResponse.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanRevokeCurrentAndPendingCredentials()
    {
        var originalSecret = await ProvisionDeviceAsync();
        DeviceCredentialRotationResult rotation;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<DeviceAuthenticationService>();
            var access = scope.ServiceProvider.GetRequiredService<HouseholdAccessService>();
            rotation = await service.CreateCredentialRotationAsync(_deviceCode, 1, access);
            var revokedCount = await service.RevokeCredentialsAsync(_deviceCode, 1, access);
            Assert.Equal(2, revokedCount);
        }

        var originalBytes = WebEncoders.Base64UrlDecode(originalSecret);
        var rotatedBytes = WebEncoders.Base64UrlDecode(rotation.NewDeviceSecret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var oldNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var newNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        var oldResponse = await SendHeartbeatAsync(
            timestamp, oldNonce, SignHeartbeat(originalBytes, timestamp, oldNonce));
        var newResponse = await SendHeartbeatAsync(
            timestamp, newNonce, SignHeartbeat(rotatedBytes, timestamp, newNonce));
        Assert.Equal(HttpStatusCode.Unauthorized, oldResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, newResponse.StatusCode);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var securityDb = verifyScope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
        Assert.All(await securityDb.Credentials.ToArrayAsync(),
            credential => Assert.NotNull(credential.RevokedAtUtc));
        Assert.Contains("CredentialsRevoked",
            await securityDb.AuditEvents.Select(item => item.EventType).ToArrayAsync());
    }

    [Fact]
    public async Task NonAdministratorCannotRevokeDeviceCredentials()
    {
        await ProvisionDeviceAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DeviceAuthenticationService>();
        var access = scope.ServiceProvider.GetRequiredService<HouseholdAccessService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RevokeCredentialsAsync(_deviceCode, 2, access));
    }

    [Fact]
    public async Task DeviceConsoleDisplaysAdministratorSecurityAuditHistory()
    {
        await ProvisionDeviceAsync();

        using var response = await _client.GetAsync($"/Devices/{_deviceCode}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Device security history", html);
        Assert.Contains("ProvisioningCodeCreated", html);
        Assert.Contains("DeviceProvisioned", html);
    }

    private async Task<string> ProvisionDeviceAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DeviceAuthenticationService>();
        var access = scope.ServiceProvider.GetRequiredService<HouseholdAccessService>();
        var code = await service.CreateProvisioningCodeAsync(_deviceCode, 1, access);
        var result = await service.ExchangeProvisioningCodeAsync(_deviceCode, code.Code);
        return result.DeviceSecret;
    }

    private async Task<HttpResponseMessage> SendHeartbeatAsync(
        string timestamp, string nonce, string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/device-auth/heartbeat")
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };
        request.Headers.Add("X-EMS-Device", _deviceCode);
        request.Headers.Add("X-EMS-Timestamp", timestamp);
        request.Headers.Add("X-EMS-Nonce", nonce);
        request.Headers.Add("X-EMS-Signature", signature);
        return await _client.SendAsync(request);
    }

    private string SignHeartbeat(byte[] secret, string timestamp, string nonce)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>()));
        var canonical = string.Join("\n",
            "POST", "/api/device-auth/heartbeat", timestamp, nonce, bodyHash);
        return Convert.ToHexString(HMACSHA256.HashData(
            secret, Encoding.UTF8.GetBytes(canonical)));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
