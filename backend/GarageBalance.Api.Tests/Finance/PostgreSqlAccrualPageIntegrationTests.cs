using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlAccrualPageIntegrationTests
{
    [PostgreSqlFact]
    public async Task AccrualPageLoadsCountRowsAndRelatedNamesInOneCommandForEveryPageShape()
    {
        var owner = new Owner { LastName = "Петров", FirstName = "Пётр", MiddleName = "Петрович" };
        var firstGarage = new Garage { Number = "1", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var secondGarage = new Garage { Number = "2", PeopleCount = 1, FloorCount = 1 };
        var regularIncomeType = new IncomeType { Name = "Тестовый целевой взнос 2046" };
        var otherIncomeType = new IncomeType { Name = "Тестовая прочая оплата 2046" };
        var irregularPayment = new IrregularPayment { Name = "Target пропуск", Amount = 325.50m };
        var feeCampaign = new FeeCampaign
        {
            Name = "Target ремонт ворот",
            IncomeType = regularIncomeType,
            ContributionAmount = 700m,
            TargetAmount = 7000m,
            StartsOn = new DateOnly(2046, 1, 1)
        };
        var expectedCreatedAt = new DateTimeOffset(2046, 2, 5, 10, 30, 0, TimeSpan.Zero);
        var expectedUpdatedAt = expectedCreatedAt.AddHours(2);
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AddRange(
                CreateAccrual(
                    secondGarage,
                    regularIncomeType,
                    new DateOnly(2046, 3, 1),
                    900m,
                    "Target latest"),
                CreateAccrual(
                    firstGarage,
                    otherIncomeType,
                    new DateOnly(2046, 2, 1),
                    325.50m,
                    "Разовое начисление",
                    irregularPayment: irregularPayment,
                    accountingYear: 2046,
                    dueDateNeedsReview: true,
                    dueDateReviewReason: "Проверить протокол",
                    createdAtUtc: expectedCreatedAt,
                    updatedAtUtc: expectedUpdatedAt),
                CreateAccrual(
                    firstGarage,
                    regularIncomeType,
                    new DateOnly(2046, 1, 1),
                    700m,
                    "Объявленный сбор",
                    feeCampaign: feeCampaign),
                CreateAccrual(
                    firstGarage,
                    regularIncomeType,
                    new DateOnly(2045, 12, 1),
                    500m,
                    "Target outside period"),
                CreateAccrual(
                    secondGarage,
                    regularIncomeType,
                    new DateOnly(2046, 4, 1),
                    100m,
                    "Target canceled",
                    isCanceled: true),
                CreateAccrual(
                    secondGarage,
                    regularIncomeType,
                    new DateOnly(2046, 2, 1),
                    100m,
                    "Ordinary row"));
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var repository = new EfAccrualRepository(context);

        var page = await repository.GetPageAsync(
            new DateOnly(2046, 1, 1),
            new DateOnly(2046, 3, 1),
            "target",
            1,
            2,
            CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(
            [new DateOnly(2046, 2, 1), new DateOnly(2046, 1, 1)],
            page.Items.Select(accrual => accrual.AccountingMonth));

        var irregularAccrual = page.Items[0];
        Assert.Equal(firstGarage.Id, irregularAccrual.GarageId);
        Assert.Equal("1", irregularAccrual.Garage.Number);
        Assert.Equal(owner.FullName, irregularAccrual.Garage.Owner!.FullName);
        Assert.Equal(otherIncomeType.Id, irregularAccrual.IncomeTypeId);
        Assert.Equal("Тестовая прочая оплата 2046", irregularAccrual.IncomeType.Name);
        Assert.Equal(irregularPayment.Id, irregularAccrual.IrregularPaymentId);
        Assert.Equal("Target пропуск", irregularAccrual.IrregularPayment!.Name);
        Assert.Null(irregularAccrual.FeeCampaignId);
        Assert.Equal(2046, irregularAccrual.AccountingYear);
        Assert.Equal(new DateOnly(2046, 2, 20), irregularAccrual.DueDate);
        Assert.Equal(new DateOnly(2046, 3, 1), irregularAccrual.OverdueFromDate);
        Assert.True(irregularAccrual.DueDateNeedsReview);
        Assert.Equal("Проверить протокол", irregularAccrual.DueDateReviewReason);
        Assert.Equal(325.50m, irregularAccrual.Amount);
        Assert.Equal(AccrualSources.Manual, irregularAccrual.Source);
        Assert.Equal("Разовое начисление", irregularAccrual.Comment);
        Assert.False(irregularAccrual.IsCanceled);
        Assert.Equal(expectedCreatedAt, irregularAccrual.CreatedAtUtc);
        Assert.Equal(expectedUpdatedAt, irregularAccrual.UpdatedAtUtc);

        var campaignAccrual = page.Items[1];
        Assert.Equal(feeCampaign.Id, campaignAccrual.FeeCampaignId);
        Assert.Equal("Target ремонт ворот", campaignAccrual.FeeCampaign!.Name);
        Assert.Null(campaignAccrual.IrregularPaymentId);
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var beyondEnd = await repository.GetPageAsync(
            new DateOnly(2046, 1, 1),
            new DateOnly(2046, 3, 1),
            "target",
            20,
            5,
            CancellationToken.None);

        Assert.Equal(3, beyondEnd.TotalCount);
        Assert.Empty(beyondEnd.Items);
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var empty = await repository.GetPageAsync(null, null, "missing", 0, 5, CancellationToken.None);

        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Items);
        AssertSingleCombinedCommand(capture);
    }

    private static Accrual CreateAccrual(
        Garage garage,
        IncomeType incomeType,
        DateOnly accountingMonth,
        decimal amount,
        string comment,
        IrregularPayment? irregularPayment = null,
        FeeCampaign? feeCampaign = null,
        int? accountingYear = null,
        bool dueDateNeedsReview = false,
        string? dueDateReviewReason = null,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? updatedAtUtc = null,
        bool isCanceled = false) =>
        new()
        {
            Garage = garage,
            IncomeType = incomeType,
            IrregularPayment = irregularPayment,
            FeeCampaign = feeCampaign,
            AccountingMonth = accountingMonth,
            AccountingYear = accountingYear,
            DueDate = accountingMonth.AddDays(19),
            OverdueFromDate = accountingMonth.AddMonths(1),
            DueDateNeedsReview = dueDateNeedsReview,
            DueDateReviewReason = dueDateReviewReason,
            Amount = amount,
            Source = AccrualSources.Manual,
            Comment = comment,
            IsCanceled = isCanceled,
            CreatedAtUtc = createdAtUtc ?? accountingMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            UpdatedAtUtc = updatedAtUtc ?? accountingMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        };

    private static void AssertSingleCombinedCommand(ReaderCommandCapture capture)
    {
        var command = Assert.Single(capture.Commands);
        Assert.Contains("COUNT(*)", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accruals", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("garages", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("owners", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("income_types", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("irregular_payments", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fee_campaigns", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", command, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ReaderCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
