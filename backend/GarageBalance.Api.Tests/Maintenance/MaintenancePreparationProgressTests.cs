using GarageBalance.StorageTool;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class MaintenancePreparationProgressTests
{
    private static readonly Guid Id = Guid.Parse("5aa5e64f-d46d-4af5-85a0-2cc016219477");
    private static readonly string Hash = new('a', 64);

    [Fact]
    public void OnlyNewPreparingThenBoundPreparedThenOneExecutingAreAllowed()
    {
        var preparing = new MaintenancePreparationProgress(Id, "preparing", null);
        var prepared = new MaintenancePreparationProgress(Id, "prepared", Hash);
        var executing = new MaintenancePreparationProgress(Id, "executing", Hash);
        foreach (var value in new[] { preparing, prepared, executing })
            Assert.Equal(value, MaintenancePreparationProgress.Parse(value.Encode()));
        MaintenancePreparationProgress.ValidateTransition(null, preparing);
        MaintenancePreparationProgress.ValidateTransition(preparing, prepared);
        MaintenancePreparationProgress.ValidateTransition(prepared, executing);
        Assert.Throws<InvalidOperationException>(() => MaintenancePreparationProgress.ValidateTransition(executing, executing));
        Assert.Throws<InvalidOperationException>(() => MaintenancePreparationProgress.ValidateTransition(executing, prepared));
        Assert.Throws<InvalidOperationException>(() => MaintenancePreparationProgress.ValidateTransition(null, prepared));
        Assert.Throws<InvalidOperationException>(() => MaintenancePreparationProgress.ValidateTransition(preparing, executing));
        Assert.Throws<InvalidOperationException>(() => MaintenancePreparationProgress.ValidateTransition(prepared, executing with { Fingerprint = new string('b', 64) }));
        Assert.Throws<InvalidOperationException>(() => MaintenancePreparationProgress.ValidateTransition(preparing, prepared with { PlanId = Guid.NewGuid() }));
    }

    [Theory]
    [InlineData("", "preparing", "", true)]
    [InlineData("00000000-0000-0000-0000-000000000000", "preparing", "", true)]
    [InlineData("5aa5e64fd46d4af585a02cc016219477", "preparing", "", true)]
    [InlineData("5aa5e64f-d46d-4af5-85a0-2cc016219477", "unknown", "", true)]
    [InlineData("5aa5e64f-d46d-4af5-85a0-2cc016219477", "preparing", "a", true)]
    [InlineData("5aa5e64f-d46d-4af5-85a0-2cc016219477", "prepared", "", true)]
    [InlineData("5aa5e64f-d46d-4af5-85a0-2cc016219477", "executing", "invalid", true)]
    [InlineData("5aa5e64f-d46d-4af5-85a0-2cc016219477", "preparing", "", false)]
    public void MalformedJournalIsNeverTreatedAsPrepared(string id, string phase, string hash, bool newline)
    {
        var content = $"{id}\n{phase}\n{hash}" + (newline ? "\n" : "");
        Assert.Throws<InvalidOperationException>(() => MaintenancePreparationProgress.Parse(content));
    }

    [Fact]
    public void EncodeAndTransitionRejectInvalidObjectRatherThanWritingIt()
    {
        var invalid = new MaintenancePreparationProgress(Id, "preparing", Hash);
        Assert.Throws<InvalidOperationException>(() => invalid.Encode());
        Assert.Throws<InvalidOperationException>(() => MaintenancePreparationProgress.ValidateTransition(null, invalid));
        Assert.Throws<InvalidOperationException>(() => MaintenancePreparationProgress.Parse($"{Id:D}\nexecuting\n{new string('z', 64)}\n"));
        Assert.Throws<InvalidOperationException>(() => MaintenancePreparationProgress.Parse($"{Id:D}\npreparing\n\nextra\n"));
    }
}
