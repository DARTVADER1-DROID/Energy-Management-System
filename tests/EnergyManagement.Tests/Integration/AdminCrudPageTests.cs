using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Households;
using EnergyManagement.Api.Modules.Identity;
using EnergyManagement.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnergyManagement.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdminCrudPageTests(PostgreSqlFixture postgres) : IAsyncLifetime
{
    private EnergyManagementApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new EnergyManagementApiFactory(postgres.ConnectionString);
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await ResetDatabaseAsync();
    }

    [Fact]
    public async Task Administrator_CanOpenAllManagementPagesAndSeesSearchControls()
    {
        var clients = await _client.GetAsync("/Admin/Clients");
        var devices = await _client.GetAsync("/Admin/Devices");
        var households = await _client.GetAsync("/Admin/Households");

        Assert.Equal(HttpStatusCode.OK, clients.StatusCode);
        Assert.Equal(HttpStatusCode.OK, devices.StatusCode);
        Assert.Equal(HttpStatusCode.OK, households.StatusCode);
        Assert.Contains("Create client", await clients.Content.ReadAsStringAsync());
        Assert.Contains("data-admin-search=\"clients\"", await clients.Content.ReadAsStringAsync());
        Assert.Contains("data-admin-status-filter=\"clients\"", await clients.Content.ReadAsStringAsync());
        Assert.Contains("data-admin-search=\"devices\"", await devices.Content.ReadAsStringAsync());
        Assert.Contains("data-admin-status-filter=\"devices\"", await devices.Content.ReadAsStringAsync());
        Assert.Contains("data-admin-search=\"households\"", await households.Content.ReadAsStringAsync());
        Assert.Contains("/js/admin-crud.js", await clients.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/Admin/Clients")]
    [InlineData("/Admin/Devices")]
    [InlineData("/Admin/Households")]
    public async Task AnonymousUser_CannotOpenAdminPages(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Anonymous", "true");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/Admin/Clients")]
    [InlineData("/Admin/Devices")]
    [InlineData("/Admin/Households")]
    public async Task HouseholdMember_CannotOpenAdminPages(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Role", "Household Member");
        request.Headers.Add("X-Test-User-Id", "2");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/Admin/Clients?handler=Create")]
    [InlineData("/Admin/Devices?handler=Create")]
    [InlineData("/Admin/Households?handler=Create")]
    public async Task Mutations_RequireRecentAuthentication(string path)
    {
        var parts = path.Split("?handler=", StringSplitOptions.None);
        var response = await PostFormAsync(parts[0], parts[1], new(), noRecentAuth: true);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Reauthenticate", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ClientCreate_PersistsIdentityAndRejectsDuplicateEmail()
    {
        var create = await PostFormAsync("/Admin/Clients", "Create", new()
        {
            ["CreateInput.Email"] = "  new-client@example.test  ",
            ["CreateInput.Password"] = "StrongPass1!"
        });

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var created = await manager.FindByEmailAsync("new-client@example.test");
            Assert.NotNull(created);
            Assert.Equal("new-client@example.test", created.UserName);
            Assert.True(created.EmailConfirmed);
            Assert.False(await manager.IsInRoleAsync(created, "Administrator"));
        }

        var duplicate = await PostFormAsync("/Admin/Clients", "Create", new()
        {
            ["CreateInput.Email"] = "new-client@example.test",
            ["CreateInput.Password"] = "StrongPass1!"
        });

        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("already taken", (await duplicate.Content.ReadAsStringAsync()).ToLowerInvariant());
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var users = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(4, await users.Users.CountAsync());
    }

    [Theory]
    [InlineData("not-an-email", "StrongPass1!")]
    [InlineData("valid@example.test", "weak")]
    [InlineData("valid@example.test", "StrongPassword1")]
    [InlineData("", "StrongPass1!")]
    [InlineData("valid@example.test", "")]
    public async Task ClientCreate_RejectsMalformedEmailAndWeakPassword(string email, string password)
    {
        var response = await PostFormAsync("/Admin/Clients", "Create", new()
        {
            ["CreateInput.Email"] = email,
            ["CreateInput.Password"] = password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(3, await db.Users.CountAsync());
    }

    [Fact]
    public async Task ClientUpdate_ChangesEmailAndUsernameTogetherAndCanDisableAndEnable()
    {
        var disabled = await PostFormAsync("/Admin/Clients", "Update", new()
        {
            ["EditInput.UserId"] = "2",
            ["EditInput.Email"] = "renamed@example.test",
            ["EditInput.IsDisabled"] = "true"
        });

        Assert.Equal(HttpStatusCode.Redirect, disabled.StatusCode);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await manager.FindByIdAsync("2");
            Assert.NotNull(user);
            Assert.Equal("renamed@example.test", user.Email);
            Assert.Equal("renamed@example.test", user.UserName);
            Assert.True(await manager.IsLockedOutAsync(user));
        }

        var enabled = await PostFormAsync("/Admin/Clients", "Update", new()
        {
            ["EditInput.UserId"] = "2",
            ["EditInput.Email"] = "renamed@example.test"
        });

        Assert.Equal(HttpStatusCode.Redirect, enabled.StatusCode);
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var reloaded = await verifyManager.FindByIdAsync("2");
        Assert.NotNull(reloaded);
        Assert.False(await verifyManager.IsLockedOutAsync(reloaded));
    }

    [Fact]
    public async Task ClientUpdate_RejectsDuplicateEmailWithoutChangingEitherAccount()
    {
        var response = await PostFormAsync("/Admin/Clients", "Update", new()
        {
            ["EditInput.UserId"] = "2",
            ["EditInput.Email"] = "admin@example.test"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal("client@example.test", (await db.Users.SingleAsync(user => user.Id == 2)).Email);
        Assert.Equal("admin@example.test", (await db.Users.SingleAsync(user => user.Id == 1)).Email);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ClientDelete_ProtectsCurrentAndOtherAdministratorAccounts(int protectedUserId)
    {
        var response = await PostFormAsync("/Admin/Clients", "Delete", new()
        {
            ["userId"] = protectedUserId.ToString()
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(await db.Users.AnyAsync(user => user.Id == protectedUserId));
    }

    [Fact]
    public async Task ClientDelete_BlocksAccountReferencedByIdentityAuditHistory()
    {
        await CreateHouseholdAsync("Audit retention home");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var householdId = await scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>()
                .Households.Select(item => item.Id).SingleAsync();
            db.SecurityAuditEvents.Add(new SecurityAuditEvent(householdId, 1, 2, "ClientUpdated"));
            await db.SaveChangesAsync();
        }

        var response = await PostFormAsync("/Admin/Clients", "Delete", new() { ["userId"] = "2" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("retained security", await FollowRedirectAsync(response));
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        Assert.True(await verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Users.AnyAsync(user => user.Id == 2));
    }

    [Fact]
    public async Task ClientDelete_BlocksAccountReferencedByDeviceSecurityAuditHistory()
    {
        var householdId = await CreateHouseholdAsync("Device audit retention home");
        var device = await RegisterDeviceAsync(householdId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var security = scope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
            security.AuditEvents.Add(new DeviceSecurityAuditEvent(
                device.Id, 2, "ProvisioningCodeCreated", "Retained security audit event"));
            await security.SaveChangesAsync();
        }

        var response = await PostFormAsync("/Admin/Clients", "Delete", new() { ["userId"] = "2" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("retained security", await FollowRedirectAsync(response));
    }

    [Fact]
    public async Task ClientDelete_BlocksAccountReferencedByInvitationHistory()
    {
        var householdId = await CreateHouseholdAsync("Client invitation retention home");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.HouseholdInvitations.Add(new HouseholdInvitation(
                householdId, "client@example.test", "CLIENT@EXAMPLE.TEST",
                HouseholdRole.HouseholdMember, new string('C', 64), 2,
                DateTimeOffset.UtcNow.AddDays(1)));
            await identity.SaveChangesAsync();
        }

        var response = await PostFormAsync("/Admin/Clients", "Delete", new() { ["userId"] = "2" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("retained security", await FollowRedirectAsync(response));
    }

    [Fact]
    public async Task ClientDelete_BlocksAccountReferencedByDeviceProvisioningHistory()
    {
        var householdId = await CreateHouseholdAsync("Provisioning retention home");
        var device = await RegisterDeviceAsync(householdId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var security = scope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
            security.ProvisioningCodes.Add(new DeviceProvisioningCode(
                device.Id, new string('A', 64), 2, DateTimeOffset.UtcNow.AddHours(1)));
            await security.SaveChangesAsync();
        }

        var response = await PostFormAsync("/Admin/Clients", "Delete", new() { ["userId"] = "2" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("retained security", await FollowRedirectAsync(response));
    }

    [Fact]
    public async Task ClientDelete_RemovesCleanClientAndCascadesMembership()
    {
        var householdId = await CreateHouseholdAsync("Client membership home");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.HouseholdMemberships.Add(new HouseholdMembership(2, householdId, HouseholdRole.HouseholdMember));
            await identity.SaveChangesAsync();
        }

        var response = await PostFormAsync("/Admin/Clients", "Delete", new() { ["userId"] = "2" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await verify.Users.AnyAsync(user => user.Id == 2));
        Assert.False(await verify.HouseholdMemberships.AnyAsync(item => item.UserId == 2));
    }

    [Fact]
    public async Task HouseholdCreate_TrimsNameAndCreatesAdministratorMembership()
    {
        var response = await PostFormAsync("/Admin/Households", "Create", new()
        {
            ["Input.Name"] = "  New admin home  "
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var domain = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var household = await domain.Households.SingleAsync();
        Assert.Equal("New admin home", household.Name);
        Assert.True(await identity.HouseholdMemberships.AnyAsync(item =>
            item.UserId == 1 && item.HouseholdId == household.Id &&
            item.Role == HouseholdRole.Administrator.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    public async Task HouseholdCreate_RejectsBlankOrTooShortName(string name)
    {
        var response = await PostFormAsync("/Admin/Households", "Create", new() { ["Input.Name"] = name });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>()
            .Households.ToListAsync());
    }

    [Fact]
    public async Task HouseholdCreate_RejectsNameOver120Characters()
    {
        var response = await PostFormAsync("/Admin/Households", "Create", new()
        {
            ["Input.Name"] = new string('H', 121)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>()
            .Households.ToListAsync());
    }

    [Fact]
    public async Task HouseholdUpdate_RenamesAndRejectsInvalidName()
    {
        var householdId = await CreateHouseholdAsync("Old name");
        var renamed = await PostFormAsync("/Admin/Households", "Update", new()
        {
            ["householdId"] = householdId.ToString(),
            ["Input.Name"] = "  New name  "
        });
        Assert.Equal(HttpStatusCode.Redirect, renamed.StatusCode);

        var invalid = await PostFormAsync("/Admin/Households", "Update", new()
        {
            ["householdId"] = householdId.ToString(),
            ["Input.Name"] = new string('X', 121)
        });
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.Equal("New name", (await scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>()
            .Households.SingleAsync()).Name);
    }

    [Fact]
    public async Task HouseholdDelete_BlocksHouseholdWithDevices()
    {
        var householdId = await CreateHouseholdAsync("Device protected home");
        await RegisterDeviceAsync(householdId);

        var response = await PostFormAsync("/Admin/Households", "Delete", new()
        {
            ["householdId"] = householdId.ToString()
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("devices", (await FollowRedirectAsync(response)).ToLowerInvariant());
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>()
            .Households.AnyAsync(item => item.Id == householdId));
    }

    [Fact]
    public async Task HouseholdDelete_BlocksHouseholdWithInvitationHistory()
    {
        var householdId = await CreateHouseholdAsync("Invitation protected home");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.HouseholdInvitations.Add(new HouseholdInvitation(
                householdId, "guest@example.test", "GUEST@EXAMPLE.TEST",
                HouseholdRole.HouseholdMember, new string('B', 64), 1,
                DateTimeOffset.UtcNow.AddDays(1)));
            await identity.SaveChangesAsync();
        }

        var response = await PostFormAsync("/Admin/Households", "Delete", new()
        {
            ["householdId"] = householdId.ToString()
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        Assert.True(await verifyScope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>()
            .Households.AnyAsync(item => item.Id == householdId));
    }

    [Fact]
    public async Task HouseholdDelete_BlocksHouseholdWithSecurityAuditHistory()
    {
        var householdId = await CreateHouseholdAsync("Audit protected home");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.SecurityAuditEvents.Add(new SecurityAuditEvent(householdId, 1, 2, "MembershipChanged"));
            await identity.SaveChangesAsync();
        }

        var response = await PostFormAsync("/Admin/Households", "Delete", new()
        {
            ["householdId"] = householdId.ToString()
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        Assert.True(await verifyScope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>()
            .Households.AnyAsync(item => item.Id == householdId));
    }

    [Fact]
    public async Task HouseholdDelete_RemovesCleanHouseholdAndCascadesMembership()
    {
        var householdId = await CreateHouseholdAsync("Disposable home");

        var response = await PostFormAsync("/Admin/Households", "Delete", new()
        {
            ["householdId"] = householdId.ToString()
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var domain = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await domain.Households.AnyAsync(item => item.Id == householdId));
        Assert.False(await identity.HouseholdMemberships.AnyAsync(item => item.HouseholdId == householdId));
    }

    [Fact]
    public async Task DeviceCreate_RegistersDeviceAndShowsItInGlobalInventory()
    {
        var householdId = await CreateHouseholdAsync("Device admin home");
        var response = await PostFormAsync("/Admin/Devices", "Create", ValidDeviceForm(householdId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        var device = await db.Devices.SingleAsync();
        Assert.Equal("Admin Monitor", device.Name);
        Assert.Equal("H001-DEV001", device.DeviceCode);
        Assert.True(device.IsActive);
        var page = await _client.GetAsync("/Admin/Devices");
        Assert.Contains("Admin Monitor", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeviceCreate_RejectsUnknownHouseholdAndInvalidElectricalEnum()
    {
        var unknown = await PostFormAsync("/Admin/Devices", "Create", ValidDeviceForm(9999));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);

        var householdId = await CreateHouseholdAsync("Enum test home");
        var invalid = ValidDeviceForm(householdId);
        invalid["Input.ElectricalConfiguration"] = "2147483647";
        var invalidResponse = await PostFormAsync("/Admin/Devices", "Create", invalid);
        Assert.Equal(HttpStatusCode.OK, invalidResponse.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>()
            .Devices.ToListAsync());
    }

    [Fact]
    public async Task DeviceUpdate_ChangesMetadataAndThresholdsButRejectsInvalidVoltageRange()
    {
        var householdId = await CreateHouseholdAsync("Device update home");
        var device = await RegisterDeviceAsync(householdId);
        var valid = ValidDeviceForm(householdId);
        valid["Input.Name"] = "Renamed Monitor";
        valid["Input.MinimumVoltage"] = "180";
        valid["Input.MaximumVoltage"] = "260";
        valid["Input.WarningPersistenceSeconds"] = "30";
        var updated = await PostFormAsync("/Admin/Devices", "Update", valid,
            new Dictionary<string, string> { ["deviceId"] = device.Id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, updated.StatusCode);

        var invalid = ValidDeviceForm(householdId);
        invalid["Input.Name"] = "Must Not Persist";
        invalid["Input.MinimumVoltage"] = "260";
        invalid["Input.MaximumVoltage"] = "180";
        invalid["Input.WarningPersistenceSeconds"] = "30";
        var rejected = await PostFormAsync("/Admin/Devices", "Update", invalid,
            new Dictionary<string, string> { ["deviceId"] = device.Id.ToString() });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>()
            .Devices.SingleAsync();
        Assert.Equal("Renamed Monitor", persisted.Name);
        Assert.Equal(180m, persisted.Thresholds.MinimumVoltage);
        Assert.Equal(260m, persisted.Thresholds.MaximumVoltage);
    }

    [Fact]
    public async Task DeviceDeactivate_IsRetainedAndCannotBeEditedAgain()
    {
        var householdId = await CreateHouseholdAsync("Device deactivation home");
        var device = await RegisterDeviceAsync(householdId);
        var response = await PostFormAsync("/Admin/Devices", "Deactivate", new(),
            new Dictionary<string, string> { ["deviceId"] = device.Id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var edit = ValidDeviceForm(householdId);
        edit["Input.Name"] = "Tampered inactive device";
        var rejected = await PostFormAsync("/Admin/Devices", "Update", edit,
            new Dictionary<string, string> { ["deviceId"] = device.Id.ToString() });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>()
            .Devices.SingleAsync();
        Assert.False(persisted.IsActive);
        Assert.Equal("Main Energy Monitor", persisted.Name);
    }

    [Theory]
    [InlineData("Input.Name", "")]
    [InlineData("Input.HardwareModel", "")]
    [InlineData("Input.FirmwareVersion", "")]
    [InlineData("Input.InstallationLocation", "")]
    public async Task DeviceCreate_RejectsMissingRequiredMetadata(string field, string value)
    {
        var householdId = await CreateHouseholdAsync("Required device fields");
        var form = ValidDeviceForm(householdId);
        form[field] = value;

        var response = await PostFormAsync("/Admin/Devices", "Create", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>()
            .Devices.ToListAsync());
    }

    [Fact]
    public async Task MissingAntiforgeryToken_IsRejectedForMutation()
    {
        var response = await _client.PostAsync("/Admin/Households?handler=Create",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Name"] = "CSRF attempt" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRecordIds_ReturnNotFoundRatherThanMutatingAnotherRecord()
    {
        var householdUpdate = await PostFormAsync("/Admin/Households", "Update",
            new() { ["householdId"] = "2147483647", ["Input.Name"] = "No target" });
        Assert.Equal(HttpStatusCode.NotFound, householdUpdate.StatusCode);

        var deviceDeactivate = await PostFormAsync("/Admin/Devices", "Deactivate", new(),
            new Dictionary<string, string> { ["deviceId"] = "2147483647" });
        Assert.Equal(HttpStatusCode.NotFound, deviceDeactivate.StatusCode);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var domain = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        await domain.Database.EnsureDeletedAsync();

        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var security = scope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
        await domain.Database.MigrateAsync();
        await identity.Database.MigrateAsync();
        await security.Database.MigrateAsync();

        identity.Users.AddRange(
            new ApplicationUser { Id = 1, UserName = "admin@example.test", Email = "admin@example.test", EmailConfirmed = true },
            new ApplicationUser { Id = 2, UserName = "client@example.test", Email = "client@example.test", EmailConfirmed = true },
            new ApplicationUser { Id = 3, UserName = "other-admin@example.test", Email = "other-admin@example.test", EmailConfirmed = true });
        identity.Roles.Add(new IdentityRole<int>("Administrator") { Id = 1, NormalizedName = "ADMINISTRATOR" });
        identity.Roles.Add(new IdentityRole<int>("Household Member") { Id = 2, NormalizedName = "HOUSEHOLD MEMBER" });
        identity.UserRoles.AddRange(
            new IdentityUserRole<int> { UserId = 1, RoleId = 1 },
            new IdentityUserRole<int> { UserId = 3, RoleId = 1 });
        await identity.SaveChangesAsync();
        await identity.Database.ExecuteSqlRawAsync(
            """
            SELECT setval(pg_get_serial_sequence('users', 'Id'), (SELECT MAX("Id") FROM users));
            SELECT setval(pg_get_serial_sequence('roles', 'Id'), (SELECT MAX("Id") FROM roles));
            """);
    }

    private async Task<int> CreateHouseholdAsync(string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var registration = scope.ServiceProvider.GetRequiredService<DeviceRegistrationService>();
        return (await registration.CreateHouseholdAsync(name, CancellationToken.None)).Id;
    }

    private async Task<Device> RegisterDeviceAsync(int householdId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var registration = scope.ServiceProvider.GetRequiredService<DeviceRegistrationService>();
        return await registration.RegisterAsync(householdId,
            new RegisterDeviceRequest("Main Energy Monitor", "ESP32", "1.0.0",
                "Main Distribution Board", ElectricalConfiguration.SinglePhase, null,
                new DeviceThresholdsRequest()), CancellationToken.None);
    }

    private async Task<HttpResponseMessage> PostFormAsync(
        string path,
        string handler,
        Dictionary<string, string> values,
        Dictionary<string, string>? routeValues = null,
        bool noRecentAuth = false)
    {
        var get = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var html = await get.Content.ReadAsStringAsync();
        var token = Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase);
        Assert.True(token.Success, "The rendered form did not contain an antiforgery token.");
        values["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);

        var query = new List<string> { "handler=" + Uri.EscapeDataString(handler) };
        if (routeValues is not null)
            query.AddRange(routeValues.Select(item =>
                Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(item.Value)));

        using var request = new HttpRequestMessage(HttpMethod.Post,
            path + "?" + string.Join("&", query))
        {
            Content = new FormUrlEncodedContent(values)
        };
        if (noRecentAuth) request.Headers.Add("X-Test-No-Recent-Auth", "true");
        return await _client.SendAsync(request);
    }

    private async Task<string> FollowRedirectAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        var target = location.IsAbsoluteUri ? location.PathAndQuery : location.ToString();
        return await (await _client.GetAsync(target)).Content.ReadAsStringAsync();
    }

    private static Dictionary<string, string> ValidDeviceForm(int householdId) => new()
    {
        ["Input.HouseholdId"] = householdId.ToString(),
        ["Input.Name"] = "Admin Monitor",
        ["Input.HardwareModel"] = "ESP32",
        ["Input.FirmwareVersion"] = "1.0.0",
        ["Input.InstallationLocation"] = "Main DB",
        ["Input.ElectricalConfiguration"] = "SinglePhase",
        ["Input.Description"] = "Created through admin CRUD"
    };
}
