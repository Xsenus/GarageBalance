namespace GarageBalance.Api.Tests.Deployment;

public sealed class MonthlyCycleChecklistTests
{
    [Fact]
    public void MonthlyCycleChecklistCoversRequiredOperationalSteps()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "monthly-cycle-checklist.md"));
        var userGuide = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "user-guide.md"));
        var adminGuide = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "admin-operations-guide.md"));
        var reportPanel = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "frontend", "src", "features", "reports", "ReportPanel.tsx"));
        var allocationTests = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "backend", "GarageBalance.Api.Tests", "Finance", "AccrualPaymentAllocatorTests.cs"));
        var expenseWorksheetTests = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "backend", "GarageBalance.Api.Tests", "Finance", "PostgreSqlExpenseWorksheetIntegrationTests.cs"));
        var financeService = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        var roadmap = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "forms-compliance-fixes-roadmap.md"));

        Assert.Contains("backup-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("restore-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("garagebalance_restore_check", document, StringComparison.Ordinal);
        Assert.Contains("dictionaries.read", document, StringComparison.Ordinal);
        Assert.Contains("tariffs.manage", document, StringComparison.Ordinal);
        Assert.Contains("payments.write", document, StringComparison.Ordinal);
        Assert.Contains("reports.read", document, StringComparison.Ordinal);
        Assert.Contains("Справочники", document, StringComparison.Ordinal);
        Assert.Contains("Тарифы", document, StringComparison.Ordinal);
        Assert.Contains("Показания", document, StringComparison.Ordinal);
        Assert.Contains("Регулярные начисления", document, StringComparison.Ordinal);
        Assert.Contains("meter_water", document, StringComparison.Ordinal);
        Assert.Contains("meter_electricity", document, StringComparison.Ordinal);
        Assert.Contains("all", document, StringComparison.Ordinal);
        Assert.Contains("accruals", document, StringComparison.Ordinal);
        Assert.Contains("payments", document, StringComparison.Ordinal);
        Assert.Contains("Скачать XLSX и PDF из каждой используемой вкладки", document, StringComparison.Ordinal);
        Assert.Contains("История изменений", document, StringComparison.Ordinal);
        Assert.Contains("Что нового", document, StringComparison.Ordinal);
        Assert.Contains("расширенная приемка до одного месяца", document, StringComparison.Ordinal);
        Assert.Contains("Нельзя переносить данные вручную из Access", document, StringComparison.Ordinal);
        Assert.Contains("закрытие месяца — это проверяемая сверка, а не отдельная операция", document, StringComparison.Ordinal);
        Assert.Contains("старейшие непогашенные начисления того же гаража и вида поступления", document, StringComparison.Ordinal);
        Assert.Contains("частичная оплата не закрывает обязательство", document, StringComparison.Ordinal);
        Assert.Contains("`Прочие оплаты` (`other_payments`)", document, StringComparison.Ordinal);
        Assert.Contains("`Прочие доходы` (`other_income`)", document, StringComparison.Ordinal);
        Assert.Contains("той же пары `поставщик + вид выплаты`", document, StringComparison.Ordinal);
        Assert.Contains("восемь вкладок", document, StringComparison.Ordinal);
        Assert.Contains("`Изменение фондов`", document, StringComparison.Ordinal);

        Assert.Contains("## 11. Закрытие месяца", userGuide, StringComparison.Ordinal);
        Assert.Contains("Оно не блокирует период", userGuide, StringComparison.Ordinal);
        Assert.Contains("все восемь вкладок отчетов", userGuide, StringComparison.Ordinal);
        Assert.Contains("## 9. Контроль закрытия месяца", adminGuide, StringComparison.Ordinal);
        Assert.Contains("не подтверждает бухгалтерские суммы", adminGuide, StringComparison.Ordinal);
        Assert.Contains("Не создавать технический статус или SQL-блокировку", adminGuide, StringComparison.Ordinal);

        foreach (var reportTab in new[] { "Консолидированный", "По гаражам", "По выплатам", "Поступления", "Оплаты из кассы", "Сдача кассы в банк", "Сборы", "Изменение фондов" })
        {
            Assert.Contains($"label: '{reportTab}'", reportPanel, StringComparison.Ordinal);
            Assert.Contains($"`{reportTab}`", document, StringComparison.Ordinal);
        }

        Assert.Contains("Allocate_UsesOldestDueDateAndLeavesRemainderAsOverpayment", allocationTests, StringComparison.Ordinal);
        Assert.Contains("Allocate_PartialAnnualPaymentDoesNotCloseAccrual", allocationTests, StringComparison.Ordinal);
        Assert.Contains("SupplierThreeMonthScenario_KeepsAdvanceWithinExpenseTypeAndRecalculatesAfterCancellation", expenseWorksheetTests, StringComparison.Ordinal);
        Assert.Contains("OtherPaymentsIncomeTypeCode = \"other_payments\"", financeService, StringComparison.Ordinal);
        Assert.Contains("OtherIncomeIncomeTypeCode = \"other_income\"", financeService, StringComparison.Ordinal);
        Assert.Contains("- [x] Обновить `docs/monthly-cycle-checklist.md` и пользовательские инструкции", roadmap, StringComparison.Ordinal);
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
