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
