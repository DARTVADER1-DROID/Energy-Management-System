using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnergyManagement.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DeviceSecurityDbContext))]
public sealed class DeviceSecurityDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63)
            .HasAnnotation("Npgsql:ValueGenerationStrategy",
                Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

        modelBuilder.Entity<DeviceProvisioningCode>(entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            entity.Property<int>("DeviceId").HasColumnType("integer");
            entity.Property<string>("CodeHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            entity.Property<int>("CreatedByUserId").HasColumnType("integer");
            entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset>("ExpiresAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset?>("UsedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset?>("RevokedAtUtc").HasColumnType("timestamp with time zone");
            entity.HasKey("Id");
            entity.HasIndex("CodeHash").IsUnique();
            entity.HasIndex("DeviceId").IsUnique().HasFilter("\"UsedAtUtc\" IS NULL AND \"RevokedAtUtc\" IS NULL");
            entity.HasIndex("DeviceId", "ExpiresAtUtc");
            entity.ToTable("device_provisioning_codes");
        });

        modelBuilder.Entity<DeviceCredential>(entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            entity.Property<int>("DeviceId").HasColumnType("integer");
            entity.Property<string>("SecretHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            entity.Property<string>("ProtectedSigningKey").IsRequired().HasMaxLength(256).HasColumnType("character varying(256)");
            entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<bool>("IsPendingRotation").HasDefaultValue(false).HasColumnType("boolean");
            entity.Property<DateTimeOffset?>("RotationExpiresAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset?>("ConfirmedAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset?>("RevokedAtUtc").HasColumnType("timestamp with time zone");
            entity.HasKey("Id");
            entity.HasIndex("DeviceId", "RevokedAtUtc");
            entity.HasIndex("DeviceId", "IsPendingRotation")
                .IsUnique()
                .HasFilter("\"RevokedAtUtc\" IS NULL");
            entity.ToTable("device_credentials");
        });

        modelBuilder.Entity<DeviceRequestNonce>(entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            entity.Property<int>("DeviceId").HasColumnType("integer");
            entity.Property<string>("Nonce").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone");
            entity.HasKey("Id");
            entity.HasIndex("DeviceId", "Nonce").IsUnique();
            entity.HasIndex("CreatedAtUtc");
            entity.ToTable("device_request_nonces");
        });

        modelBuilder.Entity<DeviceSecurityAuditEvent>(entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            entity.Property<int>("DeviceId").HasColumnType("integer");
            entity.Property<int?>("ActorUserId").HasColumnType("integer");
            entity.Property<string>("EventType").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            entity.Property<string>("Details").IsRequired().HasMaxLength(256).HasColumnType("character varying(256)");
            entity.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone");
            entity.HasKey("Id");
            entity.HasIndex("DeviceId", "CreatedAtUtc");
            entity.ToTable("device_security_audit_events");
        });
    }
}
