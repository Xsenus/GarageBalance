using GarageBalance.Api.Infrastructure.Data;

namespace GarageBalance.Api.Tests.Infrastructure;

public sealed class PostgresLikeSearchTests
{
    [Theory]
    [InlineData("water", "%water%")]
    [InlineData("100%", "%100\\%%")]
    [InlineData("garage_1", "%garage\\_1%")]
    [InlineData(@"folder\name", @"%folder\\name%")]
    [InlineData(@"50%_done\today", @"%50\%\_done\\today%")]
    public void ContainsPattern_EscapesPostgreSqlLikeMetacharacters(string value, string expected)
    {
        Assert.Equal(expected, PostgresLikeSearch.ContainsPattern(value));
    }
}
