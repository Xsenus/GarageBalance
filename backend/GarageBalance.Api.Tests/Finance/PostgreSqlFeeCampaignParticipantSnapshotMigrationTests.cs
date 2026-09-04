using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data.Migrations;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFeeCampaignParticipantSnapshotMigrationTests
{
    private const string PreviousMigration = "20260904064206_AddFinancialOperationConcurrencyVersion";

    [Fact]
    public void RawSqlOperations_AreTerminatedForIdempotentMigrationScripts()
    {
        var migration = new SnapshotFeeCampaignParticipants();
        var sqlOperations = migration.UpOperations.OfType<SqlOperation>().ToArray();

        Assert.NotEmpty(sqlOperations);
        Assert.All(sqlOperations, operation => Assert.EndsWith(";", operation.Sql.TrimEnd(), StringComparison.Ordinal));
    }

    [PostgreSqlFact]
    public async Task MigrationSnapshotsOnlyGaragesKnownAtAnnouncementAndPreservesHistoricalAccruals()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var campaignId = Guid.NewGuid();
        var earlyGarageId = Guid.NewGuid();
        var archivedGarageId = Guid.NewGuid();
        var lateGarageId = Guid.NewGuid();
        var lateWithAccrualGarageId = Guid.NewGuid();
        var announcedAt = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

        await using (var setupContext = database.CreateContext())
        {
            await setupContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            var incomeTypeId = await setupContext.IncomeTypes
                .Where(item => item.Code == "other_income")
                .Select(item => item.Id)
                .SingleAsync();
            var garages = new[]
            {
                new Garage { Id = earlyGarageId, Number = "SNAPSHOT-EARLY", PeopleCount = 1, FloorCount = 1, CreatedAtUtc = announcedAt.AddDays(-1), UpdatedAtUtc = announcedAt.AddDays(-1) },
                new Garage { Id = archivedGarageId, Number = "SNAPSHOT-ARCHIVED", PeopleCount = 1, FloorCount = 1, IsArchived = true, CreatedAtUtc = announcedAt.AddDays(-1), UpdatedAtUtc = announcedAt.AddDays(-1) },
                new Garage { Id = lateGarageId, Number = "SNAPSHOT-LATE", PeopleCount = 1, FloorCount = 1, CreatedAtUtc = announcedAt.AddDays(1), UpdatedAtUtc = announcedAt.AddDays(1) },
                new Garage { Id = lateWithAccrualGarageId, Number = "SNAPSHOT-HISTORY", PeopleCount = 1, FloorCount = 1, CreatedAtUtc = announcedAt.AddDays(1), UpdatedAtUtc = announcedAt.AddDays(1) }
            };
            var campaign = new FeeCampaign
            {
                Id = campaignId,
                Name = "Исторический сбор",
                IncomeTypeId = incomeTypeId,
                ContributionAmount = 100m,
                TargetAmount = 300m,
                StartsOn = new DateOnly(2026, 8, 1),
                AppliesToAllGarages = true,
                CreatedAtUtc = announcedAt,
                UpdatedAtUtc = announcedAt
            };
            setupContext.AddRange(garages);
            setupContext.Add(campaign);
            setupContext.Accruals.Add(new Accrual
            {
                GarageId = lateWithAccrualGarageId,
                IncomeTypeId = incomeTypeId,
                FeeCampaignId = campaignId,
                AccountingMonth = new DateOnly(2026, 8, 1),
                DueDate = new DateOnly(2026, 8, 31),
                OverdueFromDate = new DateOnly(2026, 9, 1),
                Amount = 100m,
                Source = AccrualSources.FeeCampaign,
                CreatedAtUtc = announcedAt,
                UpdatedAtUtc = announcedAt
            });
            await setupContext.SaveChangesAsync();
            await setupContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var participantIds = await verificationContext.FeeCampaignGarages
            .Where(item => item.FeeCampaignId == campaignId)
            .Select(item => item.GarageId)
            .OrderBy(item => item)
            .ToArrayAsync();

        Assert.Equal(
            new[] { earlyGarageId, lateWithAccrualGarageId }.Order().ToArray(),
            participantIds);
        Assert.DoesNotContain(archivedGarageId, participantIds);
        Assert.DoesNotContain(lateGarageId, participantIds);
    }
}
