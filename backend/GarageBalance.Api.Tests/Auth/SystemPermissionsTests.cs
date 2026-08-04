using GarageBalance.Api.Domain.Security;

namespace GarageBalance.Api.Tests.Auth;

public sealed class SystemPermissionsTests
{
    [Fact]
    public void ExpandWithDependencies_AddsTransitiveReadPermissions()
    {
        var expanded = SystemPermissions.ExpandWithDependencies(
            [SystemPermissions.HistoricalMeterReadingsCorrect, SystemPermissions.OpeningDataAdjust, SystemPermissions.ReportsRead]);

        Assert.Equal(
            [
                SystemPermissions.DictionariesRead,
                SystemPermissions.DictionariesWrite,
                SystemPermissions.OpeningDataAdjust,
                SystemPermissions.HistoricalMeterReadingsCorrect,
                SystemPermissions.PaymentsRead,
                SystemPermissions.PaymentsWrite,
                SystemPermissions.ReportsRead
            ],
            expanded);
    }

    [Fact]
    public void ExpandWithDependencies_RemovesDuplicatesAndKeepsIndependentPermissions()
    {
        var expanded = SystemPermissions.ExpandWithDependencies(
            [SystemPermissions.AuditRead, SystemPermissions.AuditRead, SystemPermissions.UsersManage]);

        Assert.Equal([SystemPermissions.AuditRead, SystemPermissions.UsersManage], expanded);
    }
}
