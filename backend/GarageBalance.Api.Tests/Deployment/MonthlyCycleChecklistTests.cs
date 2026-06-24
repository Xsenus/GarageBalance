namespace GarageBalance.Api.Tests.Deployment;

public sealed class MonthlyCycleChecklistTests
{
    [Fact]
    public void MonthlyCycleChecklistCoversRequiredOperationalSteps()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "monthly-cycle-checklist.md"));

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
        Assert.Contains("Скачать сводный XLSX", document, StringComparison.Ordinal);
        Assert.Contains("Скачать поступления XLSX/PDF", document, StringComparison.Ordinal);
        Assert.Contains("Скачать выплаты XLSX/PDF", document, StringComparison.Ordinal);
        Assert.Contains("Audit-журнал", document, StringComparison.Ordinal);
        Assert.Contains("Что нового", document, StringComparison.Ordinal);
        Assert.Contains("расширенная приемка до одного месяца", document, StringComparison.Ordinal);
        Assert.Contains("Нельзя переносить данные вручную из Access", document, StringComparison.Ordinal);
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
