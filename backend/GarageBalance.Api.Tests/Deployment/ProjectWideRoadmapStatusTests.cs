namespace GarageBalance.Api.Tests.Deployment;

public sealed class ProjectWideRoadmapStatusTests
{
    [Fact]
    public void UsersObjectCoverageIsMarkedCompleteWhenAuthAndUserAuditFlowsAreCovered()
    {
        var usersLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Пользователи: создание", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Пользователи:", usersLine, StringComparison.Ordinal);
        Assert.Contains("отключение активного пользователя требует причину", usersLine, StringComparison.Ordinal);
        Assert.Contains("отключенного пользователя можно восстановить", usersLine, StringComparison.Ordinal);
        Assert.Contains("повторное сохранение без изменения", usersLine, StringComparison.Ordinal);
        Assert.Contains("auth.login_failed", usersLine, StringComparison.Ordinal);
        Assert.Contains("auth.login_inactive", usersLine, StringComparison.Ordinal);
        Assert.Contains("auth.login_rate_limited", usersLine, StringComparison.Ordinal);
        Assert.Contains("429", usersLine, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnersObjectCoverageIsMarkedCompleteWhenCreateUpdateArchiveRestoreFlowsAreCovered()
    {
        var ownersLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Владельцы: создание", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Владельцы:", ownersLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.owner_created", ownersLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.owner_updated", ownersLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.owner_archived", ownersLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.owner_restored", ownersLine, StringComparison.Ordinal);
        Assert.Contains("архивирование требует причину", ownersLine, StringComparison.Ordinal);
        Assert.Contains("no-op не создает событие", ownersLine, StringComparison.Ordinal);
    }

    [Fact]
    public void GaragesObjectCoverageIsMarkedCompleteWhenCreateUpdateArchiveRestoreFlowsAreCovered()
    {
        var garagesLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Гаражи: создание", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Гаражи:", garagesLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.garage_created", garagesLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.garage_updated", garagesLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.garage_archived", garagesLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.garage_restored", garagesLine, StringComparison.Ordinal);
        Assert.Contains("архивирование требует причину", garagesLine, StringComparison.Ordinal);
        Assert.Contains("конфликте активного номера", garagesLine, StringComparison.Ordinal);
        Assert.Contains("duplicate number", garagesLine, StringComparison.Ordinal);
    }

    [Fact]
    public void SuppliersObjectCoverageIsMarkedCompleteWhenAuditRestoreAndDuplicateFlowsAreCovered()
    {
        var suppliersLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Поставщики: создание", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Поставщики:", suppliersLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.supplier_created", suppliersLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.supplier_updated", suppliersLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.supplier_archived", suppliersLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.supplier_restored", suppliersLine, StringComparison.Ordinal);
        Assert.Contains("архивирование требует причину", suppliersLine, StringComparison.Ordinal);
        Assert.Contains("активную группу", suppliersLine, StringComparison.Ordinal);
        Assert.Contains("активный дубль", suppliersLine, StringComparison.Ordinal);
        Assert.Contains("восстановление через контакт", suppliersLine, StringComparison.Ordinal);
        Assert.Contains("duplicate supplier", suppliersLine, StringComparison.Ordinal);
    }

    [Fact]
    public void TariffsAndServicesObjectCoverageIsMarkedCompleteWhenAuditArchiveRestoreFlowsAreCovered()
    {
        var tariffsLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Тарифы и услуги: создание", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Тарифы и услуги:", tariffsLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.tariff_created", tariffsLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.tariff_updated", tariffsLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.tariff_archived", tariffsLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.tariff_restored", tariffsLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.charge_service_created", tariffsLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.charge_service_updated", tariffsLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.charge_service_archived", tariffsLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.charge_service_restored", tariffsLine, StringComparison.Ordinal);
        Assert.Contains("архивирование требует причину", tariffsLine, StringComparison.Ordinal);
        Assert.Contains("restore блокирует активный дубль", tariffsLine, StringComparison.Ordinal);
        Assert.Contains("duplicate tariff/charge service", tariffsLine, StringComparison.Ordinal);
    }

    [Fact]
    public void MeterReadingsObjectCoverageIsMarkedCompleteWhenAuditCancelRestoreAndWorkflowFlowsAreCovered()
    {
        var meterReadingsLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Показания счетчиков: ввод", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Показания счетчиков:", meterReadingsLine, StringComparison.Ordinal);
        Assert.Contains("finance.meter_reading_created", meterReadingsLine, StringComparison.Ordinal);
        Assert.Contains("finance.meter_reading_updated", meterReadingsLine, StringComparison.Ordinal);
        Assert.Contains("finance.meter_reading_canceled", meterReadingsLine, StringComparison.Ordinal);
        Assert.Contains("finance.meter_reading_restored", meterReadingsLine, StringComparison.Ordinal);
        Assert.Contains("отмена требует причину", meterReadingsLine, StringComparison.Ordinal);
        Assert.Contains("проверяет активный дубль", meterReadingsLine, StringComparison.Ordinal);
        Assert.Contains("duplicate reading", meterReadingsLine, StringComparison.Ordinal);
        Assert.Contains("decreased value", meterReadingsLine, StringComparison.Ordinal);
        Assert.Contains("missing readings", meterReadingsLine, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnerPaymentsObjectCoverageIsMarkedCompleteWhenIncomeDebtRestoreAndFullPaymentFlowsAreCovered()
    {
        var ownerPaymentsLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Платежи владельцев: создание", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Платежи владельцев:", ownerPaymentsLine, StringComparison.Ordinal);
        Assert.Contains("finance.income_created", ownerPaymentsLine, StringComparison.Ordinal);
        Assert.Contains("finance.income_updated", ownerPaymentsLine, StringComparison.Ordinal);
        Assert.Contains("finance.operation_canceled", ownerPaymentsLine, StringComparison.Ordinal);
        Assert.Contains("finance.operation_restored", ownerPaymentsLine, StringComparison.Ordinal);
        Assert.Contains("CreateGarageDebtPayment", ownerPaymentsLine, StringComparison.Ordinal);
        Assert.Contains("finance.debt_transfer_created", ownerPaymentsLine, StringComparison.Ordinal);
        Assert.Contains("finance.debt_transfer_updated", ownerPaymentsLine, StringComparison.Ordinal);
        Assert.Contains("Полная оплата", ownerPaymentsLine, StringComparison.Ordinal);
        Assert.Contains("GetGarageIncomeWorksheet", ownerPaymentsLine, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierAndStaffPayoutsObjectCoverageIsMarkedCompleteWhenExpenseRestoreAndLimitFlowsAreCovered()
    {
        var payoutsLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Выплаты поставщикам и персоналу: создание", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Выплаты поставщикам и персоналу:", payoutsLine, StringComparison.Ordinal);
        Assert.Contains("CreateExpenseAsync", payoutsLine, StringComparison.Ordinal);
        Assert.Contains("CreateStaffPaymentAsync", payoutsLine, StringComparison.Ordinal);
        Assert.Contains("finance.expense_created", payoutsLine, StringComparison.Ordinal);
        Assert.Contains("finance.expense_updated", payoutsLine, StringComparison.Ordinal);
        Assert.Contains("finance.operation_canceled", payoutsLine, StringComparison.Ordinal);
        Assert.Contains("finance.operation_restored", payoutsLine, StringComparison.Ordinal);
        Assert.Contains("остаток банка/кассы", payoutsLine, StringComparison.Ordinal);
        Assert.Contains("доступный лимит", payoutsLine, StringComparison.Ordinal);
        Assert.Contains("server-side expense worksheet", payoutsLine, StringComparison.Ordinal);
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
