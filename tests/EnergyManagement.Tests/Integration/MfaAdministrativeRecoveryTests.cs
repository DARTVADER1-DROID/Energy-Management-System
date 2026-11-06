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
public sealed class MfaAdministrativeRecoveryTests(PostgreSqlFixture postgres) : IAsyncLifetime
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

        identityDb.Users.AddRange(
            new ApplicationUser
            {
                Id = 1,
                UserName = "recovery-admin@example.test",
                Email = "recovery-admin@example.test",
                EmailConfirmed = true,
                TwoFactorEnabled = true
            },
            new ApplicationUser
            {
                Id = 2,
                UserName = "recovery-member@example.test",
                Email = "recovery-member@example.test",
                EmailConfirmed = true,
                TwoFactorEnabled = true
            });
        await identityDb.SaveChangesAsync();

        var registration = scope.ServiceProvider.GetRequiredService<DeviceRegistrationService>();
        var household = await registration.CreateHouseholdAsync(
            "MFA Recovery Home", CancellationToken.None);
        _householdId = household.Id;
        identityDb.HouseholdMemberships.AddRange(
            new HouseholdMembership(1, _householdId, HouseholdRole.Administrator),
            new HouseholdMembership(2, _householdId, HouseholdRole.HouseholdMember));
        await identityDb.SaveChangesAsync();

        var recoveryCodes = scope.ServiceProvider.GetRequiredService<MfaRecoveryCodeService>();
        await recoveryCodes.GenerateAsync(2);
    }

    [Fact]
    public async Task AnotherHouseholdAdministratorCanResetMfaAndAuditTheRecovery()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<
            Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var targetBefore = await userManager.FindByIdAsync("2");
        Assert.NotNull(targetBefore);
        var oldSecurityStamp = targetBefore.SecurityStamp;

        var recovery = scope.ServiceProvider.GetRequiredService<MfaAdministrativeRecoveryService>();
        await recovery.ResetAsync(_householdId, 2, 1);

        var targetAfter = await userManager.FindByIdAsync("2");
        Assert.NotNull(targetAfter);
        Assert.False(targetAfter.TwoFactorEnabled);
        Assert.NotEqual(oldSecurityStamp, targetAfter.SecurityStamp);
        Assert.False(string.IsNullOrWhiteSpace(await userManager.GetAuthenticatorKeyAsync(targetAfter)));
        Assert.Empty(await identityDb.MfaRecoveryCodes
            .Where(code => code.UserId == 2)
            .ToListAsync());

        var auditEvent = await identityDb.SecurityAuditEvents.SingleAsync();
        Assert.Equal(_householdId, auditEvent.HouseholdId);
        Assert.Equal(1, auditEvent.ActorUserId);
        Assert.Equal(2, auditEvent.TargetUserId);
        Assert.Equal("MfaResetByAdministrator", auditEvent.EventType);
    }

    [Fact]
    public async Task AdministratorCannotUseRecoveryFlowOnTheirOwnAccount()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var recovery = scope.ServiceProvider.GetRequiredService<MfaAdministrativeRecoveryService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recovery.ResetAsync(_householdId, 1, 1));
    }

    [Fact]
    public async Task HouseholdMemberCannotResetAnotherUsersMfa()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var recovery = scope.ServiceProvider.GetRequiredService<MfaAdministrativeRecoveryService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            recovery.ResetAsync(_householdId, 1, 2));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
