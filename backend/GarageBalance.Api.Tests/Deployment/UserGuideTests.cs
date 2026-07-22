namespace GarageBalance.Api.Tests.Deployment;

public sealed class UserGuideTests
{
    [Fact]
    public void UserGuideCoversDailyWorkflowsAndVisibleSections()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "user-guide.md"));

        Assert.Contains("http://127.0.0.1:5173", document, StringComparison.Ordinal);
        Assert.Contains("https://sgk.blagodaty.ru", document, StringComparison.Ordinal);
        Assert.Contains("validation summary", document, StringComparison.Ordinal);
        Assert.Contains("users.manage", document, StringComparison.Ordinal);
        Assert.Contains("dictionaries.read", document, StringComparison.Ordinal);
        Assert.Contains("tariffs.manage", document, StringComparison.Ordinal);
        Assert.Contains("payments.read", document, StringComparison.Ordinal);
        Assert.Contains("reports.read", document, StringComparison.Ordinal);
        Assert.Contains("import.run", document, StringComparison.Ordinal);
        Assert.Contains("audit.read", document, StringComparison.Ordinal);
        Assert.Contains("Пользователи", document, StringComparison.Ordinal);
        Assert.Contains("Справочники", document, StringComparison.Ordinal);
        Assert.Contains("## 5. Тарифы и сборы", document, StringComparison.Ordinal);
        Assert.Contains("Раздел \"Тарифы и сборы\" доступен пользователям с `dictionaries.read`", document, StringComparison.Ordinal);
        Assert.Contains("Платежи", document, StringComparison.Ordinal);
        Assert.Contains("Импорт Access", document, StringComparison.Ordinal);
        Assert.Contains("Отчеты", document, StringComparison.Ordinal);
        Assert.Contains("История изменений", document, StringComparison.Ordinal);
        Assert.Contains("Что нового", document, StringComparison.Ordinal);
        Assert.Contains("Показывать архивные", document, StringComparison.Ordinal);
        Assert.Contains("Вернуть", document, StringComparison.Ordinal);
        Assert.Contains("было -> стало", document, StringComparison.Ordinal);
        Assert.Contains("Только восстановления", document, StringComparison.Ordinal);
        Assert.Contains("связанный гараж", document, StringComparison.Ordinal);
        Assert.Contains("причину или комментарий", document, StringComparison.Ordinal);
        Assert.Contains("Скачать отчет JSON", document, StringComparison.Ordinal);
        Assert.Contains("восемь рабочих вкладок", document, StringComparison.Ordinal);
        Assert.Contains("`Оплаты из кассы`", document, StringComparison.Ordinal);
        Assert.Contains("`Изменение фондов`", document, StringComparison.Ordinal);
        Assert.Contains("`Скачать XLSX` или `Скачать PDF`", document, StringComparison.Ordinal);
        Assert.Contains("## 11. Закрытие месяца", document, StringComparison.Ordinal);
        Assert.Contains("GET /api/app-releases", document, StringComparison.Ordinal);
        Assert.Contains("unauthorized", document, StringComparison.Ordinal);
        Assert.Contains("forbidden", document, StringComparison.Ordinal);
        Assert.Contains("validation_failed", document, StringComparison.Ordinal);
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
