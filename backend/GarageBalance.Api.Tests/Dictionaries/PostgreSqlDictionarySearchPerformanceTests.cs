using System.Data.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlDictionarySearchPerformanceTests
{
    [PostgreSqlFact]
    public async Task SmallDictionarySearchUsesRawIlikeAndTreatsWildcardsLiterally()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateContext())
        {
            setupContext.SupplierGroups.AddRange(
                new SupplierGroup { Name = "Группа 100%_готово" },
                new SupplierGroup { Name = "Группа 100 процентов готово" });
            setupContext.MeasurementUnits.AddRange(
                new MeasurementUnit { Name = "Единица 100%_готово" },
                new MeasurementUnit { Name = "Единица 100 процентов готово" });
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var groups = await new EfSupplierGroupRepository(context)
            .GetListAsync("%_", false, 25, CancellationToken.None);
        var groupCommand = capture.TakeCommandsAndClear().Single();
        var units = await new EfMeasurementUnitRepository(context)
            .GetListAsync("%_", false, 25, CancellationToken.None);
        var unitCommand = capture.TakeCommandsAndClear().Single();

        Assert.Collection(groups, item => Assert.Equal("Группа 100%_готово", item.Name));
        Assert.Collection(units, item => Assert.Equal("Единица 100%_готово", item.Name));
        AssertRawIlike(groupCommand);
        AssertRawIlike(unitCommand);
    }

    [PostgreSqlFact]
    public async Task DictionaryPages_KeepSearchPagingAndRelatedDataQueriesBounded()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var group = new SupplierGroup { Name = "Коммунальные услуги" };
        var department = new StaffDepartment { Name = "Бухгалтерия" };
        var owners = Enumerable.Range(1, 120)
            .Select(index => new Owner
            {
                LastName = index == 73 ? "Целевой%Владелец" : $"Владелец{index:D3}",
                FirstName = "Тест"
            })
            .ToArray();
        var garages = owners.Select((owner, index) => new Garage
        {
            Number = index == 73 ? "73%А" : $"{index + 1:D4}",
            Owner = owner
        }).ToArray();
        var suppliers = Enumerable.Range(1, 120)
            .Select(index => new Supplier
            {
                Name = index == 73 ? "Поставщик%Цель" : $"Поставщик {index:D3}",
                Inn = $"77{index:D8}",
                Group = group
            })
            .ToArray();
        var contacts = suppliers.Select((supplier, index) => new SupplierContact
        {
            Supplier = supplier,
            FullName = index == 73 ? "Контакт%Цель" : $"Контакт {index:D3}",
            Status = "Работает"
        }).ToArray();
        var staff = Enumerable.Range(1, 120)
            .Select(index => new StaffMember
            {
                FullName = index == 73 ? "Сотрудник%Цель" : $"Сотрудник {index:D3}",
                Department = department,
                Rate = index
            })
            .ToArray();

        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(group, department);
            setupContext.AddRange(owners);
            setupContext.AddRange(garages);
            setupContext.AddRange(suppliers);
            setupContext.AddRange(contacts);
            setupContext.AddRange(staff);
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var ownerPage = await new EfOwnerRepository(context).GetPageAsync("%", false, 0, 10, CancellationToken.None);
        Assert.Single(ownerPage.Items);
        Assert.Equal(1, ownerPage.TotalCount);
        Assert.Equal(3, capture.TakeCountAndClear());

        var garagePage = await new EfGarageRepository(context).GetPageAsync(
            "%",
            new GarageColumnFilters(null, null, null, null, null),
            false,
            false,
            0,
            10,
            "number",
            false,
            CancellationToken.None);
        Assert.Equal(2, garagePage.Items.Count);
        Assert.Equal(2, garagePage.TotalCount);
        Assert.Equal(2, capture.TakeCountAndClear());

        var supplierPage = await new EfSupplierRepository(context).GetPageAsync(
            null, "%", false, 0, 10, "name", false, CancellationToken.None);
        Assert.Single(supplierPage.Items);
        Assert.Equal(1, supplierPage.TotalCount);
        Assert.InRange(capture.TakeCountAndClear(), 2, 4);

        var contactPage = await new EfSupplierContactRepository(context).GetPageAsync(
            null, "%", false, 0, 10, "fullName", false, CancellationToken.None);
        Assert.Single(contactPage.Items);
        Assert.Equal(1, contactPage.TotalCount);
        Assert.Equal(2, capture.TakeCountAndClear());

        var staffPage = await new EfStaffMemberRepository(context).GetPageAsync(
            null, "%", false, 0, 10, "fullName", false, CancellationToken.None);
        Assert.Single(staffPage.Items);
        Assert.Equal(1, staffPage.TotalCount);
        Assert.Equal(2, capture.TakeCountAndClear());
    }

    [PostgreSqlFact]
    public async Task DictionarySearchMigration_CreatesAllPostgreSqlIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var indexNames = await context.Database
            .SqlQueryRaw<string>("""
                SELECT indexname AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'public'
                """)
            .ToListAsync();

        string[] expectedIndexes =
        [
            "IX_owners_LastName_trgm",
            "IX_owners_FullName_trgm",
            "IX_garages_Number_trgm",
            "IX_suppliers_Name_trgm",
            "IX_suppliers_Inn_trgm",
            "IX_supplier_groups_Name_trgm",
            "IX_measurement_units_Name_trgm",
            "IX_charge_service_settings_Name_trgm",
            "IX_supplier_contacts_FullName_trgm",
            "IX_supplier_contacts_Position_trgm",
            "IX_supplier_contacts_Phone_trgm",
            "IX_supplier_contacts_Email_trgm",
            "IX_supplier_contacts_PrimaryContact",
            "IX_staff_departments_Name_trgm",
            "IX_staff_members_FullName_trgm"
        ];
        Assert.All(expectedIndexes, expected => Assert.Contains(expected, indexNames));
    }

    [PostgreSqlFact]
    public async Task ReportGarageSearchPredicatesUseDictionaryTrigramIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        await AssertPlanUsesAsync(
            connection,
            "IX_garages_Number_trgm",
            """SELECT "Id" FROM garages WHERE "Number" ILIKE '%101-а%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_owners_LastName_trgm",
            """SELECT "Id" FROM owners WHERE "LastName" ILIKE '%иванов%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_owners_FullName_trgm",
            """
            SELECT "Id" FROM owners
            WHERE ("LastName" || ' ' || "FirstName" || ' ' || COALESCE("MiddleName", ''))
                  ILIKE '%иванов иван%' ESCAPE '\';
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_supplier_groups_Name_trgm",
            """SELECT "Id" FROM supplier_groups WHERE "Name" ILIKE '%коммунальные%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_measurement_units_Name_trgm",
            """SELECT "Id" FROM measurement_units WHERE "Name" ILIKE '%кубический метр%' ESCAPE '\';""");
    }

    private static void AssertRawIlike(string commandText)
    {
        Assert.DoesNotContain("lower(", commandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ILIKE", commandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", commandText, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertPlanUsesAsync(NpgsqlConnection connection, string indexName, string query)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SET enable_seqscan = off; EXPLAIN (ANALYZE, BUFFERS) {query}";
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        Assert.Contains(indexName, string.Join(Environment.NewLine, lines), StringComparison.Ordinal);
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        private int count;
        private readonly List<string> commands = [];

        public int TakeCountAndClear()
        {
            var result = count;
            count = 0;
            commands.Clear();
            return result;
        }

        public IReadOnlyList<string> TakeCommandsAndClear()
        {
            var result = commands.ToArray();
            count = 0;
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
                count++;
                commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
