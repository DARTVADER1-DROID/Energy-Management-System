using System.Security.Cryptography;
using System.Text;
using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Modules.Identity;

public sealed class HouseholdInvitationService(
    IdentityDbContext identityDb,
    EnergyManagementDbContext energyDb,
    HouseholdAccessService householdAccess,
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<HouseholdInvitationService> logger)
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<HouseholdMemberSummary>> GetMembersAsync(
        int householdId, int administratorUserId, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(administratorUserId, householdId, cancellationToken);
        return await (
            from membership in identityDb.HouseholdMemberships.AsNoTracking()
            join user in identityDb.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.HouseholdId == householdId
            orderby user.Email
            select new HouseholdMemberSummary(
                user.Id, user.Email ?? string.Empty, membership.Role, membership.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HouseholdInvitationSummary>> GetPendingInvitationsAsync(
        int householdId, int administratorUserId, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(administratorUserId, householdId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return await identityDb.HouseholdInvitations.AsNoTracking()
            .Where(invitation => invitation.HouseholdId == householdId &&
                invitation.AcceptedAtUtc == null && invitation.RevokedAtUtc == null &&
                invitation.ExpiresAtUtc > now)
            .OrderByDescending(invitation => invitation.CreatedAtUtc)
            .Select(invitation => new HouseholdInvitationSummary(
                invitation.Id, invitation.Email, invitation.Role,
                invitation.CreatedAtUtc, invitation.ExpiresAtUtc))
            .ToArrayAsync(cancellationToken);
    }

    public async Task CreateAsync(
        int householdId, int administratorUserId, string email, HouseholdRole role,
        CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(administratorUserId, householdId, cancellationToken);
        ValidateRole(role);
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 256)
            throw new ArgumentException("Enter a valid email address.", nameof(email));

        var normalizedEmail = userManager.NormalizeEmail(email.Trim());
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new ArgumentException("Enter a valid email address.", nameof(email));

        var publicBaseUrl = configuration["EMS_PUBLIC_BASE_URL"];
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Invitation delivery requires an HTTPS EMS_PUBLIC_BASE_URL.");

        var existingUser = await userManager.FindByEmailAsync(email.Trim());
        if (existingUser is not null &&
            await householdAccess.IsMemberAsync(existingUser.Id, householdId, cancellationToken))
            throw new InvalidOperationException("This account is already a member of the selected household.");

        var now = DateTimeOffset.UtcNow;
        if (await identityDb.HouseholdInvitations.AnyAsync(invitation =>
            invitation.HouseholdId == householdId &&
            invitation.NormalizedEmail == normalizedEmail &&
            invitation.AcceptedAtUtc == null && invitation.RevokedAtUtc == null &&
            invitation.ExpiresAtUtc > now, cancellationToken))
            throw new InvalidOperationException("A pending invitation already exists for this email address.");

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var invitation = new HouseholdInvitation(
            householdId, email.Trim(), normalizedEmail, role, HashToken(token),
            administratorUserId, now.Add(InvitationLifetime));

        var householdName = await energyDb.Households.AsNoTracking()
            .Where(item => item.Id == householdId)
            .Select(item => item.Name)
            .SingleOrDefaultAsync(cancellationToken);
        if (householdName is null)
            throw new InvalidOperationException("The selected household no longer exists.");

        identityDb.HouseholdInvitations.Add(invitation);
        await identityDb.SaveChangesAsync(cancellationToken);

        var invitationUrl = $"{publicBaseUrl!.TrimEnd('/')}/Account/AcceptInvitation?token={Uri.EscapeDataString(token)}";
        var body = $"""
            You have been invited to join {householdName} in Energy Management System as a {DisplayRole(role)}.

            Accept this invitation within 24 hours:
            {invitationUrl}

            If you were not expecting this invitation, you can ignore this email.
            """;
        try
        {
            await emailSender.SendAsync(email.Trim(),
                "Your Energy Management System household invitation", body, cancellationToken);
        }
        catch (Exception exception)
        {
            await identityDb.HouseholdInvitations
                .Where(item => item.Id == invitation.Id && item.AcceptedAtUtc == null)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(item => item.RevokedAtUtc, DateTimeOffset.UtcNow),
                    CancellationToken.None);
            logger.LogError(exception,
                "Household invitation email delivery failed for household {HouseholdId}.", householdId);
            throw new InvalidOperationException(
                "The invitation could not be emailed. Check SMTP configuration and try again.");
        }
    }

    public async Task<HouseholdInvitationDetails?> GetInvitationDetailsAsync(
        string token, CancellationToken cancellationToken = default)
    {
        var invitation = await FindPendingInvitationAsync(token, cancellationToken);
        if (invitation is null ||
            !Enum.TryParse<HouseholdRole>(invitation.Role, out var role))
            return null;

        var household = await energyDb.Households.AsNoTracking()
            .Where(item => item.Id == invitation.HouseholdId)
            .Select(item => new { item.Id, item.Name })
            .SingleOrDefaultAsync(cancellationToken);
        return household is null ? null : new HouseholdInvitationDetails(
            household.Id, household.Name, invitation.Email, role, invitation.ExpiresAtUtc);
    }

    public async Task<ApplicationUser> AcceptForNewUserAsync(
        string token, string password, CancellationToken cancellationToken = default)
    {
        var invitation = await FindPendingInvitationAsync(token, cancellationToken)
            ?? throw new InvalidOperationException("This invitation is invalid, expired, or has already been used.");
        if (await userManager.FindByEmailAsync(invitation.Email) is not null)
            throw new InvalidOperationException("An account already exists for this email. Sign in to accept the invitation.");

        await using var transaction = await identityDb.Database.BeginTransactionAsync(cancellationToken);
        var user = new ApplicationUser
        {
            UserName = invitation.Email,
            Email = invitation.Email,
            EmailConfirmed = true
        };
        var createResult = await userManager.CreateAsync(user, password);
        EnsureIdentitySucceeded(createResult, "Unable to create the invited account.");

        await CompleteAcceptanceAsync(invitation, user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user;
    }

    public async Task AcceptForExistingUserAsync(
        string token, ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var invitation = await FindPendingInvitationAsync(token, cancellationToken)
            ?? throw new InvalidOperationException("This invitation is invalid, expired, or has already been used.");
        if (!string.Equals(userManager.NormalizeEmail(user.Email),
            invitation.NormalizedEmail, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Sign in with the email address that received this invitation.");

        await using var transaction = await identityDb.Database.BeginTransactionAsync(cancellationToken);
        await CompleteAcceptanceAsync(invitation, user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RevokeAsync(
        int householdId, int invitationId, int administratorUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(administratorUserId, householdId, cancellationToken);
        var updated = await identityDb.HouseholdInvitations
            .Where(invitation => invitation.Id == invitationId &&
                invitation.HouseholdId == householdId &&
                invitation.AcceptedAtUtc == null && invitation.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(invitation => invitation.RevokedAtUtc, DateTimeOffset.UtcNow),
                cancellationToken);
        if (updated != 1)
            throw new InvalidOperationException("The invitation is no longer pending.");
    }

    public async Task ChangeRoleAsync(
        int householdId, int memberUserId, int administratorUserId, HouseholdRole newRole,
        CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(administratorUserId, householdId, cancellationToken);
        ValidateRole(newRole);
        if (memberUserId == administratorUserId)
            throw new InvalidOperationException("You cannot change your own household role.");

        var membership = await identityDb.HouseholdMemberships.SingleOrDefaultAsync(item =>
            item.HouseholdId == householdId && item.UserId == memberUserId, cancellationToken)
            ?? throw new InvalidOperationException("The selected household member no longer exists.");

        var previousRole = membership.Role;
        if (previousRole == HouseholdRole.Administrator.ToString() &&
            newRole != HouseholdRole.Administrator)
        {
            var adminCount = await identityDb.HouseholdMemberships.CountAsync(item =>
                item.HouseholdId == householdId &&
                item.Role == HouseholdRole.Administrator.ToString(), cancellationToken);
            if (adminCount <= 1)
                throw new InvalidOperationException("A household must retain at least one administrator.");
        }

        await using var transaction = await identityDb.Database.BeginTransactionAsync(cancellationToken);
        membership.ChangeRole(newRole);
        await identityDb.SaveChangesAsync(cancellationToken);

        var user = await userManager.FindByIdAsync(memberUserId.ToString())
            ?? throw new InvalidOperationException("The selected account no longer exists.");
        if (newRole == HouseholdRole.Administrator)
        {
            await EnsureAdministratorIdentityRoleAsync(user);
        }
        else if (previousRole == HouseholdRole.Administrator.ToString())
        {
            var remainsAdministrator = await identityDb.HouseholdMemberships.AnyAsync(item =>
                item.UserId == memberUserId &&
                item.Role == HouseholdRole.Administrator.ToString(), cancellationToken);
            if (!remainsAdministrator && await userManager.IsInRoleAsync(user, "Administrator"))
                EnsureIdentitySucceeded(
                    await userManager.RemoveFromRoleAsync(user, "Administrator"),
                    "Unable to update the administrator sign-in role.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task CompleteAcceptanceAsync(
        HouseholdInvitation invitation, ApplicationUser user, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var accepted = await identityDb.HouseholdInvitations
            .Where(item => item.Id == invitation.Id &&
                item.AcceptedAtUtc == null && item.RevokedAtUtc == null &&
                item.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(item => item.AcceptedAtUtc, now), cancellationToken);
        if (accepted != 1)
            throw new InvalidOperationException("This invitation is invalid, expired, or has already been used.");

        if (!Enum.TryParse<HouseholdRole>(invitation.Role, out var role))
            throw new InvalidOperationException("The invitation contains an unsupported household role.");

        identityDb.HouseholdMemberships.Add(
            new HouseholdMembership(user.Id, invitation.HouseholdId, role));
        await identityDb.SaveChangesAsync(cancellationToken);
        if (role == HouseholdRole.Administrator)
            await EnsureAdministratorIdentityRoleAsync(user);
    }

    private async Task EnsureAdministratorIdentityRoleAsync(ApplicationUser user)
    {
        if (!await userManager.IsInRoleAsync(user, "Administrator"))
            EnsureIdentitySucceeded(await userManager.AddToRoleAsync(user, "Administrator"),
                "Unable to grant the administrator sign-in role.");
    }

    private async Task<HouseholdInvitation?> FindPendingInvitationAsync(
        string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
            return null;
        var hash = HashToken(token);
        var now = DateTimeOffset.UtcNow;
        return await identityDb.HouseholdInvitations.AsNoTracking()
            .SingleOrDefaultAsync(invitation => invitation.TokenHash == hash &&
                invitation.AcceptedAtUtc == null && invitation.RevokedAtUtc == null &&
                invitation.ExpiresAtUtc > now, cancellationToken);
    }

    private async Task EnsureAdministratorAsync(
        int userId, int householdId, CancellationToken cancellationToken)
    {
        if (!await householdAccess.IsAdministratorAsync(userId, householdId, cancellationToken))
            throw new UnauthorizedAccessException(
                "Only an administrator of this household can manage invitations and membership.");
    }

    private static void ValidateRole(HouseholdRole role)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role), "Choose a supported household role.");
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string DisplayRole(HouseholdRole role) => role switch
    {
        HouseholdRole.Administrator => "Administrator",
        HouseholdRole.HouseholdMember => "Household Member",
        HouseholdRole.ReadOnlyViewer => "Read-only Viewer",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static void EnsureIdentitySucceeded(IdentityResult result, string message)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"{message} {string.Join("; ", result.Errors.Select(error => error.Code))}");
    }
}

public sealed record HouseholdMemberSummary(
    int UserId, string Email, string Role, DateTimeOffset JoinedAtUtc);

public sealed record HouseholdInvitationSummary(
    int Id, string Email, string Role, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);

public sealed record HouseholdInvitationDetails(
    int HouseholdId, string HouseholdName, string Email, HouseholdRole Role, DateTimeOffset ExpiresAtUtc);
