using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFeeCampaignListProjectionIntegrationTests
{
    [PostgreSqlFact]
    public async Task GetListAsync_UsesBoundedCompactProjectionForDisplayedCampaignData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fund = new Fund
        {
            Name = "Фонд компактного сбора 57721",
            NormalizedName = "фонд компактного сбора 57721",
            Balance = 9876m,
            SortOrder = 99
        };
        var incomeType = new IncomeType
        {
            Name = "Услуга компактного сбора 57721",
            Code = "compact_fee_campaign_57721",
            DestinationFund = fund
        };
        var owner = new Owner
        {
            LastName = "Не загружается",
            FirstName = "Владелец",
            Phone = "+7 900 000-00-00",
            Address = "Не должен загружаться"
        };
        var firstGarage = new Garage
        {
            Number = "FEE-COMPACT-02",
            PeopleCount = 8,
            FloorCount = 4,
            StartingBalance = 500m,
            InitialWaterMeterValue = 123m,
            Comment = "Не должен загружаться из гаража",
            Owner = owner
        };
        var secondGarage = new Garage
        {
            Number = "FEE-COMPACT-01",
            PeopleCount = 6,
            FloorCount = 3
        };
        var campaign = new FeeCampaign
        {
            Name = "Компактный сбор %_ 57721",
            IncomeType = incomeType,
            Goal = "Цель отображается",
            ContributionAmount = 750m,
            TargetAmount = 7500m,
            StartsOn = new DateOnly(2052, 3, 1),
            EndsOn = new DateOnly(2052, 9, 30),
            AppliesToAllGarages = false,
            OverdueGraceDays = 45,
            ClosedAtUtc = new DateTimeOffset(2052, 8, 15, 10, 30, 0, TimeSpan.Zero),
            IsClosedEarly = true,
            ClosureComment = "Комментарий закрытия отображается"
        };
        campaign.ParticipantGarages.Add(new FeeCampaignGarage { FeeCampaign = campaign, Garage = firstGarage });
        campaign.ParticipantGarages.Add(new FeeCampaignGarage { FeeCampaign = campaign, Garage = secondGarage });
        await using (var setupContext = database.CreateContext())
        {
            setupContext.FeeCampaigns.AddRange(
                campaign,
                new FeeCampaign
                {
                    Name = "Второй компактный сбор 57721",
                    IncomeType = new IncomeType
                    {
                        Name = "Вторая услуга компактного сбора 57721",
                        Code = "compact_fee_campaign_second_57721"
                    },
                    ContributionAmount = 10m,
                    TargetAmount = 100m,
                    StartsOn = new DateOnly(2052, 2, 1),
                    AppliesToAllGarages = true,
                    OverdueGraceDays = 30
                });
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfFeeCampaignRepository(queryContext);

        var result = await repository.GetListAsync(null, false, 1, CancellationToken.None);

        var actual = Assert.Single(result);
        Assert.Equal(campaign.Id, actual.Id);
        Assert.Equal("Компактный сбор %_ 57721", actual.Name);
        Assert.Equal("Услуга компактного сбора 57721", actual.IncomeType.Name);
        Assert.Equal(fund.Id, actual.IncomeType.DestinationFundId);
        Assert.Equal("Фонд компактного сбора 57721", actual.IncomeType.DestinationFund!.Name);
        Assert.Equal("Цель отображается", actual.Goal);
        Assert.Equal(750m, actual.ContributionAmount);
        Assert.Equal(7500m, actual.TargetAmount);
        Assert.Equal(new DateOnly(2052, 3, 1), actual.StartsOn);
        Assert.Equal(new DateOnly(2052, 9, 30), actual.EndsOn);
        Assert.False(actual.AppliesToAllGarages);
        Assert.Equal(
            [secondGarage.Id, firstGarage.Id],
            actual.ParticipantGarages.OrderBy(item => item.Garage.Number).Select(item => item.GarageId));
        Assert.All(actual.ParticipantGarages, item => Assert.Same(actual, item.FeeCampaign));
        Assert.Equal(45, actual.OverdueGraceDays);
        Assert.True(actual.IsClosedEarly);
        Assert.Equal("Комментарий закрытия отображается", actual.ClosureComment);
        Assert.Empty(queryContext.ChangeTracker.Entries());

        var command = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fee_campaign_garages", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Number", command, StringComparison.Ordinal);
        Assert.DoesNotContain("PeopleCount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("FloorCount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("StartingBalance", command, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialWaterMeterValue", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Phone", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Address", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Code", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Balance", command, StringComparison.Ordinal);
        Assert.DoesNotContain("SortOrder", command, StringComparison.Ordinal);
        Assert.DoesNotContain("ClosedByUserId", command, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Version", command, StringComparison.Ordinal);

        var literalSearch = await repository.GetListAsync("%_", false, 10, CancellationToken.None);

        Assert.Equal(campaign.Id, Assert.Single(literalSearch).Id);
        var searchCommand = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("ILIKE", searchCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE '\\'", searchCommand, StringComparison.Ordinal);
        Assert.Contains("LIMIT", searchCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PeopleCount", searchCommand, StringComparison.Ordinal);
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
        var repository = new EfFeeCampaignRepository(queryContext);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.GetListAsync(null, false, 10, cancellation.Token));

        Assert.Empty(capture.TakeCommandsAndClear());
        Assert.Empty(queryContext.ChangeTracker.Entries());
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        private readonly List<string> commands = [];

        public IReadOnlyList<string> TakeCommandsAndClear()
        {
            var result = commands.ToArray();
            commands.Clear();
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
