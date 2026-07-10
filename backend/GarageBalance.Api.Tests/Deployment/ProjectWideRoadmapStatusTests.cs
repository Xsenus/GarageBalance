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
    public void CurrentSafeRestoreButtonsAreMarkedCompleteWhenBackendUiCoverageIsDocumented()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var restoreButtonsLine = activeRoadmapLines
            .Single(line => line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
                line.Contains("Для каждого текущего soft-deleted/canceled объекта", StringComparison.Ordinal));
        var definitionOfDoneLine = activeRoadmapLines
            .SkipWhile(line => !string.Equals(line, "## Definition Of Done", StringComparison.Ordinal))
            .Single(line => line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
                line.Contains("soft-deleted", StringComparison.Ordinal) &&
                line.Contains("restore", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]`", restoreButtonsLine, StringComparison.Ordinal);
        Assert.Contains("пользователи", restoreButtonsLine, StringComparison.Ordinal);
        Assert.Contains("контакты поставщиков", restoreButtonsLine, StringComparison.Ordinal);
        Assert.Contains("нерегулярные платежи", restoreButtonsLine, StringComparison.Ordinal);
        Assert.Contains("операции фондов", restoreButtonsLine, StringComparison.Ordinal);
        Assert.Contains("Backend restore endpoints/services", restoreButtonsLine, StringComparison.Ordinal);
        Assert.Contains("frontend restore dialogs/buttons", restoreButtonsLine, StringComparison.Ordinal);
        Assert.Contains("SoftDeleteCancelCoverageDocumentationTests", restoreButtonsLine, StringComparison.Ordinal);
        Assert.Contains("FormWorkflowCoverageDocumentationTests", restoreButtonsLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", restoreButtonsLine, StringComparison.Ordinal);
        Assert.Contains("future restore-формы", restoreButtonsLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[x]`", definitionOfDoneLine, StringComparison.Ordinal);
        Assert.Contains("пользователи", definitionOfDoneLine, StringComparison.Ordinal);
        Assert.Contains("контакты поставщиков", definitionOfDoneLine, StringComparison.Ordinal);
        Assert.Contains("нерегулярные платежи", definitionOfDoneLine, StringComparison.Ordinal);
        Assert.Contains("операции фондов", definitionOfDoneLine, StringComparison.Ordinal);
        Assert.Contains("Backend restore endpoints/services", definitionOfDoneLine, StringComparison.Ordinal);
        Assert.Contains("frontend restore dialogs/buttons", definitionOfDoneLine, StringComparison.Ordinal);
        Assert.Contains("SoftDeleteCancelCoverageDocumentationTests", definitionOfDoneLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", definitionOfDoneLine, StringComparison.Ordinal);
        Assert.Contains("Future restore-формы", definitionOfDoneLine, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentRestoreConflictChecksAreMarkedCompleteWhenBusinessRulesAreCovered()
    {
        var restoreConflictLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
                line.Contains("Восстановление проверяет конфликты уникальности", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]`", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("номер гаража", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("активную группу/поставщика/контакт", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("активный отдел сотрудника", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("название активного тарифа/услуги/нерегулярного платежа/сбора", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("активные финансовые документы", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("кассу/банк", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("лимит сотрудника", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("дубли начислений и показаний", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("DictionaryServiceTests", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("FinanceServiceTests", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("FundsControllerTests", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("SoftDeleteCancelCoverageDocumentationTests", restoreConflictLine, StringComparison.Ordinal);
        Assert.Contains("будущие restore-модели", restoreConflictLine, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateRequestDiffCoverageIsMarkedCompleteWhenCurrentServicesWriteOldAndNewValues()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var updateDiffLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Для update requests сохранять diff старого и нового состояния", StringComparison.Ordinal));

        Assert.Contains("владельцы", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("гаражи", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("поставщики", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("тарифы", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("пользователи/назначенные роли", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("права роли", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("отделы персонала", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("сотрудники", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("операции фондов", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("OldValues/NewValues", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.staff_department_updated", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.staff_member_updated", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("fund.operation_updated", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("users.role_permissions_updated", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("DictionaryServiceTests", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("FundServiceTests", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("UserManagementServiceTests", updateDiffLine, StringComparison.Ordinal);
        Assert.Contains("future update-модели", updateDiffLine, StringComparison.Ordinal);
    }

    [Fact]
    public void RolesAndPermissionsAuditContextIsMarkedCompleteWhenAssignmentsAndPermissionMatrixWriteOldAndNewSets()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var rolesAndPermissionsLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Для прав и ролей добавить старый и новый набор ролей/прав", StringComparison.Ordinal));

        Assert.Contains("назначенные роли пользователя", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("role codes", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("матрица прав роли", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("permissions", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("users.user_updated", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("users.role_permissions_updated", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("OldValues/NewValues", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("oldValue/newValue", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("roleCode", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("UpdateUserAsync_UpdatesUserAndWritesAudit", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("UpdateRolePermissionsAsync_UpdatesPermissionsAndWritesAudit", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("UpdateRolePermissionsAsync_DoesNotWriteAuditWhenPermissionsAreUnchanged", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("UpdateRolePermissions_ReturnsUpdatedRoleAndPassesActorUserId", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("ProjectWideRoadmapStatusTests", rolesAndPermissionsLine, StringComparison.Ordinal);
        Assert.Contains("future role/permission-модели", rolesAndPermissionsLine, StringComparison.Ordinal);
    }

    [Fact]
    public void NoOpUpdateCoverageIsMarkedCompleteWhenCurrentServicesSkipFalseAuditEvents()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var noOpUpdateLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Если update не меняет данные", StringComparison.Ordinal));

        Assert.Contains("контакты поставщиков", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("отделы персонала", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("сотрудники", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("нерегулярные платежи", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("сборы", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("операции фондов", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("права роли", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("release notes", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("CurrentExtendedUpdateMethods_DoNotWriteAuditWhenNormalizedValuesAreUnchanged", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("UpdateOperationAsync_DoesNotWriteAuditWhenNothingChanged", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("UpdateRolePermissionsAsync_DoesNotWriteAuditWhenPermissionsAreUnchanged", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("UpsertSecretAsync_DoesNotWriteAuditWhenSecretIsUnchanged", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("UpdateReleaseAsync_WritesReadableAuditDiffAndSkipsNoOpUpdate", noOpUpdateLine, StringComparison.Ordinal);
        Assert.Contains("future update-модели", noOpUpdateLine, StringComparison.Ordinal);
    }

    [Fact]
    public void FinancialAuditContextCoverageIsMarkedCompleteWhenCurrentFinanceAndFundEventsHaveMoneyAndRelatedContext()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var financialContextLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("финансовых update/cancel", StringComparison.Ordinal));

        Assert.Contains("FinanceService", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("поступления", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("выплаты", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("начисления владельцам", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("начисления поставщикам", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("показания счетчиков", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("relatedAccountingMonth", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("relatedGarageId/Number", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("relatedCounterpartyId/Name", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("relatedDocumentId/Number", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("FundService", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("операции фондов", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("fundId", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("fundName", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("operationKind", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("amount", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("isCanceled", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("FinanceServiceTests", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("FundServiceTests", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("ProjectWideRoadmapStatusTests", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("docs/audit-event-coverage.md", financialContextLine, StringComparison.Ordinal);
        Assert.Contains("future financial-модели", financialContextLine, StringComparison.Ordinal);
    }

    [Fact]
    public void TariffAndAccrualAuditContextIsMarkedCompleteWhenRatesPeriodsThresholdsAndAccrualFieldsAreCovered()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var tariffAndAccrualLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Для тарифов и начислений добавить старую и новую ставку/единицу/порог/период", StringComparison.Ordinal));

        Assert.Contains("dictionary.tariff_updated", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.charge_service_updated", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.fee_campaign_updated", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("finance.accrual_updated", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("finance.supplier_accrual_updated", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("finance.regular_accruals_generated", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("finance.regular_catalog_accruals_generated", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("finance.fee_campaign_accruals_generated", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("finance.supplier_group_salary_accruals_generated", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("rate", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("calculationBase", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("effectiveFrom", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("electricityTiers", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("accountingMonth", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("documentNumber", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("UpdateTariffAsync_UpdatesTariffAndWritesAudit", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("FeeCampaignAsync_CreatesUpdatesArchivesAndRestoresWithAudit", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("UpdateAccrualAsync_WritesBeforeAndAfterAuditForManualCorrection", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("UpdateSupplierAccrualAsync_WritesBeforeAndAfterAuditForManualCorrection", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("GenerateFeeCampaignAccrualsAsync_CreatesAccrualsForActiveGaragesAndWritesAudit", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("ProjectWideRoadmapStatusTests", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("docs/audit-event-coverage.md", tariffAndAccrualLine, StringComparison.Ordinal);
        Assert.Contains("future tariff/accrual-модели", tariffAndAccrualLine, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultDeletionRuleIsMarkedCompleteWhenSoftArchiveCancelAndPolicyCoverageExist()
    {
        var deletionRuleLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
                line.Contains("Удаление по умолчанию", StringComparison.Ordinal) &&
                line.Contains("soft delete / archive / cancel", StringComparison.Ordinal));

        Assert.Contains("IsArchived", deletionRuleLine, StringComparison.Ordinal);
        Assert.Contains("IsCanceled", deletionRuleLine, StringComparison.Ordinal);
        Assert.Contains("IsActive=false", deletionRuleLine, StringComparison.Ordinal);
        Assert.Contains("ResolvedAtUtc", deletionRuleLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.*_archived", deletionRuleLine, StringComparison.Ordinal);
        Assert.Contains("finance.*_canceled", deletionRuleLine, StringComparison.Ordinal);
        Assert.Contains("fund.operation_canceled", deletionRuleLine, StringComparison.Ordinal);
        Assert.Contains("docs/soft-delete-cancel-coverage.md", deletionRuleLine, StringComparison.Ordinal);
        Assert.Contains("SoftDeleteCancelCoverageDocumentationTests", deletionRuleLine, StringComparison.Ordinal);
        Assert.Contains("FormWorkflowCoverageDocumentationTests", deletionRuleLine, StringComparison.Ordinal);
        Assert.Contains("DatabaseMigrationPolicyTests.ProductionBackendCode_DoesNotPhysicallyDeleteDataOutsideMigrations", deletionRuleLine, StringComparison.Ordinal);
        Assert.Contains("ProjectWideRoadmapStatusTests", deletionRuleLine, StringComparison.Ordinal);
    }

    [Fact]
    public void DangerousActionsConfirmationRuleIsMarkedCompleteWhenFrontendBackendAndCoverageAgree()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var confirmationRuleLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Каждое опасное действие удаления/архивации/отмены", StringComparison.Ordinal));
        var definitionOfDoneLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Все delete/archive/cancel actions требуют confirmation", StringComparison.Ordinal));

        Assert.Contains("пользователи", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("справочники", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("платежи/выплаты/начисления/показания", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("фонды", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("импортные cancel/rollback-заявки", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("квитанциями", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("FrontendDialogPolicyTests", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("window.confirm", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("window.prompt", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("window.alert", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("ControllerThinnessTests", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("Archive*", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("Cancel*", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("Delete*", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("Reason", confirmationRuleLine, StringComparison.Ordinal);
        Assert.Contains("confirmation", definitionOfDoneLine, StringComparison.Ordinal);
    }

    [Fact]
    public void DangerousBackendReasonRulesAreMarkedCompleteWhenCurrentRequestsAreProtected()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var backendRuleLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("На backend не полагаться только на frontend confirmation", StringComparison.Ordinal));
        var reasonRuleLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Для delete/archive/cancel requests сделать обязательную причину", StringComparison.Ordinal));

        Assert.Contains("finance cancel endpoints", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("funds cancel endpoint", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("import rollback/apply/apply-cancel", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("cancel/reprint квитанций", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("backup confirmation", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("ControllerThinnessTests", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("[FromBody]", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("Reason", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("1000", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("FinanceControllerTests", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("FundServiceTests", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("ImportServiceTests", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("ReceiptPrintingServiceTests", backendRuleLine, StringComparison.Ordinal);
        Assert.Contains("Future dangerous endpoints", backendRuleLine, StringComparison.Ordinal);

        Assert.Contains("операций фондов", reasonRuleLine, StringComparison.Ordinal);
        Assert.Contains("контактов", reasonRuleLine, StringComparison.Ordinal);
        Assert.Contains("отделов", reasonRuleLine, StringComparison.Ordinal);
        Assert.Contains("сотрудников", reasonRuleLine, StringComparison.Ordinal);
        Assert.Contains("сборов", reasonRuleLine, StringComparison.Ordinal);
        Assert.Contains("import rollback/apply-cancel", reasonRuleLine, StringComparison.Ordinal);
        Assert.Contains("receipt cancel/reprint", reasonRuleLine, StringComparison.Ordinal);
        Assert.Contains("reason и backup confirmation", reasonRuleLine, StringComparison.Ordinal);
        Assert.Contains("future `Archive*`/`Cancel*`/`Delete*` endpoints", reasonRuleLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmationInventoryIsMarkedCompleteWhenCurrentFlowsAndDialogPolicyAreCovered()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var inventoryLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Найти все действия, где уже есть confirmation dialog", StringComparison.Ordinal));

        Assert.Contains("docs/form-workflow-coverage.md", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("create/edit/delete/archive/restore/cancel/import/export/generated", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("FormWorkflowCoverageDocumentationTests", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("FrontendDialogPolicyTests", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("ControllerThinnessTests", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("пользователей", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("справочников", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("контрагентов", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("тарифов/сборов", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("платежей", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("фондов", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("импорта", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("квитанций", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("window.confirm", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("window.alert", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("window.prompt", inventoryLine, StringComparison.Ordinal);
        Assert.Contains("workflow/a11y tests", inventoryLine, StringComparison.Ordinal);
    }

    [Fact]
    public void EditSaveConfirmationRulesAreMarkedCompleteWhenDiffAndNoOpCoverageExist()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var topRuleLines = activeRoadmapLines
            .TakeWhile(line => !string.Equals(line, "## Решения И Допущения", StringComparison.Ordinal))
            .ToArray();
        var definitionOfDoneLines = activeRoadmapLines
            .SkipWhile(line => !string.Equals(line, "## Definition Of Done", StringComparison.Ordinal))
            .ToArray();

        var diffRuleLine = topRuleLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Каждое сохранение формы с изменениями", StringComparison.Ordinal));
        var noOpRuleLine = topRuleLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Если изменений нет", StringComparison.Ordinal));
        var diffDefinitionOfDoneLine = definitionOfDoneLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Все edit-save actions с изменениями", StringComparison.Ordinal));
        var noOpDefinitionOfDoneLine = definitionOfDoneLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("No-op save/close", StringComparison.Ordinal));

        Assert.Contains("users/settings", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("dictionaries", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("contractors", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("tariffs/services/thresholds/fees", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("meter readings", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("finance operations/accruals/payments", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("funds", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("ChangePreview", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("changePreview.test.ts", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("FormWorkflowCoverageDocumentationTests", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("ProjectWideRoadmapStatusTests", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("future edit-save", diffRuleLine, StringComparison.Ordinal);
        Assert.Contains("no-op save", noOpRuleLine, StringComparison.Ordinal);
        Assert.Contains("без update-запроса", noOpRuleLine, StringComparison.Ordinal);
        Assert.Contains("changePreview.test.ts", noOpRuleLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", noOpRuleLine, StringComparison.Ordinal);
        Assert.Contains("FormWorkflowCoverageDocumentationTests", noOpRuleLine, StringComparison.Ordinal);
        Assert.Contains("ProjectWideRoadmapStatusTests", noOpRuleLine, StringComparison.Ordinal);
        Assert.Contains("confirmation", diffDefinitionOfDoneLine, StringComparison.Ordinal);
        Assert.Contains("confirmation", noOpDefinitionOfDoneLine, StringComparison.Ordinal);
    }

    [Fact]
    public void UnifiedUiControlStyleRuleIsMarkedCompleteWhenMilestoneAndCoverageAgree()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var topRuleLines = activeRoadmapLines
            .TakeWhile(line => !string.Equals(line, "## Решения И Допущения", StringComparison.Ordinal))
            .ToArray();
        var definitionOfDoneLines = activeRoadmapLines
            .SkipWhile(line => !string.Equals(line, "## Definition Of Done", StringComparison.Ordinal))
            .ToArray();

        var styleRuleLine = topRuleLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Все контролы проекта должны использовать единый стиль", StringComparison.Ordinal));
        var definitionOfDoneLine = definitionOfDoneLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Все controls проекта приведены к единому стилю", StringComparison.Ordinal));

        Assert.Contains("primary/secondary/ghost/icon/destructive/link buttons", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("date/month/year inputs", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("pagination/filter states", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("empty/loading/error states", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("sidebar/topbar/dashboard", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("palette", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("docs/ui-control-style-coverage.md", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("UiControlStyleCoverageDocumentationTests", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("accessibleStatus.test.ts", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("responsiveLayout.test.ts", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("focusHooks.test.tsx", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("ProjectWideRoadmapStatusTests", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("[acceptance]", styleRuleLine, StringComparison.Ordinal);
        Assert.Contains("единому стилю", definitionOfDoneLine, StringComparison.Ordinal);
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

    [Fact]
    public void OwnerAccrualsObjectCoverageIsMarkedCompleteWhenManualRegularMeterCancelAndRestoreFlowsAreCovered()
    {
        var ownerAccrualsLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Начисления владельцам: ручные", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Начисления владельцам:", ownerAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("CreateAccrualAsync", ownerAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("finance.accrual_created", ownerAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("finance.accrual_updated", ownerAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("finance.accrual_canceled", ownerAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("finance.accrual_restored", ownerAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("finance.regular_accruals_generated", ownerAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("meter_water", ownerAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("meter_electricity", ownerAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("GenerateRegularAccruals", ownerAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("GenerateRegularCatalogAccruals", ownerAccrualsLine, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierAndSalaryAccrualsObjectCoverageIsMarkedCompleteWhenManualSalaryCancelAndRestoreFlowsAreCovered()
    {
        var supplierAccrualsLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Начисления поставщикам и зарплаты: создание", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Начисления поставщикам и зарплаты:", supplierAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("CreateSupplierAccrualAsync", supplierAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("finance.supplier_accrual_created", supplierAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("finance.supplier_accrual_updated", supplierAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("finance.supplier_accrual_canceled", supplierAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("finance.supplier_accrual_restored", supplierAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("finance.supplier_group_salary_accruals_generated", supplierAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("salary_accruals_empty", supplierAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("GenerateSupplierGroupSalaryAccruals", supplierAccrualsLine, StringComparison.Ordinal);
    }

    [Fact]
    public void FundsObjectCoverageIsMarkedCompleteWhenFundOperationsAuditCancelRestoreAndLimitsAreCovered()
    {
        var fundsLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Фонды: пополнение", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Фонды:", fundsLine, StringComparison.Ordinal);
        Assert.Contains("FundService.CreateOperationAsync", fundsLine, StringComparison.Ordinal);
        Assert.Contains("fund.operation_deposited", fundsLine, StringComparison.Ordinal);
        Assert.Contains("fund.operation_withdrawn", fundsLine, StringComparison.Ordinal);
        Assert.Contains("fund.operation_updated", fundsLine, StringComparison.Ordinal);
        Assert.Contains("fund.operation_canceled", fundsLine, StringComparison.Ordinal);
        Assert.Contains("fund.operation_restored", fundsLine, StringComparison.Ordinal);
        Assert.Contains("обратная операция", fundsLine, StringComparison.Ordinal);
        Assert.Contains("Управление фондами", fundsLine, StringComparison.Ordinal);
        Assert.Contains("доступный лимит", fundsLine, StringComparison.Ordinal);
    }

    [Fact]
    public void CashAndBankObjectCoverageIsMarkedCompleteWhenBankDepositFundOperationsAndReportsAreCovered()
    {
        var cashAndBankLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Касса и банк: сдача", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Касса и банк:", cashAndBankLine, StringComparison.Ordinal);
        Assert.Contains("Учет суммы на счете в банке", cashAndBankLine, StringComparison.Ordinal);
        Assert.Contains("FundsClient.createOperation", cashAndBankLine, StringComparison.Ordinal);
        Assert.Contains("FundService.CreateOperationAsync", cashAndBankLine, StringComparison.Ordinal);
        Assert.Contains("fund.operation_updated", cashAndBankLine, StringComparison.Ordinal);
        Assert.Contains("fund.operation_canceled", cashAndBankLine, StringComparison.Ordinal);
        Assert.Contains("fund.operation_restored", cashAndBankLine, StringComparison.Ordinal);
        Assert.Contains("reports.cash_payments_generated", cashAndBankLine, StringComparison.Ordinal);
        Assert.Contains("reports.bank_deposits_generated", cashAndBankLine, StringComparison.Ordinal);
        Assert.Contains("reports.cash_payments_exported", cashAndBankLine, StringComparison.Ordinal);
        Assert.Contains("reports.bank_deposits_exported", cashAndBankLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeHistoryBackendStorageRuleIsMarkedCompleteWhenAuditEventsArePersistedInPostgreSql()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var storageLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("История должна храниться на backend и в PostgreSQL", StringComparison.Ordinal));
        var allActionsAuditRuleLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[ ]`", StringComparison.Ordinal) &&
            line.Contains("Любое создание, изменение, архивирование", StringComparison.Ordinal));

        Assert.Contains("AuditEvent", storageLine, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext.AuditEvents", storageLine, StringComparison.Ordinal);
        Assert.Contains("audit_events", storageLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventWriter", storageLine, StringComparison.Ordinal);
        Assert.Contains("AuditService", storageLine, StringComparison.Ordinal);
        Assert.Contains("AuditController", storageLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventDto", storageLine, StringComparison.Ordinal);
        Assert.Contains("InitialAuthSchema", storageLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventRelatedFields", storageLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventSectionActionKind", storageLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventLookupIndexes", storageLine, StringComparison.Ordinal);
        Assert.Contains("AuditServiceTests", storageLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventWriterTests", storageLine, StringComparison.Ordinal);
        Assert.Contains("AuditControllerTests", storageLine, StringComparison.Ordinal);
        Assert.Contains("DatabaseMigrationPolicyTests", storageLine, StringComparison.Ordinal);
        Assert.Contains("ProjectWideRoadmapStatusTests", storageLine, StringComparison.Ordinal);
        Assert.Contains("каждое действие во всех разделах", storageLine, StringComparison.Ordinal);
        Assert.Contains("открытым правилом", storageLine, StringComparison.Ordinal);
        Assert.Contains("историю изменения", allActionsAuditRuleLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeHistoryContractCoverageIsMarkedCompleteWhenStructuredFieldsAndIndexesAreCovered()
    {
        var changeHistoryLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
                line.Contains("ChangeHistoryEvent", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]`", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventDto", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("section", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("actionKind", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("fieldName", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("oldValue", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("newValue", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("reason", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("metadata", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("entityDisplayName", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("relatedGarageId", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("relatedDocumentNumber", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventRelatedFields", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventSectionActionKind", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventLookupIndexes", changeHistoryLine, StringComparison.Ordinal);
        Assert.Contains("DatabaseMigrationPolicyTests", changeHistoryLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeHistoryMinimumVisibleFieldsCoverageIsMarkedCompleteWhenDtoUiCsvAndStorageAreCovered()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var definitionOfDoneLines = activeRoadmapLines
            .SkipWhile(line => !string.Equals(line, "## Definition Of Done", StringComparison.Ordinal))
            .ToArray();

        var minimumFieldsLine = activeRoadmapLines.Single(line => line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
                line.Contains("История должна показывать минимум", StringComparison.Ordinal));
        var visibleHistoryDefinitionLine = definitionOfDoneLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("История показывает `кто`", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]`", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("createdAtUtc", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("actorUserId", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("section", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("entityType", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("entityId", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("entityDisplayName", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("actionKind", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("fieldName", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("oldValue/newValue", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("reason", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("metadata", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("relatedGarageNumber", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("relatedCounterpartyName", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("relatedAccountingMonth", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("relatedDocumentNumber", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventDto", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("AuditServiceTests", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("AuditControllerTests", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("DatabaseMigrationPolicyTests", minimumFieldsLine, StringComparison.Ordinal);
        Assert.Contains("actorUserId", visibleHistoryDefinitionLine, StringComparison.Ordinal);
        Assert.Contains("createdAtUtc", visibleHistoryDefinitionLine, StringComparison.Ordinal);
        Assert.Contains("entityDisplayName", visibleHistoryDefinitionLine, StringComparison.Ordinal);
        Assert.Contains("oldValue/newValue", visibleHistoryDefinitionLine, StringComparison.Ordinal);
        Assert.Contains("reason", visibleHistoryDefinitionLine, StringComparison.Ordinal);
        Assert.Contains("metadata", visibleHistoryDefinitionLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventDto", visibleHistoryDefinitionLine, StringComparison.Ordinal);
        Assert.Contains("CSV/XLSX export", visibleHistoryDefinitionLine, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL", visibleHistoryDefinitionLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeHistoryServiceTestCoverageIsMarkedCompleteWhenAuditServiceScenariosAreCovered()
    {
        var serviceTestsLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
                line.Contains("service tests: diff", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]`", serviceTestsLine, StringComparison.Ordinal);
        Assert.Contains("AuditServiceTests", serviceTestsLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventWriterTests", serviceTestsLine, StringComparison.Ordinal);
        Assert.Contains("AuditChangeDiffBuilderTests", serviceTestsLine, StringComparison.Ordinal);
        Assert.Contains("DatabaseMigrationPolicyTests", serviceTestsLine, StringComparison.Ordinal);
        Assert.Contains("ordering", serviceTestsLine, StringComparison.Ordinal);
        Assert.Contains("pagination", serviceTestsLine, StringComparison.Ordinal);
        Assert.Contains("masking", serviceTestsLine, StringComparison.Ordinal);
        Assert.Contains("oldValue/newValue", serviceTestsLine, StringComparison.Ordinal);
        Assert.Contains("metadata", serviceTestsLine, StringComparison.Ordinal);
        Assert.Contains("entityDisplayName", serviceTestsLine, StringComparison.Ordinal);
        Assert.Contains("CSV structured columns", serviceTestsLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeHistoryApplicationWriterAndReasonFormatAreMarkedCompleteWhenDirectAuditWritesAreForbidden()
    {
        const string historyHeader = "## \u0418\u0441\u0442\u043e\u0440\u0438\u044f \u0432\u044b\u043f\u043e\u043b\u043d\u0435\u043d\u0438\u044f";
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, historyHeader, StringComparison.Ordinal))
            .ToArray();

        var writerLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("future mutating services", StringComparison.Ordinal));
        var reasonLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("future manual audit-", StringComparison.Ordinal));

        Assert.Contains("IAuditEventWriter", writerLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventWriter", writerLine, StringComparison.Ordinal);
        Assert.Contains("auth", writerLine, StringComparison.Ordinal);
        Assert.Contains("users", writerLine, StringComparison.Ordinal);
        Assert.Contains("dictionaries", writerLine, StringComparison.Ordinal);
        Assert.Contains("finance", writerLine, StringComparison.Ordinal);
        Assert.Contains("funds", writerLine, StringComparison.Ordinal);
        Assert.Contains("import", writerLine, StringComparison.Ordinal);
        Assert.Contains("integrations", writerLine, StringComparison.Ordinal);
        Assert.Contains("receipt printing", writerLine, StringComparison.Ordinal);
        Assert.Contains("reports", writerLine, StringComparison.Ordinal);
        Assert.Contains("form states", writerLine, StringComparison.Ordinal);
        Assert.Contains("app releases", writerLine, StringComparison.Ordinal);
        Assert.Contains("AuditEvents.Add", writerLine, StringComparison.Ordinal);
        Assert.Contains("future mutating services", writerLine, StringComparison.Ordinal);

        Assert.Contains("AuditEventWriter", reasonLine, StringComparison.Ordinal);
        Assert.Contains("archive/delete/cancel", reasonLine, StringComparison.Ordinal);
        Assert.Contains("Reason", reasonLine, StringComparison.Ordinal);
        Assert.Contains("\u041f\u0440\u0438\u0447\u0438\u043d\u0430", reasonLine, StringComparison.Ordinal);
        Assert.Contains("\u041a\u043e\u043c\u043c\u0435\u043d\u0442\u0430\u0440\u0438\u0439", reasonLine, StringComparison.Ordinal);
        Assert.Contains("AuditEventWriterTests", reasonLine, StringComparison.Ordinal);
        Assert.Contains("ControllerThinnessTests", reasonLine, StringComparison.Ordinal);
        Assert.Contains("ProductionBackendCode_CreatesAuditEventsOnlyThroughAuditEventWriter", reasonLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeHistoryReactCoverageIsMarkedCompleteWhenAuditPanelHasErrorAndRetryTests()
    {
        const string historyHeader = "## \u0418\u0441\u0442\u043e\u0440\u0438\u044f \u0432\u044b\u043f\u043e\u043b\u043d\u0435\u043d\u0438\u044f";
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, historyHeader, StringComparison.Ordinal))
            .ToArray();

        var reactCoverageLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("React tests", StringComparison.Ordinal) &&
            line.Contains("retry загрузки журнала", StringComparison.Ordinal));

        Assert.Contains("App.test.tsx", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("audit.read", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("CSV/XLSX", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("detail view", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("deep links", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("pagination", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("empty state", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("validation", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("retry загрузки карточки события", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("retry ошибок CSV/XLSX export", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains(nameof(ChangeHistoryReactCoverageIsMarkedCompleteWhenAuditPanelHasErrorAndRetryTests), reactCoverageLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeHistoryObjectQuickLinksAreMarkedCompleteWhenContractorCardsOpenFromAuditDetails()
    {
        const string historyHeader = "## \u0418\u0441\u0442\u043e\u0440\u0438\u044f \u0432\u044b\u043f\u043e\u043b\u043d\u0435\u043d\u0438\u044f";
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, historyHeader, StringComparison.Ordinal))
            .ToArray();

        var quickLinksLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("ContractorOpenTarget", StringComparison.Ordinal));

        Assert.Contains("owner", quickLinksLine, StringComparison.Ordinal);
        Assert.Contains("garage", quickLinksLine, StringComparison.Ordinal);
        Assert.Contains("supplier", quickLinksLine, StringComparison.Ordinal);
        Assert.Contains("staff_member", quickLinksLine, StringComparison.Ordinal);
        Assert.Contains("dictionaries.read", quickLinksLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", quickLinksLine, StringComparison.Ordinal);
        Assert.Contains("future deep links", quickLinksLine, StringComparison.Ordinal);
        Assert.Contains(nameof(ChangeHistoryObjectQuickLinksAreMarkedCompleteWhenContractorCardsOpenFromAuditDetails), File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmationDeleteCloseFrontendCoverageIsMarkedCompleteWhenCurrentFlowsAreCovered()
    {
        const string historyHeader = "## \u0418\u0441\u0442\u043e\u0440\u0438\u044f \u0432\u044b\u043f\u043e\u043b\u043d\u0435\u043d\u0438\u044f";
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, historyHeader, StringComparison.Ordinal))
            .ToArray();

        var confirmationCoverageLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Frontend tests", StringComparison.Ordinal) &&
            line.Contains("confirmation delete/close", StringComparison.Ordinal));

        Assert.Contains("App.test.tsx", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("FrontendDialogPolicyTests", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("FormWorkflowCoverageDocumentationTests", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("ControllerThinnessTests", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("window.confirm", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("window.prompt", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("window.alert", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("payments", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("users", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("dictionary", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("contractors", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("tariffs/services/fees", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("Access import", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("1C Fresh", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("receipt", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains("future", confirmationCoverageLine, StringComparison.Ordinal);
        Assert.Contains(nameof(ConfirmationDeleteCloseFrontendCoverageIsMarkedCompleteWhenCurrentFlowsAreCovered), confirmationCoverageLine, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendDialogAccessibilityCoverageIsMarkedCompleteWhenCurrentDialogFamiliesAreCovered()
    {
        const string historyHeader = "## \u0418\u0441\u0442\u043e\u0440\u0438\u044f \u0432\u044b\u043f\u043e\u043b\u043d\u0435\u043d\u0438\u044f";
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, historyHeader, StringComparison.Ordinal))
            .ToArray();

        var accessibilityLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains("Frontend accessibility tests for dialogs and keyboard", StringComparison.Ordinal));

        Assert.Contains("App.test.tsx", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("focus-loop", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("initial focus", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("Escape", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("restore focus", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("useFocusTrap", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("useEscapeKey", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("useFocusOnOpen", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("useRestoreFocusOnClose", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("docs/ui-control-style-coverage.md", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("FormWorkflowCoverageDocumentationTests", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("FrontendDialogPolicyTests", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("Access import", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("1C Fresh", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("receipt print/cancel/reprint", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("audit event detail", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("validation dialogs", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains("future dialogs/control families", accessibilityLine, StringComparison.Ordinal);
        Assert.Contains(nameof(FrontendDialogAccessibilityCoverageIsMarkedCompleteWhenCurrentDialogFamiliesAreCovered), accessibilityLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeHistoryControllerTestCoverageIsMarkedCompleteWhenAuditControllerContractIsCovered()
    {
        var controllerTestsLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
                line.Contains("controller tests: success", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]`", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("AuditController", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("AuditControllerTests", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("AuditServiceTests", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("ControllerAuthorizationCoverageTests", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("ApiAuthorizationMiddlewareResultHandlerTests", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("success/list/detail/page", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("CSV/XLSX export", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("filters", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("pagination", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("validation", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("limit", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("offset", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("masking", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("audit.read", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("AllowAnonymous", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("401/403 problem details", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("ProducesResponseType", controllerTestsLine, StringComparison.Ordinal);
        Assert.Contains("ChangeHistoryController", controllerTestsLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeHistoryLeftMenuNamingDecisionIsMarkedCompleteWhenCustomerLabelIsUsed()
    {
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## РСЃС‚РѕСЂРёСЏ РІС‹РїРѕР»РЅРµРЅРёСЏ", StringComparison.Ordinal))
            .ToArray();

        const string customerLabel = "\u0418\u0441\u0442\u043e\u0440\u0438\u044f \u0438\u0437\u043c\u0435\u043d\u0435\u043d\u0438\u0439";
        const string technicalLabel = "\u0410\u0443\u0434\u0438\u0442";
        const string decisionText = "\u0420\u0435\u0448\u0435\u043d\u043e \u043d\u0430\u0437\u0432\u0430\u043d\u0438\u0435 \u043b\u0435\u0432\u043e\u0433\u043e \u043f\u0443\u043d\u043a\u0442\u0430";
        const string riskText = "\u041d\u0430\u0437\u0432\u0430\u043d\u0438\u0435 \u043b\u0435\u0432\u043e\u0433\u043e \u043f\u0443\u043d\u043a\u0442\u0430 \u0440\u0435\u0448\u0435\u043d\u043e";

        Assert.DoesNotContain(activeRoadmapLines, line =>
            line.StartsWith("- `[decision]`", StringComparison.Ordinal) &&
            line.Contains(customerLabel, StringComparison.Ordinal) &&
            line.Contains(technicalLabel, StringComparison.Ordinal));

        var namingDecisionLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains(decisionText, StringComparison.Ordinal));
        var riskDecisionLine = activeRoadmapLines.Single(line =>
            line.StartsWith("- `[x]`", StringComparison.Ordinal) &&
            line.Contains(riskText, StringComparison.Ordinal));

        Assert.Contains(customerLabel, namingDecisionLine, StringComparison.Ordinal);
        Assert.Contains(technicalLabel, namingDecisionLine, StringComparison.Ordinal);
        Assert.Contains("App.tsx", namingDecisionLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", namingDecisionLine, StringComparison.Ordinal);
        Assert.Contains("ProjectWideRoadmapStatusTests", namingDecisionLine, StringComparison.Ordinal);
        Assert.Contains(customerLabel, riskDecisionLine, StringComparison.Ordinal);
        Assert.Contains(technicalLabel, riskDecisionLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessImportObjectCoverageKeepsPartialStatusUntilRealImportAndRollbackAreImplemented()
    {
        var importLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Импорт Access: dry-run", StringComparison.Ordinal));

        Assert.StartsWith("- `[~]` Импорт Access:", importLine, StringComparison.Ordinal);
        Assert.Contains("Reader Access", importLine, StringComparison.Ordinal);
        Assert.Contains("import.apply_requested", importLine, StringComparison.Ordinal);
        Assert.Contains("import.apply_request_cancelled", importLine, StringComparison.Ordinal);
        Assert.Contains("import.rollback_requested", importLine, StringComparison.Ordinal);
        Assert.Contains("backup confirmation", importLine, StringComparison.Ordinal);
        Assert.Contains("Фактический перенос строк Access", importLine, StringComparison.Ordinal);
        Assert.Contains("настоящий rollback созданных записей остаются следующими срезами", importLine, StringComparison.Ordinal);
    }

    [Fact]
    public void OneCFreshCoverageKeepsPartialStatusUntilRealSyncAndConflictResolutionAreImplemented()
    {
        var integrationLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("1C Fresh и будущие интеграции:", StringComparison.Ordinal));

        Assert.StartsWith("- `[~]` 1C Fresh и будущие интеграции:", integrationLine, StringComparison.Ordinal);
        Assert.Contains("one_c_fresh.sync_retry_requested", integrationLine, StringComparison.Ordinal);
        Assert.Contains("pending_adapter", integrationLine, StringComparison.Ordinal);
        Assert.Contains("без plaintext-токена", integrationLine, StringComparison.Ordinal);
        Assert.Contains("Реальный обмен с 1C Fresh", integrationLine, StringComparison.Ordinal);
        Assert.Contains("conflict resolution остаются будущими срезами", integrationLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptPrintingCoverageKeepsPartialStatusUntilRealDeviceAndAcceptanceAreImplemented()
    {
        var receiptLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Печать чеков/квитанций:", StringComparison.Ordinal));

        Assert.StartsWith("- `[~]` Печать чеков/квитанций:", receiptLine, StringComparison.Ordinal);
        Assert.Contains("печати, отмены и повторной печати квитанций", receiptLine, StringComparison.Ordinal);
        Assert.Contains("pending_adapter", receiptLine, StringComparison.Ordinal);
        Assert.Contains("device_error", receiptLine, StringComparison.Ordinal);
        Assert.Contains("Фактический адаптер конкретного устройства", receiptLine, StringComparison.Ordinal);
        Assert.Contains("ручная приемка на шаблоне квитанции остаются будущими срезами", receiptLine, StringComparison.Ordinal);
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
