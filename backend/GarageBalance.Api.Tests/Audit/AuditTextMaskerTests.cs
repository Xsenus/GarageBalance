using GarageBalance.Api.Application.Audit;

namespace GarageBalance.Api.Tests.Audit;

public sealed class AuditTextMaskerTests
{
    [Fact]
    public void Mask_ReturnsNullAndEmptyValuesAsIs()
    {
        Assert.Null(AuditTextMasker.Mask(null));
        Assert.Equal(string.Empty, AuditTextMasker.Mask(string.Empty));
    }

    [Fact]
    public void Mask_HidesEmailTokensSecretsAndLongNumbers()
    {
        var masked = AuditTextMasker.Mask("email user@example.com password=Secret123 token: abc.def.ghi api_key = key-123 account 40817810507220051060 Bearer eyJhbGciOi");

        Assert.Equal("email [email скрыт] password=[секрет скрыт] token: [секрет скрыт] api_key = [секрет скрыт] account [номер скрыт] Bearer [token скрыт]", masked);
    }

    [Fact]
    public void Mask_KeepsRegularGarageNumbersAndAmounts()
    {
        var masked = AuditTextMasker.Mask("Создано поступление 1 250,50 по гаражу 12.");

        Assert.Equal("Создано поступление 1 250,50 по гаражу 12.", masked);
    }
}
