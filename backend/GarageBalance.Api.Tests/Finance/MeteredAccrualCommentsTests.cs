using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeteredAccrualCommentsTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("Заметка сотрудника", "Заметка сотрудника")]
    [InlineData("Автоначисление; тариф прежний.", "Автоначисление; тариф прежний.")]
    [InlineData("Начисление по показанию gas: расход 1", "Начисление по показанию gas: расход 1")]
    [InlineData("Начисление по показанию water: расход 1 — уточнить", "Начисление по показанию water: расход 1 — уточнить")]
    [InlineData("Начисление по показанию water: расход 1\n", "Начисление по показанию water: расход 1\n")]
    [InlineData("Начисление по показанию water: расход 1", "Начисление по показанию water: расход 2,5")]
    [InlineData("Начисление по показанию electricity: расход 1.25; тариф 7.47; заметка", "Начисление по показанию electricity: расход 2,5; тариф 7.47; заметка")]
    [InlineData("Начисление по показанию water: расход1,25; тариф", "Начисление по показанию water: расход 2,5; тариф")]
    [InlineData("Начисление по показанию water: показание отменено; тариф", "Начисление по показанию water: расход 2,5; тариф")]
    public void RefreshChangesOnlyTheGeneratedHeading(string? input, string? expected) =>
        Assert.Equal(expected, MeteredAccrualComments.Refresh(input, 2.5m));

    [Fact]
    public void CancellationAndRestorationDoNotAccumulateTextOrLoseSuffix()
    {
        var suffix = "; тариф 7.47; " + new string('я', 1000);
        var comment = "Начисление по показанию water: расход 9" + suffix;
        var canceled = MeteredAccrualComments.Refresh(comment, null);
        Assert.Equal("Начисление по показанию water: показание отменено" + suffix, canceled);
        Assert.Equal(canceled, MeteredAccrualComments.Refresh(canceled, null));
        Assert.Equal("Начисление по показанию water: расход 0" + suffix, MeteredAccrualComments.Refresh(canceled, 0m));
        Assert.Equal(comment, MeteredAccrualComments.Refresh(canceled, 9m));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeteredAccrualComments.Refresh(comment, -1m));
    }
}
