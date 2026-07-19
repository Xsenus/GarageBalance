using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfExpenseReportQuery(GarageBalanceDbContext dbContext) : IExpenseReportQuery
{
    private const int StartingBalanceTotalCategory = 1;
    private const int AccrualTotalCategory = 2;
    private const int ExpenseTotalCategory = 3;
    private const string AllRows = "all";
    private const string AccrualRows = "accruals";
    private const string PaymentRows = "payments";
    private const string StartingBalanceRows = "starting_balance";

    public Task<ExpenseReportQueryData> GetRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<Guid> expenseTypeIds,
        string? search,
        int? limit,
        int offset,
        CancellationToken cancellationToken) =>
        GetRowsAsync(dateFrom, dateTo, rowMode, supplierIds, new HashSet<Guid>(), expenseTypeIds, search, limit, offset, new ReportSort("date", false), cancellationToken);

    public Task<ExpenseReportQueryData> GetRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<Guid> expenseTypeIds,
        string? search,
        int? limit,
        int offset,
        ReportSort sort,
        CancellationToken cancellationToken) =>
        GetRowsAsync(dateFrom, dateTo, rowMode, supplierIds, new HashSet<Guid>(), expenseTypeIds, search, limit, offset, sort, cancellationToken);

    public async Task<ExpenseReportQueryData> GetRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<Guid> staffMemberIds,
        IReadOnlySet<Guid> expenseTypeIds,
        string? search,
        int? limit,
        int offset,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        if (IsNpgsql())
        {
            return await GetPostgresRowsAsync(
                dateFrom,
                dateTo,
                rowMode,
                supplierIds,
                staffMemberIds,
                expenseTypeIds,
                search,
                limit,
                offset,
                sort,
                cancellationToken);
        }

        var rows = new List<ExpenseReportRowDto>();
        var accrualTotal = 0m;
        var expenseTotal = 0m;
        var rowCount = 0;
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var normalizedSearch = search?.Trim().ToLowerInvariant();
        var useClientSearch = hasSearch && !(dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false);
        var fetchLimit = useClientSearch ? null : GetFetchLimit(offset, limit);
        var includeSuppliers = supplierIds.Count > 0 || staffMemberIds.Count == 0;
        var includeStaff = staffMemberIds.Count > 0 || supplierIds.Count == 0;
        var staffRows = new List<ExpenseReportRowDto>();
        var aggregateQuery = dbContext.SupplierAccruals.AsNoTracking()
            .Where(_ => false)
            .Select(_ => new { Category = 0, Total = 0m, Count = 0 });
        var hasAggregateQueries = false;

        if (rowMode is AllRows or AccrualRows)
        {
            if (expenseTypeIds.Count == 0)
            {
                var startingBalanceQuery = dbContext.Suppliers.AsNoTracking()
                    .Where(supplier => includeSuppliers && !supplier.IsArchived && supplier.StartingBalance != 0);
                if (supplierIds.Count > 0)
                {
                    startingBalanceQuery = startingBalanceQuery.Where(supplier => supplierIds.Contains(supplier.Id));
                }

                if (hasSearch && !useClientSearch && !"Стартовый баланс".Contains(normalizedSearch!, StringComparison.OrdinalIgnoreCase))
                {
                    startingBalanceQuery = startingBalanceQuery.Where(supplier => supplier.Name.ToLower().Contains(normalizedSearch!));
                }

                if (!useClientSearch)
                {
                    var startingBalanceAggregate = startingBalanceQuery
                        .GroupBy(_ => 1)
                        .Select(group => new
                        {
                            Category = StartingBalanceTotalCategory,
                            Total = group.Sum(supplier => supplier.StartingBalance),
                            Count = group.Count()
                        });
                    aggregateQuery = aggregateQuery.Concat(startingBalanceAggregate);
                    hasAggregateQueries = true;
                }

                var startingBalances = await ApplyLimit(startingBalanceQuery.OrderBy(supplier => supplier.Name).ThenBy(supplier => supplier.Id), fetchLimit)
                    .ToListAsync(cancellationToken);
                rows.AddRange(startingBalances.Select(supplier => new ExpenseReportRowDto(
                    StartingBalanceRows,
                    dateFrom,
                    dateFrom,
                    supplier.Id,
                    supplier.Name,
                    Guid.Empty,
                    "Стартовый баланс",
                    supplier.StartingBalance,
                    0m,
                    supplier.StartingBalance,
                    null,
                    "Начальное обязательство перед поставщиком")));
            }

            var accrualsQuery = dbContext.SupplierAccruals.AsNoTracking()
                .Include(accrual => accrual.Supplier)
                .Include(accrual => accrual.ExpenseType)
                .Where(accrual =>
                    includeSuppliers &&
                    !accrual.IsCanceled &&
                    accrual.AccountingMonth >= dateFrom &&
                    accrual.AccountingMonth <= dateTo);
            if (supplierIds.Count > 0)
            {
                accrualsQuery = accrualsQuery.Where(accrual => supplierIds.Contains(accrual.SupplierId));
            }

            if (expenseTypeIds.Count > 0)
            {
                accrualsQuery = accrualsQuery.Where(accrual => expenseTypeIds.Contains(accrual.ExpenseTypeId));
            }

            if (hasSearch && !useClientSearch)
            {
                accrualsQuery = accrualsQuery.Where(accrual =>
                    accrual.Supplier.Name.ToLower().Contains(normalizedSearch!) ||
                    accrual.ExpenseType.Name.ToLower().Contains(normalizedSearch!) ||
                    (accrual.DocumentNumber != null && accrual.DocumentNumber.ToLower().Contains(normalizedSearch!)));
            }

            if (!useClientSearch)
            {
                var accrualAggregate = accrualsQuery
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Category = AccrualTotalCategory,
                        Total = group.Sum(accrual => accrual.Amount),
                        Count = group.Count()
                    });
                aggregateQuery = aggregateQuery.Concat(accrualAggregate);
                hasAggregateQueries = true;
            }

            var accruals = await ApplyLimit(
                    accrualsQuery.OrderBy(accrual => accrual.AccountingMonth).ThenBy(accrual => accrual.Supplier.Name).ThenBy(accrual => accrual.Id),
                    fetchLimit)
                .ToListAsync(cancellationToken);
            rows.AddRange(accruals.Select(accrual => new ExpenseReportRowDto(
                AccrualRows,
                accrual.AccountingMonth,
                accrual.AccountingMonth,
                accrual.SupplierId,
                accrual.Supplier.Name,
                accrual.ExpenseTypeId,
                accrual.ExpenseType.Name,
                accrual.Amount,
                0m,
                accrual.Amount,
                accrual.DocumentNumber,
                accrual.Comment)));
        }

        if (rowMode is AllRows or PaymentRows)
        {
            var paymentsQuery = dbContext.FinancialOperations.AsNoTracking()
                .Include(operation => operation.Supplier)
                .Include(operation => operation.ExpenseType)
                .Where(operation =>
                    includeSuppliers &&
                    !operation.IsCanceled &&
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    operation.SupplierId != null &&
                    operation.ExpenseTypeId != null &&
                    operation.OperationDate >= dateFrom &&
                    operation.OperationDate <= dateTo);
            if (supplierIds.Count > 0)
            {
                paymentsQuery = paymentsQuery.Where(operation => supplierIds.Contains(operation.SupplierId!.Value));
            }

            if (expenseTypeIds.Count > 0)
            {
                paymentsQuery = paymentsQuery.Where(operation => expenseTypeIds.Contains(operation.ExpenseTypeId!.Value));
            }

            if (hasSearch && !useClientSearch)
            {
                paymentsQuery = paymentsQuery.Where(operation =>
                    operation.Supplier!.Name.ToLower().Contains(normalizedSearch!) ||
                    operation.ExpenseType!.Name.ToLower().Contains(normalizedSearch!) ||
                    (operation.DocumentNumber != null && operation.DocumentNumber.ToLower().Contains(normalizedSearch!)));
            }

            if (!useClientSearch)
            {
                var expenseAggregate = paymentsQuery
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Category = ExpenseTotalCategory,
                        Total = group.Sum(operation => operation.Amount),
                        Count = group.Count()
                    });
                aggregateQuery = aggregateQuery.Concat(expenseAggregate);
                hasAggregateQueries = true;
            }

            var payments = await ApplyLimit(
                    paymentsQuery.OrderBy(operation => operation.OperationDate).ThenBy(operation => operation.Supplier!.Name).ThenBy(operation => operation.Id),
                    fetchLimit)
                .ToListAsync(cancellationToken);
            rows.AddRange(payments.Select(operation => new ExpenseReportRowDto(
                PaymentRows,
                operation.OperationDate,
                operation.AccountingMonth,
                operation.SupplierId!.Value,
                operation.Supplier!.Name,
                operation.ExpenseTypeId!.Value,
                operation.ExpenseType!.Name,
                0m,
                operation.Amount,
                -operation.Amount,
                operation.DocumentNumber,
                operation.Comment)));
        }

        if (includeStaff && rowMode is AllRows or AccrualRows)
        {
            var staffAccrualSources = await (
                    from member in dbContext.StaffMembers.AsNoTracking()
                    where !member.IsArchived && (staffMemberIds.Count == 0 || staffMemberIds.Contains(member.Id))
                    from expenseType in dbContext.ExpenseTypes.AsNoTracking()
                    where !expenseType.IsArchived && expenseType.Code == "salary" && (expenseTypeIds.Count == 0 || expenseTypeIds.Contains(expenseType.Id))
                    select new
                    {
                        member.Id,
                        member.FullName,
                        member.Rate,
                        member.CreatedAtUtc,
                        ExpenseTypeId = expenseType.Id,
                        ExpenseTypeName = expenseType.Name
                    })
                .ToListAsync(cancellationToken);
            var months = EnumerateMonths(dateFrom, dateTo).ToArray();
            staffRows.AddRange(
                from source in staffAccrualSources
                from month in months
                where month >= NormalizeMonth(DateOnly.FromDateTime(source.CreatedAtUtc.UtcDateTime))
                select new ExpenseReportRowDto(
                    AccrualRows,
                    month,
                    month,
                    source.Id,
                    source.FullName,
                    source.ExpenseTypeId,
                    source.ExpenseTypeName,
                    source.Rate,
                    0m,
                    source.Rate,
                    null,
                    "Расчетная ставка сотрудника",
                    source.Id,
                    "staff"));
        }

        if (includeStaff && rowMode is AllRows or PaymentRows)
        {
            var staffPayments = await dbContext.FinancialOperations.AsNoTracking()
                .Include(operation => operation.StaffMember)
                .Include(operation => operation.ExpenseType)
                .Where(operation =>
                    includeStaff &&
                    !operation.IsCanceled &&
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    operation.StaffMemberId != null &&
                    operation.ExpenseTypeId != null &&
                    operation.OperationDate >= dateFrom &&
                    operation.OperationDate <= dateTo &&
                    (staffMemberIds.Count == 0 || staffMemberIds.Contains(operation.StaffMemberId.Value)) &&
                    (expenseTypeIds.Count == 0 || expenseTypeIds.Contains(operation.ExpenseTypeId.Value)))
                .ToListAsync(cancellationToken);
            staffRows.AddRange(staffPayments.Select(operation => new ExpenseReportRowDto(
                PaymentRows,
                operation.OperationDate,
                operation.AccountingMonth,
                operation.StaffMemberId!.Value,
                operation.StaffMember!.FullName,
                operation.ExpenseTypeId!.Value,
                operation.ExpenseType!.Name,
                0m,
                operation.Amount,
                -operation.Amount,
                operation.DocumentNumber,
                operation.Comment,
                operation.StaffMemberId,
                "staff")));
        }

        if (hasSearch)
        {
            var clientSearch = search!.Trim();
            staffRows = staffRows.Where(row =>
                    row.SupplierName.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ||
                    row.ExpenseTypeName.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ||
                    (row.DocumentNumber?.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        rows.AddRange(staffRows);

        if (!useClientSearch && hasAggregateQueries)
        {
            var aggregates = await aggregateQuery.ToListAsync(cancellationToken);
            accrualTotal = aggregates
                .Where(row => row.Category is StartingBalanceTotalCategory or AccrualTotalCategory)
                .Sum(row => row.Total);
            expenseTotal = aggregates
                .Where(row => row.Category == ExpenseTotalCategory)
                .Sum(row => row.Total);
            rowCount = aggregates.Sum(row => row.Count);
        }
        else if (useClientSearch)
        {
            var clientSearch = search!.Trim();
            rows = rows.Where(row =>
                    row.SupplierName.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ||
                    row.ExpenseTypeName.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ||
                    (row.DocumentNumber?.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            accrualTotal = rows.Sum(row => row.AccrualAmount);
            expenseTotal = rows.Sum(row => row.ExpenseAmount);
            rowCount = rows.Count;
        }

        if (!useClientSearch)
        {
            accrualTotal += staffRows.Sum(row => row.AccrualAmount);
            expenseTotal += staffRows.Sum(row => row.ExpenseAmount);
            rowCount += staffRows.Count;
        }

        var visibleRows = ApplyPage(
                ApplySort(rows, sort)
                    .ThenBy(row => row.SupplierName, StringComparer.Ordinal)
                    .ThenBy(row => row.ExpenseTypeName, StringComparer.Ordinal)
                    .ThenBy(row => row.SupplierId),
                offset,
                limit)
            .ToList();
        return new ExpenseReportQueryData(accrualTotal, expenseTotal, rowCount, visibleRows);
    }

    private async Task<ExpenseReportQueryData> GetPostgresRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<Guid> staffMemberIds,
        IReadOnlySet<Guid> expenseTypeIds,
        string? search,
        int? limit,
        int offset,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        if (rowMode == AllRows)
        {
            return await GetPostgresAllRowsAsync(
                dateFrom,
                dateTo,
                supplierIds,
                staffMemberIds,
                expenseTypeIds,
                search,
                limit,
                offset,
                sort,
                cancellationToken);
        }

        if (rowMode == AccrualRows)
        {
            return await GetPostgresAccrualRowsAsync(
                dateFrom,
                dateTo,
                supplierIds,
                staffMemberIds,
                expenseTypeIds,
                search,
                limit,
                offset,
                sort,
                cancellationToken);
        }

        if (rowMode == PaymentRows)
        {
            return await GetPostgresPaymentRowsAsync(
                dateFrom,
                dateTo,
                supplierIds,
                staffMemberIds,
                expenseTypeIds,
                search,
                limit,
                offset,
                sort,
                cancellationToken);
        }

        var normalizedSearch = search?.Trim().ToLowerInvariant();
        var hasSearch = !string.IsNullOrWhiteSpace(normalizedSearch);
        var includeSuppliers = supplierIds.Count > 0 || staffMemberIds.Count == 0;
        var includeStaff = staffMemberIds.Count > 0 || supplierIds.Count == 0;
        IQueryable<ExpenseReportProjection>? reportRows = null;
        var aggregateQuery = dbContext.SupplierAccruals.AsNoTracking()
            .Where(_ => false)
            .Select(_ => new { Category = 0, Total = 0m, Count = 0 });

        if (rowMode is AllRows or AccrualRows)
        {
            if (expenseTypeIds.Count == 0)
            {
                var startingBalances = dbContext.Suppliers.AsNoTracking()
                    .Where(supplier => includeSuppliers && !supplier.IsArchived && supplier.StartingBalance != 0);
                if (supplierIds.Count > 0)
                {
                    startingBalances = startingBalances.Where(supplier => supplierIds.Contains(supplier.Id));
                }

                if (hasSearch && !"Стартовый баланс".Contains(search!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    startingBalances = startingBalances.Where(supplier => supplier.Name.ToLower().Contains(normalizedSearch!));
                }

                aggregateQuery = aggregateQuery.Concat(startingBalances
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Category = StartingBalanceTotalCategory,
                        Total = group.Sum(supplier => supplier.StartingBalance),
                        Count = group.Count()
                    }));

                reportRows = startingBalances.Select(supplier => new ExpenseReportProjection
                {
                    RowType = StartingBalanceRows,
                    Date = dateFrom,
                    AccountingMonth = dateFrom,
                    SupplierId = supplier.Id,
                    SupplierName = supplier.Name,
                    ExpenseTypeId = Guid.Empty,
                    ExpenseTypeName = "Стартовый баланс",
                    AccrualAmount = supplier.StartingBalance,
                    ExpenseAmount = 0m,
                    Difference = supplier.StartingBalance,
                    DocumentNumber = null,
                    Comment = "Начальное обязательство перед поставщиком",
                    CreatedAtUtc = supplier.CreatedAtUtc,
                    RowId = supplier.Id,
                    StaffMemberId = null,
                    CounterpartyKind = "supplier"
                });
            }

            var accruals = dbContext.SupplierAccruals.AsNoTracking()
                .Where(accrual => includeSuppliers && !accrual.IsCanceled && accrual.AccountingMonth >= dateFrom && accrual.AccountingMonth <= dateTo);
            if (supplierIds.Count > 0)
            {
                accruals = accruals.Where(accrual => supplierIds.Contains(accrual.SupplierId));
            }

            if (expenseTypeIds.Count > 0)
            {
                accruals = accruals.Where(accrual => expenseTypeIds.Contains(accrual.ExpenseTypeId));
            }

            if (hasSearch)
            {
                accruals = accruals.Where(accrual =>
                    accrual.Supplier.Name.ToLower().Contains(normalizedSearch!) ||
                    accrual.ExpenseType.Name.ToLower().Contains(normalizedSearch!) ||
                    (accrual.DocumentNumber != null && accrual.DocumentNumber.ToLower().Contains(normalizedSearch!)));
            }

            aggregateQuery = aggregateQuery.Concat(accruals
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Category = AccrualTotalCategory,
                    Total = group.Sum(accrual => accrual.Amount),
                    Count = group.Count()
                }));

            var projectedAccruals = accruals.Select(accrual => new ExpenseReportProjection
            {
                RowType = AccrualRows,
                Date = accrual.AccountingMonth,
                AccountingMonth = accrual.AccountingMonth,
                SupplierId = accrual.SupplierId,
                SupplierName = accrual.Supplier.Name,
                ExpenseTypeId = accrual.ExpenseTypeId,
                ExpenseTypeName = accrual.ExpenseType.Name,
                AccrualAmount = accrual.Amount,
                ExpenseAmount = 0m,
                Difference = accrual.Amount,
                DocumentNumber = accrual.DocumentNumber,
                Comment = accrual.Comment,
                CreatedAtUtc = accrual.CreatedAtUtc,
                RowId = accrual.Id,
                StaffMemberId = null,
                CounterpartyKind = "supplier"
            });
            reportRows = reportRows == null ? projectedAccruals : reportRows.Concat(projectedAccruals);
        }

        if (rowMode is AllRows or PaymentRows)
        {
            var payments = dbContext.FinancialOperations.AsNoTracking()
                .Where(operation =>
                    includeSuppliers &&
                    !operation.IsCanceled &&
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    operation.SupplierId != null &&
                    operation.ExpenseTypeId != null &&
                    operation.OperationDate >= dateFrom &&
                    operation.OperationDate <= dateTo);
            if (supplierIds.Count > 0)
            {
                payments = payments.Where(operation => supplierIds.Contains(operation.SupplierId!.Value));
            }

            if (expenseTypeIds.Count > 0)
            {
                payments = payments.Where(operation => expenseTypeIds.Contains(operation.ExpenseTypeId!.Value));
            }

            if (hasSearch)
            {
                payments = payments.Where(operation =>
                    operation.Supplier!.Name.ToLower().Contains(normalizedSearch!) ||
                    operation.ExpenseType!.Name.ToLower().Contains(normalizedSearch!) ||
                    (operation.DocumentNumber != null && operation.DocumentNumber.ToLower().Contains(normalizedSearch!)));
            }

            aggregateQuery = aggregateQuery.Concat(payments
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Category = ExpenseTotalCategory,
                    Total = group.Sum(operation => operation.Amount),
                    Count = group.Count()
                }));

            var projectedPayments = payments.Select(operation => new ExpenseReportProjection
            {
                RowType = PaymentRows,
                Date = operation.OperationDate,
                AccountingMonth = operation.AccountingMonth,
                SupplierId = operation.SupplierId!.Value,
                SupplierName = operation.Supplier!.Name,
                ExpenseTypeId = operation.ExpenseTypeId!.Value,
                ExpenseTypeName = operation.ExpenseType!.Name,
                AccrualAmount = 0m,
                ExpenseAmount = operation.Amount,
                Difference = -operation.Amount,
                DocumentNumber = operation.DocumentNumber,
                Comment = operation.Comment,
                CreatedAtUtc = operation.CreatedAtUtc,
                RowId = operation.Id,
                StaffMemberId = null,
                CounterpartyKind = "supplier"
            });
            reportRows = reportRows == null ? projectedPayments : reportRows.Concat(projectedPayments);
        }

        if (includeStaff && rowMode is AllRows or AccrualRows)
        {
            const string staffAccrualSql = """
                SELECT
                    'accruals'::text AS "RowType",
                    month_value::date AS "Date",
                    month_value::date AS "AccountingMonth",
                    member."Id" AS "SupplierId",
                    member."FullName" AS "SupplierName",
                    expense_type."Id" AS "ExpenseTypeId",
                    expense_type."Name" AS "ExpenseTypeName",
                    member."Rate" AS "AccrualAmount",
                    0::numeric AS "ExpenseAmount",
                    member."Rate" AS "Difference",
                    NULL::text AS "DocumentNumber",
                    'Расчетная ставка сотрудника'::text AS "Comment",
                    member."CreatedAtUtc" AS "CreatedAtUtc",
                    md5(member."Id"::text || ':' || expense_type."Id"::text || ':' || month_value::date::text)::uuid AS "RowId",
                    member."Id" AS "StaffMemberId",
                    'staff'::text AS "CounterpartyKind"
                FROM staff_members member
                CROSS JOIN expense_types expense_type
                CROSS JOIN generate_series(@month_from::date, @month_to::date, interval '1 month') month_value
                WHERE member."IsArchived" = FALSE
                  AND expense_type."IsArchived" = FALSE
                  AND expense_type."Code" = 'salary'
                  AND month_value::date >= date_trunc('month', member."CreatedAtUtc" AT TIME ZONE 'UTC')::date
                  AND (@has_staff_filter = FALSE OR member."Id" = ANY(@staff_ids))
                  AND (@has_type_filter = FALSE OR expense_type."Id" = ANY(@expense_type_ids))
                  AND (@has_search = FALSE OR lower(member."FullName") LIKE '%' || @search || '%' OR lower(expense_type."Name") LIKE '%' || @search || '%')
                """;
            var staffAccrualSource = dbContext.Database.SqlQueryRaw<ExpenseReportProjection>(
                staffAccrualSql,
                new NpgsqlParameter<DateOnly>("month_from", NormalizeMonth(dateFrom)),
                new NpgsqlParameter<DateOnly>("month_to", NormalizeMonth(dateTo)),
                new NpgsqlParameter<bool>("has_staff_filter", staffMemberIds.Count > 0),
                new NpgsqlParameter<Guid[]>("staff_ids", staffMemberIds.ToArray()),
                new NpgsqlParameter<bool>("has_type_filter", expenseTypeIds.Count > 0),
                new NpgsqlParameter<Guid[]>("expense_type_ids", expenseTypeIds.ToArray()),
                new NpgsqlParameter<bool>("has_search", hasSearch),
                new NpgsqlParameter<string>("search", normalizedSearch ?? string.Empty));
            var staffAccruals = staffAccrualSource.Select(row => new ExpenseReportProjection
            {
                RowType = row.RowType,
                Date = row.Date,
                AccountingMonth = row.AccountingMonth,
                SupplierId = row.SupplierId,
                SupplierName = row.SupplierName,
                ExpenseTypeId = row.ExpenseTypeId,
                ExpenseTypeName = row.ExpenseTypeName,
                AccrualAmount = row.AccrualAmount,
                ExpenseAmount = row.ExpenseAmount,
                Difference = row.Difference,
                DocumentNumber = row.DocumentNumber,
                Comment = row.Comment,
                CreatedAtUtc = row.CreatedAtUtc,
                RowId = row.RowId,
                StaffMemberId = row.StaffMemberId,
                CounterpartyKind = row.CounterpartyKind
            });
            aggregateQuery = aggregateQuery.Concat(staffAccruals
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Category = AccrualTotalCategory,
                    Total = group.Sum(row => row.AccrualAmount),
                    Count = group.Count()
                }));
            reportRows = reportRows == null ? staffAccruals : reportRows.Concat(staffAccruals);
        }

        if (includeStaff && rowMode is AllRows or PaymentRows)
        {
            var staffPayments = dbContext.FinancialOperations.AsNoTracking()
                .Where(operation =>
                    !operation.IsCanceled &&
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    operation.StaffMemberId != null &&
                    operation.ExpenseTypeId != null &&
                    operation.OperationDate >= dateFrom &&
                    operation.OperationDate <= dateTo &&
                    (staffMemberIds.Count == 0 || staffMemberIds.Contains(operation.StaffMemberId.Value)) &&
                    (expenseTypeIds.Count == 0 || expenseTypeIds.Contains(operation.ExpenseTypeId.Value)) &&
                    (!hasSearch ||
                        operation.StaffMember!.FullName.ToLower().Contains(normalizedSearch!) ||
                        operation.ExpenseType!.Name.ToLower().Contains(normalizedSearch!) ||
                        (operation.DocumentNumber != null && operation.DocumentNumber.ToLower().Contains(normalizedSearch!))));
            aggregateQuery = aggregateQuery.Concat(staffPayments
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Category = ExpenseTotalCategory,
                    Total = group.Sum(operation => operation.Amount),
                    Count = group.Count()
                }));
            var projectedStaffPayments = staffPayments.Select(operation => new ExpenseReportProjection
            {
                RowType = PaymentRows,
                Date = operation.OperationDate,
                AccountingMonth = operation.AccountingMonth,
                SupplierId = operation.StaffMemberId!.Value,
                SupplierName = operation.StaffMember!.FullName,
                ExpenseTypeId = operation.ExpenseTypeId!.Value,
                ExpenseTypeName = operation.ExpenseType!.Name,
                AccrualAmount = 0m,
                ExpenseAmount = operation.Amount,
                Difference = -operation.Amount,
                DocumentNumber = operation.DocumentNumber,
                Comment = operation.Comment,
                CreatedAtUtc = operation.CreatedAtUtc,
                RowId = operation.Id,
                StaffMemberId = operation.StaffMemberId,
                CounterpartyKind = "staff"
            });
            reportRows = reportRows == null ? projectedStaffPayments : reportRows.Concat(projectedStaffPayments);
        }

        if (reportRows == null)
        {
            return new ExpenseReportQueryData(0m, 0m, 0, []);
        }

        var aggregates = await aggregateQuery.ToListAsync(cancellationToken);
        var rowCount = aggregates.Sum(row => row.Count);
        if (rowCount == 0)
        {
            return new ExpenseReportQueryData(0m, 0m, 0, []);
        }
        var accrualTotal = aggregates.Where(row => row.Category is StartingBalanceTotalCategory or AccrualTotalCategory).Sum(row => row.Total);
        var expenseTotal = aggregates.Where(row => row.Category == ExpenseTotalCategory).Sum(row => row.Total);

        var sortableRows = reportRows.Select(row => new ExpenseReportSortableProjection
        {
            RowType = row.RowType,
            Date = row.Date,
            AccountingMonth = row.AccountingMonth,
            SupplierId = row.SupplierId,
            SupplierName = row.SupplierName,
            ExpenseTypeId = row.ExpenseTypeId,
            ExpenseTypeName = row.ExpenseTypeName,
            AccrualAmount = row.AccrualAmount,
            ExpenseAmount = row.ExpenseAmount,
            Difference = row.Difference,
            DocumentNumber = row.DocumentNumber,
            Comment = row.Comment,
            CreatedAtUtc = row.CreatedAtUtc,
            RowId = row.RowId,
            StaffMemberId = row.StaffMemberId,
            CounterpartyKind = row.CounterpartyKind
        });
        var ordered = ApplyPostgresSort(sortableRows, sort)
            .ThenBy(row => row.SupplierName)
            .ThenBy(row => row.ExpenseTypeName)
            .ThenBy(row => row.RowId);
        IQueryable<ExpenseReportSortableProjection> page = offset > 0 ? ordered.Skip(offset) : ordered;
        if (limit is > 0)
        {
            page = page.Take(limit.Value);
        }

        var rows = await page
            .Select(row => new ExpenseReportRowDto(
                row.RowType,
                row.Date,
                row.AccountingMonth,
                row.SupplierId,
                row.SupplierName,
                row.ExpenseTypeId,
                row.ExpenseTypeName,
                row.AccrualAmount,
                row.ExpenseAmount,
                row.Difference,
                row.DocumentNumber,
                row.Comment,
                row.StaffMemberId,
                row.CounterpartyKind))
            .ToListAsync(cancellationToken);
        return new ExpenseReportQueryData(accrualTotal, expenseTotal, rowCount, rows);
    }

    private async Task<ExpenseReportQueryData> GetPostgresAllRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<Guid> staffMemberIds,
        IReadOnlySet<Guid> expenseTypeIds,
        string? search,
        int? limit,
        int offset,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var normalizedSearch = search?.Trim().ToLowerInvariant();
        var hasSearch = !string.IsNullOrWhiteSpace(normalizedSearch);
        var includeSuppliers = supplierIds.Count > 0 || staffMemberIds.Count == 0;
        var includeStaff = staffMemberIds.Count > 0 || supplierIds.Count == 0;
        var includeStartingBalances = includeSuppliers && expenseTypeIds.Count == 0;
        var sortColumn = sort.Field switch
        {
            "accountingMonth" => "accounting_month",
            "supplierName" => "counterparty_name",
            "expenseTypeName" => "expense_type_name",
            "accrualAmount" => "accrual_amount",
            "expenseAmount" => "expense_amount",
            "difference" => "difference",
            "documentNumber" => "document_number",
            _ => "row_date"
        };
        var direction = sort.Descending ? "DESC" : "ASC";
        var supplierClause = supplierIds.Count > 0 ? "AND supplier.\"Id\" = ANY(@supplier_ids)" : string.Empty;
        var staffClause = staffMemberIds.Count > 0 ? "AND member.\"Id\" = ANY(@staff_ids)" : string.Empty;
        var expenseTypeClause = expenseTypeIds.Count > 0 ? "AND expense_type.\"Id\" = ANY(@expense_type_ids)" : string.Empty;
        var startingSearchClause = hasSearch && !"Стартовый баланс".Contains(search!.Trim(), StringComparison.OrdinalIgnoreCase)
            ? "AND STRPOS(LOWER(supplier.\"Name\"), @search) > 0"
            : string.Empty;
        var supplierAccrualSearchClause = hasSearch
            ? """
              AND (STRPOS(LOWER(supplier."Name"), @search) > 0
                   OR STRPOS(LOWER(expense_type."Name"), @search) > 0
                   OR STRPOS(LOWER(accrual."DocumentNumber"), @search) > 0)
              """
            : string.Empty;
        var supplierPaymentSearchClause = hasSearch
            ? """
              AND (STRPOS(LOWER(supplier."Name"), @search) > 0
                   OR STRPOS(LOWER(expense_type."Name"), @search) > 0
                   OR STRPOS(LOWER(operation."DocumentNumber"), @search) > 0)
              """
            : string.Empty;
        var staffAccrualSearchClause = hasSearch
            ? """
              AND (STRPOS(LOWER(member."FullName"), @search) > 0
                   OR STRPOS(LOWER(expense_type."Name"), @search) > 0)
              """
            : string.Empty;
        var staffPaymentSearchClause = hasSearch
            ? """
              AND (STRPOS(LOWER(member."FullName"), @search) > 0
                   OR STRPOS(LOWER(expense_type."Name"), @search) > 0
                   OR STRPOS(LOWER(operation."DocumentNumber"), @search) > 0)
              """
            : string.Empty;
        var limitClause = limit is > 0 ? "LIMIT @limit" : string.Empty;
        var sql = $$"""
            WITH filtered_rows AS (
                SELECT supplier."Id" AS id,
                       'starting_balance'::text AS row_type,
                       @date_from::date AS row_date,
                       @date_from::date AS accounting_month,
                       supplier."Id" AS counterparty_id,
                       supplier."Name" AS counterparty_name,
                       '00000000-0000-0000-0000-000000000000'::uuid AS expense_type_id,
                       'Стартовый баланс'::text AS expense_type_name,
                       supplier."StartingBalance" AS accrual_amount,
                       0::numeric AS expense_amount,
                       supplier."StartingBalance" AS difference,
                       NULL::text AS document_number,
                       'Начальное обязательство перед поставщиком'::text AS comment,
                       supplier."CreatedAtUtc" AS created_at_utc,
                       NULL::uuid AS staff_member_id,
                       'supplier'::text AS counterparty_kind
                FROM suppliers supplier
                WHERE @include_starting_balances = TRUE
                  AND supplier."IsArchived" = FALSE
                  AND supplier."StartingBalance" <> 0
                  {{supplierClause}}
                  {{startingSearchClause}}
                UNION ALL
                SELECT accrual."Id", 'accruals'::text, accrual."AccountingMonth", accrual."AccountingMonth",
                       supplier."Id", supplier."Name", expense_type."Id", expense_type."Name",
                       accrual."Amount", 0::numeric, accrual."Amount", accrual."DocumentNumber", accrual."Comment",
                       accrual."CreatedAtUtc", NULL::uuid, 'supplier'::text
                FROM supplier_accruals accrual
                INNER JOIN suppliers supplier ON supplier."Id" = accrual."SupplierId"
                INNER JOIN expense_types expense_type ON expense_type."Id" = accrual."ExpenseTypeId"
                WHERE @include_suppliers = TRUE
                  AND accrual."IsCanceled" = FALSE
                  AND accrual."AccountingMonth" >= @date_from::date
                  AND accrual."AccountingMonth" <= @date_to::date
                  {{supplierClause}}
                  {{expenseTypeClause}}
                  {{supplierAccrualSearchClause}}
                UNION ALL
                SELECT operation."Id", 'payments'::text, operation."OperationDate", operation."AccountingMonth",
                       supplier."Id", supplier."Name", expense_type."Id", expense_type."Name",
                       0::numeric, operation."Amount", -operation."Amount", operation."DocumentNumber", operation."Comment",
                       operation."CreatedAtUtc", NULL::uuid, 'supplier'::text
                FROM financial_operations operation
                INNER JOIN suppliers supplier ON supplier."Id" = operation."SupplierId"
                INNER JOIN expense_types expense_type ON expense_type."Id" = operation."ExpenseTypeId"
                WHERE @include_suppliers = TRUE
                  AND operation."IsCanceled" = FALSE
                  AND operation."OperationKind" = 'expense'
                  AND operation."SupplierId" IS NOT NULL
                  AND operation."ExpenseTypeId" IS NOT NULL
                  AND operation."OperationDate" >= @date_from::date
                  AND operation."OperationDate" <= @date_to::date
                  {{supplierClause}}
                  {{expenseTypeClause}}
                  {{supplierPaymentSearchClause}}
                UNION ALL
                SELECT md5(member."Id"::text || ':' || expense_type."Id"::text || ':' || month_value::date::text)::uuid,
                       'accruals'::text, month_value::date, month_value::date,
                       member."Id", member."FullName", expense_type."Id", expense_type."Name",
                       member."Rate", 0::numeric, member."Rate", NULL::text,
                       'Расчетная ставка сотрудника'::text, member."CreatedAtUtc", member."Id", 'staff'::text
                FROM staff_members member
                CROSS JOIN expense_types expense_type
                CROSS JOIN generate_series(@month_from::date, @month_to::date, interval '1 month') month_value
                WHERE @include_staff = TRUE
                  AND member."IsArchived" = FALSE
                  AND expense_type."IsArchived" = FALSE
                  AND expense_type."Code" = 'salary'
                  AND month_value::date >= date_trunc('month', member."CreatedAtUtc" AT TIME ZONE 'UTC')::date
                  {{staffClause}}
                  {{expenseTypeClause}}
                  {{staffAccrualSearchClause}}
                UNION ALL
                SELECT operation."Id", 'payments'::text, operation."OperationDate", operation."AccountingMonth",
                       member."Id", member."FullName", expense_type."Id", expense_type."Name",
                       0::numeric, operation."Amount", -operation."Amount", operation."DocumentNumber", operation."Comment",
                       operation."CreatedAtUtc", member."Id", 'staff'::text
                FROM financial_operations operation
                INNER JOIN staff_members member ON member."Id" = operation."StaffMemberId"
                INNER JOIN expense_types expense_type ON expense_type."Id" = operation."ExpenseTypeId"
                WHERE @include_staff = TRUE
                  AND operation."IsCanceled" = FALSE
                  AND operation."OperationKind" = 'expense'
                  AND operation."StaffMemberId" IS NOT NULL
                  AND operation."ExpenseTypeId" IS NOT NULL
                  AND operation."OperationDate" >= @date_from::date
                  AND operation."OperationDate" <= @date_to::date
                  {{staffClause}}
                  {{expenseTypeClause}}
                  {{staffPaymentSearchClause}}
            ), page_rows AS (
                SELECT filtered_rows.*,
                       ROW_NUMBER() OVER (
                           ORDER BY {{sortColumn}} {{direction}}, counterparty_name, expense_type_name, id)::int AS row_order
                FROM filtered_rows
                ORDER BY {{sortColumn}} {{direction}}, counterparty_name, expense_type_name, id
                OFFSET @offset
                {{limitClause}}
            )
            SELECT {{PageCategory}} AS "Category", row_order AS "RowOrder", id AS "Id", row_type AS "RowType",
                   row_date AS "OperationDate", accounting_month AS "AccountingMonth",
                   counterparty_id AS "CounterpartyId", counterparty_name AS "CounterpartyName",
                   expense_type_id AS "ExpenseTypeId", expense_type_name AS "ExpenseTypeName",
                   accrual_amount AS "AccrualAmount", expense_amount AS "ExpenseAmount", difference AS "Difference",
                   document_number AS "DocumentNumber", comment AS "Comment", created_at_utc AS "CreatedAtUtc",
                   staff_member_id AS "StaffMemberId", counterparty_kind AS "CounterpartyKind",
                   0::numeric AS "AccrualTotal", 0::numeric AS "ExpenseTotal", 0 AS "RowCount"
            FROM page_rows
            UNION ALL
            SELECT {{TotalsCategory}}, 0, NULL::uuid, NULL::text, NULL::date, NULL::date, NULL::uuid, NULL::text,
                   NULL::uuid, NULL::text, 0::numeric, 0::numeric, 0::numeric, NULL::text, NULL::text,
                   NULL::timestamptz, NULL::uuid, NULL::text,
                   COALESCE(SUM(accrual_amount), 0), COALESCE(SUM(expense_amount), 0), COUNT(*)::int
            FROM filtered_rows
            ORDER BY "Category", "RowOrder"
            """;
        var parameters = new List<object>
        {
            new NpgsqlParameter<bool>("include_starting_balances", includeStartingBalances),
            new NpgsqlParameter<bool>("include_suppliers", includeSuppliers),
            new NpgsqlParameter<bool>("include_staff", includeStaff),
            new NpgsqlParameter<DateOnly>("date_from", dateFrom),
            new NpgsqlParameter<DateOnly>("date_to", dateTo),
            new NpgsqlParameter<DateOnly>("month_from", NormalizeMonth(dateFrom)),
            new NpgsqlParameter<DateOnly>("month_to", NormalizeMonth(dateTo)),
            new NpgsqlParameter<int>("offset", offset)
        };
        if (supplierIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("supplier_ids", supplierIds.ToArray()));
        }
        if (staffMemberIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("staff_ids", staffMemberIds.ToArray()));
        }
        if (expenseTypeIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("expense_type_ids", expenseTypeIds.ToArray()));
        }
        if (hasSearch)
        {
            parameters.Add(new NpgsqlParameter<string>("search", normalizedSearch!));
        }
        if (limit is > 0)
        {
            parameters.Add(new NpgsqlParameter<int>("limit", limit.Value));
        }

        var combinedRows = await dbContext.Database
            .SqlQueryRaw<ExpenseAllCombinedQueryRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        var totals = combinedRows.Single(row => row.Category == TotalsCategory);
        var rows = combinedRows
            .Where(row => row.Category == PageCategory)
            .Select(row => new ExpenseReportRowDto(
                row.RowType!,
                row.OperationDate!.Value,
                row.AccountingMonth!.Value,
                row.CounterpartyId!.Value,
                row.CounterpartyName!,
                row.ExpenseTypeId!.Value,
                row.ExpenseTypeName!,
                row.AccrualAmount,
                row.ExpenseAmount,
                row.Difference,
                row.DocumentNumber,
                row.Comment,
                row.StaffMemberId,
                row.CounterpartyKind!))
            .ToList();
        return new ExpenseReportQueryData(totals.AccrualTotal, totals.ExpenseTotal, totals.RowCount, rows);
    }

    private async Task<ExpenseReportQueryData> GetPostgresAccrualRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<Guid> staffMemberIds,
        IReadOnlySet<Guid> expenseTypeIds,
        string? search,
        int? limit,
        int offset,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var normalizedSearch = search?.Trim().ToLowerInvariant();
        var hasSearch = !string.IsNullOrWhiteSpace(normalizedSearch);
        var includeSuppliers = supplierIds.Count > 0 || staffMemberIds.Count == 0;
        var includeStaff = staffMemberIds.Count > 0 || supplierIds.Count == 0;
        var includeStartingBalances = includeSuppliers && expenseTypeIds.Count == 0;
        var sortColumn = sort.Field switch
        {
            "accountingMonth" => "accounting_month",
            "supplierName" => "counterparty_name",
            "expenseTypeName" => "expense_type_name",
            "accrualAmount" => "accrual_amount",
            "expenseAmount" => "expense_amount",
            "difference" => "difference",
            "documentNumber" => "document_number",
            _ => "row_date"
        };
        var direction = sort.Descending ? "DESC" : "ASC";
        var supplierClause = supplierIds.Count > 0 ? "AND supplier.\"Id\" = ANY(@supplier_ids)" : string.Empty;
        var staffClause = staffMemberIds.Count > 0 ? "AND member.\"Id\" = ANY(@staff_ids)" : string.Empty;
        var expenseTypeClause = expenseTypeIds.Count > 0 ? "AND expense_type.\"Id\" = ANY(@expense_type_ids)" : string.Empty;
        var startingSearchClause = hasSearch && !"Стартовый баланс".Contains(search!.Trim(), StringComparison.OrdinalIgnoreCase)
            ? "AND STRPOS(LOWER(supplier.\"Name\"), @search) > 0"
            : string.Empty;
        var supplierAccrualSearchClause = hasSearch
            ? """
              AND (STRPOS(LOWER(supplier."Name"), @search) > 0
                   OR STRPOS(LOWER(expense_type."Name"), @search) > 0
                   OR STRPOS(LOWER(accrual."DocumentNumber"), @search) > 0)
              """
            : string.Empty;
        var staffAccrualSearchClause = hasSearch
            ? """
              AND (STRPOS(LOWER(member."FullName"), @search) > 0
                   OR STRPOS(LOWER(expense_type."Name"), @search) > 0)
              """
            : string.Empty;
        var limitClause = limit is > 0 ? "LIMIT @limit" : string.Empty;
        var sql = $$"""
            WITH filtered_rows AS (
                SELECT supplier."Id" AS id,
                       'starting_balance'::text AS row_type,
                       @date_from::date AS row_date,
                       @date_from::date AS accounting_month,
                       supplier."Id" AS counterparty_id,
                       supplier."Name" AS counterparty_name,
                       '00000000-0000-0000-0000-000000000000'::uuid AS expense_type_id,
                       'Стартовый баланс'::text AS expense_type_name,
                       supplier."StartingBalance" AS accrual_amount,
                       0::numeric AS expense_amount,
                       supplier."StartingBalance" AS difference,
                       NULL::text AS document_number,
                       'Начальное обязательство перед поставщиком'::text AS comment,
                       supplier."CreatedAtUtc" AS created_at_utc,
                       NULL::uuid AS staff_member_id,
                       'supplier'::text AS counterparty_kind
                FROM suppliers supplier
                WHERE @include_starting_balances = TRUE
                  AND supplier."IsArchived" = FALSE
                  AND supplier."StartingBalance" <> 0
                  {{supplierClause}}
                  {{startingSearchClause}}
                UNION ALL
                SELECT accrual."Id", 'accruals'::text, accrual."AccountingMonth", accrual."AccountingMonth",
                       supplier."Id", supplier."Name", expense_type."Id", expense_type."Name",
                       accrual."Amount", 0::numeric, accrual."Amount", accrual."DocumentNumber", accrual."Comment",
                       accrual."CreatedAtUtc", NULL::uuid, 'supplier'::text
                FROM supplier_accruals accrual
                INNER JOIN suppliers supplier ON supplier."Id" = accrual."SupplierId"
                INNER JOIN expense_types expense_type ON expense_type."Id" = accrual."ExpenseTypeId"
                WHERE @include_suppliers = TRUE
                  AND accrual."IsCanceled" = FALSE
                  AND accrual."AccountingMonth" >= @date_from::date
                  AND accrual."AccountingMonth" <= @date_to::date
                  {{supplierClause}}
                  {{expenseTypeClause}}
                  {{supplierAccrualSearchClause}}
                UNION ALL
                SELECT md5(member."Id"::text || ':' || expense_type."Id"::text || ':' || month_value::date::text)::uuid,
                       'accruals'::text, month_value::date, month_value::date,
                       member."Id", member."FullName", expense_type."Id", expense_type."Name",
                       member."Rate", 0::numeric, member."Rate", NULL::text,
                       'Расчетная ставка сотрудника'::text, member."CreatedAtUtc", member."Id", 'staff'::text
                FROM staff_members member
                CROSS JOIN expense_types expense_type
                CROSS JOIN generate_series(@month_from::date, @month_to::date, interval '1 month') month_value
                WHERE @include_staff = TRUE
                  AND member."IsArchived" = FALSE
                  AND expense_type."IsArchived" = FALSE
                  AND expense_type."Code" = 'salary'
                  AND month_value::date >= date_trunc('month', member."CreatedAtUtc" AT TIME ZONE 'UTC')::date
                  {{staffClause}}
                  {{expenseTypeClause}}
                  {{staffAccrualSearchClause}}
            ), page_rows AS (
                SELECT filtered_rows.*,
                       ROW_NUMBER() OVER (
                           ORDER BY {{sortColumn}} {{direction}}, counterparty_name, expense_type_name, id)::int AS row_order
                FROM filtered_rows
                ORDER BY {{sortColumn}} {{direction}}, counterparty_name, expense_type_name, id
                OFFSET @offset
                {{limitClause}}
            )
            SELECT {{PageCategory}} AS "Category", row_order AS "RowOrder", id AS "Id", row_type AS "RowType",
                   row_date AS "OperationDate", accounting_month AS "AccountingMonth",
                   counterparty_id AS "CounterpartyId", counterparty_name AS "CounterpartyName",
                   expense_type_id AS "ExpenseTypeId", expense_type_name AS "ExpenseTypeName",
                   accrual_amount AS "AccrualAmount", expense_amount AS "ExpenseAmount", difference AS "Difference",
                   document_number AS "DocumentNumber", comment AS "Comment", created_at_utc AS "CreatedAtUtc",
                   staff_member_id AS "StaffMemberId", counterparty_kind AS "CounterpartyKind",
                   0::numeric AS "AccrualTotal", 0 AS "RowCount"
            FROM page_rows
            UNION ALL
            SELECT {{TotalsCategory}}, 0, NULL::uuid, NULL::text, NULL::date, NULL::date, NULL::uuid, NULL::text,
                   NULL::uuid, NULL::text, 0::numeric, 0::numeric, 0::numeric, NULL::text, NULL::text,
                   NULL::timestamptz, NULL::uuid, NULL::text, COALESCE(SUM(accrual_amount), 0), COUNT(*)::int
            FROM filtered_rows
            ORDER BY "Category", "RowOrder"
            """;
        var parameters = new List<object>
        {
            new NpgsqlParameter<bool>("include_starting_balances", includeStartingBalances),
            new NpgsqlParameter<bool>("include_suppliers", includeSuppliers),
            new NpgsqlParameter<bool>("include_staff", includeStaff),
            new NpgsqlParameter<DateOnly>("date_from", dateFrom),
            new NpgsqlParameter<DateOnly>("date_to", dateTo),
            new NpgsqlParameter<DateOnly>("month_from", NormalizeMonth(dateFrom)),
            new NpgsqlParameter<DateOnly>("month_to", NormalizeMonth(dateTo)),
            new NpgsqlParameter<int>("offset", offset)
        };
        if (supplierIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("supplier_ids", supplierIds.ToArray()));
        }
        if (staffMemberIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("staff_ids", staffMemberIds.ToArray()));
        }
        if (expenseTypeIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("expense_type_ids", expenseTypeIds.ToArray()));
        }
        if (hasSearch)
        {
            parameters.Add(new NpgsqlParameter<string>("search", normalizedSearch!));
        }
        if (limit is > 0)
        {
            parameters.Add(new NpgsqlParameter<int>("limit", limit.Value));
        }

        var combinedRows = await dbContext.Database
            .SqlQueryRaw<ExpenseAccrualCombinedQueryRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        var totals = combinedRows.Single(row => row.Category == TotalsCategory);
        var rows = combinedRows
            .Where(row => row.Category == PageCategory)
            .Select(row => new ExpenseReportRowDto(
                row.RowType!,
                row.OperationDate!.Value,
                row.AccountingMonth!.Value,
                row.CounterpartyId!.Value,
                row.CounterpartyName!,
                row.ExpenseTypeId!.Value,
                row.ExpenseTypeName!,
                row.AccrualAmount,
                row.ExpenseAmount,
                row.Difference,
                row.DocumentNumber,
                row.Comment,
                row.StaffMemberId,
                row.CounterpartyKind!))
            .ToList();
        return new ExpenseReportQueryData(totals.AccrualTotal, 0m, totals.RowCount, rows);
    }

    private async Task<ExpenseReportQueryData> GetPostgresPaymentRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<Guid> staffMemberIds,
        IReadOnlySet<Guid> expenseTypeIds,
        string? search,
        int? limit,
        int offset,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var includeSuppliers = supplierIds.Count > 0 || staffMemberIds.Count == 0;
        var includeStaff = staffMemberIds.Count > 0 || supplierIds.Count == 0;
        var sortColumn = sort.Field switch
        {
            "accountingMonth" => "accounting_month",
            "supplierName" => "counterparty_name",
            "expenseTypeName" => "expense_type_name",
            "accrualAmount" => "accrual_amount",
            "expenseAmount" => "expense_amount",
            "difference" => "difference",
            "documentNumber" => "document_number",
            _ => "operation_date"
        };
        var direction = sort.Descending ? "DESC" : "ASC";
        var supplierClause = supplierIds.Count > 0 ? "AND operation.\"SupplierId\" = ANY(@supplier_ids)" : string.Empty;
        var staffClause = staffMemberIds.Count > 0 ? "AND operation.\"StaffMemberId\" = ANY(@staff_ids)" : string.Empty;
        var expenseTypeClause = expenseTypeIds.Count > 0 ? "AND operation.\"ExpenseTypeId\" = ANY(@expense_type_ids)" : string.Empty;
        var supplierSearchClause = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : """
              AND (STRPOS(LOWER(supplier."Name"), @search) > 0
                   OR STRPOS(LOWER(expense_type."Name"), @search) > 0
                   OR STRPOS(LOWER(operation."DocumentNumber"), @search) > 0)
              """;
        var staffSearchClause = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : """
              AND (STRPOS(LOWER(member."FullName"), @search) > 0
                   OR STRPOS(LOWER(expense_type."Name"), @search) > 0
                   OR STRPOS(LOWER(operation."DocumentNumber"), @search) > 0)
              """;
        var limitClause = limit is > 0 ? "LIMIT @limit" : string.Empty;
        var sql = $$"""
            WITH filtered_rows AS (
                SELECT operation."Id" AS id,
                       operation."OperationDate" AS operation_date,
                       operation."AccountingMonth" AS accounting_month,
                       operation."SupplierId" AS counterparty_id,
                       supplier."Name" AS counterparty_name,
                       operation."ExpenseTypeId" AS expense_type_id,
                       expense_type."Name" AS expense_type_name,
                       0::numeric AS accrual_amount,
                       operation."Amount" AS expense_amount,
                       -operation."Amount" AS difference,
                       operation."DocumentNumber" AS document_number,
                       operation."Comment" AS comment,
                       operation."CreatedAtUtc" AS created_at_utc,
                       NULL::uuid AS staff_member_id,
                       'supplier'::text AS counterparty_kind
                FROM financial_operations operation
                INNER JOIN suppliers supplier ON supplier."Id" = operation."SupplierId"
                INNER JOIN expense_types expense_type ON expense_type."Id" = operation."ExpenseTypeId"
                WHERE @include_suppliers = TRUE
                  AND operation."IsCanceled" = FALSE
                  AND operation."OperationKind" = 'expense'
                  AND operation."SupplierId" IS NOT NULL
                  AND operation."ExpenseTypeId" IS NOT NULL
                  AND operation."OperationDate" >= @date_from::date
                  AND operation."OperationDate" <= @date_to::date
                  {{supplierClause}}
                  {{expenseTypeClause}}
                  {{supplierSearchClause}}
                UNION ALL
                SELECT operation."Id", operation."OperationDate", operation."AccountingMonth",
                       operation."StaffMemberId", member."FullName", operation."ExpenseTypeId",
                       expense_type."Name", 0::numeric, operation."Amount", -operation."Amount",
                       operation."DocumentNumber", operation."Comment", operation."CreatedAtUtc",
                       operation."StaffMemberId", 'staff'::text
                FROM financial_operations operation
                INNER JOIN staff_members member ON member."Id" = operation."StaffMemberId"
                INNER JOIN expense_types expense_type ON expense_type."Id" = operation."ExpenseTypeId"
                WHERE @include_staff = TRUE
                  AND operation."IsCanceled" = FALSE
                  AND operation."OperationKind" = 'expense'
                  AND operation."StaffMemberId" IS NOT NULL
                  AND operation."ExpenseTypeId" IS NOT NULL
                  AND operation."OperationDate" >= @date_from::date
                  AND operation."OperationDate" <= @date_to::date
                  {{staffClause}}
                  {{expenseTypeClause}}
                  {{staffSearchClause}}
            ), page_rows AS (
                SELECT filtered_rows.*,
                       ROW_NUMBER() OVER (
                           ORDER BY {{sortColumn}} {{direction}}, counterparty_name, expense_type_name, id)::int AS row_order
                FROM filtered_rows
                ORDER BY {{sortColumn}} {{direction}}, counterparty_name, expense_type_name, id
                OFFSET @offset
                {{limitClause}}
            )
            SELECT {{PageCategory}} AS "Category", row_order AS "RowOrder", id AS "Id",
                   operation_date AS "OperationDate", accounting_month AS "AccountingMonth",
                   counterparty_id AS "CounterpartyId", counterparty_name AS "CounterpartyName",
                   expense_type_id AS "ExpenseTypeId", expense_type_name AS "ExpenseTypeName",
                   accrual_amount AS "AccrualAmount", expense_amount AS "ExpenseAmount", difference AS "Difference",
                   document_number AS "DocumentNumber", comment AS "Comment", created_at_utc AS "CreatedAtUtc",
                   staff_member_id AS "StaffMemberId", counterparty_kind AS "CounterpartyKind",
                   0::numeric AS "ExpenseTotal", 0 AS "RowCount"
            FROM page_rows
            UNION ALL
            SELECT {{TotalsCategory}}, 0, NULL::uuid, NULL::date, NULL::date, NULL::uuid, NULL::text,
                   NULL::uuid, NULL::text, 0::numeric, 0::numeric, 0::numeric, NULL::text, NULL::text,
                   NULL::timestamptz, NULL::uuid, NULL::text, COALESCE(SUM(expense_amount), 0), COUNT(*)::int
            FROM filtered_rows
            ORDER BY "Category", "RowOrder"
            """;
        var parameters = new List<object>
        {
            new NpgsqlParameter<bool>("include_suppliers", includeSuppliers),
            new NpgsqlParameter<bool>("include_staff", includeStaff),
            new NpgsqlParameter<DateOnly>("date_from", dateFrom),
            new NpgsqlParameter<DateOnly>("date_to", dateTo),
            new NpgsqlParameter<int>("offset", offset)
        };
        if (supplierIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("supplier_ids", supplierIds.ToArray()));
        }
        if (staffMemberIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("staff_ids", staffMemberIds.ToArray()));
        }
        if (expenseTypeIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("expense_type_ids", expenseTypeIds.ToArray()));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add(new NpgsqlParameter<string>("search", search.Trim().ToLowerInvariant()));
        }
        if (limit is > 0)
        {
            parameters.Add(new NpgsqlParameter<int>("limit", limit.Value));
        }

        var combinedRows = await dbContext.Database
            .SqlQueryRaw<ExpensePaymentCombinedQueryRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        var totals = combinedRows.Single(row => row.Category == TotalsCategory);
        var rows = combinedRows
            .Where(row => row.Category == PageCategory)
            .Select(row => new ExpenseReportRowDto(
                PaymentRows,
                row.OperationDate!.Value,
                row.AccountingMonth!.Value,
                row.CounterpartyId!.Value,
                row.CounterpartyName!,
                row.ExpenseTypeId!.Value,
                row.ExpenseTypeName!,
                row.AccrualAmount,
                row.ExpenseAmount,
                row.Difference,
                row.DocumentNumber,
                row.Comment,
                row.StaffMemberId,
                row.CounterpartyKind!))
            .ToList();
        return new ExpenseReportQueryData(0m, totals.ExpenseTotal, totals.RowCount, rows);
    }

    private static IOrderedQueryable<ExpenseReportSortableProjection> ApplyPostgresSort(IQueryable<ExpenseReportSortableProjection> rows, ReportSort sort) =>
        sort.Field switch
        {
            "accountingMonth" => sort.Descending ? rows.OrderByDescending(row => row.AccountingMonth) : rows.OrderBy(row => row.AccountingMonth),
            "supplierName" => sort.Descending ? rows.OrderByDescending(row => row.SupplierName) : rows.OrderBy(row => row.SupplierName),
            "expenseTypeName" => sort.Descending ? rows.OrderByDescending(row => row.ExpenseTypeName) : rows.OrderBy(row => row.ExpenseTypeName),
            "accrualAmount" => sort.Descending ? rows.OrderByDescending(row => row.AccrualAmount) : rows.OrderBy(row => row.AccrualAmount),
            "expenseAmount" => sort.Descending ? rows.OrderByDescending(row => row.ExpenseAmount) : rows.OrderBy(row => row.ExpenseAmount),
            "difference" => sort.Descending ? rows.OrderByDescending(row => row.Difference) : rows.OrderBy(row => row.Difference),
            "documentNumber" => sort.Descending ? rows.OrderByDescending(row => row.DocumentNumber) : rows.OrderBy(row => row.DocumentNumber),
            _ => sort.Descending ? rows.OrderByDescending(row => row.Date) : rows.OrderBy(row => row.Date)
        };

    private static IQueryable<T> ApplyLimit<T>(IQueryable<T> query, int? limit) =>
        limit is > 0 ? query.Take(limit.Value) : query;

    private static IEnumerable<T> ApplyPage<T>(IEnumerable<T> rows, int offset, int? limit)
    {
        var page = offset > 0 ? rows.Skip(offset) : rows;
        return limit is > 0 ? page.Take(limit.Value) : page;
    }

    private static int? GetFetchLimit(int offset, int? limit) =>
        limit is > 0 ? (int)Math.Min((long)offset + limit.Value, int.MaxValue) : null;

    private static IEnumerable<DateOnly> EnumerateMonths(DateOnly dateFrom, DateOnly dateTo)
    {
        for (var month = NormalizeMonth(dateFrom); month <= NormalizeMonth(dateTo); month = month.AddMonths(1))
        {
            yield return month;
        }
    }

    private static DateOnly NormalizeMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private bool IsNpgsql() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static IOrderedEnumerable<ExpenseReportRowDto> ApplySort(IEnumerable<ExpenseReportRowDto> rows, ReportSort sort) =>
        sort.Field switch
        {
            "accountingMonth" => sort.Descending ? rows.OrderByDescending(row => row.AccountingMonth) : rows.OrderBy(row => row.AccountingMonth),
            "supplierName" => sort.Descending ? rows.OrderByDescending(row => row.SupplierName, StringComparer.Ordinal) : rows.OrderBy(row => row.SupplierName, StringComparer.Ordinal),
            "expenseTypeName" => sort.Descending ? rows.OrderByDescending(row => row.ExpenseTypeName, StringComparer.Ordinal) : rows.OrderBy(row => row.ExpenseTypeName, StringComparer.Ordinal),
            "accrualAmount" => sort.Descending ? rows.OrderByDescending(row => row.AccrualAmount) : rows.OrderBy(row => row.AccrualAmount),
            "expenseAmount" => sort.Descending ? rows.OrderByDescending(row => row.ExpenseAmount) : rows.OrderBy(row => row.ExpenseAmount),
            "difference" => sort.Descending ? rows.OrderByDescending(row => row.Difference) : rows.OrderBy(row => row.Difference),
            "documentNumber" => sort.Descending ? rows.OrderByDescending(row => row.DocumentNumber, StringComparer.Ordinal) : rows.OrderBy(row => row.DocumentNumber, StringComparer.Ordinal),
            _ => sort.Descending ? rows.OrderByDescending(row => row.Date) : rows.OrderBy(row => row.Date)
        };

    private sealed class ExpenseReportProjection : ExpenseReportSortableProjection
    {
    }

    private sealed record ExpenseAllCombinedQueryRow(
        int Category,
        int RowOrder,
        Guid? Id,
        string? RowType,
        DateOnly? OperationDate,
        DateOnly? AccountingMonth,
        Guid? CounterpartyId,
        string? CounterpartyName,
        Guid? ExpenseTypeId,
        string? ExpenseTypeName,
        decimal AccrualAmount,
        decimal ExpenseAmount,
        decimal Difference,
        string? DocumentNumber,
        string? Comment,
        DateTimeOffset? CreatedAtUtc,
        Guid? StaffMemberId,
        string? CounterpartyKind,
        decimal AccrualTotal,
        decimal ExpenseTotal,
        int RowCount);

    private sealed record ExpenseAccrualCombinedQueryRow(
        int Category,
        int RowOrder,
        Guid? Id,
        string? RowType,
        DateOnly? OperationDate,
        DateOnly? AccountingMonth,
        Guid? CounterpartyId,
        string? CounterpartyName,
        Guid? ExpenseTypeId,
        string? ExpenseTypeName,
        decimal AccrualAmount,
        decimal ExpenseAmount,
        decimal Difference,
        string? DocumentNumber,
        string? Comment,
        DateTimeOffset? CreatedAtUtc,
        Guid? StaffMemberId,
        string? CounterpartyKind,
        decimal AccrualTotal,
        int RowCount);

    private sealed record ExpensePaymentCombinedQueryRow(
        int Category,
        int RowOrder,
        Guid? Id,
        DateOnly? OperationDate,
        DateOnly? AccountingMonth,
        Guid? CounterpartyId,
        string? CounterpartyName,
        Guid? ExpenseTypeId,
        string? ExpenseTypeName,
        decimal AccrualAmount,
        decimal ExpenseAmount,
        decimal Difference,
        string? DocumentNumber,
        string? Comment,
        DateTimeOffset? CreatedAtUtc,
        Guid? StaffMemberId,
        string? CounterpartyKind,
        decimal ExpenseTotal,
        int RowCount);

    private class ExpenseReportSortableProjection
    {
        public string RowType { get; init; } = string.Empty;
        public DateOnly Date { get; init; }
        public DateOnly AccountingMonth { get; init; }
        public Guid SupplierId { get; init; }
        public string SupplierName { get; init; } = string.Empty;
        public Guid ExpenseTypeId { get; init; }
        public string ExpenseTypeName { get; init; } = string.Empty;
        public decimal AccrualAmount { get; init; }
        public decimal ExpenseAmount { get; init; }
        public decimal Difference { get; init; }
        public string? DocumentNumber { get; init; }
        public string? Comment { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public Guid RowId { get; init; }
        public Guid? StaffMemberId { get; init; }
        public string CounterpartyKind { get; init; } = "supplier";
    }
}
