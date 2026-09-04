using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFinancialOperationRepository(GarageBalanceDbContext dbContext) : IFinancialOperationRepository
{
    private static readonly byte[] ReceiptBatchLockNamespace = "receipt-batch"u8.ToArray();

    public async Task<IReadOnlyList<FinancialOperation>> GetListAsync(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? operationKind,
        string? normalizedSearch,
        Guid? garageId,
        Guid? supplierId,
        Guid? staffMemberId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), dateFrom, dateTo, operationKind, garageId, supplierId, staffMemberId);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            return (await ReadCompactListAsync(Order(query), cancellationToken))
                .Where(operation => OperationMatchesSearch(operation, normalizedSearch))
                .Take(limit)
                .ToList();
        }

        return await ReadCompactListAsync(
            Order(ApplySearch(query, normalizedSearch)).Take(limit),
            cancellationToken);
    }

    public async Task<FinancialOperationPageData> GetPageAsync(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? operationKind,
        string? normalizedSearch,
        Guid? garageId,
        Guid? supplierId,
        Guid? staffMemberId,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), dateFrom, dateTo, operationKind, garageId, supplierId, staffMemberId);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            var filtered = (await ReadCompactListAsync(Order(query), cancellationToken))
                .Where(operation => OperationMatchesSearch(operation, normalizedSearch))
                .ToList();
            return new FinancialOperationPageData(filtered.Skip(offset).Take(limit).ToList(), filtered.Count);
        }

        query = ApplySearch(query, normalizedSearch);
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresPageAsync(query, offset, limit, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await ReadCompactListAsync(
            Order(query).Skip(offset).Take(limit),
            cancellationToken);
        return new FinancialOperationPageData(items, totalCount);
    }

    private async Task<FinancialOperationPageData> GetPostgresPageAsync(
        IQueryable<FinancialOperation> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = Order(query)
            .Skip(offset)
            .Take(limit)
            .Select(operation => new
            {
                Category = PageCategory,
                Id = (Guid?)operation.Id,
                OperationKind = (string?)operation.OperationKind,
                OperationDate = (DateOnly?)operation.OperationDate,
                AccountingMonth = (DateOnly?)operation.AccountingMonth,
                Amount = (decimal?)operation.Amount,
                operation.ReceiptBatchId,
                operation.ExpensePaymentType,
                operation.ExpensePaymentSource,
                operation.CounterpartyName,
                NegativeFundBalanceConfirmed = (bool?)operation.NegativeFundBalanceConfirmed,
                operation.DocumentNumber,
                operation.Comment,
                operation.GarageId,
                GarageNumber = operation.Garage == null ? null : operation.Garage.Number,
                GarageStartingBalance = operation.Garage == null ? null : (decimal?)operation.Garage.StartingBalance,
                OwnerId = operation.Garage == null ? null : operation.Garage.OwnerId,
                OwnerLastName = operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.LastName,
                OwnerFirstName = operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.FirstName,
                OwnerMiddleName = operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.MiddleName,
                operation.IncomeTypeId,
                IncomeTypeName = operation.IncomeType == null ? null : operation.IncomeType.Name,
                operation.SupplierId,
                SupplierName = operation.Supplier == null ? null : operation.Supplier.Name,
                SupplierStartingBalance = operation.Supplier == null ? null : (decimal?)operation.Supplier.StartingBalance,
                operation.StaffMemberId,
                StaffMemberName = operation.StaffMember == null ? null : operation.StaffMember.FullName,
                StaffDepartmentId = operation.StaffMember == null ? null : (Guid?)operation.StaffMember.DepartmentId,
                StaffDepartmentName = operation.StaffMember == null ? null : operation.StaffMember.Department.Name,
                operation.ExpenseTypeId,
                ExpenseTypeName = operation.ExpenseType == null ? null : operation.ExpenseType.Name,
                operation.ExpenseFundId,
                ExpenseFundName = operation.ExpenseFund == null ? null : operation.ExpenseFund.Name,
                Version = (Guid?)operation.Version,
                IsCanceled = (bool?)operation.IsCanceled,
                CreatedAtUtc = (DateTimeOffset?)operation.CreatedAtUtc,
                UpdatedAtUtc = (DateTimeOffset?)operation.UpdatedAtUtc,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new
            {
                Category = TotalsCategory,
                Id = (Guid?)null,
                OperationKind = (string?)null,
                OperationDate = (DateOnly?)null,
                AccountingMonth = (DateOnly?)null,
                Amount = (decimal?)null,
                ReceiptBatchId = (Guid?)null,
                ExpensePaymentType = (string?)null,
                ExpensePaymentSource = (string?)null,
                CounterpartyName = (string?)null,
                NegativeFundBalanceConfirmed = (bool?)null,
                DocumentNumber = (string?)null,
                Comment = (string?)null,
                GarageId = (Guid?)null,
                GarageNumber = (string?)null,
                GarageStartingBalance = (decimal?)null,
                OwnerId = (Guid?)null,
                OwnerLastName = (string?)null,
                OwnerFirstName = (string?)null,
                OwnerMiddleName = (string?)null,
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = (string?)null,
                SupplierId = (Guid?)null,
                SupplierName = (string?)null,
                SupplierStartingBalance = (decimal?)null,
                StaffMemberId = (Guid?)null,
                StaffMemberName = (string?)null,
                StaffDepartmentId = (Guid?)null,
                StaffDepartmentName = (string?)null,
                ExpenseTypeId = (Guid?)null,
                ExpenseTypeName = (string?)null,
                ExpenseFundId = (Guid?)null,
                ExpenseFundName = (string?)null,
                Version = (Guid?)null,
                IsCanceled = (bool?)null,
                CreatedAtUtc = (DateTimeOffset?)null,
                UpdatedAtUtc = (DateTimeOffset?)null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenByDescending(row => row.OperationDate)
            .ThenBy(row => row.DocumentNumber)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new FinancialOperation
            {
                Id = row.Id!.Value,
                OperationKind = row.OperationKind!,
                OperationDate = row.OperationDate!.Value,
                AccountingMonth = row.AccountingMonth!.Value,
                Amount = row.Amount!.Value,
                ReceiptBatchId = row.ReceiptBatchId,
                ExpensePaymentType = row.ExpensePaymentType,
                ExpensePaymentSource = row.ExpensePaymentSource,
                CounterpartyName = row.CounterpartyName,
                NegativeFundBalanceConfirmed = row.NegativeFundBalanceConfirmed!.Value,
                DocumentNumber = row.DocumentNumber,
                Comment = row.Comment,
                GarageId = row.GarageId,
                Garage = row.GarageId is null
                    ? null
                    : new Garage
                    {
                        Id = row.GarageId.Value,
                        Number = row.GarageNumber!,
                        StartingBalance = row.GarageStartingBalance ?? 0m,
                        OwnerId = row.OwnerId,
                        Owner = row.OwnerId is null
                            ? null
                            : new Owner
                            {
                                Id = row.OwnerId.Value,
                                LastName = row.OwnerLastName!,
                                FirstName = row.OwnerFirstName!,
                                MiddleName = row.OwnerMiddleName
                            }
                    },
                IncomeTypeId = row.IncomeTypeId,
                IncomeType = row.IncomeTypeId is null
                    ? null
                    : new IncomeType { Id = row.IncomeTypeId.Value, Name = row.IncomeTypeName! },
                SupplierId = row.SupplierId,
                Supplier = row.SupplierId is null
                    ? null
                    : new Supplier
                    {
                        Id = row.SupplierId.Value,
                        Name = row.SupplierName!,
                        StartingBalance = row.SupplierStartingBalance ?? 0m
                    },
                StaffMemberId = row.StaffMemberId,
                StaffMember = row.StaffMemberId is null
                    ? null
                    : new StaffMember
                    {
                        Id = row.StaffMemberId.Value,
                        FullName = row.StaffMemberName!,
                        DepartmentId = row.StaffDepartmentId!.Value,
                        Department = new StaffDepartment
                        {
                            Id = row.StaffDepartmentId.Value,
                            Name = row.StaffDepartmentName!
                        }
                    },
                ExpenseTypeId = row.ExpenseTypeId,
                ExpenseType = row.ExpenseTypeId is null
                    ? null
                    : new ExpenseType { Id = row.ExpenseTypeId.Value, Name = row.ExpenseTypeName! },
                ExpenseFundId = row.ExpenseFundId,
                ExpenseFund = row.ExpenseFundId is null
                    ? null
                    : new Fund
                    {
                        Id = row.ExpenseFundId.Value,
                        Name = row.ExpenseFundName!,
                        NormalizedName = row.ExpenseFundName!
                    },
                Version = row.Version!.Value,
                IsCanceled = row.IsCanceled!.Value,
                CreatedAtUtc = row.CreatedAtUtc!.Value,
                UpdatedAtUtc = row.UpdatedAtUtc!.Value
            })
            .ToList();
        return new FinancialOperationPageData(items, totalCount);
    }

    public Task<FinancialOperation?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Aggregate(dbContext.FinancialOperations)
            .SingleOrDefaultAsync(operation => operation.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetLinkedFeeCampaignIdsAsync(Guid id, CancellationToken cancellationToken)
    {
        var directlyTaggedCampaigns = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => operation.Id == id && operation.FeeCampaignId != null)
            .Select(operation => operation.FeeCampaignId!.Value);
        var allocationLinkedCampaigns = dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(allocation =>
                allocation.FinancialOperationId == id &&
                allocation.Accrual.FeeCampaignId != null)
            .Select(allocation => allocation.Accrual.FeeCampaignId!.Value);

        // Allocation history is intentional here: once an untagged legacy payment has
        // been earmarked for a campaign, restoring or editing it must not bypass the
        // campaign lifecycle merely because a later rebuild deactivated the row.
        return await directlyTaggedCampaigns
            .Concat(allocationLinkedCampaigns)
            .Distinct()
            .OrderBy(campaignId => campaignId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IAsyncDisposable> AcquireReceiptBatchLockAsync(
        Guid receiptBatchId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return NoOpAsyncDisposable.Instance;
        }

        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State == ConnectionState.Closed;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var lockKey = CreateReceiptBatchLockKey(receiptBatchId);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_lock(@lock_key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.Value = lockKey;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new AdvisoryLockLease(connection, lockKey, closeConnection);
    }

    public async Task<IReadOnlyList<FinancialOperation>> GetByReceiptBatchIdAsync(
        Guid receiptBatchId,
        CancellationToken cancellationToken) =>
        await Aggregate(dbContext.FinancialOperations.AsNoTracking())
            .Where(operation => operation.ReceiptBatchId == receiptBatchId)
            .OrderBy(operation => operation.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> ActiveDocumentDuplicateExistsAsync(
        Guid? ignoredId,
        string operationKind,
        DateOnly operationDate,
        string documentNumber,
        CancellationToken cancellationToken) =>
        dbContext.FinancialOperations.AsNoTracking().AnyAsync(operation =>
            !operation.IsCanceled &&
            (!ignoredId.HasValue || operation.Id != ignoredId.Value) &&
            operation.OperationKind == operationKind &&
            operation.OperationDate == operationDate &&
            operation.DocumentNumber == documentNumber,
            cancellationToken);

    public Task<bool> ReceiptBatchConflictExistsAsync(
        Guid receiptBatchId,
        Guid garageId,
        DateOnly operationDate,
        CancellationToken cancellationToken) =>
        dbContext.FinancialOperations.AsNoTracking().AnyAsync(operation =>
            operation.ReceiptBatchId == receiptBatchId &&
            (operation.GarageId != garageId || operation.OperationDate != operationDate),
            cancellationToken);

    public async Task<decimal> GetIncomeTotalBeforeMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken) =>
        await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.AccountingMonth < accountingMonth)
            .SumAsync(operation => operation.Amount, cancellationToken);

    public async Task<decimal> GetOpeningDebtPaymentTotalAsync(
        Guid garageId,
        DateOnly accountingMonth,
        string incomeTypeCode,
        string incomeTypeName,
        CancellationToken cancellationToken) =>
        await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.AccountingMonth == accountingMonth &&
                operation.IncomeType != null &&
                (operation.IncomeType.Code == incomeTypeCode || operation.IncomeType.Name == incomeTypeName))
            .SumAsync(operation => operation.Amount, cancellationToken);

    public async Task<decimal> GetStaffExpenseTotalAsync(Guid staffMemberId, DateOnly accountingMonth, CancellationToken cancellationToken) =>
        await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.StaffMemberId == staffMemberId &&
                operation.AccountingMonth == accountingMonth)
            .SumAsync(operation => operation.Amount, cancellationToken);

    public async Task<decimal> GetPreviousGarageIncomeTotalAsync(Guid ignoredId, Guid garageId, DateOnly operationDate, CancellationToken cancellationToken)
    {
        var operationCreatedAtUtc = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => operation.Id == ignoredId)
            .Select(operation => (DateTimeOffset?)operation.CreatedAtUtc)
            .SingleOrDefaultAsync(cancellationToken);

        var query = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.Id != ignoredId &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId);
        if (IsSqliteProvider())
        {
            var earlierTotal = await query
                .Where(operation => operation.OperationDate < operationDate)
                .SumAsync(operation => operation.Amount, cancellationToken);
            if (!operationCreatedAtUtc.HasValue)
            {
                return earlierTotal;
            }

            var sameDayRows = await query
                .Where(operation => operation.OperationDate == operationDate)
                .Select(operation => new { operation.CreatedAtUtc, operation.Amount })
                .ToListAsync(cancellationToken);
            return earlierTotal + sameDayRows
                .Where(operation => operation.CreatedAtUtc < operationCreatedAtUtc.Value)
                .Sum(operation => operation.Amount);
        }

        return await query
            .Where(operation =>
                operation.OperationDate < operationDate ||
                (operation.OperationDate == operationDate &&
                    operationCreatedAtUtc.HasValue &&
                    operation.CreatedAtUtc < operationCreatedAtUtc.Value))
            .SumAsync(operation => operation.Amount, cancellationToken);
    }

    public async Task<decimal> GetPreviousSupplierExpenseTotalAsync(
        Guid ignoredId,
        Guid supplierId,
        Guid expenseTypeId,
        DateOnly operationDate,
        CancellationToken cancellationToken)
    {
        var operationCreatedAtUtc = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => operation.Id == ignoredId)
            .Select(operation => (DateTimeOffset?)operation.CreatedAtUtc)
            .SingleOrDefaultAsync(cancellationToken);

        var query = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.Id != ignoredId &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.SupplierId == supplierId &&
                operation.ExpenseTypeId == expenseTypeId);
        if (IsSqliteProvider())
        {
            var earlierTotal = await query
                .Where(operation => operation.OperationDate < operationDate)
                .SumAsync(operation => operation.Amount, cancellationToken);
            if (!operationCreatedAtUtc.HasValue)
            {
                return earlierTotal;
            }

            var sameDayRows = await query
                .Where(operation => operation.OperationDate == operationDate)
                .Select(operation => new { operation.CreatedAtUtc, operation.Amount })
                .ToListAsync(cancellationToken);
            return earlierTotal + sameDayRows
                .Where(operation => operation.CreatedAtUtc < operationCreatedAtUtc.Value)
                .Sum(operation => operation.Amount);
        }

        return await query
            .Where(operation =>
                operation.OperationDate < operationDate ||
                (operation.OperationDate == operationDate &&
                    operationCreatedAtUtc.HasValue &&
                    operation.CreatedAtUtc < operationCreatedAtUtc.Value))
            .SumAsync(operation => operation.Amount, cancellationToken);
    }

    public Task<DateOnly?> GetPreviousActiveIncomeDateAsync(
        Guid garageId,
        Guid incomeTypeId,
        DateOnly operationDate,
        Guid? ignoredOperationId,
        CancellationToken cancellationToken) =>
        dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.IncomeTypeId == incomeTypeId &&
                operation.OperationDate <= operationDate &&
                (!ignoredOperationId.HasValue || operation.Id != ignoredOperationId.Value))
            .OrderByDescending(operation => operation.OperationDate)
            .ThenByDescending(operation => operation.Id)
            .Select(operation => (DateOnly?)operation.OperationDate)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(FinancialOperation operation) => dbContext.FinancialOperations.Add(operation);

    private IQueryable<FinancialOperation> QueryActive() =>
        dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled);

    private static async Task<IReadOnlyList<FinancialOperation>> ReadCompactListAsync(
        IQueryable<FinancialOperation> query,
        CancellationToken cancellationToken)
    {
        var rows = await query
            .Select(operation => new FinancialOperationListRow(
                operation.Id,
                operation.OperationKind,
                operation.OperationDate,
                operation.AccountingMonth,
                operation.Amount,
                operation.ReceiptBatchId,
                operation.ExpensePaymentType,
                operation.ExpensePaymentSource,
                operation.CounterpartyName,
                operation.NegativeFundBalanceConfirmed,
                operation.DocumentNumber,
                operation.Comment,
                operation.GarageId,
                operation.Garage == null ? null : operation.Garage.Number,
                operation.Garage == null ? null : (decimal?)operation.Garage.StartingBalance,
                operation.Garage == null ? null : operation.Garage.OwnerId,
                operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.LastName,
                operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.FirstName,
                operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.MiddleName,
                operation.IncomeTypeId,
                operation.IncomeType == null ? null : operation.IncomeType.Name,
                operation.SupplierId,
                operation.Supplier == null ? null : operation.Supplier.Name,
                operation.Supplier == null ? null : (decimal?)operation.Supplier.StartingBalance,
                operation.StaffMemberId,
                operation.StaffMember == null ? null : operation.StaffMember.FullName,
                operation.StaffMember == null ? null : (Guid?)operation.StaffMember.DepartmentId,
                operation.StaffMember == null ? null : operation.StaffMember.Department.Name,
                operation.ExpenseTypeId,
                operation.ExpenseType == null ? null : operation.ExpenseType.Name,
                operation.ExpenseFundId,
                operation.ExpenseFund == null ? null : operation.ExpenseFund.Name,
                operation.Version,
                operation.IsCanceled,
                operation.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return rows.Select(CreateCompactOperation).ToList();
    }

    private static FinancialOperation CreateCompactOperation(FinancialOperationListRow row)
    {
        return new FinancialOperation
        {
            Id = row.Id,
            OperationKind = row.OperationKind,
            OperationDate = row.OperationDate,
            AccountingMonth = row.AccountingMonth,
            Amount = row.Amount,
            ReceiptBatchId = row.ReceiptBatchId,
            ExpensePaymentType = row.ExpensePaymentType,
            ExpensePaymentSource = row.ExpensePaymentSource,
            CounterpartyName = row.CounterpartyName,
            NegativeFundBalanceConfirmed = row.NegativeFundBalanceConfirmed,
            DocumentNumber = row.DocumentNumber,
            Comment = row.Comment,
            GarageId = row.GarageId,
            Garage = row.GarageId is null
                ? null
                : new Garage
                {
                    Id = row.GarageId.Value,
                    Number = row.GarageNumber ?? string.Empty,
                    StartingBalance = row.GarageStartingBalance ?? 0m,
                    OwnerId = row.OwnerId,
                    Owner = row.OwnerId is null
                        ? null
                        : new Owner
                        {
                            Id = row.OwnerId.Value,
                            LastName = row.OwnerLastName ?? string.Empty,
                            FirstName = row.OwnerFirstName ?? string.Empty,
                            MiddleName = row.OwnerMiddleName
                        }
                },
            IncomeTypeId = row.IncomeTypeId,
            IncomeType = row.IncomeTypeId is null
                ? null
                : new IncomeType { Id = row.IncomeTypeId.Value, Name = row.IncomeTypeName ?? string.Empty },
            SupplierId = row.SupplierId,
            Supplier = row.SupplierId is null
                ? null
                : new Supplier
                {
                    Id = row.SupplierId.Value,
                    Name = row.SupplierName ?? string.Empty,
                    StartingBalance = row.SupplierStartingBalance ?? 0m
                },
            StaffMemberId = row.StaffMemberId,
            StaffMember = row.StaffMemberId is null
                ? null
                : new StaffMember
                {
                    Id = row.StaffMemberId.Value,
                    FullName = row.StaffMemberName ?? string.Empty,
                    DepartmentId = row.StaffDepartmentId!.Value,
                    Department = new StaffDepartment
                    {
                        Id = row.StaffDepartmentId.Value,
                        Name = row.StaffDepartmentName ?? string.Empty
                    }
                },
            ExpenseTypeId = row.ExpenseTypeId,
            ExpenseType = row.ExpenseTypeId is null
                ? null
                : new ExpenseType { Id = row.ExpenseTypeId.Value, Name = row.ExpenseTypeName ?? string.Empty },
            ExpenseFundId = row.ExpenseFundId,
            ExpenseFund = row.ExpenseFundId is null
                ? null
                : new Fund
                {
                    Id = row.ExpenseFundId.Value,
                    Name = row.ExpenseFundName ?? string.Empty,
                    NormalizedName = row.ExpenseFundName ?? string.Empty
                },
            Version = row.Version,
            IsCanceled = row.IsCanceled,
            CreatedAtUtc = row.CreatedAtUtc
        };
    }

    private static IQueryable<FinancialOperation> Aggregate(IQueryable<FinancialOperation> query) =>
        query
            .Include(operation => operation.Garage)
            .ThenInclude(garage => garage!.Owner)
            .Include(operation => operation.IncomeType)
            .Include(operation => operation.Supplier)
            .Include(operation => operation.StaffMember)
            .ThenInclude(staffMember => staffMember!.Department)
            .Include(operation => operation.ExpenseType)
            .Include(operation => operation.ExpenseFund);

    private sealed record FinancialOperationListRow(
        Guid Id,
        string OperationKind,
        DateOnly OperationDate,
        DateOnly AccountingMonth,
        decimal Amount,
        Guid? ReceiptBatchId,
        string? ExpensePaymentType,
        string? ExpensePaymentSource,
        string? CounterpartyName,
        bool NegativeFundBalanceConfirmed,
        string? DocumentNumber,
        string? Comment,
        Guid? GarageId,
        string? GarageNumber,
        decimal? GarageStartingBalance,
        Guid? OwnerId,
        string? OwnerLastName,
        string? OwnerFirstName,
        string? OwnerMiddleName,
        Guid? IncomeTypeId,
        string? IncomeTypeName,
        Guid? SupplierId,
        string? SupplierName,
        decimal? SupplierStartingBalance,
        Guid? StaffMemberId,
        string? StaffMemberName,
        Guid? StaffDepartmentId,
        string? StaffDepartmentName,
        Guid? ExpenseTypeId,
        string? ExpenseTypeName,
        Guid? ExpenseFundId,
        string? ExpenseFundName,
        Guid Version,
        bool IsCanceled,
        DateTimeOffset CreatedAtUtc);

    private static IQueryable<FinancialOperation> ApplyFilters(
        IQueryable<FinancialOperation> query,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? operationKind,
        Guid? garageId,
        Guid? supplierId,
        Guid? staffMemberId)
    {
        if (dateFrom.HasValue)
        {
            query = query.Where(operation => operation.OperationDate >= dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            query = query.Where(operation => operation.OperationDate <= dateTo.Value);
        }

        if (operationKind is not null)
        {
            query = query.Where(operation => operation.OperationKind == operationKind);
        }

        if (garageId.HasValue)
        {
            query = query.Where(operation => operation.GarageId == garageId.Value);
        }

        if (supplierId.HasValue)
        {
            query = query.Where(operation => operation.SupplierId == supplierId.Value);
        }

        if (staffMemberId.HasValue)
        {
            query = query.Where(operation => operation.StaffMemberId == staffMemberId.Value);
        }

        return query;
    }

    private static IQueryable<FinancialOperation> ApplySearch(IQueryable<FinancialOperation> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
        return query.Where(operation =>
            (operation.DocumentNumber != null && EF.Functions.ILike(operation.DocumentNumber, pattern, @"\")) ||
            (operation.Comment != null && EF.Functions.ILike(operation.Comment, pattern, @"\")) ||
            (operation.Garage != null && EF.Functions.ILike(operation.Garage.Number, pattern, @"\")) ||
            (operation.Supplier != null && EF.Functions.ILike(operation.Supplier.Name, pattern, @"\")) ||
            (operation.CounterpartyName != null && EF.Functions.ILike(operation.CounterpartyName, pattern, @"\")) ||
            (operation.StaffMember != null && EF.Functions.ILike(operation.StaffMember.FullName, pattern, @"\")));
    }

    private static IOrderedQueryable<FinancialOperation> Order(IQueryable<FinancialOperation> query) =>
        query.OrderByDescending(operation => operation.OperationDate)
            .ThenBy(operation => operation.DocumentNumber);

    private static bool OperationMatchesSearch(FinancialOperation operation, string normalizedSearch) =>
        (operation.DocumentNumber?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.Garage?.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.Supplier?.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.CounterpartyName?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.StaffMember?.FullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private static long CreateReceiptBatchLockKey(Guid receiptBatchId)
    {
        Span<byte> source = stackalloc byte[16 + ReceiptBatchLockNamespace.Length];
        receiptBatchId.TryWriteBytes(source[..16]);
        ReceiptBatchLockNamespace.CopyTo(source[16..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(source, hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }

    private sealed class AdvisoryLockLease(
        System.Data.Common.DbConnection connection,
        long lockKey,
        bool closeConnection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@lock_key)";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "lock_key";
                parameter.Value = lockKey;
                command.Parameters.Add(parameter);
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                if (closeConnection)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static readonly NoOpAsyncDisposable Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
