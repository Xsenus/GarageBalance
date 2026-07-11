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
    public void FullPaymentAcceptanceScenarioIsMarkedCompleteWhenRoadmapCodeTestsAndReleaseNotesExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var financeApiText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "financeApi.ts"));
        var financeApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "financeApi.test.ts"));
        var releaseText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "AppReleases", "releases.json"));

        var acceptanceLine = activeRoadmapLines.Single(line =>
            line.Contains("Оператор принимает полную оплату по выбранному месяцу", StringComparison.Ordinal));
        var detailLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать форму `Полная оплата`", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]`", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("readonly рассчитанную сумму", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("CreateGarageDebtPayment", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("0.412.0", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("0.429.0", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("0.437.0", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("FullPaymentAcceptanceScenarioIsMarkedCompleteWhenRoadmapCodeTestsAndReleaseNotesExist", acceptanceLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[x]`", detailLine, StringComparison.Ordinal);

        Assert.Contains("<h3 id=\"full-payment-title\">Полная оплата</h3>", appText, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Сумма полной оплаты\" inputMode=\"decimal\" value={amount} readOnly", appText, StringComparison.Ordinal);
        Assert.Contains("createGarageDebtPayment", appText, StringComparison.Ordinal);
        Assert.Contains("shows payments prototype and opens payment form modals", appTestsText, StringComparison.Ordinal);
        Assert.Contains("pays opening debt through full payment when worksheet has no service rows", appTestsText, StringComparison.Ordinal);
        Assert.Contains("expect(fullPaymentAmount).toHaveAttribute('readonly')", appTestsText, StringComparison.Ordinal);
        Assert.Contains("/api/finance/income/debt-payment", financeApiText, StringComparison.Ordinal);
        Assert.Contains("posts opening debt payment to the debt payment endpoint", financeApiTestsText, StringComparison.Ordinal);

        Assert.Contains("\"version\": \"0.412.0\"", releaseText, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"0.429.0\"", releaseText, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"0.437.0\"", releaseText, StringComparison.Ordinal);
        Assert.Contains("Полная оплата использует рассчитанную сумму", releaseText, StringComparison.Ordinal);
        Assert.Contains("Полная оплата учитывает входящий долг периода", releaseText, StringComparison.Ordinal);

        Assert.Contains("синхронизирован верхний пользовательский сценарий приемки", historyText, StringComparison.Ordinal);
        Assert.Contains("FullPaymentAcceptanceScenarioIsMarkedCompleteWhenRoadmapCodeTestsAndReleaseNotesExist", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessImportAcceptanceScenarioIsInProgressWhenDryRunReportQuarantineAndApplyRequestExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var importServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportServiceTests.cs"));
        var importControllerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportControllerTests.cs"));
        var quarantineTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportQuarantineServiceTests.cs"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var importApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "importApi.test.ts"));
        var releaseText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "AppReleases", "releases.json"));

        var acceptanceLine = activeRoadmapLines.Single(line =>
            line.Contains("Администратор запускает dry-run импорта Access", StringComparison.Ordinal));
        var dryRunLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать dry-run импорт с отчетом", StringComparison.Ordinal));
        var quarantineLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать quarantine/error bucket", StringComparison.Ordinal));
        var transferLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать перенос справочников, гаражей, владельцев", StringComparison.Ordinal));

        Assert.StartsWith("- `[~]`", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("Dry-run", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("JSON-отчет", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("quarantine/error bucket", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("заявка на фактический перенос", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("фактический перенос строк Access", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("AccessImportAcceptanceScenarioIsInProgressWhenDryRunReportQuarantineAndApplyRequestExist", acceptanceLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[!]`", dryRunLine, StringComparison.Ordinal);
        Assert.Contains("AccessDryRunImportRoadmapItemRemainsBlockedUntilLiveReaderCountsAndReportExist", dryRunLine, StringComparison.Ordinal);
        Assert.Contains("реальные row counts/checksums", dryRunLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[!]`", quarantineLine, StringComparison.Ordinal);
        Assert.Contains("AccessImportQuarantineRoadmapItemRemainsBlockedUntilTransferRegistersMalformedRows", quarantineLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[!]`", transferLine, StringComparison.Ordinal);
        Assert.Contains("AccessTransferRoadmapItemRemainsBlockedUntilLiveReaderMappingAndReconciliationExist", transferLine, StringComparison.Ordinal);
        Assert.Contains("pending_access_reader", transferLine, StringComparison.Ordinal);

        Assert.Contains("access_dry_run_report_exported", importServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("runs/{id:guid}/report", importControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("ApplyResult", importControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("ApplyCancelResult", importControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("LogResult", importControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("CreatedRecordsResult", importControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("import.quarantine_registered", quarantineTestsText, StringComparison.Ordinal);
        Assert.Contains("import.quarantine_resolved", quarantineTestsText, StringComparison.Ordinal);
        Assert.Contains("runs Access import dry-run and shows checks history", appTestsText, StringComparison.Ordinal);
        Assert.Contains("requestAccessImportApply", appTestsText, StringComparison.Ordinal);
        Assert.Contains("cancelAccessImportApplyRequest", appTestsText, StringComparison.Ordinal);
        Assert.Contains("downloads dry-run report through POST", importApiTestsText, StringComparison.Ordinal);
        Assert.Contains("requests Access import apply with reason and backup confirmation", importApiTestsText, StringComparison.Ordinal);
        Assert.Contains("cancels Access import apply request with a required reason", importApiTestsText, StringComparison.Ordinal);

        Assert.Contains("В dry-run импорта Access теперь видно", releaseText, StringComparison.Ordinal);
        Assert.Contains("после успешного dry-run можно зафиксировать заявку на фактический перенос", releaseText, StringComparison.Ordinal);
        Assert.Contains("Закрытие строки карантина Access теперь требует отдельного подтверждения", releaseText, StringComparison.Ordinal);

        Assert.Contains("синхронизирован пользовательский сценарий приемки \"Администратор запускает dry-run импорта Access", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessImportAcceptanceScenarioIsInProgressWhenDryRunReportQuarantineAndApplyRequestExist", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendUnitTestingMatrixIsInProgressWhenFinanceTariffMeterDebtBalanceAndDateCoverageExists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var dictionaryServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Dictionaries", "DictionaryServiceTests.cs"));
        var financeServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Finance", "FinanceServiceTests.cs"));
        var reportServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportServiceTests.cs"));

        var backendUnitLine = activeRoadmapLines.Single(line =>
            line.Contains("Backend unit: тарифы, счетчики, начисления, платежи", StringComparison.Ordinal));

        Assert.StartsWith("- `[~]` Backend unit:", backendUnitLine, StringComparison.Ordinal);
        Assert.Contains("DictionaryServiceTests", backendUnitLine, StringComparison.Ordinal);
        Assert.Contains("FinanceServiceTests", backendUnitLine, StringComparison.Ordinal);
        Assert.Contains("ReportServiceTests", backendUnitLine, StringComparison.Ordinal);
        Assert.Contains("AwayFromZero", backendUnitLine, StringComparison.Ordinal);
        Assert.Contains("DateOnly", backendUnitLine, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL-specific edge cases", backendUnitLine, StringComparison.Ordinal);
        Assert.Contains("BackendUnitTestingMatrixIsInProgressWhenFinanceTariffMeterDebtBalanceAndDateCoverageExists", backendUnitLine, StringComparison.Ordinal);

        Assert.Contains("Dictionaries_RoundMoneyAndTariffRateBeforeSaving", dictionaryServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateTariffAsync_SavesElectricityTiersAndWritesAudit", dictionaryServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateTariffAsync_RejectsUnsupportedCalculationBase", dictionaryServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("UpdateTariffAsync_RejectsEffectiveDateAfterExistingRegularAccrual", dictionaryServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateFinanceDocuments_RoundsManualMoneyAmountsAwayFromZero", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateGarageDebtPaymentAsync_CreatesSystemIncomeAndReducesOpeningDebt", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateIncomeAsync_AllocatesPaymentToOldestGarageDebts", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateExpenseAsync_AllocatesPaymentToOldestSupplierDebts", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetGarageBalanceHistoryAsync_ReturnsMonthlyRunningDebt", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GenerateRegularAccrualsAsync_CalculatesMeterAmountFromReading", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GenerateRegularAccrualsAsync_CalculatesTieredElectricityAmountFromReading", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateMeterReadingAsync_RoundsMeterValuesAndConsumptionAwayFromZero", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetConsolidatedReportAsync_NormalizesReportPeriodToMonthStarts", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetIncomeReportAsync_ReturnsDebtAfterEachPayment", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetIncomeReportAsync_IncludesGarageStartingBalanceAsDebt", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetExpenseReportAsync_IncludesSupplierStartingBalanceAsObligation", reportServiceTestsText, StringComparison.Ordinal);

        Assert.Contains("синхронизирован пункт матрицы тестирования `Backend unit:", historyText, StringComparison.Ordinal);
        Assert.Contains("BackendUnitTestingMatrixIsInProgressWhenFinanceTariffMeterDebtBalanceAndDateCoverageExists", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void BalanceModelRoadmapItemRequiresBusinessDecisionForManualCorrectionsAndOverpaymentClosure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "balance-model-decision-checklist.md"));
        var financeServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Finance", "FinanceServiceTests.cs"));
        var formattersTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "formatters.test.ts"));
        var dictionaryWorkbenchText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "dictionaryWorkbench.ts"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));

        var balanceModelLine = activeRoadmapLines.Single(line =>
            line.Contains("Зафиксировать модель баланса", StringComparison.Ordinal));

        Assert.StartsWith("- `[decision]` Зафиксировать модель баланса", balanceModelLine, StringComparison.Ordinal);
        Assert.Contains("отрицательного долга как переплаты", balanceModelLine, StringComparison.Ordinal);
        Assert.Contains("распределение платежа по долгам", balanceModelLine, StringComparison.Ordinal);
        Assert.Contains("ручных корректировок", balanceModelLine, StringComparison.Ordinal);
        Assert.Contains("списания/возврата переплат", balanceModelLine, StringComparison.Ordinal);
        Assert.Contains("docs/balance-model-decision-checklist.md", balanceModelLine, StringComparison.Ordinal);
        Assert.Contains("BalanceModelRoadmapItemRequiresBusinessDecisionForManualCorrectionsAndOverpaymentClosure", balanceModelLine, StringComparison.Ordinal);

        Assert.Contains("Отрицательный долг отображается как переплата", checklist, StringComparison.Ordinal);
        Assert.Contains("Платежи распределяются по старейшим долгам", checklist, StringComparison.Ordinal);
        Assert.Contains("Переплата переносится на будущие месяцы автоматически", checklist, StringComparison.Ordinal);
        Assert.Contains("возвращать переплату", checklist, StringComparison.Ordinal);
        Assert.Contains("отрицательное начисление", checklist, StringComparison.Ordinal);
        Assert.Contains("смене владельца гаража", checklist, StringComparison.Ordinal);
        Assert.Contains("Guard `BalanceModelRoadmapItemRequiresBusinessDecisionForManualCorrectionsAndOverpaymentClosure`", checklist, StringComparison.Ordinal);

        Assert.Contains("CreateGarageDebtPaymentAsync_CreatesSystemIncomeAndReducesOpeningDebt", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateIncomeAsync_ReturnsGarageDebtBeforeAndAfterPayment", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateIncomeAsync_AllocatesPaymentToOldestGarageDebts", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateExpenseAsync_AllocatesPaymentToOldestSupplierDebts", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetGarageBalanceHistoryAsync_ReturnsMonthlyRunningDebt", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateAccrualAsync_RequiresCommentForManualAccrual", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("UpdateAccrualAsync_WritesBeforeAndAfterAuditForManualCorrection", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateSupplierAccrualAsync_RequiresCommentForManualAccrual", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("UpdateSupplierAccrualAsync_WritesBeforeAndAfterAuditForManualCorrection", financeServiceTestsText, StringComparison.Ordinal);

        Assert.Contains("getDebtClassName(-1)", formattersTestsText, StringComparison.Ordinal);
        Assert.Contains("formatPaymentAllocations", formattersTestsText, StringComparison.Ordinal);
        Assert.Contains("Долг положительным числом, переплата отрицательным.", dictionaryWorkbenchText, StringComparison.Ordinal);
        Assert.Contains("paymentAllocations", appTestsText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 6 \"Зафиксировать модель баланса\"", historyText, StringComparison.Ordinal);
        Assert.Contains("BalanceModelRoadmapItemRequiresBusinessDecisionForManualCorrectionsAndOverpaymentClosure", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void MonthlyAccrualRowsRoadmapItemIsCompleteWhenAccrualTablesTransferIndexesAndUiTestsExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "monthly-accrual-rows-verification.md"));
        var financeServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Finance", "FinanceServiceTests.cs"));
        var financeControllerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Finance", "FinanceControllerTests.cs"));
        var dbContextText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "GarageBalanceDbContext.cs"));
        var activeAccrualMigrationText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "Migrations", "20260624070738_ActiveAccrualUniqueness.cs"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var financeApiText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "financeApi.ts"));
        var releaseText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "AppReleases", "releases.json"));

        var monthlyAccrualsLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать помесячные строки начислений", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Реализовать помесячные строки начислений", monthlyAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("partial unique indexes", monthlyAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("отмена/восстановление с audit", monthlyAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("createDebtTransfer", monthlyAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("docs/monthly-accrual-rows-verification.md", monthlyAccrualsLine, StringComparison.Ordinal);
        Assert.Contains("MonthlyAccrualRowsRoadmapItemIsCompleteWhenAccrualTablesTransferIndexesAndUiTestsExist", monthlyAccrualsLine, StringComparison.Ordinal);

        Assert.Contains("Backend хранит начисления владельцев", verification, StringComparison.Ordinal);
        Assert.Contains("Повторный запуск регулярных начислений за тот же месяц не создает дубли", verification, StringComparison.Ordinal);
        Assert.Contains("UI поддерживает перенос задолженности владельца на следующий месяц", verification, StringComparison.Ordinal);
        Assert.Contains("FinanceServiceTests", verification, StringComparison.Ordinal);
        Assert.Contains("FinanceControllerTests", verification, StringComparison.Ordinal);
        Assert.Contains("ActiveAccrualUniqueness", verification, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не нужна", verification, StringComparison.Ordinal);

        Assert.Contains("CreateAccrualAsync_CreatesManualAccrualAndWritesAudit", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateAccrualAsync_RejectsDuplicateGarageTypeMonthAndSource", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateAccrualAsync_AllowsReplacementAfterCancel", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CancelAccrualAsync_CancelsAccrualAndRemovesItFromSummary", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("RestoreAccrualAsync_RestoresCanceledAccrualAndWritesAudit", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("RestoreAccrualAsync_RejectsDuplicateActiveAccrual", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateDebtTransferAsync_CreatesAndAccumulatesSystemAccrualWithAudit", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GenerateRegularAccrualsAsync_RejectsSecondRunForSameMonth", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateSupplierAccrualAsync_AllowsReplacementAfterCancel", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("RestoreSupplierAccrualAsync_RejectsDuplicateActiveAccrual", financeServiceTestsText, StringComparison.Ordinal);

        Assert.Contains("CreateDebtTransfer_PassesActorUserIdAndRequestToService", financeControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("CancelAccrual_ReturnsBadRequestForBlankCancelReason", financeControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("RestoreAccrual_ReturnsConflictForActiveAccrual", financeControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("GenerateRegularAccruals_PassesActorUserIdToService", financeControllerTestsText, StringComparison.Ordinal);

        Assert.Contains("HasFilter(\"\\\"IsCanceled\\\" = false\")", dbContextText, StringComparison.Ordinal);
        Assert.Contains("IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source", activeAccrualMigrationText, StringComparison.Ordinal);
        Assert.Contains("IX_supplier_accruals_SupplierId_ExpenseTypeId_AccountingMonth_~", activeAccrualMigrationText, StringComparison.Ordinal);

        Assert.Contains("moves garage debt to the next month and saves the transfer in form history", appTestsText, StringComparison.Ordinal);
        Assert.Contains("createDebtTransfer", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Последние начисления", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Разбивка начисления", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Месяц регулярных начислений", appTestsText, StringComparison.Ordinal);
        Assert.Contains("createDebtTransfer(accessToken", financeApiText, StringComparison.Ordinal);

        Assert.Contains("Задолженность можно перенести на следующий месяц", releaseText, StringComparison.Ordinal);
        Assert.Contains("пункт Stage 6 \"Реализовать помесячные строки начислений\"", historyText, StringComparison.Ordinal);
        Assert.Contains("MonthlyAccrualRowsRoadmapItemIsCompleteWhenAccrualTablesTransferIndexesAndUiTestsExist", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void IncomeExcelScenarioRoadmapItemIsCompleteWhenWorksheetPaymentCellCashAndHistoryAreCovered()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "income-excel-scenario-verification.md"));
        var financeServiceText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        var financeServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Finance", "FinanceServiceTests.cs"));
        var financeControllerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Finance", "FinanceControllerTests.cs"));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var financeApiText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "financeApi.ts"));

        var incomeExcelLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать Excel-сценарий поступлений", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Реализовать Excel-сценарий поступлений", incomeExcelLine, StringComparison.Ordinal);
        Assert.Contains("getGarageIncomeWorksheet", incomeExcelLine, StringComparison.Ordinal);
        Assert.Contains("createIncome", incomeExcelLine, StringComparison.Ordinal);
        Assert.Contains("Платёж", incomeExcelLine, StringComparison.Ordinal);
        Assert.Contains("Оплачено", incomeExcelLine, StringComparison.Ordinal);
        Assert.Contains("Задолженность", incomeExcelLine, StringComparison.Ordinal);
        Assert.Contains("кассы", incomeExcelLine, StringComparison.Ordinal);
        Assert.Contains("истории платежей", incomeExcelLine, StringComparison.Ordinal);
        Assert.Contains("docs/income-excel-scenario-verification.md", incomeExcelLine, StringComparison.Ordinal);
        Assert.Contains("IncomeExcelScenarioRoadmapItemIsCompleteWhenWorksheetPaymentCellCashAndHistoryAreCovered", incomeExcelLine, StringComparison.Ordinal);

        Assert.Contains("Выбор гаража выполняется через поиск", verification, StringComparison.Ordinal);
        Assert.Contains("backend-ведомости `getGarageIncomeWorksheet`", verification, StringComparison.Ordinal);
        Assert.Contains("После Enter ячейка платежа очищается", verification, StringComparison.Ordinal);
        Assert.Contains("История платежей гаража пополняется новой операцией", verification, StringComparison.Ordinal);
        Assert.Contains("Итоги кассы и банка", verification, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не нужна", verification, StringComparison.Ordinal);

        Assert.Contains("GetGarageIncomeWorksheetAsync", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(bucket => bucket.AccountingMonth)", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("GetGarageIncomeWorksheetAsync_BuildsRowsFromAccrualsPaymentsAndMeters", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetGarageIncomeWorksheetAsync_CarriesOpeningDebtIntoPeriodTotals", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetGarageIncomeWorksheet_PassesGarageAndPeriodToService", financeControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("GetGarageIncomeWorksheet_ReturnsNotFoundForMissingGarage", financeControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("GetGarageIncomeWorksheet_ReturnsBadRequestForInvalidPeriod", financeControllerTestsText, StringComparison.Ordinal);

        Assert.Contains("createGarageIncomeRowsFromWorksheet", appText, StringComparison.Ordinal);
        Assert.Contains("loadGarageIncomeWorksheet", appText, StringComparison.Ordinal);
        Assert.Contains("financeClient.createIncome", appText, StringComparison.Ordinal);
        Assert.Contains("paymentDraft: ''", appText, StringComparison.Ordinal);
        Assert.Contains("setHistoryRows", appText, StringComparison.Ordinal);
        Assert.Contains("loadGaragePaymentHistory(selectedGarage)", appText, StringComparison.Ordinal);
        Assert.Contains("Выбранный гараж", appText, StringComparison.Ordinal);
        Assert.Contains("Платёж", appText, StringComparison.Ordinal);
        Assert.Contains("Оплачено", appText, StringComparison.Ordinal);
        Assert.Contains("Задолженность", appText, StringComparison.Ordinal);
        Assert.Contains("История платежей гаража", appText, StringComparison.Ordinal);
        Assert.Contains("Итоги кассы и банка", appText, StringComparison.Ordinal);

        Assert.Contains("loads selected garage income worksheet from finance backend", appTestsText, StringComparison.Ordinal);
        Assert.Contains("does not restore stale saved payment rows for a real garage before backend worksheet loads", appTestsText, StringComparison.Ordinal);
        Assert.Contains("does not show prototype income rows while selected real garage worksheet is unavailable", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Платеж Серверная электроэнергия июн.26", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Платеж Электроэнергия июн.26", appTestsText, StringComparison.Ordinal);
        Assert.Contains("toHaveValue('')", appTestsText, StringComparison.Ordinal);
        Assert.Contains("savedIncomeRequests[0]", appTestsText, StringComparison.Ordinal);
        Assert.Contains("getGarageIncomeWorksheet", financeApiText, StringComparison.Ordinal);
        Assert.Contains("createIncome(accessToken", financeApiText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 6 \"Реализовать Excel-сценарий поступлений\"", historyText, StringComparison.Ordinal);
        Assert.Contains("IncomeExcelScenarioRoadmapItemIsCompleteWhenWorksheetPaymentCellCashAndHistoryAreCovered", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void IncomeServiceRulesRoadmapItemRequiresBusinessDecisionForCurrentMonthEarlyPaymentWaterAndAnnualStopRules()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "income-service-rules-decision-checklist.md"));
        var financeServiceText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        var dictionaryServiceText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));

        var serviceRulesLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать уточненную логику поступлений по услугам", StringComparison.Ordinal));

        Assert.StartsWith("- `[decision]` Реализовать уточненную логику поступлений по услугам", serviceRulesLine, StringComparison.Ordinal);
        Assert.DoesNotContain("- `[x]` Реализовать уточненную логику поступлений по услугам", serviceRulesLine, StringComparison.Ordinal);
        Assert.Contains("электросчетчик только за текущий месяц", serviceRulesLine, StringComparison.Ordinal);
        Assert.Contains("подтверждению платежа по электроэнергии раньше 30-го дня", serviceRulesLine, StringComparison.Ordinal);
        Assert.Contains("подсветки ручного водоснабжения", serviceRulesLine, StringComparison.Ordinal);
        Assert.Contains("годовых взносов и наружного освещения", serviceRulesLine, StringComparison.Ordinal);
        Assert.Contains("docs/income-service-rules-decision-checklist.md", serviceRulesLine, StringComparison.Ordinal);
        Assert.Contains("IncomeServiceRulesRoadmapItemRequiresBusinessDecisionForCurrentMonthEarlyPaymentWaterAndAnnualStopRules", serviceRulesLine, StringComparison.Ordinal);

        Assert.Contains("Backend проверяет совместимость вида поступления и тарифа", checklist, StringComparison.Ordinal);
        Assert.Contains("мусор использует `people`", checklist, StringComparison.Ordinal);
        Assert.Contains("Форма поступлений подсвечивает пустую ячейку счетчика", checklist, StringComparison.Ordinal);
        Assert.Contains("Правка электросчетчика только за текущий месяц", checklist, StringComparison.Ordinal);
        Assert.Contains("Подтверждение платежа по электроэнергии раньше 30 дней", checklist, StringComparison.Ordinal);
        Assert.Contains("Годовые взносы и наружное освещение", checklist, StringComparison.Ordinal);
        Assert.Contains("Roadmap-пункт нельзя закрывать как `[x]`", checklist, StringComparison.Ordinal);

        Assert.Contains("\"water\" => calculationBase == TariffCalculationBases.MeterWater", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("\"trash\" => calculationBase == TariffCalculationBases.People", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("\"electricity\" => calculationBase == TariffCalculationBases.MeterElectricity", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("TariffCalculationBases.People => AmountCalculationResult.Success(MoneyMath.RoundMoney(tariff.Rate * garage.PeopleCount))", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("TariffCalculationBases.MeterWater => await CalculateMeterAmountAsync", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("TariffCalculationBases.MeterElectricity => await CalculateElectricityMeterAmountAsync", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("HasGapWarning", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("IsChargeServiceDueForMonth", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("periodicity >= 12", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("FeeCampaign", financeServiceText, StringComparison.Ordinal);

        Assert.Contains("\"trash\" => calculationBase == TariffCalculationBases.People", dictionaryServiceText, StringComparison.Ordinal);
        Assert.Contains("paymentDueDay", dictionaryServiceText, StringComparison.Ordinal);
        Assert.Contains("paymentDueMonth", dictionaryServiceText, StringComparison.Ordinal);

        Assert.Contains("payments-prototype-required-cell", appText, StringComparison.Ordinal);
        Assert.Contains("row.meterRequired && row.meter === null", appText, StringComparison.Ordinal);
        Assert.Contains("meterRequired: row.meterKind !== null && row.meterValue === null", appText, StringComparison.Ordinal);
        Assert.Contains("meter_reading", appText, StringComparison.Ordinal);
        Assert.Contains("Подтвердить показание?", appText, StringComparison.Ordinal);

        Assert.Contains("calculationBase: 'meter_water'", appTestsText, StringComparison.Ordinal);
        Assert.Contains("calculationBase: 'meter_electricity'", appTestsText, StringComparison.Ordinal);
        Assert.Contains("paymentDueDay", appTestsText, StringComparison.Ordinal);
        Assert.Contains("createFeeCampaign", appTestsText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 6 \"Реализовать уточненную логику поступлений по услугам\"", historyText, StringComparison.Ordinal);
        Assert.Contains("IncomeServiceRulesRoadmapItemRequiresBusinessDecisionForCurrentMonthEarlyPaymentWaterAndAnnualStopRules", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpenseExcelScenarioRoadmapItemRequiresBusinessDecisionForRolloverZeroingAndManualCostEntry()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "expense-excel-scenario-decision-checklist.md"));
        var financeServiceText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        var financeServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Finance", "FinanceServiceTests.cs"));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var financeApiText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "financeApi.ts"));

        var expenseExcelLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать Excel-сценарий выплат", StringComparison.Ordinal));

        Assert.StartsWith("- `[decision]` Реализовать Excel-сценарий выплат", expenseExcelLine, StringComparison.Ordinal);
        Assert.DoesNotContain("- `[x]` Реализовать Excel-сценарий выплат", expenseExcelLine, StringComparison.Ordinal);
        Assert.Contains("серверная месячная ведомость выплат", expenseExcelLine, StringComparison.Ordinal);
        Assert.Contains("ручное начисление поставщику по счету", expenseExcelLine, StringComparison.Ordinal);
        Assert.Contains("переноса остатков/долгов", expenseExcelLine, StringComparison.Ordinal);
        Assert.Contains("обнулению полностью оплаченных услуг", expenseExcelLine, StringComparison.Ordinal);
        Assert.Contains("audit-документу закрытия месяца", expenseExcelLine, StringComparison.Ordinal);
        Assert.Contains("docs/expense-excel-scenario-decision-checklist.md", expenseExcelLine, StringComparison.Ordinal);
        Assert.Contains("ExpenseExcelScenarioRoadmapItemRequiresBusinessDecisionForRolloverZeroingAndManualCostEntry", expenseExcelLine, StringComparison.Ordinal);

        Assert.Contains("Backend строит месячную ведомость выплат", checklist, StringComparison.Ordinal);
        Assert.Contains("UI позволяет добавить начисление поставщику по счету", checklist, StringComparison.Ordinal);
        Assert.Contains("источник ручного ввода стоимости услуг по счетам", checklist, StringComparison.Ordinal);
        Assert.Contains("что переносится в следующий месяц", checklist, StringComparison.Ordinal);
        Assert.Contains("правило обнуления полностью оплаченных услуг", checklist, StringComparison.Ordinal);
        Assert.Contains("audit-событие и документ", checklist, StringComparison.Ordinal);
        Assert.Contains("Guard `ExpenseExcelScenarioRoadmapItemRequiresBusinessDecisionForRolloverZeroingAndManualCostEntry`", checklist, StringComparison.Ordinal);

        Assert.Contains("GetExpenseWorksheetAsync", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("TryGetCollectedAmount", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("CalculateAvailableBankAmountAsync", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("CalculateAvailableCashAmountAsync", financeServiceText, StringComparison.Ordinal);
        Assert.Contains("GetExpenseWorksheetAsync_BuildsRowsFromSupplierAccrualsExpensesStaffAndCollections", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetExpenseWorksheetAsync_KeepsCashAndBankEqualCollectedFundsAfterMixedExpenses", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateExpenseAsync_AllocatesPaymentToOldestSupplierDebts", financeServiceTestsText, StringComparison.Ordinal);

        Assert.Contains("createExpenseRowsFromWorksheet", appText, StringComparison.Ordinal);
        Assert.Contains(".getExpenseWorksheet(auth.accessToken", appText, StringComparison.Ordinal);
        Assert.Contains("Оплатить", appText, StringComparison.Ordinal);
        Assert.Contains("Добавить начисление", appText, StringComparison.Ordinal);
        Assert.Contains("Добавить выплату", appText, StringComparison.Ordinal);
        Assert.Contains("loads expense worksheet from finance backend", appTestsText, StringComparison.Ordinal);
        Assert.Contains("does not show prototype expense rows when expense worksheet is unavailable", appTestsText, StringComparison.Ordinal);
        Assert.Contains("savedExpenseRequests[0]", appTestsText, StringComparison.Ordinal);
        Assert.Contains("savedStaffPaymentRequests[0]", appTestsText, StringComparison.Ordinal);
        Assert.Contains("getExpenseWorksheet(accessToken", financeApiText, StringComparison.Ordinal);
        Assert.Contains("createSupplierAccrual(accessToken", financeApiText, StringComparison.Ordinal);
        Assert.Contains("createExpense(accessToken", financeApiText, StringComparison.Ordinal);
        Assert.Contains("createStaffPayment(accessToken", financeApiText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 6 \"Реализовать Excel-сценарий выплат\"", historyText, StringComparison.Ordinal);
        Assert.Contains("ExpenseExcelScenarioRoadmapItemRequiresBusinessDecisionForRolloverZeroingAndManualCostEntry", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendFinanceTestCoverageRoadmapItemIsCompleteWhenCalculationsDebtsPermissionsAndAuditAreCovered()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "backend-finance-tests-verification.md"));
        var financeServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Finance", "FinanceServiceTests.cs"));
        var financeControllerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Finance", "FinanceControllerTests.cs"));
        var authorizationTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Auth", "ControllerAuthorizationCoverageTests.cs"));
        var permissionHandlerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Auth", "PermissionAuthorizationHandlerTests.cs"));

        var backendFinanceTestsLine = activeRoadmapLines.Single(line =>
            line.Contains("Добавить backend-тесты расчетов, долгов, ручных корректировок, прав и audit", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Добавить backend-тесты расчетов, долгов, ручных корректировок, прав и audit", backendFinanceTestsLine, StringComparison.Ordinal);
        Assert.Contains("FinanceServiceTests", backendFinanceTestsLine, StringComparison.Ordinal);
        Assert.Contains("FinanceControllerTests", backendFinanceTestsLine, StringComparison.Ordinal);
        Assert.Contains("ControllerAuthorizationCoverageTests", backendFinanceTestsLine, StringComparison.Ordinal);
        Assert.Contains("PermissionAuthorizationHandlerTests", backendFinanceTestsLine, StringComparison.Ordinal);
        Assert.Contains("docs/backend-finance-tests-verification.md", backendFinanceTestsLine, StringComparison.Ordinal);
        Assert.Contains("BackendFinanceTestCoverageRoadmapItemIsCompleteWhenCalculationsDebtsPermissionsAndAuditAreCovered", backendFinanceTestsLine, StringComparison.Ordinal);

        Assert.Contains("Долги владельцев и поставщиков", verification, StringComparison.Ordinal);
        Assert.Contains("Ручные корректировки начислений", verification, StringComparison.Ordinal);
        Assert.Contains("Backend-права проверяются", verification, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не нужна", verification, StringComparison.Ordinal);

        Assert.Contains("CreateIncomeAsync_CreatesOperationAndWritesAudit", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("UpdateIncomeAsync_UpdatesOperationAndWritesBeforeAfterAudit", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateExpenseAsync_CreatesOperationAndWritesAudit", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("UpdateExpenseAsync_UpdatesOperationAndWritesBeforeAfterAudit", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateIncomeAsync_AllocatesPaymentToOldestGarageDebts", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateExpenseAsync_AllocatesPaymentToOldestSupplierDebts", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateGarageDebtPaymentAsync_CreatesSystemIncomeAndReducesOpeningDebt", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateAccrualAsync_RequiresCommentForManualAccrual", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("UpdateAccrualAsync_WritesBeforeAndAfterAuditForManualCorrection", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateSupplierAccrualAsync_RequiresCommentForManualAccrual", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("UpdateSupplierAccrualAsync_WritesBeforeAndAfterAuditForManualCorrection", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("CancelOperationAsync_CancelsOperationAndRemovesItFromSummary", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("RestoreOperationAsync_RestoresCanceledIncomeAndWritesAudit", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetGarageIncomeWorksheetAsync_CarriesOpeningDebtIntoPeriodTotals", financeServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetExpenseWorksheetAsync_KeepsCashAndBankEqualCollectedFundsAfterMixedExpenses", financeServiceTestsText, StringComparison.Ordinal);

        Assert.Contains("CreateIncome_PassesActorUserIdToService", financeControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateExpense_ReturnsConflictWhenBankAmountIsInsufficient", financeControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("CancelOperation_ReturnsBadRequestForMissingCancelBody", financeControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateDebtTransfer_PassesActorUserIdAndRequestToService", financeControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateSupplierAccrual_ReturnsConflictForDuplicateAccrual", financeControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("CancelSupplierAccrual_ReturnsBadRequestForBlankCancelReason", financeControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("CreateMeterReading_ReturnsConflictForDuplicateReading", financeControllerTestsText, StringComparison.Ordinal);
        Assert.Contains("RestoreMeterReading_ReturnsConflictForActiveReading", financeControllerTestsText, StringComparison.Ordinal);

        Assert.Contains("FinanceActionsRequireExpectedPaymentPermissions", authorizationTestsText, StringComparison.Ordinal);
        Assert.Contains("FundsActionsRequireReportsReadAndPaymentsWritePermissions", authorizationTestsText, StringComparison.Ordinal);
        Assert.Contains("SystemPermissions.PaymentsRead", authorizationTestsText, StringComparison.Ordinal);
        Assert.Contains("SystemPermissions.PaymentsWrite", authorizationTestsText, StringComparison.Ordinal);
        Assert.Contains("HandleAsync_SucceedsWhenPermissionClaimExists", permissionHandlerTestsText, StringComparison.Ordinal);
        Assert.Contains("HandleAsync_DoesNotSucceedWhenPermissionClaimIsMissing", permissionHandlerTestsText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 6 \"Добавить backend-тесты расчетов, долгов", historyText, StringComparison.Ordinal);
        Assert.Contains("BackendFinanceTestCoverageRoadmapItemIsCompleteWhenCalculationsDebtsPermissionsAndAuditAreCovered", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendPaymentTestCoverageRoadmapItemIsCompleteWhenTablesDialogsPaymentsWarningsAndErrorsAreCovered()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "frontend-payment-tests-verification.md"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));

        var frontendPaymentTestsLine = activeRoadmapLines.Single(line =>
            line.Contains("Добавить React-тесты таблиц, модалок, платежей, подсветок, предупреждений и ошибок", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Добавить React-тесты таблиц, модалок, платежей, подсветок, предупреждений и ошибок", frontendPaymentTestsLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", frontendPaymentTestsLine, StringComparison.Ordinal);
        Assert.Contains("server-side ведомости", frontendPaymentTestsLine, StringComparison.Ordinal);
        Assert.Contains("context menu", frontendPaymentTestsLine, StringComparison.Ordinal);
        Assert.Contains("read-only permissions", frontendPaymentTestsLine, StringComparison.Ordinal);
        Assert.Contains("warning разрыва электроэнергии", frontendPaymentTestsLine, StringComparison.Ordinal);
        Assert.Contains("подсветку гаражей без показаний", frontendPaymentTestsLine, StringComparison.Ordinal);
        Assert.Contains("docs/frontend-payment-tests-verification.md", frontendPaymentTestsLine, StringComparison.Ordinal);
        Assert.Contains("FrontendPaymentTestCoverageRoadmapItemIsCompleteWhenTablesDialogsPaymentsWarningsAndErrorsAreCovered", frontendPaymentTestsLine, StringComparison.Ordinal);

        Assert.Contains("Таблицы платежного раздела", verification, StringComparison.Ordinal);
        Assert.Contains("Платежные dialogs покрыты", verification, StringComparison.Ordinal);
        Assert.Contains("Read-only/permission state покрыт", verification, StringComparison.Ordinal);
        Assert.Contains("Предупреждения покрыты", verification, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не нужна", verification, StringComparison.Ordinal);

        Assert.Contains("shows payments prototype and opens payment form modals", appTestsText, StringComparison.Ordinal);
        Assert.Contains("loads selected garage income worksheet from finance backend", appTestsText, StringComparison.Ordinal);
        Assert.Contains("loads expense worksheet from finance backend", appTestsText, StringComparison.Ordinal);
        Assert.Contains("does not show prototype expense rows when expense worksheet is unavailable", appTestsText, StringComparison.Ordinal);
        Assert.Contains("edits income operation from payments table", appTestsText, StringComparison.Ordinal);
        Assert.Contains("edits expense operation from payments table with confirmation", appTestsText, StringComparison.Ordinal);
        Assert.Contains("opens new income dialog from payment context menu", appTestsText, StringComparison.Ordinal);
        Assert.Contains("opens create dialogs from every payment table context menu", appTestsText, StringComparison.Ordinal);
        Assert.Contains("does not call finance APIs when payment forms fail client validation", appTestsText, StringComparison.Ordinal);
        Assert.Contains("cancels income operation with required reason from payments workspace", appTestsText, StringComparison.Ordinal);
        Assert.Contains("cancels expense operation with required reason from payments table context menu", appTestsText, StringComparison.Ordinal);
        Assert.Contains("cancels accruals and meter readings with required reasons from payments workspace", appTestsText, StringComparison.Ordinal);
        Assert.Contains("warns before closing changed payment editor", appTestsText, StringComparison.Ordinal);
        Assert.Contains("shows electricity gap warning returned by API", appTestsText, StringComparison.Ordinal);
        Assert.Contains("highlights garages without meter readings for selected month", appTestsText, StringComparison.Ordinal);
        Assert.Contains("keeps dictionary and payment actions read-only without write permissions", appTestsText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 6 \"Добавить React-тесты таблиц", historyText, StringComparison.Ordinal);
        Assert.Contains("FrontendPaymentTestCoverageRoadmapItemIsCompleteWhenTablesDialogsPaymentsWarningsAndErrorsAreCovered", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void IncomeReportRoadmapItemIsCompleteWhenFiltersRowModesExportsAndUiTestsAreCovered()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "income-report-verification.md"));
        var reportServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportServiceTests.cs"));
        var controllerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportsControllerTests.cs"));
        var reportsApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "reportsApi.test.ts"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var reportFiltersTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "reportFilters.test.ts"));
        var validationTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "validation.test.ts"));

        var incomeReportLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать отчет по поступлениям с фильтрами по датам", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Реализовать отчет по поступлениям", incomeReportLine, StringComparison.Ordinal);
        Assert.Contains("GET /api/reports/income", incomeReportLine, StringComparison.Ordinal);
        Assert.Contains("мультивыбор гаражей/владельцев/видов поступлений", incomeReportLine, StringComparison.Ordinal);
        Assert.Contains("режим строк", incomeReportLine, StringComparison.Ordinal);
        Assert.Contains("XLSX-экспорт", incomeReportLine, StringComparison.Ordinal);
        Assert.Contains("PDF-экспорт", incomeReportLine, StringComparison.Ordinal);
        Assert.Contains("docs/income-report-verification.md", incomeReportLine, StringComparison.Ordinal);
        Assert.Contains("IncomeReportRoadmapItemIsCompleteWhenFiltersRowModesExportsAndUiTestsAreCovered", incomeReportLine, StringComparison.Ordinal);

        Assert.Contains("Backend endpoint `GET /api/reports/income`", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/income/export/xlsx", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/income/export/pdf", verification, StringComparison.Ordinal);
        Assert.Contains("rowMode", verification, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не нужна", verification, StringComparison.Ordinal);

        Assert.Contains("GetIncomeReportAsync_ReturnsAccrualAndPaymentRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetIncomeReportAsync_AppliesRowLimitWithoutChangingTotals", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetIncomeReportAsync_ReturnsDebtAfterEachPayment", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetIncomeReportAsync_IncludesGarageStartingBalanceAsDebt", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetIncomeReportAsync_FiltersByOwnerIncomeTypeAndRowMode", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportIncomeReportXlsxAsync_ReturnsWorkbookWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportIncomeReportPdfAsync_ReturnsDocumentWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportIncomeReportXlsxAsync_WritesGeneratedAndExportedAuditWithoutRawSearch", reportServiceTestsText, StringComparison.Ordinal);

        Assert.Contains("GetIncomeReport_ReturnsOk", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("GetIncomeReport_ReturnsBadRequestForInvalidPeriod", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportIncomeReportXlsx_ReturnsFile", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportIncomeReportPdf_ReturnsFile", controllerTestsText, StringComparison.Ordinal);

        Assert.Contains("exportIncomeReportXlsx('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportIncomeReportPdf('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("garageIds=garage-1&ownerIds=owner-1&incomeTypeIds=income-1", reportsApiTestsText, StringComparison.Ordinal);

        Assert.Contains("Отчет по поступлениям", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Остаток долга после платежа", appTestsText, StringComparison.Ordinal);
        Assert.Contains("not.toHaveTextContent('Начисление за июнь')", appTestsText, StringComparison.Ordinal);
        Assert.Contains("garageIds: ['g1']", reportFiltersTestsText, StringComparison.Ordinal);
        Assert.Contains("ownerIds: ['o1']", reportFiltersTestsText, StringComparison.Ordinal);
        Assert.Contains("incomeTypeIds: ['i1']", reportFiltersTestsText, StringComparison.Ordinal);
        Assert.Contains("getIncomeReportValidationErrors", validationTestsText, StringComparison.Ordinal);
        Assert.Contains("Начало отчета по поступлениям не может быть позже конца", validationTestsText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 7 \"Реализовать отчет по поступлениям", historyText, StringComparison.Ordinal);
        Assert.Contains("IncomeReportRoadmapItemIsCompleteWhenFiltersRowModesExportsAndUiTestsAreCovered", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpenseReportRoadmapItemIsCompleteWhenFiltersRowModesExportsAndUiTestsAreCovered()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "expense-report-verification.md"));
        var reportServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportServiceTests.cs"));
        var controllerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportsControllerTests.cs"));
        var reportsApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "reportsApi.test.ts"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var reportFiltersTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "reportFilters.test.ts"));
        var validationTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "validation.test.ts"));

        var expenseReportLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать отчет по выплатам с фильтрами по датам", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Реализовать отчет по выплатам", expenseReportLine, StringComparison.Ordinal);
        Assert.Contains("GET /api/reports/expense", expenseReportLine, StringComparison.Ordinal);
        Assert.Contains("мультивыбор поставщиков/видов выплат", expenseReportLine, StringComparison.Ordinal);
        Assert.Contains("режим строк", expenseReportLine, StringComparison.Ordinal);
        Assert.Contains("начисления поставщикам", expenseReportLine, StringComparison.Ordinal);
        Assert.Contains("XLSX-экспорт", expenseReportLine, StringComparison.Ordinal);
        Assert.Contains("PDF-экспорт", expenseReportLine, StringComparison.Ordinal);
        Assert.Contains("docs/expense-report-verification.md", expenseReportLine, StringComparison.Ordinal);
        Assert.Contains("ExpenseReportRoadmapItemIsCompleteWhenFiltersRowModesExportsAndUiTestsAreCovered", expenseReportLine, StringComparison.Ordinal);

        Assert.Contains("Backend endpoint `GET /api/reports/expense`", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/expense/export/xlsx", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/expense/export/pdf", verification, StringComparison.Ordinal);
        Assert.Contains("SupplierStartingBalanceAsObligation", verification, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не нужна", verification, StringComparison.Ordinal);

        Assert.Contains("GetExpenseReportAsync_ReturnsPaymentRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetExpenseReportAsync_AppliesRowLimitWithoutChangingTotals", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetExpenseReportAsync_IncludesSupplierStartingBalanceAsObligation", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetExpenseReportAsync_FiltersBySupplierExpenseTypeAndSearch", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetExpenseReportAsync_ReturnsSupplierAccrualRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportExpenseReportXlsxAsync_ReturnsWorkbookWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportExpenseReportPdfAsync_ReturnsDocumentWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);

        Assert.Contains("GetExpenseReport_ReturnsOk", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("GetExpenseReport_ReturnsBadRequestForInvalidPeriod", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportExpenseReportXlsx_ReturnsFile", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportExpenseReportPdf_ReturnsFile", controllerTestsText, StringComparison.Ordinal);

        Assert.Contains("exportExpenseReportXlsx('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportExpenseReportPdf('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("supplierIds=supplier-1&expenseTypeIds=expense-1", reportsApiTestsText, StringComparison.Ordinal);

        Assert.Contains("Отчёт по выплатам", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Поставщики или сотрудники", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Отчет по выплатам", appTestsText, StringComparison.Ordinal);
        Assert.Contains("supplierIds: ['s1']", reportFiltersTestsText, StringComparison.Ordinal);
        Assert.Contains("expenseTypeIds: ['e1']", reportFiltersTestsText, StringComparison.Ordinal);
        Assert.Contains("getExpenseReportValidationErrors", validationTestsText, StringComparison.Ordinal);
        Assert.Contains("Укажите начало отчета по выплатам", validationTestsText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 7 \"Реализовать отчет по выплатам", historyText, StringComparison.Ordinal);
        Assert.Contains("ExpenseReportRoadmapItemIsCompleteWhenFiltersRowModesExportsAndUiTestsAreCovered", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void XlsxExportRoadmapItemIsCompleteWhenCurrentReportWorkbooksRoutesAndUiClientsAreCovered()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "xlsx-export-verification.md"));
        var reportServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportServiceTests.cs"));
        var controllerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportsControllerTests.cs"));
        var reportsApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "reportsApi.test.ts"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var xlsxBuilderText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Reports", "XlsxWorkbookBuilder.cs"));

        var xlsxLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать экспорт XLSX", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Реализовать экспорт XLSX", xlsxLine, StringComparison.Ordinal);
        Assert.Contains("сводного отчета", xlsxLine, StringComparison.Ordinal);
        Assert.Contains("поступлениям", xlsxLine, StringComparison.Ordinal);
        Assert.Contains("выплатам", xlsxLine, StringComparison.Ordinal);
        Assert.Contains("оплатам из кассы", xlsxLine, StringComparison.Ordinal);
        Assert.Contains("сдаче кассы в банк", xlsxLine, StringComparison.Ordinal);
        Assert.Contains("сборам", xlsxLine, StringComparison.Ordinal);
        Assert.Contains("изменению фондов", xlsxLine, StringComparison.Ordinal);
        Assert.Contains("docs/xlsx-export-verification.md", xlsxLine, StringComparison.Ordinal);
        Assert.Contains("XlsxExportRoadmapItemIsCompleteWhenCurrentReportWorkbooksRoutesAndUiClientsAreCovered", xlsxLine, StringComparison.Ordinal);

        Assert.Contains("POST /api/reports/consolidated/export/xlsx", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/income/export/xlsx", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/expense/export/xlsx", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/cash-payments/export/xlsx", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/bank-deposits/export/xlsx", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/fees/export/xlsx", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/fund-changes/export/xlsx", verification, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не нужна", verification, StringComparison.Ordinal);

        Assert.Contains("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsxBuilderText, StringComparison.Ordinal);
        Assert.Contains("ExportConsolidatedReportXlsxAsync_ReturnsWorkbookWithMonthlyAndGarageRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportConsolidatedReportXlsxAsync_AppliesGarageSearchFilter", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportIncomeReportXlsxAsync_ReturnsWorkbookWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportExpenseReportXlsxAsync_ReturnsWorkbookWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportCashPaymentReportXlsxAsync_ReturnsWorkbookWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportBankDepositReportXlsxAsync_ReturnsWorkbookWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportFeeReportXlsxAsync_ReturnsWorkbookWithSummaryGaragesAndDebtors", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportFundChangeReportXlsxAsync_ReturnsWorkbookWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("AssertWorkbookContains", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("AssertWorkbookDoesNotContain", reportServiceTestsText, StringComparison.Ordinal);

        Assert.Contains("ExportReportActions_UsePostBecauseExportsWriteAuditEvents", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("consolidated/export/xlsx", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("income/export/xlsx", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("expense/export/xlsx", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("fund-changes/export/xlsx", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("cash-payments/export/xlsx", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("bank-deposits/export/xlsx", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("fees/export/xlsx", controllerTestsText, StringComparison.Ordinal);

        Assert.Contains("exportConsolidatedReportXlsx('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportIncomeReportXlsx('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportExpenseReportXlsx('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportCashPaymentReportXlsx('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportBankDepositReportXlsx('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportFeeReportXlsx('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportFundChangeReportXlsx('token'", reportsApiTestsText, StringComparison.Ordinal);

        Assert.Contains("Скачать XLSX", appTestsText, StringComparison.Ordinal);
        Assert.Contains("exportCashPaymentReportXlsx", appTestsText, StringComparison.Ordinal);
        Assert.Contains("exportFeeReportXlsx", appTestsText, StringComparison.Ordinal);
        Assert.Contains("exportFundChangeReportXlsx", appTestsText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 7 \"Реализовать экспорт XLSX", historyText, StringComparison.Ordinal);
        Assert.Contains("XlsxExportRoadmapItemIsCompleteWhenCurrentReportWorkbooksRoutesAndUiClientsAreCovered", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfExportRoadmapItemIsCompleteWhenCurrentReportDocumentsRoutesAndUiClientsAreCovered()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "pdf-export-verification.md"));
        var reportServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportServiceTests.cs"));
        var controllerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportsControllerTests.cs"));
        var reportsApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "reportsApi.test.ts"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var pdfBuilderText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Reports", "PdfReportDocumentBuilder.cs"));

        var pdfLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать экспорт PDF", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Реализовать экспорт PDF", pdfLine, StringComparison.Ordinal);
        Assert.Contains("сводного отчета", pdfLine, StringComparison.Ordinal);
        Assert.Contains("поступлениям", pdfLine, StringComparison.Ordinal);
        Assert.Contains("выплатам", pdfLine, StringComparison.Ordinal);
        Assert.Contains("оплатам из кассы", pdfLine, StringComparison.Ordinal);
        Assert.Contains("сдаче кассы в банк", pdfLine, StringComparison.Ordinal);
        Assert.Contains("сборам", pdfLine, StringComparison.Ordinal);
        Assert.Contains("изменению фондов", pdfLine, StringComparison.Ordinal);
        Assert.Contains("docs/pdf-export-verification.md", pdfLine, StringComparison.Ordinal);
        Assert.Contains("PdfExportRoadmapItemIsCompleteWhenCurrentReportDocumentsRoutesAndUiClientsAreCovered", pdfLine, StringComparison.Ordinal);

        Assert.Contains("POST /api/reports/consolidated/export/pdf", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/income/export/pdf", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/expense/export/pdf", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/cash-payments/export/pdf", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/bank-deposits/export/pdf", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/fees/export/pdf", verification, StringComparison.Ordinal);
        Assert.Contains("POST /api/reports/fund-changes/export/pdf", verification, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не нужна", verification, StringComparison.Ordinal);

        Assert.Contains("BuildPdf", pdfBuilderText, StringComparison.Ordinal);
        Assert.Contains("ExportConsolidatedReportPdfAsync_ReturnsDocumentWithMonthlyAndGarageRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportConsolidatedReportPdfAsync_AppliesGarageSearchFilter", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportIncomeReportPdfAsync_ReturnsDocumentWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportExpenseReportPdfAsync_ReturnsDocumentWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportCashPaymentReportPdfAsync_ReturnsDocumentWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportBankDepositReportPdfAsync_ReturnsDocumentWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportFeeReportPdfAsync_ReturnsDocumentWithTotals", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("ExportFundChangeReportPdfAsync_ReturnsDocumentWithFilteredRows", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("AssertPdfContains", reportServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("AssertPdfDoesNotContain", reportServiceTestsText, StringComparison.Ordinal);

        Assert.Contains("ExportReportActions_UsePostBecauseExportsWriteAuditEvents", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("consolidated/export/pdf", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("income/export/pdf", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("expense/export/pdf", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("fund-changes/export/pdf", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("cash-payments/export/pdf", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("bank-deposits/export/pdf", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("fees/export/pdf", controllerTestsText, StringComparison.Ordinal);

        Assert.Contains("exportConsolidatedReportPdf('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportIncomeReportPdf('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportExpenseReportPdf('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportCashPaymentReportPdf('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportBankDepositReportPdf('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportFeeReportPdf('token'", reportsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("exportFundChangeReportPdf('token'", reportsApiTestsText, StringComparison.Ordinal);

        Assert.Contains("Скачать PDF", appTestsText, StringComparison.Ordinal);
        Assert.Contains("exportBankDepositReportPdf", appTestsText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 7 \"Реализовать экспорт PDF", historyText, StringComparison.Ordinal);
        Assert.Contains("PdfExportRoadmapItemIsCompleteWhenCurrentReportDocumentsRoutesAndUiClientsAreCovered", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportFileNameRoadmapItemIsCompleteWhenPeriodAndSnapshotReportsUseStableNames()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "export-file-name-verification.md"));
        var reportServiceText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Reports", "ReportService.cs"));
        var reportServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportServiceTests.cs"));
        var controllerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportsControllerTests.cs"));
        var xlsxVerification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "xlsx-export-verification.md"));
        var pdfVerification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "pdf-export-verification.md"));

        var fileNameLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать единый формат имени экспортируемых файлов", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Реализовать единый формат имени экспортируемых файлов", fileNameLine, StringComparison.Ordinal);
        Assert.Contains("garagebalance-{type}-{yyyyMMdd}-{yyyyMMdd}.{xlsx|pdf}", fileNameLine, StringComparison.Ordinal);
        Assert.Contains("garagebalance-fees.{xlsx|pdf}", fileNameLine, StringComparison.Ordinal);
        Assert.Contains("docs/export-file-name-verification.md", fileNameLine, StringComparison.Ordinal);
        Assert.Contains("ExportFileNameRoadmapItemIsCompleteWhenPeriodAndSnapshotReportsUseStableNames", fileNameLine, StringComparison.Ordinal);

        Assert.Contains("BuildExportFileName", reportServiceText, StringComparison.Ordinal);
        Assert.Contains("garagebalance-{reportType}-{dateFrom:yyyyMMdd}-{dateTo:yyyyMMdd}.{extension}", reportServiceText, StringComparison.Ordinal);
        Assert.Contains("BuildSnapshotExportFileName", reportServiceText, StringComparison.Ordinal);
        Assert.Contains("garagebalance-{reportType}.{extension}", reportServiceText, StringComparison.Ordinal);

        foreach (var expectedFileName in new[]
        {
            "garagebalance-consolidated-20260601-20260601.xlsx",
            "garagebalance-consolidated-20260601-20260601.pdf",
            "garagebalance-income-20260601-20260630.xlsx",
            "garagebalance-income-20260601-20260630.pdf",
            "garagebalance-expense-20260601-20260630.xlsx",
            "garagebalance-expense-20260601-20260630.pdf",
            "garagebalance-cash-payments-20260601-20260630.xlsx",
            "garagebalance-cash-payments-20260601-20260630.pdf",
            "garagebalance-bank-deposits-20260601-20260630.xlsx",
            "garagebalance-bank-deposits-20260601-20260630.pdf",
            "garagebalance-fund-changes-20260601-20260630.xlsx",
            "garagebalance-fund-changes-20260601-20260630.pdf",
            "garagebalance-fees.xlsx",
            "garagebalance-fees.pdf"
        })
        {
            Assert.Contains(expectedFileName, reportServiceTestsText, StringComparison.Ordinal);
        }

        Assert.Contains("FileDownloadName", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal(export.FileName, file.FileDownloadName)", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("Period exports", verification, StringComparison.Ordinal);
        Assert.Contains("Snapshot exports", verification, StringComparison.Ordinal);
        Assert.Contains("garagebalance-income-{yyyyMMdd}-{yyyyMMdd}.xlsx", xlsxVerification, StringComparison.Ordinal);
        Assert.Contains("garagebalance-income-{yyyyMMdd}-{yyyyMMdd}.pdf", pdfVerification, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 7 \"Реализовать единый формат имени", historyText, StringComparison.Ordinal);
        Assert.Contains("ExportFileNameRoadmapItemIsCompleteWhenPeriodAndSnapshotReportsUseStableNames", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportExportFiltersAndPermissionsRoadmapItemIsCompleteWhenExportsShareScreenContracts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "export-filter-permission-verification.md"));
        var controllerText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Controllers", "ReportsController.cs"));
        var controllerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportsControllerTests.cs"));
        var reportServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportServiceTests.cs"));
        var reportsApiText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "reportsApi.ts"));
        var reportsApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "reportsApi.test.ts"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));

        var filterPermissionLine = activeRoadmapLines.Single(line =>
            line.Contains("Проверить, что экспорт учитывает те же фильтры и права", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Проверить, что экспорт учитывает те же фильтры и права", filterPermissionLine, StringComparison.Ordinal);
        Assert.Contains("reports.read", filterPermissionLine, StringComparison.Ordinal);
        Assert.Contains("frontend query builders", filterPermissionLine, StringComparison.Ordinal);
        Assert.Contains("docs/export-filter-permission-verification.md", filterPermissionLine, StringComparison.Ordinal);
        Assert.Contains("ReportExportFiltersAndPermissionsRoadmapItemIsCompleteWhenExportsShareScreenContracts", filterPermissionLine, StringComparison.Ordinal);

        Assert.Contains("[Authorize(Policy = SystemPermissions.ReportsRead)]", controllerText, StringComparison.Ordinal);
        Assert.DoesNotContain("[AllowAnonymous]", controllerText, StringComparison.Ordinal);

        foreach (var expectedRoute in new[]
        {
            "[HttpGet(\"consolidated\")]",
            "[HttpPost(\"consolidated/export/xlsx\")]",
            "[HttpPost(\"consolidated/export/pdf\")]",
            "[HttpGet(\"income\")]",
            "[HttpPost(\"income/export/xlsx\")]",
            "[HttpPost(\"income/export/pdf\")]",
            "[HttpGet(\"expense\")]",
            "[HttpPost(\"expense/export/xlsx\")]",
            "[HttpPost(\"expense/export/pdf\")]",
            "[HttpGet(\"fund-changes\")]",
            "[HttpPost(\"fund-changes/export/xlsx\")]",
            "[HttpPost(\"fund-changes/export/pdf\")]",
            "[HttpGet(\"cash-payments\")]",
            "[HttpPost(\"cash-payments/export/xlsx\")]",
            "[HttpPost(\"cash-payments/export/pdf\")]",
            "[HttpGet(\"bank-deposits\")]",
            "[HttpPost(\"bank-deposits/export/xlsx\")]",
            "[HttpPost(\"bank-deposits/export/pdf\")]",
            "[HttpGet(\"fees\")]",
            "[HttpPost(\"fees/export/xlsx\")]",
            "[HttpPost(\"fees/export/pdf\")]"
        })
        {
            Assert.Contains(expectedRoute, controllerText, StringComparison.Ordinal);
        }

        foreach (var expectedRequest in new[]
        {
            "new ConsolidatedReportRequest(monthFrom, monthTo, search",
            "new IncomeReportRequest(",
            "garageIds ?? []",
            "ownerIds ?? []",
            "incomeTypeIds ?? []",
            "new ExpenseReportRequest(",
            "supplierIds ?? []",
            "expenseTypeIds ?? []",
            "new FundChangeReportRequest(dateFrom, dateTo, search",
            "new CashPaymentReportRequest(dateFrom, dateTo, search",
            "new BankDepositReportRequest(dateFrom, dateTo, search",
            "new FeeReportRequest(variation"
        })
        {
            Assert.Contains(expectedRequest, controllerText, StringComparison.Ordinal);
        }

        foreach (var queryBuilder in new[]
        {
            "const query = buildConsolidatedReportQuery(params)",
            "const query = buildIncomeReportQuery(params)",
            "const query = buildExpenseReportQuery(params)",
            "const query = buildFundChangeReportQuery(params)",
            "const query = buildCashPaymentReportQuery(params)",
            "const query = buildBankDepositReportQuery(params)",
            "const query = buildFeeReportQuery(params)"
        })
        {
            Assert.Contains(queryBuilder, reportsApiText, StringComparison.Ordinal);
        }

        foreach (var expectedQueryPart in new[]
        {
            "monthFrom=2026-06-01&monthTo=2026-06-01&search=12",
            "dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&garageIds=garage-1&ownerIds=owner-1&incomeTypeIds=income-1",
            "dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&supplierIds=supplier-1&expenseTypeIds=expense-1",
            "cash-payments/export/xlsx?dateFrom=2026-06-01&dateTo=2026-06-30&search=",
            "bank-deposits/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&search=",
            "fees/export/xlsx?variation=",
            "fund-changes/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&search="
        })
        {
            Assert.Contains(expectedQueryPart, reportsApiTestsText, StringComparison.Ordinal);
        }

        foreach (var expectedServiceTest in new[]
        {
            "ExportConsolidatedReportXlsxAsync_AppliesGarageSearchFilter",
            "ExportConsolidatedReportPdfAsync_AppliesGarageSearchFilter",
            "ExportIncomeReportXlsxAsync_ReturnsWorkbookWithFilteredRows",
            "ExportIncomeReportPdfAsync_ReturnsDocumentWithFilteredRows",
            "ExportExpenseReportXlsxAsync_ReturnsWorkbookWithFilteredRows",
            "ExportExpenseReportPdfAsync_ReturnsDocumentWithFilteredRows",
            "ExportCashPaymentReportXlsxAsync_ReturnsWorkbookWithFilteredRows",
            "ExportCashPaymentReportPdfAsync_ReturnsDocumentWithFilteredRows",
            "ExportBankDepositReportXlsxAsync_ReturnsWorkbookWithFilteredRows",
            "ExportBankDepositReportPdfAsync_ReturnsDocumentWithFilteredRows",
            "ExportFeeReportXlsxAsync_ReturnsWorkbookWithSummaryGaragesAndDebtors",
            "ExportFeeReportPdfAsync_ReturnsDocumentWithTotals",
            "ExportFundChangeReportXlsxAsync_ReturnsWorkbookWithFilteredRows",
            "ExportFundChangeReportPdfAsync_ReturnsDocumentWithFilteredRows"
        })
        {
            Assert.Contains(expectedServiceTest, reportServiceTestsText, StringComparison.Ordinal);
        }

        Assert.Contains("ExportReportActions_UsePostBecauseExportsWriteAuditEvents", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal(export.FileName, file.FileDownloadName)", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("exportConsolidatedReportXlsx:", appTestsText, StringComparison.Ordinal);
        Assert.Contains("exportIncomeReportXlsx:", appTestsText, StringComparison.Ordinal);
        Assert.Contains("exportExpenseReportXlsx:", appTestsText, StringComparison.Ordinal);
        Assert.Contains("downloadCashOrBankReport", File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")), StringComparison.Ordinal);

        Assert.Contains("reports.read", verification, StringComparison.Ordinal);
        Assert.Contains("общий frontend query builder", verification, StringComparison.Ordinal);
        Assert.Contains("пункт Stage 7 \"Проверить, что экспорт учитывает", historyText, StringComparison.Ordinal);
        Assert.Contains("ReportExportFiltersAndPermissionsRoadmapItemIsCompleteWhenExportsShareScreenContracts", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendReportTestsRoadmapItemIsCompleteWhenRequestsFiltersTotalsAndPermissionsAreCovered()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "backend-report-tests-verification.md"));
        var reportServiceTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportServiceTests.cs"));
        var reportsControllerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Reports", "ReportsControllerTests.cs"));
        var authorizationTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Auth", "ControllerAuthorizationCoverageTests.cs"));
        var permissionHandlerTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Auth", "PermissionAuthorizationHandlerTests.cs"));

        var backendReportTestsLine = activeRoadmapLines.Single(line =>
            line.Contains("Добавить backend-тесты отчетных запросов, фильтров, итогов и прав", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Добавить backend-тесты отчетных запросов", backendReportTestsLine, StringComparison.Ordinal);
        Assert.Contains("reports.read", backendReportTestsLine, StringComparison.Ordinal);
        Assert.Contains("permission-handler", backendReportTestsLine, StringComparison.Ordinal);
        Assert.Contains("docs/backend-report-tests-verification.md", backendReportTestsLine, StringComparison.Ordinal);
        Assert.Contains("BackendReportTestsRoadmapItemIsCompleteWhenRequestsFiltersTotalsAndPermissionsAreCovered", backendReportTestsLine, StringComparison.Ordinal);

        foreach (var expectedServiceTest in new[]
        {
            "GetConsolidatedReportAsync_ReturnsMonthlyTotalsAndGarageRows",
            "GetConsolidatedReportAsync_AppliesGarageRowLimitWithoutChangingTotals",
            "GetConsolidatedReportAsync_FiltersGarageRowsByOwnerOrGarage",
            "GetConsolidatedReportAsync_NormalizesReportPeriodToMonthStarts",
            "GetConsolidatedReportAsync_ReturnsErrorForInvalidPeriod",
            "GetIncomeReportAsync_ReturnsAccrualAndPaymentRows",
            "GetIncomeReportAsync_AppliesRowLimitWithoutChangingTotals",
            "GetIncomeReportAsync_ReturnsDebtAfterEachPayment",
            "GetIncomeReportAsync_IncludesGarageStartingBalanceAsDebt",
            "GetIncomeReportAsync_FiltersByOwnerIncomeTypeAndRowMode",
            "GetIncomeReportAsync_ReturnsErrorForInvalidPeriod",
            "GetExpenseReportAsync_ReturnsPaymentRows",
            "GetExpenseReportAsync_AppliesRowLimitWithoutChangingTotals",
            "GetExpenseReportAsync_IncludesSupplierStartingBalanceAsObligation",
            "GetExpenseReportAsync_FiltersBySupplierExpenseTypeAndSearch",
            "GetExpenseReportAsync_ReturnsSupplierAccrualRows",
            "GetExpenseReportAsync_ReturnsErrorForInvalidPeriod",
            "GetFundChangeReportAsync_ReturnsFundOperationsAndWritesAudit",
            "GetCashPaymentReportAsync_ReturnsExpenseRowsAndWritesAudit",
            "GetBankDepositReportAsync_ReturnsDepositRowsAndWritesAudit",
            "GetFeeReportAsync_ReturnsSummaryDebtorsAndWritesAudit",
            "GetFeeReportAsync_UsesFeeCampaignGoalAndTargetAmountWhenCampaignExists",
            "ExportIncomeReportXlsxAsync_ReturnsWorkbookWithFilteredRows",
            "ExportIncomeReportPdfAsync_ReturnsDocumentWithFilteredRows",
            "ExportExpenseReportXlsxAsync_ReturnsWorkbookWithFilteredRows",
            "ExportExpenseReportPdfAsync_ReturnsDocumentWithFilteredRows",
            "ExportCashPaymentReportXlsxAsync_ReturnsWorkbookWithFilteredRows",
            "ExportCashPaymentReportPdfAsync_ReturnsDocumentWithFilteredRows",
            "ExportBankDepositReportXlsxAsync_ReturnsWorkbookWithFilteredRows",
            "ExportBankDepositReportPdfAsync_ReturnsDocumentWithFilteredRows",
            "ExportFeeReportXlsxAsync_ReturnsWorkbookWithSummaryGaragesAndDebtors",
            "ExportFeeReportPdfAsync_ReturnsDocumentWithTotals",
            "ExportFundChangeReportXlsxAsync_ReturnsWorkbookWithFilteredRows",
            "ExportFundChangeReportPdfAsync_ReturnsDocumentWithFilteredRows",
            "ExportIncomeReportXlsxAsync_WritesGeneratedAndExportedAuditWithoutRawSearch"
        })
        {
            Assert.Contains(expectedServiceTest, reportServiceTestsText, StringComparison.Ordinal);
        }

        foreach (var expectedControllerTest in new[]
        {
            "ExportReportActions_UsePostBecauseExportsWriteAuditEvents",
            "GetConsolidatedReport_ReturnsOk",
            "GetConsolidatedReport_ReturnsBadRequestForInvalidPeriod",
            "GetIncomeReport_ReturnsOk",
            "GetIncomeReport_ReturnsBadRequestForInvalidPeriod",
            "GetExpenseReport_ReturnsOk",
            "GetExpenseReport_ReturnsBadRequestForInvalidPeriod",
            "GetFundChangeReport_ReturnsOk",
            "GetFundChangeReport_ReturnsBadRequestForInvalidPeriod",
            "GetCashPaymentReport_ReturnsOk",
            "GetCashPaymentReport_ReturnsBadRequestForInvalidPeriod",
            "GetBankDepositReport_ReturnsOk",
            "GetBankDepositReport_ReturnsBadRequestForInvalidPeriod",
            "GetFeeReport_ReturnsOk",
            "ExportFeeReportXlsx_ReturnsFile",
            "ExportFeeReportPdf_ReturnsFile",
            "Assert.Equal(export.FileName, file.FileDownloadName)"
        })
        {
            Assert.Contains(expectedControllerTest, reportsControllerTestsText, StringComparison.Ordinal);
        }

        Assert.Contains("ReportActionsRequireReportsReadPermission", authorizationTestsText, StringComparison.Ordinal);
        Assert.Contains("SystemPermissions.ReportsRead", authorizationTestsText, StringComparison.Ordinal);
        Assert.Contains("HandleAsync_SucceedsForReportsReadPermission", permissionHandlerTestsText, StringComparison.Ordinal);
        Assert.Contains("HandleAsync_DoesNotSucceedForReportsReadWhenPermissionClaimIsMissing", permissionHandlerTestsText, StringComparison.Ordinal);

        Assert.Contains("Runtime 401/403 через полный HTTP pipeline можно расширить", verification, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не нужна", verification, StringComparison.Ordinal);
        Assert.Contains("пункт Stage 7 \"Добавить backend-тесты отчетных запросов", historyText, StringComparison.Ordinal);
        Assert.Contains("BackendReportTestsRoadmapItemIsCompleteWhenRequestsFiltersTotalsAndPermissionsAreCovered", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendReportTestsRoadmapItemIsCompleteWhenFiltersStatesExportsAndErrorsAreCovered()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "frontend-report-tests-verification.md"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var reportsApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "reportsApi.test.ts"));
        var reportFiltersTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "reportFilters.test.ts"));

        var frontendReportTestsLine = activeRoadmapLines.Single(line =>
            line.Contains("Добавить React-тесты фильтров, мультивыбора, итоговых строк", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Добавить React-тесты фильтров", frontendReportTestsLine, StringComparison.Ordinal);
        Assert.Contains("docs/frontend-report-tests-verification.md", frontendReportTestsLine, StringComparison.Ordinal);
        Assert.Contains("FrontendReportTestsRoadmapItemIsCompleteWhenFiltersStatesExportsAndErrorsAreCovered", frontendReportTestsLine, StringComparison.Ordinal);
        Assert.Contains("ошибки XLSX/PDF-выгрузки без ложного статуса готовности", frontendReportTestsLine, StringComparison.Ordinal);

        foreach (var expectedAppCoverage in new[]
        {
            "shows report workbook tabs with Excel-like filters and tables",
            "shows daily, fee and fund report filters with quick period buttons",
            "keeps report export errors visible without announcing a ready file",
            "keeps reports closed without dictionary read permission for filters",
            "Консолидированный отчёт",
            "Отчет по гаражам",
            "Отчет по выплатам",
            "Отчет по поступлениям",
            "Отчет по оплатам из кассы",
            "Отчет по сдаче кассы в банк",
            "Отчет по сборам",
            "Отчет по изменению фондов",
            "Сгруппировать начисления",
            "Показать должников",
            "Только должники",
            "Скачать XLSX",
            "Скачать PDF",
            "XLSX отчета временно недоступен",
            "queryByText('Отчет XLSX готов.')",
            "exportCashPaymentReportXlsx",
            "exportBankDepositReportPdf",
            "exportFeeReportXlsx",
            "exportFundChangeReportXlsx"
        })
        {
            Assert.Contains(expectedAppCoverage, appTestsText, StringComparison.Ordinal);
        }

        foreach (var expectedApiCoverage in new[]
        {
            "downloads report exports through POST because the backend records audit events",
            "loads cash, bank and fee reports through dedicated filtered endpoints",
            "monthFrom=2026-06-01&monthTo=2026-06-01&search=12",
            "garageIds=garage-1&ownerIds=owner-1&incomeTypeIds=income-1",
            "supplierIds=supplier-1&expenseTypeIds=expense-1",
            "cash-payments/export/xlsx",
            "bank-deposits/export/pdf",
            "fees/export/xlsx",
            "fund-changes/export/pdf"
        })
        {
            Assert.Contains(expectedApiCoverage, reportsApiTestsText, StringComparison.Ordinal);
        }

        foreach (var expectedFilterCoverage in new[]
        {
            "creates default filters for all report tabs",
            "loads saved report filters and normalizes unsafe values",
            "falls back to defaults for missing or malformed saved filters",
            "saves report filters under stable session storage keys",
            "garageIds: ['g1']",
            "ownerIds: ['o1']",
            "incomeTypeIds: ['i1']",
            "supplierIds: ['s1']",
            "expenseTypeIds: ['e1']",
            "rowMode: 'payments'"
        })
        {
            Assert.Contains(expectedFilterCoverage, reportFiltersTestsText, StringComparison.Ordinal);
        }

        Assert.Contains("negative-сценарий", verification, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не нужна", verification, StringComparison.Ordinal);
        Assert.Contains("пункт Stage 7 \"Добавить React-тесты фильтров", historyText, StringComparison.Ordinal);
        Assert.Contains("FrontendReportTestsRoadmapItemIsCompleteWhenFiltersStatesExportsAndErrorsAreCovered", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageSevenReportPerformanceCheckRequiresRealPostgresAcceptance()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "report-performance-verification.md"));
        var reportService = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Reports", "ReportService.cs"));
        var performanceGuards = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Deployment", "BackendPerformanceGuardTests.cs"));

        var performanceLine = activeRoadmapLines.Single(line =>
            line.Contains("Проверить производительность отчетов на периодах в несколько месяцев", StringComparison.Ordinal));

        Assert.StartsWith("- `[acceptance]`", performanceLine, StringComparison.Ordinal);
        Assert.Contains("docs/report-performance-verification.md", performanceLine, StringComparison.Ordinal);
        Assert.Contains("BackendPerformanceGuardTests", performanceLine, StringComparison.Ordinal);
        Assert.Contains("ApplyReportRowLimit", performanceLine, StringComparison.Ordinal);
        Assert.Contains("Take(NormalizeReportLimit(...))", performanceLine, StringComparison.Ordinal);
        Assert.Contains("CountAsync", performanceLine, StringComparison.Ordinal);
        Assert.Contains("SumAsync", performanceLine, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=False", performanceLine, StringComparison.Ordinal);
        Assert.Contains("psql=False", performanceLine, StringComparison.Ordinal);
        Assert.Contains("docker=False", performanceLine, StringComparison.Ordinal);
        Assert.Contains("StageSevenReportPerformanceCheckRequiresRealPostgresAcceptance", performanceLine, StringComparison.Ordinal);
        Assert.DoesNotContain("- `[x]` Проверить производительность отчетов", performanceLine, StringComparison.Ordinal);

        Assert.Contains("GetIncomeReportWithoutSearchAsync", reportService, StringComparison.Ordinal);
        Assert.Contains("GetExpenseReportWithoutSearchAsync", reportService, StringComparison.Ordinal);
        Assert.Contains("ApplyReportRowLimit", reportService, StringComparison.Ordinal);
        Assert.Contains(".Take(NormalizeReportLimit(limit.Value))", reportService, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", reportService, StringComparison.Ordinal);
        Assert.Contains("SumAsync(", reportService, StringComparison.Ordinal);
        Assert.Contains("ScreenReportQueries_UseDatabaseLimitsForVisibleRows", performanceGuards, StringComparison.Ordinal);

        Assert.Contains("живого PostgreSQL-прогона", verification, StringComparison.Ordinal);
        Assert.Contains("EXPLAIN (ANALYZE, BUFFERS)", verification, StringComparison.Ordinal);
        Assert.Contains("browser console", verification, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=False", verification, StringComparison.Ordinal);
        Assert.Contains("psql=False", verification, StringComparison.Ordinal);
        Assert.Contains("docker=False", verification, StringComparison.Ordinal);
        Assert.Contains("пункт Stage 7 \"Проверить производительность отчетов", historyText, StringComparison.Ordinal);
        Assert.Contains("StageSevenReportPerformanceCheckRequiresRealPostgresAcceptance", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageSevenReportExtendedAcceptanceRuleIsMarkedCompleteWhenSourceMonthlyChecklistAndRoadmapSeparateManualAcceptance()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var sourceAnalysis = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "source-analysis.md"));
        var monthlyChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "monthly-cycle-checklist.md"));
        var extendedAcceptance = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "report-extended-acceptance.md"));
        var monthlyChecklistTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Deployment", "MonthlyCycleChecklistTests.cs"));

        var extendedAcceptanceLine = activeRoadmapLines.Single(line =>
            line.Contains("Для отчетов учесть расширенную приемку до одного месяца", StringComparison.Ordinal));
        var reportManualAcceptanceLine = activeRoadmapLines.Single(line =>
            line.Contains("Заказчик проверяет отчеты на месячном цикле", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]`", extendedAcceptanceLine, StringComparison.Ordinal);
        Assert.Contains("docs/source-analysis.md", extendedAcceptanceLine, StringComparison.Ordinal);
        Assert.Contains("docs/monthly-cycle-checklist.md", extendedAcceptanceLine, StringComparison.Ordinal);
        Assert.Contains("docs/report-extended-acceptance.md", extendedAcceptanceLine, StringComparison.Ordinal);
        Assert.Contains("StageSevenReportExtendedAcceptanceRuleIsMarkedCompleteWhenSourceMonthlyChecklistAndRoadmapSeparateManualAcceptance", extendedAcceptanceLine, StringComparison.Ordinal);
        Assert.Contains("отдельным `[acceptance]`-условием", extendedAcceptanceLine, StringComparison.Ordinal);

        Assert.StartsWith("- `[acceptance]`", reportManualAcceptanceLine, StringComparison.Ordinal);
        Assert.Contains("для отчетов допускается расширенная приемка до одного месяца", reportManualAcceptanceLine, StringComparison.Ordinal);
        Assert.Contains("Договоренность по отчетам", sourceAnalysis, StringComparison.Ordinal);
        Assert.Contains("расширенная приемка до одного месяца", monthlyChecklist, StringComparison.Ordinal);
        Assert.Contains("MonthlyCycleChecklistCoversRequiredOperationalSteps", monthlyChecklistTests, StringComparison.Ordinal);
        Assert.Contains("расширенная приемка до одного месяца", monthlyChecklistTests, StringComparison.Ordinal);
        Assert.Contains("замечания по отчетам могут приниматься", extendedAcceptance, StringComparison.Ordinal);
        Assert.Contains("обезличенное описание проблемы", extendedAcceptance, StringComparison.Ordinal);
        Assert.Contains("закрыт пункт Stage 7 \"Для отчетов учесть расширенную приемку", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptanceTestingMatrixRequiresManualRealDataLocalInstallAndDeploymentChecks()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var localInstallChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "local-pc-install-checklist.md"));
        var backupRestoreChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "postgres-backup-restore.md"));
        var migrationChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "migration-verification-checklist.md"));
        var vpsChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "vps-deployment-checklist.md"));

        var acceptanceLine = activeRoadmapLines.Single(line =>
            line.Contains("Acceptance checks: реальные данные Access", StringComparison.Ordinal));
        var accessImportLine = activeRoadmapLines.Single(line =>
            line.Contains("Администратор запускает dry-run импорта Access", StringComparison.Ordinal));
        var migrationLine = activeRoadmapLines.Single(line =>
            line.Contains("Проверить миграции на чистой базе и на базе после импорта", StringComparison.Ordinal));
        var localInstallLine = activeRoadmapLines.Single(line =>
            line.Contains("Подготовить инструкцию локальной установки на ПК заказчика", StringComparison.Ordinal));
        var backupRestoreLine = activeRoadmapLines.Single(line =>
            line.Contains("Проверить backup/restore PostgreSQL", StringComparison.Ordinal));
        var vpsLine = activeRoadmapLines.Single(line =>
            line.Contains("Подготовить инструкцию VPS/domain deployment", StringComparison.Ordinal));

        Assert.StartsWith("- `[acceptance]` Acceptance checks:", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("ручной приемки на копии реальной Access-БД", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("сверки ролей заказчика", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("сверки отчетов", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("локальной установки на ПК", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("Docker/VPS smoke", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("нельзя отмечать `[x]`", acceptanceLine, StringComparison.Ordinal);
        Assert.Contains("AcceptanceTestingMatrixRequiresManualRealDataLocalInstallAndDeploymentChecks", acceptanceLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[~]`", accessImportLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[!]`", migrationLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[!]`", localInstallLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[!]`", backupRestoreLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[!]`", vpsLine, StringComparison.Ordinal);

        Assert.Contains("C:\\GarageBalance", localInstallChecklist, StringComparison.Ordinal);
        Assert.Contains("check-local-postgres.ps1", localInstallChecklist, StringComparison.Ordinal);
        Assert.Contains("restore-check", backupRestoreChecklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("garagebalance_restore_check", backupRestoreChecklist, StringComparison.Ordinal);
        Assert.Contains("garagebalance_migration_clean_check", migrationChecklist, StringComparison.Ordinal);
        Assert.Contains("garagebalance_migration_import_check", migrationChecklist, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging.service", vpsChecklist, StringComparison.Ordinal);
        Assert.Contains("nginx", vpsChecklist, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("синхронизирован пункт матрицы тестирования `Acceptance checks:", historyText, StringComparison.Ordinal);
        Assert.Contains("AcceptanceTestingMatrixRequiresManualRealDataLocalInstallAndDeploymentChecks", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessReaderRoadmapItemRemainsBlockedUntilAceOrConversionSmokeReadExists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-reader-verification.md"));
        var disabledReader = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Import", "DisabledAccessImportReader.cs"));
        var userGuide = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "user-guide.md"));
        var sourceAnalysis = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "source-analysis.md"));

        var readerLine = activeRoadmapLines.Single(line =>
            line.Contains("Получить рабочий способ чтения `.accdb`", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]` Получить рабочий способ чтения `.accdb`:", readerLine, StringComparison.Ordinal);
        Assert.Contains("32-bit ODBC-драйверы", readerLine, StringComparison.Ordinal);
        Assert.Contains("ACE/DAO provider для `.accdb` не зарегистрирован", readerLine, StringComparison.Ordinal);
        Assert.Contains("Access.Application", readerLine, StringComparison.Ordinal);
        Assert.Contains("не является переносимым backend-reader", readerLine, StringComparison.Ordinal);
        Assert.Contains("docs/access-reader-verification.md", readerLine, StringComparison.Ordinal);
        Assert.Contains("smoke-read", readerLine, StringComparison.Ordinal);
        Assert.Contains("AccessReaderRoadmapItemRemainsBlockedUntilAceOrConversionSmokeReadExists", readerLine, StringComparison.Ordinal);

        Assert.Contains("Get-OdbcDriver", checklist, StringComparison.Ordinal);
        Assert.Contains("DAO.DBEngine.160", checklist, StringComparison.Ordinal);
        Assert.Contains("ACE OLE DB/ODBC provider", checklist, StringComparison.Ordinal);
        Assert.Contains("C:\\GarageBalance\\Imports", checklist, StringComparison.Ordinal);
        Assert.Contains("Не коммитить `.accdb`, `.mdb`", checklist, StringComparison.Ordinal);
        Assert.Contains("Фактическое чтение Access не подключено", disabledReader, StringComparison.Ordinal);
        Assert.Contains("ACE-драйвер, Microsoft Access или предварительная конвертация", disabledReader, StringComparison.Ordinal);
        Assert.Contains("Reader Access", userGuide, StringComparison.Ordinal);
        Assert.Contains("Access repair/compact или конвертацию", sourceAnalysis, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 5 \"Получить рабочий способ чтения `.accdb`", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessReaderRoadmapItemRemainsBlockedUntilAceOrConversionSmokeReadExists", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessWorkingCopyRoadmapItemRemainsBlockedUntilPrivateCopyChecksumAndPrivacyCheckExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-working-copy-checklist.md"));
        var gitignore = File.ReadAllText(Path.Combine(repositoryRoot, ".gitignore"));
        var privacyScript = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "verify-package-privacy.ps1"));
        var securityPolicy = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "security-data-protection.md"));

        var copyLine = activeRoadmapLines.Single(line =>
            line.Contains("Создать копию исходной Access-БД для экспериментов", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]` Создать копию исходной Access-БД", copyLine, StringComparison.Ordinal);
        Assert.Contains("Реальный `.accdb/.mdb` файл отсутствует", copyLine, StringComparison.Ordinal);
        Assert.Contains("docs/access-working-copy-checklist.md", copyLine, StringComparison.Ordinal);
        Assert.Contains("SHA-256/размера", copyLine, StringComparison.Ordinal);
        Assert.Contains("оригинал не изменялся", copyLine, StringComparison.Ordinal);
        Assert.Contains("AccessWorkingCopyRoadmapItemRemainsBlockedUntilPrivateCopyChecksumAndPrivacyCheckExist", copyLine, StringComparison.Ordinal);

        Assert.Contains("C:\\GarageBalance\\Imports", checklist, StringComparison.Ordinal);
        Assert.Contains("private-imports/", checklist, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -Algorithm SHA256", checklist, StringComparison.Ordinal);
        Assert.Contains("Copy-Item -LiteralPath", checklist, StringComparison.Ordinal);
        Assert.Contains("Оригинальный `.accdb` или `.mdb` файл не изменяется", checklist, StringComparison.Ordinal);
        Assert.Contains("infrastructure/scripts/verify-package-privacy.ps1", checklist, StringComparison.Ordinal);
        Assert.Contains("private-imports/", gitignore, StringComparison.Ordinal);
        Assert.Contains("imports/private/", gitignore, StringComparison.Ordinal);
        Assert.Contains("imports/raw/", gitignore, StringComparison.Ordinal);
        Assert.Contains(@"\.(accdb|mdb|pgdump|dump|backup|bak|db|sqlite|sqlite3)$", privacyScript, StringComparison.Ordinal);
        Assert.Contains("Оригинал Access-БД не изменять", securityPolicy, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 5 \"Создать копию исходной Access-БД", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessWorkingCopyRoadmapItemRemainsBlockedUntilPrivateCopyChecksumAndPrivacyCheckExist", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessSchemaInventoryRoadmapItemRemainsBlockedUntilPrivateReaderAndSafeInventoryExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-schema-inventory-checklist.md"));
        var sourceAnalysis = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "source-analysis.md"));
        var importService = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Import", "ImportService.cs"));
        var accessReaderChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-reader-verification.md"));
        var workingCopyChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-working-copy-checklist.md"));

        var schemaLine = activeRoadmapLines.Single(line =>
            line.Contains("Снять полную схему таблиц, связей и объемов данных", StringComparison.Ordinal));
        var readerLine = activeRoadmapLines.Single(line =>
            line.Contains("Получить рабочий способ чтения `.accdb`", StringComparison.Ordinal));
        var copyLine = activeRoadmapLines.Single(line =>
            line.Contains("Создать копию исходной Access-БД для экспериментов", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]` Снять полную схему таблиц", schemaLine, StringComparison.Ordinal);
        Assert.Contains("приватную рабочую копию Access-БД", schemaLine, StringComparison.Ordinal);
        Assert.Contains("рабочий reader/конвертацию", schemaLine, StringComparison.Ordinal);
        Assert.Contains("docs/access-schema-inventory-checklist.md", schemaLine, StringComparison.Ordinal);
        Assert.Contains("таблицами, колонками, keys/relationships/indexes", schemaLine, StringComparison.Ordinal);
        Assert.Contains("row counts", schemaLine, StringComparison.Ordinal);
        Assert.Contains("AccessSchemaInventoryRoadmapItemRemainsBlockedUntilPrivateReaderAndSafeInventoryExist", schemaLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[!]`", readerLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[!]`", copyLine, StringComparison.Ordinal);

        Assert.Contains("Список пользовательских таблиц", checklist, StringComparison.Ordinal);
        Assert.Contains("Количество строк по каждой пользовательской таблице", checklist, StringComparison.Ordinal);
        Assert.Contains("Primary keys", checklist, StringComparison.Ordinal);
        Assert.Contains("Foreign keys/relationships", checklist, StringComparison.Ordinal);
        Assert.Contains("Indexes", checklist, StringComparison.Ordinal);
        Assert.Contains("rawRowsExported=false", checklist, StringComparison.Ordinal);
        Assert.Contains("ФИО, адреса, телефоны", checklist, StringComparison.Ordinal);
        Assert.Contains("сам `.accdb/.mdb` файл", checklist, StringComparison.Ordinal);
        Assert.Contains("полноценную схему и данные нужно снимать через установленный ACE-драйвер", sourceAnalysis, StringComparison.Ordinal);
        Assert.Contains("schema_hints", importService, StringComparison.Ordinal);
        Assert.Contains("ACE OLE DB/ODBC provider", accessReaderChecklist, StringComparison.Ordinal);
        Assert.Contains("Рабочая копия реально создана в приватной папке", workingCopyChecklist, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 5 \"Снять полную схему таблиц", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessSchemaInventoryRoadmapItemRemainsBlockedUntilPrivateReaderAndSafeInventoryExist", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessPreImportBaselineRoadmapItemRemainsBlockedUntilSafeCountsAndChecksumsExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-preimport-baseline-checklist.md"));
        var schemaChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-schema-inventory-checklist.md"));
        var workingCopyChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-working-copy-checklist.md"));
        var privacyScript = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "verify-package-privacy.ps1"));

        var baselineLine = activeRoadmapLines.Single(line =>
            line.Contains("Снять контрольные суммы/количество строк по ключевым таблицам до импорта", StringComparison.Ordinal));
        var schemaLine = activeRoadmapLines.Single(line =>
            line.Contains("Снять полную схему таблиц, связей и объемов данных", StringComparison.Ordinal));
        var copyLine = activeRoadmapLines.Single(line =>
            line.Contains("Создать копию исходной Access-БД для экспериментов", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]` Снять контрольные суммы/количество строк", baselineLine, StringComparison.Ordinal);
        Assert.Contains("приватную рабочую копию Access-БД", baselineLine, StringComparison.Ordinal);
        Assert.Contains("schema inventory", baselineLine, StringComparison.Ordinal);
        Assert.Contains("row counts", baselineLine, StringComparison.Ordinal);
        Assert.Contains("checksums/агрегаты", baselineLine, StringComparison.Ordinal);
        Assert.Contains("AccessPreImportBaselineRoadmapItemRemainsBlockedUntilSafeCountsAndChecksumsExist", baselineLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[!]`", schemaLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[!]`", copyLine, StringComparison.Ordinal);

        Assert.Contains("SHA-256 и размер рабочей копии Access-БД", checklist, StringComparison.Ordinal);
        Assert.Contains("Row counts по ключевым таблицам", checklist, StringComparison.Ordinal);
        Assert.Contains("Checksum или агрегаты", checklist, StringComparison.Ordinal);
        Assert.Contains("гаражи, владельцы, платежи/поступления, выплаты, счетчики", checklist, StringComparison.Ordinal);
        Assert.Contains("rawRowsExported=false", checklist, StringComparison.Ordinal);
        Assert.Contains("ФИО, адреса, телефоны", checklist, StringComparison.Ordinal);
        Assert.Contains("номера документов и платежные строки", checklist, StringComparison.Ordinal);
        Assert.Contains("сам `.accdb/.mdb`, exports, dumps", checklist, StringComparison.Ordinal);
        Assert.Contains("Row counts и checksums есть для всех ключевых таблиц", checklist, StringComparison.Ordinal);
        Assert.Contains("Таблицы, колонки, ключи, связи, индексы и row counts сняты", schemaChecklist, StringComparison.Ordinal);
        Assert.Contains("SHA-256/размер оригинала и копии сверены", workingCopyChecklist, StringComparison.Ordinal);
        Assert.Contains(@"\.(accdb|mdb|pgdump|dump|backup|bak|db|sqlite|sqlite3)$", privacyScript, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 5 \"Снять контрольные суммы/количество строк", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessPreImportBaselineRoadmapItemRemainsBlockedUntilSafeCountsAndChecksumsExist", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessPostgreSqlMappingRoadmapItemRemainsBlockedUntilFieldLevelInventoryMappingExists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-postgresql-mapping-checklist.md"));
        var sourceAnalysis = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "source-analysis.md"));
        var schemaChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-schema-inventory-checklist.md"));
        var baselineChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-preimport-baseline-checklist.md"));
        var dbContext = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "GarageBalanceDbContext.cs"));

        var mappingLine = activeRoadmapLines.Single(line =>
            line.Contains("Составить карту соответствия Access -> PostgreSQL", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]` Составить карту соответствия Access -> PostgreSQL", mappingLine, StringComparison.Ordinal);
        Assert.Contains("docs/access-postgresql-mapping-checklist.md", mappingLine, StringComparison.Ordinal);
        Assert.Contains("field-level mapping", mappingLine, StringComparison.Ordinal);
        Assert.Contains("privacy-check", mappingLine, StringComparison.Ordinal);
        Assert.Contains("AccessPostgreSqlMappingRoadmapItemRemainsBlockedUntilFieldLevelInventoryMappingExists", mappingLine, StringComparison.Ordinal);

        Assert.Contains("предварительной", checklist, StringComparison.Ordinal);
        Assert.Contains("не считается финальной field-level mapping", checklist, StringComparison.Ordinal);
        Assert.Contains("`garage`, `Vladelci`, `vidoplati`, `VidViplat`", checklist, StringComparison.Ordinal);
        Assert.Contains("GARAGENUMBER", checklist, StringComparison.Ordinal);
        Assert.Contains("OwnerID", checklist, StringComparison.Ordinal);
        Assert.Contains("owners", checklist, StringComparison.Ordinal);
        Assert.Contains("garages", checklist, StringComparison.Ordinal);
        Assert.Contains("financial_operations", checklist, StringComparison.Ordinal);
        Assert.Contains("accruals", checklist, StringComparison.Ordinal);
        Assert.Contains("meter_readings", checklist, StringComparison.Ordinal);
        Assert.Contains("access_import_row_fingerprints", checklist, StringComparison.Ordinal);
        Assert.Contains("access_import_created_records", checklist, StringComparison.Ordinal);
        Assert.Contains("access_import_quarantine_items", checklist, StringComparison.Ordinal);
        Assert.Contains("rawRowsExported=false", checklist, StringComparison.Ordinal);
        Assert.Contains("Privacy-check подтверждает", checklist, StringComparison.Ordinal);
        Assert.Contains("Guard `AccessPostgreSqlMappingRoadmapItemRemainsBlockedUntilFieldLevelInventoryMappingExists`", checklist, StringComparison.Ordinal);

        Assert.Contains("таблицы/объекты: `garage`, `Vladelci`, `vidoplati`, `VidViplat`", sourceAnalysis, StringComparison.Ordinal);
        Assert.Contains("Таблицы, колонки, ключи, связи, индексы и row counts сняты", schemaChecklist, StringComparison.Ordinal);
        Assert.Contains("Row counts и checksums есть для всех ключевых таблиц", baselineChecklist, StringComparison.Ordinal);
        Assert.Contains("DbSet<Owner>", dbContext, StringComparison.Ordinal);
        Assert.Contains("DbSet<Garage>", dbContext, StringComparison.Ordinal);
        Assert.Contains("DbSet<FinancialOperation>", dbContext, StringComparison.Ordinal);
        Assert.Contains("DbSet<MeterReading>", dbContext, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 5 \"Составить карту соответствия Access -> PostgreSQL\"", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessPostgreSqlMappingRoadmapItemRemainsBlockedUntilFieldLevelInventoryMappingExists", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessFormsQueriesDecisionRoadmapItemRequiresInventoryAndBusinessApproval()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-forms-queries-decision-checklist.md"));
        var schemaChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-schema-inventory-checklist.md"));
        var mappingChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-postgresql-mapping-checklist.md"));

        var formsLine = activeRoadmapLines.Single(line =>
            line.Contains("Согласовать, какие старые формы/запросы Access являются только UI", StringComparison.Ordinal));

        Assert.StartsWith("- `[decision]` Согласовать, какие старые формы/запросы Access", formsLine, StringComparison.Ordinal);
        Assert.Contains("docs/access-forms-queries-decision-checklist.md", formsLine, StringComparison.Ordinal);
        Assert.Contains("ui_only", formsLine, StringComparison.Ordinal);
        Assert.Contains("business_rule", formsLine, StringComparison.Ordinal);
        Assert.Contains("подтверждение заказчика", formsLine, StringComparison.Ordinal);
        Assert.Contains("raw SQL/screenshots", formsLine, StringComparison.Ordinal);
        Assert.Contains("AccessFormsQueriesDecisionRoadmapItemRequiresInventoryAndBusinessApproval", formsLine, StringComparison.Ordinal);

        Assert.Contains("не считается финальной field-level mapping", mappingChecklist, StringComparison.Ordinal);
        Assert.Contains("Список queries/forms/reports", schemaChecklist, StringComparison.Ordinal);
        Assert.Contains("нельзя закрывать как `[x]`", checklist, StringComparison.Ordinal);
        Assert.Contains("приватного schema inventory", checklist, StringComparison.Ordinal);
        Assert.Contains("участник со стороны заказчика", checklist, StringComparison.Ordinal);
        Assert.Contains("`ui_only`", checklist, StringComparison.Ordinal);
        Assert.Contains("`report_only`", checklist, StringComparison.Ordinal);
        Assert.Contains("`business_rule`", checklist, StringComparison.Ordinal);
        Assert.Contains("`import_helper`", checklist, StringComparison.Ordinal);
        Assert.Contains("`duplicate_or_obsolete`", checklist, StringComparison.Ordinal);
        Assert.Contains("`unknown_requires_decision`", checklist, StringComparison.Ordinal);
        Assert.Contains("no SQL text/raw rows/screenshots", checklist, StringComparison.Ordinal);
        Assert.Contains("Все `business_rule` и `import_helper` объекты связаны", checklist, StringComparison.Ordinal);
        Assert.Contains("Privacy-check подтверждает", checklist, StringComparison.Ordinal);
        Assert.Contains("Guard `AccessFormsQueriesDecisionRoadmapItemRequiresInventoryAndBusinessApproval`", checklist, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 5 \"Согласовать, какие старые формы/запросы Access", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessFormsQueriesDecisionRoadmapItemRequiresInventoryAndBusinessApproval", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessDryRunImportRoadmapItemRemainsBlockedUntilLiveReaderCountsAndReportExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-dry-run-verification-checklist.md"));
        var importService = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Import", "ImportService.cs"));
        var importServiceTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportServiceTests.cs"));
        var importControllerTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportControllerTests.cs"));
        var importApi = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "importApi.ts"));

        var dryRunLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать dry-run импорт с отчетом", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]` Реализовать dry-run импорт с отчетом", dryRunLine, StringComparison.Ordinal);
        Assert.Contains("docs/access-dry-run-verification-checklist.md", dryRunLine, StringComparison.Ordinal);
        Assert.Contains("live Access reader/конвертацию", dryRunLine, StringComparison.Ordinal);
        Assert.Contains("реальные row counts/checksums", dryRunLine, StringComparison.Ordinal);
        Assert.Contains("AccessDryRunImportRoadmapItemRemainsBlockedUntilLiveReaderCountsAndReportExist", dryRunLine, StringComparison.Ordinal);

        Assert.Contains("Текущий dry-run уже проверяет файл", checklist, StringComparison.Ordinal);
        Assert.Contains("Принимается файл `.accdb`/`.mdb`", checklist, StringComparison.Ordinal);
        Assert.Contains("Рассчитывается SHA-256", checklist, StringComparison.Ordinal);
        Assert.Contains("Сохраняется JSON report", checklist, StringComparison.Ordinal);
        Assert.Contains("Пишется пошаговый run log", checklist, StringComparison.Ordinal);
        Assert.Contains("rawRowsExported=false", checklist, StringComparison.Ordinal);
        Assert.Contains("Reader/conversion returned real schema and row counts", checklist, StringComparison.Ordinal);
        Assert.Contains("Guard `AccessDryRunImportRoadmapItemRemainsBlockedUntilLiveReaderCountsAndReportExist`", checklist, StringComparison.Ordinal);

        Assert.Contains("DryRunAccessImportAsync", importService, StringComparison.Ordinal);
        Assert.Contains("ContentSha256", importService, StringComparison.Ordinal);
        Assert.Contains("ReportJson", importService, StringComparison.Ordinal);
        Assert.Contains("native_reader", importService, StringComparison.Ordinal);
        Assert.Contains("schema_hints", importService, StringComparison.Ordinal);
        Assert.Contains("AddRunLog", importService, StringComparison.Ordinal);
        Assert.Contains("DryRunAccessImportAsync_PersistsReportAndWritesAudit", importServiceTests, StringComparison.Ordinal);
        Assert.Contains("DryRunAccessImportAsync_WarnsWhenSameFileContentWasAlreadyChecked", importServiceTests, StringComparison.Ordinal);
        Assert.Contains("ExportAccessImportRunReportAsync_ReturnsJsonReportFile", importServiceTests, StringComparison.Ordinal);
        Assert.Contains("DryRunAccessImport_ReturnsBadRequestWhenFileMissing", importControllerTests, StringComparison.Ordinal);
        Assert.Contains("dryRunAccess", importApi, StringComparison.Ordinal);
        Assert.Contains("downloadAccessRunReport", importApi, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 5 \"Реализовать dry-run импорт с отчетом\"", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessDryRunImportRoadmapItemRemainsBlockedUntilLiveReaderCountsAndReportExist", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessTransferRoadmapItemRemainsBlockedUntilLiveReaderMappingAndReconciliationExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-transfer-implementation-checklist.md"));
        var mappingChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-postgresql-mapping-checklist.md"));
        var dryRunChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-dry-run-verification-checklist.md"));
        var importService = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Import", "ImportService.cs"));
        var importContracts = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Import", "ImportContracts.cs"));
        var importServiceTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportServiceTests.cs"));
        var dbContext = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "GarageBalanceDbContext.cs"));

        var transferLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать перенос справочников, гаражей, владельцев", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]` Реализовать перенос справочников", transferLine, StringComparison.Ordinal);
        Assert.Contains("docs/access-transfer-implementation-checklist.md", transferLine, StringComparison.Ordinal);
        Assert.Contains("live Access reader/конвертации", transferLine, StringComparison.Ordinal);
        Assert.Contains("field-level mapping", transferLine, StringComparison.Ordinal);
        Assert.Contains("reconciliation report", transferLine, StringComparison.Ordinal);
        Assert.Contains("pending_access_reader", transferLine, StringComparison.Ordinal);
        Assert.Contains("AccessTransferRoadmapItemRemainsBlockedUntilLiveReaderMappingAndReconciliationExist", transferLine, StringComparison.Ordinal);

        Assert.Contains("нельзя закрывать как `[x]`", checklist, StringComparison.Ordinal);
        Assert.Contains("pending_access_reader", checklist, StringComparison.Ordinal);
        Assert.Contains("access_import_row_fingerprints", checklist, StringComparison.Ordinal);
        Assert.Contains("access_import_quarantine_items", checklist, StringComparison.Ordinal);
        Assert.Contains("access_import_created_records", checklist, StringComparison.Ordinal);
        Assert.Contains("Владельцы: `Vladelci` -> `owners`", checklist, StringComparison.Ordinal);
        Assert.Contains("Гаражи: `garage` -> `garages`", checklist, StringComparison.Ordinal);
        Assert.Contains("Исторические поступления", checklist, StringComparison.Ordinal);
        Assert.Contains("Счетчики", checklist, StringComparison.Ordinal);
        Assert.Contains("deterministic idempotency key", checklist, StringComparison.Ordinal);
        Assert.Contains("Все created target records фиксируются", checklist, StringComparison.Ordinal);
        Assert.Contains("Compare imported counts with pre-import baseline", checklist, StringComparison.Ordinal);
        Assert.Contains("Guard `AccessTransferRoadmapItemRemainsBlockedUntilLiveReaderMappingAndReconciliationExist`", checklist, StringComparison.Ordinal);

        Assert.Contains("Mapping покрывает гаражи, владельцев", mappingChecklist, StringComparison.Ordinal);
        Assert.Contains("Reader/conversion returned real schema and row counts", dryRunChecklist, StringComparison.Ordinal);
        Assert.Contains("pending_access_reader", importService, StringComparison.Ordinal);
        Assert.Contains("AccessImportCreatedRecords", importService, StringComparison.Ordinal);
        Assert.Contains("AccessImportCreatedRecordDto", importContracts, StringComparison.Ordinal);
        Assert.Contains("RequestAccessImportApplyAsync_MarksRunAndWritesAuditWithBackupConfirmation", importServiceTests, StringComparison.Ordinal);
        Assert.Contains("GetAccessImportCreatedRecordsAsync_ReturnsRunRecordsWithLimit", importServiceTests, StringComparison.Ordinal);
        Assert.Contains("DbSet<AccessImportRowFingerprint>", dbContext, StringComparison.Ordinal);
        Assert.Contains("DbSet<AccessImportQuarantineItem>", dbContext, StringComparison.Ordinal);
        Assert.Contains("DbSet<AccessImportCreatedRecord>", dbContext, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 5 \"Реализовать перенос справочников", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessTransferRoadmapItemRemainsBlockedUntilLiveReaderMappingAndReconciliationExist", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessImportIdempotencyRoadmapItemRemainsBlockedUntilTransferUsesFingerprintRegistry()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-import-idempotency-checklist.md"));
        var transferChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-transfer-implementation-checklist.md"));
        var fingerprintService = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Import", "ImportFingerprintService.cs"));
        var fingerprintContracts = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Import", "ImportFingerprintContracts.cs"));
        var fingerprintTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportFingerprintServiceTests.cs"));
        var dbContext = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "GarageBalanceDbContext.cs"));
        var erd = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "data-model-erd.md"));

        var idempotencyLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать идемпотентность: повторный импорт не должен плодить дубли", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]` Реализовать идемпотентность", idempotencyLine, StringComparison.Ordinal);
        Assert.Contains("access_import_row_fingerprints", idempotencyLine, StringComparison.Ordinal);
        Assert.Contains("IImportFingerprintService", idempotencyLine, StringComparison.Ordinal);
        Assert.Contains("field-level mapping", idempotencyLine, StringComparison.Ordinal);
        Assert.Contains("canonical row hash", idempotencyLine, StringComparison.Ordinal);
        Assert.Contains("transfer flow", idempotencyLine, StringComparison.Ordinal);
        Assert.Contains("reconciliation report", idempotencyLine, StringComparison.Ordinal);
        Assert.Contains("docs/access-import-idempotency-checklist.md", idempotencyLine, StringComparison.Ordinal);
        Assert.Contains("AccessImportIdempotencyRoadmapItemRemainsBlockedUntilTransferUsesFingerprintRegistry", idempotencyLine, StringComparison.Ordinal);

        Assert.Contains("end-to-end гарантия \"повторный импорт не создает дубли\"", checklist, StringComparison.Ordinal);
        Assert.Contains("AccessImportRowFingerprint.FingerprintKey", checklist, StringComparison.Ordinal);
        Assert.Contains("IImportFingerprintService.RegisterAsync", checklist, StringComparison.Ordinal);
        Assert.Contains("Created=false", checklist, StringComparison.Ordinal);
        Assert.Contains("IImportFingerprintService.ExistsAsync", checklist, StringComparison.Ordinal);
        Assert.Contains("import.row_fingerprint_registered", checklist, StringComparison.Ordinal);
        Assert.Contains("canonical SHA-256 row hash", checklist, StringComparison.Ordinal);
        Assert.Contains("conflict policy", checklist, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL unique constraint violations", checklist, StringComparison.Ordinal);
        Assert.Contains("Second transfer run on the same working copy creates zero duplicates", checklist, StringComparison.Ordinal);
        Assert.Contains("Guard `AccessImportIdempotencyRoadmapItemRemainsBlockedUntilTransferUsesFingerprintRegistry`", checklist, StringComparison.Ordinal);

        Assert.Contains("Каждая переносимая строка получает deterministic idempotency key", transferChecklist, StringComparison.Ordinal);
        Assert.Contains("Повторный запуск не создает дубли", transferChecklist, StringComparison.Ordinal);
        Assert.Contains("BuildFingerprintKey", fingerprintService, StringComparison.Ordinal);
        Assert.Contains("RegisterImportRowFingerprintDto(false", fingerprintService, StringComparison.Ordinal);
        Assert.Contains("import.row_fingerprint_registered", fingerprintService, StringComparison.Ordinal);
        Assert.Contains("RegisterImportRowFingerprintRequest", fingerprintContracts, StringComparison.Ordinal);
        Assert.Contains("RegisterAsync_ReturnsExistingForDuplicateExternalIdWithoutSecondAudit", fingerprintTests, StringComparison.Ordinal);
        Assert.Contains("RegisterAsync_UsesRowHashWhenExternalIdIsMissing", fingerprintTests, StringComparison.Ordinal);
        Assert.Contains("DbSet<AccessImportRowFingerprint>", dbContext, StringComparison.Ordinal);
        Assert.Contains("FingerprintKey", erd, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 5 \"Реализовать идемпотентность", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessImportIdempotencyRoadmapItemRemainsBlockedUntilTransferUsesFingerprintRegistry", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessImportQuarantineRoadmapItemRemainsBlockedUntilTransferRegistersMalformedRows()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-import-quarantine-checklist.md"));
        var transferChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-transfer-implementation-checklist.md"));
        var quarantineService = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Import", "ImportQuarantineService.cs"));
        var quarantineContracts = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Import", "ImportQuarantineContracts.cs"));
        var quarantineTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportQuarantineServiceTests.cs"));
        var importControllerTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportControllerTests.cs"));
        var appTests = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var dbContext = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "GarageBalanceDbContext.cs"));
        var erd = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "data-model-erd.md"));

        var quarantineLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать quarantine/error bucket для строк", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]` Реализовать quarantine/error bucket", quarantineLine, StringComparison.Ordinal);
        Assert.Contains("access_import_quarantine_items", quarantineLine, StringComparison.Ordinal);
        Assert.Contains("IImportQuarantineService", quarantineLine, StringComparison.Ordinal);
        Assert.Contains("audit без raw snapshot", quarantineLine, StringComparison.Ordinal);
        Assert.Contains("field-level mapping", quarantineLine, StringComparison.Ordinal);
        Assert.Contains("reason-code policy", quarantineLine, StringComparison.Ordinal);
        Assert.Contains("safe snapshot policy", quarantineLine, StringComparison.Ordinal);
        Assert.Contains("transfer flow", quarantineLine, StringComparison.Ordinal);
        Assert.Contains("reconciliation report", quarantineLine, StringComparison.Ordinal);
        Assert.Contains("docs/access-import-quarantine-checklist.md", quarantineLine, StringComparison.Ordinal);
        Assert.Contains("AccessImportQuarantineRoadmapItemRemainsBlockedUntilTransferRegistersMalformedRows", quarantineLine, StringComparison.Ordinal);

        Assert.Contains("quarantine/error bucket", checklist, StringComparison.Ordinal);
        Assert.Contains("access_import_quarantine_items", checklist, StringComparison.Ordinal);
        Assert.Contains("IImportQuarantineService.RegisterAsync", checklist, StringComparison.Ordinal);
        Assert.Contains("IImportQuarantineService.ResolveAsync", checklist, StringComparison.Ordinal);
        Assert.Contains("DTO не возвращает raw `RowSnapshotJson`", checklist, StringComparison.Ordinal);
        Assert.Contains("import.quarantine_registered", checklist, StringComparison.Ordinal);
        Assert.Contains("missing owner", checklist, StringComparison.Ordinal);
        Assert.Contains("safe snapshot policy", checklist, StringComparison.Ordinal);
        Assert.Contains("Idempotency policy", checklist, StringComparison.Ordinal);
        Assert.Contains("Second transfer run", checklist, StringComparison.Ordinal);
        Assert.Contains("Guard `AccessImportQuarantineRoadmapItemRemainsBlockedUntilTransferRegistersMalformedRows`", checklist, StringComparison.Ordinal);

        Assert.Contains("Ошибочные/неоднозначные строки уходят в quarantine", transferChecklist, StringComparison.Ordinal);
        Assert.Contains("RegisterAsync_CreatesQuarantineItemAndAuditWithoutRawSnapshot", quarantineTests, StringComparison.Ordinal);
        Assert.Contains("GetOpenItemsAsync_AppliesExplicitLimit", quarantineTests, StringComparison.Ordinal);
        Assert.Contains("ResolveAsync_MarksItemResolvedAndWritesAudit", quarantineTests, StringComparison.Ordinal);
        Assert.Contains("GetOpenQuarantineItems_ReturnsItemsFromService", importControllerTests, StringComparison.Ordinal);
        Assert.Contains("ResolveQuarantineItem_ReturnsResolvedItemAndPassesActor", importControllerTests, StringComparison.Ordinal);
        Assert.Contains("RowSnapshotJson", quarantineService, StringComparison.Ordinal);
        Assert.Contains("import.quarantine_registered", quarantineService, StringComparison.Ordinal);
        Assert.Contains("import.quarantine_resolved", quarantineService, StringComparison.Ordinal);
        Assert.Contains("AccessImportQuarantineItemDto", quarantineContracts, StringComparison.Ordinal);
        Assert.Contains("quarantine", appTests, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DbSet<AccessImportQuarantineItem>", dbContext, StringComparison.Ordinal);
        Assert.Contains("публичные DTO не возвращают raw snapshot", erd, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 5 \"Реализовать quarantine/error bucket", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessImportQuarantineRoadmapItemRemainsBlockedUntilTransferRegistersMalformedRows", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessImportReconciliationReportRoadmapItemRemainsBlockedUntilLiveTransferBaselineAndReportExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-import-reconciliation-checklist.md"));
        var transferChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-transfer-implementation-checklist.md"));
        var baselineChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-preimport-baseline-checklist.md"));
        var importService = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Import", "ImportService.cs"));
        var importContracts = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Import", "ImportContracts.cs"));
        var importServiceTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportServiceTests.cs"));
        var importControllerTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportControllerTests.cs"));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTests = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var importApiTests = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "importApi.test.ts"));
        var dbContext = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "GarageBalanceDbContext.cs"));

        var reconciliationLine = activeRoadmapLines.Single(line =>
            line.Contains("После импорта сформировать сверочный отчет", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]` После импорта сформировать сверочный отчет", reconciliationLine, StringComparison.Ordinal);
        Assert.Contains("created-records registry", reconciliationLine, StringComparison.Ordinal);
        Assert.Contains("pre-import baseline", reconciliationLine, StringComparison.Ordinal);
        Assert.Contains("access_import_created_records", reconciliationLine, StringComparison.Ordinal);
        Assert.Contains("quarantine/duplicate counts", reconciliationLine, StringComparison.Ordinal);
        Assert.Contains("financial/balance totals", reconciliationLine, StringComparison.Ordinal);
        Assert.Contains("without raw sensitive rows", reconciliationLine, StringComparison.Ordinal);
        Assert.Contains("docs/access-import-reconciliation-checklist.md", reconciliationLine, StringComparison.Ordinal);
        Assert.Contains("AccessImportReconciliationReportRoadmapItemRemainsBlockedUntilLiveTransferBaselineAndReportExist", reconciliationLine, StringComparison.Ordinal);

        Assert.Contains("run history, created-records registry, quarantine and fingerprint infrastructure", checklist, StringComparison.Ordinal);
        Assert.Contains("AccessImportCreatedRecordDto", checklist, StringComparison.Ordinal);
        Assert.Contains("GetAccessImportCreatedRecordsAsync", checklist, StringComparison.Ordinal);
        Assert.Contains("Frontend import panel показывает вкладку созданных записей", checklist, StringComparison.Ordinal);
        Assert.Contains("Reconciliation report compares Access baseline counts", checklist, StringComparison.Ordinal);
        Assert.Contains("Report separates imported, skipped duplicate, quarantined", checklist, StringComparison.Ordinal);
        Assert.Contains("Report excludes raw personal, payment, address, phone, passport, bank and raw Access row data", checklist, StringComparison.Ordinal);
        Assert.Contains("Financial summary", checklist, StringComparison.Ordinal);
        Assert.Contains("Duplicate summary", checklist, StringComparison.Ordinal);
        Assert.Contains("Guard `AccessImportReconciliationReportRoadmapItemRemainsBlockedUntilLiveTransferBaselineAndReportExist`", checklist, StringComparison.Ordinal);

        Assert.Contains("Export final reconciliation report without raw personal/payment rows", transferChecklist, StringComparison.Ordinal);
        Assert.Contains("row counts", baselineChecklist, StringComparison.Ordinal);
        Assert.Contains("AccessImportCreatedRecords", importService, StringComparison.Ordinal);
        Assert.Contains("GetAccessImportCreatedRecordsAsync", importService, StringComparison.Ordinal);
        Assert.Contains("AccessImportCreatedRecordDto", importContracts, StringComparison.Ordinal);
        Assert.Contains("GetAccessImportCreatedRecordsAsync_ReturnsRunRecordsWithLimit", importServiceTests, StringComparison.Ordinal);
        Assert.Contains("GetAccessImportCreatedRecords_ReturnsRecords", importControllerTests, StringComparison.Ordinal);
        Assert.Contains("importCreatedRecordsScreenRequestLimit", appText, StringComparison.Ordinal);
        Assert.Contains("Созданные записи появятся после фактического переноса Access", appText, StringComparison.Ordinal);
        Assert.Contains("getAccessCreatedRecords", appTests, StringComparison.Ordinal);
        Assert.Contains("getAccessCreatedRecords", importApiTests, StringComparison.Ordinal);
        Assert.Contains("DbSet<AccessImportCreatedRecord>", dbContext, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 5 \"После импорта сформировать сверочный отчет", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessImportReconciliationReportRoadmapItemRemainsBlockedUntilLiveTransferBaselineAndReportExist", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessImportBackendTestsRoadmapItemRemainsBlockedUntilMappingTransferAndPostgresCoverageExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-import-backend-tests-checklist.md"));
        var transferChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-transfer-implementation-checklist.md"));
        var mappingChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-postgresql-mapping-checklist.md"));
        var importServiceTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportServiceTests.cs"));
        var importControllerTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportControllerTests.cs"));
        var fingerprintTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportFingerprintServiceTests.cs"));
        var quarantineTests = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Import", "ImportQuarantineServiceTests.cs"));

        var backendTestsLine = activeRoadmapLines.Single(line =>
            line.Contains("Добавить backend-тесты маппинга, дублей, ошибок и повторного запуска", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]` Добавить backend-тесты маппинга", backendTestsLine, StringComparison.Ordinal);
        Assert.Contains("duplicate content warning", backendTestsLine, StringComparison.Ordinal);
        Assert.Contains("created-records list", backendTestsLine, StringComparison.Ordinal);
        Assert.Contains("fingerprint/idempotency service", backendTestsLine, StringComparison.Ordinal);
        Assert.Contains("quarantine service", backendTestsLine, StringComparison.Ordinal);
        Assert.Contains("final field-level mapping", backendTestsLine, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL integration coverage", backendTestsLine, StringComparison.Ordinal);
        Assert.Contains("repeat-run tests", backendTestsLine, StringComparison.Ordinal);
        Assert.Contains("transaction/rollback/status tracking tests", backendTestsLine, StringComparison.Ordinal);
        Assert.Contains("docs/access-import-backend-tests-checklist.md", backendTestsLine, StringComparison.Ordinal);
        Assert.Contains("AccessImportBackendTestsRoadmapItemRemainsBlockedUntilMappingTransferAndPostgresCoverageExist", backendTestsLine, StringComparison.Ordinal);

        Assert.Contains("Сейчас покрыта инфраструктура вокруг импорта", checklist, StringComparison.Ordinal);
        Assert.Contains("ImportServiceTests", checklist, StringComparison.Ordinal);
        Assert.Contains("ImportControllerTests", checklist, StringComparison.Ordinal);
        Assert.Contains("ImportFingerprintServiceTests", checklist, StringComparison.Ordinal);
        Assert.Contains("ImportQuarantineServiceTests", checklist, StringComparison.Ordinal);
        Assert.Contains("Final field-level Access -> PostgreSQL mapping", checklist, StringComparison.Ordinal);
        Assert.Contains("Transfer service exists", checklist, StringComparison.Ordinal);
        Assert.Contains("Mapping tests validate required fields", checklist, StringComparison.Ordinal);
        Assert.Contains("Duplicate tests cover external id duplicates", checklist, StringComparison.Ordinal);
        Assert.Contains("Repeat-run tests prove idempotent second import", checklist, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL integration tests verify indexes", checklist, StringComparison.Ordinal);
        Assert.Contains("Guard `AccessImportBackendTestsRoadmapItemRemainsBlockedUntilMappingTransferAndPostgresCoverageExist`", checklist, StringComparison.Ordinal);

        Assert.Contains("Backend tests cover mapping, duplicates, malformed rows", transferChecklist, StringComparison.Ordinal);
        Assert.Contains("не считается финальной field-level mapping", mappingChecklist, StringComparison.Ordinal);
        Assert.Contains("duplicate_content_detected", importServiceTests, StringComparison.Ordinal);
        Assert.Contains("RequestAccessImportApplyAsync_MarksRunAndWritesAuditWithBackupConfirmation", importServiceTests, StringComparison.Ordinal);
        Assert.Contains("RequestAccessImportRollbackAsync_MarksRunAndWritesAudit", importServiceTests, StringComparison.Ordinal);
        Assert.Contains("GetAccessImportCreatedRecordsAsync_ReturnsRunRecordsWithLimit", importServiceTests, StringComparison.Ordinal);
        Assert.Contains("GetAccessImportCreatedRecords_ReturnsRecords", importControllerTests, StringComparison.Ordinal);
        Assert.Contains("RegisterAsync_ReturnsExistingForDuplicateExternalIdWithoutSecondAudit", fingerprintTests, StringComparison.Ordinal);
        Assert.Contains("RegisterAsync_UsesRowHashWhenExternalIdIsMissing", fingerprintTests, StringComparison.Ordinal);
        Assert.Contains("RegisterAsync_CreatesQuarantineItemAndAuditWithoutRawSnapshot", quarantineTests, StringComparison.Ordinal);
        Assert.Contains("ResolveAsync_MarksItemResolvedAndWritesAudit", quarantineTests, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 5 \"Добавить backend-тесты маппинга", historyText, StringComparison.Ordinal);
        Assert.Contains("AccessImportBackendTestsRoadmapItemRemainsBlockedUntilMappingTransferAndPostgresCoverageExist", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessImportFrontendTestsRoadmapItemRemainsBlockedUntilTransferWizardAndReconciliationUiExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "access-import-frontend-tests-checklist.md"));
        var appTests = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var importApiTests = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "importApi.test.ts"));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));

        var frontendTestsLine = activeRoadmapLines.Single(line =>
            line.Contains("React-", StringComparison.Ordinal)
            && line.Contains(".accdb", StringComparison.Ordinal)
            && line.Contains("live transfer wizard", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]`", frontendTestsLine, StringComparison.Ordinal);
        Assert.Contains("dry-run", frontendTestsLine, StringComparison.Ordinal);
        Assert.Contains("apply/cancel request", frontendTestsLine, StringComparison.Ordinal);
        Assert.Contains("created records", frontendTestsLine, StringComparison.Ordinal);
        Assert.Contains("quarantine UI", frontendTestsLine, StringComparison.Ordinal);
        Assert.Contains("reconciliation UI", frontendTestsLine, StringComparison.Ordinal);
        Assert.Contains("repeat-run/idempotency UX", frontendTestsLine, StringComparison.Ordinal);
        Assert.Contains("privacy", frontendTestsLine, StringComparison.Ordinal);
        Assert.Contains("docs/access-import-frontend-tests-checklist.md", frontendTestsLine, StringComparison.Ordinal);
        Assert.Contains("AccessImportFrontendTestsRoadmapItemRemainsBlockedUntilTransferWizardAndReconciliationUiExist", frontendTestsLine, StringComparison.Ordinal);

        Assert.Contains("Расширенный мастер фактического переноса", checklist, StringComparison.Ordinal);
        Assert.Contains("`App.test.tsx` covers choosing `.accdb`", checklist, StringComparison.Ordinal);
        Assert.Contains("`importApi.test.ts` covers dry-run", checklist, StringComparison.Ordinal);
        Assert.Contains("Transfer wizard confirmation", checklist, StringComparison.Ordinal);
        Assert.Contains("Reconciliation summary", checklist, StringComparison.Ordinal);
        Assert.Contains("raw sensitive Access rows are not rendered", checklist, StringComparison.Ordinal);
        Assert.Contains("Guard `AccessImportFrontendTestsRoadmapItemRemainsBlockedUntilTransferWizardAndReconciliationUiExist`", checklist, StringComparison.Ordinal);

        Assert.Contains("runs Access import dry-run and shows checks history", appTests, StringComparison.Ordinal);
        Assert.Contains("requestAccessImportApply", appTests, StringComparison.Ordinal);
        Assert.Contains("cancelAccessImportApplyRequest", appTests, StringComparison.Ordinal);
        Assert.Contains("getAccessCreatedRecords", appTests, StringComparison.Ordinal);
        Assert.Contains("quarantine", appTests, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Созданные записи появятся после фактического переноса Access", appTests, StringComparison.Ordinal);

        Assert.Contains("downloads dry-run report through POST", importApiTests, StringComparison.Ordinal);
        Assert.Contains("requests Access import apply with reason and backup confirmation", importApiTests, StringComparison.Ordinal);
        Assert.Contains("cancels Access import apply request with a required reason", importApiTests, StringComparison.Ordinal);
        Assert.Contains("getAccessCreatedRecords", importApiTests, StringComparison.Ordinal);

        Assert.Contains("importCreatedRecordsScreenRequestLimit", appText, StringComparison.Ordinal);
        Assert.Contains("loadingCreatedRecords", appText, StringComparison.Ordinal);
        Assert.Contains("quarantine", appText, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("AccessImportFrontendTestsRoadmapItemRemainsBlockedUntilTransferWizardAndReconciliationUiExist", historyText, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
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
    public void ExternalIntegrationAdaptersAreMarkedCompleteWhenInterfacesDisabledAdaptersAndDiArePresent()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var adaptersLine = activeRoadmapLines.Single(line =>
            line.Contains("Все будущие внешние интеграции изолировать адаптерами", StringComparison.Ordinal));
        var programText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        var accessReaderContract = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Import",
            "IAccessImportReader.cs"));
        var accessReaderAdapter = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Import",
            "DisabledAccessImportReader.cs"));
        var oneCFreshAdapter = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "OneCFreshSyncAdapter.cs"));
        var receiptPrintingAdapter = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "ReceiptPrintingAdapter.cs"));

        Assert.StartsWith("- `[x]`", adaptersLine, StringComparison.Ordinal);
        Assert.Contains("IAccessImportReader", adaptersLine, StringComparison.Ordinal);
        Assert.Contains("DisabledAccessImportReader", adaptersLine, StringComparison.Ordinal);
        Assert.Contains("IOneCFreshSyncAdapter", adaptersLine, StringComparison.Ordinal);
        Assert.Contains("DisabledOneCFreshSyncAdapter", adaptersLine, StringComparison.Ordinal);
        Assert.Contains("IReceiptPrintingAdapter", adaptersLine, StringComparison.Ordinal);
        Assert.Contains("DisabledReceiptPrintingAdapter", adaptersLine, StringComparison.Ordinal);
        Assert.Contains("DI-регистрации", adaptersLine, StringComparison.Ordinal);

        Assert.Contains("public interface IAccessImportReader", accessReaderContract, StringComparison.Ordinal);
        Assert.Contains("public sealed class DisabledAccessImportReader : IAccessImportReader", accessReaderAdapter, StringComparison.Ordinal);
        Assert.Contains("public interface IOneCFreshSyncAdapter", oneCFreshAdapter, StringComparison.Ordinal);
        Assert.Contains("public sealed class DisabledOneCFreshSyncAdapter : IOneCFreshSyncAdapter", oneCFreshAdapter, StringComparison.Ordinal);
        Assert.Contains("public interface IReceiptPrintingAdapter", receiptPrintingAdapter, StringComparison.Ordinal);
        Assert.Contains("public sealed class DisabledReceiptPrintingAdapter : IReceiptPrintingAdapter", receiptPrintingAdapter, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IAccessImportReader, DisabledAccessImportReader>", programText, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IOneCFreshSyncAdapter, DisabledOneCFreshSyncAdapter>", programText, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IReceiptPrintingAdapter, DisabledReceiptPrintingAdapter>", programText, StringComparison.Ordinal);
    }

    [Fact]
    public void OneCFreshTokenStorageIsMarkedCompleteWhenProtectedSecretInfrastructureExists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var tokenStorageLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать безопасное хранение токенов/доступов", StringComparison.Ordinal));
        var programText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        var domainText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Domain",
            "Integrations",
            "IntegrationSecretSetting.cs"));
        var serviceContractText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "IIntegrationSecretSettingsService.cs"));
        var serviceText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "IntegrationSecretSettingsService.cs"));
        var serviceTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "IntegrationSecretSettingsServiceTests.cs"));
        var syncServiceText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "OneCFreshSyncService.cs"));
        var migrationText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "Migrations",
            "20260624021433_IntegrationSecretSettings.cs"));
        var releaseNotesText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "AppReleases",
            "releases.json"));
        var securityDocsText = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "security-data-protection.md"));

        Assert.StartsWith("- `[x]`", tokenStorageLine, StringComparison.Ordinal);
        Assert.Contains("IntegrationSecretSetting", tokenStorageLine, StringComparison.Ordinal);
        Assert.Contains("integration_secret_settings", tokenStorageLine, StringComparison.Ordinal);
        Assert.Contains("IIntegrationSecretSettingsService", tokenStorageLine, StringComparison.Ordinal);
        Assert.Contains("ISensitiveDataProtector", tokenStorageLine, StringComparison.Ordinal);
        Assert.Contains("integration.secret_upserted", tokenStorageLine, StringComparison.Ordinal);
        Assert.Contains("OneCFresh:RefreshToken", tokenStorageLine, StringComparison.Ordinal);
        Assert.Contains("runtime-секрет", tokenStorageLine, StringComparison.Ordinal);

        Assert.Contains("public sealed class IntegrationSecretSetting", domainText, StringComparison.Ordinal);
        Assert.Contains("public string ProtectedValue", domainText, StringComparison.Ordinal);
        Assert.Contains("Task<IntegrationSecretSettingResult<string>> GetSecretAsync", serviceContractText, StringComparison.Ordinal);
        Assert.Contains("IIntegrationSecretSettingsService, IntegrationSecretSettingsService", programText, StringComparison.Ordinal);
        Assert.Contains("sensitiveDataProtector.Protect", serviceText, StringComparison.Ordinal);
        Assert.Contains("sensitiveDataProtector.Unprotect", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"integration.secret_upserted\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("[\"protectedValueState\"] = protectedValueState", serviceText, StringComparison.Ordinal);
        Assert.Contains("UpsertSecretAsync_StoresProtectedValueAndWritesAuditWithoutPlaintext", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("Assert.DoesNotContain(secret, stored.ProtectedValue", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("Assert.DoesNotContain(secret, auditEvent.MetadataJson", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("OneCFresh", syncServiceText, StringComparison.Ordinal);
        Assert.Contains("RefreshToken", syncServiceText, StringComparison.Ordinal);
        Assert.Contains("secretSettingsService.GetSecretAsync", syncServiceText, StringComparison.Ordinal);
        Assert.Contains("name: \"integration_secret_settings\"", migrationText, StringComparison.Ordinal);
        Assert.Contains("ProtectedValue = table.Column<string>", migrationText, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"0.115.0\"", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("Добавлено хранилище секретов интеграций", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("1C Fresh", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("IIntegrationSecretSettingsService", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("integration.secret_upserted", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("ISensitiveDataProtector", securityDocsText, StringComparison.Ordinal);
        Assert.Contains("IIntegrationSecretSettingsService", securityDocsText, StringComparison.Ordinal);
        Assert.Contains("gb:protected:v1:", securityDocsText, StringComparison.Ordinal);
    }

    [Fact]
    public void OneCFreshManualSyncIsMarkedCompleteWhenControllerServiceUiTestsAndReleaseNotesExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var manualSyncLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать ручной запуск синхронизации", StringComparison.Ordinal));
        var controllerText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Controllers",
            "IntegrationsController.cs"));
        var serviceText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "OneCFreshSyncService.cs"));
        var adapterText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "OneCFreshSyncAdapter.cs"));
        var serviceTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "OneCFreshSyncServiceTests.cs"));
        var controllerTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "IntegrationsControllerTests.cs"));
        var authorizationTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Auth",
            "ControllerAuthorizationCoverageTests.cs"));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var integrationsApiText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "integrationsApi.ts"));
        var integrationsApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "integrationsApi.test.ts"));
        var releaseNotesText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "AppReleases",
            "releases.json"));

        Assert.StartsWith("- `[x]`", manualSyncLine, StringComparison.Ordinal);
        Assert.Contains("POST /api/integrations/one-c-fresh/sync-runs", manualSyncLine, StringComparison.Ordinal);
        Assert.Contains("/retry", manualSyncLine, StringComparison.Ordinal);
        Assert.Contains("import.run", manualSyncLine, StringComparison.Ordinal);
        Assert.Contains("OneCFreshSyncService", manualSyncLine, StringComparison.Ordinal);
        Assert.Contains("IOneCFreshSyncAdapter", manualSyncLine, StringComparison.Ordinal);
        Assert.Contains("one_c_fresh.sync_requested", manualSyncLine, StringComparison.Ordinal);
        Assert.Contains("one_c_fresh.sync_retry_requested", manualSyncLine, StringComparison.Ordinal);
        Assert.Contains("Запустить синхронизацию", manualSyncLine, StringComparison.Ordinal);
        Assert.Contains("Повторить запрос", manualSyncLine, StringComparison.Ordinal);
        Assert.Contains("Фоновый режим не включался", manualSyncLine, StringComparison.Ordinal);

        Assert.Contains("[HttpPost(\"one-c-fresh/sync-runs\")]", controllerText, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"one-c-fresh/sync-runs/retry\")]", controllerText, StringComparison.Ordinal);
        Assert.Contains("Policy = SystemPermissions.ImportRun", controllerText, StringComparison.Ordinal);
        Assert.Contains("StartSyncAsync", controllerText, StringComparison.Ordinal);
        Assert.Contains("RetrySyncAsync", controllerText, StringComparison.Ordinal);
        Assert.Contains("secretSettingsService.GetSecretAsync", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"one_c_fresh.sync_requested\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"one_c_fresh.sync_retry_requested\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("new OneCFreshSyncAdapterRequest(refreshToken.Value", serviceText, StringComparison.Ordinal);
        Assert.Contains("pending_adapter", adapterText, StringComparison.Ordinal);
        Assert.Contains("StartSyncAsync_CreatesAuditEventWithoutPlaintextToken", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("RetrySyncAsync_CreatesSeparateAuditEventWithoutPlaintextToken", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("StartOneCFreshSync_ReturnsResultAndPassesActorUserId", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("RetryOneCFreshSync_ReturnsResultAndPassesActorUserId", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("StartOneCFreshSync", authorizationTestsText, StringComparison.Ordinal);
        Assert.Contains("RetryOneCFreshSync", authorizationTestsText, StringComparison.Ordinal);
        Assert.Contains("Запустить синхронизацию", appText, StringComparison.Ordinal);
        Assert.Contains("Повторить запрос", appText, StringComparison.Ordinal);
        Assert.Contains("startOneCFreshSync", integrationsApiText, StringComparison.Ordinal);
        Assert.Contains("retryOneCFreshSync", integrationsApiText, StringComparison.Ordinal);
        Assert.Contains("/api/integrations/one-c-fresh/sync-runs", integrationsApiText, StringComparison.Ordinal);
        Assert.Contains("/api/integrations/one-c-fresh/sync-runs/retry", integrationsApiText, StringComparison.Ordinal);
        Assert.Contains("starts 1C Fresh synchronization from settings with confirmation", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Повтор после ошибки адаптера", appTestsText, StringComparison.Ordinal);
        Assert.Contains("retryOneCFreshSync", integrationsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"0.507.0\"", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("Запуск синхронизации 1C Fresh", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"0.524.0\"", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("Повтор запроса синхронизации 1C Fresh", releaseNotesText, StringComparison.Ordinal);
    }

    [Fact]
    public void OneCFreshErrorRetryConflictHandlingIsMarkedCompleteWhenBackendUiTestsAndReleaseNotesExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var errorRetryConflictLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать обработку ошибок, повторов и конфликтов", StringComparison.Ordinal));
        var contractsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "OneCFreshSyncContracts.cs"));
        var serviceText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "OneCFreshSyncService.cs"));
        var adapterText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "OneCFreshSyncAdapter.cs"));
        var serviceTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "OneCFreshSyncServiceTests.cs"));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var integrationsApiText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "integrationsApi.ts"));
        var integrationsApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "integrationsApi.test.ts"));
        var releaseNotesText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "AppReleases",
            "releases.json"));

        Assert.StartsWith("- `[x]`", errorRetryConflictLine, StringComparison.Ordinal);
        Assert.Contains("canRetry", errorRetryConflictLine, StringComparison.Ordinal);
        Assert.Contains("hasConflict", errorRetryConflictLine, StringComparison.Ordinal);
        Assert.Contains("recoveryAction", errorRetryConflictLine, StringComparison.Ordinal);
        Assert.Contains("OneCFreshSyncServiceTests", errorRetryConflictLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", errorRetryConflictLine, StringComparison.Ordinal);
        Assert.Contains("0.535.0", errorRetryConflictLine, StringComparison.Ordinal);
        Assert.Contains("conflict resolution", errorRetryConflictLine, StringComparison.Ordinal);
        Assert.Contains("[decision]", errorRetryConflictLine, StringComparison.Ordinal);

        Assert.Contains("bool CanRetry", contractsText, StringComparison.Ordinal);
        Assert.Contains("bool HasConflict", contractsText, StringComparison.Ordinal);
        Assert.Contains("string? ErrorCode", contractsText, StringComparison.Ordinal);
        Assert.Contains("string? ExternalRunId", contractsText, StringComparison.Ordinal);
        Assert.Contains("string? RecoveryAction", contractsText, StringComparison.Ordinal);

        Assert.Contains("ClassifyAdapterResult", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"canRetry\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"hasConflict\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"recoveryAction\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"resolve_conflict\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("adapterResult.ErrorCode.Trim()", serviceText, StringComparison.Ordinal);
        Assert.Contains("Conflict(string statusMessage", adapterText, StringComparison.Ordinal);

        Assert.Contains("StartSyncAsync_WritesAdapterErrorStatusAsRetryable", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("StartSyncAsync_WritesConflictStatusWithoutRetry", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("RetrySyncAsync_CreatesSeparateAuditEventWithoutPlaintextToken", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("getOneCFreshSyncRecoveryMessage", appText, StringComparison.Ordinal);
        Assert.Contains("oneCFreshSyncResult?.canRetry", appText, StringComparison.Ordinal);
        Assert.Contains("Обнаружен конфликт синхронизации", appText, StringComparison.Ordinal);
        Assert.Contains("canRetry: boolean", integrationsApiText, StringComparison.Ordinal);
        Assert.Contains("hasConflict: boolean", integrationsApiText, StringComparison.Ordinal);
        Assert.Contains("recoveryAction: string | null", integrationsApiText, StringComparison.Ordinal);
        Assert.Contains("shows 1C Fresh retry and conflict recovery states from backend result", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Найдены конфликтующие документы 1C Fresh.", appTestsText, StringComparison.Ordinal);
        Assert.Contains("result.canRetry", integrationsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"0.535.0\"", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("Понятные статусы повторов и конфликтов 1C Fresh", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("OneCFreshErrorRetryConflictHandlingIsMarkedCompleteWhenBackendUiTestsAndReleaseNotesExist", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void OneCFreshPreviewModeIsMarkedCompleteWhenEndpointAuditUiTestsAndReleaseNotesExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var previewLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать режим предпросмотра синхронизации", StringComparison.Ordinal));
        var controllerText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Controllers",
            "IntegrationsController.cs"));
        var serviceInterfaceText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "IOneCFreshSyncService.cs"));
        var contractsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "OneCFreshSyncContracts.cs"));
        var serviceText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "OneCFreshSyncService.cs"));
        var serviceTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "OneCFreshSyncServiceTests.cs"));
        var controllerTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "IntegrationsControllerTests.cs"));
        var authorizationTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Auth",
            "ControllerAuthorizationCoverageTests.cs"));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var integrationsApiText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "integrationsApi.ts"));
        var integrationsApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "integrationsApi.test.ts"));
        var releaseNotesText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "AppReleases",
            "releases.json"));

        Assert.StartsWith("- `[x]`", previewLine, StringComparison.Ordinal);
        Assert.Contains("POST /api/integrations/one-c-fresh/sync-runs/preview", previewLine, StringComparison.Ordinal);
        Assert.Contains("PreviewSyncAsync", previewLine, StringComparison.Ordinal);
        Assert.Contains("one_c_fresh.sync_preview_requested", previewLine, StringComparison.Ordinal);
        Assert.Contains("draft_preview", previewLine, StringComparison.Ordinal);
        Assert.Contains("snapshotHash", previewLine, StringComparison.Ordinal);
        Assert.Contains("canApply=false", previewLine, StringComparison.Ordinal);
        Assert.Contains("IOneCFreshSyncAdapter", previewLine, StringComparison.Ordinal);
        Assert.Contains("0.536.0", previewLine, StringComparison.Ordinal);
        Assert.Contains("[decision]", previewLine, StringComparison.Ordinal);

        Assert.Contains("[HttpPost(\"one-c-fresh/sync-runs/preview\")]", controllerText, StringComparison.Ordinal);
        Assert.Contains("PreviewOneCFreshSync", controllerText, StringComparison.Ordinal);
        Assert.Contains("Policy = SystemPermissions.ImportRun", controllerText, StringComparison.Ordinal);
        Assert.Contains("PreviewSyncAsync", serviceInterfaceText, StringComparison.Ordinal);
        Assert.Contains("OneCFreshSyncPreviewDto", contractsText, StringComparison.Ordinal);
        Assert.Contains("OneCFreshSyncPreviewCountDto", contractsText, StringComparison.Ordinal);
        Assert.Contains("OneCFreshSyncPreviewNoticeDto", contractsText, StringComparison.Ordinal);
        Assert.Contains("\"one_c_fresh.sync_preview_requested\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"draft_preview\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"snapshotHash\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("BuildPreviewSnapshotHash", serviceText, StringComparison.Ordinal);
        var previewMethodStart = serviceText.IndexOf(
            "public async Task<OneCFreshSyncResult<OneCFreshSyncPreviewDto>> PreviewSyncAsync",
            StringComparison.Ordinal);
        var startSyncMethodStart = serviceText.IndexOf(
            "public async Task<OneCFreshSyncResult<OneCFreshSyncDto>> StartSyncAsync",
            StringComparison.Ordinal);
        Assert.True(previewMethodStart >= 0);
        Assert.True(startSyncMethodStart > previewMethodStart);
        var previewMethodText = serviceText[previewMethodStart..startSyncMethodStart];
        Assert.DoesNotContain("syncAdapter.", previewMethodText, StringComparison.Ordinal);
        Assert.Contains("PreviewSyncAsync_CreatesAuditEventWithoutCallingAdapterOrPlaintextToken", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("PreviewOneCFreshSync_ReturnsResultAndPassesActorUserId", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("PreviewOneCFreshSync", authorizationTestsText, StringComparison.Ordinal);
        Assert.Contains("previewOneCFreshSync", integrationsApiText, StringComparison.Ordinal);
        Assert.Contains("/api/integrations/one-c-fresh/sync-runs/preview", integrationsApiText, StringComparison.Ordinal);
        Assert.Contains("Подготовить предпросмотр", appText, StringComparison.Ordinal);
        Assert.Contains("Предпросмотр синхронизации 1C Fresh", appText, StringComparison.Ordinal);
        Assert.Contains("oneCFreshPreview.canApply", appText, StringComparison.Ordinal);
        Assert.Contains("previews 1C Fresh synchronization before sending changes", appTestsText, StringComparison.Ordinal);
        Assert.Contains("previewOneCFreshSync", integrationsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"0.536.0\"", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("Предпросмотр синхронизации 1C Fresh до отправки", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("OneCFreshPreviewModeIsMarkedCompleteWhenEndpointAuditUiTestsAndReleaseNotesExist", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void OneCFreshSyncDesignIsMarkedCompleteWhenModelStatusesJournalAndPreviewAreDocumented()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var syncDesignLine = activeRoadmapLines.Single(line =>
            line.Contains("Спроектировать модель синхронизации", StringComparison.Ordinal));
        var designText = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "one-c-fresh-sync-design.md"));

        Assert.StartsWith("- `[x]`", syncDesignLine, StringComparison.Ordinal);
        Assert.Contains("one-c-fresh-sync-design.md", syncDesignLine, StringComparison.Ordinal);
        Assert.Contains("IntegrationSyncRun", syncDesignLine, StringComparison.Ordinal);
        Assert.Contains("IntegrationSyncItem", syncDesignLine, StringComparison.Ordinal);
        Assert.Contains("IntegrationSyncConflict", syncDesignLine, StringComparison.Ordinal);
        Assert.Contains("preview mode", syncDesignLine, StringComparison.Ordinal);
        Assert.Contains("[decision]", syncDesignLine, StringComparison.Ordinal);

        Assert.Contains("# 1C Fresh Sync Design", designText, StringComparison.Ordinal);
        Assert.Contains("IOneCFreshSyncAdapter", designText, StringComparison.Ordinal);
        Assert.Contains("IntegrationSyncRun", designText, StringComparison.Ordinal);
        Assert.Contains("IntegrationSyncItem", designText, StringComparison.Ordinal);
        Assert.Contains("IntegrationSyncConflict", designText, StringComparison.Ordinal);
        Assert.Contains("Run statuses", designText, StringComparison.Ordinal);
        Assert.Contains("draft_preview", designText, StringComparison.Ordinal);
        Assert.Contains("preview_ready", designText, StringComparison.Ordinal);
        Assert.Contains("succeeded_with_warnings", designText, StringComparison.Ordinal);
        Assert.Contains("conflict", designText, StringComparison.Ordinal);
        Assert.Contains("Item statuses", designText, StringComparison.Ordinal);
        Assert.Contains("Preview Mode", designText, StringComparison.Ordinal);
        Assert.Contains("Preview is mandatory before applying changes", designText, StringComparison.Ordinal);
        Assert.Contains("Error, Retry And Conflict Rules", designText, StringComparison.Ordinal);
        Assert.Contains("Retry creates a new `IntegrationSyncRun`", designText, StringComparison.Ordinal);
        Assert.Contains("Conflicts block apply until resolved or skipped", designText, StringComparison.Ordinal);
        Assert.Contains("Permissions", designText, StringComparison.Ordinal);
        Assert.Contains("Journal UI", designText, StringComparison.Ordinal);
        Assert.Contains("No plaintext token", designText, StringComparison.Ordinal);

        Assert.Contains("OneCFreshSyncDesignIsMarkedCompleteWhenModelStatusesJournalAndPreviewAreDocumented", historyText, StringComparison.Ordinal);
        Assert.Contains("Decision-пункты по API 1C Fresh", historyText, StringComparison.Ordinal);
        Assert.Contains("preview до apply", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void OneCFreshBackendCoverageIsMarkedCompleteWhenClientMappingRetryErrorsAndPermissionsAreTested()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var backendCoverageLine = activeRoadmapLines.Single(line =>
            line.Contains("Добавить backend-тесты клиента, маппинга, ошибок, retry и прав", StringComparison.Ordinal));
        var adapterTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "OneCFreshSyncAdapterTests.cs"));
        var serviceTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "OneCFreshSyncServiceTests.cs"));
        var controllerTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "IntegrationsControllerTests.cs"));
        var authorizationTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Auth",
            "ControllerAuthorizationCoverageTests.cs"));

        Assert.StartsWith("- `[x]`", backendCoverageLine, StringComparison.Ordinal);
        Assert.Contains("OneCFreshSyncAdapterTests", backendCoverageLine, StringComparison.Ordinal);
        Assert.Contains("OneCFreshSyncServiceTests", backendCoverageLine, StringComparison.Ordinal);
        Assert.Contains("IntegrationsControllerTests", backendCoverageLine, StringComparison.Ordinal);
        Assert.Contains("ControllerAuthorizationCoverageTests", backendCoverageLine, StringComparison.Ordinal);
        Assert.Contains("без изменений пользовательского поведения", backendCoverageLine, StringComparison.Ordinal);

        Assert.Contains("DisabledAdapter_ReturnsPendingStatusWithoutLeakingRequestData", adapterTestsText, StringComparison.Ordinal);
        Assert.Contains("AdapterResultFactories_PreserveExternalRunConflictAndErrorMappingData", adapterTestsText, StringComparison.Ordinal);
        Assert.Contains("StartSyncAsync_ForwardsTrimmedCommentRetryFlagAndCancellationTokenToAdapter", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("StartSyncAsync_MapsStartedStatusToWatchStatusWithoutRetry", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("StartSyncAsync_MapsRetryableAdapterStatusesAndErrorCodes", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("StartSyncAsync_MapsConflictStatusFamiliesToResolutionRequired", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("PreviewSyncAsync_CreatesAuditEventWithoutCallingAdapterOrPlaintextToken", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("RetrySyncAsync_CreatesSeparateAuditEventWithoutPlaintextToken", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("StartOneCFreshSync_ReturnsResultAndPassesActorUserId", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("RetryOneCFreshSync_ReturnsResultAndPassesActorUserId", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("PreviewOneCFreshSync_ReturnsResultAndPassesActorUserId", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("StartOneCFreshSync", authorizationTestsText, StringComparison.Ordinal);
        Assert.Contains("RetryOneCFreshSync", authorizationTestsText, StringComparison.Ordinal);
        Assert.Contains("PreviewOneCFreshSync", authorizationTestsText, StringComparison.Ordinal);
        Assert.Contains("OneCFreshBackendCoverageIsMarkedCompleteWhenClientMappingRetryErrorsAndPermissionsAreTested", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationTestingMatrixIsMarkedCompleteWhenOneCFreshSecretsAndReceiptAdapterErrorsAreCovered()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var integrationTestsLine = activeRoadmapLines.Single(line =>
            line.Contains("Integration tests: 1C Fresh client mocks", StringComparison.Ordinal));
        var oneCFreshAdapterTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "OneCFreshSyncAdapterTests.cs"));
        var oneCFreshServiceTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "OneCFreshSyncServiceTests.cs"));
        var secretTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "IntegrationSecretSettingsServiceTests.cs"));
        var receiptPrintingTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "ReceiptPrintingServiceTests.cs"));
        var controllerTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "IntegrationsControllerTests.cs"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var integrationsApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "integrationsApi.test.ts"));

        Assert.StartsWith("- `[x]`", integrationTestsLine, StringComparison.Ordinal);
        Assert.Contains("OneCFreshSyncAdapterTests", integrationTestsLine, StringComparison.Ordinal);
        Assert.Contains("OneCFreshSyncServiceTests", integrationTestsLine, StringComparison.Ordinal);
        Assert.Contains("IntegrationSecretSettingsServiceTests", integrationTestsLine, StringComparison.Ordinal);
        Assert.Contains("ReceiptPrintingServiceTests", integrationTestsLine, StringComparison.Ordinal);
        Assert.Contains("device_error", integrationTestsLine, StringComparison.Ordinal);
        Assert.Contains("IsCopy", integrationTestsLine, StringComparison.Ordinal);

        Assert.Contains("DisabledAdapter_ReturnsPendingStatusWithoutLeakingRequestData", oneCFreshAdapterTestsText, StringComparison.Ordinal);
        Assert.Contains("AdapterResultFactories_PreserveExternalRunConflictAndErrorMappingData", oneCFreshAdapterTestsText, StringComparison.Ordinal);
        Assert.Contains("StartSyncAsync_ForwardsTrimmedCommentRetryFlagAndCancellationTokenToAdapter", oneCFreshServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("StartSyncAsync_MapsRetryableAdapterStatusesAndErrorCodes", oneCFreshServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("StartSyncAsync_MapsConflictStatusFamiliesToResolutionRequired", oneCFreshServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("PreviewSyncAsync_CreatesAuditEventWithoutCallingAdapterOrPlaintextToken", oneCFreshServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("RetrySyncAsync_CreatesSeparateAuditEventWithoutPlaintextToken", oneCFreshServiceTestsText, StringComparison.Ordinal);
        Assert.Contains("GetSecretAsync_ReturnsPlaintextThroughProtector", secretTestsText, StringComparison.Ordinal);
        Assert.Contains("UpsertSecretAsync_StoresProtectedValueAndWritesAuditWithoutPlaintext", secretTestsText, StringComparison.Ordinal);
        Assert.Contains("RegisterActionAsync_WritesAdapterStatusAndSafeErrorDetails", receiptPrintingTestsText, StringComparison.Ordinal);
        Assert.Contains("device_error", receiptPrintingTestsText, StringComparison.Ordinal);
        Assert.Contains("NO_CONNECTION", receiptPrintingTestsText, StringComparison.Ordinal);
        Assert.Contains("RegisterActionAsync_ReprintCreatesAuditEventWithReasonAndExternalReceiptId", receiptPrintingTestsText, StringComparison.Ordinal);
        Assert.Contains("Assert.True(result.Value.IsCopy)", receiptPrintingTestsText, StringComparison.Ordinal);
        Assert.Contains("RegisterReceiptPrintingAction_MapsServiceErrors", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("shows 1C Fresh retry and conflict recovery states from backend result", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Квитанция: reprint Отметка: КОПИЯ.", appTestsText, StringComparison.Ordinal);
        Assert.Contains("registers receipt printing actions through the operation action endpoint", integrationsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("IntegrationTestingMatrixIsMarkedCompleteWhenOneCFreshSecretsAndReceiptAdapterErrorsAreCovered", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void OneCFreshReactCoverageIsMarkedCompleteWhenSettingsLaunchStatusesAndErrorsAreTested()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var reactCoverageLine = activeRoadmapLines.Single(line =>
            line.Contains("Добавить React-тесты настроек, запуска, статусов и ошибок", StringComparison.Ordinal));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var projectWideText = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "project-wide-history-and-safety-roadmap.md"));

        Assert.StartsWith("- `[x]`", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("без раскрытия токена", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("ненастроенное состояние", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("ошибку загрузки статуса", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("ошибки preview/retry", reactCoverageLine, StringComparison.Ordinal);
        Assert.Contains("отсутствие запроса статуса без `import.run`", reactCoverageLine, StringComparison.Ordinal);

        Assert.Contains("shows safe 1C Fresh integration status in settings", appTestsText, StringComparison.Ordinal);
        Assert.Contains("shows unconfigured 1C Fresh status with synchronization controls disabled", appTestsText, StringComparison.Ordinal);
        Assert.Contains("shows 1C Fresh status loading errors without exposing synchronization actions", appTestsText, StringComparison.Ordinal);
        Assert.Contains("starts 1C Fresh synchronization from settings with confirmation", appTestsText, StringComparison.Ordinal);
        Assert.Contains("keeps 1C Fresh preview and retry dialogs open when synchronization requests fail", appTestsText, StringComparison.Ordinal);
        Assert.Contains("previews 1C Fresh synchronization before sending changes", appTestsText, StringComparison.Ordinal);
        Assert.Contains("shows 1C Fresh retry and conflict recovery states from backend result", appTestsText, StringComparison.Ordinal);
        Assert.Contains("does not request 1C Fresh status without import permission", appTestsText, StringComparison.Ordinal);
        Assert.Contains("toBeDisabled()", appTestsText, StringComparison.Ordinal);
        Assert.Contains("findByRole('alert')", appTestsText, StringComparison.Ordinal);
        Assert.Contains("toHaveValue('Проверить проблемный период')", appTestsText, StringComparison.Ordinal);
        Assert.Contains("toHaveValue('Повторить после сбоя')", appTestsText, StringComparison.Ordinal);

        Assert.Contains("React coverage закрепляет настройки 1C Fresh", projectWideText, StringComparison.Ordinal);
        Assert.Contains("OneCFreshReactCoverageIsMarkedCompleteWhenSettingsLaunchStatusesAndErrorsAreTested", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptPrintingDesignIsMarkedCompleteWhenFormsAdapterAuditPermissionsAndTestsAreDocumented()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var designLine = activeRoadmapLines.Single(line =>
            line.Contains("Спроектировать печатные формы и интеграционный слой", StringComparison.Ordinal));
        var designText = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "receipt-printing-design.md"));
        var adapterText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "ReceiptPrintingAdapter.cs"));
        var serviceText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "ReceiptPrintingService.cs"));
        var projectWideText = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "project-wide-history-and-safety-roadmap.md"));

        Assert.StartsWith("- `[x]`", designLine, StringComparison.Ordinal);
        Assert.Contains("docs/receipt-printing-design.md", designLine, StringComparison.Ordinal);
        Assert.Contains("внутреннюю квитанцию", designLine, StringComparison.Ordinal);
        Assert.Contains("фискальный чек", designLine, StringComparison.Ordinal);
        Assert.Contains("IReceiptPrintingAdapter", designLine, StringComparison.Ordinal);
        Assert.Contains("[decision]", designLine, StringComparison.Ordinal);

        Assert.Contains("# Receipt Printing Design", designText, StringComparison.Ordinal);
        Assert.Contains("Internal Receipt", designText, StringComparison.Ordinal);
        Assert.Contains("Fiscal Receipt", designText, StringComparison.Ordinal);
        Assert.Contains("IntegrationsController", designText, StringComparison.Ordinal);
        Assert.Contains("ReceiptPrintingService", designText, StringComparison.Ordinal);
        Assert.Contains("IReceiptPrintingAdapter", designText, StringComparison.Ordinal);
        Assert.Contains("ReceiptPrintingAdapterRequest", designText, StringComparison.Ordinal);
        Assert.Contains("pending_adapter", designText, StringComparison.Ordinal);
        Assert.Contains("printed", designText, StringComparison.Ordinal);
        Assert.Contains("device_error", designText, StringComparison.Ordinal);
        Assert.Contains("template_error", designText, StringComparison.Ordinal);
        Assert.Contains("fiscalization_error", designText, StringComparison.Ordinal);
        Assert.Contains("receipt.print_requested", designText, StringComparison.Ordinal);
        Assert.Contains("receipt.print_canceled", designText, StringComparison.Ordinal);
        Assert.Contains("receipt.reprint_requested", designText, StringComparison.Ordinal);
        Assert.Contains("payments.write", designText, StringComparison.Ordinal);
        Assert.Contains("Plaintext-секреты", designText, StringComparison.Ordinal);
        Assert.Contains("Test Plan", designText, StringComparison.Ordinal);
        Assert.Contains("Definition Of Done", designText, StringComparison.Ordinal);
        Assert.Contains("`[decision]` Legal scenario", designText, StringComparison.Ordinal);
        Assert.Contains("`[acceptance]` Real print", designText, StringComparison.Ordinal);

        Assert.Contains("public interface IReceiptPrintingAdapter", adapterText, StringComparison.Ordinal);
        Assert.Contains("public sealed record ReceiptPrintingAdapterRequest", adapterText, StringComparison.Ordinal);
        Assert.Contains("ReceiptPrintingAdapterResult.Pending", adapterText, StringComparison.Ordinal);
        Assert.Contains("\"receipt.print_requested\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"receipt.print_canceled\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"receipt.reprint_requested\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("payments.write", designText, StringComparison.Ordinal);
        Assert.Contains("docs/receipt-printing-design.md", projectWideText, StringComparison.Ordinal);
        Assert.Contains("ReceiptPrintingDesignIsMarkedCompleteWhenFormsAdapterAuditPermissionsAndTestsAreDocumented", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptPrintingJournalIsMarkedCompleteWhenAuditStatusesErrorsAndClientCoverageExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var journalLine = activeRoadmapLines.Single(line =>
            line.Contains("Добавить журнал печати и ошибок", StringComparison.Ordinal));
        var serviceText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "ReceiptPrintingService.cs"));
        var serviceTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "ReceiptPrintingServiceTests.cs"));
        var controllerTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "IntegrationsControllerTests.cs"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var integrationsApiTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "integrationsApi.test.ts"));
        var releaseText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "AppReleases", "releases.json"));

        Assert.StartsWith("- `[x]`", journalLine, StringComparison.Ordinal);
        Assert.Contains("общий audit-журнал", journalLine, StringComparison.Ordinal);
        Assert.Contains("receipt_printing", journalLine, StringComparison.Ordinal);
        Assert.Contains("adapterStatus", journalLine, StringComparison.Ordinal);
        Assert.Contains("deviceResponseCode", journalLine, StringComparison.Ordinal);
        Assert.Contains("externalReceiptId", journalLine, StringComparison.Ordinal);

        Assert.Contains("\"receipt.print_requested\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"receipt.print_canceled\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"receipt.reprint_requested\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"adapterStatus\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"adapterMessage\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"deviceResponseCode\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"externalReceiptId\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("RelatedGarageNumber", serviceText, StringComparison.Ordinal);
        Assert.Contains("RelatedCounterpartyName", serviceText, StringComparison.Ordinal);
        Assert.Contains("RelatedAccountingMonth", serviceText, StringComparison.Ordinal);
        Assert.Contains("RelatedDocumentNumber", serviceText, StringComparison.Ordinal);

        Assert.Contains("RegisterActionAsync_PrintCreatesAuditEventForIncomeOperation", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("RegisterActionAsync_CancelCreatesAuditEventWithReason", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("RegisterActionAsync_ReprintCreatesAuditEventWithReasonAndExternalReceiptId", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("RegisterActionAsync_WritesAdapterStatusAndSafeErrorDetails", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("device_error", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("NO_CONNECTION", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("receipt-copy-42", serviceTestsText, StringComparison.Ordinal);

        Assert.Contains("RegisterReceiptPrintingAction_ReturnsResultAndPassesActorUserId", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("RegisterReceiptPrintingAction_MapsServiceErrors", controllerTestsText, StringComparison.Ordinal);
        Assert.Contains("loads selected garage payment history from finance backend", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Сформировать квитанцию платежа", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Напечатать копию квитанции платежа", appTestsText, StringComparison.Ordinal);
        Assert.Contains("registers receipt printing actions through the operation action endpoint", integrationsApiTestsText, StringComparison.Ordinal);
        Assert.Contains("2026-07-10-receipt-printing-adapter-status", releaseText, StringComparison.Ordinal);
        Assert.Contains("2026-07-09-receipt-printing-payment-actions", releaseText, StringComparison.Ordinal);
        Assert.Contains("ReceiptPrintingJournalIsMarkedCompleteWhenAuditStatusesErrorsAndClientCoverageExist", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptPrintingReprintCopyMarkIsMarkedCompleteWhenBackendUiAuditAndReleaseNotesExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var copyLine = activeRoadmapLines.Single(line =>
            line.Contains("Реализовать повторную печать/копию с отметкой", StringComparison.Ordinal));
        var adapterText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "ReceiptPrintingAdapter.cs"));
        var serviceText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "ReceiptPrintingService.cs"));
        var serviceTestsText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Integrations",
            "ReceiptPrintingServiceTests.cs"));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var integrationsApiText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "services", "integrationsApi.ts"));
        var releaseText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "AppReleases", "releases.json"));

        Assert.StartsWith("- `[x]`", copyLine, StringComparison.Ordinal);
        Assert.Contains("`IsCopy=true`", copyLine, StringComparison.Ordinal);
        Assert.Contains("`CopyMark=КОПИЯ`", copyLine, StringComparison.Ordinal);
        Assert.Contains("Печать копии квитанции", copyLine, StringComparison.Ordinal);
        Assert.Contains("0.537.0", copyLine, StringComparison.Ordinal);

        Assert.Contains("bool IsCopy", adapterText, StringComparison.Ordinal);
        Assert.Contains("string? CopyMark", adapterText, StringComparison.Ordinal);
        Assert.Contains("\"КОПИЯ\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"isCopy\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("\"copyMark\"", serviceText, StringComparison.Ordinal);
        Assert.Contains("Копия квитанции", serviceText, StringComparison.Ordinal);
        Assert.Contains("RegisterActionAsync_ReprintCreatesAuditEventWithReasonAndExternalReceiptId", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("Assert.True(result.Value.IsCopy)", serviceTestsText, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal(\"КОПИЯ\", result.Value.CopyMark)", serviceTestsText, StringComparison.Ordinal);

        Assert.Contains("Напечатать копию квитанции?", appText, StringComparison.Ordinal);
        Assert.Contains("Отметка:", appText, StringComparison.Ordinal);
        Assert.Contains("isCopy: boolean", integrationsApiText, StringComparison.Ordinal);
        Assert.Contains("copyMark: string | null", integrationsApiText, StringComparison.Ordinal);
        Assert.Contains("Напечатать копию квитанции платежа", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Квитанция: reprint Отметка: КОПИЯ.", appTestsText, StringComparison.Ordinal);

        Assert.Contains("2026-07-11-receipt-copy-mark", releaseText, StringComparison.Ordinal);
        Assert.Contains("Отметка копии при повторной печати квитанции", releaseText, StringComparison.Ordinal);
        Assert.Contains("ReceiptPrintingReprintCopyMarkIsMarkedCompleteWhenBackendUiAuditAndReleaseNotesExist", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveShellPrototypeIsMarkedCompleteWhenNavigationSearchDialogsTablesAndResponsiveTestsExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var shellLine = activeRoadmapLines.Single(line =>
            line.Contains("Подготовить кликабельный shell", StringComparison.Ordinal));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var accessibleStatusTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "accessibleStatus.test.ts"));
        var responsiveLayoutTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "responsiveLayout.test.ts"));

        Assert.StartsWith("- `[x]`", shellLine, StringComparison.Ordinal);
        Assert.Contains("app-shell", shellLine, StringComparison.Ordinal);
        Assert.Contains("sidebar", shellLine, StringComparison.Ordinal);
        Assert.Contains("Workspace", shellLine, StringComparison.Ordinal);
        Assert.Contains("поисковые поля", shellLine, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", shellLine, StringComparison.Ordinal);
        Assert.Contains("таблицы", shellLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", shellLine, StringComparison.Ordinal);
        Assert.Contains("accessibleStatus.test.ts", shellLine, StringComparison.Ordinal);
        Assert.Contains("responsiveLayout.test.ts", shellLine, StringComparison.Ordinal);

        Assert.Contains("className={showSidebar ? `app-shell ${sidebarModeClass}`", appText, StringComparison.Ordinal);
        Assert.Contains("sidebarToggleLabel", appText, StringComparison.Ordinal);
        Assert.Contains("const navigation: NavigationItem[]", appText, StringComparison.Ordinal);
        Assert.Contains("<Workspace activeSection={effectiveActiveSection}", appText, StringComparison.Ordinal);
        Assert.Contains("aria-label={`Поиск: ${activeOption.label}`}", appText, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", appText, StringComparison.Ordinal);
        Assert.Contains("role=\"table\"", appText, StringComparison.Ordinal);
        Assert.Contains("dictionary-data-table", appText, StringComparison.Ordinal);
        Assert.Contains("report-workbook-table", appText, StringComparison.Ordinal);
        Assert.Contains("opens the workspace with users and dictionaries", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Главное меню", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Справочники", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Платежи", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Импорт", appTestsText, StringComparison.Ordinal);
        Assert.Contains("keeps sidebar topbar and dashboard icon navigation labeled titled and focusable", accessibleStatusTestsText, StringComparison.Ordinal);
        Assert.Contains("keeps detail dialogs named, described and modal", accessibleStatusTestsText, StringComparison.Ordinal);
        Assert.Contains("keeps tables scrollable focused and announced", accessibleStatusTestsText, StringComparison.Ordinal);
        Assert.Contains("collapses the main shell and data rows on tablet width", responsiveLayoutTestsText, StringComparison.Ordinal);
        Assert.Contains("keeps tall dialogs scrollable inside the viewport", responsiveLayoutTestsText, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticatedStartScreenIsMarkedCompleteWhenLoginOpensWorkspaceDashboardWithoutLandingPage()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var startScreenLine = activeRoadmapLines.Single(line =>
            line.Contains("Основной экран не должен быть landing page", StringComparison.Ordinal));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));

        Assert.StartsWith("- `[x]`", startScreenLine, StringComparison.Ordinal);
        Assert.Contains("auth-entry", startScreenLine, StringComparison.Ordinal);
        Assert.Contains("AuthGate", startScreenLine, StringComparison.Ordinal);
        Assert.Contains("app-shell", startScreenLine, StringComparison.Ordinal);
        Assert.Contains("Главные разделы", startScreenLine, StringComparison.Ordinal);
        Assert.Contains("AuthenticatedStartScreenIsMarkedCompleteWhenLoginOpensWorkspaceDashboardWithoutLandingPage", startScreenLine, StringComparison.Ordinal);

        Assert.Contains("if (!auth)", appText, StringComparison.Ordinal);
        Assert.Contains("<main className=\"auth-entry\">", appText, StringComparison.Ordinal);
        Assert.Contains("<AuthGate authClient={authClient} onAuthenticated={handleAuthenticated} />", appText, StringComparison.Ordinal);
        Assert.Contains("className={showSidebar ? `app-shell ${sidebarModeClass}`", appText, StringComparison.Ordinal);
        Assert.Contains("case 'dashboard'", appText, StringComparison.Ordinal);
        Assert.Contains("className=\"dashboard-home\"", appText, StringComparison.Ordinal);
        Assert.Contains("role=\"group\" aria-label=\"Главные разделы\"", appText, StringComparison.Ordinal);
        Assert.Contains("const dashboardTiles", appText, StringComparison.Ordinal);
        Assert.Contains("{ title: 'Платежи', section: 'payments'", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("hero", appText, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("shows auth gate before workspace is available", appTestsText, StringComparison.Ordinal);
        Assert.Contains("queryByRole('navigation', { name: 'Основные разделы' })", appTestsText, StringComparison.Ordinal);
        Assert.Contains("creates first administrator and opens the workspace with users and dictionaries", appTestsText, StringComparison.Ordinal);
        Assert.Contains("findByRole('group', { name: 'Главные разделы' })", appTestsText, StringComparison.Ordinal);
        Assert.Contains("restores authenticated workspace after page reload", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Главное меню", appTestsText, StringComparison.Ordinal);
        Assert.Contains("AuthenticatedStartScreenIsMarkedCompleteWhenLoginOpensWorkspaceDashboardWithoutLandingPage", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void MainScreenPrototypeIsMarkedCompleteWhenAuthDashboardDictionariesTariffsPaymentsReportsAndImportExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var prototypeLine = activeRoadmapLines.Single(line =>
            line.Contains("Подготовить прототип основных экранов", StringComparison.Ordinal));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));

        Assert.StartsWith("- `[x]`", prototypeLine, StringComparison.Ordinal);
        Assert.Contains("auth gate", prototypeLine, StringComparison.Ordinal);
        Assert.Contains("dashboard tiles", prototypeLine, StringComparison.Ordinal);
        Assert.Contains("Справочники", prototypeLine, StringComparison.Ordinal);
        Assert.Contains("Тарифы и сборы", prototypeLine, StringComparison.Ordinal);
        Assert.Contains("Платежи", prototypeLine, StringComparison.Ordinal);
        Assert.Contains("Отчеты", prototypeLine, StringComparison.Ordinal);
        Assert.Contains("Импорт Access", prototypeLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", prototypeLine, StringComparison.Ordinal);

        Assert.Contains("case 'dashboard'", appText, StringComparison.Ordinal);
        Assert.Contains("case 'dictionaries'", appText, StringComparison.Ordinal);
        Assert.Contains("case 'tariffsAndFees'", appText, StringComparison.Ordinal);
        Assert.Contains("case 'payments'", appText, StringComparison.Ordinal);
        Assert.Contains("case 'reports'", appText, StringComparison.Ordinal);
        Assert.Contains("case 'import'", appText, StringComparison.Ordinal);
        Assert.Contains("<DictionaryPanelV2", appText, StringComparison.Ordinal);
        Assert.Contains("<TariffsAndFeesPrototypePanel", appText, StringComparison.Ordinal);
        Assert.Contains("<FinancePanel", appText, StringComparison.Ordinal);
        Assert.Contains("<ReportPanel", appText, StringComparison.Ordinal);
        Assert.Contains("<ImportPanel", appText, StringComparison.Ordinal);

        Assert.Contains("shows auth gate before workspace is available", appTestsText, StringComparison.Ordinal);
        Assert.Contains("creates first administrator and opens the workspace with users and dictionaries", appTestsText, StringComparison.Ordinal);
        Assert.Contains("shows tariffs and fees", appTestsText, StringComparison.Ordinal);
        Assert.Contains("shows contractors tabs", appTestsText, StringComparison.Ordinal);
        Assert.Contains("shows payments prototype and opens payment form modals", appTestsText, StringComparison.Ordinal);
        Assert.Contains("shows report workbook tabs with Excel-like filters and tables", appTestsText, StringComparison.Ordinal);
        Assert.Contains("runs Access import dry-run and shows checks history", appTestsText, StringComparison.Ordinal);
    }

    [Fact]
    public void UiStatesPrototypeIsMarkedCompleteWhenEmptyErrorForbiddenAndLoadingStatesAreTested()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var statesLine = activeRoadmapLines.Single(line =>
            line.Contains("Показать состояния без данных", StringComparison.Ordinal));
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var appTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.test.tsx"));
        var accessibleStatusTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "accessibleStatus.test.ts"));
        var formFeedbackText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "formFeedback.tsx"));
        var formFeedbackTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "formFeedback.test.tsx"));

        Assert.StartsWith("- `[x]`", statesLine, StringComparison.Ordinal);
        Assert.Contains("AccessNotice", statesLine, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", statesLine, StringComparison.Ordinal);
        Assert.Contains("empty-state", statesLine, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", statesLine, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", statesLine, StringComparison.Ordinal);
        Assert.Contains("App.test.tsx", statesLine, StringComparison.Ordinal);
        Assert.Contains("accessibleStatus.test.ts", statesLine, StringComparison.Ordinal);
        Assert.Contains("formFeedback.test.tsx", statesLine, StringComparison.Ordinal);

        Assert.Contains("function AccessNotice", appText, StringComparison.Ordinal);
        Assert.Contains("Раздел недоступен", appText, StringComparison.Ordinal);
        Assert.Contains("Требуется право:", appText, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", appText, StringComparison.Ordinal);
        Assert.Contains("className=\"empty-state\"", appText, StringComparison.Ordinal);
        Assert.Contains("role=\"status\" aria-live=\"polite\"", appText, StringComparison.Ordinal);
        Assert.Contains("Загружаем историю обновлений", appText, StringComparison.Ordinal);
        Assert.Contains("Пока нет опубликованных изменений", appText, StringComparison.Ordinal);
        Assert.Contains("Пользователей пока нет", appText, StringComparison.Ordinal);
        Assert.Contains("В этом справочнике пока нет записей", appText, StringComparison.Ordinal);
        Assert.Contains("Проверок пока нет", appText, StringComparison.Ordinal);

        Assert.Contains("shows login errors without opening protected workspace", appTestsText, StringComparison.Ordinal);
        Assert.Contains("shows auth validation summary before calling backend", appTestsText, StringComparison.Ordinal);
        Assert.Contains("announces empty release notes for authenticated users", appTestsText, StringComparison.Ordinal);
        Assert.Contains("announces release notes loading status for authenticated users", appTestsText, StringComparison.Ordinal);
        Assert.Contains("shows workspace loading errors inside the related panel", appTestsText, StringComparison.Ordinal);
        Assert.Contains("announces empty user and role lists for administrators", appTestsText, StringComparison.Ordinal);
        Assert.Contains("announces empty dictionary lists", appTestsText, StringComparison.Ordinal);
        Assert.Contains("shows accessible empty states for Access import lists", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Отчеты недоступны", appTestsText, StringComparison.Ordinal);
        Assert.Contains("Нет доступа к платежам.", appTestsText, StringComparison.Ordinal);

        Assert.Contains("expect(appCss).toContain('.empty-state')", accessibleStatusTestsText, StringComparison.Ordinal);
        Assert.Contains("expect(appSource).toContain('role=\"status\" aria-live=\"polite\"')", accessibleStatusTestsText, StringComparison.Ordinal);
        Assert.Contains("export function FormError", formFeedbackText, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", formFeedbackText, StringComparison.Ordinal);
        Assert.Contains("renders form errors as alerts", formFeedbackTestsText, StringComparison.Ordinal);
        Assert.Contains("renders validation summary as a named alert", formFeedbackTestsText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageEightTestDataIsMarkedCompleteWhenDemoFixtureCoversCoreAcceptanceScenarios()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activeRoadmapLines = File
            .ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();

        var testDataLine = activeRoadmapLines.Single(line =>
            line.Contains("Подготовить набор тестовых данных", StringComparison.Ordinal));
        var testDataPath = Path.Combine(repositoryRoot, "docs", "stage-8-demo-test-data.json");
        var testDataText = File.ReadAllText(testDataPath);

        using var document = System.Text.Json.JsonDocument.Parse(testDataText);
        var root = document.RootElement;
        var owners = root.GetProperty("owners").EnumerateArray().ToArray();
        var garages = root.GetProperty("garages").EnumerateArray().ToArray();
        var suppliers = root.GetProperty("suppliers").EnumerateArray().ToArray();
        var tariffs = root.GetProperty("tariffs").EnumerateArray().ToArray();
        var accruals = root.GetProperty("accruals").EnumerateArray().ToArray();
        var payments = root.GetProperty("payments").EnumerateArray().ToArray();
        var supplierAccruals = root.GetProperty("supplierAccruals").EnumerateArray().ToArray();
        var meterReadings = root.GetProperty("meterReadings").EnumerateArray().ToArray();
        var expectedBalances = root.GetProperty("expectedBalances").EnumerateArray().ToArray();
        var demoChecklist = root.GetProperty("demoChecklist").EnumerateArray().ToArray();

        Assert.StartsWith("- `[x]`", testDataLine, StringComparison.Ordinal);
        Assert.Contains("stage-8-demo-test-data.json", testDataLine, StringComparison.Ordinal);
        Assert.Contains("гаража", testDataLine, StringComparison.Ordinal);
        Assert.Contains("владельца", testDataLine, StringComparison.Ordinal);
        Assert.Contains("поставщика", testDataLine, StringComparison.Ordinal);
        Assert.Contains("тариф", testDataLine, StringComparison.Ordinal);
        Assert.Contains("долг", testDataLine, StringComparison.Ordinal);
        Assert.Contains("переплата", testDataLine, StringComparison.Ordinal);
        Assert.Contains("счетчик", testDataLine, StringComparison.Ordinal);

        Assert.Contains("fictional", root.GetProperty("metadata").GetProperty("privacy").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(owners.Length >= 3);
        Assert.True(garages.Length >= 3);
        Assert.True(suppliers.Length >= 3);
        Assert.True(tariffs.Length >= 4);
        Assert.True(accruals.Length >= 6);
        Assert.True(payments.Length >= 3);
        Assert.True(supplierAccruals.Length >= 3);
        Assert.True(meterReadings.Length >= 3);
        Assert.True(demoChecklist.Length >= 6);

        Assert.Equal(owners.Length, owners.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(garages.Length, garages.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(suppliers.Length, suppliers.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(tariffs.Length, tariffs.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());

        Assert.Contains(garages, item => item.GetProperty("meters").EnumerateArray().Any(meter => meter.GetString() == "water"));
        Assert.Contains(garages, item => item.GetProperty("meters").EnumerateArray().Any(meter => meter.GetString() == "electricity"));
        Assert.Contains(expectedBalances, item => item.GetProperty("balanceKind").GetString() == "debt" && item.GetProperty("closingDebt").GetDecimal() > 0);
        Assert.Contains(expectedBalances, item => item.GetProperty("balanceKind").GetString() == "overpayment" && item.GetProperty("closingDebt").GetDecimal() < 0);
        Assert.Contains(expectedBalances, item => item.GetProperty("balanceKind").GetString() == "settled" && item.GetProperty("closingDebt").GetDecimal() == 0);
        Assert.All(payments, item => Assert.StartsWith("DEMO-IN-", item.GetProperty("documentNumber").GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public void StageEightAcceptanceAndFullTestRunStatusesAreConsistent()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var demoScript = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "stage-8-demo-script.md"));
        var feedbackTemplate = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "stage-8-feedback-template.md"));

        var demoLine = activeRoadmapLines.Single(line =>
            line.Contains("Провести демонстрацию на тестовых данных", StringComparison.Ordinal));
        var feedbackLine = activeRoadmapLines.Single(line =>
            line.Contains("Собрать замечания и внести их в roadmap", StringComparison.Ordinal));
        var backendRunLine = activeRoadmapLines.Single(line =>
            line.Contains("Провести полный backend test run", StringComparison.Ordinal));
        var frontendRunLine = activeRoadmapLines.Single(line =>
            line.Contains("Провести полный frontend test run", StringComparison.Ordinal));

        Assert.StartsWith("- `[acceptance]`", demoLine, StringComparison.Ordinal);
        Assert.Contains("ручной показ", demoLine, StringComparison.Ordinal);
        Assert.Contains("stage-8-demo-test-data.json", demoLine, StringComparison.Ordinal);
        Assert.Contains("stage-8-demo-script.md", demoLine, StringComparison.Ordinal);
        Assert.DoesNotContain("- `[x]` Провести демонстрацию", demoLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[acceptance]`", feedbackLine, StringComparison.Ordinal);
        Assert.Contains("список замечаний", feedbackLine, StringComparison.Ordinal);
        Assert.Contains("подтверждение, что замечаний нет", feedbackLine, StringComparison.Ordinal);
        Assert.Contains("stage-8-feedback-template.md", feedbackLine, StringComparison.Ordinal);
        Assert.Contains("StageEightAcceptanceAndFullTestRunStatusesAreConsistent", feedbackLine, StringComparison.Ordinal);
        Assert.DoesNotContain("- `[x]` Собрать замечания", feedbackLine, StringComparison.Ordinal);

        Assert.Contains("гараж 12: есть долг", demoScript, StringComparison.Ordinal);
        Assert.Contains("гараж 27: есть переплата", demoScript, StringComparison.Ordinal);
        Assert.Contains("гараж 41: целевой сбор полностью закрыт", demoScript, StringComparison.Ordinal);
        Assert.Contains("Список замечаний", demoScript, StringComparison.Ordinal);
        Assert.Contains("обезличенном виде", demoScript, StringComparison.Ordinal);
        Assert.Contains("можно закрыть как `[x]` только после фактического показа", demoScript, StringComparison.Ordinal);
        Assert.Contains("Таблица Замечаний", feedbackTemplate, StringComparison.Ordinal);
        Assert.Contains("ST8-FB-001", feedbackTemplate, StringComparison.Ordinal);
        Assert.Contains("Не переносить в Git реальные паспортные данные", feedbackTemplate, StringComparison.Ordinal);
        Assert.Contains("Каждое подтвержденное замечание переносить в roadmap", feedbackTemplate, StringComparison.Ordinal);
        Assert.Contains("можно закрыть как `[x]` только после заполненного списка замечаний", feedbackTemplate, StringComparison.Ordinal);

        Assert.StartsWith("- `[x]`", backendRunLine, StringComparison.Ordinal);
        Assert.Contains("dotnet test GarageBalance.slnx --no-restore --configuration Debug", backendRunLine, StringComparison.Ordinal);
        Assert.Contains("idempotent EF SQL", backendRunLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[x]`", frontendRunLine, StringComparison.Ordinal);
        Assert.Contains("npm run test -- --reporter=dot --maxWorkers=1 --testTimeout=180000", frontendRunLine, StringComparison.Ordinal);
        Assert.Contains("jsdom", frontendRunLine, StringComparison.Ordinal);

        Assert.Contains("StageEightAcceptanceAndFullTestRunStatusesAreConsistent", historyText, StringComparison.Ordinal);
        Assert.Contains("Демонстрация на тестовых данных и сбор замечаний переведены в `[acceptance]`", historyText, StringComparison.Ordinal);
        Assert.Contains("Провести демонстрацию на тестовых данных\" дополнительно подготовлен к ручному показу", historyText, StringComparison.Ordinal);
        Assert.Contains("Собрать замечания и внести их в roadmap\" подготовлен к ручной фиксации", historyText, StringComparison.Ordinal);
        Assert.Contains("dotnet test GarageBalance.slnx --no-restore --configuration Debug", historyText, StringComparison.Ordinal);
        Assert.Contains("npm run test -- --reporter=dot --maxWorkers=1 --testTimeout=180000", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageEightInfrastructureChecksRemainBlockedWithoutRequiredLocalTools()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var dockerComposeText = File.ReadAllText(Path.Combine(repositoryRoot, "docker-compose.yml"));
        var backendDockerfileText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Dockerfile"));
        var frontendDockerfileText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "Dockerfile"));
        var localInstallText = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "local-pc-install-checklist.md"));
        var dockerPreflightScriptText = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "check-docker-compose.ps1"));

        var dockerBuildLine = activeRoadmapLines.Single(line =>
            line.Contains("Проверить Docker Compose build", StringComparison.Ordinal));
        var localInstallLine = activeRoadmapLines.Single(line =>
            line.Contains("Проверить локальный сценарий установки без Docker", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]`", dockerBuildLine, StringComparison.Ordinal);
        Assert.Contains("docker=missing", dockerBuildLine, StringComparison.Ordinal);
        Assert.Contains("docker compose build", dockerBuildLine, StringComparison.Ordinal);
        Assert.Contains("check-docker-compose.ps1", dockerBuildLine, StringComparison.Ordinal);
        Assert.DoesNotContain("- `[x]` Проверить Docker Compose build", dockerBuildLine, StringComparison.Ordinal);
        Assert.Contains("dockerfile: backend/GarageBalance.Api/Dockerfile", dockerComposeText, StringComparison.Ordinal);
        Assert.Contains("dockerfile: frontend/Dockerfile", dockerComposeText, StringComparison.Ordinal);
        Assert.Contains("postgres:17-alpine", dockerComposeText, StringComparison.Ordinal);
        Assert.Contains("\"${API_BIND_ADDRESS:-127.0.0.1}:${API_PORT:-5080}:8080\"", dockerComposeText, StringComparison.Ordinal);
        Assert.Contains("\"${FRONTEND_BIND_ADDRESS:-127.0.0.1}:${FRONTEND_PORT:-5173}:80\"", dockerComposeText, StringComparison.Ordinal);
        Assert.Contains("dotnet publish", backendDockerfileText, StringComparison.Ordinal);
        Assert.Contains("nginx", frontendDockerfileText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-Command docker", dockerPreflightScriptText, StringComparison.Ordinal);
        Assert.Contains("dockerComposeConfig=Skipped", dockerPreflightScriptText, StringComparison.Ordinal);
        Assert.Contains("dockerComposeBuild=Skipped", dockerPreflightScriptText, StringComparison.Ordinal);
        Assert.Contains("docker compose config", dockerPreflightScriptText, StringComparison.Ordinal);
        Assert.Contains("docker compose build", dockerPreflightScriptText, StringComparison.Ordinal);
        Assert.Contains("-RequireBuild", dockerPreflightScriptText, StringComparison.Ordinal);

        Assert.StartsWith("- `[!]`", localInstallLine, StringComparison.Ordinal);
        Assert.Contains("psql/createuser/createdb", localInstallLine, StringComparison.Ordinal);
        Assert.Contains("dotnet publish", localInstallLine, StringComparison.Ordinal);
        Assert.Contains("VITE_API_BASE_URL=http://127.0.0.1:5080", localInstallLine, StringComparison.Ordinal);
        Assert.Contains("Проверить локальную PostgreSQL перед миграциями", localInstallText, StringComparison.Ordinal);
        Assert.Contains("dotnet tool run dotnet-ef database update", localInstallText, StringComparison.Ordinal);
        Assert.Contains("curl -fsS http://127.0.0.1:5080/health", localInstallText, StringComparison.Ordinal);

        Assert.Contains("StageEightInfrastructureChecksRemainBlockedWithoutRequiredLocalTools", historyText, StringComparison.Ordinal);
        Assert.Contains("docker` CLI отсутствует", historyText, StringComparison.Ordinal);
        Assert.Contains("check-docker-compose.ps1", historyText, StringComparison.Ordinal);
        Assert.Contains("psql/createuser/createdb", historyText, StringComparison.Ordinal);
        Assert.Contains("dotnet publish backend/GarageBalance.Api/GarageBalance.Api.csproj -c Release --no-restore", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageElevenDockerfilesRemainBlockedWithoutDockerCliButHaveStaticCoverage()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var backendDockerfileText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Dockerfile"));
        var frontendDockerfileText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "Dockerfile"));
        var nginxConfigText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "nginx.conf"));
        var composeText = File.ReadAllText(Path.Combine(repositoryRoot, "docker-compose.yml"));

        var dockerfileLine = activeRoadmapLines.Single(line =>
            line.Contains("Актуализировать Dockerfile API и frontend", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]`", dockerfileLine, StringComparison.Ordinal);
        Assert.Contains("BackendDockerfileTests", dockerfileLine, StringComparison.Ordinal);
        Assert.Contains("FrontendDockerfileTests", dockerfileLine, StringComparison.Ordinal);
        Assert.Contains("docker=False", dockerfileLine, StringComparison.Ordinal);
        Assert.Contains("StageElevenDockerfilesRemainBlockedWithoutDockerCliButHaveStaticCoverage", dockerfileLine, StringComparison.Ordinal);

        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build", backendDockerfileText, StringComparison.Ordinal);
        Assert.Contains("RUN dotnet publish backend/GarageBalance.Api/GarageBalance.Api.csproj -c Release -o /app/publish --no-restore", backendDockerfileText, StringComparison.Ordinal);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime", backendDockerfileText, StringComparison.Ordinal);
        Assert.Contains("ENV ASPNETCORE_URLS=http://+:8080", backendDockerfileText, StringComparison.Ordinal);

        Assert.Contains("FROM node:22-alpine AS build", frontendDockerfileText, StringComparison.Ordinal);
        Assert.Contains("RUN npm ci", frontendDockerfileText, StringComparison.Ordinal);
        Assert.Contains("RUN npm run build", frontendDockerfileText, StringComparison.Ordinal);
        Assert.Contains("FROM nginx:1.27-alpine AS runtime", frontendDockerfileText, StringComparison.Ordinal);
        Assert.Contains("COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf", frontendDockerfileText, StringComparison.Ordinal);
        Assert.Contains("Cache-Control \"public, max-age=2592000, immutable\"", nginxConfigText, StringComparison.Ordinal);
        Assert.Contains("Cache-Control \"no-store, no-cache, must-revalidate, max-age=0\" always", nginxConfigText, StringComparison.Ordinal);
        Assert.Contains("dockerfile: backend/GarageBalance.Api/Dockerfile", composeText, StringComparison.Ordinal);
        Assert.Contains("dockerfile: frontend/Dockerfile", composeText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 11 \"Актуализировать Dockerfile API и frontend\" доведен до проверяемого blocked-состояния", historyText, StringComparison.Ordinal);
        Assert.Contains("FrontendDockerfileTests.DockerfileUsesNodeBuildStageNginxRuntimeAndSpaCachePolicy", historyText, StringComparison.Ordinal);
        Assert.Contains("live `docker build`, `docker compose up --build`, container healthchecks", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageElevenDockerComposeRemainsBlockedWithoutDockerCliButHasFinalLocalStructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var composeText = File.ReadAllText(Path.Combine(repositoryRoot, "docker-compose.yml"));
        var envExampleText = File.ReadAllText(Path.Combine(repositoryRoot, ".env.example"));
        var gitIgnoreText = File.ReadAllText(Path.Combine(repositoryRoot, ".gitignore"));

        var composeLine = activeRoadmapLines.Single(line =>
            line.Contains("Актуализировать `docker-compose.yml` под финальную структуру", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]`", composeLine, StringComparison.Ordinal);
        Assert.Contains("DockerComposeTests", composeLine, StringComparison.Ordinal);
        Assert.Contains("docker=False", composeLine, StringComparison.Ordinal);
        Assert.Contains("StageElevenDockerComposeRemainsBlockedWithoutDockerCliButHasFinalLocalStructure", composeLine, StringComparison.Ordinal);

        Assert.Contains("POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?Set POSTGRES_PASSWORD in .env before running docker compose}", composeText, StringComparison.Ordinal);
        Assert.Contains("Jwt__SigningKey: ${JWT_SIGNING_KEY:?Set JWT_SIGNING_KEY in .env before running docker compose}", composeText, StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Production}", composeText, StringComparison.Ordinal);
        Assert.Contains("\"${POSTGRES_BIND_ADDRESS:-127.0.0.1}:${POSTGRES_PORT:-5432}:5432\"", composeText, StringComparison.Ordinal);
        Assert.Contains("\"${API_BIND_ADDRESS:-127.0.0.1}:${API_PORT:-5080}:8080\"", composeText, StringComparison.Ordinal);
        Assert.Contains("\"${FRONTEND_BIND_ADDRESS:-127.0.0.1}:${FRONTEND_PORT:-5173}:80\"", composeText, StringComparison.Ordinal);
        Assert.Contains("${BACKUP_HOST_PATH:-./backups}:/backups", composeText, StringComparison.Ordinal);
        Assert.Contains("condition: service_healthy", composeText, StringComparison.Ordinal);

        Assert.Contains("POSTGRES_BIND_ADDRESS=127.0.0.1", envExampleText, StringComparison.Ordinal);
        Assert.Contains("API_BIND_ADDRESS=127.0.0.1", envExampleText, StringComparison.Ordinal);
        Assert.Contains("FRONTEND_BIND_ADDRESS=127.0.0.1", envExampleText, StringComparison.Ordinal);
        Assert.Contains("FRONTEND_ORIGIN=http://127.0.0.1:5173", envExampleText, StringComparison.Ordinal);
        Assert.Contains("BACKUP_HOST_PATH=./backups", envExampleText, StringComparison.Ordinal);
        Assert.Contains("backups/", gitIgnoreText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 11 \"Актуализировать `docker-compose.yml` под финальную структуру\" доведен до проверяемого blocked-состояния", historyText, StringComparison.Ordinal);
        Assert.Contains("DockerComposeTests.ComposeDefinesServiceHealthChecksAndStartupOrder", historyText, StringComparison.Ordinal);
        Assert.Contains("live `docker compose up --build`, container healthchecks", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageElevenLocalPcInstallGuideRemainsBlockedUntilLiveLocalInstallButHasCompleteChecklist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var localInstallText = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "local-pc-install-checklist.md"));
        var preflightScriptText = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "check-local-postgres.ps1"));

        var localInstallLine = activeRoadmapLines.Single(line =>
            line.Contains("Подготовить инструкцию локальной установки на ПК заказчика", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]`", localInstallLine, StringComparison.Ordinal);
        Assert.Contains("local-pc-install-checklist.md", localInstallLine, StringComparison.Ordinal);
        Assert.Contains("docker=False", localInstallLine, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=False", localInstallLine, StringComparison.Ordinal);
        Assert.Contains("psql=False", localInstallLine, StringComparison.Ordinal);
        Assert.Contains("StageElevenLocalPcInstallGuideRemainsBlockedUntilLiveLocalInstallButHasCompleteChecklist", localInstallLine, StringComparison.Ordinal);

        Assert.Contains("http://127.0.0.1:5173", localInstallText, StringComparison.Ordinal);
        Assert.Contains("C:\\GarageBalance\\Config\\garagebalance.local.env", localInstallText, StringComparison.Ordinal);
        Assert.Contains("POSTGRES_BIND_ADDRESS=127.0.0.1", localInstallText, StringComparison.Ordinal);
        Assert.Contains("BACKUP_HOST_PATH=./backups", localInstallText, StringComparison.Ordinal);
        Assert.Contains("docker compose up --build -d", localInstallText, StringComparison.Ordinal);
        Assert.Contains("check-local-postgres.ps1", localInstallText, StringComparison.Ordinal);
        Assert.Contains("dotnet tool run dotnet-ef database update", localInstallText, StringComparison.Ordinal);
        Assert.Contains("backup-postgres.ps1", localInstallText, StringComparison.Ordinal);
        Assert.Contains("restore-postgres.ps1", localInstallText, StringComparison.Ordinal);
        Assert.Contains("garagebalance_restore_check", localInstallText, StringComparison.Ordinal);
        Assert.Contains("Условия финального закрытия локальной установки", localInstallText, StringComparison.Ordinal);
        Assert.Contains("docker compose config", localInstallText, StringComparison.Ordinal);
        Assert.Contains("localPostgresPreflight=OK", localInstallText, StringComparison.Ordinal);

        Assert.Contains("Test-NetConnection", preflightScriptText, StringComparison.Ordinal);
        Assert.Contains("psqlConnection=True", preflightScriptText, StringComparison.Ordinal);
        Assert.Contains("localPostgresPreflight=OK", preflightScriptText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 11 \"Подготовить инструкцию локальной установки на ПК заказчика\" доведен до проверяемого blocked-состояния", historyText, StringComparison.Ordinal);
        Assert.Contains("live Docker Compose или без-Docker запуск", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageElevenVpsDeploymentGuideRemainsBlockedUntilLiveDeployButHasCompleteChecklist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklistText = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "vps-deployment-checklist.md"));
        var workflowText = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "deploy-staging.yml"));
        var applyScriptText = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "vps-apply-release.sh"));

        var deploymentLine = activeRoadmapLines.Single(line =>
            line.Contains("Подготовить инструкцию VPS/domain deployment", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]`", deploymentLine, StringComparison.Ordinal);
        Assert.Contains("vps-deployment-checklist.md", deploymentLine, StringComparison.Ordinal);
        Assert.Contains("GitHub Secrets", deploymentLine, StringComparison.Ordinal);
        Assert.Contains("https://sgk.blagodaty.ru/health", deploymentLine, StringComparison.Ordinal);
        Assert.Contains("StageElevenVpsDeploymentGuideRemainsBlockedUntilLiveDeployButHasCompleteChecklist", deploymentLine, StringComparison.Ordinal);

        Assert.Contains("sgk.blagodaty.ru", checklistText, StringComparison.Ordinal);
        Assert.Contains("/opt/garagebalance-staging", checklistText, StringComparison.Ordinal);
        Assert.Contains("/etc/garagebalance-staging.env", checklistText, StringComparison.Ordinal);
        Assert.Contains("VPS_HOST", checklistText, StringComparison.Ordinal);
        Assert.Contains("sudo -l -U garagebalance-deploy", checklistText, StringComparison.Ordinal);
        Assert.Contains("/usr/local/bin/garagebalance-deploy-apply <release-id>", checklistText, StringComparison.Ordinal);
        Assert.Contains("pg_dump", checklistText, StringComparison.Ordinal);
        Assert.Contains("dotnet tool run dotnet-ef migrations script --idempotent", checklistText, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging.service", checklistText, StringComparison.Ordinal);
        Assert.Contains("certbot --nginx -d sgk.blagodaty.ru", checklistText, StringComparison.Ordinal);
        Assert.Contains("curl -fsS https://sgk.blagodaty.ru/health", checklistText, StringComparison.Ordinal);
        Assert.Contains("Условия финального закрытия VPS/domain deployment", checklistText, StringComparison.Ordinal);
        Assert.Contains("desktop/mobile", checklistText, StringComparison.Ordinal);

        Assert.Contains("secrets.VPS_SSH_KEY", workflowText, StringComparison.Ordinal);
        Assert.Contains("sudo /usr/local/bin/garagebalance-deploy-apply", workflowText, StringComparison.Ordinal);
        Assert.Contains("pg_dump --format=custom", applyScriptText, StringComparison.Ordinal);
        Assert.Contains("restore_previous_release", applyScriptText, StringComparison.Ordinal);
        Assert.Contains("curl -fsSk -H \"Host: ${PUBLIC_HOST}\" \"https://127.0.0.1/health\"", applyScriptText, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 11 \"Подготовить инструкцию VPS/domain deployment\" доведен до проверяемого blocked-состояния", historyText, StringComparison.Ordinal);
        Assert.Contains("workflow/deploy run отсутствует", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageElevenPostgresBackupRestoreRemainsBlockedWithoutLocalPostgresButHasScriptsDocsAndOwnerFix()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var backupDocument = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "postgres-backup-restore.md"));
        var preflightScript = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "check-local-postgres.ps1"));
        var backupScript = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "backup-postgres.ps1"));
        var restoreScript = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "restore-postgres.ps1"));

        var backupRestoreLine = activeRoadmapLines.Single(line =>
            line.Contains("Проверить backup/restore PostgreSQL", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]`", backupRestoreLine, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=False", backupRestoreLine, StringComparison.Ordinal);
        Assert.Contains("psql=False", backupRestoreLine, StringComparison.Ordinal);
        Assert.Contains("docker=False", backupRestoreLine, StringComparison.Ordinal);
        Assert.Contains("StageElevenPostgresBackupRestoreRemainsBlockedWithoutLocalPostgresButHasScriptsDocsAndOwnerFix", backupRestoreLine, StringComparison.Ordinal);

        Assert.Contains("Test-NetConnection", preflightScript, StringComparison.Ordinal);
        Assert.Contains("SELECT 1;", preflightScript, StringComparison.Ordinal);
        Assert.Contains("localPostgresPreflight=OK", preflightScript, StringComparison.Ordinal);
        Assert.Contains("Get-Command pg_dump", backupScript, StringComparison.Ordinal);
        Assert.Contains("--format=custom", backupScript, StringComparison.Ordinal);
        Assert.Contains(".incomplete", backupScript, StringComparison.Ordinal);
        Assert.Contains("Backup file is empty.", backupScript, StringComparison.Ordinal);
        Assert.Contains("Get-Command pg_restore", restoreScript, StringComparison.Ordinal);
        Assert.Contains("garagebalance_restore_check", restoreScript, StringComparison.Ordinal);
        Assert.Contains("AllowProductionTarget", restoreScript, StringComparison.Ordinal);
        Assert.Contains("$quotedOwner", restoreScript, StringComparison.Ordinal);
        Assert.Contains("OWNER $quotedOwner", restoreScript, StringComparison.Ordinal);

        Assert.Contains("Условия финального закрытия backup/restore", backupDocument, StringComparison.Ordinal);
        Assert.Contains("localPostgresPreflight=OK", backupDocument, StringComparison.Ordinal);
        Assert.Contains("backup-postgres.ps1", backupDocument, StringComparison.Ordinal);
        Assert.Contains("restore-postgres.ps1 -TargetDatabase garagebalance_restore_check -DropAndCreate", backupDocument, StringComparison.Ordinal);
        Assert.Contains("-AllowProductionTarget", backupDocument, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 11 \"Проверить backup/restore PostgreSQL\" доведен до проверяемого blocked-состояния", historyText, StringComparison.Ordinal);
        Assert.Contains("создает проверочную базу с владельцем из `-Username`", historyText, StringComparison.Ordinal);
        Assert.Contains("live `check-local-postgres.ps1 -RequirePsql`", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageElevenRegularLocalBackupRemainsBlockedUntilTaskSchedulerRunButHasScriptDocsAndChecklist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var backupDocument = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "postgres-backup-restore.md"));
        var localChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "local-pc-install-checklist.md"));
        var scheduledTaskScript = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "register-local-backup-task.ps1"));

        var regularBackupLine = activeRoadmapLines.Single(line =>
            line.Contains("Настроить регулярный локальный backup для установки на ПК", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]`", regularBackupLine, StringComparison.Ordinal);
        Assert.Contains("register-local-backup-task.ps1", regularBackupLine, StringComparison.Ordinal);
        Assert.Contains("GarageBalance Local PostgreSQL Backup", regularBackupLine, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=False", regularBackupLine, StringComparison.Ordinal);
        Assert.Contains("psql=False", regularBackupLine, StringComparison.Ordinal);
        Assert.Contains("docker=False", regularBackupLine, StringComparison.Ordinal);
        Assert.Contains("StageElevenRegularLocalBackupRemainsBlockedUntilTaskSchedulerRunButHasScriptDocsAndChecklist", regularBackupLine, StringComparison.Ordinal);
        Assert.DoesNotContain("- `[x]` Настроить регулярный локальный backup", regularBackupLine, StringComparison.Ordinal);

        Assert.Contains("New-ScheduledTaskTrigger -Daily", scheduledTaskScript, StringComparison.Ordinal);
        Assert.Contains("Register-ScheduledTask", scheduledTaskScript, StringComparison.Ordinal);
        Assert.Contains("backup-postgres.ps1", scheduledTaskScript, StringComparison.Ordinal);
        Assert.Contains("ParseExact($At, \"HH:mm\"", scheduledTaskScript, StringComparison.Ordinal);
        Assert.Contains("scheduledTask=", scheduledTaskScript, StringComparison.Ordinal);

        Assert.Contains("Условия финального закрытия регулярного backup", backupDocument, StringComparison.Ordinal);
        Assert.Contains("scheduledTask=GarageBalance Local PostgreSQL Backup", backupDocument, StringComparison.Ordinal);
        Assert.Contains("Задача запущена вручную хотя бы один раз", backupDocument, StringComparison.Ordinal);
        Assert.Contains("restore-postgres.ps1 -TargetDatabase garagebalance_restore_check -DropAndCreate", backupDocument, StringComparison.Ordinal);
        Assert.Contains("Для ежедневного backup зарегистрировать задачу `GarageBalance Local PostgreSQL Backup`", localChecklist, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 11 \"Настроить регулярный локальный backup для установки на ПК\" доведен до проверяемого blocked-состояния", historyText, StringComparison.Ordinal);
        Assert.Contains("live регистрация", historyText, StringComparison.Ordinal);
        Assert.Contains("ручной запуск задачи", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageElevenMigrationVerificationRemainsBlockedWithoutLocalPostgresButHasChecklistAndScript()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var checklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "migration-verification-checklist.md"));
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "generate-migration-script.ps1"));

        var migrationLine = activeRoadmapLines.Single(line =>
            line.Contains("Проверить миграции на чистой базе и на базе после импорта", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]`", migrationLine, StringComparison.Ordinal);
        Assert.Contains("generate-migration-script.ps1", migrationLine, StringComparison.Ordinal);
        Assert.Contains("migration-verification-checklist.md", migrationLine, StringComparison.Ordinal);
        Assert.Contains("garagebalance_migration_clean_check", migrationLine, StringComparison.Ordinal);
        Assert.Contains("garagebalance_migration_import_check", migrationLine, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=False", migrationLine, StringComparison.Ordinal);
        Assert.Contains("psql=False", migrationLine, StringComparison.Ordinal);
        Assert.Contains("docker=False", migrationLine, StringComparison.Ordinal);
        Assert.Contains("post-import базы", migrationLine, StringComparison.Ordinal);
        Assert.Contains("StageElevenMigrationVerificationRemainsBlockedWithoutLocalPostgresButHasChecklistAndScript", migrationLine, StringComparison.Ordinal);
        Assert.DoesNotContain("- `[x]` Проверить миграции", migrationLine, StringComparison.Ordinal);

        Assert.Contains("--idempotent", script, StringComparison.Ordinal);
        Assert.Contains("migrationScriptPath=", script, StringComparison.Ordinal);
        Assert.Contains("migrationScriptBytes=", script, StringComparison.Ordinal);
        Assert.Contains("Migration script is empty", script, StringComparison.Ordinal);

        Assert.Contains("Условия финального закрытия проверки миграций", checklist, StringComparison.Ordinal);
        Assert.Contains("check-local-postgres.ps1 -RequirePsql", checklist, StringComparison.Ordinal);
        Assert.Contains("dotnet tool run dotnet-ef database update", checklist, StringComparison.Ordinal);
        Assert.Contains("dotnet tool run dotnet-ef migrations list", checklist, StringComparison.Ordinal);
        Assert.Contains("import run, quarantine, audit", checklist, StringComparison.Ordinal);
        Assert.Contains("без персональных, финансовых и импортных данных", checklist, StringComparison.Ordinal);

        Assert.Contains("пункт Stage 11 \"Проверить миграции на чистой базе и на базе после импорта\" доведен до проверяемого blocked-состояния", historyText, StringComparison.Ordinal);
        Assert.Contains("live `dotnet-ef database update` на чистой PostgreSQL", historyText, StringComparison.Ordinal);
        Assert.Contains("post-import базы", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageElevenFullBackendFrontendTestRunIsMarkedCompleteWhenCurrentVerificationCommandsPass()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));

        var fullTestRunLine = activeRoadmapLines.Single(line =>
            line.Contains("Провести полные backend/frontend тесты", StringComparison.Ordinal));
        var performanceLine = activeRoadmapLines.Single(line =>
            line.Contains("Провести финальную проверку производительности", StringComparison.Ordinal));
        var finalReleaseLine = activeRoadmapLines.Single(line =>
            line.Contains("Подготовить финальную запись \"Что нового\"", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]`", fullTestRunLine, StringComparison.Ordinal);
        Assert.Contains("dotnet test GarageBalance.slnx --no-restore --configuration Debug", fullTestRunLine, StringComparison.Ordinal);
        Assert.Contains("npm run test -- --reporter=dot --maxWorkers=1 --testTimeout=180000", fullTestRunLine, StringComparison.Ordinal);
        Assert.Contains("StageElevenFullBackendFrontendTestRunIsMarkedCompleteWhenCurrentVerificationCommandsPass", fullTestRunLine, StringComparison.Ordinal);

        Assert.StartsWith("- `[!]`", performanceLine, StringComparison.Ordinal);
        Assert.StartsWith("- `[x]`", finalReleaseLine, StringComparison.Ordinal);
        Assert.Contains("0.538.0", finalReleaseLine, StringComparison.Ordinal);

        Assert.Contains("закрыт пункт Stage 11 \"Провести полные backend/frontend тесты\"", historyText, StringComparison.Ordinal);
        Assert.Contains("полный backend 1199/1199", historyText, StringComparison.Ordinal);
        Assert.Contains("полный frontend 303/303", historyText, StringComparison.Ordinal);
        Assert.Contains("dotnet format GarageBalance.slnx --verify-no-changes --no-restore", historyText, StringComparison.Ordinal);
        Assert.Contains("npm exec tsc -- --noEmit", historyText, StringComparison.Ordinal);
        Assert.Contains("npm run lint", historyText, StringComparison.Ordinal);
        Assert.Contains("npm run build", historyText, StringComparison.Ordinal);
        Assert.Contains("npm run check:bundle", historyText, StringComparison.Ordinal);
        Assert.Contains("idempotent EF migration script", historyText, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=False", historyText, StringComparison.Ordinal);
        Assert.Contains("psql=False", historyText, StringComparison.Ordinal);
        Assert.Contains("docker=False", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageElevenFinalPerformanceCheckRemainsBlockedWithoutRealPostgresDataAndHasChecklist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var performanceChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "final-performance-checklist.md"));

        var performanceLine = activeRoadmapLines.Single(line =>
            line.Contains("Провести финальную проверку производительности", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]`", performanceLine, StringComparison.Ordinal);
        Assert.Contains("final-performance-checklist.md", performanceLine, StringComparison.Ordinal);
        Assert.Contains("BackendPerformanceGuardTests", performanceLine, StringComparison.Ordinal);
        Assert.Contains("npm run check:bundle", performanceLine, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=False", performanceLine, StringComparison.Ordinal);
        Assert.Contains("psql=False", performanceLine, StringComparison.Ordinal);
        Assert.Contains("docker=False", performanceLine, StringComparison.Ordinal);
        Assert.Contains("StageElevenFinalPerformanceCheckRemainsBlockedWithoutRealPostgresDataAndHasChecklist", performanceLine, StringComparison.Ordinal);

        Assert.Contains("EXPLAIN (ANALYZE, BUFFERS)", performanceChecklist, StringComparison.Ordinal);
        Assert.Contains("GIN trigram indexes", performanceChecklist, StringComparison.Ordinal);
        Assert.Contains("CountAsync", performanceChecklist, StringComparison.Ordinal);
        Assert.Contains("SumAsync", performanceChecklist, StringComparison.Ordinal);
        Assert.Contains("browser console", performanceChecklist, StringComparison.Ordinal);

        Assert.Contains("подготовлен checklist финальной проверки производительности", historyText, StringComparison.Ordinal);
        Assert.Contains("BackendPerformanceGuardTests.FinalPerformanceChecklistCoversAutomatedGatesPostgresQueriesFrontendAndAcceptanceThresholds", historyText, StringComparison.Ordinal);
        Assert.Contains("StageElevenFinalPerformanceCheckRemainsBlockedWithoutRealPostgresDataAndHasChecklist", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageElevenFinalReleaseNoteIsMarkedCompleteWhenReleaseEntryWarnsAboutAcceptanceGates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var releaseNotesText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "AppReleases", "releases.json"));

        var finalReleaseLine = activeRoadmapLines.Single(line =>
            line.Contains("Подготовить финальную запись \"Что нового\"", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]`", finalReleaseLine, StringComparison.Ordinal);
        Assert.Contains("0.538.0", finalReleaseLine, StringComparison.Ordinal);
        Assert.Contains("StageElevenFinalReleaseNoteIsMarkedCompleteWhenReleaseEntryWarnsAboutAcceptanceGates", finalReleaseLine, StringComparison.Ordinal);

        Assert.Contains("\"releaseId\": \"2026-07-11-final-acceptance-readiness\"", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"0.538.0\"", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("\"title\": \"Финальный контроль готовности перед приемкой\"", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"important\"", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("полные автоматические проверки backend и frontend", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("восстановление после сбоя", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("производительность на объемах ГСК", releaseNotesText, StringComparison.Ordinal);
        Assert.Contains("печати, 1C Fresh и правилам учета", releaseNotesText, StringComparison.Ordinal);

        Assert.Contains("закрыт пункт Stage 11 \"Подготовить финальную запись", historyText, StringComparison.Ordinal);
        Assert.Contains("JSON release-файла", historyText, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=False", historyText, StringComparison.Ordinal);
        Assert.Contains("psql=False", historyText, StringComparison.Ordinal);
        Assert.Contains("docker=False", historyText, StringComparison.Ordinal);
        Assert.Contains("live backup/restore", historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void StageElevenRecoveryCheckRemainsBlockedWithoutLocalPostgresAndHasRunbook()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var historyText = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var recoveryChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "disaster-recovery-checklist.md"));

        var recoveryLine = activeRoadmapLines.Single(line =>
            line.Contains("Проверить восстановление после сбоя импорта и после неудачного обновления", StringComparison.Ordinal));

        Assert.StartsWith("- `[!]`", recoveryLine, StringComparison.Ordinal);
        Assert.Contains("disaster-recovery-checklist.md", recoveryLine, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=False", recoveryLine, StringComparison.Ordinal);
        Assert.Contains("psql=False", recoveryLine, StringComparison.Ordinal);
        Assert.Contains("docker=False", recoveryLine, StringComparison.Ordinal);
        Assert.Contains("StageElevenRecoveryCheckRemainsBlockedWithoutLocalPostgresAndHasRunbook", recoveryLine, StringComparison.Ordinal);

        Assert.Contains("restore-postgres.ps1", recoveryChecklist, StringComparison.Ordinal);
        Assert.Contains("garagebalance_restore_check", recoveryChecklist, StringComparison.Ordinal);
        Assert.Contains("Failed Access import", recoveryChecklist, StringComparison.Ordinal);
        Assert.Contains("Failed update or migration", recoveryChecklist, StringComparison.Ordinal);
        Assert.Contains("Production restore gate", recoveryChecklist, StringComparison.Ordinal);
        Assert.Contains("-AllowProductionTarget", recoveryChecklist, StringComparison.Ordinal);

        Assert.Contains("подготовлен checklist восстановления после сбоя импорта и неудачного обновления", historyText, StringComparison.Ordinal);
        Assert.Contains("BackupScriptTests.DisasterRecoveryChecklistCoversFailedImportUpdateRestoreCheckAndProductionGuard", historyText, StringComparison.Ordinal);
        Assert.Contains("StageElevenRecoveryCheckRemainsBlockedWithoutLocalPostgresAndHasRunbook", historyText, StringComparison.Ordinal);
        Assert.Contains("Запись \"Что нового\" не добавлялась", historyText, StringComparison.Ordinal);
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
        Assert.Contains("backend regression coverage", integrationLine, StringComparison.Ordinal);
        Assert.Contains("React coverage закрепляет настройки 1C Fresh", integrationLine, StringComparison.Ordinal);
        Assert.Contains("Реальный обмен с 1C Fresh", integrationLine, StringComparison.Ordinal);
        Assert.Contains("conflict resolution на контуре остаются будущими задачами", integrationLine, StringComparison.Ordinal);
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
