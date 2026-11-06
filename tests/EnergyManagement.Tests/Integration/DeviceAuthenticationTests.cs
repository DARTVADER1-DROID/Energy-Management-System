using System.Net;
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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExchangeProvisioningCodeAsync(_deviceCode, code.Code));
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
