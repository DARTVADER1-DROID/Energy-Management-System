using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Households;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Infrastructure.Persistence;

public sealed class EnergyManagementDbContext(
    DbContextOptions<EnergyManagementDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>(options)
{
    public DbSet<Household> Households => Set<Household>();

    public DbSet<Device> Devices => Set<Device>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(user => user.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<IdentityRole<int>>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<int>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<int>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<int>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityRoleClaim<int>>().ToTable("role_claims");
        modelBuilder.Entity<IdentityUserToken<int>>().ToTable("user_tokens");

        modelBuilder.Entity<Household>(entity =>
        {
            entity.ToTable("households");
            entity.HasKey(household => household.Id);
            entity.Property(household => household.Name).HasMaxLength(120).IsRequired();
            entity.Property(household => household.NextDeviceNumber).IsConcurrencyToken();
            entity.Ignore(household => household.HouseholdCode);
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.ToTable("devices");
            entity.HasKey(device => device.Id);
            entity.HasIndex(device => device.DeviceCode).IsUnique();
            entity.HasIndex(device => new { device.HouseholdId, device.DeviceNumber }).IsUnique();
            entity.Property(device => device.DeviceCode).HasMaxLength(32).IsRequired();
            entity.Property(device => device.Name).HasMaxLength(120).IsRequired();
            entity.Property(device => device.HardwareModel).HasMaxLength(80).IsRequired();
            entity.Property(device => device.FirmwareVersion).HasMaxLength(80).IsRequired();
            entity.Property(device => device.InstallationLocation).HasMaxLength(160).IsRequired();
            entity.Property(device => device.Description).HasMaxLength(1000);
            entity.Property(device => device.ElectricalConfiguration).HasConversion<string>().HasMaxLength(24);
            entity.OwnsOne(device => device.Thresholds, thresholds =>
            {
                thresholds.Property(value => value.MinimumVoltage).HasColumnName("minimum_voltage");
                thresholds.Property(value => value.MaximumVoltage).HasColumnName("maximum_voltage");
                thresholds.Property(value => value.MaximumCurrent).HasColumnName("maximum_current");
                thresholds.Property(value => value.MaximumInstantaneousPower).HasColumnName("maximum_instantaneous_power");
                thresholds.Property(value => value.MaximumDailyEnergy).HasColumnName("maximum_daily_energy");
                thresholds.Property(value => value.MaximumTemperature).HasColumnName("maximum_temperature");
                thresholds.Property(value => value.WarningPersistenceSeconds).HasColumnName("warning_persistence_seconds");
            });
            entity.HasOne<Household>()
                .WithMany()
                .HasForeignKey(device => device.HouseholdId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
