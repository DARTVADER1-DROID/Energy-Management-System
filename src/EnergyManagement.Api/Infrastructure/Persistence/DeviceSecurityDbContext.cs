using EnergyManagement.Api.Modules.Devices;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Infrastructure.Persistence;

public sealed class DeviceSecurityDbContext(
    DbContextOptions<DeviceSecurityDbContext> options) : DbContext(options)
{
    public DbSet<DeviceProvisioningCode> ProvisioningCodes => Set<DeviceProvisioningCode>();
    public DbSet<DeviceCredential> Credentials => Set<DeviceCredential>();
    public DbSet<DeviceRequestNonce> RequestNonces => Set<DeviceRequestNonce>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceProvisioningCode>(entity =>
        {
            entity.ToTable("device_provisioning_codes");
            entity.HasKey(code => code.Id);
            entity.HasIndex(code => code.CodeHash).IsUnique();
            entity.HasIndex(code => code.DeviceId)
                .IsUnique()
                .HasFilter("\"UsedAtUtc\" IS NULL AND \"RevokedAtUtc\" IS NULL");
            entity.HasIndex(code => new { code.DeviceId, code.ExpiresAtUtc });
            entity.Property(code => code.CodeHash).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<DeviceCredential>(entity =>
        {
            entity.ToTable("device_credentials");
            entity.HasKey(credential => credential.Id);
            entity.HasIndex(credential => new { credential.DeviceId, credential.RevokedAtUtc });
            entity.HasIndex(
                    credential => credential.DeviceId,
                    "IX_device_credentials_DeviceId_Current")
                .IsUnique()
                .HasFilter("\"IsPendingRotation\" = FALSE AND \"RevokedAtUtc\" IS NULL");
            entity.HasIndex(
                    credential => credential.DeviceId,
                    "IX_device_credentials_DeviceId")
                .IsUnique()
                .HasFilter("\"IsPendingRotation\" = TRUE AND \"RevokedAtUtc\" IS NULL");
            entity.Property(credential => credential.SecretHash).HasMaxLength(64).IsRequired();
            entity.Property(credential => credential.ProtectedSigningKey).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<DeviceRequestNonce>(entity =>
        {
            entity.ToTable("device_request_nonces");
            entity.HasKey(nonce => nonce.Id);
            entity.HasIndex(nonce => new { nonce.DeviceId, nonce.Nonce }).IsUnique();
            entity.HasIndex(nonce => nonce.CreatedAtUtc);
            entity.Property(nonce => nonce.Nonce).HasMaxLength(64).IsRequired();
        });
    }
}
