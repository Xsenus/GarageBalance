using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFinancialJournalQuery(GarageBalanceDbContext dbContext) : IFinancialJournalQuery
{
    private const int DefaultLimit = 25;
    private const int MaximumLimit = 100;
    private const int MaximumOffset = 1_000_000;

    public async Task<FinancePagedResult<FinancialJournalEntryDto>> GetPageAsync(
        FinancialJournalRequest request,
        CancellationToken cancellationToken)
    {
        var offset = Math.Clamp(request.Offset ?? 0, 0, MaximumOffset);
        var limit = Math.Clamp(request.Limit ?? DefaultLimit, 1, MaximumLimit);
        var entityType = Normalize(request.EntityType);
        var counterparty = Normalize(request.Counterparty)?.ToLowerInvariant();
        var status = Normalize(request.Status)?.ToLowerInvariant();
        var document = Normalize(request.Document)?.ToLowerInvariant();

        var take = checked(offset + limit);
        var segments = new List<FinancialJournalSegment>(7)
        {
            await ReadSegmentAsync(BuildFinancialOperations(), "financial_operation", request, entityType, counterparty, status, document, take, cancellationToken),
            await ReadSegmentAsync(BuildAccruals(), "accrual", request, entityType, counterparty, status, document, take, cancellationToken),
            await ReadSegmentAsync(BuildSupplierAccruals(), "supplier_accrual", request, entityType, counterparty, status, document, take, cancellationToken),
            await ReadSegmentAsync(BuildStaffSalaryAdjustments(), "staff_salary_adjustment", request, entityType, counterparty, status, document, take, cancellationToken),
            await ReadFundSegmentAsync(request, entityType, counterparty, status, document, take, cancellationToken),
            await ReadSegmentAsync(BuildCashBankTransfers(), "cash_bank_transfer", request, entityType, counterparty, status, document, take, cancellationToken),
            await ReadSegmentAsync(BuildCashBankBalanceOperations(), "cash_bank_balance_operation", request, entityType, counterparty, status, document, take, cancellationToken)
        };
        var totalCount = segments.Sum(segment => segment.TotalCount);
        var items = segments
            .SelectMany(segment => segment.Rows)
            .OrderByDescending(row => row.OperationDate)
            .ThenByDescending(row => row.CreatedAtUtc)
            .ThenBy(row => row.Id)
            .Skip(offset)
            .Take(limit)
            .Select(ToDto)
            .ToArray();
        return new FinancePagedResult<FinancialJournalEntryDto>(items, totalCount, offset, limit);
    }

    private static IQueryable<FinancialJournalRow> ApplyFilters(
        IQueryable<FinancialJournalRow> query,
        FinancialJournalRequest request,
        string? counterparty,
        string? status,
        string? document)
    {
        if (request.DateFrom.HasValue)
        {
            query = query.Where(row => row.OperationDate >= request.DateFrom.Value);
        }

        if (request.DateTo.HasValue)
        {
            query = query.Where(row => row.OperationDate <= request.DateTo.Value);
        }

        if (counterparty is not null)
        {
            query = query.Where(row => row.Counterparty.ToLower().Contains(counterparty));
        }

        if (status is "active")
        {
            query = query.Where(row => !row.IsCanceled);
        }
        else if (status is "canceled")
        {
            query = query.Where(row => row.IsCanceled);
        }

        if (document is not null)
        {
            query = query.Where(row => row.DocumentNumber != null && row.DocumentNumber.ToLower().Contains(document));
        }

        return query;
    }

    private async Task<FinancialJournalSegment> ReadSegmentAsync(
        IQueryable<FinancialJournalRow> source,
        string sourceEntityType,
        FinancialJournalRequest request,
        string? entityType,
        string? counterparty,
        string? status,
        string? document,
        int take,
        CancellationToken cancellationToken)
    {
        if (entityType is not null && entityType != sourceEntityType)
        {
            return new FinancialJournalSegment(0, []);
        }

        var query = ApplyFilters(source, request, counterparty, status, document);
        var count = await query.CountAsync(cancellationToken);
        var ordered = query.OrderByDescending(row => row.OperationDate);
        var pageQuery = IsSqliteProvider()
            ? ordered.ThenBy(row => row.Id).Take(take)
            : ordered.ThenByDescending(row => row.CreatedAtUtc).ThenBy(row => row.Id).Take(take);
        var rows = await pageQuery.ToListAsync(cancellationToken);
        return new FinancialJournalSegment(count, rows);
    }

    private IQueryable<FinancialJournalRow> BuildFinancialOperations() =>
        dbContext.FinancialOperations.AsNoTracking().Select(operation => new FinancialJournalRow
        {
            Id = operation.Id,
            EntityType = "financial_operation",
            OperationType = operation.OperationKind,
            OperationDate = operation.OperationDate,
            AccountingMonth = operation.AccountingMonth,
            Amount = operation.Amount,
            Counterparty = operation.Garage != null
                ? "Гараж " + operation.Garage.Number + (operation.Garage.Owner != null
                    ? " · " + operation.Garage.Owner.LastName + " " + operation.Garage.Owner.FirstName + (operation.Garage.Owner.MiddleName != null ? " " + operation.Garage.Owner.MiddleName : "")
                    : "")
                : operation.StaffMember != null
                    ? operation.StaffMember.FullName
                    : operation.Supplier != null
                        ? operation.Supplier.Name
                        : operation.CounterpartyName ?? "Контрагент не указан",
            Category = operation.OperationKind == FinancialOperationKinds.Income
                ? operation.IncomeType != null ? operation.IncomeType.Name : "Поступление"
                : operation.ExpenseType != null ? operation.ExpenseType.Name : "Выплата",
            DocumentNumber = operation.DocumentNumber,
            Comment = operation.Comment,
            Source = operation.ReceiptBatchId != null ? "receipt_batch" : "manual",
            IsCanceled = operation.IsCanceled,
            CreatedAtUtc = operation.CreatedAtUtc,
            Version = null,
            CanEdit = operation.FeeCampaignId == null,
            CanCancel = operation.FeeCampaignId == null,
            CanRestore = operation.FeeCampaignId == null,
            ProtectionReason = operation.FeeCampaignId != null
                ? "Платёж связан со сбором и изменяется только через жизненный цикл сбора."
                : null,
            CorrectionHint = operation.FeeCampaignId != null
                ? "Исправьте или закройте сбор в разделе «Тарифы и сборы»."
                : null
        });

    private IQueryable<FinancialJournalRow> BuildAccruals() =>
        dbContext.Accruals.AsNoTracking().Select(accrual => new FinancialJournalRow
        {
            Id = accrual.Id,
            EntityType = "accrual",
            OperationType = "accrual",
            OperationDate = accrual.AccountingMonth,
            AccountingMonth = accrual.AccountingMonth,
            Amount = accrual.Amount,
            Counterparty = "Гараж " + accrual.Garage.Number + (accrual.Garage.Owner != null
                ? " · " + accrual.Garage.Owner.LastName + " " + accrual.Garage.Owner.FirstName + (accrual.Garage.Owner.MiddleName != null ? " " + accrual.Garage.Owner.MiddleName : "")
                : ""),
            Category = accrual.IncomeType.Name,
            DocumentNumber = null,
            Comment = accrual.Comment,
            Source = accrual.Source,
            IsCanceled = accrual.IsCanceled,
            CreatedAtUtc = accrual.CreatedAtUtc,
            Version = null,
            CanEdit = accrual.FeeCampaignId == null && accrual.IrregularPaymentId == null && accrual.Basis == null && accrual.Source == AccrualSources.Manual,
            CanCancel = accrual.FeeCampaignId == null,
            CanRestore = accrual.FeeCampaignId == null,
            ProtectionReason = accrual.FeeCampaignId != null
                ? "Начисление сбора защищено от прямого изменения."
                : accrual.Source == AccrualSources.Regular
                    ? "Автоматическое регулярное начисление нельзя исправлять как ручную сумму."
                    : accrual.IrregularPaymentId != null || accrual.Basis != null
                        ? "Разовое начисление сохраняет исходное основание и не переназначается напрямую."
                        : null,
            CorrectionHint = accrual.FeeCampaignId != null
                ? "Пересчитайте или закройте соответствующий сбор."
                : accrual.Source == AccrualSources.Regular
                    ? "Используйте безопасный предпросмотр перерасчёта неоплаченных начислений."
                    : accrual.IrregularPaymentId != null || accrual.Basis != null
                        ? "Отмените запись и создайте корректное разовое начисление."
                        : null
        });

    private IQueryable<FinancialJournalRow> BuildSupplierAccruals() =>
        dbContext.SupplierAccruals.AsNoTracking().Select(accrual => new FinancialJournalRow
        {
            Id = accrual.Id,
            EntityType = "supplier_accrual",
            OperationType = "supplier_accrual",
            OperationDate = accrual.AccountingMonth,
            AccountingMonth = accrual.AccountingMonth,
            Amount = accrual.Amount,
            Counterparty = accrual.Supplier.Name,
            Category = accrual.ExpenseType.Name,
            DocumentNumber = accrual.DocumentNumber,
            Comment = accrual.Comment,
            Source = accrual.Source,
            IsCanceled = accrual.IsCanceled,
            CreatedAtUtc = accrual.CreatedAtUtc,
            Version = null,
            CanEdit = accrual.SourceFinancialOperationId == null,
            CanCancel = accrual.SourceFinancialOperationId == null,
            CanRestore = accrual.SourceFinancialOperationId == null,
            ProtectionReason = accrual.SourceFinancialOperationId != null
                ? "Начисление создано вместе с кассовой выплатой и изменяется через исходную выплату."
                : null,
            CorrectionHint = accrual.SourceFinancialOperationId != null
                ? "Откройте связанную выплату в журнале."
                : null
        });

    private IQueryable<FinancialJournalRow> BuildStaffSalaryAdjustments() =>
        dbContext.StaffSalaryAdjustments.AsNoTracking().Select(adjustment => new FinancialJournalRow
        {
            Id = adjustment.Id,
            EntityType = "staff_salary_adjustment",
            OperationType = adjustment.AdjustmentType,
            OperationDate = adjustment.AccountingMonth,
            AccountingMonth = adjustment.AccountingMonth,
            Amount = adjustment.Amount,
            Counterparty = adjustment.StaffMember.FullName,
            Category = adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Bonus ? "Премия" : "Штраф",
            DocumentNumber = adjustment.DocumentNumber,
            Comment = adjustment.Reason,
            Source = "manual",
            IsCanceled = adjustment.IsCanceled,
            CreatedAtUtc = adjustment.CreatedAtUtc,
            Version = adjustment.Version,
            CanEdit = true,
            CanCancel = true,
            CanRestore = true,
            ProtectionReason = null,
            CorrectionHint = "Изменение доступно в карточке сотрудника."
        });

    private async Task<FinancialJournalSegment> ReadFundSegmentAsync(
        FinancialJournalRequest request,
        string? entityType,
        string? counterparty,
        string? status,
        string? document,
        int take,
        CancellationToken cancellationToken)
    {
        if (entityType is not null && entityType != "fund_operation")
        {
            return new FinancialJournalSegment(0, []);
        }

        var query = dbContext.FundOperations.AsNoTracking();
        if (request.DateFrom.HasValue)
        {
            var from = new DateTimeOffset(request.DateFrom.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(operation => operation.CreatedAtUtc >= from);
        }

        if (request.DateTo.HasValue)
        {
            var until = new DateTimeOffset(request.DateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(operation => operation.CreatedAtUtc < until);
        }

        if (counterparty is not null)
        {
            query = query.Where(operation =>
                (operation.SourceFinancialOperation != null && operation.SourceFinancialOperation.Garage != null && ("Гараж " + operation.SourceFinancialOperation.Garage.Number).ToLower().Contains(counterparty)) ||
                (operation.SourceFinancialOperation != null && operation.SourceFinancialOperation.Supplier != null && operation.SourceFinancialOperation.Supplier.Name.ToLower().Contains(counterparty)) ||
                (operation.SourceFinancialOperation != null && operation.SourceFinancialOperation.StaffMember != null && operation.SourceFinancialOperation.StaffMember.FullName.ToLower().Contains(counterparty)) ||
                (operation.SourceFinancialOperation != null && operation.SourceFinancialOperation.CounterpartyName != null && operation.SourceFinancialOperation.CounterpartyName.ToLower().Contains(counterparty)) ||
                (operation.SourceFinancialOperation == null && "ручная операция фонда".Contains(counterparty)));
        }

        if (status is "active")
        {
            query = query.Where(operation => !operation.IsCanceled);
        }
        else if (status is "canceled")
        {
            query = query.Where(operation => operation.IsCanceled);
        }

        if (document is not null)
        {
            query = query.Where(operation => operation.SourceFinancialOperation != null &&
                operation.SourceFinancialOperation.DocumentNumber != null &&
                operation.SourceFinancialOperation.DocumentNumber.ToLower().Contains(document));
        }

        var count = await query.CountAsync(cancellationToken);
        var ordered = IsSqliteProvider()
            ? query.OrderByDescending(operation => operation.Id)
            : query.OrderByDescending(operation => operation.CreatedAtUtc).ThenBy(operation => operation.Id);
        var data = await ordered
            .Take(take)
            .Select(operation => new FundJournalData(
                operation.Id,
                operation.OperationKind,
                operation.Amount,
                operation.SourceFinancialOperation != null
                    ? operation.SourceFinancialOperation.Garage != null
                        ? "Гараж " + operation.SourceFinancialOperation.Garage.Number
                        : operation.SourceFinancialOperation.Supplier != null
                            ? operation.SourceFinancialOperation.Supplier.Name
                            : operation.SourceFinancialOperation.StaffMember != null
                                ? operation.SourceFinancialOperation.StaffMember.FullName
                                : operation.SourceFinancialOperation.CounterpartyName ?? "Системное распределение"
                    : "Ручная операция фонда",
                operation.Fund.Name,
                operation.SourceFinancialOperation != null ? operation.SourceFinancialOperation.DocumentNumber : null,
                operation.Reason,
                operation.SourceFinancialOperationId != null,
                operation.IsCanceled,
                operation.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        var rows = data.Select(operation => new FinancialJournalRow
        {
            Id = operation.Id,
            EntityType = "fund_operation",
            OperationType = operation.OperationKind,
            OperationDate = DateOnly.FromDateTime(operation.CreatedAtUtc.UtcDateTime),
            AccountingMonth = null,
            Amount = operation.Amount,
            Counterparty = operation.Counterparty,
            Category = operation.FundName,
            DocumentNumber = operation.DocumentNumber,
            Comment = operation.Reason,
            Source = operation.IsDerived ? "derived" : "manual",
            IsCanceled = operation.IsCanceled,
            CreatedAtUtc = operation.CreatedAtUtc,
            Version = null,
            CanEdit = !operation.IsDerived,
            CanCancel = !operation.IsDerived,
            CanRestore = !operation.IsDerived,
            ProtectionReason = operation.IsDerived
                ? "Фондовое движение рассчитано из финансовой операции и не меняется отдельно."
                : null,
            CorrectionHint = operation.IsDerived
                ? "Исправьте исходное поступление или выплату."
                : "Изменение доступно в разделе «Управление фондами»."
        }).ToArray();
        return new FinancialJournalSegment(count, rows);
    }

    private IQueryable<FinancialJournalRow> BuildCashBankTransfers() =>
        dbContext.CashBankTransfers.AsNoTracking().Select(transfer => new FinancialJournalRow
        {
            Id = transfer.Id,
            EntityType = "cash_bank_transfer",
            OperationType = "cash_to_bank",
            OperationDate = transfer.TransferDate,
            AccountingMonth = null,
            Amount = transfer.Amount,
            Counterparty = "Касса → банк",
            Category = "Перевод между счетами",
            DocumentNumber = null,
            Comment = transfer.Comment,
            Source = "manual",
            IsCanceled = transfer.IsCanceled,
            CreatedAtUtc = transfer.CreatedAtUtc,
            Version = null,
            CanEdit = false,
            CanCancel = false,
            CanRestore = false,
            ProtectionReason = "Перевод влияет одновременно на кассу и банк и защищён от прямой правки.",
            CorrectionHint = "Создайте обратную корректировку с документированным основанием."
        });

    private IQueryable<FinancialJournalRow> BuildCashBankBalanceOperations() =>
        dbContext.CashBankBalanceOperations.AsNoTracking().Select(operation => new FinancialJournalRow
        {
            Id = operation.Id,
            EntityType = "cash_bank_balance_operation",
            OperationType = operation.OperationKind + "_" + operation.Direction,
            OperationDate = operation.OperationDate,
            AccountingMonth = null,
            Amount = operation.Amount,
            Counterparty = operation.Account == CashBankAccounts.Cash ? "Касса" : "Банк",
            Category = operation.OperationKind == CashBankBalanceOperationKinds.OpeningBalance ? "Начальный остаток" : "Корректировка остатка",
            DocumentNumber = null,
            Comment = operation.Reason,
            Source = "protected_adjustment",
            IsCanceled = false,
            CreatedAtUtc = operation.CreatedAtUtc,
            Version = null,
            CanEdit = false,
            CanCancel = false,
            CanRestore = false,
            ProtectionReason = "Стартовая корректировка является неизменяемой записью бухгалтерского следа.",
            CorrectionHint = "Добавьте новую компенсирующую корректировку с основанием."
        });

    private static FinancialJournalEntryDto ToDto(FinancialJournalRow row) => new(
        row.Id,
        row.EntityType,
        row.OperationType,
        row.OperationDate,
        row.AccountingMonth,
        row.Amount,
        row.Counterparty,
        row.Category,
        row.DocumentNumber,
        row.Comment,
        row.Source,
        row.IsCanceled,
        row.CreatedAtUtc,
        row.Version,
        row.CanEdit,
        row.CanCancel,
        row.CanRestore,
        row.ProtectionReason,
        row.CorrectionHint);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private sealed class FinancialJournalRow
    {
        public Guid Id { get; init; }
        public string EntityType { get; init; } = string.Empty;
        public string OperationType { get; init; } = string.Empty;
        public DateOnly OperationDate { get; init; }
        public DateOnly? AccountingMonth { get; init; }
        public decimal Amount { get; init; }
        public string Counterparty { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string? DocumentNumber { get; init; }
        public string? Comment { get; init; }
        public string Source { get; init; } = string.Empty;
        public bool IsCanceled { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public Guid? Version { get; init; }
        public bool CanEdit { get; init; }
        public bool CanCancel { get; init; }
        public bool CanRestore { get; init; }
        public string? ProtectionReason { get; init; }
        public string? CorrectionHint { get; init; }
    }

    private sealed record FinancialJournalSegment(int TotalCount, IReadOnlyList<FinancialJournalRow> Rows);

    private sealed record FundJournalData(
        Guid Id,
        string OperationKind,
        decimal Amount,
        string Counterparty,
        string FundName,
        string? DocumentNumber,
        string Reason,
        bool IsDerived,
        bool IsCanceled,
        DateTimeOffset CreatedAtUtc);
}
