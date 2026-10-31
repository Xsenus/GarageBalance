using GarageBalance.Api.Application.Finance;
using System.Text.Json;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfMeterReadingRepository(GarageBalanceDbContext dbContext) : IMeterReadingRepository
{
    public async Task<IReadOnlyDictionary<Guid, MeterReplacementMarkerDto>> GetReplacementMarkersAsync(
        IReadOnlyCollection<Guid> readingIds, CancellationToken cancellationToken)
    {
        if (readingIds.Count == 0) return new Dictionary<Guid, MeterReplacementMarkerDto>();
        var markers = await (from reading in dbContext.MeterReadings.AsNoTracking()
                             join device in dbContext.MeterDevices.AsNoTracking() on reading.MeterDeviceId equals device.Id
                             join old in dbContext.MeterDevices.AsNoTracking() on device.ReplacedDeviceId equals old.Id
                             where readingIds.Contains(reading.Id) && reading.IsMeterReplacement && !reading.IsCanceled && !device.IsCanceled
                             select new
                             {
                                 reading.Id,
                                 Marker = new MeterReplacementMarkerDto(device.Id, device.InstalledOn, old.SerialNumber, old.FinalValue,
                                     device.SerialNumber, device.InitialValue, reading.CurrentValue, reading.PreviousDeviceConsumption, reading.Consumption)
                             }).ToDictionaryAsync(item => item.Id, item => item.Marker, cancellationToken);
        // Legacy accounting restarts deliberately have no physical replacement link.
        // Explain their first reading without inventing removed-device consumption.
        var restarts = await (from reading in dbContext.MeterReadings.AsNoTracking()
                              join device in dbContext.MeterDevices.AsNoTracking() on reading.MeterDeviceId equals device.Id
                              join old in dbContext.MeterDevices.AsNoTracking() on device.GarageId equals old.GarageId
                              where readingIds.Contains(reading.Id) && !reading.IsCanceled && !reading.IsMeterReplacement &&
                                  !device.IsCanceled && device.ReplacedDeviceId == null && device.ReplacementAccountingMonth == null &&
                                  device.SerialNumber.StartsWith(MeterBaselineResolver.RestartDeviceSerialPrefix) &&
                                  reading.PreviousDeviceConsumption == 0m && reading.PreviousValue == device.InitialValue &&
                                  reading.Consumption == reading.CurrentValue - device.InitialValue &&
                                  reading.AccountingMonth.AddMonths(1) > device.InstalledOn &&
                                  old.Id == dbContext.MeterDevices.Where(previous => previous.GarageId == device.GarageId &&
                                      previous.MeterKind == device.MeterKind && !previous.IsCanceled && previous.Id != device.Id &&
                                      previous.RemovedOn == device.InstalledOn.AddDays(-1))
                                      .OrderByDescending(previous => previous.InstalledOn).ThenBy(previous => previous.Id)
                                      .Select(previous => previous.Id).FirstOrDefault() &&
                                  !dbContext.MeterReadings.Any(previous => !previous.IsCanceled && previous.MeterDeviceId == device.Id &&
                                      previous.AccountingMonth < reading.AccountingMonth)
                              select new
                              {
                                  reading.Id,
                                  Marker = new MeterReplacementMarkerDto(device.Id, device.InstalledOn, old.SerialNumber, old.FinalValue,
                                      device.SerialNumber, device.InitialValue, reading.CurrentValue, reading.PreviousDeviceConsumption, reading.Consumption, true)
                              }).ToListAsync(cancellationToken);
        foreach (var restart in restarts) markers.TryAdd(restart.Id, restart.Marker);
        return markers;
    }

    public async Task<IReadOnlyList<string>> GetStoredMeterKindsAsync(Guid garageId, CancellationToken cancellationToken) =>
        await dbContext.MeterReadings.Where(reading => reading.GarageId == garageId).Select(reading => reading.MeterKind)
            .Union(dbContext.MeterDevices.Where(device => device.GarageId == garageId).Select(device => device.MeterKind))
            .Union(dbContext.GarageMeterConnections.Where(connection => connection.GarageId == garageId).Select(connection => connection.MeterKind))
            .OrderBy(kind => kind).Take(501).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MeterReading>> GetListAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? meterKind,
        string? normalizedSearch,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), monthFrom, monthTo, meterKind);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            return (await Order(query).ToListAsync(cancellationToken))
                .Where(reading => ReadingMatchesSearch(reading, normalizedSearch))
                .Take(limit)
                .ToList();
        }

        return await Order(ApplySearch(query, normalizedSearch))
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<MeterReadingPageData> GetPageAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? meterKind,
        string? normalizedSearch,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), monthFrom, monthTo, meterKind);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            var filtered = (await Order(query).ToListAsync(cancellationToken))
                .Where(reading => ReadingMatchesSearch(reading, normalizedSearch))
                .ToList();
            return new MeterReadingPageData(filtered.Skip(offset).Take(limit).ToList(), filtered.Count);
        }

        query = ApplySearch(query, normalizedSearch);
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresPageAsync(query, offset, limit, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await Order(query)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new MeterReadingPageData(items, totalCount);
    }

    public async Task<IReadOnlyList<MeterReading>> GetActiveForGaragePeriodAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        IReadOnlyCollection<string> meterKinds,
        CancellationToken cancellationToken)
    {
        if (meterKinds.Count == 0)
        {
            return [];
        }

        return await dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                reading.GarageId == garageId &&
                reading.AccountingMonth >= monthFrom &&
                reading.AccountingMonth <= monthTo &&
                meterKinds.Contains(reading.MeterKind))
            .OrderBy(reading => reading.AccountingMonth)
            .ThenBy(reading => reading.MeterKind)
            .ToListAsync(cancellationToken);
    }

    public async Task<MeterAccrualPeriodData> GetAccrualPeriodDataAsync(
        IReadOnlyCollection<Guid> garageIds, IReadOnlyCollection<string> meterKinds,
        DateOnly monthFrom, DateOnly monthTo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (garageIds.Count == 0 || meterKinds.Count == 0 || monthFrom > monthTo) return new([], []);

        // Both branches are restricted before materialization. UNION ALL keeps connections
        // without readings and gives the calculation one snapshot with no extra round trip.
        var readings = dbContext.MeterReadings.AsNoTracking()
            .Where(reading => !reading.IsCanceled && garageIds.Contains(reading.GarageId) &&
                meterKinds.Contains(reading.MeterKind) && reading.AccountingMonth >= monthFrom && reading.AccountingMonth <= monthTo)
            .Select(reading => new
            {
                IsConnection = false,
                reading.Id,
                reading.GarageId,
                reading.MeterKind,
                PeriodDate = reading.AccountingMonth,
                OtherDate = (DateOnly?)reading.ReadingDate,
                reading.PreviousValue,
                reading.CurrentValue,
                reading.Consumption,
                BaselineId = (Guid?)null
            });
        var connections = dbContext.GarageMeterConnections.AsNoTracking()
            .Where(connection => connection.ClosedOn == null && garageIds.Contains(connection.GarageId) && meterKinds.Contains(connection.MeterKind))
            .Select(connection => new
            {
                IsConnection = true,
                connection.Id,
                connection.GarageId,
                connection.MeterKind,
                PeriodDate = connection.ConnectedOn,
                OtherDate = connection.ClosedOn,
                PreviousValue = 0m,
                CurrentValue = 0m,
                Consumption = 0m,
                BaselineId = connection.BaselineMeterDeviceId
            });
        var rows = await readings.Concat(connections).ToListAsync(cancellationToken);
        return new(
            rows.Where(row => !row.IsConnection).Select(row => new MeterReading
            {
                Id = row.Id,
                GarageId = row.GarageId,
                MeterKind = row.MeterKind,
                AccountingMonth = row.PeriodDate,
                ReadingDate = row.OtherDate!.Value,
                PreviousValue = row.PreviousValue,
                CurrentValue = row.CurrentValue,
                Consumption = row.Consumption
            }).ToArray(),
            rows.Where(row => row.IsConnection).Select(row => new GarageMeterConnection
            {
                Id = row.Id,
                GarageId = row.GarageId,
                MeterKind = row.MeterKind,
                ConnectedOn = row.PeriodDate,
                ClosedOn = row.OtherDate,
                BaselineMeterDeviceId = row.BaselineId
            }).ToArray());
    }

    private async Task<MeterReadingPageData> GetPostgresPageAsync(
        IQueryable<MeterReading> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = Order(query)
            .Skip(offset)
            .Take(limit)
            .Select(reading => new
            {
                Category = PageCategory,
                Id = (Guid?)reading.Id,
                GarageId = (Guid?)reading.GarageId,
                GarageNumber = (string?)reading.Garage.Number,
                OwnerId = reading.Garage.OwnerId,
                OwnerLastName = reading.Garage.Owner == null ? null : reading.Garage.Owner.LastName,
                OwnerFirstName = reading.Garage.Owner == null ? null : reading.Garage.Owner.FirstName,
                OwnerMiddleName = reading.Garage.Owner == null ? null : reading.Garage.Owner.MiddleName,
                MeterKind = (string?)reading.MeterKind,
                AccountingMonth = (DateOnly?)reading.AccountingMonth,
                ReadingDate = (DateOnly?)reading.ReadingDate,
                CurrentValue = (decimal?)reading.CurrentValue,
                PreviousValue = (decimal?)reading.PreviousValue,
                Consumption = (decimal?)reading.Consumption,
                HasGapWarning = (bool?)reading.HasGapWarning,
                Comment = reading.Comment,
                IsCanceled = (bool?)reading.IsCanceled,
                Version = (Guid?)reading.Version,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new
            {
                Category = TotalsCategory,
                Id = (Guid?)null,
                GarageId = (Guid?)null,
                GarageNumber = (string?)null,
                OwnerId = (Guid?)null,
                OwnerLastName = (string?)null,
                OwnerFirstName = (string?)null,
                OwnerMiddleName = (string?)null,
                MeterKind = (string?)null,
                AccountingMonth = (DateOnly?)null,
                ReadingDate = (DateOnly?)null,
                CurrentValue = (decimal?)null,
                PreviousValue = (decimal?)null,
                Consumption = (decimal?)null,
                HasGapWarning = (bool?)null,
                Comment = (string?)null,
                IsCanceled = (bool?)null,
                Version = (Guid?)null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenByDescending(row => row.AccountingMonth)
            .ThenBy(row => row.GarageNumber)
            .ThenBy(row => row.MeterKind)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new MeterReading
            {
                Id = row.Id!.Value,
                GarageId = row.GarageId!.Value,
                Garage = new Garage
                {
                    Id = row.GarageId.Value,
                    Number = row.GarageNumber!,
                    OwnerId = row.OwnerId,
                    Owner = row.OwnerId is null
                        ? null
                        : new Owner
                        {
                            Id = row.OwnerId.Value,
                            LastName = row.OwnerLastName!,
                            FirstName = row.OwnerFirstName!,
                            MiddleName = row.OwnerMiddleName
                        }
                },
                MeterKind = row.MeterKind!,
                AccountingMonth = row.AccountingMonth!.Value,
                ReadingDate = row.ReadingDate!.Value,
                CurrentValue = row.CurrentValue!.Value,
                PreviousValue = row.PreviousValue!.Value,
                Consumption = row.Consumption!.Value,
                HasGapWarning = row.HasGapWarning!.Value,
                Comment = row.Comment,
                IsCanceled = row.IsCanceled!.Value,
                Version = row.Version!.Value
            })
            .ToList();
        return new MeterReadingPageData(items, totalCount);
    }

    public async Task<MeterReadingYearPageData> GetYearPageAsync(
        int year,
        string meterKind,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresYearPageAsync(year, meterKind, offset, limit, cancellationToken);
        }

        var settingsQuery = dbContext.ChargeServiceSettings.AsNoTracking()
            .Where(setting => !setting.IsArchived && setting.IsRegular && setting.IsMetered && setting.MeterKind == meterKind);
        var monthFrom = new DateOnly(year, 1, 1);
        var monthTo = new DateOnly(year, 12, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var connections = dbContext.GarageMeterConnections.AsNoTracking().Where(connection => connection.MeterKind == meterKind);
        var garageQuery = dbContext.Garages.AsNoTracking()
            .Where(garage => !garage.IsArchived)
            .Where(garage => dbContext.MeterReadings.Any(reading => reading.GarageId == garage.Id && reading.MeterKind == meterKind &&
                    !reading.IsCanceled && reading.AccountingMonth >= monthFrom && reading.AccountingMonth <= monthTo) ||
                (connections.Any(connection => connection.GarageId == garage.Id)
                ? connections.Any(connection => connection.GarageId == garage.Id && connection.ConnectedOn <= yearEnd &&
                    (!connection.ClosedOn.HasValue || connection.ClosedOn >= monthFrom))
                : !settingsQuery.Any() || settingsQuery.Any(setting =>
                    !setting.AppliesToSelectedGarages || setting.GarageIds.Contains(garage.Id))));
        var totalCount = await garageQuery.CountAsync(cancellationToken);
        var garages = await garageQuery
            .OrderBy(garage => garage.Number.Length)
            .ThenBy(garage => garage.Number)
            .ThenBy(garage => garage.Id)
            .Skip(offset)
            .Take(limit)
            .Select(garage => new
            {
                garage.Id,
                garage.Number,
                garage.InitialMeterReadingMonth,
                garage.AccountingStartMonth,
                InitialReadingValue = meterKind == MeterKinds.Water ? garage.InitialWaterMeterValue : meterKind == MeterKinds.Electricity ? garage.InitialElectricityMeterValue : null,
                HasConnection = connections.Any(connection => connection.GarageId == garage.Id),
                ConnectedOn = connections.Where(connection => connection.GarageId == garage.Id)
                    .OrderByDescending(connection => connection.ConnectedOn <= yearEnd)
                    .ThenByDescending(connection => connection.ConnectedOn)
                    .Select(connection => (DateOnly?)connection.ConnectedOn)
                    .FirstOrDefault(),
                ClosedOn = connections.Where(connection => connection.GarageId == garage.Id)
                    .OrderByDescending(connection => connection.ConnectedOn <= yearEnd)
                    .ThenByDescending(connection => connection.ConnectedOn)
                    .Select(connection => connection.ClosedOn)
                    .FirstOrDefault(),
                BaselineMeterDeviceId = connections.Where(connection => connection.GarageId == garage.Id)
                    .OrderByDescending(connection => connection.ConnectedOn <= yearEnd)
                    .ThenByDescending(connection => connection.ConnectedOn)
                    .Select(connection => connection.BaselineMeterDeviceId)
                    .FirstOrDefault(),
                HasLegacyBase = (meterKind == MeterKinds.Water && garage.InitialWaterMeterValue.HasValue) ||
                    (meterKind == MeterKinds.Electricity && garage.InitialElectricityMeterValue.HasValue) ||
                    dbContext.MeterDevices.Any(device => device.GarageId == garage.Id && device.MeterKind == meterKind && !device.IsCanceled) ||
                    dbContext.MeterReadings.Any(reading => reading.GarageId == garage.Id && reading.MeterKind == meterKind && !reading.IsCanceled)
            })
            .ToListAsync(cancellationToken);
        var garageData = garages.Select(garage => new MeterReadingYearGarageData(
            garage.Id, garage.Number,
            !garage.HasConnection ? garage.InitialMeterReadingMonth : null,
            !garage.HasConnection ? garage.InitialReadingValue : null,
            garage.ConnectedOn,
            garage.HasConnection ? !garage.BaselineMeterDeviceId.HasValue : !garage.HasLegacyBase,
            !garage.HasConnection && !garage.HasLegacyBase,
            garage.ClosedOn, garage.AccountingStartMonth)).ToList();

        if (garages.Count == 0)
        {
            return new MeterReadingYearPageData(garageData, [], totalCount);
        }

        var garageIds = garages.Select(garage => garage.Id).ToArray();
        var devices = await dbContext.MeterDevices.AsNoTracking()
            .Where(device => garageIds.Contains(device.GarageId) && device.MeterKind == meterKind && !device.IsCanceled)
            .Select(device => new MeterDevice
            {
                Id = device.Id,
                GarageId = device.GarageId,
                MeterKind = device.MeterKind,
                SerialNumber = device.SerialNumber,
                InstalledOn = device.InstalledOn,
                RemovedOn = device.RemovedOn,
                InitialValue = device.InitialValue,
                ReplacedDeviceId = device.ReplacedDeviceId
            }).ToListAsync(cancellationToken);
        var firstReadings = await dbContext.MeterReadings.AsNoTracking()
            .Where(reading => garageIds.Contains(reading.GarageId) && reading.MeterKind == meterKind && !reading.IsCanceled)
            .GroupBy(reading => reading.GarageId)
            .Select(group => group.OrderBy(reading => reading.AccountingMonth).ThenBy(reading => reading.Id)
                .Select(reading => new MeterReading
                {
                    Id = reading.Id,
                    GarageId = reading.GarageId,
                    MeterKind = reading.MeterKind,
                    AccountingMonth = reading.AccountingMonth,
                    MeterDeviceId = reading.MeterDeviceId,
                    IsMeterReplacement = reading.IsMeterReplacement
                }).First()).ToListAsync(cancellationToken);
        var devicesByGarage = devices.ToLookup(device => device.GarageId);
        var firstByGarage = firstReadings.ToDictionary(reading => reading.GarageId);
        var connectionDevices = garages.ToDictionary(garage => garage.Id, garage => garage.BaselineMeterDeviceId);
        garageData = garageData.Select(garage => ApplyYearPageStartValue(garage, new MeterYearStartSnapshot
        {
            Devices = devicesByGarage[garage.Id].ToList(),
            FirstReading = firstByGarage.GetValueOrDefault(garage.Id),
            ConnectionBaselineDeviceId = connectionDevices[garage.Id]
        })).ToList();
        var readings = await dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                reading.MeterKind == meterKind &&
                reading.AccountingMonth >= monthFrom &&
                reading.AccountingMonth <= monthTo &&
                garageIds.Contains(reading.GarageId))
            .OrderBy(reading => reading.GarageId)
            .ThenBy(reading => reading.AccountingMonth)
            .Select(reading => new MeterReadingYearValueData(
                reading.Id,
                reading.GarageId,
                reading.AccountingMonth,
                reading.CurrentValue,
                reading.Version,
                reading.MeterDeviceId,
                reading.MeterDevice != null && (reading.IsMeterReplacement || reading.MeterDevice.SerialNumber.StartsWith(MeterBaselineResolver.RestartDeviceSerialPrefix))
                    ? reading.MeterDevice.SerialNumber
                    : null,
                reading.IsMeterReplacement))
            .ToListAsync(cancellationToken);

        return new MeterReadingYearPageData(garageData, readings, totalCount);
    }

    private async Task<MeterReadingYearPageData> GetPostgresYearPageAsync(
        int year,
        string meterKind,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var monthFrom = new DateOnly(year, 1, 1);
        var monthTo = new DateOnly(year, 12, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var rows = await dbContext.Database.SqlQuery<MeterReadingYearPageRow>($$"""
            WITH applicable_garages AS (
                SELECT garage."Id", garage."Number", garage."AccountingStartMonth",
                       CASE WHEN connection."Id" IS NULL THEN garage."InitialMeterReadingMonth" ELSE NULL END AS "InitialMeterReadingMonth",
                       CASE WHEN connection."Id" IS NULL THEN garage."InitialWaterMeterValue" ELSE NULL END AS "InitialWaterMeterValue",
                       CASE WHEN connection."Id" IS NULL THEN garage."InitialElectricityMeterValue" ELSE NULL END AS "InitialElectricityMeterValue",
                       connection."ConnectedOn", connection."ClosedOn", connection."BaselineMeterDeviceId" AS "ConnectionBaselineDeviceId",
                       CASE WHEN connection."Id" IS NOT NULL THEN connection."BaselineMeterDeviceId" IS NULL
                            ELSE NOT legacy."HasBase" END AS "RequiresStartValue",
                       connection."Id" IS NULL AND NOT legacy."HasBase" AS "RequiresConnectionDate"
                FROM garages AS garage
                LEFT JOIN LATERAL (
                    SELECT candidate."Id", candidate."ConnectedOn", candidate."ClosedOn", candidate."BaselineMeterDeviceId"
                    FROM garage_meter_connections AS candidate
                    WHERE candidate."GarageId" = garage."Id" AND candidate."MeterKind" = {{meterKind}}
                    ORDER BY (candidate."ConnectedOn" <= {{yearEnd}}) DESC, candidate."ConnectedOn" DESC
                    LIMIT 1
                ) AS connection ON TRUE
                CROSS JOIN LATERAL (
                    SELECT ({{meterKind}} = 'water' AND garage."InitialWaterMeterValue" IS NOT NULL)
                        OR ({{meterKind}} = 'electricity' AND garage."InitialElectricityMeterValue" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM meter_devices AS device WHERE device."GarageId" = garage."Id"
                            AND device."MeterKind" = {{meterKind}} AND device."IsCanceled" = FALSE)
                        OR EXISTS (SELECT 1 FROM meter_readings AS historical WHERE historical."GarageId" = garage."Id"
                            AND historical."MeterKind" = {{meterKind}} AND historical."IsCanceled" = FALSE) AS "HasBase"
                ) AS legacy
                WHERE garage."IsArchived" = FALSE
                  AND (EXISTS (SELECT 1 FROM meter_readings AS historical WHERE historical."GarageId" = garage."Id"
                        AND historical."MeterKind" = {{meterKind}} AND historical."IsCanceled" = FALSE
                        AND historical."AccountingMonth" >= {{monthFrom}} AND historical."AccountingMonth" <= {{monthTo}})
                  OR (connection."Id" IS NOT NULL AND (
                    (connection."ConnectedOn" <= {{yearEnd}} AND (connection."ClosedOn" IS NULL OR connection."ClosedOn" >= {{monthFrom}}))
                    ))
                  OR (connection."Id" IS NULL AND (
                    NOT EXISTS (
                        SELECT 1 FROM charge_service_settings AS setting
                        WHERE setting."IsArchived" = FALSE AND setting."IsRegular" = TRUE
                          AND setting."IsMetered" = TRUE AND setting."MeterKind" = {{meterKind}})
                    OR EXISTS (
                        SELECT 1 FROM charge_service_settings AS setting
                        WHERE setting."IsArchived" = FALSE AND setting."IsRegular" = TRUE
                          AND setting."IsMetered" = TRUE AND setting."MeterKind" = {{meterKind}}
                          AND (setting."AppliesToSelectedGarages" = FALSE OR garage."Id" = ANY(setting."GarageIds")))
                  )))
            ), paged_garages AS (
                SELECT
                    garage."Id" AS "GarageId",
                    garage."Number" AS "GarageNumber",
                    garage."InitialMeterReadingMonth" AS "InitialReadingMonth",
                    garage."AccountingStartMonth",
                    CASE WHEN {{meterKind}} = 'water' THEN garage."InitialWaterMeterValue"
                         WHEN {{meterKind}} = 'electricity' THEN garage."InitialElectricityMeterValue"
                         ELSE NULL END AS "InitialReadingValue",
                    garage."ConnectedOn", garage."ClosedOn", garage."RequiresStartValue", garage."RequiresConnectionDate", garage."ConnectionBaselineDeviceId",
                    ROW_NUMBER() OVER (
                        ORDER BY LENGTH(garage."Number"), garage."Number", garage."Id") AS "GarageOrdinal",
                    COUNT(*) OVER () AS "TotalCount"
                FROM applicable_garages AS garage
                ORDER BY LENGTH(garage."Number"), garage."Number", garage."Id"
                OFFSET {{offset}}
                LIMIT {{limit}}
            )
            SELECT
                1 AS "Category",
                paged_garage."GarageId",
                paged_garage."GarageNumber",
                paged_garage."InitialReadingMonth",
                paged_garage."AccountingStartMonth",
                paged_garage."InitialReadingValue",
                paged_garage."ConnectedOn", paged_garage."ClosedOn", paged_garage."RequiresStartValue", paged_garage."RequiresConnectionDate",
                paged_garage."GarageOrdinal",
                reading."Id" AS "ReadingId",
                reading."AccountingMonth",
                reading."CurrentValue",
                reading."Version",
                reading."MeterDeviceId",
                meter_device."SerialNumber" AS "MeterDeviceSerialNumber",
                COALESCE(reading."IsMeterReplacement", FALSE) AS "IsMeterReplacement",
                paged_garage."TotalCount",
                NULL::text AS "StartSnapshotJson"
            FROM paged_garages AS paged_garage
            LEFT JOIN meter_readings AS reading
                ON reading."GarageId" = paged_garage."GarageId"
               AND reading."IsCanceled" = FALSE
               AND reading."MeterKind" = {{meterKind}}
               AND reading."AccountingMonth" >= {{monthFrom}}
               AND reading."AccountingMonth" <= {{monthTo}}
            LEFT JOIN meter_devices AS meter_device
                ON meter_device."Id" = reading."MeterDeviceId"
                AND (reading."IsMeterReplacement" = TRUE OR meter_device."SerialNumber" LIKE {{MeterBaselineResolver.RestartDeviceSerialPrefix + "%"}})

            UNION ALL

            SELECT
                2 AS "Category",
                NULL::uuid AS "GarageId",
                NULL::text AS "GarageNumber",
                NULL::date AS "InitialReadingMonth",
                NULL::date AS "AccountingStartMonth",
                NULL::numeric AS "InitialReadingValue",
                NULL::date AS "ConnectedOn", NULL::date AS "ClosedOn", FALSE AS "RequiresStartValue", FALSE AS "RequiresConnectionDate",
                NULL::bigint AS "GarageOrdinal",
                NULL::uuid AS "ReadingId",
                NULL::date AS "AccountingMonth",
                NULL::numeric AS "CurrentValue",
                NULL::uuid AS "Version",
                NULL::uuid AS "MeterDeviceId",
                NULL::text AS "MeterDeviceSerialNumber",
                FALSE AS "IsMeterReplacement",
                (SELECT COUNT(*) FROM applicable_garages) AS "TotalCount",
                NULL::text AS "StartSnapshotJson"
            WHERE NOT EXISTS (SELECT 1 FROM paged_garages)

            UNION ALL

            SELECT 3 AS "Category", paged_garage."GarageId", NULL::text AS "GarageNumber",
                NULL::date AS "InitialReadingMonth", NULL::date AS "AccountingStartMonth", NULL::numeric AS "InitialReadingValue",
                NULL::date AS "ConnectedOn", NULL::date AS "ClosedOn", FALSE AS "RequiresStartValue", FALSE AS "RequiresConnectionDate",
                paged_garage."GarageOrdinal", NULL::uuid AS "ReadingId", NULL::date AS "AccountingMonth", NULL::numeric AS "CurrentValue",
                NULL::uuid AS "Version", NULL::uuid AS "MeterDeviceId", NULL::text AS "MeterDeviceSerialNumber", FALSE AS "IsMeterReplacement",
                0::bigint AS "TotalCount",
                jsonb_build_object(
                    'connectionBaselineDeviceId', paged_garage."ConnectionBaselineDeviceId",
                    'devices', COALESCE((
                        SELECT jsonb_agg(jsonb_build_object(
                            'id', device."Id", 'garageId', device."GarageId", 'meterKind', device."MeterKind",
                            'serialNumber', device."SerialNumber", 'installedOn', device."InstalledOn", 'removedOn', device."RemovedOn",
                            'initialValue', device."InitialValue", 'replacedDeviceId', device."ReplacedDeviceId"
                        ) ORDER BY device."InstalledOn", device."Id")
                        FROM meter_devices AS device
                        WHERE device."GarageId" = paged_garage."GarageId" AND device."MeterKind" = {{meterKind}} AND device."IsCanceled" = FALSE
                    ), '[]'::jsonb),
                    'firstReading', (
                        SELECT jsonb_build_object(
                            'id', first_reading."Id", 'garageId', first_reading."GarageId", 'meterKind', first_reading."MeterKind",
                            'accountingMonth', first_reading."AccountingMonth", 'meterDeviceId', first_reading."MeterDeviceId",
                            'isMeterReplacement', first_reading."IsMeterReplacement")
                        FROM meter_readings AS first_reading
                        WHERE first_reading."GarageId" = paged_garage."GarageId" AND first_reading."MeterKind" = {{meterKind}} AND first_reading."IsCanceled" = FALSE
                        ORDER BY first_reading."AccountingMonth", first_reading."Id" LIMIT 1
                    )
                )::text AS "StartSnapshotJson"
            FROM paged_garages AS paged_garage
            ORDER BY "Category", "GarageOrdinal", "AccountingMonth", "ReadingId"
            """).ToListAsync(cancellationToken);

        var totalCount = checked((int)(rows.FirstOrDefault()?.TotalCount ?? 0));
        var startSnapshots = rows.Where(row => row.Category == 3)
            .ToDictionary(row => row.GarageId!.Value,
                row => JsonSerializer.Deserialize<MeterYearStartSnapshot>(row.StartSnapshotJson!, MeterYearStartJsonOptions)!);
        var garages = rows
            .Where(row => row.Category == 1)
            .GroupBy(row => new { row.GarageId, row.GarageNumber, row.GarageOrdinal, row.InitialReadingMonth, row.InitialReadingValue, row.ConnectedOn, row.ClosedOn, row.RequiresStartValue, row.RequiresConnectionDate, row.AccountingStartMonth })
            .OrderBy(group => group.Key.GarageOrdinal)
            .Select(group => new MeterReadingYearGarageData(group.Key.GarageId!.Value, group.Key.GarageNumber!, group.Key.InitialReadingMonth, group.Key.InitialReadingValue,
                group.Key.ConnectedOn, group.Key.RequiresStartValue, group.Key.RequiresConnectionDate, group.Key.ClosedOn, group.Key.AccountingStartMonth))
            .Select(garage => ApplyYearPageStartValue(garage, startSnapshots[garage.Id]))
            .ToList();
        var readings = rows
            .Where(row => row.Category == 1 && row.ReadingId is not null)
            .OrderBy(row => row.GarageId)
            .ThenBy(row => row.AccountingMonth)
            .Select(row => new MeterReadingYearValueData(
                row.ReadingId!.Value,
                row.GarageId!.Value,
                row.AccountingMonth!.Value,
                row.CurrentValue!.Value,
                row.Version!.Value,
                row.MeterDeviceId,
                row.MeterDeviceSerialNumber,
                row.IsMeterReplacement))
            .ToList();
        return new MeterReadingYearPageData(garages, readings, totalCount);
    }

    private static readonly JsonSerializerOptions MeterYearStartJsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class MeterYearStartSnapshot
    {
        public Guid? ConnectionBaselineDeviceId { get; set; }
        public List<MeterDevice> Devices { get; set; } = [];
        public MeterReading? FirstReading { get; set; }
    }

    private static MeterReadingYearGarageData ApplyYearPageStartValue(MeterReadingYearGarageData garage, MeterYearStartSnapshot snapshot)
    {
        var devices = snapshot.Devices.OrderBy(device => device.InstalledOn).ThenBy(device => device.Id).ToList();
        var hasConnection = garage.ConnectedOn.HasValue;
        var baseline = hasConnection
            ? devices.FirstOrDefault(device => device.Id == snapshot.ConnectionBaselineDeviceId)
            : MeterBaselineResolver.FindBaselineDevice(devices, snapshot.FirstReading);
        var value = baseline?.InitialValue ?? (hasConnection ? null : garage.InitialReadingValue);
        return garage with
        {
            StartValue = value,
            StartValueDate = hasConnection ? garage.ConnectedOn : baseline is not null
                ? MeterBaselineResolver.GetAccountingStartDate(baseline, garage.AccountingStartMonth, devices, snapshot.FirstReading, garage.InitialReadingMonth)
                : garage.AccountingStartMonth?.AddMonths(-1) ?? garage.InitialReadingMonth,
            StartValueSource = value is null ? "unknown" : hasConnection ? "connection" : baseline is null ? "legacy" : "device",
            StartValueDeviceSerial = baseline?.SerialNumber,
            RequiresStartValue = hasConnection ? value is null : garage.RequiresStartValue
        };
    }

    private sealed class MeterReadingYearPageRow
    {
        public int Category { get; set; }
        public Guid? GarageId { get; set; }
        public string? GarageNumber { get; set; }
        public DateOnly? InitialReadingMonth { get; set; }
        public DateOnly? AccountingStartMonth { get; set; }
        public decimal? InitialReadingValue { get; set; }
        public DateOnly? ConnectedOn { get; set; }
        public DateOnly? ClosedOn { get; set; }
        public bool RequiresStartValue { get; set; }
        public bool RequiresConnectionDate { get; set; }
        public long? GarageOrdinal { get; set; }
        public Guid? ReadingId { get; set; }
        public DateOnly? AccountingMonth { get; set; }
        public decimal? CurrentValue { get; set; }
        public Guid? Version { get; set; }
        public Guid? MeterDeviceId { get; set; }
        public string? MeterDeviceSerialNumber { get; set; }
        public bool IsMeterReplacement { get; set; }
        public long TotalCount { get; set; }
        public string? StartSnapshotJson { get; set; }
    }

    public async Task<IReadOnlyDictionary<Guid, MeterReading>> GetActiveByGarageIdsAsync(
        IReadOnlyCollection<Guid> garageIds,
        string meterKind,
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        if (garageIds.Count == 0)
        {
            return new Dictionary<Guid, MeterReading>();
        }

        return await dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                garageIds.Contains(reading.GarageId) &&
                reading.MeterKind == meterKind &&
                reading.AccountingMonth == accountingMonth)
            .ToDictionaryAsync(reading => reading.GarageId, cancellationToken);
    }

    public async Task<int> CountActiveAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), monthFrom, monthTo, null);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            return (await query.ToListAsync(cancellationToken))
                .Count(reading => ReadingMatchesSearch(reading, normalizedSearch));
        }

        return await ApplySearch(query, normalizedSearch).CountAsync(cancellationToken);
    }

    public Task<bool> ActiveDuplicateExistsAsync(
        Guid? ignoredId,
        Guid garageId,
        string meterKind,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        dbContext.MeterReadings.AsNoTracking().AnyAsync(reading =>
            !reading.IsCanceled &&
            (!ignoredId.HasValue || reading.Id != ignoredId.Value) &&
            reading.GarageId == garageId &&
            reading.MeterKind == meterKind &&
            reading.AccountingMonth == accountingMonth,
            cancellationToken);

    public Task<MeterReading?> GetPreviousActiveAsync(
        Guid? ignoredId,
        Guid garageId,
        string meterKind,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        dbContext.MeterReadings.AsNoTracking()
            .Include(reading => reading.MeterDevice)
            .Where(reading =>
                !reading.IsCanceled &&
                (!ignoredId.HasValue || reading.Id != ignoredId.Value) &&
                reading.GarageId == garageId &&
                reading.MeterKind == meterKind &&
                reading.AccountingMonth < accountingMonth)
            .OrderByDescending(reading => reading.AccountingMonth)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<MeterReading?> GetNextActiveAsync(
        Guid? ignoredId,
        Guid garageId,
        string meterKind,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        dbContext.MeterReadings.AsNoTracking()
            .Include(reading => reading.MeterDevice)
            .Where(reading =>
                !reading.IsCanceled &&
                (!ignoredId.HasValue || reading.Id != ignoredId.Value) &&
                reading.GarageId == garageId &&
                reading.MeterKind == meterKind &&
                reading.AccountingMonth > accountingMonth)
            .OrderBy(reading => reading.AccountingMonth)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MeterReading>> GetActiveFromForUpdateAsync(
        Guid? ignoredId,
        Guid garageId,
        string meterKind,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        await dbContext.MeterReadings
            .Include(reading => reading.MeterDevice)
            .Where(reading =>
                !reading.IsCanceled &&
                (!ignoredId.HasValue || reading.Id != ignoredId.Value) &&
                reading.GarageId == garageId &&
                reading.MeterKind == meterKind &&
                reading.AccountingMonth >= accountingMonth)
            .OrderBy(reading => reading.AccountingMonth)
            .ThenBy(reading => reading.Id)
            .ToListAsync(cancellationToken);

    public Task<MeterReading?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.MeterReadings
            .Include(reading => reading.Garage)
            .ThenInclude(garage => garage.Owner)
            .Include(reading => reading.MeterDevice)
            .SingleOrDefaultAsync(reading => reading.Id == id, cancellationToken);

    public async Task ReloadForUpdateAsync(MeterReading reading, CancellationToken cancellationToken)
    {
        await dbContext.Entry(reading).ReloadAsync(cancellationToken);
        await dbContext.Entry(reading).Reference(item => item.Garage).LoadAsync(cancellationToken);
        if (reading.Garage.OwnerId.HasValue)
        {
            await dbContext.Entry(reading.Garage).Reference(garage => garage.Owner).LoadAsync(cancellationToken);
        }
    }

    public Task<MeterReading?> GetActiveAsync(
        Guid garageId,
        string meterKind,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        dbContext.MeterReadings.AsNoTracking().SingleOrDefaultAsync(
            reading =>
                !reading.IsCanceled &&
                reading.GarageId == garageId &&
                reading.MeterKind == meterKind &&
                reading.AccountingMonth == accountingMonth,
            cancellationToken);

    public async Task<IReadOnlyList<MeterDevice>> GetDevicesAsync(
        Guid garageId,
        string meterKind,
        CancellationToken cancellationToken) =>
        await dbContext.MeterDevices.AsNoTracking()
            .Where(device => device.GarageId == garageId && device.MeterKind == meterKind && !device.IsCanceled)
            .OrderByDescending(device => device.InstalledOn)
            .ThenByDescending(device => device.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MeterDevice>> GetDevicesForUpdateAsync(
        Guid garageId,
        string meterKind,
        CancellationToken cancellationToken) =>
        await dbContext.MeterDevices
            .Where(device => device.GarageId == garageId && device.MeterKind == meterKind && !device.IsCanceled)
            .OrderBy(device => device.InstalledOn)
            .ThenBy(device => device.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetLastReadingValuesByDeviceAsync(
        Guid garageId,
        string meterKind,
        CancellationToken cancellationToken)
    {
        var readings = await dbContext.MeterReadings.AsNoTracking()
            .Where(reading => !reading.IsCanceled && reading.GarageId == garageId &&
                reading.MeterKind == meterKind && reading.MeterDeviceId != null)
            .GroupBy(reading => reading.MeterDeviceId!.Value)
            .Select(group => new
            {
                DeviceId = group.Key,
                CurrentValue = group.OrderByDescending(reading => reading.AccountingMonth)
                    .ThenByDescending(reading => reading.Id)
                    .Select(reading => reading.CurrentValue).First()
            })
            .ToListAsync(cancellationToken);
        return readings.ToDictionary(reading => reading.DeviceId, reading => reading.CurrentValue);
    }

    public Task<MeterReading?> GetFirstActiveAsync(
        Guid garageId,
        string meterKind,
        CancellationToken cancellationToken) =>
        dbContext.MeterReadings.AsNoTracking()
            .Where(reading => !reading.IsCanceled && reading.GarageId == garageId && reading.MeterKind == meterKind)
            .OrderBy(reading => reading.AccountingMonth)
            .ThenBy(reading => reading.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<MeterDevice?> GetDeviceForDateForUpdateAsync(
        Guid garageId,
        string meterKind,
        DateOnly date,
        CancellationToken cancellationToken) =>
        dbContext.MeterDevices
            .Where(device =>
                device.GarageId == garageId &&
                device.MeterKind == meterKind &&
                !device.IsCanceled &&
                device.InstalledOn <= date &&
                (!device.RemovedOn.HasValue || device.RemovedOn.Value >= date))
            .OrderByDescending(device => device.InstalledOn)
            .ThenByDescending(device => device.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<MeterDevice?> GetActiveDeviceForUpdateAsync(
        Guid garageId,
        string meterKind,
        CancellationToken cancellationToken) =>
        dbContext.MeterDevices.SingleOrDefaultAsync(
            device => device.GarageId == garageId && device.MeterKind == meterKind && !device.RemovedOn.HasValue && !device.IsCanceled,
            cancellationToken);

    public async Task<IReadOnlyList<MeterReading>> GetAllActiveForUpdateAsync(
        Guid garageId,
        string meterKind,
        CancellationToken cancellationToken) =>
        await dbContext.MeterReadings
            .Include(reading => reading.Garage).ThenInclude(garage => garage.Owner)
            .Include(reading => reading.MeterDevice)
            .Where(reading => !reading.IsCanceled && reading.GarageId == garageId && reading.MeterKind == meterKind)
            .OrderBy(reading => reading.AccountingMonth)
            .ThenBy(reading => reading.Id)
            .ToListAsync(cancellationToken);

    public void Add(MeterReading reading) => dbContext.MeterReadings.Add(reading);

    public Task<MeterDevice?> FindDeviceAsync(Guid deviceId, CancellationToken cancellationToken) =>
        dbContext.MeterDevices.AsNoTracking().SingleOrDefaultAsync(device => device.Id == deviceId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetActiveReadingVersionsAsync(
        IReadOnlyCollection<Guid> readingIds, CancellationToken cancellationToken) =>
        await dbContext.MeterReadings.AsNoTracking().Where(reading => readingIds.Contains(reading.Id) && !reading.IsCanceled)
            .ToDictionaryAsync(reading => reading.Id, reading => reading.Version, cancellationToken);

    public async Task<MeterDevicePageData> GetDeviceHistoryPageAsync(
        Guid garageId, string? meterKind, int offset, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.MeterDevices.AsNoTracking().Where(device => device.GarageId == garageId);
        if (meterKind is not null)
        {
            query = query.Where(device => device.MeterKind == meterKind);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(device => device.InstalledOn)
            .ThenByDescending(device => device.Id).Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return new MeterDevicePageData(items, total);
    }

    public void Add(MeterDevice device) => dbContext.MeterDevices.Add(device);

    public async Task<IReadOnlyDictionary<Guid, DateOnly>> GetConnectionBaselineDatesAsync(Guid garageId, CancellationToken cancellationToken)
    {
        // Closed connections still explain a historical device's independent opening.
        var connections = await dbContext.GarageMeterConnections.AsNoTracking()
            .Where(connection => connection.GarageId == garageId && connection.BaselineMeterDeviceId.HasValue)
            .Select(connection => new { DeviceId = connection.BaselineMeterDeviceId!.Value, connection.ConnectedOn })
            .ToListAsync(cancellationToken);
        return connections.GroupBy(connection => connection.DeviceId)
            .ToDictionary(group => group.Key, group => group.Min(connection => connection.ConnectedOn));
    }

    public async Task<IReadOnlyList<GarageMeterConnection>> GetConnectionsAsync(Guid garageId, CancellationToken cancellationToken) =>
        await dbContext.GarageMeterConnections.AsNoTracking().Include(connection => connection.BaselineMeterDevice)
            .Where(connection => connection.GarageId == garageId && connection.ClosedOn == null)
            .OrderBy(connection => connection.MeterKind).ToListAsync(cancellationToken);

    public Task<GarageMeterConnection?> GetConnectionForUpdateAsync(Guid garageId, string meterKind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A new connection and its baseline are planned together before the caller commits.
        // SQL cannot see that pending boundary while rebuilding the existing meter chain.
        var pending = dbContext.GarageMeterConnections.Local.FirstOrDefault(connection =>
            connection.GarageId == garageId && connection.MeterKind == meterKind && connection.ClosedOn == null &&
            dbContext.Entry(connection).State == EntityState.Added);
        return pending is not null
            ? Task.FromResult<GarageMeterConnection?>(pending)
            : dbContext.GarageMeterConnections.Include(connection => connection.BaselineMeterDevice)
                .SingleOrDefaultAsync(connection => connection.GarageId == garageId && connection.MeterKind == meterKind && connection.ClosedOn == null,
                    cancellationToken);
    }

    public async Task<IReadOnlyList<GarageMeterConnection>> GetConnectionsAsync(
        IReadOnlyCollection<Guid> garageIds, IReadOnlyCollection<string> meterKinds, CancellationToken cancellationToken) =>
        await dbContext.GarageMeterConnections.AsNoTracking().Include(connection => connection.BaselineMeterDevice)
            .Where(connection => garageIds.Contains(connection.GarageId) && meterKinds.Contains(connection.MeterKind) && connection.ClosedOn == null)
            .OrderBy(connection => connection.GarageId).ThenBy(connection => connection.MeterKind).ToListAsync(cancellationToken);

    public void Add(GarageMeterConnection connection) => dbContext.GarageMeterConnections.Add(connection);

    public async Task<IMeterConnectionWriteScope> BeginConnectionWriteAsync(CancellationToken cancellationToken, bool allowExistingTransaction = false)
    {
        if (allowExistingTransaction && dbContext.Database.CurrentTransaction is { } outerTransaction)
        {
            var name = $"gb_accounting_{Guid.NewGuid():N}";
            await outerTransaction.CreateSavepointAsync(name, cancellationToken);
            return new AccountingSavepointScope(dbContext, outerTransaction, name);
        }
        // Keep the same physical session alive after commit/rollback until advisory leases
        // have been released. Otherwise EF can return a locked connection to the pool.
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            return new MeterConnectionWriteScope(dbContext, await dbContext.Database.BeginTransactionAsync(cancellationToken));
        }
        catch
        {
            await dbContext.Database.CloseConnectionAsync();
            throw;
        }
    }

    public Task<bool> ExistsConnectionAsync(Guid garageId, string meterKind, CancellationToken cancellationToken) =>
        dbContext.GarageMeterConnections.AsNoTracking()
            .AnyAsync(connection => connection.GarageId == garageId && connection.MeterKind == meterKind, cancellationToken);

    public IReadOnlyList<GarageMeterConnectionAccrualChange> GetPendingConnectionAccrualChanges(Guid garageId)
    {
        dbContext.ChangeTracker.DetectChanges();
        return dbContext.ChangeTracker.Entries<Accrual>()
            .Where(entry => entry.Entity.GarageId == garageId && entry.State == EntityState.Modified)
            .OrderBy(entry => entry.Entity.AccountingMonth).ThenBy(entry => entry.Entity.Id)
            .Select(entry => new GarageMeterConnectionAccrualChange(
                entry.Entity.Id, entry.Entity.AccountingMonth, entry.Entity.CalculationMeterKind,
                entry.OriginalValues.GetValue<decimal>(nameof(Accrual.Amount)), entry.Entity.Amount,
                entry.OriginalValues.GetValue<bool>(nameof(Accrual.RequiresMeterReading)), entry.Entity.RequiresMeterReading))
            .ToArray();
    }

    private sealed class AccountingSavepointScope(GarageBalanceDbContext context, IDbContextTransaction transaction, string name) : IMeterConnectionWriteScope
    {
        private bool completed;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await transaction.ReleaseSavepointAsync(name, cancellationToken);
            completed = true;
        }

        public async Task RollbackAsync()
        {
            if (completed) return;
            try
            {
                await transaction.RollbackToSavepointAsync(name, CancellationToken.None);
                await transaction.ReleaseSavepointAsync(name, CancellationToken.None);
            }
            finally
            {
                completed = true;
                context.ChangeTracker.Clear();
            }
        }

        public async ValueTask DisposeAsync() => await RollbackAsync();
    }

    private sealed class MeterConnectionWriteScope(GarageBalanceDbContext context, IDbContextTransaction transaction) : IMeterConnectionWriteScope
    {
        private bool committed;
        private bool rolledBack;
        private bool disposed;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }

        public async Task RollbackAsync()
        {
            if (committed || rolledBack) return;
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                rolledBack = true;
                context.ChangeTracker.Clear();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                await RollbackAsync();
            }
            finally
            {
                try
                {
                    await transaction.DisposeAsync();
                }
                finally
                {
                    await context.Database.CloseConnectionAsync();
                }
            }
        }
    }

    private IQueryable<MeterReading> QueryActive() =>
        dbContext.MeterReadings.AsNoTracking()
            .Include(reading => reading.Garage)
            .ThenInclude(garage => garage.Owner)
            .Where(reading => !reading.IsCanceled);

    private static IQueryable<MeterReading> ApplyFilters(
        IQueryable<MeterReading> query,
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? meterKind)
    {
        if (monthFrom.HasValue)
        {
            query = query.Where(reading => reading.AccountingMonth >= monthFrom.Value);
        }

        if (monthTo.HasValue)
        {
            query = query.Where(reading => reading.AccountingMonth <= monthTo.Value);
        }

        if (meterKind is not null)
        {
            query = query.Where(reading => reading.MeterKind == meterKind);
        }

        return query;
    }

    private IQueryable<MeterReading> ApplySearch(IQueryable<MeterReading> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
        var candidates = dbContext.MeterReadings.AsNoTracking();
        var matchingIds = candidates
            .Where(reading => EF.Functions.ILike(reading.Garage.Number, pattern, @"\"))
            .Select(reading => reading.Id)
            .Concat(candidates
                .Where(reading => reading.Comment != null && EF.Functions.ILike(reading.Comment, pattern, @"\"))
                .Select(reading => reading.Id));
        return query.Where(reading => matchingIds.Contains(reading.Id));
    }

    private static IOrderedQueryable<MeterReading> Order(IQueryable<MeterReading> query) =>
        query.OrderByDescending(reading => reading.AccountingMonth)
            .ThenBy(reading => reading.Garage.Number)
            .ThenBy(reading => reading.MeterKind);

    private static bool ReadingMatchesSearch(MeterReading reading, string normalizedSearch) =>
        reading.Garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (reading.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
