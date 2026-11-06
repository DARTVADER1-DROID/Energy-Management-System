using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Infrastructure.IdentityPersistence;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>(options)
{
    public DbSet<HouseholdMembership> HouseholdMemberships => Set<HouseholdMembership>();

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

        modelBuilder.Entity<HouseholdMembership>(entity =>
        {
            entity.ToTable("household_memberships");
            entity.HasKey(membership => membership.Id);
            entity.HasIndex(membership => new { membership.UserId, membership.HouseholdId }).IsUnique();
            entity.Property(membership => membership.Role).HasMaxLength(32).IsRequired();
            entity.Property(membership => membership.CreatedAtUtc).IsRequired();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(membership => membership.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
