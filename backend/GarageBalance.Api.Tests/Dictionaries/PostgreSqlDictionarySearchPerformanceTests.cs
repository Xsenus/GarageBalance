using System.Data.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
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
        var owner = Assert.Single(ownerPage.Items);
        Assert.Equal(1, ownerPage.TotalCount);
        Assert.Equal("0073", Assert.Single(owner.Garages).Number);
        var ownerCommand = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("COUNT(*)", ownerCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", ownerCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", ownerCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PeopleCount", ownerCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("FloorCount", ownerCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialWaterMeterValue", ownerCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialElectricityMeterValue", ownerCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("Comment", ownerCommand, StringComparison.Ordinal);

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
        var garageCommand = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("COUNT(*)", garageCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", garageCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", garageCommand, StringComparison.OrdinalIgnoreCase);

        var supplierPage = await new EfSupplierRepository(context).GetPageAsync(
            null, "%", false, 0, 10, "name", false, CancellationToken.None);
        Assert.Single(supplierPage.Items);
        Assert.Equal(1, supplierPage.TotalCount);
        var supplierCommand = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("COUNT(*)", supplierCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", supplierCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supplier_contacts", supplierCommand, StringComparison.OrdinalIgnoreCase);

        var contactPage = await new EfSupplierContactRepository(context).GetPageAsync(
            null, "%", false, 0, 10, "fullName", false, CancellationToken.None);
        Assert.Single(contactPage.Items);
        Assert.Equal(1, contactPage.TotalCount);
        var contactCommand = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("COUNT(*)", contactCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", contactCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreatedAtUtc", contactCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", contactCommand, StringComparison.Ordinal);

        var staffPage = await new EfStaffMemberRepository(context).GetPageAsync(
            null, "%", false, 0, 10, "fullName", false, CancellationToken.None);
        Assert.Single(staffPage.Items);
        Assert.Equal(1, staffPage.TotalCount);
        var staffCommand = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("COUNT(*)", staffCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", staffCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreatedAtUtc", staffCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", staffCommand, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task CompactRelatedDictionaryPagesPreserveAllPostgresSortingAndEmptySlices()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var group = new SupplierGroup { Name = "Поставщики" };
        var suppliers = new[]
        {
            new Supplier { Name = "Поставщик В", Group = group },
            new Supplier { Name = "Поставщик А", Group = group },
            new Supplier { Name = "Поставщик Б", Group = group }
        };
        var contacts = new[]
        {
            new SupplierContact { Supplier = suppliers[0], FullName = "Контакт 1", Position = "Ведущий", Phone = "101", Email = "one@example.test", Status = "Работает", Comment = "Первый" },
            new SupplierContact { Supplier = suppliers[1], FullName = "Контакт 2", Position = "Аналитик", Phone = "102", Email = "two@example.test", Status = "Не работает", Comment = "Второй" },
            new SupplierContact { Supplier = suppliers[2], FullName = "Контакт 3", Position = "Специалист", Phone = "103", Email = "three@example.test", Status = "В отпуске", Comment = "Третий" }
        };
        var departments = new[]
        {
            new StaffDepartment { Name = "Отдел В" },
            new StaffDepartment { Name = "Отдел А" },
            new StaffDepartment { Name = "Отдел Б" }
        };
        var staff = new[]
        {
            new StaffMember { FullName = "Сотрудник 1", Department = departments[0], Rate = 300m },
            new StaffMember { FullName = "Сотрудник 2", Department = departments[1], Rate = 100m },
            new StaffMember { FullName = "Сотрудник 3", Department = departments[2], Rate = 200m }
        };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.Add(group);
            setupContext.AddRange(suppliers);
            setupContext.AddRange(contacts);
            setupContext.AddRange(departments);
            setupContext.AddRange(staff);
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var contactRepository = new EfSupplierContactRepository(context);
        var contactCases = new (string SortBy, bool Descending, string ExpectedName)[]
        {
            ("supplier", false, "Контакт 2"),
            ("supplier", true, "Контакт 1"),
            ("position", false, "Контакт 2"),
            ("position", true, "Контакт 3"),
            ("status", false, "Контакт 3"),
            ("status", true, "Контакт 1"),
            ("fullName", false, "Контакт 1"),
            ("unsupported", true, "Контакт 3")
        };
        foreach (var item in contactCases)
        {
            var page = await contactRepository.GetPageAsync(
                null, null, false, 0, 1, item.SortBy, item.Descending, CancellationToken.None);
            Assert.Equal(3, page.TotalCount);
            var contact = Assert.Single(page.Items);
            Assert.Equal(item.ExpectedName, contact.FullName);
            Assert.NotEmpty(contact.Supplier.Name);
            AssertCompactPageCommand(capture);
        }

        var emptyContacts = await contactRepository.GetPageAsync(
            null, null, false, 3, 3, "fullName", false, CancellationToken.None);
        Assert.Equal(3, emptyContacts.TotalCount);
        Assert.Empty(emptyContacts.Items);
        AssertCompactPageCommand(capture);

        var staffRepository = new EfStaffMemberRepository(context);
        var staffCases = new (string SortBy, bool Descending, string ExpectedName)[]
        {
            ("department", false, "Сотрудник 2"),
            ("department", true, "Сотрудник 1"),
            ("rate", false, "Сотрудник 2"),
            ("rate", true, "Сотрудник 1"),
            ("fullName", false, "Сотрудник 1"),
            ("unsupported", true, "Сотрудник 3")
        };
        foreach (var item in staffCases)
        {
            var page = await staffRepository.GetPageAsync(
                null, null, false, 0, 1, item.SortBy, item.Descending, CancellationToken.None);
            Assert.Equal(3, page.TotalCount);
            var member = Assert.Single(page.Items);
            Assert.Equal(item.ExpectedName, member.FullName);
            Assert.NotEmpty(member.Department.Name);
            AssertCompactPageCommand(capture);
        }

        var emptyStaff = await staffRepository.GetPageAsync(
            null, null, false, 3, 3, "fullName", false, CancellationToken.None);
        Assert.Equal(3, emptyStaff.TotalCount);
        Assert.Empty(emptyStaff.Items);
        AssertCompactPageCommand(capture);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static void AssertCompactPageCommand(SelectCommandCapture capture)
    {
        var command = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("COUNT(*)", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task SmallDictionaryPagesReturnExactTotalsFromOneCompactPostgresCommand()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fund = new Fund { Name = "Фонд оптимизации страниц", NormalizedName = "ФОНД ОПТИМИЗАЦИИ СТРАНИЦ" };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.Add(fund);
            setupContext.ExpenseTypes.AddRange(
                new ExpenseType { Name = "Page A — расходы", Code = "page_expense_a", IsSystem = true },
                new ExpenseType { Name = "Page B — расходы", Code = "page_expense_b" },
                new ExpenseType { Name = "Page C — архив расходов", IsArchived = true });
            setupContext.IncomeTypes.AddRange(
                new IncomeType { Name = "Page A — поступления", Code = "page_income_a", DestinationFund = fund, IsSystem = true },
                new IncomeType { Name = "Page B — поступления", Code = "page_income_b" },
                new IncomeType { Name = "Page C — архив поступлений", IsArchived = true });
            setupContext.MeasurementUnits.AddRange(
                new MeasurementUnit { Name = "Page A — единица" },
                new MeasurementUnit { Name = "Page B — единица" },
                new MeasurementUnit { Name = "Page C — архив единиц", IsArchived = true });
            setupContext.SupplierGroups.AddRange(
                new SupplierGroup { Name = "Page A — группы", IsSystem = true },
                new SupplierGroup { Name = "Page B — группы" },
                new SupplierGroup { Name = "Page C — архив групп", IsArchived = true });
            setupContext.Tariffs.AddRange(
                new Tariff
                {
                    Name = "Page A — тариф",
                    CalculationBase = "По счетчику",
                    Rate = 35.5m,
                    EffectiveFrom = new DateOnly(2026, 8, 1),
                    Comment = "Текущий",
                    ElectricityTiersJson = "[]"
                },
                new Tariff
                {
                    Name = "Page B — тариф",
                    CalculationBase = "По счетчику",
                    Rate = 7.5m,
                    EffectiveFrom = new DateOnly(2026, 7, 1),
                    ElectricityFirstThreshold = 100m,
                    ElectricityFirstTierName = "Первый",
                    ElectricityFirstRate = 7.5m
                },
                new Tariff
                {
                    Name = "Page C — архив тарифов",
                    CalculationBase = "Фиксированная сумма",
                    Rate = 1m,
                    EffectiveFrom = new DateOnly(2026, 6, 1),
                    IsArchived = true
                });
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var expenseRepository = new EfExpenseTypeRepository(context);
        var expenses = await expenseRepository.GetPageAsync("page", false, 0, 1, CancellationToken.None);
        Assert.Equal(2, expenses.TotalCount);
        Assert.Equal("Page A — расходы", Assert.Single(expenses.Items).Name);
        AssertCompactPageCommand(capture);
        var emptyExpenses = await expenseRepository.GetPageAsync("page", false, 2, 1, CancellationToken.None);
        Assert.Equal(2, emptyExpenses.TotalCount);
        Assert.Empty(emptyExpenses.Items);
        AssertCompactPageCommand(capture);
        var archivedExpenses = await expenseRepository.GetPageAsync("page", true, 2, 1, CancellationToken.None);
        Assert.Equal(3, archivedExpenses.TotalCount);
        Assert.True(Assert.Single(archivedExpenses.Items).IsArchived);
        AssertCompactPageCommand(capture);

        var incomeRepository = new EfIncomeTypeRepository(context);
        var incomes = await incomeRepository.GetPageAsync("page", false, 0, 1, CancellationToken.None);
        var income = Assert.Single(incomes.Items);
        Assert.Equal(2, incomes.TotalCount);
        Assert.Equal("Фонд оптимизации страниц", income.DestinationFund?.Name);
        AssertCompactPageCommand(capture);
        var incomeWithoutFund = await incomeRepository.GetPageAsync("page", false, 1, 1, CancellationToken.None);
        Assert.Equal(2, incomeWithoutFund.TotalCount);
        Assert.Null(Assert.Single(incomeWithoutFund.Items).DestinationFund);
        AssertCompactPageCommand(capture);
        var emptyIncomes = await incomeRepository.GetPageAsync("page", false, 2, 1, CancellationToken.None);
        Assert.Equal(2, emptyIncomes.TotalCount);
        Assert.Empty(emptyIncomes.Items);
        AssertCompactPageCommand(capture);

        var unitRepository = new EfMeasurementUnitRepository(context);
        var units = await unitRepository.GetPageAsync("page", false, 0, 1, CancellationToken.None);
        Assert.Equal(2, units.TotalCount);
        Assert.Equal("Page A — единица", Assert.Single(units.Items).Name);
        AssertCompactPageCommand(capture);
        var emptyUnits = await unitRepository.GetPageAsync("page", false, 2, 1, CancellationToken.None);
        Assert.Equal(2, emptyUnits.TotalCount);
        Assert.Empty(emptyUnits.Items);
        AssertCompactPageCommand(capture);

        var groupRepository = new EfSupplierGroupRepository(context);
        var groups = await groupRepository.GetPageAsync("page", false, 0, 1, CancellationToken.None);
        Assert.Equal(2, groups.TotalCount);
        Assert.Equal("Page A — группы", Assert.Single(groups.Items).Name);
        AssertCompactPageCommand(capture);
        var emptyGroups = await groupRepository.GetPageAsync("page", false, 2, 1, CancellationToken.None);
        Assert.Equal(2, emptyGroups.TotalCount);
        Assert.Empty(emptyGroups.Items);
        AssertCompactPageCommand(capture);

        var tariffRepository = new EfTariffRepository(context);
        var tariffs = await tariffRepository.GetPageAsync("page", false, 0, 1, CancellationToken.None);
        var tariff = Assert.Single(tariffs.Items);
        Assert.Equal(2, tariffs.TotalCount);
        Assert.Equal("Page A — тариф", tariff.Name);
        Assert.Equal("[]", tariff.ElectricityTiersJson);
        Assert.NotEqual(Guid.Empty, tariff.Version);
        AssertCompactPageCommand(capture);
        var tieredTariffs = await tariffRepository.GetPageAsync("page", false, 1, 1, CancellationToken.None);
        var tieredTariff = Assert.Single(tieredTariffs.Items);
        Assert.Equal(2, tieredTariffs.TotalCount);
        Assert.Equal(100m, tieredTariff.ElectricityFirstThreshold);
        Assert.Equal("Первый", tieredTariff.ElectricityFirstTierName);
        Assert.Equal(7.5m, tieredTariff.ElectricityFirstRate);
        AssertCompactPageCommand(capture);
        var emptyTariffs = await tariffRepository.GetPageAsync("page", false, 2, 1, CancellationToken.None);
        Assert.Equal(2, emptyTariffs.TotalCount);
        Assert.Empty(emptyTariffs.Items);
        AssertCompactPageCommand(capture);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task OwnerPagePreservesOwnersWithoutActiveGaragesAndAnEmptySlice()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var archivedGarageOwner = new Owner { LastName = "Архивный", FirstName = "Гараж" };
        var ownerWithoutGarage = new Owner { LastName = "Без", FirstName = "Гаража" };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(archivedGarageOwner, ownerWithoutGarage);
            setupContext.Garages.Add(new Garage
            {
                Number = "АРХ-1",
                Owner = archivedGarageOwner,
                IsArchived = true
            });
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var repository = new EfOwnerRepository(context);

        var firstPage = await repository.GetPageAsync(null, false, 0, 2, CancellationToken.None);
        var emptyPage = await repository.GetPageAsync(null, false, 2, 2, CancellationToken.None);

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.All(firstPage.Items, owner => Assert.Empty(owner.Garages));
        Assert.Equal(2, emptyPage.TotalCount);
        Assert.Empty(emptyPage.Items);
        var commands = capture.TakeCommandsAndClear();
        Assert.Equal(2, commands.Count);
        Assert.All(commands, command => Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(context.ChangeTracker.Entries());
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
