using System.Net;
using System.Net.Http.Json;
using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Identity;
using EnergyManagement.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnergyManagement.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class RecentAuthenticationTests(PostgreSqlFixture postgres) : IAsyncLifetime
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
                UserName = "recent-auth-admin@example.test",
                Email = "recent-auth-admin@example.test",
                EmailConfirmed = true,
                TwoFactorEnabled = true
            },
            new ApplicationUser
            {
                Id = 2,
                UserName = "recent-auth-member@example.test",
                Email = "recent-auth-member@example.test",
                EmailConfirmed = true
            });
        await identityDb.SaveChangesAsync();

        var registration = scope.ServiceProvider.GetRequiredService<DeviceRegistrationService>();
        var household = await registration.CreateHouseholdAsync(
            "Recent Authentication Home", CancellationToken.None);
        _householdId = household.Id;
        identityDb.HouseholdMemberships.AddRange(
            new HouseholdMembership(1, _householdId, HouseholdRole.Administrator),
            new HouseholdMembership(2, _householdId, HouseholdRole.HouseholdMember));
        await identityDb.SaveChangesAsync();

        var device = await registration.RegisterAsync(
            _householdId,
            new RegisterDeviceRequest(
                "Main Energy Monitor", "ESP32", "1.0.0", "Main Distribution Board",
                ElectricalConfiguration.SinglePhase, null, new DeviceThresholdsRequest()),
            CancellationToken.None);
        _deviceCode = device.DeviceCode;
    }

    [Fact]
    public async Task DeviceRegistrationRequiresRecentAuthentication()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/devices/{_householdId}")
        {
            Content = JsonContent.Create(new RegisterDeviceRequest(
                "Main Energy Monitor",
                "ESP32",
                "1.0.0",
                "Main Distribution Board",
                ElectricalConfiguration.SinglePhase,
                null,
                new DeviceThresholdsRequest()))
        };
        request.Headers.Add("X-Test-No-Recent-Auth", "true");

        var response = await _client.SendAsync(request);

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
    }

    [Fact]
    public async Task HouseholdAdministratorSeesDeviceDeactivationControl()
    {
        var response = await _client.GetAsync($"/Devices/{_deviceCode}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Deactivate registration", html);
    }

    [Fact]
    public async Task HouseholdMemberCannotSeeDeviceDeactivationControl()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/Devices/{_deviceCode}");
        request.Headers.Add("X-Test-User-Id", "2");
        request.Headers.Add("X-Test-Role", "Household Member");
        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Deactivate registration", html);
    }

    [Fact]
    public async Task AccountSecurityPageOffersRecoveryCodeRegenerationWhenMfaIsEnabled()
    {
        var response = await _client.GetAsync("/Account/Profile");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Recovery codes remaining", html);
        Assert.Contains("Regenerate recovery codes", html);
        Assert.Contains("Change password", html);
    }

    [Fact]
    public async Task AccountPagesIncludeSecurityHeaders()
    {
        var response = await _client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-store, no-cache, max-age=0", response.Headers.GetValues("Cache-Control").Single());
        Assert.Equal("no-cache", response.Headers.GetValues("Pragma").Single());
    }

    [Fact]
    public async Task ReauthenticationPagePromptsForPassword()
    {
        var response = await _client.GetAsync("/Account/Reauthenticate");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Confirm your identity", html);
        Assert.Contains("Password", html);
    }

    [Fact]
    public async Task MfaEnrollmentRequiresRecentAuthenticationAndDisablesCaching()
    {
        using var noRedirectClient = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Account/EnableMfa");
        request.Headers.Add("X-Test-No-Recent-Auth", "true");

        using var response = await noRedirectClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Account/Reauthenticate", response.RequestMessage?.RequestUri?.AbsolutePath);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task MfaEnrollmentPageIsNotCacheable()
    {
        using var response = await _client.GetAsync("/Account/EnableMfa");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
