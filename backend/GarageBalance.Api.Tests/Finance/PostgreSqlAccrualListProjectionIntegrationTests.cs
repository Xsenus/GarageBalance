using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlAccrualListProjectionIntegrationTests
{
    [PostgreSqlFact]
    public async Task GetListAsync_UsesBoundedCompactProjectionForDisplayedAccrualData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var owner = new Owner
        {
            LastName = "Петров",
            FirstName = "Пётр",
            MiddleName = "Петрович",
            Phone = "+7 900 000-00-00",
            Address = "Не должен загружаться",
            MeterNotes = "Не должны загружаться"
        };
        var garage = new Garage
        {
            Number = "ACCRUAL-COMPACT-27182",
            PeopleCount = 8,
            FloorCount = 4,
            StartingBalance = 900m,
            InitialWaterMeterValue = 123m,
            Comment = "Не должен загружаться из гаража",
            Owner = owner
        };
        var incomeType = new IncomeType
        {
            Name = "Начисление компактного списка 27182",
            Code = "compact_accrual_income_27182"
        };
        var irregularPayment = new IrregularPayment
        {
            Name = "Нерегулярный платёж списка 27182",
            Amount = 555m,
            IsActive = false
        };
        var feeCampaign = new FeeCampaign
        {
            Name = "Объявленный сбор списка 27182",
            IncomeType = incomeType,
            IncomeTypeId = incomeType.Id,
            Goal = "Не должна загружаться",
            ContributionAmount = 700m,
            TargetAmount = 7000m,
            StartsOn = new DateOnly(2050, 1, 1),
            OverdueGraceDays = 45
        };
        var accrual = new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            IrregularPayment = irregularPayment,
            Basis = "Основание отображается",
            FeeCampaign = feeCampaign,
            AccountingMonth = new DateOnly(2050, 3, 1),
            AccountingYear = 2050,
            DueDate = new DateOnly(2050, 3, 20),
            OverdueFromDate = new DateOnly(2050, 4, 1),
            DueDateNeedsReview = true,
            DueDateReviewReason = "Не используется рабочим списком",
            Amount = 1250m,
            RequiresMeterReading = true,
            CalculationMeterKind = MeterKinds.Water,
            CalculationDetailsJson = $"{{\"payload\":\"{new string('x', 20_000)}\"}}",
            Source = AccrualSources.Manual,
            Comment = "Комментарий начисления отображается"
        };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.Accruals.AddRange(
                accrual,
                new Accrual
                {
                    Garage = new Garage { Number = "ACCRUAL-COMPACT-SECOND", PeopleCount = 1, FloorCount = 1 },
                    IncomeType = new IncomeType
                    {
                        Name = "Второе начисление компактного списка 27182",
                        Code = "compact_accrual_income_second_27182"
                    },
                    AccountingMonth = new DateOnly(2050, 2, 1),
                    DueDate = new DateOnly(2050, 2, 20),
                    OverdueFromDate = new DateOnly(2050, 3, 1),
                    Amount = 10m,
                    Source = AccrualSources.Manual
                });
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfAccrualRepository(queryContext);

        var result = await repository.GetListAsync(null, null, null, 1, CancellationToken.None);

        var actual = Assert.Single(result);
        Assert.Equal(accrual.Id, actual.Id);
        Assert.Equal("ACCRUAL-COMPACT-27182", actual.Garage.Number);
        Assert.Equal("Петров Пётр Петрович", actual.Garage.Owner!.FullName);
        Assert.Equal("Начисление компактного списка 27182", actual.IncomeType.Name);
        Assert.Equal("Нерегулярный платёж списка 27182", actual.IrregularPayment!.Name);
        Assert.Equal("Основание отображается", actual.Basis);
        Assert.Equal("Объявленный сбор списка 27182", actual.FeeCampaign!.Name);
        Assert.Equal(2050, actual.AccountingYear);
        Assert.Equal(new DateOnly(2050, 3, 20), actual.DueDate);
        Assert.Equal(new DateOnly(2050, 4, 1), actual.OverdueFromDate);
        Assert.Equal(1250m, actual.Amount);
        Assert.Equal("Комментарий начисления отображается", actual.Comment);
        Assert.Empty(queryContext.ChangeTracker.Entries());

        var command = Assert.Single(capture.Commands);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AccountingYear", command, StringComparison.Ordinal);
        Assert.DoesNotContain("PeopleCount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("FloorCount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("StartingBalance", command, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialWaterMeterValue", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Phone", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Address", command, StringComparison.Ordinal);
        Assert.DoesNotContain("MeterNotes", command, StringComparison.Ordinal);
        Assert.DoesNotContain("DestinationFundId", command, StringComparison.Ordinal);
        Assert.DoesNotContain("IsActive", command, StringComparison.Ordinal);
        Assert.DoesNotContain("ContributionAmount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetAmount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("OverdueGraceDays", command, StringComparison.Ordinal);
        Assert.DoesNotContain("TariffId", command, StringComparison.Ordinal);
        Assert.DoesNotContain("DueDateNeedsReview", command, StringComparison.Ordinal);
        Assert.DoesNotContain("CalculationDetailsJson", command, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task GetListAsync_PropagatesCancellationBeforeDatabaseRead()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfAccrualRepository(queryContext);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.GetListAsync(null, null, null, 10, cancellation.Token));

        Assert.Empty(capture.Commands);
        Assert.Empty(queryContext.ChangeTracker.Entries());
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
