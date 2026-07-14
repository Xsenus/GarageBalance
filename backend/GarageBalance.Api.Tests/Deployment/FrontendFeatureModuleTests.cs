namespace GarageBalance.Api.Tests.Deployment;

public sealed class FrontendFeatureModuleTests
{
    [Fact]
    public void FinanceWorkbench_LoadsOnlyTheActivePagedTableAndDefersMissingMeters()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "frontend", "src", "features", "finance", "FinancePanel.tsx"));
        var methodStart = source.IndexOf("const loadFinanceWorkbench = useCallback", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("useEffect(() =>", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        Assert.Contains("const activePagePromise", method, StringComparison.Ordinal);
        Assert.Contains("section === 'meterReadings'", method, StringComparison.Ordinal);
        Assert.Contains("Promise.resolve(null)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("limit: section ===", method, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendCompositionRootRemainsThinAfterFeatureExtraction()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appPath = Path.Combine(repositoryRoot, "frontend", "src", "App.tsx");
        var appText = File.ReadAllText(appPath);
        var appLineCount = File.ReadLines(appPath).Count();

        Assert.True(appLineCount <= 100, $"App.tsx must remain a thin composition root, but contains {appLineCount} lines.");
        Assert.Contains("<AuthGate", appText, StringComparison.Ordinal);
        Assert.Contains("<AuthenticatedAppShell", appText, StringComparison.Ordinal);
        Assert.Contains("loadStoredAuthSession", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function Workspace(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function FinancePanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function ContractorsPrototypePanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function DictionaryPanelV2(", appText, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "AppShell.tsx")));
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx")));
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "frontend", "src", "shared", "focusHooks.ts")));
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "frontend", "src", "services", "financeApi.ts")));
    }

    [Fact]
    public void AppShellRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var shellText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "AppShell.tsx"));
        Assert.Contains("import { AuthenticatedAppShell } from './features/workspace/AppShell'", appText, StringComparison.Ordinal);
        Assert.Contains("<AuthenticatedAppShell", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("sidebarExpandedStorageKey", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("const navigation", appText, StringComparison.Ordinal);
        Assert.Contains("export function AuthenticatedAppShell(", shellText, StringComparison.Ordinal);
        Assert.Contains("sidebarExpandedStorageKey", shellText, StringComparison.Ordinal);
        Assert.Contains("const navigation: NavigationItem[]", shellText, StringComparison.Ordinal);
        Assert.Contains("<Workspace activeSection={effectiveActiveSection}", shellText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/workspace/AppShell.tsx", File.ReadAllText(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var shellText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "AppShell.tsx"));
        var featureText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        Assert.Contains("import { Workspace } from './Workspace'", shellText, StringComparison.Ordinal);
        Assert.Contains("<Workspace activeSection={effectiveActiveSection}", shellText, StringComparison.Ordinal);
        Assert.DoesNotContain("function Workspace(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function AccessNotice(", appText, StringComparison.Ordinal);
        Assert.Contains("export function Workspace(", featureText, StringComparison.Ordinal);
        Assert.Contains("const dashboardTiles", featureText, StringComparison.Ordinal);
        Assert.Contains("function AccessNotice(", featureText, StringComparison.Ordinal);
        Assert.Contains("case 'payments':", featureText, StringComparison.Ordinal);
        Assert.Contains("<FinancePanel", featureText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/workspace/Workspace.tsx", File.ReadAllText(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void FinancePanelRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var featureText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "finance", "FinancePanel.tsx"));
        AssertLazyFeatureImport(appText, "FinancePanel", "../finance/FinancePanel");
        Assert.Contains("<FinancePanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function FinancePanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function PaymentsPrototypePanel(", appText, StringComparison.Ordinal);
        Assert.Contains("export function FinancePanel(", featureText, StringComparison.Ordinal);
        Assert.Contains("function PaymentsPrototypePanel(", featureText, StringComparison.Ordinal);
        Assert.Contains("financeClient.createIncome", featureText, StringComparison.Ordinal);
        Assert.Contains("financeClient.cancelOperation", featureText, StringComparison.Ordinal);
        Assert.Contains("integrationClient.registerReceiptPrintingAction", featureText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/finance/FinancePanel.tsx", File.ReadAllText(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void ContractorsPanelRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var featureText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "contractors", "ContractorsPanel.tsx"));
        AssertLazyFeatureImport(appText, "ContractorsPrototypePanel", "../contractors/ContractorsPanel");
        Assert.Contains("<ContractorsPrototypePanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function ContractorsPrototypePanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("type ContractorGarageRow", appText, StringComparison.Ordinal);
        Assert.Contains("export function ContractorsPrototypePanel(", featureText, StringComparison.Ordinal);
        Assert.Contains("dictionaryClient.createGarage", featureText, StringComparison.Ordinal);
        Assert.Contains("dictionaryClient.getGaragesPage", featureText, StringComparison.Ordinal);
        Assert.Contains("isGarageServerSortKey", featureText, StringComparison.Ordinal);
        Assert.Contains("ariaLabel=\"Пагинация гаражей\"", featureText, StringComparison.Ordinal);
        Assert.Contains("dictionaryClient.updateSupplier", featureText, StringComparison.Ordinal);
        Assert.Contains("dictionaryClient.getSuppliersPage", featureText, StringComparison.Ordinal);
        Assert.Contains("isSupplierServerSortKey", featureText, StringComparison.Ordinal);
        Assert.Contains("ariaLabel=\"Пагинация поставщиков\"", featureText, StringComparison.Ordinal);
        Assert.Contains("dictionaryClient.getStaffMembersPage", featureText, StringComparison.Ordinal);
        Assert.Contains("sort.key, sort.direction", featureText, StringComparison.Ordinal);
        Assert.Contains("ariaLabel=\"Пагинация персонала\"", featureText, StringComparison.Ordinal);
        Assert.Contains("financeClient.getGarageBalanceHistory", featureText, StringComparison.Ordinal);
        Assert.Contains("onOpenAudit", featureText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/contractors/ContractorsPanel.tsx", File.ReadAllText(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void TariffsAndFeesPanelRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var featureText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "tariffs", "TariffsAndFeesPanel.tsx"));
        AssertLazyFeatureImport(appText, "TariffsAndFeesPrototypePanel", "../tariffs/TariffsAndFeesPanel");
        Assert.Contains("<TariffsAndFeesPrototypePanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function TariffsAndFeesPrototypePanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("type TariffPrototypePendingChange", appText, StringComparison.Ordinal);
        Assert.Contains("export function TariffsAndFeesPrototypePanel(", featureText, StringComparison.Ordinal);
        Assert.Contains("dictionaryClient.createChargeServiceSetting", featureText, StringComparison.Ordinal);
        Assert.Contains("dictionaryClient.createFeeCampaign", featureText, StringComparison.Ordinal);
        Assert.Contains("financeClient.generateFeeCampaignAccruals", featureText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/tariffs/TariffsAndFeesPanel.tsx", File.ReadAllText(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void DictionaryPanelRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var dictionaryPanelText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "dictionaries", "DictionaryPanel.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        AssertLazyFeatureImport(appText, "DictionaryPanelV2", "../dictionaries/DictionaryPanel");
        Assert.Contains("<DictionaryPanelV2", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function DictionaryPanelV2(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("type DictionaryEditorState", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("getDictionaryRestoreErrorMessage", appText, StringComparison.Ordinal);

        Assert.Contains("export function DictionaryPanelV2(", dictionaryPanelText, StringComparison.Ordinal);
        Assert.Contains("dictionaryClient.createGarage", dictionaryPanelText, StringComparison.Ordinal);
        Assert.Contains("dictionaryClient.updateSupplier", dictionaryPanelText, StringComparison.Ordinal);
        Assert.Contains("dictionaryClient.archiveTariff", dictionaryPanelText, StringComparison.Ordinal);
        Assert.Contains("dictionaryClient.restoreGarage", dictionaryPanelText, StringComparison.Ordinal);
        Assert.Contains("financeClient.getGarageBalanceHistory", dictionaryPanelText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/dictionaries/DictionaryPanel.tsx", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void DictionaryListRemainsInSharedUi()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var dictionaryListText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "DictionaryList.tsx"));
        var dictionaryPanelText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "dictionaries", "DictionaryPanel.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        Assert.Contains("import { DictionaryList } from '../../shared/DictionaryList'", dictionaryPanelText, StringComparison.Ordinal);
        Assert.Contains("<DictionaryList", dictionaryPanelText, StringComparison.Ordinal);
        Assert.DoesNotContain("function DictionaryList(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("type DictionaryListItem", appText, StringComparison.Ordinal);

        Assert.Contains("export function DictionaryList(", dictionaryListText, StringComparison.Ordinal);
        Assert.Contains("aria-controls={listId}", dictionaryListText, StringComparison.Ordinal);
        Assert.Contains("aria-expanded={showAllItems}", dictionaryListText, StringComparison.Ordinal);
        Assert.Contains("pendingArchive.onArchive(reason)", dictionaryListText, StringComparison.Ordinal);
        Assert.Contains("useFocusTrap<HTMLElement>", dictionaryListText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/shared/DictionaryList.tsx", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void UserManagementPanelRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var userManagementPanelText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "users",
            "UserManagementPanel.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        AssertLazyFeatureImport(appText, "UserManagementPanel", "../users/UserManagementPanel");
        Assert.Contains("<UserManagementPanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function UserManagementPanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("type UserEditorState", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function RolePermissionMatrix(", appText, StringComparison.Ordinal);

        Assert.Contains("export function UserManagementPanel(", userManagementPanelText, StringComparison.Ordinal);
        Assert.Contains("userClient.getUsersPage", userManagementPanelText, StringComparison.Ordinal);
        Assert.Contains("userClient.createUser", userManagementPanelText, StringComparison.Ordinal);
        Assert.Contains("userClient.updateUser", userManagementPanelText, StringComparison.Ordinal);
        Assert.Contains("userClient.restoreUser", userManagementPanelText, StringComparison.Ordinal);
        Assert.Contains("userClient.updateRolePermissions", userManagementPanelText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/users/UserManagementPanel.tsx", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportPanelRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var reportPanelText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "reports",
            "ReportPanel.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        AssertLazyFeatureImport(appText, "ReportPanel", "../reports/ReportPanel");
        Assert.Contains("<ReportPanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function ReportPanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("type ReportWorkbookTab", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("aggregateGarageIncomeReportRows", appText, StringComparison.Ordinal);

        Assert.Contains("export function ReportPanel(", reportPanelText, StringComparison.Ordinal);
        Assert.Contains("reportClient.getConsolidatedReport", reportPanelText, StringComparison.Ordinal);
        Assert.Contains("reportClient.getIncomeReport", reportPanelText, StringComparison.Ordinal);
        Assert.Contains("reportClient.exportFeeReport", reportPanelText, StringComparison.Ordinal);
        Assert.Contains("reportClient.getFundChangeReport", reportPanelText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/reports/ReportPanel.tsx", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditPanelRemainsInItsFeatureModuleWithSharedNavigationContracts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var auditPanelText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "audit", "AuditPanel.tsx"));
        var navigationContractsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "workspaceNavigation.ts"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        AssertLazyFeatureImport(appText, "AuditPanel", "../audit/AuditPanel");
        Assert.Contains("<AuditPanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function AuditPanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("getAuditEventSectionLabel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("const auditSectionOptions", appText, StringComparison.Ordinal);

        Assert.Contains("export function AuditPanel(", auditPanelText, StringComparison.Ordinal);
        Assert.Contains("auditClient.getEventsPage", auditPanelText, StringComparison.Ordinal);
        Assert.Contains("auditClient.exportEvents", auditPanelText, StringComparison.Ordinal);
        Assert.Contains("auditClient.exportEventsXlsx", auditPanelText, StringComparison.Ordinal);
        Assert.Contains("getAuditWorkspaceTarget", auditPanelText, StringComparison.Ordinal);
        Assert.Contains("export type WorkspaceSection", navigationContractsText, StringComparison.Ordinal);
        Assert.Contains("export type ContractorOpenTarget", navigationContractsText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/audit/AuditPanel.tsx", roadmapLine, StringComparison.Ordinal);
        Assert.Contains("frontend/src/shared/workspaceNavigation.ts", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void MeterReadingsPanelRemainsInItsFeatureModuleWithSharedEditingHelpers()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var meterReadingsPanelText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "meterReadings",
            "MeterReadingsPanel.tsx"));
        var editingHelpersText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "prototypeEditing.ts"));
        var editingHelpersTestsText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "prototypeEditing.test.ts"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        AssertLazyFeatureImport(appText, "MeterReadingsPrototypePanel", "../meterReadings/MeterReadingsPanel");
        Assert.Contains("<MeterReadingsPrototypePanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function MeterReadingsPrototypePanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("const meterReadingMonths", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("createMeterReadingCellKey", appText, StringComparison.Ordinal);

        Assert.Contains("export function MeterReadingsPrototypePanel(", meterReadingsPanelText, StringComparison.Ordinal);
        Assert.Contains("financeClient.createMeterReading", meterReadingsPanelText, StringComparison.Ordinal);
        Assert.Contains("financeClient.updateMeterReading", meterReadingsPanelText, StringComparison.Ordinal);
        Assert.Contains("export function handleEditableInputKeyDown", editingHelpersText, StringComparison.Ordinal);
        Assert.Contains("export function formatPrototypeChangeValue", editingHelpersText, StringComparison.Ordinal);
        Assert.Contains("commits an editable value", editingHelpersTestsText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/meterReadings/MeterReadingsPanel.tsx", roadmapLine, StringComparison.Ordinal);
        Assert.Contains("frontend/src/shared/prototypeEditing.ts", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportPanelRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var importPanelText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "import",
            "ImportPanel.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        AssertLazyFeatureImport(appText, "ImportPanel", "../import/ImportPanel");
        Assert.Contains("<ImportPanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function ImportPanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("type ImportTab", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("importCreatedRecordsScreenRequestLimit", appText, StringComparison.Ordinal);

        Assert.Contains("export function ImportPanel(", importPanelText, StringComparison.Ordinal);
        Assert.Contains("importClient.dryRunAccess", importPanelText, StringComparison.Ordinal);
        Assert.Contains("importClient.requestAccessImportApply", importPanelText, StringComparison.Ordinal);
        Assert.Contains("importClient.cancelAccessImportApplyRequest", importPanelText, StringComparison.Ordinal);
        Assert.Contains("importClient.requestAccessImportRollback", importPanelText, StringComparison.Ordinal);
        Assert.Contains("importClient.resolveQuarantineItem", importPanelText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/import/ImportPanel.tsx", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void FundsPanelRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var fundsPanelText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "funds",
            "FundsPanel.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        AssertLazyFeatureImport(appText, "FundsPrototypePanel", "../funds/FundsPanel");
        Assert.Contains("<FundsPrototypePanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function FundsPrototypePanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("type FundOperationDraft", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("mapFundDtoToPrototypeRow", appText, StringComparison.Ordinal);

        Assert.Contains("export function FundsPrototypePanel(", fundsPanelText, StringComparison.Ordinal);
        Assert.Contains("fundsClient.createOperation", fundsPanelText, StringComparison.Ordinal);
        Assert.Contains("fundsClient.updateOperation", fundsPanelText, StringComparison.Ordinal);
        Assert.Contains("fundsClient.cancelOperation", fundsPanelText, StringComparison.Ordinal);
        Assert.Contains("fundsClient.restoreOperation", fundsPanelText, StringComparison.Ordinal);
        Assert.Contains("operationReverse.operation.fundId", fundsPanelText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/funds/FundsPanel.tsx", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordPanelRemainsInItsFeatureModuleWithSharedFormField()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var passwordPanelText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "settings",
            "PasswordPanel.tsx"));
        var formFieldText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "FormField.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        AssertLazyFeatureImport(appText, "PasswordPanel", "../settings/PasswordPanel");
        Assert.Contains("<PasswordPanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function PasswordPanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("getOneCFreshSyncConfirmationTitle", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function FormField(", appText, StringComparison.Ordinal);

        Assert.Contains("export function PasswordPanel(", passwordPanelText, StringComparison.Ordinal);
        Assert.Contains("authClient.changeOwnPassword", passwordPanelText, StringComparison.Ordinal);
        Assert.Contains("integrationClient.previewOneCFreshSync", passwordPanelText, StringComparison.Ordinal);
        Assert.Contains("integrationClient.updateProtectedSetting", passwordPanelText, StringComparison.Ordinal);
        Assert.Contains("hasPermission(auth, permissions.usersManage)", passwordPanelText, StringComparison.Ordinal);
        Assert.Contains("export function FormField(", formFieldText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/settings/PasswordPanel.tsx", roadmapLine, StringComparison.Ordinal);
        Assert.Contains("shared UI", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthGateRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var authGateText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "auth",
            "AuthGate.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        Assert.Contains("import { AuthGate } from './features/auth/AuthGate'", appText, StringComparison.Ordinal);
        Assert.Contains("<AuthGate authClient={authClient} onAuthenticated={handleAuthenticated} />", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function AuthGate(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("getAuthValidationErrors", appText, StringComparison.Ordinal);

        Assert.Contains("export function AuthGate(", authGateText, StringComparison.Ordinal);
        Assert.Contains("getAuthValidationErrors('login'", authGateText, StringComparison.Ordinal);
        Assert.Contains("await authClient.login({ email, password })", authGateText, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Вход в систему\"", authGateText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/auth/AuthGate.tsx", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasePanelRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx")) + File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "features", "workspace", "Workspace.tsx"));
        var releasePanelText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "releases",
            "ReleasePanel.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        AssertLazyFeatureImport(appText, "ReleasePanel", "../releases/ReleasePanel");
        Assert.Contains("<ReleasePanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function ReleasePanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("createReleaseEditorState", appText, StringComparison.Ordinal);

        Assert.Contains("export function ReleasePanel(", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("releaseClient.getReleases", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("releaseClient.getManageableReleases", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("hasPermission(auth, permissions.appReleasesManage)", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Что нового\"", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/releases/ReleasePanel.tsx", roadmapLine, StringComparison.Ordinal);
    }

    private static void AssertLazyFeatureImport(string source, string componentName, string modulePath)
    {
        Assert.Contains($"const {componentName} = lazy(() => import('{modulePath}')", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
