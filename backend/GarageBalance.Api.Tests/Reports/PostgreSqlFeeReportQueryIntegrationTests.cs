using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlFeeReportQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task FeePageUsesOneCommandAndPreservesPageDebtorsAndCompleteSummary()
    {
        var month = new DateOnly(2042, 5, 1);
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var incomeType = new IncomeType { Name = $"Fee integration {Guid.NewGuid():N}" };
            var firstOwner = new Owner { LastName = "Абрамов", FirstName = "Алексей" };
            var secondOwner = new Owner { LastName = "Борисов", FirstName = "Борис" };
            var firstGarage = new Garage { Number = "FEE-01", PeopleCount = 1, FloorCount = 1, Owner = firstOwner };
            var secondGarage = new Garage { Number = "FEE-02", PeopleCount = 1, FloorCount = 1, Owner = secondOwner };
            seedContext.AddRange(incomeType, firstOwner, secondOwner, firstGarage, secondGarage);
            seedContext.Accruals.AddRange(
                CreateAccrual(firstGarage, incomeType, month, 100m),
                CreateAccrual(secondGarage, incomeType, month, 50m));
            seedContext.FinancialOperations.AddRange(
                CreatePayment(firstGarage, incomeType, month, 40m, "FEE-PAY-1"),
                CreatePayment(secondGarage, incomeType, month, 70m, "FEE-PAY-2"),
                CreatePayment(null, incomeType, month, 5m, "FEE-PAY-WITHOUT-GARAGE"));
            await seedContext.SaveChangesAsync();

            var capture = new ReaderCommandCapture();
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseNpgsql(database.ConnectionString)
                .AddInterceptors(capture)
                .Options;
            await using var context = new GarageBalanceDbContext(options);

            var result = await new EfFeeReportQuery(context).GetFeeReportPageAsync(
                [incomeType.Id],
                false,
                new ReportSort("debt", true),
                0,
                1,
                CancellationToken.None);

            Assert.Equal(2, result.GarageRowCount);
            Assert.Equal(60m, result.DebtTotal);
            Assert.Equal(150m, result.AccrualTotals[incomeType.Id]);
            Assert.Equal(115m, result.CollectedTotals[incomeType.Id]);
            var garageRow = Assert.Single(result.GarageRows);
            Assert.Equal(firstGarage.Id, garageRow.GarageId);
            Assert.Equal(60m, garageRow.Debt);
            var debtorRow = Assert.Single(result.DebtorRows);
            Assert.Equal(firstGarage.Id, debtorRow.GarageId);
            Assert.Equal(60m, debtorRow.Debt);

            var command = Assert.Single(capture.Commands);
            Assert.Equal(1, CountOccurrences(command, "FROM accruals"));
            Assert.Equal(1, CountOccurrences(command, "FROM financial_operations"));
            Assert.Contains("FROM garage_page", command, StringComparison.Ordinal);
            Assert.Contains("FROM debtor_page", command, StringComparison.Ordinal);
            Assert.Contains("FROM summary_rows", command, StringComparison.Ordinal);
        }
    }

    [PostgreSqlFact]
    public async Task FeeCampaignPageUsesTheSameSingleCommandPipeline()
    {
        var month = new DateOnly(2042, 6, 1);
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var incomeType = new IncomeType { Name = $"Campaign destination {Guid.NewGuid():N}" };
            var garage = new Garage { Number = "CAMPAIGN-01", PeopleCount = 1, FloorCount = 1 };
            var campaign = new FeeCampaign
            {
                Name = "Сбор на ремонт",
                IncomeType = incomeType,
                ContributionAmount = 500m,
                TargetAmount = 500m,
                StartsOn = month,
                AppliesToAllGarages = true,
                OverdueGraceDays = 30
            };
            var accrual = CreateAccrual(garage, incomeType, month, 500m);
            accrual.FeeCampaign = campaign;
            accrual.Source = AccrualSources.FeeCampaign;
            var payment = CreatePayment(garage, incomeType, month, 200m, "CAMPAIGN-PAY");
            seedContext.AddRange(
                incomeType,
                garage,
                campaign,
                accrual,
                payment,
                new AccrualPaymentAllocation { Accrual = accrual, FinancialOperation = payment, Amount = 200m });
            await seedContext.SaveChangesAsync();

            var capture = new ReaderCommandCapture();
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseNpgsql(database.ConnectionString)
                .AddInterceptors(capture)
                .Options;
            await using var context = new GarageBalanceDbContext(options);

            var result = await new EfFeeReportQuery(context).GetFeeReportPageAsync(
                [campaign.Id],
                true,
                new ReportSort("garageNumber", false),
                0,
                25,
                CancellationToken.None);

            Assert.Equal(500m, result.AccrualTotals[campaign.Id]);
            Assert.Equal(200m, result.CollectedTotals[campaign.Id]);
            Assert.Equal(300m, result.DebtTotal);
            Assert.Equal(300m, Assert.Single(result.GarageRows).Debt);
            Assert.Equal(300m, Assert.Single(result.DebtorRows).Debt);
            var command = Assert.Single(capture.Commands);
            Assert.Contains("FROM accrual_payment_allocations", command, StringComparison.Ordinal);
            Assert.Contains("FROM garage_page", command, StringComparison.Ordinal);
            Assert.Contains("FROM summary_rows", command, StringComparison.Ordinal);
        }
    }

    private static Accrual CreateAccrual(Garage garage, IncomeType incomeType, DateOnly month, decimal amount) =>
        new()
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = month,
            DueDate = month.AddMonths(1).AddDays(-1),
            OverdueFromDate = month.AddMonths(1),
            Amount = amount,
            Source = "fee_query_integration_test"
        };

    private static FinancialOperation CreatePayment(
        Garage? garage,
        IncomeType incomeType,
        DateOnly month,
        decimal amount,
        string documentNumber) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = month.AddDays(10),
            AccountingMonth = month,
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType,
            DocumentNumber = documentNumber,
            CreatedAtUtc = new DateTimeOffset(month.AddDays(10).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)
        };

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
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
