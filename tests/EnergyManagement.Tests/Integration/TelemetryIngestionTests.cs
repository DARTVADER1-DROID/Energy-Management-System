using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Identity;
using EnergyManagement.Api.Modules.Telemetry;
using EnergyManagement.Tests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnergyManagement.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class TelemetryIngestionTests(PostgreSqlFixture postgres) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private EnergyManagementApiFactory _factory = null!;
    private HttpClient _client = null!;
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
                UserName = "telemetry-admin@example.test",
                Email = "telemetry-admin@example.test",
                EmailConfirmed = true,
                TwoFactorEnabled = true
            },
            new ApplicationUser
            {
                Id = 2,
                UserName = "telemetry-member@example.test",
                Email = "telemetry-member@example.test",
                EmailConfirmed = true
            });
        await identityDb.SaveChangesAsync();

        var registration = scope.ServiceProvider.GetRequiredService<DeviceRegistrationService>();
        var household = await registration.CreateHouseholdAsync("Telemetry Test Home", CancellationToken.None);
        identityDb.HouseholdMemberships.AddRange(
            new HouseholdMembership(1, household.Id, HouseholdRole.Administrator),
            new HouseholdMembership(2, household.Id, HouseholdRole.HouseholdMember));
        await identityDb.SaveChangesAsync();

        var device = await registration.RegisterAsync(
            household.Id,
            new RegisterDeviceRequest(
                "Telemetry Monitor", "ESP32", "1.0.0", "Distribution Board",
                ElectricalConfiguration.SinglePhase, null, new DeviceThresholdsRequest()),
            CancellationToken.None);
        _deviceCode = device.DeviceCode;
    }

    [Fact]
    public async Task SignedTelemetryIsAcceptedAndStoredWithFlexiblePayload()
    {
        var secret = await ProvisionDeviceAsync();
        var envelope = CreateEnvelope();
        using var response = await SendTelemetryAsync(secret, envelope);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        var stored = await db.TelemetryReadings.SingleAsync();
        Assert.Equal(envelope.SampleId, stored.SampleId);
        Assert.Equal(1, stored.SchemaVersion);
        Assert.Contains("\"L1\"", stored.PayloadJson);
        Assert.Contains("\"L2\"", stored.PayloadJson);
    }

    [Fact]
    public async Task DuplicateSampleIsRejectedEvenWhenRequestNonceIsNew()
    {
        var secret = await ProvisionDeviceAsync();
        var envelope = CreateEnvelope();

        using var first = await SendTelemetryAsync(secret, envelope);
        using var duplicate = await SendTelemetryAsync(secret, envelope);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task HouseholdMemberCanQueryTelemetryHistoryForTheirDevice()
    {
        var secret = await ProvisionDeviceAsync();
        var envelope = CreateEnvelope();
        using var ingestResponse = await SendTelemetryAsync(secret, envelope);
        Assert.Equal(HttpStatusCode.Accepted, ingestResponse.StatusCode);

        var fromUtc = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
        var toUtc = DateTimeOffset.UtcNow.AddHours(1).ToString("O");
        var url = $"/api/devices/{_deviceCode}/telemetry?fromUtc={Uri.EscapeDataString(fromUtc)}&toUtc={Uri.EscapeDataString(toUtc)}&limit=10";
        using var historyResponse = await _client.GetAsync(url);
        var body = await historyResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.Contains(envelope.SampleId.ToString(), body);
        Assert.Contains("231.4", body);
    }

    [Fact]
    public async Task DashboardDisplaysPersistedTelemetryInsteadOfPlaceholderValues()
    {
        var secret = await ProvisionDeviceAsync();
        using var ingestResponse = await SendTelemetryAsync(secret, CreateEnvelope());
        Assert.Equal(HttpStatusCode.Accepted, ingestResponse.StatusCode);

        using var dashboardResponse = await _client.GetAsync("/");
        var html = await dashboardResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        Assert.Contains("Telemetry data available", html);
        Assert.Contains("0.92", html);
        Assert.Contains("231.4", html);
        Assert.Contains("12.54", html);
        Assert.DoesNotContain("No telemetry has been received yet", html);
    }

    [Fact]
    public async Task InvalidMeasurementsAreRejectedWithoutPersistence()
    {
        var secret = await ProvisionDeviceAsync();
        var envelope = CreateEnvelope() with
        {
            Measurements = new TelemetryMeasurements(VoltageVolts: 5000)
        };

        using var response = await SendTelemetryAsync(secret, envelope);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        Assert.Empty(await db.TelemetryReadings.ToArrayAsync());
    }

    private TelemetryEnvelope CreateEnvelope() => new(
        1,
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        new TelemetryMeasurements(
            VoltageVolts: 231.4m,
            CurrentAmps: 4.2m,
            ActivePowerWatts: 920m,
            EnergyWattHours: 12540m,
            FrequencyHertz: 50m,
            PowerFactor: 0.95m,
            TemperatureCelsius: 34.5m),
        new Dictionary<string, PhaseTelemetryMeasurements>
        {
            ["L1"] = new(231.4m, 4.2m, 920m, 12540m, 0.95m),
            ["L2"] = new(229.8m, 2.1m, 480m, 6420m, 0.98m)
        });

    private async Task<string> ProvisionDeviceAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DeviceAuthenticationService>();
        var access = scope.ServiceProvider.GetRequiredService<HouseholdAccessService>();
        var code = await service.CreateProvisioningCodeAsync(_deviceCode, 1, access);
        var result = await service.ExchangeProvisioningCodeAsync(_deviceCode, code.Code);
        return result.DeviceSecret;
    }

    private async Task<HttpResponseMessage> SendTelemetryAsync(
        string secret,
        TelemetryEnvelope envelope)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var bodyHash = Convert.ToHexString(SHA256.HashData(body));
        const string path = "/api/device-telemetry";
        var canonical = string.Join("\n", "POST", path, timestamp, nonce, bodyHash);
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            WebEncoders.Base64UrlDecode(secret), Encoding.UTF8.GetBytes(canonical)));

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-EMS-Device", _deviceCode);
        request.Headers.Add("X-EMS-Timestamp", timestamp);
        request.Headers.Add("X-EMS-Nonce", nonce);
        request.Headers.Add("X-EMS-Signature", signature);
        return await _client.SendAsync(request);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
