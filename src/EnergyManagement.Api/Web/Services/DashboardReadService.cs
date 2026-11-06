using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Households;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Web.Services;

public sealed class DashboardReadService(EnergyManagementDbContext dbContext)
{
    public async Task<DashboardSnapshot> GetDashboardAsync(
        int? householdId,
        CancellationToken cancellationToken)
    {
        var households = await dbContext.Households
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new HouseholdOption(item.Id, item.HouseholdCode, item.Name))
            .ToArrayAsync(cancellationToken);

        var selected = households.FirstOrDefault(item => item.Id == householdId)
            ?? households.FirstOrDefault();

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
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var households = await dbContext.Households
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new HouseholdOption(item.Id, item.HouseholdCode, item.Name))
            .ToArrayAsync(cancellationToken);

        var selected = households.FirstOrDefault(item => item.Id == householdId)
            ?? households.FirstOrDefault();

        var query = dbContext.Devices.AsNoTracking();
        if (selected is not null)
        {
            query = query.Where(item => item.HouseholdId == selected.Id);
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
