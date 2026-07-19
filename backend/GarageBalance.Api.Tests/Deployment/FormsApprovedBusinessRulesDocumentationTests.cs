namespace GarageBalance.Api.Tests.Deployment;

public sealed class FormsApprovedBusinessRulesDocumentationTests
{
    [Fact]
    public void ActiveDecisionChecklistsRecordAllFiveApprovedRulesWithoutClosingRemainingDecisions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var docs = Path.Combine(repositoryRoot, "docs");

        var roadmap = File.ReadAllText(Path.Combine(docs, "forms-compliance-fixes-roadmap.md"));
        var balance = File.ReadAllText(Path.Combine(docs, "balance-model-decision-checklist.md"));
        var income = File.ReadAllText(Path.Combine(docs, "income-service-rules-decision-checklist.md"));
        var expense = File.ReadAllText(Path.Combine(docs, "expense-excel-scenario-decision-checklist.md"));
        var greenColumns = File.ReadAllText(Path.Combine(docs, "contractor-green-columns-decision-template.md"));

        Assert.Contains("старейшие непогашенные начисления того же гаража и вида поступления", balance, StringComparison.Ordinal);
        Assert.Contains("нераспределенный остаток автоматически остается переплатой", balance, StringComparison.Ordinal);
        Assert.Contains("Годовое обязательство считается погашенным только когда", income, StringComparison.Ordinal);
        Assert.Contains("стабильные системные коды `other_payments` и `other_income`", income, StringComparison.Ordinal);
        Assert.Contains("только внутри пары `поставщик + вид выплаты`", expense, StringComparison.Ordinal);
        Assert.Contains("фильтр каждой колонки определяется ее фактическим типом данных", greenColumns, StringComparison.Ordinal);

        Assert.Contains("- [decision] Можно ли вручную списывать/возвращать переплату", balance, StringComparison.Ordinal);
        Assert.Contains("- [decision] Нужно определить источник ручного ввода стоимости услуг по счетам", expense, StringComparison.Ordinal);
        Assert.Contains("точный перечень колонок и правила взаимодействия требуют решения заказчика", greenColumns, StringComparison.Ordinal);
        Assert.Contains("- [!] После утверждения зеленых колонок", roadmap, StringComparison.Ordinal);
        Assert.Contains("- [x] Обновить активные decision-checklist после утверждения правил", roadmap, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GarageBalance.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
