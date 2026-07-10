namespace GarageBalance.Api.Tests.Deployment;

public sealed class SoftDeleteCancelCoverageDocumentationTests
{
    public static TheoryData<string> CoveredBackendActions()
    {
        return new TheoryData<string>
        {
            "ArchiveOwnerAsync",
            "RestoreOwnerAsync",
            "ArchiveGarageAsync",
            "RestoreGarageAsync",
            "ArchiveSupplierGroupAsync",
            "RestoreSupplierGroupAsync",
            "ArchiveSupplierAsync",
            "RestoreSupplierAsync",
            "ArchiveIncomeTypeAsync",
            "RestoreIncomeTypeAsync",
            "ArchiveExpenseTypeAsync",
            "RestoreExpenseTypeAsync",
            "ArchiveTariffAsync",
            "RestoreTariffAsync",
            "ArchiveChargeServiceSettingAsync",
            "RestoreChargeServiceSettingAsync",
            "CancelOperationAsync",
            "RestoreOperationAsync",
            "CancelAccrualAsync",
            "RestoreAccrualAsync",
            "CancelSupplierAccrualAsync",
            "CancelMeterReadingAsync",
            "RestoreMeterReadingAsync",
            "RestoreUserAsync",
            "ResolveAsync"
        };
    }

    public static TheoryData<string> CoveredMarkersAndRules()
    {
        return new TheoryData<string>
        {
            "IsArchived",
            "IsCanceled",
            "IsActive = false",
            "ResolvedAtUtc",
            "причина обязательна",
            "ControllerThinnessTests",
            "frontend/src/App.test.tsx",
            "React-state прототип",
            "[decision]"
        };
    }

    [Fact]
    public void SoftDeleteCoverageDocumentContainsRequiredSections()
    {
        var document = ReadSoftDeleteCoverageDocument();

        Assert.Contains("# Покрытие Soft Delete, Archive, Cancel И Restore", document, StringComparison.Ordinal);
        Assert.Contains("## Backend Справочники", document, StringComparison.Ordinal);
        Assert.Contains("## Backend Финансы", document, StringComparison.Ordinal);
        Assert.Contains("## Backend Users И Import", document, StringComparison.Ordinal);
        Assert.Contains("## Frontend Рабочие Экраны", document, StringComparison.Ordinal);
        Assert.Contains("## Физическое Удаление", document, StringComparison.Ordinal);
        Assert.Contains("## Открытые Хвосты", document, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CoveredBackendActions))]
    public void SoftDeleteCoverageDocumentListsCurrentBackendAction(string action)
    {
        var document = ReadSoftDeleteCoverageDocument();

        Assert.Contains(action, document, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CoveredMarkersAndRules))]
    public void SoftDeleteCoverageDocumentListsCurrentMarkersAndRules(string expectedText)
    {
        var document = ReadSoftDeleteCoverageDocument();

        Assert.Contains(expectedText, document, StringComparison.Ordinal);
    }

    private static string ReadSoftDeleteCoverageDocument()
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "soft-delete-cancel-coverage.md"));
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
