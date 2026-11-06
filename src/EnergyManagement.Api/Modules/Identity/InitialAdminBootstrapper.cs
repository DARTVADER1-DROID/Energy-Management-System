using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Modules.Identity;

public sealed class InitialAdminBootstrapper(
    IConfiguration configuration,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<int>> roleManager,
    EnergyManagementDbContext energyDb,
    HouseholdAccessService householdAccess,
    DeviceRegistrationService deviceRegistration)
{
    private static readonly string[] Roles =
    [
        "Administrator",
        "Household Member",
        "Read-only Viewer"
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var roleName in Roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole<int>(roleName));
                EnsureSucceeded(roleResult, "Unable to create the initial role.");
            }
        }

        var email = configuration["EMS_BOOTSTRAP_ADMIN_EMAIL"]?.Trim();
        var password = configuration["EMS_BOOTSTRAP_ADMIN_PASSWORD"];
        var householdName = configuration["EMS_BOOTSTRAP_HOUSEHOLD_NAME"]?.Trim();

        if (string.IsNullOrWhiteSpace(email) &&
            string.IsNullOrWhiteSpace(password) &&
            string.IsNullOrWhiteSpace(householdName))
            return;

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(householdName))
            throw new InvalidOperationException(
                "Initial administrator bootstrap requires EMS_BOOTSTRAP_ADMIN_EMAIL, EMS_BOOTSTRAP_ADMIN_PASSWORD, and EMS_BOOTSTRAP_HOUSEHOLD_NAME.");

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            if (await userManager.Users.AnyAsync(cancellationToken))
                throw new InvalidOperationException(
                    "Initial administrator bootstrap is disabled because the installation already contains users.");

            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };
            var createResult = await userManager.CreateAsync(user, password);
            EnsureSucceeded(createResult, "Unable to create the initial administrator account.");
        }

        if (!await userManager.IsInRoleAsync(user, "Administrator"))
        {
            var roleResult = await userManager.AddToRoleAsync(user, "Administrator");
            EnsureSucceeded(roleResult, "Unable to assign the Administrator role.");
        }

        var householdIds = await householdAccess.GetHouseholdIdsAsync(user.Id, cancellationToken);
        if (householdIds.Length > 0)
            return;

        var household = await energyDb.Households.OrderBy(item => item.Id).FirstOrDefaultAsync(cancellationToken);
        household ??= await deviceRegistration.CreateHouseholdAsync(householdName, cancellationToken);

        await householdAccess.AddMembershipAsync(
            user.Id, household.Id, HouseholdRole.Administrator, cancellationToken);
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"{message} {string.Join("; ", result.Errors.Select(error => error.Code))}");
    }
}
