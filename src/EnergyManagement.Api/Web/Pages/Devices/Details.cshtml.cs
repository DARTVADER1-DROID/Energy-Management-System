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

    public async Task<IActionResult> OnGetAsync(string deviceCode, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var device = await dashboardReadService.GetDeviceDetailsAsync(
            deviceCode, userId, cancellationToken);
        if (device is null) return NotFound();

        Device = device;
        CanManageDevice = await householdAccess.IsAdministratorAsync(
            userId, device.HouseholdId, cancellationToken);
        CanRotateDeviceCredential = CanManageDevice &&
            await deviceAuthenticationService.HasActiveCredentialAsync(device.Id, cancellationToken);
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

    public async Task<IActionResult> OnPostDeactivateAsync(
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

        if (device.IsActive)
        {
            device.Deactivate();
            await energyDb.SaveChangesAsync(cancellationToken);
            TempData["StatusMessage"] = "Device registration deactivated.";
        }

        return RedirectToPage(new { deviceCode });
    }
}
