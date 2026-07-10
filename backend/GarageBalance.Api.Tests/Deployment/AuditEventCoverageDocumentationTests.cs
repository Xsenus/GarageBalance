namespace GarageBalance.Api.Tests.Deployment;

public sealed class AuditEventCoverageDocumentationTests
{
    public static TheoryData<string> DocumentedAuditActions()
    {
        return new TheoryData<string>
        {
            "auth.bootstrap_admin_created",
            "auth.login_success",
            "auth.login_failed",
            "auth.login_rate_limited",
            "auth.login_inactive",
            "auth.password_change_failed",
            "auth.password_changed",
            "users.user_created",
            "users.user_updated",
            "users.user_restored",
            "dictionary.owner_created",
            "dictionary.owner_updated",
            "dictionary.owner_archived",
            "dictionary.owner_restored",
            "dictionary.garage_created",
            "dictionary.garage_updated",
            "dictionary.garage_archived",
            "dictionary.garage_restored",
            "dictionary.supplier_group_created",
            "dictionary.supplier_group_updated",
            "dictionary.supplier_group_archived",
            "dictionary.supplier_group_restored",
            "dictionary.supplier_created",
            "dictionary.supplier_updated",
            "dictionary.supplier_archived",
            "dictionary.supplier_restored",
            "dictionary.income_type_created",
            "dictionary.income_type_updated",
            "dictionary.income_type_archived",
            "dictionary.income_type_restored",
            "dictionary.expense_type_created",
            "dictionary.expense_type_updated",
            "dictionary.expense_type_archived",
            "dictionary.expense_type_restored",
            "dictionary.tariff_created",
            "dictionary.tariff_updated",
            "dictionary.tariff_archived",
            "dictionary.tariff_restored",
            "dictionary.charge_service_created",
            "dictionary.charge_service_updated",
            "dictionary.charge_service_archived",
            "dictionary.charge_service_restored",
            "finance.income_created",
            "finance.income_updated",
            "finance.expense_created",
            "finance.expense_updated",
            "finance.operation_canceled",
            "finance.operation_restored",
            "finance.accrual_created",
            "finance.accrual_updated",
            "finance.accrual_canceled",
            "finance.accrual_restored",
            "finance.debt_transfer_created",
            "finance.debt_transfer_updated",
            "finance.supplier_accrual_created",
            "finance.supplier_accrual_updated",
            "finance.supplier_accrual_canceled",
            "finance.supplier_accrual_restored",
            "finance.regular_accruals_generated",
            "finance.fee_campaign_accruals_generated",
            "finance.supplier_group_salary_accruals_generated",
            "finance.meter_reading_created",
            "finance.meter_reading_updated",
            "finance.meter_reading_canceled",
            "finance.meter_reading_restored",
            "fund.operation_deposited",
            "fund.operation_withdrawn",
            "fund.operation_updated",
            "fund.operation_canceled",
            "fund.operation_restored",
            "import.access_dry_run",
            "import.access_dry_run_report_exported",
            "import.apply_requested",
            "import.apply_request_cancelled",
            "import.rollback_requested",
            "import.quarantine_registered",
            "import.quarantine_resolved",
            "import.row_fingerprint_registered",
            "integration.secret_upserted",
            "one_c_fresh.sync_requested",
            "one_c_fresh.sync_retry_requested",
            "receipt.print_requested",
            "receipt.print_canceled",
            "receipt.reprint_requested",
            "reports.consolidated_generated",
            "reports.income_generated",
            "reports.expense_generated",
            "reports.cash_payments_generated",
            "reports.bank_deposits_generated",
            "reports.consolidated_exported",
            "reports.income_exported",
            "reports.expense_exported",
            "reports.cash_payments_exported",
            "reports.bank_deposits_exported"
        };
    }

    [Fact]
    public void AuditCoverageDocumentContainsRequiredInventoryColumns()
    {
        var document = ReadAuditCoverageDocument();

        Assert.Contains("# Покрытие Backend Истории Изменений", document, StringComparison.Ordinal);
        Assert.Contains("| Action | Объект | Было/стало | Причина | Actor | Тесты | Примечание |", document, StringComparison.Ordinal);
        Assert.Contains("## Что Остается", document, StringComparison.Ordinal);
        Assert.Contains("backend/GarageBalance.Api.Tests/**", document, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectWideRoadmapReferencesCompletedBackendAuditInventory()
    {
        var roadmap = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"));

        Assert.Contains(
            "- `[x]` Найти все backend audit-события и составить таблицу",
            roadmap,
            StringComparison.Ordinal);
        Assert.Contains("docs/audit-event-coverage.md", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "- `[ ]` Найти все backend audit-события и составить таблицу",
            roadmap,
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DocumentedAuditActions))]
    public void AuditCoverageDocumentListsCurrentBackendAuditAction(string action)
    {
        var document = ReadAuditCoverageDocument();

        Assert.Contains($"`{action}`", document, StringComparison.Ordinal);
    }

    private static string ReadAuditCoverageDocument()
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "audit-event-coverage.md"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
