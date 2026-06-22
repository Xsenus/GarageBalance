using System.Text.Json;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Import;
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
    public DbSet<IncomeType> IncomeTypes => Set<IncomeType>();
    public DbSet<ExpenseType> ExpenseTypes => Set<ExpenseType>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<FinancialOperation> FinancialOperations => Set<FinancialOperation>();
    public DbSet<Accrual> Accruals => Set<Accrual>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<AccessImportRun> AccessImportRuns => Set<AccessImportRun>();

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

        modelBuilder.Entity<IncomeType>(entity =>
        {
            entity.ToTable("income_types");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Code).HasMaxLength(80);
            entity.HasIndex(item => item.Name).IsUnique();
            entity.HasIndex(item => item.Code);
        });

        modelBuilder.Entity<ExpenseType>(entity =>
        {
            entity.ToTable("expense_types");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Code).HasMaxLength(80);
            entity.HasIndex(item => item.Name).IsUnique();
            entity.HasIndex(item => item.Code);
        });

        modelBuilder.Entity<Tariff>(entity =>
        {
            entity.ToTable("tariffs");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.CalculationBase).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Rate).HasPrecision(18, 4);
            entity.Property(item => item.Comment).HasMaxLength(1000);
            entity.HasIndex(item => new { item.Name, item.EffectiveFrom }).IsUnique();
            entity.HasIndex(item => item.CalculationBase);
            entity.HasIndex(item => item.EffectiveFrom);
        });

        modelBuilder.Entity<FinancialOperation>(entity =>
        {
            entity.ToTable("financial_operations");
            entity.HasKey(operation => operation.Id);
            entity.Property(operation => operation.OperationKind).HasMaxLength(20).IsRequired();
            entity.Property(operation => operation.Amount).HasPrecision(18, 2);
            entity.Property(operation => operation.DocumentNumber).HasMaxLength(120);
            entity.Property(operation => operation.Comment).HasMaxLength(1000);
            entity.HasIndex(operation => operation.OperationDate);
            entity.HasIndex(operation => operation.AccountingMonth);
            entity.HasIndex(operation => operation.OperationKind);
            entity.HasIndex(operation => new { operation.OperationKind, operation.OperationDate, operation.DocumentNumber });
            entity.HasIndex(operation => operation.GarageId);
            entity.HasIndex(operation => operation.SupplierId);
            entity.HasOne(operation => operation.Garage)
                .WithMany()
                .HasForeignKey(operation => operation.GarageId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(operation => operation.IncomeType)
                .WithMany()
                .HasForeignKey(operation => operation.IncomeTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(operation => operation.Supplier)
                .WithMany()
                .HasForeignKey(operation => operation.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(operation => operation.ExpenseType)
                .WithMany()
                .HasForeignKey(operation => operation.ExpenseTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Accrual>(entity =>
        {
            entity.ToTable("accruals");
            entity.HasKey(accrual => accrual.Id);
            entity.Property(accrual => accrual.Amount).HasPrecision(18, 2);
            entity.Property(accrual => accrual.Source).HasMaxLength(40).IsRequired();
            entity.Property(accrual => accrual.Comment).HasMaxLength(1000);
            entity.HasIndex(accrual => accrual.AccountingMonth);
            entity.HasIndex(accrual => accrual.GarageId);
            entity.HasIndex(accrual => accrual.IncomeTypeId);
            entity.HasIndex(accrual => new { accrual.GarageId, accrual.IncomeTypeId, accrual.AccountingMonth, accrual.Source }).IsUnique();
            entity.HasOne(accrual => accrual.Garage)
                .WithMany()
                .HasForeignKey(accrual => accrual.GarageId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(accrual => accrual.IncomeType)
                .WithMany()
                .HasForeignKey(accrual => accrual.IncomeTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MeterReading>(entity =>
        {
            entity.ToTable("meter_readings");
            entity.HasKey(reading => reading.Id);
            entity.Property(reading => reading.MeterKind).HasMaxLength(40).IsRequired();
            entity.Property(reading => reading.CurrentValue).HasPrecision(18, 3);
            entity.Property(reading => reading.PreviousValue).HasPrecision(18, 3);
            entity.Property(reading => reading.Consumption).HasPrecision(18, 3);
            entity.Property(reading => reading.Comment).HasMaxLength(1000);
            entity.HasIndex(reading => reading.AccountingMonth);
            entity.HasIndex(reading => reading.ReadingDate);
            entity.HasIndex(reading => reading.GarageId);
            entity.HasIndex(reading => new { reading.GarageId, reading.MeterKind, reading.AccountingMonth }).IsUnique();
            entity.HasOne(reading => reading.Garage)
                .WithMany()
                .HasForeignKey(reading => reading.GarageId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AccessImportRun>(entity =>
        {
            entity.ToTable("access_import_runs");
            entity.HasKey(run => run.Id);
            entity.Property(run => run.Mode).HasMaxLength(40).IsRequired();
            entity.Property(run => run.Status).HasMaxLength(40).IsRequired();
            entity.Property(run => run.OriginalFileName).HasMaxLength(260).IsRequired();
            entity.Property(run => run.FileExtension).HasMaxLength(20).IsRequired();
            entity.Property(run => run.ContentSha256).HasMaxLength(64).IsRequired();
            entity.Property(run => run.Summary).HasMaxLength(1000).IsRequired();
            entity.Property(run => run.ReportJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(run => run.StartedAtUtc);
            entity.HasIndex(run => run.Status);
            entity.HasIndex(run => run.ContentSha256);
        });
    }
}
