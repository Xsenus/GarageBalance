using GarageBalance.Api.Application.Audit;

namespace GarageBalance.Api.Tests.Audit;

public sealed class AuditChangeDiffBuilderTests
{
    [Fact]
    public void Build_ReturnsOnlyChangedFieldsWithLabels()
    {
        var changes = AuditChangeDiffBuilder.Build(
            new Dictionary<string, object?>
            {
                ["garageNumber"] = "12",
                ["amount"] = 100.50m,
                ["comment"] = "old"
            },
            new Dictionary<string, object?>
            {
                ["garageNumber"] = "12",
                ["amount"] = 125.75m,
                ["comment"] = "new"
            },
            new Dictionary<string, string>
            {
                ["amount"] = "Сумма",
                ["comment"] = "Комментарий"
            });

        Assert.Collection(
            changes,
            change =>
            {
                Assert.Equal("Сумма", change.FieldName);
                Assert.Equal("100.5", change.OldValue);
                Assert.Equal("125.75", change.NewValue);
            },
            change =>
            {
                Assert.Equal("Комментарий", change.FieldName);
                Assert.Equal("old", change.OldValue);
                Assert.Equal("new", change.NewValue);
            });
    }

    [Fact]
    public void Build_ReturnsEmptyListWhenValuesDidNotChange()
    {
        var changes = AuditChangeDiffBuilder.Build(
            new Dictionary<string, object?>
            {
                ["name"] = "  Тариф  ",
                ["amount"] = 10m,
                ["empty"] = string.Empty
            },
            new Dictionary<string, object?>
            {
                ["name"] = "Тариф",
                ["amount"] = 10.00m,
                ["empty"] = null
            });

        Assert.Empty(changes);
    }

    [Fact]
    public void Build_HandlesAddedRemovedDatesAndMoney()
    {
        var changes = AuditChangeDiffBuilder.Build(
            new Dictionary<string, object?>
            {
                ["period"] = new DateOnly(2026, 6, 1),
                ["paidAt"] = null,
                ["balance"] = 5300m
            },
            new Dictionary<string, object?>
            {
                ["period"] = new DateOnly(2026, 7, 1),
                ["paidAt"] = new DateTimeOffset(2026, 7, 2, 9, 30, 0, TimeSpan.Zero),
                ["balance"] = null
            });

        Assert.Collection(
            changes,
            change =>
            {
                Assert.Equal("balance", change.FieldName);
                Assert.Equal("5300", change.OldValue);
                Assert.Null(change.NewValue);
            },
            change =>
            {
                Assert.Equal("paidAt", change.FieldName);
                Assert.Null(change.OldValue);
                Assert.Equal("2026-07-02T09:30:00.0000000+00:00", change.NewValue);
            },
            change =>
            {
                Assert.Equal("period", change.FieldName);
                Assert.Equal("2026-06-01", change.OldValue);
                Assert.Equal("2026-07-01", change.NewValue);
            });
    }

    [Fact]
    public void Build_MasksSensitiveFieldNamesAndValues()
    {
        var changes = AuditChangeDiffBuilder.Build(
            new Dictionary<string, object?>
            {
                ["ownerEmail"] = "old@example.com",
                ["phone"] = "+7 999 111-22-33",
                ["password"] = "OldPassword123",
                ["meterValue"] = 123,
                ["bankAccount"] = "40817810507220051060",
                ["comment"] = "token=old-token account 40817810507220051060"
            },
            new Dictionary<string, object?>
            {
                ["ownerEmail"] = "new@example.com",
                ["phone"] = "+7 999 444-55-66",
                ["password"] = "NewPassword123",
                ["meterValue"] = 145,
                ["bankAccount"] = "40817810507220051061",
                ["comment"] = "token=new-token account 40817810507220051061"
            },
            new Dictionary<string, string>
            {
                ["ownerEmail"] = "Почта владельца",
                ["phone"] = "Телефон",
                ["password"] = "Пароль",
                ["meterValue"] = "Счётчик",
                ["bankAccount"] = "Расчетный счет",
                ["comment"] = "Комментарий"
            });

        Assert.Collection(
            changes,
            change =>
            {
                Assert.Equal("Расчетный счет", change.FieldName);
                Assert.Equal("[секрет скрыт]", change.OldValue);
                Assert.Equal("[секрет скрыт]", change.NewValue);
            },
            change =>
            {
                Assert.Equal("Комментарий", change.FieldName);
                Assert.Equal("token=[секрет скрыт] account [номер скрыт]", change.OldValue);
                Assert.Equal("token=[секрет скрыт] account [номер скрыт]", change.NewValue);
            },
            change =>
            {
                Assert.Equal("Счётчик", change.FieldName);
                Assert.Equal("123", change.OldValue);
                Assert.Equal("145", change.NewValue);
            },
            change =>
            {
                Assert.Equal("Почта владельца", change.FieldName);
                Assert.Equal("[секрет скрыт]", change.OldValue);
                Assert.Equal("[секрет скрыт]", change.NewValue);
            },
            change =>
            {
                Assert.Equal("Пароль", change.FieldName);
                Assert.Equal("[секрет скрыт]", change.OldValue);
                Assert.Equal("[секрет скрыт]", change.NewValue);
            },
            change =>
            {
                Assert.Equal("Телефон", change.FieldName);
                Assert.Equal("[секрет скрыт]", change.OldValue);
                Assert.Equal("[секрет скрыт]", change.NewValue);
            });
    }

    [Fact]
    public void FormatSummary_UsesRussianBeforeAfterLabelsAndEmptyPlaceholder()
    {
        var summary = AuditChangeDiffBuilder.FormatSummary(
        [
            new AuditChangeDiff("Сумма", "100", "200"),
            new AuditChangeDiff("Комментарий", null, "Оплачено")
        ]);

        Assert.Equal("Сумма: было 100, стало 200; Комментарий: было (пусто), стало Оплачено", summary);
    }
}
