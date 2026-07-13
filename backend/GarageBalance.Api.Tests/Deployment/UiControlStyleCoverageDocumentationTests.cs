namespace GarageBalance.Api.Tests.Deployment;

public sealed class UiControlStyleCoverageDocumentationTests
{
    public static TheoryData<string> RequiredControlFamilies()
    {
        return new TheoryData<string>
        {
            "Primary buttons",
            "Secondary buttons",
            "Ghost buttons",
            "Icon-only buttons",
            "Destructive buttons",
            "Link-like actions",
            "Text inputs",
            "Number inputs",
            "Date/month/year controls",
            "Select controls",
            "Textarea",
            "Checkbox/toggle",
            "Tabs",
            "Dialogs/modals",
            "Tables",
            "Pagination/filter states",
            "Empty/loading/error states"
        };
    }

    public static TheoryData<string> RequiredScreens()
    {
        return new TheoryData<string>
        {
            "Авторизация",
            "Главное меню",
            "Sidebar/topbar",
            "Пользователи",
            "Справочники",
            "Тарифы и сборы-прототип",
            "Контрагенты-прототип",
            "Показания-прототип",
            "Платежи-прототип",
            "Управление фондами-прототип",
            "Финансы/backend экран",
            "Отчеты",
            "Импорт Access",
            "История изменений",
            "\"Что нового\""
        };
    }

    public static TheoryData<string> RequiredStyleClassesAndTests()
    {
        return new TheoryData<string>
        {
            "primary-button",
            "secondary-button",
            "ghost-button",
            "icon-button",
            "danger-button",
            "link-button",
            "dashboard-tile",
            "topbar-back-button",
            "dictionary-form",
            "detail-dialog",
            "modal-backdrop",
            "detail-dialog-actions",
            "empty-state",
            "accessibleStatus.test.ts",
            "responsiveLayout.test.ts",
            "focusHooks.test.tsx",
            "changePreview.test.ts"
        };
    }

    [Fact]
    public void UiControlStyleCoverageDocumentContainsRequiredSections()
    {
        var document = ReadUiCoverageDocument();

        Assert.Contains("# Инвентаризация UI Контролов И Стилей", document, StringComparison.Ordinal);
        Assert.Contains("## Источники", document, StringComparison.Ordinal);
        Assert.Contains("## Семейства Контролов", document, StringComparison.Ordinal);
        Assert.Contains("## Экраны И Текущий Статус", document, StringComparison.Ordinal);
        Assert.Contains("## Уже Закреплено Тестами", document, StringComparison.Ordinal);
        Assert.Contains("## Открытые Хвосты", document, StringComparison.Ordinal);
        Assert.Contains("## Связь С Roadmap", document, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RequiredControlFamilies))]
    public void UiControlStyleCoverageDocumentListsControlFamily(string family)
    {
        var document = ReadUiCoverageDocument();

        Assert.Contains(family, document, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RequiredScreens))]
    public void UiControlStyleCoverageDocumentListsScreen(string screen)
    {
        var document = ReadUiCoverageDocument();

        Assert.Contains(screen, document, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RequiredStyleClassesAndTests))]
    public void UiControlStyleCoverageDocumentListsStyleClassOrTest(string requiredText)
    {
        var document = ReadUiCoverageDocument();

        Assert.Contains(requiredText, document, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectWideRoadmapPointsToUiControlStyleCoverageDocument()
    {
        var roadmap = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "archive", "project-wide-history-and-safety-roadmap.md"));

        Assert.Contains("docs/ui-control-style-coverage.md", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain("- `[ ]` Найти все кнопки, input, textarea, select, date/month controls, tabs, dialogs и проверить стили.", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain("- `[ ]` Составить инвентарь всех button/input/select/textarea/date/month/year controls/tabs/dialogs/tables.", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain("- `[ ]` Все контролы проекта должны использовать единый стиль", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain("- `[ ]` Все controls проекта приведены к единому стилю", roadmap, StringComparison.Ordinal);
    }

    [Fact]
    public void UiControlStyleCoverageDocumentKeepsCurrentStyleAuditClosedExceptManualAcceptance()
    {
        var document = ReadUiCoverageDocument();

        Assert.Contains("Закрывает Milestone 8 по текущему UI", document, StringComparison.Ordinal);
        Assert.Contains("ручную acceptance-приемку скриншотов", document, StringComparison.Ordinal);
        Assert.DoesNotContain("style audit остается частично открытым", document, StringComparison.Ordinal);
        Assert.DoesNotContain("Довести style audit до фактических правок", document, StringComparison.Ordinal);
    }

    private static string ReadUiCoverageDocument()
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "ui-control-style-coverage.md"));
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
