namespace GarageBalance.Api.Tests.Deployment;

public sealed class ApiEndpointDocumentationTests
{
    [Theory]
    [InlineData("/api/import/access/runs/{id}/report")]
    [InlineData("/api/reports/consolidated/export/xlsx")]
    [InlineData("/api/reports/consolidated/export/pdf")]
    [InlineData("/api/reports/income/export/xlsx")]
    [InlineData("/api/reports/income/export/pdf")]
    [InlineData("/api/reports/expense/export/xlsx")]
    [InlineData("/api/reports/expense/export/pdf")]
    public void ReadmeDocumentsAuditWritingExportsAsPost(string route)
    {
        var readme = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "README.md"));

        Assert.Contains($"POST {route}", readme, StringComparison.Ordinal);
        Assert.DoesNotContain($"GET {route}", readme, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/import/access/runs/{id}/report")]
    [InlineData("/api/reports/consolidated/export/xlsx")]
    [InlineData("/api/reports/consolidated/export/pdf")]
    [InlineData("/api/reports/income/export/xlsx")]
    [InlineData("/api/reports/income/export/pdf")]
    [InlineData("/api/reports/expense/export/xlsx")]
    [InlineData("/api/reports/expense/export/pdf")]
    public void ProjectRoadmapDocumentsAuditWritingExportsAsPost(string route)
    {
        var roadmap = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "project-roadmap.md"));

        Assert.Contains($"POST {route}", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain($"GET {route}", roadmap, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectWideHistoryRoadmapDocumentsInventoryStatusSync()
    {
        var roadmap = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"));

        const string inventoryStatusLine =
            "Обновить этот roadmap по итогам инвентаризации: готовые пункты отмечены `[x]`, частично готовые оставлены `[~]`, спорные бизнес-решения оставлены `[decision]`, отсутствующая работа оставлена `[ ]`, ручная приемка оставлена `[acceptance]`.";

        Assert.Contains($"- `[x]` {inventoryStatusLine}", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "- `[ ]` Обновить этот roadmap по итогам инвентаризации",
            roadmap,
            StringComparison.Ordinal);
        Assert.Contains("ProjectWideHistoryRoadmapDocumentsInventoryStatusSync", roadmap, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectWideHistoryRoadmapDocumentsMilestone7ConfirmationStatusSync()
    {
        var roadmap = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"));

        string[] completedConfirmationLines =
        [
            "Для каждого edit dialog хранить исходный snapshot.",
            "При нажатии `Сохранить`, если есть изменения, показывать confirmation dialog `Подтвердите изменения`.",
            "Dialog должен показывать список измененных полей в формате `Поле: было -> стало`.",
            "Для денежных значений использовать формат денег.",
            "Для дат использовать пользовательский формат.",
            "Для select/enum показывать человекочитаемые названия, а не id.",
            "Для sensitive fields показывать `изменено`, не раскрывая старое/новое значение.",
            "Если изменений нет, закрывать форму или считать сохранение успешным без confirmation."
        ];

        foreach (var line in completedConfirmationLines)
        {
            Assert.Contains($"- `[x]` {line}", roadmap, StringComparison.Ordinal);
            Assert.DoesNotContain($"- `[ ]` {line}", roadmap, StringComparison.Ordinal);
            Assert.DoesNotContain($"- `[~]` {line}", roadmap, StringComparison.Ordinal);
        }

        Assert.Contains("ProjectWideHistoryRoadmapDocumentsMilestone7ConfirmationStatusSync", roadmap, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectWideHistoryRoadmapDocumentsMilestone9SettingsStatusSync()
    {
        var roadmap = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"));

        const string settingsCoverageLine =
            "Настройки: все системные настройки, интеграционные настройки, protected/secret поля с маскированием.";
        const string milestoneLine =
            "Настройки: история, confirmations, style audit.";

        Assert.Contains($"- `[x]` {settingsCoverageLine}", roadmap, StringComparison.Ordinal);
        Assert.Contains($"- `[x]` {milestoneLine}", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain($"- `[ ]` {settingsCoverageLine}", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain($"- `[~]` {settingsCoverageLine}", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain($"- `[ ]` {milestoneLine}", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain($"- `[~]` {milestoneLine}", roadmap, StringComparison.Ordinal);
        Assert.Contains("ProjectWideHistoryRoadmapDocumentsMilestone9SettingsStatusSync", roadmap, StringComparison.Ordinal);
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
