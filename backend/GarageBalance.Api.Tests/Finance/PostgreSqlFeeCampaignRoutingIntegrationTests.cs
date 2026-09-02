using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFeeCampaignRoutingIntegrationTests
{
    [PostgreSqlFact]
    public async Task Campaigns_GenerateForAllOrUpdatedSelectionAndLockHistoricalParticipants()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var owner = new Owner { LastName = "Иванов", FirstName = "Иван" };
        var firstGarage = new Garage { Number = "FEE-ALL-1", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var secondGarage = new Garage { Number = "FEE-ALL-2", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var thirdGarage = new Garage { Number = "FEE-ALL-3", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var archivedGarage = new Garage { Number = "FEE-ARCHIVED", PeopleCount = 1, FloorCount = 1, Owner = owner, IsArchived = true };
        Guid otherIncomeId;
        Guid allCampaignId;
        Guid selectedCampaignId;

        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(owner, firstGarage, secondGarage, thirdGarage, archivedGarage);
            await setupContext.SaveChangesAsync();
            otherIncomeId = await setupContext.IncomeTypes
                .Where(item => item.Code == "other_income")
                .Select(item => item.Id)
                .SingleAsync();
            var dictionaries = DictionaryServiceTestFactory.Create(setupContext);
            var allCampaign = await dictionaries.CreateFeeCampaignAsync(
                CampaignRequest("Сбор для всех", otherIncomeId, true, []),
                null,
                CancellationToken.None);
            var selectedCampaign = await dictionaries.CreateFeeCampaignAsync(
                CampaignRequest("Выборочный сбор", otherIncomeId, false, [firstGarage.Id]),
                null,
                CancellationToken.None);
            Assert.True(allCampaign.Succeeded, allCampaign.ErrorMessage);
            Assert.True(selectedCampaign.Succeeded, selectedCampaign.ErrorMessage);
            allCampaignId = allCampaign.Value!.Id;
            selectedCampaignId = selectedCampaign.Value!.Id;

            var changedBeforeGeneration = await dictionaries.UpdateFeeCampaignAsync(
                selectedCampaignId,
                CampaignRequest("Выборочный сбор", otherIncomeId, false, [firstGarage.Id, secondGarage.Id]),
                null,
                CancellationToken.None);
            Assert.True(changedBeforeGeneration.Succeeded, changedBeforeGeneration.ErrorMessage);
            Assert.Equal([firstGarage.Id, secondGarage.Id], changedBeforeGeneration.Value!.ParticipantGarageIds);
        }

        await using (var generationContext = database.CreateContext())
        {
            var finance = FinanceServiceTestFactory.Create(generationContext);
            var allResult = await finance.GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(allCampaignId, new DateOnly(2026, 6, 1), null),
                null,
                CancellationToken.None);
            var selectedResult = await finance.GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(selectedCampaignId, new DateOnly(2026, 6, 1), null),
                null,
                CancellationToken.None);

            Assert.True(allResult.Succeeded, allResult.ErrorMessage);
            Assert.Equal(3, allResult.Value!.CreatedCount);
            Assert.DoesNotContain(allResult.Value.CreatedAccruals, item => item.GarageId == archivedGarage.Id);
            Assert.True(selectedResult.Succeeded, selectedResult.ErrorMessage);
            Assert.Equal(2, selectedResult.Value!.CreatedCount);
            Assert.Equal(
                new[] { firstGarage.Id, secondGarage.Id }.Order().ToArray(),
                selectedResult.Value.CreatedAccruals.Select(item => item.GarageId).Order().ToArray());
        }

        await using (var updateContext = database.CreateContext())
        {
            var dictionaries = DictionaryServiceTestFactory.Create(updateContext);
            var rejected = await dictionaries.UpdateFeeCampaignAsync(
                selectedCampaignId,
                CampaignRequest("Выборочный сбор", otherIncomeId, false, [thirdGarage.Id]),
                null,
                CancellationToken.None);

            Assert.False(rejected.Succeeded);
            Assert.Equal("fee_campaign_participants_locked", rejected.ErrorCode);
            var storedParticipants = await updateContext.FeeCampaignGarages
                .Where(item => item.FeeCampaignId == selectedCampaignId)
                .Select(item => item.GarageId)
                .Order()
                .ToArrayAsync();
            Assert.Equal(new[] { firstGarage.Id, secondGarage.Id }.Order().ToArray(), storedParticipants);
            Assert.Equal(2, await updateContext.Accruals.CountAsync(item => item.FeeCampaignId == selectedCampaignId));
            Assert.Single(updateContext.AuditEvents, item =>
                item.Action == "dictionary.fee_campaign_updated" && item.EntityId == selectedCampaignId.ToString());
        }
    }

    [PostgreSqlFact]
    public async Task MigratedDatabase_RoutesDifferentCampaignsToStableDestinationAndRejectsCampaignDuplicate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var builder = new AccountingTestDataBuilder();
        var garage = builder.BuildGarage(number: "FEE-PG-1");
        var legacyIncomeType = new IncomeType { Name = "Старое назначение сбора", Code = "legacy_fee" };
        var firstCampaign = CreateCampaign("Сбор на ворота", legacyIncomeType, 500m);
        var secondCampaign = CreateCampaign("Сбор на камеры", legacyIncomeType, 700m);
        Guid firstPaymentId;

        Guid destinationId;
        await using (var setupContext = database.CreateContext())
        {
            var destination = await setupContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            destination.Name = "Переименованное назначение доходов";
            destinationId = destination.Id;
            setupContext.AddRange(garage, legacyIncomeType, firstCampaign, secondCampaign);
            await setupContext.SaveChangesAsync();
        }

        await using (var createContext = database.CreateContext())
        {
            var service = FinanceServiceTestFactory.Create(createContext);
            var first = await service.GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(firstCampaign.Id, new DateOnly(2026, 9, 1), null),
                null,
                CancellationToken.None);
            var second = await service.GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(secondCampaign.Id, new DateOnly(2026, 9, 1), null),
                null,
                CancellationToken.None);

            Assert.True(first.Succeeded, first.ErrorMessage);
            Assert.True(second.Succeeded, second.ErrorMessage);
            Assert.Equal(destinationId, first.Value!.IncomeTypeId);
            Assert.Equal(destinationId, second.Value!.IncomeTypeId);
            Assert.Equal("Переименованное назначение доходов", first.Value.IncomeTypeName);
            Assert.Equal(firstCampaign.Id, Assert.Single(first.Value.CreatedAccruals).FeeCampaignId);
            Assert.Equal(secondCampaign.Id, Assert.Single(second.Value.CreatedAccruals).FeeCampaignId);

            var payment = await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    garage.Id,
                    destinationId,
                    new DateOnly(2026, 9, 15),
                    new DateOnly(2026, 9, 1),
                    100m,
                    "FEE-STABLE-DESTINATION",
                    null,
                    FeeCampaignId: firstCampaign.Id),
                null,
                CancellationToken.None);
            Assert.True(payment.Succeeded, payment.ErrorMessage);
            firstPaymentId = payment.Value!.Id;
        }

        await using (var verificationContext = database.CreateContext())
        {
            var accruals = await verificationContext.Accruals
                .AsNoTracking()
                .Include(item => item.IncomeType)
                .Include(item => item.FeeCampaign)
                .OrderBy(item => item.Amount)
                .ToListAsync();
            Assert.Equal(2, accruals.Count);
            Assert.All(accruals, item =>
            {
                Assert.Equal("other_income", item.IncomeType.Code);
                Assert.NotNull(item.IncomeType.DestinationFundId);
                Assert.NotNull(item.FeeCampaign);
            });
            Assert.All(
                await verificationContext.FeeCampaigns
                    .AsNoTracking()
                    .Where(item => item.Id == firstCampaign.Id || item.Id == secondCampaign.Id)
                    .ToArrayAsync(),
                item => Assert.Equal(destinationId, item.IncomeTypeId));
            var persistedPayment = await verificationContext.FinancialOperations
                .AsNoTracking()
                .SingleAsync(item => item.Id == firstPaymentId);
            Assert.Equal(firstCampaign.Id, persistedPayment.FeeCampaignId);
            Assert.Equal(destinationId, persistedPayment.IncomeTypeId);
            var persistedAllocation = Assert.Single(await verificationContext.AccrualPaymentAllocations
                .AsNoTracking()
                .Include(item => item.Accrual)
                .Where(item => item.IsActive && item.FinancialOperationId == firstPaymentId)
                .ToArrayAsync());
            Assert.Equal(firstCampaign.Id, persistedAllocation.Accrual.FeeCampaignId);
            Assert.Equal(100m, persistedAllocation.Amount);

            verificationContext.Accruals.Add(new Accrual
            {
                GarageId = garage.Id,
                IncomeTypeId = destinationId,
                FeeCampaignId = firstCampaign.Id,
                AccountingMonth = new DateOnly(2026, 10, 1),
                DueDate = new DateOnly(2026, 10, 31),
                OverdueFromDate = new DateOnly(2026, 12, 1),
                Amount = 500m,
                Source = AccrualSources.FeeCampaign
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => verificationContext.SaveChangesAsync());
        }
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_NormalizesLegacyCampaignAndCampaignAwareRebuildPreservesLegacyTaggedPayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid garageId;
        Guid stableIncomeTypeId;
        Guid stableFundId;
        Guid legacyFundId;
        Guid principalId;
        Guid legacyPaymentId;
        decimal stableFundBalanceBefore;
        await using (var legacyContext = database.CreateContext())
        {
            var stableIncomeType = await legacyContext.IncomeTypes
                .Include(item => item.DestinationFund)
                .SingleAsync(item => item.Code == "other_income");
            var legacyFund = new Fund
            {
                Name = "Legacy fee destination",
                NormalizedName = "legacy fee destination",
                Balance = 100m
            };
            var legacyIncomeType = new IncomeType
            {
                Name = "Legacy fee income",
                Code = "legacy_fee_migration",
                DestinationFund = legacyFund,
                DestinationFundId = legacyFund.Id
            };
            var owner = new Owner { LastName = "Legacy", FirstName = "Destination" };
            var garage = new Garage { Number = "FEE-LEGACY-DEST", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Legacy destination campaign", legacyIncomeType, 500m);
            campaign.TargetAmount = 5000m;
            var principal = CreateCampaignAccrual(
                garage,
                stableIncomeType,
                campaign,
                new DateOnly(2026, 6, 1),
                500m);
            var legacyPayment = CreateCampaignIncome(
                garage,
                legacyIncomeType,
                campaign,
                new DateOnly(2026, 6, 15),
                100m);
            var legacyAssignment = new FundOperation
            {
                Fund = legacyFund,
                SourceFinancialOperation = legacyPayment,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 100m,
                BalanceBefore = 0m,
                BalanceAfter = 100m,
                Reason = "Legacy tagged fee destination"
            };
            legacyContext.AddRange(
                owner,
                garage,
                legacyFund,
                legacyIncomeType,
                campaign,
                principal,
                legacyPayment,
                legacyAssignment);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            stableIncomeTypeId = stableIncomeType.Id;
            stableFundId = stableIncomeType.DestinationFundId!.Value;
            legacyFundId = legacyFund.Id;
            stableFundBalanceBefore = stableIncomeType.DestinationFund!.Balance;
            principalId = principal.Id;
            legacyPaymentId = legacyPayment.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        Guid newPaymentId;
        await using (var paymentContext = database.CreateContext())
        {
            Assert.Equal(
                stableIncomeTypeId,
                (await paymentContext.FeeCampaigns
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == campaignId)).IncomeTypeId);
            var payment = await FinanceServiceTestFactory.Create(paymentContext).CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    garageId,
                    stableIncomeTypeId,
                    new DateOnly(2026, 7, 15),
                    new DateOnly(2026, 7, 1),
                    100m,
                    "FEE-STABLE-AFTER-MIGRATION",
                    null,
                    FeeCampaignId: campaignId),
                null,
                CancellationToken.None);
            Assert.True(payment.Succeeded, payment.ErrorMessage);
            newPaymentId = payment.Value!.Id;
        }

        await using var verificationContext = database.CreateContext();
        var allocations = await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item =>
                item.IsActive &&
                item.AccrualId == principalId &&
                (item.FinancialOperationId == legacyPaymentId || item.FinancialOperationId == newPaymentId))
            .ToArrayAsync();
        Assert.Equal(2, allocations.Length);
        Assert.Equal(200m, allocations.Sum(item => item.Amount));
        Assert.Equal(100m, (await verificationContext.Funds.SingleAsync(item => item.Id == legacyFundId)).Balance);
        Assert.Equal(
            stableFundBalanceBefore + 100m,
            (await verificationContext.Funds.SingleAsync(item => item.Id == stableFundId)).Balance);
        Assert.Single(await verificationContext.FundOperations
            .Where(item => !item.IsCanceled && item.SourceFinancialOperationId == legacyPaymentId)
            .ToArrayAsync());
        Assert.Single(await verificationContext.FundOperations
            .Where(item => !item.IsCanceled && item.SourceFinancialOperationId == newPaymentId)
            .ToArrayAsync());
        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var campaignRow = Assert.Single(worksheet.Value!.Rows, row => row.FeeCampaignId == campaignId);
        Assert.Equal(200m, campaignRow.IncomeAmount);
        Assert.Equal(300m, campaignRow.Debt);
        Assert.Equal(500m, worksheet.Value.AccrualTotal);
        Assert.Equal(200m, worksheet.Value.IncomeTotal);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
        Assert.DoesNotContain(worksheet.Value.Rows, row => row.FeeCampaignId == null && row.AdvanceAmount > 0m);
        Assert.Equal(worksheet.Value.AdvanceTotal, worksheet.Value.Rows.Sum(row => row.AdvanceAmount));
        Assert.Equal(300m, worksheet.Value.DebtTotal);
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_NormalizesLegacyPrincipalAndNewStablePaymentPreservesBothAllocations()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid garageId;
        Guid stableIncomeTypeId;
        Guid principalId;
        Guid legacyPaymentId;
        await using (var legacyContext = database.CreateContext())
        {
            var stableIncomeType = await legacyContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var legacyFund = new Fund
            {
                Name = "Legacy principal fund",
                NormalizedName = "legacy principal fund"
            };
            var legacyIncomeType = new IncomeType
            {
                Name = "Legacy principal income",
                Code = "legacy_principal_income",
                DestinationFund = legacyFund
            };
            var owner = new Owner { LastName = "Legacy", FirstName = "Principal destination" };
            var garage = new Garage { Number = "FEE-LEGACY-PRINCIPAL", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Legacy principal campaign", legacyIncomeType, 500m);
            campaign.TargetAmount = 5000m;
            var principal = CreateCampaignAccrual(
                garage,
                legacyIncomeType,
                campaign,
                new DateOnly(2026, 6, 1),
                500m);
            var legacyPayment = CreateCampaignIncome(
                garage,
                legacyIncomeType,
                campaign,
                new DateOnly(2026, 6, 15),
                100m);
            legacyContext.AddRange(
                owner,
                garage,
                legacyFund,
                legacyIncomeType,
                campaign,
                principal,
                legacyPayment);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            stableIncomeTypeId = stableIncomeType.Id;
            principalId = principal.Id;
            legacyPaymentId = legacyPayment.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        Guid newPaymentId;
        await using (var paymentContext = database.CreateContext())
        {
            var repairedPrincipal = await paymentContext.Accruals
                .AsNoTracking()
                .SingleAsync(item => item.Id == principalId);
            Assert.Equal(stableIncomeTypeId, repairedPrincipal.IncomeTypeId);
            var payment = await FinanceServiceTestFactory.Create(paymentContext).CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    garageId,
                    stableIncomeTypeId,
                    new DateOnly(2026, 7, 15),
                    new DateOnly(2026, 7, 1),
                    100m,
                    "FEE-NEW-STABLE-PRINCIPAL",
                    null,
                    FeeCampaignId: campaignId),
                null,
                CancellationToken.None);
            Assert.True(payment.Succeeded, payment.ErrorMessage);
            newPaymentId = payment.Value!.Id;
        }

        await using var verificationContext = database.CreateContext();
        var allocations = await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item =>
                item.IsActive &&
                item.AccrualId == principalId &&
                (item.FinancialOperationId == legacyPaymentId || item.FinancialOperationId == newPaymentId))
            .ToArrayAsync();
        Assert.Equal(2, allocations.Length);
        Assert.Equal(200m, allocations.Sum(item => item.Amount));
        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var campaignRow = Assert.Single(worksheet.Value!.Rows, row => row.FeeCampaignId == campaignId);
        Assert.Equal(200m, campaignRow.IncomeAmount);
        Assert.Equal(300m, campaignRow.Debt);
        Assert.DoesNotContain(worksheet.Value.Rows, row => row.FeeCampaignId == null && row.AdvanceAmount > 0m);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
        Assert.Equal(worksheet.Value.AdvanceTotal, worksheet.Value.Rows.Sum(row => row.AdvanceAmount));
        Assert.Equal(300m, worksheet.Value.DebtTotal);
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_PreservesCrossDestinationUntaggedEarmarkWhenStablePaymentRebuilds()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid garageId;
        Guid stableIncomeTypeId;
        Guid campaignPrincipalId;
        Guid ordinaryAccrualId;
        Guid legacyPaymentId;
        await using (var legacyContext = database.CreateContext())
        {
            var stableIncomeType = await legacyContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var legacyFund = new Fund
            {
                Name = "Legacy split fund",
                NormalizedName = "legacy split fund"
            };
            var legacyIncomeType = new IncomeType
            {
                Name = "Legacy split income",
                Code = "legacy_split_income",
                DestinationFund = legacyFund
            };
            var owner = new Owner { LastName = "Legacy", FirstName = "Split allocation" };
            var garage = new Garage { Number = "FEE-LEGACY-SPLIT", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Legacy split campaign", legacyIncomeType, 500m);
            campaign.TargetAmount = 5000m;
            var campaignPrincipal = CreateCampaignAccrual(
                garage,
                legacyIncomeType,
                campaign,
                new DateOnly(2026, 6, 1),
                500m);
            var ordinaryAccrual = new Accrual
            {
                Garage = garage,
                IncomeType = legacyIncomeType,
                AccountingMonth = new DateOnly(2026, 6, 1),
                DueDate = new DateOnly(2026, 6, 30),
                OverdueFromDate = new DateOnly(2026, 7, 31),
                Amount = 60m,
                Source = AccrualSources.Manual,
                Basis = "Legacy ordinary debt"
            };
            var legacyPayment = CreateUntaggedIncome(
                garage,
                legacyIncomeType,
                new DateOnly(2026, 6, 15),
                160m);
            var ordinaryAllocation = new AccrualPaymentAllocation
            {
                FinancialOperation = legacyPayment,
                Accrual = ordinaryAccrual,
                Amount = 60m
            };
            var campaignAllocation = new AccrualPaymentAllocation
            {
                FinancialOperation = legacyPayment,
                Accrual = campaignPrincipal,
                Amount = 100m
            };
            legacyContext.AddRange(
                owner,
                garage,
                legacyFund,
                legacyIncomeType,
                campaign,
                campaignPrincipal,
                ordinaryAccrual,
                legacyPayment,
                ordinaryAllocation,
                campaignAllocation);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            stableIncomeTypeId = stableIncomeType.Id;
            campaignPrincipalId = campaignPrincipal.Id;
            ordinaryAccrualId = ordinaryAccrual.Id;
            legacyPaymentId = legacyPayment.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using (var mutationContext = database.CreateContext())
        {
            var legacyPayment = await mutationContext.FinancialOperations
                .AsNoTracking()
                .SingleAsync(operation => operation.Id == legacyPaymentId);
            var finance = FinanceServiceTestFactory.Create(mutationContext);
            var update = await finance.UpdateIncomeAsync(
                legacyPaymentId,
                new CreateIncomeOperationRequest(
                    garageId,
                    legacyPayment.IncomeTypeId!.Value,
                    legacyPayment.OperationDate,
                    legacyPayment.AccountingMonth,
                    170m,
                    "MUTATION-MUST-BE-REJECTED",
                    null),
                null,
                CancellationToken.None);
            Assert.False(update.Succeeded);
            Assert.Equal("targeted_income_update_forbidden", update.ErrorCode);

            var cancel = await finance.CancelOperationAsync(
                legacyPaymentId,
                new CancelFinanceEntryRequest("Нельзя разрушать legacy earmark"),
                null,
                CancellationToken.None);
            Assert.False(cancel.Succeeded);
            Assert.Equal("fee_campaign_payment_mutation_forbidden", cancel.ErrorCode);
        }

        Guid newPaymentId;
        await using (var paymentContext = database.CreateContext())
        {
            var payment = await FinanceServiceTestFactory.Create(paymentContext).CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    garageId,
                    stableIncomeTypeId,
                    new DateOnly(2026, 7, 15),
                    new DateOnly(2026, 7, 1),
                    100m,
                    "FEE-STABLE-AFTER-SPLIT",
                    null,
                    FeeCampaignId: campaignId),
                null,
                CancellationToken.None);
            Assert.True(payment.Succeeded, payment.ErrorMessage);
            newPaymentId = payment.Value!.Id;
        }

        await using var verificationContext = database.CreateContext();
        Assert.Equal(
            100m,
            await verificationContext.AccrualPaymentAllocations
                .Where(item => item.IsActive && item.FinancialOperationId == legacyPaymentId && item.AccrualId == campaignPrincipalId)
                .SumAsync(item => item.Amount));
        Assert.Equal(
            60m,
            await verificationContext.AccrualPaymentAllocations
                .Where(item => item.IsActive && item.FinancialOperationId == legacyPaymentId && item.AccrualId == ordinaryAccrualId)
                .SumAsync(item => item.Amount));
        Assert.Equal(
            100m,
            await verificationContext.AccrualPaymentAllocations
                .Where(item => item.IsActive && item.FinancialOperationId == newPaymentId && item.AccrualId == campaignPrincipalId)
                .SumAsync(item => item.Amount));
        Assert.Equal(
            160m,
            await verificationContext.AccrualPaymentAllocations
                .Where(item => item.IsActive && item.FinancialOperationId == legacyPaymentId)
                .SumAsync(item => item.Amount));
        var preservedLegacyPayment = await verificationContext.FinancialOperations
            .AsNoTracking()
            .SingleAsync(operation => operation.Id == legacyPaymentId);
        Assert.False(preservedLegacyPayment.IsCanceled);
        Assert.Equal(160m, preservedLegacyPayment.Amount);
        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var campaignRow = Assert.Single(worksheet.Value!.Rows, row => row.FeeCampaignId == campaignId);
        Assert.Equal(200m, campaignRow.IncomeAmount);
        Assert.Equal(300m, campaignRow.Debt);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
        Assert.Equal(worksheet.Value.AdvanceTotal, worksheet.Value.Rows.Sum(row => row.AdvanceAmount));
        Assert.Equal(300m, worksheet.Value.DebtTotal);
    }

    [PostgreSqlFact]
    public async Task SameRouteLegacyMixedEarmark_SurvivesLaterOrdinaryAccrualRebuild()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid campaignId;
        Guid garageId;
        Guid incomeTypeId;
        Guid campaignPrincipalId;
        Guid paidOrdinaryAccrualId;
        Guid legacyPaymentId;
        await using (var setupContext = database.CreateContext())
        {
            var incomeType = await setupContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var owner = new Owner { LastName = "Legacy", FirstName = "Same route split" };
            var garage = new Garage { Number = "FEE-SAME-ROUTE-SPLIT", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Same route legacy campaign", incomeType, 500m);
            campaign.TargetAmount = 5000m;
            var campaignPrincipal = CreateCampaignAccrual(
                garage,
                incomeType,
                campaign,
                new DateOnly(2026, 6, 1),
                500m);
            var paidOrdinaryAccrual = new Accrual
            {
                Garage = garage,
                IncomeType = incomeType,
                AccountingMonth = new DateOnly(2026, 6, 1),
                DueDate = new DateOnly(2026, 6, 30),
                OverdueFromDate = new DateOnly(2026, 7, 31),
                Amount = 60m,
                Source = AccrualSources.Manual,
                Basis = "Same route paid ordinary debt"
            };
            var legacyPayment = CreateUntaggedIncome(
                garage,
                incomeType,
                new DateOnly(2026, 6, 15),
                160m);
            var ordinaryAllocation = new AccrualPaymentAllocation
            {
                FinancialOperation = legacyPayment,
                Accrual = paidOrdinaryAccrual,
                Amount = 60m
            };
            var campaignAllocation = new AccrualPaymentAllocation
            {
                FinancialOperation = legacyPayment,
                Accrual = campaignPrincipal,
                Amount = 100m
            };
            setupContext.AddRange(
                owner,
                garage,
                campaign,
                campaignPrincipal,
                paidOrdinaryAccrual,
                legacyPayment,
                ordinaryAllocation,
                campaignAllocation);
            await setupContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            incomeTypeId = incomeType.Id;
            campaignPrincipalId = campaignPrincipal.Id;
            paidOrdinaryAccrualId = paidOrdinaryAccrual.Id;
            legacyPaymentId = legacyPayment.Id;
        }

        Guid laterOrdinaryAccrualId;
        await using (var rebuildContext = database.CreateContext())
        {
            var created = await FinanceServiceTestFactory.Create(rebuildContext).CreateAccrualAsync(
                new CreateAccrualRequest(
                    garageId,
                    incomeTypeId,
                    new DateOnly(2026, 7, 1),
                    100m,
                    AccrualSources.Manual,
                    "Later same-route ordinary debt"),
                null,
                CancellationToken.None);
            Assert.True(created.Succeeded, created.ErrorMessage);
            laterOrdinaryAccrualId = created.Value!.Id;
        }

        await using var verificationContext = database.CreateContext();
        Assert.Equal(
            100m,
            await verificationContext.AccrualPaymentAllocations
                .Where(item => item.IsActive && item.FinancialOperationId == legacyPaymentId && item.AccrualId == campaignPrincipalId)
                .SumAsync(item => item.Amount));
        Assert.Equal(
            60m,
            await verificationContext.AccrualPaymentAllocations
                .Where(item => item.IsActive && item.FinancialOperationId == legacyPaymentId && item.AccrualId == paidOrdinaryAccrualId)
                .SumAsync(item => item.Amount));
        Assert.False(await verificationContext.AccrualPaymentAllocations
            .AnyAsync(item => item.IsActive && item.FinancialOperationId == legacyPaymentId && item.AccrualId == laterOrdinaryAccrualId));
        Assert.Equal(
            160m,
            await verificationContext.AccrualPaymentAllocations
                .Where(item => item.IsActive && item.FinancialOperationId == legacyPaymentId)
                .SumAsync(item => item.Amount));
        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var campaignRow = Assert.Single(worksheet.Value!.Rows, row => row.FeeCampaignId == campaignId);
        Assert.Equal(100m, campaignRow.IncomeAmount);
        Assert.Equal(400m, campaignRow.Debt);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
        Assert.Equal(500m, worksheet.Value.DebtTotal);
    }

    [PostgreSqlFact]
    public async Task ActiveCampaignAutomation_UsesPostgreSqlMonthWindowAndRemainsIdempotent()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var builder = new AccountingTestDataBuilder();
        var garage = builder.BuildGarage(number: "FEE-AUTO-PG-1");
        Guid dueCampaignId;

        await using (var setupContext = database.CreateContext())
        {
            var destination = await setupContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var dueCampaign = CreateCampaign("Автоматический сбор PostgreSQL", destination, 650m);
            dueCampaign.StartsOn = new DateOnly(2026, 8, 20);
            dueCampaign.EndsOn = new DateOnly(2026, 8, 31);
            var futureCampaign = CreateCampaign("Будущий сбор PostgreSQL", destination, 700m);
            futureCampaign.StartsOn = new DateOnly(2026, 9, 1);
            setupContext.AddRange(garage, dueCampaign, futureCampaign);
            await setupContext.SaveChangesAsync();
            dueCampaignId = dueCampaign.Id;
        }

        await using (var generationContext = database.CreateContext())
        {
            var service = FinanceServiceTestFactory.Create(generationContext);
            var request = new GenerateActiveFeeCampaignAccrualsRequest(new DateOnly(2026, 8, 1), "Автоматический запуск PostgreSQL");

            var first = await service.GenerateActiveFeeCampaignAccrualsAsync(request, null, CancellationToken.None);
            var second = await service.GenerateActiveFeeCampaignAccrualsAsync(request, null, CancellationToken.None);

            Assert.True(first.Succeeded, first.ErrorMessage);
            Assert.Equal(1, first.Value!.CreatedCount);
            Assert.Equal(dueCampaignId, Assert.Single(first.Value.CampaignResults).FeeCampaignId);
            Assert.True(second.Succeeded, second.ErrorMessage);
            Assert.Equal(0, second.Value!.CreatedCount);
            Assert.Single(second.Value.SkippedCampaigns);
        }

        await using var verificationContext = database.CreateContext();
        var accrual = await verificationContext.Accruals.AsNoTracking().SingleAsync();
        Assert.Equal(dueCampaignId, accrual.FeeCampaignId);
        Assert.Equal(new DateOnly(2026, 8, 1), accrual.AccountingMonth);
        Assert.Contains("Автоматический запуск PostgreSQL", accrual.Comment, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task CampaignNextMonth_ProjectsPostgreSqlRemainderWithoutDuplicatePrincipal()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var owner = new Owner { LastName = "Проверка", FirstName = "Сбора" };
        var paidGarage = new Garage { Number = "FEE-PAID-PG", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var partialGarage = new Garage { Number = "FEE-PARTIAL-PG", PeopleCount = 1, FloorCount = 1, Owner = owner };
        Guid campaignId;
        Guid destinationId;

        await using (var setupContext = database.CreateContext())
        {
            var destination = await setupContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var campaign = CreateCampaign("Повторный сбор PostgreSQL", destination, 650m);
            campaign.AppliesToAllGarages = false;
            campaign.ParticipantGarages.Add(new FeeCampaignGarage { FeeCampaign = campaign, Garage = paidGarage });
            campaign.ParticipantGarages.Add(new FeeCampaignGarage { FeeCampaign = campaign, Garage = partialGarage });
            setupContext.AddRange(owner, paidGarage, partialGarage, campaign);
            await setupContext.SaveChangesAsync();
            campaignId = campaign.Id;
            destinationId = destination.Id;
        }

        await using (var generationContext = database.CreateContext())
        {
            var service = FinanceServiceTestFactory.Create(generationContext);
            var june = await service.GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(campaignId, new DateOnly(2026, 6, 1), null),
                null,
                CancellationToken.None);
            Assert.True(june.Succeeded, june.ErrorMessage);
            Assert.Equal(2, june.Value!.CreatedCount);

            var fullPayment = await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    paidGarage.Id,
                    destinationId,
                    new DateOnly(2026, 6, 15),
                    new DateOnly(2026, 6, 1),
                    650m,
                    "FEE-PAID-PG",
                    null),
                null,
                CancellationToken.None);
            var partialPayment = await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    partialGarage.Id,
                    destinationId,
                    new DateOnly(2026, 6, 15),
                    new DateOnly(2026, 6, 1),
                    300m,
                    "FEE-PARTIAL-PG",
                    null),
                null,
                CancellationToken.None);
            Assert.True(fullPayment.Succeeded, fullPayment.ErrorMessage);
            Assert.True(partialPayment.Succeeded, partialPayment.ErrorMessage);

            var july = await service.GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(campaignId, new DateOnly(2026, 7, 1), null),
                null,
                CancellationToken.None);
            Assert.False(july.Succeeded);
            Assert.Equal("fee_campaign_accruals_empty", july.ErrorCode);

            var range = new GarageIncomeWorksheetRequest(
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 7, 1));
            var partialWorksheet = await service.GetGarageIncomeWorksheetAsync(
                partialGarage.Id,
                range,
                CancellationToken.None);
            var paidWorksheet = await service.GetGarageIncomeWorksheetAsync(
                paidGarage.Id,
                range,
                CancellationToken.None);
            var projection = Assert.Single(
                partialWorksheet.Value!.Rows,
                row => row.FeeCampaignId == campaignId);
            Assert.Equal(new DateOnly(2026, 7, 1), projection.AccountingMonth);
            Assert.Equal(650m, projection.AccrualAmount);
            Assert.Equal(300m, projection.IncomeAmount);
            Assert.Equal(350m, projection.Debt);
            Assert.DoesNotContain(paidWorksheet.Value!.Rows, row => row.FeeCampaignId == campaignId);

            var julyOnlyWorksheet = await service.GetGarageIncomeWorksheetAsync(
                partialGarage.Id,
                new GarageIncomeWorksheetRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1)),
                CancellationToken.None);
            Assert.True(julyOnlyWorksheet.Succeeded, julyOnlyWorksheet.ErrorMessage);
            var julyOnlyProjection = Assert.Single(
                julyOnlyWorksheet.Value!.Rows,
                row => row.FeeCampaignId == campaignId);
            Assert.Equal(0m, julyOnlyProjection.AccrualAmount);
            Assert.Equal(350m, julyOnlyProjection.PayableAmount);
            Assert.Equal(0m, julyOnlyProjection.IncomeAmount);
            Assert.Equal(350m, julyOnlyProjection.Debt);
            Assert.Equal(350m, julyOnlyWorksheet.Value.OpeningDebt);
            Assert.Equal(0m, julyOnlyWorksheet.Value.AccrualTotal);
            Assert.Equal(0m, julyOnlyWorksheet.Value.IncomeTotal);
            Assert.Equal(350m, julyOnlyWorksheet.Value.DebtTotal);

            var close = await DictionaryServiceTestFactory.Create(generationContext).CloseFeeCampaignAsync(
                campaignId,
                new CloseFeeCampaignRequest("PostgreSQL early-close reconciliation"),
                null,
                CancellationToken.None);
            Assert.True(close.Succeeded, close.ErrorMessage);
            var closedWorksheet = await service.GetGarageIncomeWorksheetAsync(
                partialGarage.Id,
                range,
                CancellationToken.None);
            Assert.DoesNotContain(closedWorksheet.Value!.Rows, row => row.FeeCampaignId == campaignId);
            Assert.Equal(300m, closedWorksheet.Value.AccrualTotal);
            Assert.Equal(300m, closedWorksheet.Value.IncomeTotal);
            Assert.Equal(0m, closedWorksheet.Value.ClosingBalance);
            Assert.Equal(0m, closedWorksheet.Value.DebtTotal);
        }

        await using var verificationContext = database.CreateContext();
        Assert.Equal(
            0,
            await verificationContext.Accruals.CountAsync(item =>
                item.FeeCampaignId == campaignId &&
                item.AccountingMonth == new DateOnly(2026, 7, 1)));
        Assert.Equal(2, await verificationContext.Accruals.CountAsync(item => item.FeeCampaignId == campaignId));
        Assert.All(
            await verificationContext.Accruals
                .AsNoTracking()
                .Where(item => item.FeeCampaignId == campaignId)
                .ToArrayAsync(),
            item => Assert.Equal(new DateOnly(2026, 6, 1), item.AccountingMonth));
        Assert.Equal(
            950m,
            await verificationContext.Accruals
                .Where(item => item.FeeCampaignId == campaignId && !item.IsCanceled)
                .SumAsync(item => item.Amount));
        Assert.Equal(
            950m,
            await verificationContext.AccrualPaymentAllocations
                .Where(item => item.IsActive && item.Accrual.FeeCampaignId == campaignId)
                .SumAsync(item => item.Amount));
    }

    [PostgreSqlFact]
    public async Task CloseAndGenerate_SerializeOnCampaignLockAndLeaveNoActiveDebt()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (campaignId, garageId) = await SeedConcurrencyCampaignAsync(database, "CONCURRENT-GENERATE");
        await using var closeContext = database.CreateContext();
        var outerLock = await new EfFeeCampaignRepository(closeContext).AcquirePaymentLockAsync(
            campaignId,
            CancellationToken.None);
        Task<FinanceResult<FeeCampaignAccrualGenerationResultDto>> generationTask;
        try
        {
            generationTask = Task.Run(async () =>
            {
                await using var generationContext = database.CreateContext();
                return await FinanceServiceTestFactory.Create(generationContext).GenerateFeeCampaignAccrualsAsync(
                    new GenerateFeeCampaignAccrualsRequest(campaignId, new DateOnly(2026, 6, 1), null),
                    null,
                    CancellationToken.None);
            });
            await WaitForAdvisoryLockWaiterAsync(database.ConnectionString, TimeSpan.FromSeconds(10));
            Assert.False(generationTask.IsCompleted);

            var close = await DictionaryServiceTestFactory.Create(closeContext).CloseFeeCampaignAsync(
                campaignId,
                new CloseFeeCampaignRequest("Concurrency close before generation"),
                null,
                CancellationToken.None);
            Assert.True(close.Succeeded, close.ErrorMessage);
        }
        finally
        {
            await outerLock.DisposeAsync();
        }

        var generation = await generationTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.False(generation.Succeeded);
        Assert.Equal("fee_campaign_closed", generation.ErrorCode);

        await using var verificationContext = database.CreateContext();
        Assert.True((await verificationContext.FeeCampaigns.SingleAsync(item => item.Id == campaignId)).ClosedAtUtc.HasValue);
        Assert.Empty(await verificationContext.Accruals
            .Where(item => item.FeeCampaignId == campaignId && !item.IsCanceled)
            .ToArrayAsync());
        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.DoesNotContain(worksheet.Value!.Rows, row => row.FeeCampaignId == campaignId);
        Assert.Equal(0m, worksheet.Value.DebtTotal);
    }

    [PostgreSqlFact]
    public async Task CloseAndWorksheetEnsure_SerializeAndDoNotUseStaleOption()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (campaignId, garageId) = await SeedConcurrencyCampaignAsync(database, "CONCURRENT-WORKSHEET");
        await using var closeContext = database.CreateContext();
        var outerLock = await new EfFeeCampaignRepository(closeContext).AcquirePaymentLockAsync(
            campaignId,
            CancellationToken.None);
        Task<FinanceResult<GarageIncomeWorksheetDto>> worksheetTask;
        try
        {
            worksheetTask = Task.Run(async () =>
            {
                await using var worksheetContext = database.CreateContext();
                return await FinanceServiceTestFactory.Create(worksheetContext).GetGarageIncomeWorksheetAsync(
                    garageId,
                    new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
                    CancellationToken.None);
            });
            await WaitForAdvisoryLockWaiterAsync(database.ConnectionString, TimeSpan.FromSeconds(10));
            Assert.False(worksheetTask.IsCompleted);

            var close = await DictionaryServiceTestFactory.Create(closeContext).CloseFeeCampaignAsync(
                campaignId,
                new CloseFeeCampaignRequest("Concurrency close before worksheet ensure"),
                null,
                CancellationToken.None);
            Assert.True(close.Succeeded, close.ErrorMessage);
        }
        finally
        {
            await outerLock.DisposeAsync();
        }

        var worksheet = await worksheetTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.DoesNotContain(worksheet.Value!.Rows, row => row.FeeCampaignId == campaignId);
        Assert.Equal(0m, worksheet.Value.AccrualTotal);
        Assert.Equal(0m, worksheet.Value.DebtTotal);

        await using var verificationContext = database.CreateContext();
        Assert.Empty(await verificationContext.Accruals
            .Where(item => item.FeeCampaignId == campaignId && !item.IsCanceled)
            .ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task WorksheetRead_WaitsForGarageWorkflowLockBeforeCreatingCampaignAccrual()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (campaignId, garageId) = await SeedConcurrencyCampaignAsync(database, "CONCURRENT-WORKSHEET-READ");
        await using var blockerContext = database.CreateContext();
        var blocker = await new EfAccrualPaymentAllocationRepository(blockerContext)
            .AcquireGarageIncomeWorksheetLockAsync(garageId, CancellationToken.None);
        Task<FinanceResult<GarageIncomeWorksheetDto>> worksheetTask;
        try
        {
            worksheetTask = Task.Run(async () =>
            {
                await using var worksheetContext = database.CreateContext();
                return await FinanceServiceTestFactory.Create(worksheetContext).GetGarageIncomeWorksheetAsync(
                    garageId,
                    new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
                    CancellationToken.None);
            });
            await WaitForAdvisoryLockWaiterAsync(database.ConnectionString, TimeSpan.FromSeconds(10));
            Assert.False(worksheetTask.IsCompleted);
        }
        finally
        {
            await blocker.DisposeAsync();
        }

        var worksheet = await worksheetTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        await using var verificationContext = database.CreateContext();
        Assert.Single(await verificationContext.Accruals
            .Where(item => item.FeeCampaignId == campaignId && item.GarageId == garageId && !item.IsCanceled)
            .ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task OrdinaryPaymentQueuedBeforeClose_IsIncludedInFreshCampaignSettlement()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (campaignId, garageId) = await SeedConcurrencyCampaignAsync(database, "CONCURRENT-ORDINARY-CLOSE");
        Guid incomeTypeId;
        await using (var generationContext = database.CreateContext())
        {
            incomeTypeId = await generationContext.IncomeTypes
                .Where(item => item.Code == "other_income")
                .Select(item => item.Id)
                .SingleAsync();
            var generation = await FinanceServiceTestFactory.Create(generationContext).GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(campaignId, new DateOnly(2026, 6, 1), null),
                null,
                CancellationToken.None);
            Assert.True(generation.Succeeded, generation.ErrorMessage);
        }

        var allocationKey = new AccrualPaymentAllocationKey(garageId, incomeTypeId);
        await using var blockerContext = database.CreateContext();
        var outerAllocationLock = await new EfAccrualPaymentAllocationRepository(blockerContext)
            .AcquireRebuildLockAsync([allocationKey], CancellationToken.None);
        Task<FinanceResult<FinancialOperationDto>> paymentTask;
        Task<DictionaryResult<FeeCampaignDto>> closeTask;
        try
        {
            paymentTask = Task.Run(async () =>
            {
                await using var context = database.CreateContext();
                return await FinanceServiceTestFactory.Create(context).CreateIncomeAsync(
                    new CreateIncomeOperationRequest(
                        garageId,
                        incomeTypeId,
                        new DateOnly(2026, 6, 15),
                        new DateOnly(2026, 6, 1),
                        100m,
                        "ORDINARY-BEFORE-CLOSE",
                        null),
                    null,
                    CancellationToken.None);
            });
            await WaitForAdvisoryLockWaiterAsync(database.ConnectionString, TimeSpan.FromSeconds(10));
            Assert.False(paymentTask.IsCompleted);

            closeTask = Task.Run(async () =>
            {
                await using var context = database.CreateContext();
                return await DictionaryServiceTestFactory.Create(context).CloseFeeCampaignAsync(
                    campaignId,
                    new CloseFeeCampaignRequest("Close must include the queued ordinary payment"),
                    null,
                    CancellationToken.None);
            });
            await WaitForAdvisoryLockWaiterAsync(
                database.ConnectionString,
                TimeSpan.FromSeconds(10),
                expectedWaiterCount: 2);
            Assert.False(closeTask.IsCompleted);
        }
        finally
        {
            await outerAllocationLock.DisposeAsync();
        }

        var payment = await paymentTask.WaitAsync(TimeSpan.FromSeconds(20));
        var close = await closeTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(payment.Succeeded, payment.ErrorMessage);
        Assert.True(close.Succeeded, close.ErrorMessage);
        Assert.Equal(100m, close.Value!.CollectedAmount);

        await using var verificationContext = database.CreateContext();
        var principal = Assert.Single(await verificationContext.Accruals
            .AsNoTracking()
            .Where(item => item.FeeCampaignId == campaignId && !item.IsCanceled)
            .ToArrayAsync());
        Assert.Equal(100m, principal.Amount);
        var allocation = Assert.Single(await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item => item.IsActive && item.FinancialOperationId == payment.Value!.Id)
            .ToArrayAsync());
        Assert.Equal(principal.Id, allocation.AccrualId);
        Assert.Equal(100m, allocation.Amount);
    }

    [PostgreSqlFact]
    public async Task CloseAndFullPayment_SerializeAndRejectStaleCampaignLine()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (campaignId, garageId) = await SeedConcurrencyCampaignAsync(database, "CONCURRENT-FULL-PAYMENT");
        Guid incomeTypeId;
        await using (var lookupContext = database.CreateContext())
        {
            incomeTypeId = await lookupContext.IncomeTypes
                .Where(item => item.Code == "other_income")
                .Select(item => item.Id)
                .SingleAsync();
        }

        await using var closeContext = database.CreateContext();
        var outerLock = await new EfFeeCampaignRepository(closeContext).AcquirePaymentLockAsync(
            campaignId,
            CancellationToken.None);
        var receiptBatchId = Guid.NewGuid();
        Task<FinanceResult<FullGaragePaymentDto>> paymentTask;
        try
        {
            paymentTask = Task.Run(async () =>
            {
                await using var paymentContext = database.CreateContext();
                return await FinanceServiceTestFactory.Create(paymentContext).CreateFullGaragePaymentAsync(
                    new CreateFullGaragePaymentRequest(
                        garageId,
                        new DateOnly(2026, 6, 15),
                        [new CreateFullGaragePaymentLineRequest(
                            incomeTypeId,
                            new DateOnly(2026, 6, 1),
                            500m,
                            null,
                            FeeCampaignId: campaignId)],
                        receiptBatchId),
                    null,
                    CancellationToken.None);
            });
            await WaitForAdvisoryLockWaiterAsync(database.ConnectionString, TimeSpan.FromSeconds(10));
            Assert.False(paymentTask.IsCompleted);

            var close = await DictionaryServiceTestFactory.Create(closeContext).CloseFeeCampaignAsync(
                campaignId,
                new CloseFeeCampaignRequest("Close wins before stale full-payment quote"),
                null,
                CancellationToken.None);
            Assert.True(close.Succeeded, close.ErrorMessage);
        }
        finally
        {
            await outerLock.DisposeAsync();
        }

        var payment = await paymentTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.False(payment.Succeeded);
        Assert.Equal("fee_campaign_closed", payment.ErrorCode);
        await using var verificationContext = database.CreateContext();
        Assert.Empty(await verificationContext.FinancialOperations
            .Where(item => item.ReceiptBatchId == receiptBatchId || item.FeeCampaignId == campaignId)
            .ToArrayAsync());
        Assert.Empty(await verificationContext.Accruals
            .Where(item => item.FeeCampaignId == campaignId && !item.IsCanceled)
            .ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task ConcurrentIdenticalOrdinaryFullPayments_SerializeByReceiptBatchAndPersistOneBatch()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var builder = new AccountingTestDataBuilder();
        var garage = builder.BuildGarage(number: "FULL-PAYMENT-IDEMPOTENT-PG");
        Guid incomeTypeId;
        await using (var setupContext = database.CreateContext())
        {
            incomeTypeId = await setupContext.IncomeTypes
                .Where(item => item.Code == "other_income")
                .Select(item => item.Id)
                .SingleAsync();
            setupContext.Add(garage);
            await setupContext.SaveChangesAsync();
        }

        var receiptBatchId = Guid.NewGuid();
        var request = new CreateFullGaragePaymentRequest(
            garage.Id,
            new DateOnly(2026, 9, 15),
            [new CreateFullGaragePaymentLineRequest(
                incomeTypeId,
                new DateOnly(2026, 9, 1),
                100m,
                "Concurrent idempotent payment")],
            receiptBatchId);
        await using var blockerContext = database.CreateContext();
        var outerLock = await new EfFinancialOperationRepository(blockerContext)
            .AcquireReceiptBatchLockAsync(receiptBatchId, CancellationToken.None);
        Task<FinanceResult<FullGaragePaymentDto>> firstTask;
        Task<FinanceResult<FullGaragePaymentDto>> secondTask;
        try
        {
            firstTask = Task.Run(async () =>
            {
                await using var context = database.CreateContext();
                return await FinanceServiceTestFactory.Create(context).CreateFullGaragePaymentAsync(
                    request,
                    null,
                    CancellationToken.None);
            });
            secondTask = Task.Run(async () =>
            {
                await using var context = database.CreateContext();
                return await FinanceServiceTestFactory.Create(context).CreateFullGaragePaymentAsync(
                    request,
                    null,
                    CancellationToken.None);
            });
            await WaitForAdvisoryLockWaiterAsync(
                database.ConnectionString,
                TimeSpan.FromSeconds(10),
                expectedWaiterCount: 2);
            Assert.False(firstTask.IsCompleted);
            Assert.False(secondTask.IsCompleted);
        }
        finally
        {
            await outerLock.DisposeAsync();
        }

        var first = await firstTask.WaitAsync(TimeSpan.FromSeconds(20));
        var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.Equal(receiptBatchId, first.Value!.ReceiptBatchId);
        Assert.Equal(receiptBatchId, second.Value!.ReceiptBatchId);
        Assert.Equal(Assert.Single(first.Value.Operations).Id, Assert.Single(second.Value.Operations).Id);

        await using var verificationContext = database.CreateContext();
        var persisted = Assert.Single(await verificationContext.FinancialOperations
            .AsNoTracking()
            .Where(item => item.ReceiptBatchId == receiptBatchId)
            .ToArrayAsync());
        Assert.Equal(garage.Id, persisted.GarageId);
        Assert.Equal(100m, persisted.Amount);
        Assert.Single(await verificationContext.FundOperations
            .AsNoTracking()
            .Where(item => item.SourceFinancialOperationId == persisted.Id && !item.IsCanceled)
            .ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task DirectAndFullPayment_SerializeByReceiptBatchAndRejectCrossEntrypointConflict()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var firstOwner = new Owner { LastName = "Receipt", FirstName = "Direct" };
        var secondOwner = new Owner { LastName = "Receipt", FirstName = "Full" };
        var directGarage = new Garage
        {
            Number = "RECEIPT-DIRECT-PG",
            PeopleCount = 1,
            FloorCount = 1,
            Owner = firstOwner
        };
        var fullGarage = new Garage
        {
            Number = "RECEIPT-FULL-PG",
            PeopleCount = 1,
            FloorCount = 1,
            Owner = secondOwner
        };
        Guid incomeTypeId;
        await using (var setupContext = database.CreateContext())
        {
            incomeTypeId = await setupContext.IncomeTypes
                .Where(item => item.Code == "other_income")
                .Select(item => item.Id)
                .SingleAsync();
            setupContext.AddRange(firstOwner, secondOwner, directGarage, fullGarage);
            await setupContext.SaveChangesAsync();
        }

        var receiptBatchId = Guid.NewGuid();
        await using var blockerContext = database.CreateContext();
        var outerLock = await new EfFinancialOperationRepository(blockerContext)
            .AcquireReceiptBatchLockAsync(receiptBatchId, CancellationToken.None);
        Task<FinanceResult<FinancialOperationDto>> directTask;
        Task<FinanceResult<FullGaragePaymentDto>> fullTask;
        try
        {
            directTask = Task.Run(async () =>
            {
                await using var context = database.CreateContext();
                return await FinanceServiceTestFactory.Create(context).CreateIncomeAsync(
                    new CreateIncomeOperationRequest(
                        directGarage.Id,
                        incomeTypeId,
                        new DateOnly(2026, 9, 15),
                        new DateOnly(2026, 9, 1),
                        50m,
                        "RECEIPT-DIRECT-WINS",
                        null,
                        ReceiptBatchId: receiptBatchId),
                    null,
                    CancellationToken.None);
            });
            await WaitForAdvisoryLockWaiterAsync(database.ConnectionString, TimeSpan.FromSeconds(10));
            Assert.False(directTask.IsCompleted);

            fullTask = Task.Run(async () =>
            {
                await using var context = database.CreateContext();
                return await FinanceServiceTestFactory.Create(context).CreateFullGaragePaymentAsync(
                    new CreateFullGaragePaymentRequest(
                        fullGarage.Id,
                        new DateOnly(2026, 9, 16),
                        [new CreateFullGaragePaymentLineRequest(
                            incomeTypeId,
                            new DateOnly(2026, 9, 1),
                            100m,
                            "Conflicting cross-entrypoint payment")],
                        receiptBatchId),
                    null,
                    CancellationToken.None);
            });
            await WaitForAdvisoryLockWaiterAsync(
                database.ConnectionString,
                TimeSpan.FromSeconds(10),
                expectedWaiterCount: 2);
            Assert.False(fullTask.IsCompleted);
        }
        finally
        {
            await outerLock.DisposeAsync();
        }

        var direct = await directTask.WaitAsync(TimeSpan.FromSeconds(20));
        var full = await fullTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(direct.Succeeded, direct.ErrorMessage);
        Assert.False(full.Succeeded);
        Assert.Equal("receipt_batch_conflict", full.ErrorCode);

        await using var verificationContext = database.CreateContext();
        var persisted = Assert.Single(await verificationContext.FinancialOperations
            .AsNoTracking()
            .Where(item => item.ReceiptBatchId == receiptBatchId)
            .ToArrayAsync());
        Assert.Equal(directGarage.Id, persisted.GarageId);
        Assert.Equal(new DateOnly(2026, 9, 15), persisted.OperationDate);
        Assert.Equal(50m, persisted.Amount);
    }

    [PostgreSqlFact]
    public async Task CloseAndCancelCampaignAccrual_UseCampaignBeforeRowLockAndPreserveClosedInvariant()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (campaignId, garageId) = await SeedConcurrencyCampaignAsync(database, "CONCURRENT-CANCEL-ACCRUAL");
        Guid accrualId;
        await using (var generationContext = database.CreateContext())
        {
            var generation = await FinanceServiceTestFactory.Create(generationContext).GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(campaignId, new DateOnly(2026, 6, 1), null),
                null,
                CancellationToken.None);
            Assert.True(generation.Succeeded, generation.ErrorMessage);
            accrualId = Assert.Single(generation.Value!.CreatedAccruals).Id;
        }

        await using var closeContext = database.CreateContext();
        var outerLock = await new EfFeeCampaignRepository(closeContext).AcquirePaymentLockAsync(
            campaignId,
            CancellationToken.None);
        Task<FinanceResult<AccrualDto>> cancelTask;
        try
        {
            cancelTask = Task.Run(async () =>
            {
                await using var cancelContext = database.CreateContext();
                return await FinanceServiceTestFactory.Create(cancelContext).CancelAccrualAsync(
                    accrualId,
                    new CancelFinanceEntryRequest("Concurrent manual cancel must lose"),
                    null,
                    CancellationToken.None);
            });
            await WaitForAdvisoryLockWaiterAsync(database.ConnectionString, TimeSpan.FromSeconds(10));
            Assert.False(cancelTask.IsCompleted);

            var close = await DictionaryServiceTestFactory.Create(closeContext).CloseFeeCampaignAsync(
                campaignId,
                new CloseFeeCampaignRequest("Close settles before blocked accrual mutation"),
                null,
                CancellationToken.None);
            Assert.True(close.Succeeded, close.ErrorMessage);
        }
        finally
        {
            await outerLock.DisposeAsync();
        }

        var cancel = await cancelTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.False(cancel.Succeeded);
        Assert.Equal("fee_campaign_accrual_mutation_forbidden", cancel.ErrorCode);
        await using var verificationContext = database.CreateContext();
        Assert.True((await verificationContext.FeeCampaigns.SingleAsync(item => item.Id == campaignId)).ClosedAtUtc.HasValue);
        Assert.Empty(await verificationContext.Accruals
            .Where(item => item.FeeCampaignId == campaignId && !item.IsCanceled)
            .ToArrayAsync());
        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.Equal(0m, worksheet.Value!.AccrualTotal);
        Assert.Equal(0m, worksheet.Value.DebtTotal);
    }

    [PostgreSqlFact]
    public async Task GenerateAndCancelLegacyPayment_RechecksCampaignLinkAfterAllocationLock()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid campaignId;
        Guid garageId;
        Guid incomeTypeId;
        Guid legacyPaymentId;
        await using (var setupContext = database.CreateContext())
        {
            var incomeType = await setupContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var owner = new Owner { LastName = "Race", FirstName = "Legacy earmark" };
            var garage = new Garage
            {
                Number = "FEE-RACE-LEGACY-EARMARK",
                PeopleCount = 1,
                FloorCount = 1,
                Owner = owner
            };
            var campaign = CreateCampaign("Concurrent legacy earmark", incomeType, 500m);
            var legacyPayment = CreateUntaggedIncome(
                garage,
                incomeType,
                new DateOnly(2026, 6, 15),
                100m);
            setupContext.AddRange(owner, garage, campaign, legacyPayment);
            await setupContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            incomeTypeId = incomeType.Id;
            legacyPaymentId = legacyPayment.Id;
        }

        var allocationKey = new AccrualPaymentAllocationKey(garageId, incomeTypeId);
        await using var blockerContext = database.CreateContext();
        var outerAllocationLock = await new EfAccrualPaymentAllocationRepository(blockerContext)
            .AcquireRebuildLockAsync([allocationKey], CancellationToken.None);
        Task<FinanceResult<FeeCampaignAccrualGenerationResultDto>> generationTask;
        Task<FinanceResult<FinancialOperationDto>> cancellationTask;
        try
        {
            generationTask = Task.Run(async () =>
            {
                await using var context = database.CreateContext();
                return await FinanceServiceTestFactory.Create(context).GenerateFeeCampaignAccrualsAsync(
                    new GenerateFeeCampaignAccrualsRequest(
                        campaignId,
                        new DateOnly(2026, 6, 1),
                        null),
                    null,
                    CancellationToken.None);
            });
            await WaitForAdvisoryLockWaiterAsync(database.ConnectionString, TimeSpan.FromSeconds(10));
            Assert.False(generationTask.IsCompleted);

            cancellationTask = Task.Run(async () =>
            {
                await using var context = database.CreateContext();
                return await FinanceServiceTestFactory.Create(context).CancelOperationAsync(
                    legacyPaymentId,
                    new CancelFinanceEntryRequest("Concurrent cancel must observe new campaign earmark"),
                    null,
                    CancellationToken.None);
            });
            await WaitForAdvisoryLockWaiterAsync(
                database.ConnectionString,
                TimeSpan.FromSeconds(10),
                expectedWaiterCount: 2);
            Assert.False(cancellationTask.IsCompleted);
        }
        finally
        {
            await outerAllocationLock.DisposeAsync();
        }

        var generation = await generationTask.WaitAsync(TimeSpan.FromSeconds(20));
        var cancellation = await cancellationTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(generation.Succeeded, generation.ErrorMessage);
        Assert.False(cancellation.Succeeded);
        Assert.Equal("fee_campaign_payment_mutation_forbidden", cancellation.ErrorCode);

        await using var verificationContext = database.CreateContext();
        Assert.False((await verificationContext.FinancialOperations
            .AsNoTracking()
            .SingleAsync(item => item.Id == legacyPaymentId)).IsCanceled);
        var allocation = Assert.Single(await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Include(item => item.Accrual)
            .Where(item => item.IsActive && item.FinancialOperationId == legacyPaymentId)
            .ToArrayAsync());
        Assert.Equal(campaignId, allocation.Accrual.FeeCampaignId);
        Assert.Equal(100m, allocation.Amount);
    }

    [PostgreSqlFact]
    public async Task RestoreCanceledCampaignPrincipal_WithActivePrincipalReturnsDuplicateInsteadOfUniqueViolation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid canceledPrincipalId;
        Guid activePrincipalId;
        Guid campaignId;
        await using (var setupContext = database.CreateContext())
        {
            var incomeType = await setupContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var owner = new Owner { LastName = "Restore", FirstName = "Duplicate" };
            var garage = new Garage { Number = "FEE-RESTORE-DUP", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Restore duplicate campaign", incomeType, 500m);
            var canceledPrincipal = CreateCampaignAccrual(
                garage,
                incomeType,
                campaign,
                new DateOnly(2026, 6, 1),
                500m);
            canceledPrincipal.IsCanceled = true;
            var activePrincipal = CreateCampaignAccrual(
                garage,
                incomeType,
                campaign,
                new DateOnly(2026, 7, 1),
                500m);
            setupContext.AddRange(owner, garage, campaign, canceledPrincipal, activePrincipal);
            await setupContext.SaveChangesAsync();
            canceledPrincipalId = canceledPrincipal.Id;
            activePrincipalId = activePrincipal.Id;
            campaignId = campaign.Id;
        }

        await using var restoreContext = database.CreateContext();
        var result = await FinanceServiceTestFactory.Create(restoreContext).RestoreAccrualAsync(
            canceledPrincipalId,
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("accrual_duplicate", result.ErrorCode);
        restoreContext.ChangeTracker.Clear();
        Assert.True((await restoreContext.Accruals.SingleAsync(item => item.Id == canceledPrincipalId)).IsCanceled);
        Assert.Equal(activePrincipalId, Assert.Single(await restoreContext.Accruals
            .Where(item => item.FeeCampaignId == campaignId && !item.IsCanceled)
            .ToArrayAsync()).Id);
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_CancelsLegacyDuplicatesAndExpandsPrincipalForTaggedPayments()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid garageId;
        Guid incomeTypeId;
        Guid principalId;
        Guid duplicateAllocationId;

        await using (var legacyContext = database.CreateContext())
        {
            var incomeType = await legacyContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var owner = new Owner { LastName = "Legacy", FirstName = "Campaign" };
            var garage = new Garage { Number = "FEE-LEGACY-PG", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Legacy monthly campaign", incomeType, 500m);
            campaign.TargetAmount = 5000m;
            var principal = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 6, 1), 500m);
            var julyDuplicate = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 7, 1), 500m);
            var augustDuplicate = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 8, 1), 500m);
            var principalPayment = CreateCampaignIncome(garage, incomeType, campaign, new DateOnly(2026, 6, 15), 500m);
            var excessPayment = CreateCampaignIncome(garage, incomeType, campaign, new DateOnly(2026, 7, 15), 100m);
            var principalAllocation = new AccrualPaymentAllocation
            {
                FinancialOperation = principalPayment,
                Accrual = principal,
                Amount = 500m
            };
            var duplicateAllocation = new AccrualPaymentAllocation
            {
                FinancialOperation = excessPayment,
                Accrual = julyDuplicate,
                Amount = 100m
            };
            legacyContext.AddRange(
                owner,
                garage,
                campaign,
                principal,
                julyDuplicate,
                augustDuplicate,
                principalPayment,
                excessPayment,
                principalAllocation,
                duplicateAllocation);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            incomeTypeId = incomeType.Id;
            principalId = principal.Id;
            duplicateAllocationId = duplicateAllocation.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using (var verificationContext = database.CreateContext())
        {
            var campaignAccruals = await verificationContext.Accruals
                .AsNoTracking()
                .Where(item => item.FeeCampaignId == campaignId)
                .OrderBy(item => item.AccountingMonth)
                .ToArrayAsync();
            Assert.Equal(3, campaignAccruals.Length);
            Assert.Equal(principalId, Assert.Single(campaignAccruals, item => !item.IsCanceled).Id);
            Assert.Equal(600m, Assert.Single(campaignAccruals, item => !item.IsCanceled).Amount);
            Assert.Equal(2, campaignAccruals.Count(item => item.IsCanceled));
            Assert.True((await verificationContext.AccrualPaymentAllocations
                .AsNoTracking()
                .SingleAsync(item => item.Id == duplicateAllocationId)).IsActive is false);
            Assert.Equal(
                600m,
                await verificationContext.AccrualPaymentAllocations
                    .Where(item => item.IsActive && item.AccrualId == principalId)
                    .SumAsync(item => item.Amount));

            var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
                garageId,
                new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 1)),
                CancellationToken.None);
            Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
            Assert.DoesNotContain(worksheet.Value!.Rows, row => row.FeeCampaignId == campaignId);
            Assert.Equal(600m, worksheet.Value.AccrualTotal);
            Assert.Equal(600m, worksheet.Value.IncomeTotal);
            Assert.Equal(0m, worksheet.Value.AdvanceTotal);
            Assert.Equal(0m, worksheet.Value.ClosingBalance);
            Assert.Equal(0m, worksheet.Value.DebtTotal);
        }

        await using var uniquenessContext = database.CreateContext();
        var campaignReference = await uniquenessContext.FeeCampaigns.SingleAsync(item => item.Id == campaignId);
        var garageReference = await uniquenessContext.Garages.SingleAsync(item => item.Id == garageId);
        var incomeTypeReference = await uniquenessContext.IncomeTypes.SingleAsync(item => item.Id == incomeTypeId);
        uniquenessContext.Accruals.Add(CreateCampaignAccrual(
            garageReference,
            incomeTypeReference,
            campaignReference,
            new DateOnly(2026, 9, 1),
            500m));
        await Assert.ThrowsAsync<DbUpdateException>(() => uniquenessContext.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_OpenCampaignMovesPartialDuplicateAllocationToPrincipal()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid garageId;
        Guid principalId;
        Guid duplicateAllocationId;

        await using (var legacyContext = database.CreateContext())
        {
            var incomeType = await legacyContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var owner = new Owner { LastName = "Legacy", FirstName = "Partial" };
            var garage = new Garage { Number = "FEE-OPEN-PARTIAL", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Open partial legacy campaign", incomeType, 500m);
            campaign.TargetAmount = 5000m;
            var principal = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 6, 1), 500m);
            var duplicate = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 7, 1), 500m);
            var payment = CreateUntaggedIncome(garage, incomeType, new DateOnly(2026, 7, 15), 100m);
            var duplicateAllocation = new AccrualPaymentAllocation
            {
                FinancialOperation = payment,
                Accrual = duplicate,
                Amount = 100m
            };
            legacyContext.AddRange(owner, garage, campaign, principal, duplicate, payment, duplicateAllocation);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            principalId = principal.Id;
            duplicateAllocationId = duplicateAllocation.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var accruals = await verificationContext.Accruals
            .AsNoTracking()
            .Where(item => item.FeeCampaignId == campaignId)
            .OrderBy(item => item.AccountingMonth)
            .ToArrayAsync();
        Assert.Equal(principalId, Assert.Single(accruals, item => !item.IsCanceled).Id);
        Assert.True(Assert.Single(accruals, item => item.Id != principalId).IsCanceled);
        Assert.False((await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .SingleAsync(item => item.Id == duplicateAllocationId)).IsActive);
        var repaired = Assert.Single(await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item => item.IsActive)
            .ToArrayAsync());
        Assert.Equal(principalId, repaired.AccrualId);
        Assert.Equal(100m, repaired.Amount);

        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var projection = Assert.Single(worksheet.Value!.Rows, row => row.FeeCampaignId == campaignId);
        Assert.Equal(500m, projection.AccrualAmount);
        Assert.Equal(100m, projection.IncomeAmount);
        Assert.Equal(400m, projection.Debt);
        Assert.Equal(500m, worksheet.Value.AccrualTotal);
        Assert.Equal(100m, worksheet.Value.IncomeTotal);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
        Assert.Equal(400m, worksheet.Value.DebtTotal);
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_OpenSinglePrincipalExpandsTaggedExcessAndClearsDueDateReviewForRebuild()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid garageId;
        Guid incomeTypeId;
        Guid principalId;
        Guid legacyPaymentId;
        await using (var legacyContext = database.CreateContext())
        {
            var incomeType = await legacyContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var owner = new Owner { LastName = "Legacy", FirstName = "Reviewed principal" };
            var garage = new Garage { Number = "FEE-OPEN-REVIEW", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Open reviewed legacy campaign", incomeType, 10m);
            campaign.TargetAmount = 5000m;
            var principal = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 6, 1), 500m);
            principal.DueDateNeedsReview = true;
            principal.DueDateReviewReason = "legacy_unknown_due_date";
            var legacyPayment = CreateCampaignIncome(garage, incomeType, campaign, new DateOnly(2026, 6, 15), 600m);
            legacyContext.AddRange(owner, garage, campaign, principal, legacyPayment);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            incomeTypeId = incomeType.Id;
            principalId = principal.Id;
            legacyPaymentId = legacyPayment.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using (var postMigrationContext = database.CreateContext())
        {
            var repairedPrincipal = await postMigrationContext.Accruals
                .AsNoTracking()
                .SingleAsync(item => item.Id == principalId);
            Assert.Equal(600m, repairedPrincipal.Amount);
            Assert.False(repairedPrincipal.DueDateNeedsReview);
            Assert.Null(repairedPrincipal.DueDateReviewReason);
            var repairedAllocation = Assert.Single(await postMigrationContext.AccrualPaymentAllocations
                .AsNoTracking()
                .Where(item => item.IsActive && item.FinancialOperationId == legacyPaymentId)
                .ToArrayAsync());
            Assert.Equal(principalId, repairedAllocation.AccrualId);
            Assert.Equal(600m, repairedAllocation.Amount);

            var payment = await FinanceServiceTestFactory.Create(postMigrationContext).CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    garageId,
                    incomeTypeId,
                    new DateOnly(2026, 7, 15),
                    new DateOnly(2026, 7, 1),
                    100m,
                    "FEE-REBUILD-AFTER-MIGRATION",
                    null,
                    FeeCampaignId: campaignId),
                null,
                CancellationToken.None);
            Assert.True(payment.Succeeded, payment.ErrorMessage);
        }

        await using var verificationContext = database.CreateContext();
        Assert.Equal(700m, (await verificationContext.Accruals
            .AsNoTracking()
            .SingleAsync(item => item.Id == principalId)).Amount);
        Assert.Equal(
            700m,
            await verificationContext.AccrualPaymentAllocations
                .Where(item => item.IsActive && item.AccrualId == principalId)
                .SumAsync(item => item.Amount));
        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal(700m, worksheet.Value!.AccrualTotal);
        Assert.Equal(700m, worksheet.Value.IncomeTotal);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
        Assert.Equal(0m, worksheet.Value.DebtTotal);
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_ClosedCampaignPreservesUntaggedDuplicatePaymentOnPrincipal()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid garageId;
        Guid principalId;
        Guid principalAllocationId;
        Guid duplicateAllocationId;

        await using (var legacyContext = database.CreateContext())
        {
            var incomeType = await legacyContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var owner = new Owner { LastName = "Legacy", FirstName = "Closed" };
            var garage = new Garage { Number = "FEE-CLOSED-LEGACY", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Closed legacy campaign", incomeType, 500m);
            campaign.ClosedAtUtc = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
            campaign.IsClosedEarly = true;
            campaign.ClosureComment = "Legacy close before principal repair";
            var principal = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 6, 1), 500m);
            var duplicate = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 7, 1), 500m);
            var principalPayment = CreateUntaggedIncome(garage, incomeType, new DateOnly(2026, 6, 15), 500m);
            var duplicatePayment = CreateUntaggedIncome(garage, incomeType, new DateOnly(2026, 7, 15), 100m);
            var principalAllocation = new AccrualPaymentAllocation
            {
                FinancialOperation = principalPayment,
                Accrual = principal,
                Amount = 500m
            };
            var duplicateAllocation = new AccrualPaymentAllocation
            {
                FinancialOperation = duplicatePayment,
                Accrual = duplicate,
                Amount = 100m
            };
            legacyContext.AddRange(
                owner,
                garage,
                campaign,
                principal,
                duplicate,
                principalPayment,
                duplicatePayment,
                principalAllocation,
                duplicateAllocation);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            principalId = principal.Id;
            principalAllocationId = principalAllocation.Id;
            duplicateAllocationId = duplicateAllocation.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var accruals = await verificationContext.Accruals
            .AsNoTracking()
            .Where(item => item.FeeCampaignId == campaignId)
            .OrderBy(item => item.AccountingMonth)
            .ToArrayAsync();
        var settledPrincipal = Assert.Single(accruals, item => !item.IsCanceled);
        Assert.Equal(principalId, settledPrincipal.Id);
        Assert.Equal(600m, settledPrincipal.Amount);
        Assert.True(Assert.Single(accruals, item => item.Id != principalId).IsCanceled);
        Assert.False((await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .SingleAsync(item => item.Id == principalAllocationId)).IsActive);
        Assert.False((await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .SingleAsync(item => item.Id == duplicateAllocationId)).IsActive);
        var repairedAllocations = await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item => item.IsActive && item.AccrualId == principalId)
            .ToArrayAsync();
        Assert.Equal(2, repairedAllocations.Length);
        Assert.Equal(600m, repairedAllocations.Sum(item => item.Amount));

        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.DoesNotContain(worksheet.Value!.Rows, row => row.FeeCampaignId == campaignId);
        Assert.Equal(600m, worksheet.Value.AccrualTotal);
        Assert.Equal(600m, worksheet.Value.IncomeTotal);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
        Assert.Equal(0m, worksheet.Value.ClosingBalance);
        Assert.Equal(0m, worksheet.Value.DebtTotal);
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_ClosedCampaignReactivatesCanceledPrincipalForTaggedPayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid garageId;
        Guid principalId;
        Guid paymentId;
        await using (var legacyContext = database.CreateContext())
        {
            var incomeType = await legacyContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var owner = new Owner { LastName = "Legacy", FirstName = "Canceled principal" };
            var garage = new Garage { Number = "FEE-CLOSED-CANCELED", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Closed campaign with canceled principal", incomeType, 500m);
            campaign.ClosedAtUtc = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
            campaign.IsClosedEarly = true;
            campaign.ClosureComment = "Legacy principal was canceled";
            var principal = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 6, 1), 500m);
            principal.IsCanceled = true;
            var payment = CreateCampaignIncome(garage, incomeType, campaign, new DateOnly(2026, 6, 15), 600m);
            legacyContext.AddRange(owner, garage, campaign, principal, payment);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            principalId = principal.Id;
            paymentId = payment.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var settledPrincipal = await verificationContext.Accruals
            .AsNoTracking()
            .SingleAsync(item => item.Id == principalId);
        Assert.False(settledPrincipal.IsCanceled);
        Assert.Equal(600m, settledPrincipal.Amount);
        var allocation = Assert.Single(await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item => item.IsActive && item.FinancialOperationId == paymentId)
            .ToArrayAsync());
        Assert.Equal(principalId, allocation.AccrualId);
        Assert.Equal(600m, allocation.Amount);
        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.DoesNotContain(worksheet.Value!.Rows, row => row.FeeCampaignId == campaignId);
        Assert.Equal(600m, worksheet.Value.AccrualTotal);
        Assert.Equal(600m, worksheet.Value.IncomeTotal);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
        Assert.Equal(0m, worksheet.Value.DebtTotal);
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_ClosedCampaignCapsDuplicateLegacyAllocationsAtPaymentAmount()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid garageId;
        Guid paymentId;
        await using (var legacyContext = database.CreateContext())
        {
            var incomeType = await legacyContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var owner = new Owner { LastName = "Legacy", FirstName = "Duplicate over-allocation" };
            var garage = new Garage { Number = "FEE-CLOSED-OVERALLOCATED", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Closed campaign with duplicate allocation", incomeType, 500m);
            campaign.ClosedAtUtc = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
            campaign.IsClosedEarly = true;
            campaign.ClosureComment = "Legacy duplicate allocation";
            var principal = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 6, 1), 500m);
            var duplicate = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 7, 1), 500m);
            var payment = CreateUntaggedIncome(garage, incomeType, new DateOnly(2026, 7, 15), 100m);
            var principalAllocation = new AccrualPaymentAllocation
            {
                FinancialOperation = payment,
                Accrual = principal,
                Amount = 100m
            };
            var duplicateAllocation = new AccrualPaymentAllocation
            {
                FinancialOperation = payment,
                Accrual = duplicate,
                Amount = 100m
            };
            legacyContext.AddRange(
                owner,
                garage,
                campaign,
                principal,
                duplicate,
                payment,
                principalAllocation,
                duplicateAllocation);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            paymentId = payment.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var activePrincipal = Assert.Single(await verificationContext.Accruals
            .AsNoTracking()
            .Where(item => item.GarageId == garageId && item.FeeCampaignId == campaignId && !item.IsCanceled)
            .ToArrayAsync());
        Assert.Equal(100m, activePrincipal.Amount);
        var activeAllocation = Assert.Single(await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item => item.IsActive && item.FinancialOperationId == paymentId)
            .ToArrayAsync());
        Assert.Equal(activePrincipal.Id, activeAllocation.AccrualId);
        Assert.Equal(100m, activeAllocation.Amount);
        Assert.True(activeAllocation.Amount <= 100m);
        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal(100m, worksheet.Value!.AccrualTotal);
        Assert.Equal(100m, worksheet.Value.IncomeTotal);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
        Assert.Equal(0m, worksheet.Value.DebtTotal);
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_ClosedLegacyCampaignWithoutPrincipalCreatesStableSettledPrincipal()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid stableIncomeTypeId;
        Guid paymentId;
        await using (var legacyContext = database.CreateContext())
        {
            var stableIncomeType = await legacyContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var legacyFund = new Fund { Name = "Legacy closed fund", NormalizedName = "legacy closed fund" };
            var legacyIncomeType = new IncomeType
            {
                Name = "Legacy closed income",
                Code = "legacy_closed_income",
                DestinationFund = legacyFund
            };
            var owner = new Owner { LastName = "Legacy", FirstName = "No principal" };
            var garage = new Garage { Number = "FEE-CLOSED-NO-PRINCIPAL", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Closed legacy without principal", legacyIncomeType, 500m);
            campaign.ClosedAtUtc = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
            campaign.IsClosedEarly = true;
            campaign.ClosureComment = "Legacy missing principal";
            var payment = CreateCampaignIncome(garage, legacyIncomeType, campaign, new DateOnly(2026, 6, 15), 600m);
            legacyContext.AddRange(owner, garage, legacyFund, legacyIncomeType, campaign, payment);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            stableIncomeTypeId = stableIncomeType.Id;
            paymentId = payment.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var principal = Assert.Single(await verificationContext.Accruals
            .AsNoTracking()
            .Where(item => item.FeeCampaignId == campaignId && !item.IsCanceled)
            .ToArrayAsync());
        Assert.Equal(stableIncomeTypeId, principal.IncomeTypeId);
        Assert.Equal(600m, principal.Amount);
        var allocation = Assert.Single(await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item => item.IsActive && item.FinancialOperationId == paymentId)
            .ToArrayAsync());
        Assert.Equal(principal.Id, allocation.AccrualId);
        Assert.Equal(600m, allocation.Amount);
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_OpenCampaignReactivatesCanceledPrincipalForTaggedPayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid garageId;
        Guid principalId;
        Guid paymentId;
        await using (var legacyContext = database.CreateContext())
        {
            var incomeType = await legacyContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var owner = new Owner { LastName = "Legacy", FirstName = "Open canceled principal" };
            var garage = new Garage { Number = "FEE-OPEN-CANCELED", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Open campaign with canceled principal", incomeType, 500m);
            campaign.TargetAmount = 5000m;
            var principal = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 6, 1), 500m);
            principal.IsCanceled = true;
            var payment = CreateCampaignIncome(garage, incomeType, campaign, new DateOnly(2026, 6, 15), 300m);
            legacyContext.AddRange(owner, garage, campaign, principal, payment);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            principalId = principal.Id;
            paymentId = payment.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var activePrincipals = await verificationContext.Accruals
            .AsNoTracking()
            .Where(item => item.GarageId == garageId && item.FeeCampaignId == campaignId && !item.IsCanceled)
            .ToArrayAsync();
        var repairedPrincipal = Assert.Single(activePrincipals);
        Assert.Equal(principalId, repairedPrincipal.Id);
        Assert.Equal(500m, repairedPrincipal.Amount);
        var allocation = Assert.Single(await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item => item.IsActive && item.FinancialOperationId == paymentId)
            .ToArrayAsync());
        Assert.Equal(principalId, allocation.AccrualId);
        Assert.Equal(300m, allocation.Amount);

        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var row = Assert.Single(worksheet.Value!.Rows, item => item.FeeCampaignId == campaignId);
        Assert.Equal(300m, row.IncomeAmount);
        Assert.Equal(200m, row.Debt);
        Assert.Equal(200m, worksheet.Value.DebtTotal);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_ClosedCampaignKeepsExistingActivePrincipalBeforeOlderCanceledHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid canceledHistoryId;
        Guid activePrincipalId;
        Guid paymentId;
        await using (var legacyContext = database.CreateContext())
        {
            var incomeType = await legacyContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var owner = new Owner { LastName = "Legacy", FirstName = "Mixed principal" };
            var garage = new Garage { Number = "FEE-CLOSED-MIXED", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Closed campaign mixed history", incomeType, 500m);
            campaign.ClosedAtUtc = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
            campaign.IsClosedEarly = true;
            campaign.ClosureComment = "Legacy mixed active/canceled history";
            var canceledHistory = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 5, 1), 500m);
            canceledHistory.IsCanceled = true;
            var activePrincipal = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 6, 1), 500m);
            var payment = CreateCampaignIncome(garage, incomeType, campaign, new DateOnly(2026, 6, 15), 300m);
            legacyContext.AddRange(owner, garage, campaign, canceledHistory, activePrincipal, payment);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            canceledHistoryId = canceledHistory.Id;
            activePrincipalId = activePrincipal.Id;
            paymentId = payment.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var accruals = await verificationContext.Accruals
            .AsNoTracking()
            .Where(item => item.FeeCampaignId == campaignId)
            .ToArrayAsync();
        var principal = Assert.Single(accruals, item => !item.IsCanceled);
        Assert.Equal(activePrincipalId, principal.Id);
        Assert.Equal(300m, principal.Amount);
        Assert.True(Assert.Single(accruals, item => item.Id == canceledHistoryId).IsCanceled);
        var allocation = Assert.Single(await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item => item.IsActive && item.FinancialOperationId == paymentId)
            .ToArrayAsync());
        Assert.Equal(activePrincipalId, allocation.AccrualId);
        Assert.Equal(300m, allocation.Amount);
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_OpenCampaignRestoresUnpaidCanceledPrincipalBeforePartialPayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        Guid campaignId;
        Guid garageId;
        Guid incomeTypeId;
        Guid principalId;
        await using (var legacyContext = database.CreateContext())
        {
            var incomeType = await legacyContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var owner = new Owner { LastName = "Legacy", FirstName = "Unpaid canceled principal" };
            var garage = new Garage { Number = "FEE-OPEN-UNPAID-CANCELED", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var campaign = CreateCampaign("Open unpaid canceled campaign", incomeType, 500m);
            campaign.TargetAmount = 5000m;
            var principal = CreateCampaignAccrual(garage, incomeType, campaign, new DateOnly(2026, 6, 1), 500m);
            principal.IsCanceled = true;
            legacyContext.AddRange(owner, garage, campaign, principal);
            await legacyContext.SaveChangesAsync();
            campaignId = campaign.Id;
            garageId = garage.Id;
            incomeTypeId = incomeType.Id;
            principalId = principal.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
            var migratedPrincipal = await migrationContext.Accruals
                .AsNoTracking()
                .SingleAsync(item => item.Id == principalId);
            Assert.False(migratedPrincipal.IsCanceled);
            Assert.Equal(500m, migratedPrincipal.Amount);
        }

        Guid paymentId;
        await using (var paymentContext = database.CreateContext())
        {
            var payment = await FinanceServiceTestFactory.Create(paymentContext).CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    garageId,
                    incomeTypeId,
                    new DateOnly(2026, 7, 15),
                    new DateOnly(2026, 7, 1),
                    300m,
                    "FEE-AFTER-UNPAID-REPAIR",
                    null,
                    FeeCampaignId: campaignId),
                null,
                CancellationToken.None);
            Assert.True(payment.Succeeded, payment.ErrorMessage);
            paymentId = payment.Value!.Id;
        }

        await using var verificationContext = database.CreateContext();
        var activePrincipal = await verificationContext.Accruals
            .AsNoTracking()
            .SingleAsync(item => item.Id == principalId && !item.IsCanceled);
        Assert.Equal(500m, activePrincipal.Amount);
        var allocation = Assert.Single(await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item => item.IsActive && item.FinancialOperationId == paymentId)
            .ToArrayAsync());
        Assert.Equal(principalId, allocation.AccrualId);
        Assert.Equal(300m, allocation.Amount);
        var worksheet = await FinanceServiceTestFactory.Create(verificationContext).GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var row = Assert.Single(worksheet.Value!.Rows, item => item.FeeCampaignId == campaignId);
        Assert.Equal(300m, row.IncomeAmount);
        Assert.Equal(200m, row.Debt);
        Assert.Equal(200m, worksheet.Value.DebtTotal);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
    }

    private static FeeCampaign CreateCampaign(string name, IncomeType incomeType, decimal contributionAmount) =>
        new()
        {
            Name = name,
            IncomeTypeId = incomeType.Id,
            IncomeType = incomeType,
            ContributionAmount = contributionAmount,
            TargetAmount = contributionAmount * 10,
            StartsOn = new DateOnly(2026, 1, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };

    private static Accrual CreateCampaignAccrual(
        Garage garage,
        IncomeType incomeType,
        FeeCampaign campaign,
        DateOnly accountingMonth,
        decimal amount) =>
        new()
        {
            Garage = garage,
            IncomeType = incomeType,
            FeeCampaign = campaign,
            AccountingMonth = accountingMonth,
            DueDate = accountingMonth.AddMonths(1).AddDays(-1),
            OverdueFromDate = accountingMonth.AddMonths(1),
            Amount = amount,
            Source = AccrualSources.FeeCampaign,
            Basis = campaign.Name
        };

    private static FinancialOperation CreateCampaignIncome(
        Garage garage,
        IncomeType incomeType,
        FeeCampaign campaign,
        DateOnly operationDate,
        decimal amount) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = operationDate,
            AccountingMonth = new DateOnly(operationDate.Year, operationDate.Month, 1),
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType,
            FeeCampaign = campaign
        };

    private static FinancialOperation CreateUntaggedIncome(
        Garage garage,
        IncomeType incomeType,
        DateOnly operationDate,
        decimal amount) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = operationDate,
            AccountingMonth = new DateOnly(operationDate.Year, operationDate.Month, 1),
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType
        };

    private static async Task<(Guid CampaignId, Guid GarageId)> SeedConcurrencyCampaignAsync(
        PostgreSqlTestDatabase database,
        string garageNumber)
    {
        await using var context = database.CreateContext();
        var incomeType = await context.IncomeTypes.SingleAsync(item => item.Code == "other_income");
        var owner = new Owner { LastName = "Concurrency", FirstName = "Campaign" };
        var garage = new Garage { Number = garageNumber, PeopleCount = 1, FloorCount = 1, Owner = owner };
        var campaign = CreateCampaign($"Campaign {garageNumber}", incomeType, 500m);
        campaign.AppliesToAllGarages = false;
        campaign.ParticipantGarages.Add(new FeeCampaignGarage { FeeCampaign = campaign, Garage = garage });
        context.AddRange(owner, garage, campaign);
        await context.SaveChangesAsync();
        return (campaign.Id, garage.Id);
    }

    private static async Task WaitForAdvisoryLockWaiterAsync(
        string connectionString,
        TimeSpan timeout,
        int expectedWaiterCount = 1)
    {
        await using var monitorConnection = new NpgsqlConnection(connectionString);
        await monitorConnection.OpenAsync();
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = monitorConnection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*)
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND pid <> pg_backend_pid()
                  AND wait_event_type = 'Lock'
                  AND wait_event = 'advisory'
                """;
            var waiterCount = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (waiterCount >= expectedWaiterCount)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new TimeoutException("Concurrent service call did not reach the PostgreSQL advisory lock wait state.");
    }

    private static UpsertFeeCampaignRequest CampaignRequest(
        string name,
        Guid incomeTypeId,
        bool appliesToAllGarages,
        IReadOnlyList<Guid> participantGarageIds) =>
        new(
            name,
            incomeTypeId,
            null,
            500m,
            5000m,
            new DateOnly(2026, 5, 1),
            null,
            appliesToAllGarages,
            30,
            participantGarageIds);
}
