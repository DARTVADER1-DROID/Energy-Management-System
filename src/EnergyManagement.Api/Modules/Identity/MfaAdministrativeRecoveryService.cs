using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Modules.Identity;

public sealed class MfaAdministrativeRecoveryService(
    IdentityDbContext identityDb,
    HouseholdAccessService householdAccess,
    UserManager<ApplicationUser> userManager)
{
    public async Task ResetAsync(
        int householdId,
        int targetUserId,
        int actingAdministratorUserId,
        CancellationToken cancellationToken = default)
    {
        if (targetUserId == actingAdministratorUserId)
            throw new InvalidOperationException("An administrator cannot reset their own MFA.");

        if (!await householdAccess.IsAdministratorAsync(
            actingAdministratorUserId, householdId, cancellationToken))
            throw new UnauthorizedAccessException("Only a household administrator can recover another member's MFA.");

        if (!await householdAccess.IsMemberAsync(targetUserId, householdId, cancellationToken))
            throw new InvalidOperationException("The selected account is not a member of this household.");

        var targetAdministratorHouseholds =
            await householdAccess.GetAdministratorHouseholdIdsAsync(targetUserId, cancellationToken);
        foreach (var administratorHouseholdId in targetAdministratorHouseholds)
        {
            if (!await householdAccess.IsAdministratorAsync(
                actingAdministratorUserId, administratorHouseholdId, cancellationToken))
                throw new UnauthorizedAccessException(
                    "To recover an administrator account, you must also administer every household where that account has administrator access.");
        }

        var targetUser = await userManager.FindByIdAsync(targetUserId.ToString())
            ?? throw new InvalidOperationException("The selected account no longer exists.");

        await using var transaction = await identityDb.Database.BeginTransactionAsync(cancellationToken);

        EnsureSucceeded(
            await userManager.SetTwoFactorEnabledAsync(targetUser, false),
            "Unable to disable the lost authenticator.");

        EnsureSucceeded(
            await userManager.ResetAuthenticatorKeyAsync(targetUser),
            "Unable to reset the authenticator key.");

        await identityDb.MfaRecoveryCodes
            .Where(code => code.UserId == targetUserId)
            .ExecuteDeleteAsync(cancellationToken);

        EnsureSucceeded(
            await userManager.UpdateSecurityStampAsync(targetUser),
            "Unable to invalidate the account's existing sessions.");

        identityDb.SecurityAuditEvents.Add(new SecurityAuditEvent(
            householdId, actingAdministratorUserId, targetUserId, "MfaResetByAdministrator"));
        await identityDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"{message} {string.Join("; ", result.Errors.Select(error => error.Code))}");
    }
}
