using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Tests.Common;

public sealed class AccountingTestDataBuilder
{
    private static readonly DateTimeOffset DefaultTimestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private int sequence;

    public Owner BuildOwner(
        string? lastName = null,
        string? firstName = null,
        string? middleName = null,
        bool isArchived = false)
    {
        var id = NextSequence();
        return new Owner
        {
            LastName = lastName ?? $"Owner{id}",
            FirstName = firstName ?? "Test",
            MiddleName = middleName,
            IsArchived = isArchived,
            CreatedAtUtc = DefaultTimestamp,
            UpdatedAtUtc = DefaultTimestamp
        };
    }

    public Garage BuildGarage(
        Owner? owner = null,
        string? number = null,
        int peopleCount = 1,
        int floorCount = 1,
        decimal startingBalance = 0m)
    {
        var id = NextSequence();
        var actualOwner = owner ?? BuildOwner();
        return new Garage
        {
            Number = number ?? $"TEST-{id}",
            PeopleCount = peopleCount,
            FloorCount = floorCount,
            StartingBalance = startingBalance,
            Owner = actualOwner,
            OwnerId = actualOwner.Id,
            CreatedAtUtc = DefaultTimestamp,
            UpdatedAtUtc = DefaultTimestamp
        };
    }

    public SupplierGroup BuildSupplierGroup(string? name = null, bool isSystem = false)
    {
        var id = NextSequence();
        return new SupplierGroup
        {
            Name = name ?? $"Supplier group {id}",
            IsSystem = isSystem,
            CreatedAtUtc = DefaultTimestamp,
            UpdatedAtUtc = DefaultTimestamp
        };
    }

    public Supplier BuildSupplier(
        SupplierGroup? group = null,
        string? name = null,
        decimal startingBalance = 0m)
    {
        var id = NextSequence();
        var actualGroup = group ?? BuildSupplierGroup();
        return new Supplier
        {
            Name = name ?? $"Supplier {id}",
            StartingBalance = startingBalance,
            Group = actualGroup,
            GroupId = actualGroup.Id,
            CreatedAtUtc = DefaultTimestamp,
            UpdatedAtUtc = DefaultTimestamp
        };
    }

    public IncomeType BuildIncomeType(string? name = null, string? code = null, bool isSystem = false)
    {
        var id = NextSequence();
        return new IncomeType
        {
            Name = name ?? $"Income type {id}",
            Code = code ?? $"income_{id}",
            IsSystem = isSystem,
            CreatedAtUtc = DefaultTimestamp,
            UpdatedAtUtc = DefaultTimestamp
        };
    }

    public ExpenseType BuildExpenseType(string? name = null, string? code = null, bool isSystem = false)
    {
        var id = NextSequence();
        return new ExpenseType
        {
            Name = name ?? $"Expense type {id}",
            Code = code ?? $"expense_{id}",
            IsSystem = isSystem,
            CreatedAtUtc = DefaultTimestamp,
            UpdatedAtUtc = DefaultTimestamp
        };
    }

    private int NextSequence()
    {
        return ++sequence;
    }
}
