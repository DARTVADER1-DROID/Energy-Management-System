using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Modules.Identity;

public sealed class HouseholdAccessService(IdentityDbContext identityDb)
{
    public Task<int[]> GetHouseholdIdsAsync(int userId, CancellationToken cancellationToken = default) =>
        identityDb.HouseholdMemberships.AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Select(membership => membership.HouseholdId)
            .ToArrayAsync(cancellationToken);

    public Task<int[]> GetAdministratorHouseholdIdsAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        identityDb.HouseholdMemberships.AsNoTracking()
            .Where(membership =>
                membership.UserId == userId &&
                membership.Role == HouseholdRole.Administrator.ToString())
            .Select(membership => membership.HouseholdId)
            .ToArrayAsync(cancellationToken);

    public Task<bool> IsMemberAsync(
        int userId,
        int householdId,
        CancellationToken cancellationToken = default) =>
        identityDb.HouseholdMemberships.AsNoTracking()
            .AnyAsync(membership =>
                membership.UserId == userId && membership.HouseholdId == householdId,
                cancellationToken);

    public Task<bool> IsAdministratorAsync(
        int userId,
        int householdId,
        CancellationToken cancellationToken = default) =>
        identityDb.HouseholdMemberships.AsNoTracking()
            .AnyAsync(membership =>
                membership.UserId == userId &&
                membership.HouseholdId == householdId &&
                membership.Role == HouseholdRole.Administrator.ToString(),
                cancellationToken);

    public async Task AddMembershipAsync(
        int userId,
        int householdId,
        HouseholdRole role,
        CancellationToken cancellationToken = default)
    {
        var existing = await identityDb.HouseholdMemberships
            .SingleOrDefaultAsync(membership =>
                membership.UserId == userId && membership.HouseholdId == householdId,
                cancellationToken);

        if (existing is not null)
            throw new InvalidOperationException("The user is already a member of this household.");

        identityDb.HouseholdMemberships.Add(new HouseholdMembership(userId, householdId, role));
        await identityDb.SaveChangesAsync(cancellationToken);
    }
}
