using System.Net;
using System.Text.RegularExpressions;
using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Identity;
using EnergyManagement.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnergyManagement.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class HouseholdInvitationTests(PostgreSqlFixture postgres) : IAsyncLifetime
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
                UserName = "household-admin@example.test",
                Email = "household-admin@example.test",
                EmailConfirmed = true,
                TwoFactorEnabled = true
            },
            new ApplicationUser
            {
                Id = 2,
                UserName = "non-admin@example.test",
                Email = "non-admin@example.test",
                EmailConfirmed = true
            });
        identityDb.Roles.AddRange(
            new IdentityRole<int>("Administrator") { Id = 1, NormalizedName = "ADMINISTRATOR" },
            new IdentityRole<int>("Household Member") { Id = 2, NormalizedName = "HOUSEHOLD MEMBER" },
            new IdentityRole<int>("Read-only Viewer") { Id = 3, NormalizedName = "READ-ONLY VIEWER" });
        await identityDb.SaveChangesAsync();
        await identityDb.Database.ExecuteSqlRawAsync(
            """SELECT setval(pg_get_serial_sequence('users', 'Id'), (SELECT MAX("Id") FROM users), true);""");

        var registration = scope.ServiceProvider.GetRequiredService<DeviceRegistrationService>();
        var household = await registration.CreateHouseholdAsync("Invitation Test Home", CancellationToken.None);
        _householdId = household.Id;
        identityDb.HouseholdMemberships.AddRange(
            new HouseholdMembership(1, _householdId, HouseholdRole.Administrator),
            new HouseholdMembership(2, _householdId, HouseholdRole.HouseholdMember));
        await identityDb.SaveChangesAsync();
    }

    [Fact]
    public async Task AdministratorCanInviteAndNewUserCanAcceptOnce()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<HouseholdInvitationService>();
        var emailSender = scope.ServiceProvider.GetRequiredService<TestEmailSender>();

        await service.CreateAsync(
            _householdId, 1, "new-member@example.test", HouseholdRole.HouseholdMember);

        var email = Assert.Single(emailSender.Messages);
        Assert.Equal("new-member@example.test", email.Recipient);
        Assert.Contains("within 24 hours", email.Body, StringComparison.OrdinalIgnoreCase);

        var linkMatch = Regex.Match(email.Body, @"https://\S+");
        Assert.True(linkMatch.Success);
        var inviteUri = new Uri(linkMatch.Value);
        var token = QueryHelpers.ParseQuery(inviteUri.Query)["token"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var createdUser = await service.AcceptForNewUserAsync(
            token, "A-strong-test-password-123!");

        Assert.Equal("new-member@example.test", createdUser.Email);
        Assert.True(createdUser.EmailConfirmed);

        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(await identityDb.HouseholdMemberships.AnyAsync(membership =>
            membership.UserId == createdUser.Id &&
            membership.HouseholdId == _householdId &&
            membership.Role == HouseholdRole.HouseholdMember.ToString()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcceptForNewUserAsync(token, "A-strong-test-password-123!"));
    }

    [Fact]
    public async Task AdministratorCannotCreateDuplicatePendingInvitation()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<HouseholdInvitationService>();
        var emailSender = scope.ServiceProvider.GetRequiredService<TestEmailSender>();

        await service.CreateAsync(
            _householdId, 1, "duplicate@example.test", HouseholdRole.HouseholdMember);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(
                _householdId, 1, "DUPLICATE@example.test", HouseholdRole.ReadOnlyViewer));
        Assert.Single(emailSender.Messages);
    }

    [Fact]
    public async Task ExpiredInvitationCanBeReissuedForSameEmail()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<HouseholdInvitationService>();
        var emailSender = scope.ServiceProvider.GetRequiredService<TestEmailSender>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        await service.CreateAsync(
            _householdId, 1, "reissue@example.test", HouseholdRole.HouseholdMember);
        await identityDb.HouseholdInvitations
            .Where(invitation => invitation.HouseholdId == _householdId)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(invitation => invitation.ExpiresAtUtc,
                    DateTimeOffset.UtcNow.AddMinutes(-1)));

        await service.CreateAsync(
            _householdId, 1, "reissue@example.test", HouseholdRole.ReadOnlyViewer);

        Assert.Equal(2, emailSender.Messages.Count);
        Assert.Equal(2, await identityDb.HouseholdInvitations.CountAsync(
            invitation => invitation.HouseholdId == _householdId));
        Assert.Equal(1, await identityDb.HouseholdInvitations.CountAsync(invitation =>
            invitation.HouseholdId == _householdId && invitation.RevokedAtUtc != null));
    }

    [Fact]
    public async Task NonAdministratorCannotCreateHouseholdInvitation()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<HouseholdInvitationService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateAsync(_householdId, 2, "blocked@example.test", HouseholdRole.ReadOnlyViewer));
    }

    [Fact]
    public async Task ChangingMemberRoleInvalidatesSessionsAndCreatesAuditEvent()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var member = await userManager.FindByIdAsync("2");
        Assert.NotNull(member);
        var previousSecurityStamp = member.SecurityStamp;

        var service = scope.ServiceProvider.GetRequiredService<HouseholdInvitationService>();
        await service.ChangeRoleAsync(
            _householdId, 2, 1, HouseholdRole.ReadOnlyViewer);

        var updatedMember = await userManager.FindByIdAsync("2");
        Assert.NotNull(updatedMember);
        Assert.NotEqual(previousSecurityStamp, updatedMember.SecurityStamp);

        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(await identityDb.HouseholdMemberships.AnyAsync(membership =>
            membership.HouseholdId == _householdId &&
            membership.UserId == 2 &&
            membership.Role == HouseholdRole.ReadOnlyViewer.ToString()));
        var auditEvent = await identityDb.SecurityAuditEvents.SingleAsync();
        Assert.Equal("HouseholdRoleChanged", auditEvent.EventType);
        Assert.Equal(1, auditEvent.ActorUserId);
        Assert.Equal(2, auditEvent.TargetUserId);
    }

    [Fact]
    public async Task RemovingMemberRevokesOnlySelectedHouseholdMembershipAndAuditsAction()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var member = await userManager.FindByIdAsync("2");
        Assert.NotNull(member);
        var previousSecurityStamp = member.SecurityStamp;

        var service = scope.ServiceProvider.GetRequiredService<HouseholdInvitationService>();
        await service.RemoveMemberAsync(_householdId, 2, 1);

        var updatedMember = await userManager.FindByIdAsync("2");
        Assert.NotNull(updatedMember);
        Assert.NotEqual(previousSecurityStamp, updatedMember.SecurityStamp);

        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await identityDb.HouseholdMemberships.AnyAsync(membership =>
            membership.HouseholdId == _householdId && membership.UserId == 2));
        Assert.NotNull(updatedMember);
        var auditEvent = await identityDb.SecurityAuditEvents.SingleAsync();
        Assert.Equal("HouseholdMemberRemoved", auditEvent.EventType);
    }

    [Fact]
    public async Task HouseholdUsersPageIsAvailableToHouseholdAdministrators()
    {
        var response = await _client.GetAsync($"/Households/Users?householdId={_householdId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Members and invitations", html);
        Assert.Contains("Add a household user", html);
    }

    [Fact]
    public async Task HouseholdAdministratorCanOpenHouseholdCreationPage()
    {
        var response = await _client.GetAsync("/Households/Create");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Create a household", html);
        Assert.Contains("Household name", html);
    }

    [Fact]
    public async Task HouseholdMemberCannotOpenHouseholdCreationPage()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Households/Create");
        request.Headers.Add("X-Test-Role", "Household Member");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
