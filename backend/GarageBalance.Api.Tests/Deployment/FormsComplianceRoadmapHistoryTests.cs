namespace GarageBalance.Api.Tests.Deployment;

public sealed class FormsComplianceRoadmapHistoryTests
{
    [Fact]
    public void CurrentStatusMatchesActiveChecklist()
    {
        var roadmapPath = Path.Combine(FindRepositoryRoot(), "docs", "forms-compliance-fixes-roadmap.md");
        var lines = File.ReadAllLines(roadmapPath);
        var historyIndex = Array.FindIndex(
            lines,
            line => string.Equals(line, "## История выполнения", StringComparison.Ordinal));

        Assert.True(historyIndex > 0, "Active roadmap must contain its execution history.");

        var activeChecklist = lines[..historyIndex];
        var completed = CountItems(activeChecklist, "- [x] ");
        var inProgress = CountItems(activeChecklist, "- [~] ");
        var pending = CountItems(activeChecklist, "- [ ] ");
        var blocked = CountItems(activeChecklist, "- [!] ");
        var decisions = CountItems(activeChecklist, "- [decision] ");
        var acceptance = CountItems(activeChecklist, "- [acceptance] ");
        var total = completed + inProgress + pending + blocked + decisions + acceptance;
        var status = string.Join(
            '\n',
            activeChecklist
                .SkipWhile(line => !string.Equals(line, "## Текущий статус", StringComparison.Ordinal))
                .TakeWhile((line, index) => index == 0 || !line.StartsWith("## ", StringComparison.Ordinal)));

        Assert.Matches(
            $@"На \d{{2}}\.\d{{2}}\.\d{{4}} в активной части roadmap {total} проверяемых пунктов:",
            status);
        Assert.Contains($"- завершено — {completed} из {total}, или {FormatPercentage(completed, total)}%;", status, StringComparison.Ordinal);
        Assert.Contains($"- в работе — {inProgress} из {total}, или {FormatPercentage(inProgress, total)}%;", status, StringComparison.Ordinal);
        Assert.Contains($"- не начато — {pending} из {total}, или {FormatPercentage(pending, total)}%;", status, StringComparison.Ordinal);
        Assert.Contains($"- заблокировано — {blocked} из {total}, или {FormatPercentage(blocked, total)}%;", status, StringComparison.Ordinal);
        Assert.Contains($"- ожидает бизнес-решения — {decisions} из {total}, или {FormatPercentage(decisions, total)}%;", status, StringComparison.Ordinal);
        Assert.Contains($"- ожидает ручной приемки — {acceptance} из {total}, или {FormatPercentage(acceptance, total)}%.", status, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedStagesRemainRecordedInActiveRoadmapHistory()
    {
        var roadmapPath = Path.Combine(FindRepositoryRoot(), "docs", "forms-compliance-fixes-roadmap.md");
        var lines = File.ReadAllLines(roadmapPath);
        var historyIndex = Array.FindIndex(
            lines,
            line => string.Equals(line, "## История выполнения", StringComparison.Ordinal));

        Assert.True(historyIndex > 0, "Active roadmap must contain its execution history.");

        var activeChecklist = lines[..historyIndex];
        var history = string.Join('\n', lines[(historyIndex + 1)..]);
        var datedEntries = lines[(historyIndex + 1)..]
            .Count(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^- \d{2}\.\d{2}\.\d{4} —"));

        Assert.Contains(
            "- [x] Обновлять раздел `История выполнения` этого roadmap после каждого завершенного этапа.",
            activeChecklist);
        Assert.True(datedEntries >= 85, $"Expected at least 85 dated history entries, found {datedEntries}.");
        Assert.Contains("завершен аудит полноты `Истории выполнения`", history, StringComparison.Ordinal);
        Assert.Contains("docs/forms-compliance-upgrade-runbook.md", history, StringComparison.Ordinal);
        Assert.Contains("docs/forms-compliance-release-notes.md", history, StringComparison.Ordinal);
        Assert.Contains("регламент ежемесячного цикла", history, StringComparison.Ordinal);
        Assert.Contains("обновлены четыре активных decision-checklist", history, StringComparison.Ordinal);
        Assert.Contains("Прогресс roadmap — `104/112` (`92,86%`)", history, StringComparison.Ordinal);
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

    private static int CountItems(IEnumerable<string> lines, string prefix) =>
        lines.Count(line => line.StartsWith(prefix, StringComparison.Ordinal));

    private static string FormatPercentage(int count, int total)
    {
        if (count == 0)
        {
            return "0";
        }

        var percentage = decimal.Round(count * 100m / total, 2, MidpointRounding.AwayFromZero);
        return percentage.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }
}
