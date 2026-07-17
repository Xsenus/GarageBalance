using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Import;
using GarageBalance.Api.Domain.Integrations;
using GarageBalance.Api.Domain.Releases;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Domain.Workflows;
using GarageBalance.Api.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class GarageBalanceDbContext(DbContextOptions<GarageBalanceDbContext> options) : DbContext(options), IAuditEventStore
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AppRole> Roles => Set<AppRole>();
    public DbSet<AppUserRole> UserRoles => Set<AppUserRole>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<Garage> Garages => Set<Garage>();
    public DbSet<SupplierGroup> SupplierGroups => Set<SupplierGroup>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierContact> SupplierContacts => Set<SupplierContact>();
    public DbSet<StaffDepartment> StaffDepartments => Set<StaffDepartment>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<IncomeType> IncomeTypes => Set<IncomeType>();
    public DbSet<ExpenseType> ExpenseTypes => Set<ExpenseType>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<ChargeServiceSetting> ChargeServiceSettings => Set<ChargeServiceSetting>();
    public DbSet<IrregularPayment> IrregularPayments => Set<IrregularPayment>();
    public DbSet<FeeCampaign> FeeCampaigns => Set<FeeCampaign>();
    public DbSet<FeeCampaignGarage> FeeCampaignGarages => Set<FeeCampaignGarage>();
    public DbSet<FinancialOperation> FinancialOperations => Set<FinancialOperation>();
    public DbSet<Accrual> Accruals => Set<Accrual>();
    public DbSet<AccrualPaymentAllocation> AccrualPaymentAllocations => Set<AccrualPaymentAllocation>();
    public DbSet<SupplierAccrual> SupplierAccruals => Set<SupplierAccrual>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<FundOperation> FundOperations => Set<FundOperation>();
    public DbSet<FormState> FormStates => Set<FormState>();
    public DbSet<AccessImportRun> AccessImportRuns => Set<AccessImportRun>();
    public DbSet<AccessImportRunLogEntry> AccessImportRunLogEntries => Set<AccessImportRunLogEntry>();
    public DbSet<AccessImportRowFingerprint> AccessImportRowFingerprints => Set<AccessImportRowFingerprint>();
    public DbSet<AccessImportQuarantineItem> AccessImportQuarantineItems => Set<AccessImportQuarantineItem>();
    public DbSet<AccessImportCreatedRecord> AccessImportCreatedRecords => Set<AccessImportCreatedRecord>();
    public DbSet<IntegrationSecretSetting> IntegrationSecretSettings => Set<IntegrationSecretSetting>();
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();
    public DbSet<AppReleaseRecord> AppReleases => Set<AppReleaseRecord>();

    void IAuditEventStore.Add(AuditEvent auditEvent)
    {
        Set<AuditEvent>().Add(auditEvent);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeAuditEventRelatedFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeAuditEventRelatedFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

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

        modelBuilder.Entity<ApplicationSetting>(entity =>
        {
            entity.ToTable("application_settings");
            entity.HasKey(setting => setting.Id);
            entity.Property(setting => setting.Key).HasMaxLength(160).IsRequired();
            entity.HasIndex(setting => setting.Key).IsUnique();
        });

        modelBuilder.Entity<AppReleaseRecord>(entity =>
        {
            entity.ToTable("app_releases");
            entity.HasKey(release => release.ReleaseId);
            entity.Property(release => release.ReleaseId).HasMaxLength(180);
            entity.Property(release => release.Version).HasMaxLength(80).IsRequired();
            entity.Property(release => release.Title).HasMaxLength(300).IsRequired();
            entity.Property(release => release.Summary).HasMaxLength(2000).IsRequired();
            entity.Property(release => release.ItemsJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(release => release.Version).IsUnique();
            entity.HasIndex(release => new { release.IsPublished, release.PublishedAt });
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
            entity.Property(item => item.Section).HasMaxLength(80);
            entity.Property(item => item.ActionKind).HasMaxLength(40);
            entity.Property(item => item.EntityType).HasMaxLength(120).IsRequired();
            entity.Property(item => item.EntityId).HasMaxLength(120);
            entity.Property(item => item.EntityDisplayName).HasMaxLength(256);
            entity.Property(item => item.RelatedGarageId).HasMaxLength(120);
            entity.Property(item => item.RelatedGarageNumber).HasMaxLength(80);
            entity.Property(item => item.RelatedAccountingMonth).HasMaxLength(32);
            entity.Property(item => item.RelatedCounterpartyId).HasMaxLength(120);
            entity.Property(item => item.RelatedCounterpartyName).HasMaxLength(256);
            entity.Property(item => item.RelatedDocumentId).HasMaxLength(120);
            entity.Property(item => item.RelatedDocumentNumber).HasMaxLength(120);
            entity.Property(item => item.Summary).HasMaxLength(1000).IsRequired();
            entity.HasIndex(item => item.CreatedAtUtc);
            entity.HasIndex(item => item.ActorUserId);
            entity.HasIndex(item => item.Section);
            entity.HasIndex(item => item.ActionKind);
            entity.HasIndex(item => new { item.EntityType, item.EntityId });
            entity.HasIndex(item => new { item.Section, item.ActionKind, item.CreatedAtUtc });
            entity.HasIndex(item => item.RelatedGarageId);
            entity.HasIndex(item => item.RelatedGarageNumber);
            entity.HasIndex(item => item.RelatedAccountingMonth);
            entity.HasIndex(item => item.RelatedCounterpartyId);
            entity.HasIndex(item => item.RelatedCounterpartyName);
            entity.HasIndex(item => item.RelatedDocumentId);
            entity.HasIndex(item => item.RelatedDocumentNumber);
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
            entity.Property(garage => garage.StartingBalance).HasPrecision(18, 2);
            entity.Property(garage => garage.InitialWaterMeterValue).HasPrecision(18, 3);
            entity.Property(garage => garage.InitialElectricityMeterValue).HasPrecision(18, 3);
            entity.Property(garage => garage.Comment).HasMaxLength(1000);
            entity.HasIndex(garage => garage.Number).IsUnique().HasFilter("\"IsArchived\" = false");
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
            entity.HasIndex(group => group.Name).IsUnique().HasFilter("\"IsArchived\" = false");
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
            entity.HasIndex(supplier => new { supplier.GroupId, supplier.Name }).IsUnique().HasFilter("\"IsArchived\" = false");
            entity.HasIndex(supplier => supplier.Inn);
            entity.HasIndex(supplier => supplier.ContactPerson);
            entity.HasIndex(supplier => supplier.GroupId);
            entity.HasIndex(supplier => supplier.ChargeServiceSettingId);
            entity.HasOne(supplier => supplier.Group)
                .WithMany(group => group.Suppliers)
                .HasForeignKey(supplier => supplier.GroupId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(supplier => supplier.ChargeServiceSetting)
                .WithMany()
                .HasForeignKey(supplier => supplier.ChargeServiceSettingId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SupplierContact>(entity =>
        {
            entity.ToTable("supplier_contacts");
            entity.HasKey(contact => contact.Id);
            entity.Property(contact => contact.FullName).HasMaxLength(200).IsRequired();
            entity.Property(contact => contact.Position).HasMaxLength(160);
            entity.Property(contact => contact.Phone).HasMaxLength(80);
            entity.Property(contact => contact.Email).HasMaxLength(320);
            entity.Property(contact => contact.Status).HasMaxLength(40).IsRequired();
            entity.Property(contact => contact.Comment).HasMaxLength(1000);
            entity.HasIndex(contact => contact.SupplierId);
            entity.HasIndex(contact => contact.FullName);
            entity.HasIndex(contact => contact.Phone);
            entity.HasIndex(contact => contact.Email);
            entity.HasIndex(contact => contact.Status);
            entity.HasOne(contact => contact.Supplier)
                .WithMany()
                .HasForeignKey(contact => contact.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StaffDepartment>(entity =>
        {
            entity.ToTable("staff_departments");
            entity.HasKey(department => department.Id);
            entity.Property(department => department.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(department => department.Name).IsUnique().HasFilter("\"IsArchived\" = false");
        });

        modelBuilder.Entity<StaffMember>(entity =>
        {
            entity.ToTable("staff_members");
            entity.HasKey(member => member.Id);
            entity.Property(member => member.FullName).HasMaxLength(200).IsRequired();
            entity.Property(member => member.Rate).HasPrecision(18, 2);
            entity.HasIndex(member => member.FullName);
            entity.HasIndex(member => member.DepartmentId);
            entity.HasOne(member => member.Department)
                .WithMany(department => department.StaffMembers)
                .HasForeignKey(member => member.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IncomeType>(entity =>
        {
            entity.ToTable("income_types");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Code).HasMaxLength(80);
            entity.HasIndex(item => item.Name).IsUnique().HasFilter("\"IsArchived\" = false");
            entity.HasIndex(item => item.Code);
        });

        modelBuilder.Entity<ExpenseType>(entity =>
        {
            entity.ToTable("expense_types");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Code).HasMaxLength(80);
            entity.HasIndex(item => item.Name).IsUnique().HasFilter("\"IsArchived\" = false");
            entity.HasIndex(item => item.Code);
        });

        modelBuilder.Entity<Tariff>(entity =>
        {
            entity.ToTable("tariffs");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.CalculationBase).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Rate).HasPrecision(18, 4);
            entity.Property(item => item.ElectricityFirstThreshold).HasPrecision(18, 4);
            entity.Property(item => item.ElectricitySecondThreshold).HasPrecision(18, 4);
            entity.Property(item => item.ElectricityFirstTierName).HasMaxLength(120);
            entity.Property(item => item.ElectricitySecondTierName).HasMaxLength(120);
            entity.Property(item => item.ElectricityThirdTierName).HasMaxLength(120);
            entity.Property(item => item.ElectricityFirstRate).HasPrecision(18, 4);
            entity.Property(item => item.ElectricitySecondRate).HasPrecision(18, 4);
            entity.Property(item => item.ElectricityThirdRate).HasPrecision(18, 4);
            entity.Property(item => item.Comment).HasMaxLength(1000);
            entity.HasIndex(item => new { item.Name, item.EffectiveFrom }).IsUnique().HasFilter("\"IsArchived\" = false");
            entity.HasIndex(item => item.CalculationBase);
            entity.HasIndex(item => item.EffectiveFrom);
        });

        modelBuilder.Entity<ChargeServiceSetting>(entity =>
        {
            entity.ToTable("charge_service_settings");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.UnitName).HasMaxLength(40);
            entity.HasIndex(item => item.Name).IsUnique().HasFilter("\"IsArchived\" = false");
            entity.HasIndex(item => item.IsRegular);
            entity.HasIndex(item => item.IsMetered);
            entity.HasIndex(item => item.HasTieredTariff);
            entity.HasIndex(item => item.IncomeTypeId);
            entity.HasIndex(item => item.TariffId);
            entity.HasOne(item => item.IncomeType)
                .WithMany()
                .HasForeignKey(item => item.IncomeTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Tariff)
                .WithMany()
                .HasForeignKey(item => item.TariffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IrregularPayment>(entity =>
        {
            entity.ToTable("irregular_payments");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Amount).HasPrecision(18, 2);
            entity.HasIndex(item => item.Name).IsUnique().HasFilter("\"IsArchived\" = false");
            entity.HasIndex(item => item.IsActive);
        });

        modelBuilder.Entity<FeeCampaign>(entity =>
        {
            entity.ToTable("fee_campaigns");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Goal).HasMaxLength(500);
            entity.Property(item => item.ContributionAmount).HasPrecision(18, 2);
            entity.Property(item => item.TargetAmount).HasPrecision(18, 2);
            entity.HasIndex(item => item.Name).IsUnique().HasFilter("\"IsArchived\" = false");
            entity.HasIndex(item => item.IncomeTypeId);
            entity.HasIndex(item => item.StartsOn);
            entity.HasIndex(item => item.IsArchived);
            entity.HasOne(item => item.IncomeType)
                .WithMany()
                .HasForeignKey(item => item.IncomeTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FeeCampaignGarage>(entity =>
        {
            entity.ToTable("fee_campaign_garages");
            entity.HasKey(item => new { item.FeeCampaignId, item.GarageId });
            entity.HasIndex(item => item.GarageId);
            entity.HasOne(item => item.FeeCampaign)
                .WithMany(item => item.ParticipantGarages)
                .HasForeignKey(item => item.FeeCampaignId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Garage)
                .WithMany()
                .HasForeignKey(item => item.GarageId)
                .OnDelete(DeleteBehavior.Cascade);
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
            entity.HasIndex(operation => new { operation.IsCanceled, operation.OperationKind });
            entity.HasIndex(operation => new { operation.OperationKind, operation.OperationDate, operation.DocumentNumber })
                .IsUnique()
                .HasFilter("\"IsCanceled\" = false AND \"DocumentNumber\" IS NOT NULL");
            entity.HasIndex(operation => operation.GarageId);
            entity.HasIndex(operation => operation.SupplierId);
            entity.HasIndex(operation => operation.StaffMemberId);
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
            entity.HasOne(operation => operation.StaffMember)
                .WithMany()
                .HasForeignKey(operation => operation.StaffMemberId)
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
            entity.HasIndex(accrual => accrual.DueDate);
            entity.HasIndex(accrual => accrual.OverdueFromDate);
            entity.HasIndex(accrual => new { accrual.GarageId, accrual.OverdueFromDate, accrual.IsCanceled });
            entity.HasIndex(accrual => accrual.GarageId);
            entity.HasIndex(accrual => accrual.IncomeTypeId);
            entity.HasIndex(accrual => accrual.TariffId);
            entity.HasIndex(accrual => new { accrual.GarageId, accrual.IncomeTypeId, accrual.AccountingMonth, accrual.Source })
                .IsUnique()
                .HasFilter("\"IsCanceled\" = false");
            entity.HasOne(accrual => accrual.Garage)
                .WithMany()
                .HasForeignKey(accrual => accrual.GarageId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(accrual => accrual.IncomeType)
                .WithMany()
                .HasForeignKey(accrual => accrual.IncomeTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(accrual => accrual.Tariff)
                .WithMany()
                .HasForeignKey(accrual => accrual.TariffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AccrualPaymentAllocation>(entity =>
        {
            entity.ToTable(
                "accrual_payment_allocations",
                table => table.HasCheckConstraint("CK_accrual_payment_allocations_Amount_Positive", "\"Amount\" > 0"));
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Amount).HasPrecision(18, 2);
            entity.HasIndex(item => new { item.FinancialOperationId, item.AccrualId })
                .IsUnique()
                .HasFilter("\"IsActive\" = true");
            entity.HasIndex(item => item.AccrualId);
            entity.HasOne(item => item.FinancialOperation)
                .WithMany()
                .HasForeignKey(item => item.FinancialOperationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Accrual)
                .WithMany()
                .HasForeignKey(item => item.AccrualId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SupplierAccrual>(entity =>
        {
            entity.ToTable("supplier_accruals");
            entity.HasKey(accrual => accrual.Id);
            entity.Property(accrual => accrual.Amount).HasPrecision(18, 2);
            entity.Property(accrual => accrual.Source).HasMaxLength(40).IsRequired();
            entity.Property(accrual => accrual.DocumentNumber).HasMaxLength(120);
            entity.Property(accrual => accrual.Comment).HasMaxLength(1000);
            entity.HasIndex(accrual => accrual.AccountingMonth);
            entity.HasIndex(accrual => accrual.SupplierId);
            entity.HasIndex(accrual => accrual.ExpenseTypeId);
            entity.HasIndex(accrual => new { accrual.SupplierId, accrual.ExpenseTypeId, accrual.AccountingMonth, accrual.Source, accrual.DocumentNumber })
                .IsUnique()
                .HasFilter("\"IsCanceled\" = false");
            entity.HasOne(accrual => accrual.Supplier)
                .WithMany()
                .HasForeignKey(accrual => accrual.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(accrual => accrual.ExpenseType)
                .WithMany()
                .HasForeignKey(accrual => accrual.ExpenseTypeId)
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
            entity.Property(reading => reading.HasGapWarning).HasDefaultValue(false);
            entity.Property(reading => reading.Comment).HasMaxLength(1000);
            entity.HasIndex(reading => reading.AccountingMonth);
            entity.HasIndex(reading => reading.ReadingDate);
            entity.HasIndex(reading => reading.GarageId);
            entity.HasIndex(reading => new { reading.MeterKind, reading.AccountingMonth, reading.GarageId })
                .HasFilter("\"IsCanceled\" = false");
            entity.HasIndex(reading => new { reading.GarageId, reading.MeterKind, reading.AccountingMonth })
                .IsUnique()
                .HasFilter("\"IsCanceled\" = false");
            entity.HasOne(reading => reading.Garage)
                .WithMany()
                .HasForeignKey(reading => reading.GarageId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Fund>(entity =>
        {
            entity.ToTable("funds");
            entity.HasKey(fund => fund.Id);
            entity.Property(fund => fund.Name).HasMaxLength(200).IsRequired();
            entity.Property(fund => fund.NormalizedName).HasMaxLength(200).IsRequired();
            entity.Property(fund => fund.Balance).HasPrecision(18, 2);
            entity.HasIndex(fund => fund.NormalizedName).IsUnique();
            entity.HasIndex(fund => fund.SortOrder);
        });

        modelBuilder.Entity<FundOperation>(entity =>
        {
            entity.ToTable("fund_operations");
            entity.HasKey(operation => operation.Id);
            entity.Property(operation => operation.OperationKind).HasMaxLength(20).IsRequired();
            entity.Property(operation => operation.Amount).HasPrecision(18, 2);
            entity.Property(operation => operation.BalanceBefore).HasPrecision(18, 2);
            entity.Property(operation => operation.BalanceAfter).HasPrecision(18, 2);
            entity.Property(operation => operation.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(operation => operation.IsCanceled).HasDefaultValue(false);
            entity.HasIndex(operation => operation.FundId);
            entity.HasIndex(operation => operation.CreatedAtUtc);
            entity.HasIndex(operation => operation.OperationKind);
            entity.HasIndex(operation => operation.IsCanceled);
            entity.HasOne(operation => operation.Fund)
                .WithMany(fund => fund.Operations)
                .HasForeignKey(operation => operation.FundId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FormState>(entity =>
        {
            entity.ToTable("form_states");
            entity.HasKey(state => state.Id);
            entity.Property(state => state.Scope).HasMaxLength(120).IsRequired();
            entity.Property(state => state.PayloadJson).IsRequired();
            entity.HasIndex(state => state.Scope).IsUnique();
            entity.HasIndex(state => state.UpdatedAtUtc);
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

        modelBuilder.Entity<AccessImportRunLogEntry>(entity =>
        {
            entity.ToTable("access_import_run_log_entries");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Level).HasMaxLength(20).IsRequired();
            entity.Property(entry => entry.StepCode).HasMaxLength(120).IsRequired();
            entity.Property(entry => entry.Message).HasMaxLength(1000).IsRequired();
            entity.Property(entry => entry.DetailsJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(entry => entry.AccessImportRunId);
            entity.HasIndex(entry => entry.CreatedAtUtc);
            entity.HasIndex(entry => new { entry.AccessImportRunId, entry.CreatedAtUtc });
        });

        modelBuilder.Entity<AccessImportRowFingerprint>(entity =>
        {
            entity.ToTable("access_import_row_fingerprints");
            entity.HasKey(fingerprint => fingerprint.Id);
            entity.Property(fingerprint => fingerprint.FingerprintKey).HasMaxLength(520).IsRequired();
            entity.Property(fingerprint => fingerprint.SourceSystem).HasMaxLength(80).IsRequired();
            entity.Property(fingerprint => fingerprint.EntityType).HasMaxLength(120).IsRequired();
            entity.Property(fingerprint => fingerprint.ExternalId).HasMaxLength(240);
            entity.Property(fingerprint => fingerprint.RowHash).HasMaxLength(64).IsRequired();
            entity.Property(fingerprint => fingerprint.TargetEntityType).HasMaxLength(120);
            entity.Property(fingerprint => fingerprint.TargetEntityId).HasMaxLength(120);
            entity.HasIndex(fingerprint => fingerprint.FingerprintKey).IsUnique();
            entity.HasIndex(fingerprint => new { fingerprint.SourceSystem, fingerprint.EntityType });
            entity.HasIndex(fingerprint => fingerprint.AccessImportRunId);
        });

        modelBuilder.Entity<AccessImportQuarantineItem>(entity =>
        {
            entity.ToTable("access_import_quarantine_items");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.SourceSystem).HasMaxLength(80).IsRequired();
            entity.Property(item => item.EntityType).HasMaxLength(120).IsRequired();
            entity.Property(item => item.ExternalId).HasMaxLength(240);
            entity.Property(item => item.RowHash).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ReasonCode).HasMaxLength(120).IsRequired();
            entity.Property(item => item.ReasonMessage).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.Severity).HasMaxLength(20).IsRequired();
            entity.Property(item => item.RowSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(item => item.Status).HasMaxLength(20).IsRequired();
            entity.Property(item => item.ResolutionComment).HasMaxLength(1000);
            entity.HasIndex(item => item.AccessImportRunId);
            entity.HasIndex(item => item.Status);
            entity.HasIndex(item => item.CreatedAtUtc);
            entity.HasIndex(item => new { item.SourceSystem, item.EntityType });
            entity.HasIndex(item => item.RowHash);
        });

        modelBuilder.Entity<AccessImportCreatedRecord>(entity =>
        {
            entity.ToTable("access_import_created_records");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.SourceSystem).HasMaxLength(80).IsRequired();
            entity.Property(record => record.SourceEntityType).HasMaxLength(120).IsRequired();
            entity.Property(record => record.SourceExternalId).HasMaxLength(240);
            entity.Property(record => record.SourceRowHash).HasMaxLength(64).IsRequired();
            entity.Property(record => record.TargetEntityType).HasMaxLength(120).IsRequired();
            entity.Property(record => record.TargetEntityId).HasMaxLength(120).IsRequired();
            entity.Property(record => record.TargetDisplayName).HasMaxLength(300);
            entity.Property(record => record.RollbackStatus).HasMaxLength(40).IsRequired();
            entity.Property(record => record.RollbackReason).HasMaxLength(1000);
            entity.HasIndex(record => record.AccessImportRunId);
            entity.HasIndex(record => record.CreatedAtUtc);
            entity.HasIndex(record => record.RollbackStatus);
            entity.HasIndex(record => new { record.AccessImportRunId, record.TargetEntityType, record.TargetEntityId }).IsUnique();
            entity.HasIndex(record => new { record.SourceSystem, record.SourceEntityType });
            entity.HasIndex(record => record.SourceRowHash);
        });

        modelBuilder.Entity<IntegrationSecretSetting>(entity =>
        {
            entity.ToTable("integration_secret_settings");
            entity.HasKey(setting => setting.Id);
            entity.Property(setting => setting.Provider).HasMaxLength(100).IsRequired();
            entity.Property(setting => setting.SettingKey).HasMaxLength(120).IsRequired();
            entity.Property(setting => setting.NormalizedProvider).HasMaxLength(100).IsRequired();
            entity.Property(setting => setting.NormalizedSettingKey).HasMaxLength(120).IsRequired();
            entity.Property(setting => setting.Purpose).HasMaxLength(240).IsRequired();
            entity.Property(setting => setting.ProtectedValue).HasColumnType("text").IsRequired();
            entity.HasIndex(setting => new { setting.NormalizedProvider, setting.NormalizedSettingKey })
                .IsUnique()
                .HasDatabaseName("IX_integration_secret_settings_provider_key");
            entity.HasIndex(setting => setting.Provider);
            entity.HasIndex(setting => setting.UpdatedAtUtc);
        });
    }

    private void NormalizeAuditEventRelatedFields()
    {
        foreach (var entry in ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
            {
                continue;
            }

            var auditEvent = entry.Entity;
            var metadata = ParseAuditMetadata(auditEvent.MetadataJson);

            auditEvent.Section ??= GetAuditSection(auditEvent.Action);
            auditEvent.ActionKind ??= GetAuditActionKind(auditEvent.Action);
            auditEvent.EntityDisplayName ??= ExtractAuditMetadataValue(metadata, 256, "entityDisplayName", "displayName", "name", "title");
            auditEvent.RelatedGarageId ??= ExtractAuditMetadataValue(metadata, 120, "relatedGarageId", "garageId");
            auditEvent.RelatedGarageNumber ??= ExtractAuditMetadataValue(metadata, 80, "relatedGarageNumber", "garageNumber");
            auditEvent.RelatedAccountingMonth ??= ExtractAuditMetadataValue(metadata, 32, "relatedAccountingMonth", "accountingMonth", "period", "month");
            auditEvent.RelatedCounterpartyId ??= ExtractAuditMetadataValue(metadata, 120, "relatedCounterpartyId", "counterpartyId", "supplierId", "ownerId", "employeeId");
            auditEvent.RelatedCounterpartyName ??= ExtractAuditMetadataValue(metadata, 256, "relatedCounterpartyName", "counterpartyName", "supplierName", "ownerName", "employeeName");
            auditEvent.RelatedDocumentId ??= ExtractAuditMetadataValue(metadata, 120, "relatedDocumentId", "documentId", "operationId", "paymentId", "accrualId", "invoiceId", "receiptId");
            auditEvent.RelatedDocumentNumber ??= ExtractAuditMetadataValue(metadata, 120, "relatedDocumentNumber", "documentNumber", "operationNumber", "paymentNumber", "invoiceNumber", "receiptNumber");
        }
    }

    private static IReadOnlyDictionary<string, string> ParseAuditMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (IsSensitiveAuditMetadataKey(property.Name))
                {
                    continue;
                }

                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(value))
                {
                    metadata[property.Name] = value.Trim();
                }
            }

            return metadata;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? ExtractAuditMetadataValue(IReadOnlyDictionary<string, string> metadata, int maxLength, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }

        return null;
    }

    private static bool IsSensitiveAuditMetadataKey(string key)
    {
        return key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("key", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("email", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("phone", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("passport", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("card", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("bankAccount", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("accountNumber", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("address", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAuditSection(string action)
    {
        var separatorIndex = action.IndexOf('.', StringComparison.Ordinal);
        var section = separatorIndex > 0 ? action[..separatorIndex] : "system";
        return section.Length <= 80 ? section : section[..80];
    }

    private static string GetAuditActionKind(string action)
    {
        var normalized = action.ToLowerInvariant();

        if (normalized.Contains("_created", StringComparison.Ordinal))
        {
            return "create";
        }

        if (normalized.Contains("_updated", StringComparison.Ordinal) || normalized.Contains("password_changed", StringComparison.Ordinal))
        {
            return "update";
        }

        if (normalized.Contains("_archived", StringComparison.Ordinal))
        {
            return "archive";
        }

        if (normalized.Contains("_restored", StringComparison.Ordinal))
        {
            return "restore";
        }

        if (normalized.Contains("_canceled", StringComparison.Ordinal) || normalized.Contains("_cancelled", StringComparison.Ordinal))
        {
            return "cancel";
        }

        if (normalized.Contains("_deleted", StringComparison.Ordinal))
        {
            return "delete";
        }

        if (normalized.Contains("_failed", StringComparison.Ordinal) || normalized.Contains("_rate_limited", StringComparison.Ordinal) || normalized.Contains("_inactive", StringComparison.Ordinal))
        {
            return "fail";
        }

        if (normalized.Contains("_generated", StringComparison.Ordinal))
        {
            return "generate";
        }

        if (normalized.StartsWith("auth.login", StringComparison.Ordinal))
        {
            return "login";
        }

        if (normalized.StartsWith("import.", StringComparison.Ordinal))
        {
            return "import";
        }

        if (normalized.Contains("_exported", StringComparison.Ordinal) || normalized.Contains(".export", StringComparison.Ordinal))
        {
            return "export";
        }

        return "other";
    }
}
