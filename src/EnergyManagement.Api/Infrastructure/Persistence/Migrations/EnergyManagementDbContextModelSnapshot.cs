using EnergyManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnergyManagement.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EnergyManagementDbContext))]
public sealed class EnergyManagementDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63)
            .HasAnnotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

        modelBuilder.Entity<EnergyManagement.Api.Modules.Households.Household>(entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("integer")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            entity.Property<string>("Name")
                .IsRequired()
                .HasMaxLength(120)
                .HasColumnType("character varying(120)");

            entity.Property<int>("NextDeviceNumber")
                .IsConcurrencyToken()
                .HasColumnType("integer");

            entity.HasKey("Id");
            entity.ToTable("households");
        });

        modelBuilder.Entity<EnergyManagement.Api.Modules.Devices.Device>(entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("integer")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            entity.Property<string>("DeviceCode")
                .IsRequired()
                .HasMaxLength(32)
                .HasColumnType("character varying(32)");

            entity.Property<int>("DeviceNumber").HasColumnType("integer");
            entity.Property<string>("Description")
                .HasMaxLength(1000)
                .HasColumnType("character varying(1000)");

            entity.Property<EnergyManagement.Api.Modules.Devices.ElectricalConfiguration>("ElectricalConfiguration")
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(24)
                .HasColumnType("character varying(24)");

            entity.Property<int>("HouseholdId").HasColumnType("integer");

            entity.Property<string>("FirmwareVersion")
                .IsRequired()
                .HasMaxLength(80)
                .HasColumnType("character varying(80)");

            entity.Property<string>("HardwareModel")
                .IsRequired()
                .HasMaxLength(80)
                .HasColumnType("character varying(80)");

            entity.Property<bool>("IsActive").HasColumnType("boolean");

            entity.Property<string>("InstallationLocation")
                .IsRequired()
                .HasMaxLength(160)
                .HasColumnType("character varying(160)");

            entity.Property<DateTimeOffset>("RegisteredAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<DateTimeOffset?>("LastSeenAtUtc").HasColumnType("timestamp with time zone");
            entity.Property<string>("Name")
                .IsRequired()
                .HasMaxLength(120)
                .HasColumnType("character varying(120)");

            entity.HasKey("Id");
            entity.HasIndex("DeviceCode").IsUnique();
            entity.HasIndex("HouseholdId", "DeviceNumber").IsUnique();

            entity.OwnsOne("EnergyManagement.Api.Modules.Devices.DeviceThresholds", "Thresholds", owned =>
            {
                owned.Property<int>("DeviceId").HasColumnType("integer");
                owned.Property<decimal?>("MinimumVoltage").HasColumnType("numeric").HasColumnName("minimum_voltage");
                owned.Property<decimal?>("MaximumVoltage").HasColumnType("numeric").HasColumnName("maximum_voltage");
                owned.Property<decimal?>("MaximumCurrent").HasColumnType("numeric").HasColumnName("maximum_current");
                owned.Property<decimal?>("MaximumInstantaneousPower").HasColumnType("numeric").HasColumnName("maximum_instantaneous_power");
                owned.Property<decimal?>("MaximumDailyEnergy").HasColumnType("numeric").HasColumnName("maximum_daily_energy");
                owned.Property<decimal?>("MaximumTemperature").HasColumnType("numeric").HasColumnName("maximum_temperature");
                owned.Property<int?>("WarningPersistenceSeconds").HasColumnType("integer").HasColumnName("warning_persistence_seconds");
                owned.HasKey("DeviceId");
                owned.ToTable("devices");
                owned.WithOwner().HasForeignKey("DeviceId");
            });

            entity.HasOne<EnergyManagement.Api.Modules.Households.Household>()
                .WithMany()
                .HasForeignKey("HouseholdId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            entity.Navigation("Thresholds").IsRequired();
            entity.ToTable("devices");
        });
    }
}
