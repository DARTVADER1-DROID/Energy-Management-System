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

    public async Task InitializeAsync()
    {
        _factory = new EnergyManagementApiFactory(postgres.ConnectionString);
        _client = _factory.CreateClient();

        await using var scope = _factory.Services.CreateAsyncScope();
        var domainDb = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        await domainDb.Database.EnsureDeletedAsync();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await domainDb.Database.MigrateAsync();
        await identityDb.Database.MigrateAsync();

        identityDb.Users.Add(new ApplicationUser
        {
            Id = 1,
            UserName = "recent-auth-admin@example.test",
            Email = "recent-auth-admin@example.test",
            EmailConfirmed = true,
            TwoFactorEnabled = true
        });
        await identityDb.SaveChangesAsync();

        var registration = scope.ServiceProvider.GetRequiredService<DeviceRegistrationService>();
        var household = await registration.CreateHouseholdAsync(
            "Recent Authentication Home", CancellationToken.None);
        _householdId = household.Id;
        identityDb.HouseholdMemberships.Add(
            new HouseholdMembership(1, _householdId, HouseholdRole.Administrator));
        await identityDb.SaveChangesAsync();
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
    public async Task ReauthenticationPagePromptsForPassword()
    {
        var response = await _client.GetAsync("/Account/Reauthenticate");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Confirm your identity", html);
        Assert.Contains("Password", html);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
