using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Infrastructure.IdentityPersistence;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>(options)
{
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
    }
}
