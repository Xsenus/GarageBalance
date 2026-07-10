namespace GarageBalance.Api.Tests.Deployment;

public sealed class FormWorkflowCoverageDocumentationTests
{
    public static TheoryData<string> RequiredWorkflowCategories()
    {
        return new TheoryData<string>
        {
            "create",
            "edit",
            "delete/archive",
            "restore",
            "cancel",
            "import",
            "export",
            "generated"
        };
    }

    public static TheoryData<string> RequiredBackendFlows()
    {
        return new TheoryData<string>
        {
            "BootstrapAdmin",
            "ChangeOwnPassword",
            "CreateUser",
            "UpdateUser",
            "RestoreUser",
            "CreateOwner",
            "UpdateOwner",
            "ArchiveOwner",
            "RestoreOwner",
            "CreateGarage",
            "UpdateGarage",
            "ArchiveGarage",
            "RestoreGarage",
            "CreateSupplier",
            "UpdateSupplier",
            "ArchiveSupplier",
            "RestoreSupplier",
            "CreateTariff",
            "UpdateTariff",
            "ArchiveTariff",
            "RestoreTariff",
            "CreateIncome",
            "CreateExpense",
            "CreateStaffPayment",
            "CreateGarageDebtPayment",
            "UpdateOperation",
            "CancelOperation",
            "RestoreOperation",
            "CreateAccrual",
            "UpdateAccrual",
            "CancelAccrual",
            "RestoreAccrual",
            "GenerateRegularAccruals",
            "GenerateRegularCatalogAccruals",
            "CreateSupplierAccrual",
            "UpdateSupplierAccrual",
            "CancelSupplierAccrual",
            "RestoreSupplierAccrual",
            "GenerateSupplierGroupSalaryAccruals",
            "CreateMeterReading",
            "UpdateMeterReading",
            "CancelMeterReading",
            "RestoreMeterReading",
            "CreateFundOperation",
            "UpdateFundOperation",
            "CancelFundOperation",
            "RestoreFundOperation",
            "FundsClient.createOperation",
            "DryRunAccessImport",
            "ResolveQuarantineItem",
            "ExportAccessImportRunReport",
            "ExportEventsXlsx"
        };
    }

    public static TheoryData<string> RequiredFrontendFlows()
    {
        return new TheoryData<string>
        {
            "Пользователи",
            "Справочники",
            "Платежи/финансы",
            "сдача кассы в банк",
            "Платежи-прототип",
            "Управление фондами",
            "Импорт Access",
            "Отчеты",
            "История изменений",
            "Тарифы-прототип",
            "Контрагенты-прототип",
            "Показания-прототип",
            "confirmation",
            "no-op",
            "React-state прототип"
        };
    }

    [Fact]
    public void FormWorkflowCoverageDocumentContainsRequiredSections()
    {
        var document = ReadFormCoverageDocument();

        Assert.Contains("# Покрытие Форм И Действий Сохранения", document, StringComparison.Ordinal);
        Assert.Contains("## Backend API Flows", document, StringComparison.Ordinal);
        Assert.Contains("## Frontend Формы И Диалоги", document, StringComparison.Ordinal);
        Assert.Contains("## Что Уже Закреплено Тестами", document, StringComparison.Ordinal);
        Assert.Contains("## Открытые Хвосты", document, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RequiredWorkflowCategories))]
    public void FormWorkflowCoverageDocumentListsWorkflowCategory(string category)
    {
        var document = ReadFormCoverageDocument();

        Assert.Contains(category, document, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RequiredBackendFlows))]
    public void FormWorkflowCoverageDocumentListsBackendFlow(string flow)
    {
        var document = ReadFormCoverageDocument();

        Assert.Contains(flow, document, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RequiredFrontendFlows))]
    public void FormWorkflowCoverageDocumentListsFrontendFlow(string flow)
    {
        var document = ReadFormCoverageDocument();

        Assert.Contains(flow, document, StringComparison.Ordinal);
    }

    private static string ReadFormCoverageDocument()
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "form-workflow-coverage.md"));
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
