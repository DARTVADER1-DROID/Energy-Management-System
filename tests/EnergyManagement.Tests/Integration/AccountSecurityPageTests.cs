using System.Net;
using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Identity;
using EnergyManagement.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnergyManagement.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class AccountSecurityPageTests(PostgreSqlFixture postgres) : IAsyncLifetime
{
    private EnergyManagementApiFactory _factory = null!;
    private HttpClient _client = null!;

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
            UserName = "account-security@example.test",
            Email = "account-security@example.test",
            EmailConfirmed = true,
            TwoFactorEnabled = true
        });
        await identityDb.SaveChangesAsync();
    }

    [Fact]
    public async Task ProfilePage_ShowsAccountAndAuthenticationStatus()
    {
        var response = await _client.GetAsync("/Account/Profile");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Account security", html);
        Assert.Contains("account-security@example.test", html);
        Assert.Contains("Authenticator app", html);
        Assert.Contains("Recovery codes remaining", html);
        Assert.Contains("15 minutes of inactivity", html);
    }

    [Fact]
    public async Task LoginPage_UsesAnonymousAccountShell()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Login");
        request.Headers.Add("X-Test-Anonymous", "true");
        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sign in", html);
        Assert.Contains("hidden", html);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
