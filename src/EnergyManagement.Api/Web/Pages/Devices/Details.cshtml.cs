using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Identity;
using EnergyManagement.Api.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Web.Pages.Devices;

public sealed class DetailsModel(
    DashboardReadService dashboardReadService,
    EnergyManagementDbContext energyDb,
    DeviceSecurityDbContext deviceSecurityDb,
    IdentityDbContext identityDb,
    HouseholdAccessService householdAccess,
    DeviceAuthenticationService deviceAuthenticationService) : PageModel
{
    public DeviceDetails Device { get; private set; } = null!;
    public bool CanManageDevice { get; private set; }
    public string? ProvisioningCode { get; private set; }
    public DateTimeOffset? ProvisioningCodeExpiresAtUtc { get; private set; }
    public bool CanRotateDeviceCredential { get; private set; }
    public string? RotatedDeviceSecret { get; private set; }
    public DateTimeOffset? RotationExpiresAtUtc { get; private set; }
    public IReadOnlyList<DeviceSecurityAuditSummary> SecurityEvents { get; private set; } = [];

    [BindProperty]
    public DeviceEditInput Edit { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string deviceCode, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var device = await dashboardReadService.GetDeviceDetailsAsync(
            deviceCode, userId, cancellationToken);
        if (device is null) return NotFound();

        Device = device;
        Edit = new DeviceEditInput
        {
            Name = device.Name,
            HardwareModel = device.HardwareModel,
            FirmwareVersion = device.FirmwareVersion,
            InstallationLocation = device.InstallationLocation,
            ElectricalConfiguration = device.ElectricalConfiguration,
            Description = device.Description,
            MinimumVoltage = device.MinimumVoltage,
            MaximumVoltage = device.MaximumVoltage,
            MaximumCurrent = device.MaximumCurrent,
            MaximumInstantaneousPower = device.MaximumInstantaneousPower,
            MaximumDailyEnergy = device.MaximumDailyEnergy,
            MaximumTemperature = device.MaximumTemperature,
            WarningPersistenceSeconds = device.WarningPersistenceSeconds
        };
        CanManageDevice = await householdAccess.IsAdministratorAsync(
            userId, device.HouseholdId, cancellationToken);
        CanRotateDeviceCredential = CanManageDevice &&
            await deviceAuthenticationService.HasActiveCredentialAsync(device.Id, cancellationToken);
        if (CanManageDevice)
        {
            var auditEvents = await deviceSecurityDb.AuditEvents.AsNoTracking()
                .Where(item => item.DeviceId == device.Id)
                .OrderByDescending(item => item.CreatedAtUtc)
                .Take(20)
                .ToArrayAsync(cancellationToken);
            var actorIds = auditEvents
                .Where(item => item.ActorUserId.HasValue)
                .Select(item => item.ActorUserId!.Value)
                .Distinct()
                .ToArray();
            var actorEmails = await identityDb.Users.AsNoTracking()
                .Where(user => actorIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.Email ?? "Unknown account",
                    cancellationToken);
            SecurityEvents = auditEvents.Select(item => new DeviceSecurityAuditSummary(
                item.EventType,
                item.ActorUserId.HasValue && actorEmails.TryGetValue(item.ActorUserId.Value, out var email)
                    ? email
                    : item.ActorUserId.HasValue ? "Former account" : "Device",
                item.Details,
                item.CreatedAtUtc)).ToArray();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostProvisionAsync(
        string deviceCode, CancellationToken cancellationToken)
    {
        if (!User.HasRecentAuthentication())
        {
            var returnUrl = Url.Page("/Devices/Details", new { deviceCode });
            return RedirectToPage("/Account/Reauthenticate", new { returnUrl });
        }

        try
        {
            var result = await deviceAuthenticationService.CreateProvisioningCodeAsync(
                deviceCode, User.GetUserId(), householdAccess, cancellationToken);
            ProvisioningCode = result.Code;
            ProvisioningCodeExpiresAtUtc = result.ExpiresAtUtc;
            Response.Headers["Cache-Control"] = "no-store";
            Response.Headers["Pragma"] = "no-cache";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return await OnGetAsync(deviceCode, cancellationToken);
    }

    public async Task<IActionResult> OnPostRotateCredentialAsync(
        string deviceCode, CancellationToken cancellationToken)
    {
        if (!User.HasRecentAuthentication())
        {
            var returnUrl = Url.Page("/Devices/Details", new { deviceCode });
            return RedirectToPage("/Account/Reauthenticate", new { returnUrl });
        }

        try
        {
            var result = await deviceAuthenticationService.CreateCredentialRotationAsync(
                deviceCode, User.GetUserId(), householdAccess, cancellationToken);
            RotatedDeviceSecret = result.NewDeviceSecret;
            RotationExpiresAtUtc = result.ExpiresAtUtc;
            Response.Headers["Cache-Control"] = "no-store";
            Response.Headers["Pragma"] = "no-cache";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return await OnGetAsync(deviceCode, cancellationToken);
    }

    public async Task<IActionResult> OnPostRevokeCredentialsAsync(
        string deviceCode, CancellationToken cancellationToken)
    {
        if (!User.HasRecentAuthentication())
        {
            var returnUrl = Url.Page("/Devices/Details", new { deviceCode });
            return RedirectToPage("/Account/Reauthenticate", new { returnUrl });
        }

        try
        {
            var count = await deviceAuthenticationService.RevokeCredentialsAsync(
                deviceCode, User.GetUserId(), householdAccess, cancellationToken);
            TempData["StatusMessage"] = count > 0
                ? $"Revoked {count} device credential(s). The device must be provisioned again before it can authenticate."
                : "There were no active device credentials to revoke.";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return RedirectToPage(new { deviceCode });
    }

    public async Task<IActionResult> OnPostUpdateAsync(
        string deviceCode, CancellationToken cancellationToken)
    {
        var device = await energyDb.Devices.SingleOrDefaultAsync(
            item => item.DeviceCode == deviceCode, cancellationToken);
        if (device is null) return NotFound();

        var userId = User.GetUserId();
        if (!await householdAccess.IsMemberAsync(userId, device.HouseholdId, cancellationToken))
            return NotFound();
        if (!await householdAccess.IsAdministratorAsync(userId, device.HouseholdId, cancellationToken))
            return Forbid();
        if (!User.HasRecentAuthentication())
        {
            var returnUrl = Url.Page("/Devices/Details", new { deviceCode });
            return RedirectToPage("/Account/Reauthenticate", new { returnUrl });
        }

        if (!ModelState.IsValid || !Enum.IsDefined(Edit.ElectricalConfiguration))
        {
            if (!Enum.IsDefined(Edit.ElectricalConfiguration))
                ModelState.AddModelError("Edit.ElectricalConfiguration", "Choose a supported electrical configuration.");
            return await OnGetAsync(deviceCode, cancellationToken);
        }

        try
        {
            device.UpdateMetadata(Edit.Name, Edit.HardwareModel, Edit.FirmwareVersion,
                Edit.InstallationLocation, Edit.ElectricalConfiguration, Edit.Description);
            device.UpdateThresholds(new DeviceThresholds
            {
                MinimumVoltage = Edit.MinimumVoltage,
                MaximumVoltage = Edit.MaximumVoltage,
                MaximumCurrent = Edit.MaximumCurrent,
                MaximumInstantaneousPower = Edit.MaximumInstantaneousPower,
                MaximumDailyEnergy = Edit.MaximumDailyEnergy,
                MaximumTemperature = Edit.MaximumTemperature,
                WarningPersistenceSeconds = Edit.WarningPersistenceSeconds
            });
            await energyDb.SaveChangesAsync(cancellationToken);
            TempData["StatusMessage"] = "Device configuration updated.";
            return RedirectToPage(new { deviceCode });
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await OnGetAsync(deviceCode, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostRemoveAsync(
        string deviceCode, CancellationToken cancellationToken)
    {
        var device = await energyDb.Devices.SingleOrDefaultAsync(
            item => item.DeviceCode == deviceCode, cancellationToken);
        if (device is null) return NotFound();

        var userId = User.GetUserId();
        if (!await householdAccess.IsMemberAsync(userId, device.HouseholdId, cancellationToken))
            return NotFound();
        if (!await householdAccess.IsAdministratorAsync(userId, device.HouseholdId, cancellationToken))
            return Forbid();

        if (!User.HasRecentAuthentication())
        {
            var returnUrl = Url.Page("/Devices/Details", new { deviceCode });
            return RedirectToPage("/Account/Reauthenticate", new { returnUrl });
        }

        int revoked;
        try
        {
            // Removal and all credential-issuing operations share a per-device
            // PostgreSQL advisory lock inside DeviceAuthenticationService.
            revoked = await deviceAuthenticationService.RemoveDeviceAsync(
                deviceCode, userId, householdAccess, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        TempData["StatusMessage"] =
            $"Device permanently deleted. {revoked} credential(s) and all provisioning data were purged; associated telemetry was deleted. Security audit history was retained.";
        return RedirectToPage("/Devices/Index", new { householdId = device.HouseholdId });
    }

    public async Task<IActionResult> OnPostDeactivateAsync(
        string deviceCode, CancellationToken cancellationToken)
    {
        // Keep the legacy handler safe for stale clients: it now follows the same
        // serialized removal path and cannot bypass credential revocation.
        return await OnPostRemoveAsync(deviceCode, cancellationToken);
    }
}


public sealed record DeviceSecurityAuditSummary(
    string EventType, string Actor, string Details, DateTimeOffset CreatedAtUtc);

public sealed class DeviceEditInput
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(120)]
    public string Name { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(80)]
    public string HardwareModel { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(80)]
    public string FirmwareVersion { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(160)]
    public string InstallationLocation { get; set; } = string.Empty;
    public ElectricalConfiguration ElectricalConfiguration { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(1000)]
    public string? Description { get; set; }
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "100000")]
    public decimal? MinimumVoltage { get; set; }
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "100000")]
    public decimal? MaximumVoltage { get; set; }
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "100000")]
    public decimal? MaximumCurrent { get; set; }
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "100000000")]
    public decimal? MaximumInstantaneousPower { get; set; }
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "100000000")]
    public decimal? MaximumDailyEnergy { get; set; }
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "10000")]
    public decimal? MaximumTemperature { get; set; }
    [System.ComponentModel.DataAnnotations.Range(1, 86400)]
    public int? WarningPersistenceSeconds { get; set; }
}
