using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class IntegrationsControllerTests
{
    [Fact]
    public async Task GetOneCFreshStatus_ReturnsStatusFromService()
    {
        var expected = new OneCFreshIntegrationStatusDto(
            "OneCFresh",
            "1C Fresh",
            IsConfigured: true,
            CanSynchronize: false,
            "prepared",
            "Токен сохранен.",
            ["RefreshToken"],
            ["RefreshToken"],
            DateTimeOffset.UtcNow);
        var service = new FakeIntegrationStatusService(oneCFreshStatus: expected);
        var controller = new IntegrationsController(service);

        var result = await controller.GetOneCFreshStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.True(service.OneCFreshCalled);
    }

    [Fact]
    public async Task GetReceiptPrintingStatus_ReturnsStatusFromService()
    {
        var expected = new ReceiptPrintingIntegrationStatusDto(
            "ReceiptPrinting",
            "Печать чеков и квитанций",
            IsConfigured: true,
            CanPrint: false,
            "prepared",
            "Настройки сохранены.",
            ["DeviceConnection", "ReceiptTemplate"],
            ["DeviceConnection", "ReceiptTemplate"],
            ["Печать квитанции", "Отмена печати", "Повторная печать"],
            DateTimeOffset.UtcNow);
        var service = new FakeIntegrationStatusService(receiptPrintingStatus: expected);
        var controller = new IntegrationsController(service);

        var result = await controller.GetReceiptPrintingStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.True(service.ReceiptPrintingCalled);
    }

    private sealed class FakeIntegrationStatusService(
        OneCFreshIntegrationStatusDto? oneCFreshStatus = null,
        ReceiptPrintingIntegrationStatusDto? receiptPrintingStatus = null) : IIntegrationStatusService
    {
        public bool OneCFreshCalled { get; private set; }

        public bool ReceiptPrintingCalled { get; private set; }

        public Task<OneCFreshIntegrationStatusDto> GetOneCFreshStatusAsync(CancellationToken cancellationToken)
        {
            OneCFreshCalled = true;
            return Task.FromResult(oneCFreshStatus!);
        }

        public Task<ReceiptPrintingIntegrationStatusDto> GetReceiptPrintingStatusAsync(CancellationToken cancellationToken)
        {
            ReceiptPrintingCalled = true;
            return Task.FromResult(receiptPrintingStatus!);
        }
    }
}
