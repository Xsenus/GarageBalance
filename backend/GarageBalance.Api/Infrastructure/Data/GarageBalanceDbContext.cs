using System.Text.Json;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class GarageBalanceDbContext(DbContextOptions<GarageBalanceDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AppRole> Roles => Set<AppRole>();
    public DbSet<AppUserRole> UserRoles => Set<AppUserRole>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<Garage> Garages => Set<Garage>();
    public DbSet<SupplierGroup> SupplierGroups => Set<SupplierGroup>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("app_users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(500).IsRequired();
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<AppRole>(entity =>
        {
            entity.ToTable("app_roles");
            entity.HasKey(role => role.Id);
            entity.Property(role => role.Code).HasMaxLength(100).IsRequired();
            entity.Property(role => role.Name).HasMaxLength(200).IsRequired();
            entity.Property(role => role.Permissions)
                .HasConversion(
                    permissions => JsonSerializer.Serialize(permissions, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>())
                .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
                    value => value.ToList()));
            entity.HasIndex(role => role.Code).IsUnique();
        });

        modelBuilder.Entity<AppUserRole>(entity =>
        {
            entity.ToTable("app_user_roles");
            entity.HasKey(item => new { item.UserId, item.RoleId });
            entity.HasOne(item => item.User)
                .WithMany(user => user.UserRoles)
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Role)
                .WithMany(role => role.UserRoles)
                .HasForeignKey(item => item.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Action).HasMaxLength(120).IsRequired();
            entity.Property(item => item.EntityType).HasMaxLength(120).IsRequired();
            entity.Property(item => item.EntityId).HasMaxLength(120);
            entity.Property(item => item.Summary).HasMaxLength(1000).IsRequired();
            entity.HasIndex(item => item.CreatedAtUtc);
            entity.HasIndex(item => new { item.EntityType, item.EntityId });
        });

        modelBuilder.Entity<Owner>(entity =>
        {
            entity.ToTable("owners");
            entity.HasKey(owner => owner.Id);
            entity.Property(owner => owner.LastName).HasMaxLength(120).IsRequired();
            entity.Property(owner => owner.FirstName).HasMaxLength(120).IsRequired();
            entity.Property(owner => owner.MiddleName).HasMaxLength(120);
            entity.Property(owner => owner.Phone).HasMaxLength(80);
            entity.Property(owner => owner.Address).HasMaxLength(500);
            entity.Property(owner => owner.MeterNotes).HasMaxLength(1000);
            entity.HasIndex(owner => new { owner.LastName, owner.FirstName, owner.MiddleName });
            entity.HasIndex(owner => owner.Phone);
        });

        modelBuilder.Entity<Garage>(entity =>
        {
            entity.ToTable("garages");
            entity.HasKey(garage => garage.Id);
            entity.Property(garage => garage.Number).HasMaxLength(80).IsRequired();
            entity.Property(garage => garage.InitialWaterMeterValue).HasPrecision(18, 3);
            entity.Property(garage => garage.InitialElectricityMeterValue).HasPrecision(18, 3);
            entity.Property(garage => garage.Comment).HasMaxLength(1000);
            entity.HasIndex(garage => garage.Number).IsUnique();
            entity.HasIndex(garage => garage.OwnerId);
            entity.HasOne(garage => garage.Owner)
                .WithMany(owner => owner.Garages)
                .HasForeignKey(garage => garage.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SupplierGroup>(entity =>
        {
            entity.ToTable("supplier_groups");
            entity.HasKey(group => group.Id);
            entity.Property(group => group.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(group => group.Name).IsUnique();
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.ToTable("suppliers");
            entity.HasKey(supplier => supplier.Id);
            entity.Property(supplier => supplier.Name).HasMaxLength(240).IsRequired();
            entity.Property(supplier => supplier.Inn).HasMaxLength(20);
            entity.Property(supplier => supplier.LegalAddress).HasMaxLength(500);
            entity.Property(supplier => supplier.ContactPerson).HasMaxLength(200);
            entity.Property(supplier => supplier.Phone).HasMaxLength(80);
            entity.Property(supplier => supplier.Email).HasMaxLength(320);
            entity.Property(supplier => supplier.StartingBalance).HasPrecision(18, 2);
            entity.Property(supplier => supplier.Comment).HasMaxLength(1000);
            entity.HasIndex(supplier => supplier.Name);
            entity.HasIndex(supplier => supplier.GroupId);
            entity.HasOne(supplier => supplier.Group)
                .WithMany(group => group.Suppliers)
                .HasForeignKey(supplier => supplier.GroupId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
