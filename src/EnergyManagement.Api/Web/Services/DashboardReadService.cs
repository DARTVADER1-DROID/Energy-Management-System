using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Households;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Web.Services;

public sealed class DashboardReadService(
    EnergyManagementDbContext dbContext,
    HouseholdAccessService householdAccess)
{
    public async Task<IReadOnlyList<HouseholdOption>> GetHouseholdsAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var accessibleIds = await householdAccess.GetHouseholdIdsAsync(userId, cancellationToken);
        return await dbContext.Households.AsNoTracking()
            .Where(item => accessibleIds.Contains(item.Id))
            .OrderBy(item => item.Name)
            .Select(item => new HouseholdOption(item.Id, item.HouseholdCode, item.Name))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HouseholdOption>> GetAdministratorHouseholdsAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var memberships = await dbContextIdentityMemberships(userId, cancellationToken);
        return await dbContext.Households.AsNoTracking()
            .Where(item => memberships.Contains(item.Id))
            .OrderBy(item => item.Name)
            .Select(item => new HouseholdOption(item.Id, item.HouseholdCode, item.Name))
            .ToArrayAsync(cancellationToken);
    }

    private Task<int[]> dbContextIdentityMemberships(int userId, CancellationToken cancellationToken) =>
        householdAccess.GetAdministratorHouseholdIdsAsync(userId, cancellationToken);

    public async Task<DeviceDetails?> GetDeviceDetailsAsync(
        string deviceCode,
        int userId,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.DeviceCode == deviceCode, cancellationToken);

        return device is null || !await householdAccess.IsMemberAsync(
                userId, device.HouseholdId, cancellationToken)
            ? null
            : new DeviceDetails(
                device.Id,
                device.HouseholdId,
                device.DeviceCode,
                device.Name,
                device.HardwareModel,
                device.FirmwareVersion,
                device.InstallationLocation,
                device.ElectricalConfiguration,
                device.Description,
                device.IsActive,
                device.RegisteredAtUtc,
                device.LastSeenAtUtc,
                device.Thresholds.MinimumVoltage,
                device.Thresholds.MaximumVoltage,
                device.Thresholds.MaximumCurrent,
                device.Thresholds.MaximumInstantaneousPower,
                device.Thresholds.MaximumDailyEnergy,
                device.Thresholds.MaximumTemperature,
                device.Thresholds.WarningPersistenceSeconds);
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(
        int? householdId,
        int userId,
        CancellationToken cancellationToken)
    {
        var households = await GetHouseholdsAsync(userId, cancellationToken);

        var selected = householdId.HasValue
            ? households.FirstOrDefault(item => item.Id == householdId.Value)
            : households.FirstOrDefault();

        if (selected is null)
        {
            return new DashboardSnapshot([], null, 0, 0, 0, null);
        }

        var devices = dbContext.Devices.AsNoTracking()
            .Where(item => item.HouseholdId == selected.Id);

        var totalDevices = await devices.CountAsync(cancellationToken);
        var activeDevices = await devices.CountAsync(item => item.IsActive, cancellationToken);
        var devicesSeen = await devices.CountAsync(item => item.LastSeenAtUtc != null, cancellationToken);
        var latestSeen = await devices
            .Where(item => item.LastSeenAtUtc != null)
            .MaxAsync(item => (DateTimeOffset?)item.LastSeenAtUtc, cancellationToken);

        return new DashboardSnapshot(
            households, selected, totalDevices, activeDevices, devicesSeen, latestSeen);
    }

    public async Task<DeviceInventoryPage> GetDevicesAsync(
        int? householdId,
        string? search,
        int userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var households = await GetHouseholdsAsync(userId, cancellationToken);
        var accessibleIds = households.Select(item => item.Id).ToArray();

        var query = dbContext.Devices.AsNoTracking()
            .Where(item => accessibleIds.Contains(item.HouseholdId));
        if (householdId.HasValue)
        {
            query = query.Where(item => item.HouseholdId == householdId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(item =>
                item.DeviceCode.Contains(term) ||
                item.Name.Contains(term) ||
                item.HardwareModel.Contains(term) ||
                item.InstallationLocation.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var devices = await query
            .OrderBy(item => item.DeviceCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new DeviceListItem(
                item.Id,
                item.HouseholdId,
                item.DeviceCode,
                item.Name,
                item.HardwareModel,
                item.FirmwareVersion,
                item.InstallationLocation,
                item.ElectricalConfiguration,
                item.IsActive,
                item.RegisteredAtUtc,
                item.LastSeenAtUtc))
            .ToArrayAsync(cancellationToken);

        var selected = householdId.HasValue
            ? households.FirstOrDefault(item => item.Id == householdId.Value)
            : households.FirstOrDefault();

        return new DeviceInventoryPage(
            households,
            selected,
            devices,
            totalCount,
            page,
            pageSize);
    }
}

public sealed record HouseholdOption(int Id, string Code, string Name);

public sealed record DeviceDetails(
    int Id,
    int HouseholdId,
    string DeviceCode,
    string Name,
    string HardwareModel,
    string FirmwareVersion,
    string InstallationLocation,
    ElectricalConfiguration ElectricalConfiguration,
    string? Description,
    bool IsActive,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    decimal? MinimumVoltage,
    decimal? MaximumVoltage,
    decimal? MaximumCurrent,
    decimal? MaximumInstantaneousPower,
    decimal? MaximumDailyEnergy,
    decimal? MaximumTemperature,
    int? WarningPersistenceSeconds);

public sealed record DashboardSnapshot(
    IReadOnlyList<HouseholdOption> Households,
    HouseholdOption? SelectedHousehold,
    int TotalDevices,
    int ActiveDevices,
    int DevicesSeen,
    DateTimeOffset? LatestSeenAtUtc);

public sealed record DeviceListItem(
    int Id,
    int HouseholdId,
    string DeviceCode,
    string Name,
    string HardwareModel,
    string FirmwareVersion,
    string InstallationLocation,
    ElectricalConfiguration ElectricalConfiguration,
    bool IsActive,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? LastSeenAtUtc);

public sealed record DeviceInventoryPage(
    IReadOnlyList<HouseholdOption> Households,
    HouseholdOption? SelectedHousehold,
    IReadOnlyList<DeviceListItem> Devices,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
