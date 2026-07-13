using System.Reflection;
using System.Security.Claims;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Settings;

public sealed class SettingsControllerTests
{
    [Fact]
    public void PaymentDisplayActions_RequireReadAndAdminPermissions()
    {
        var getAction = typeof(SettingsController).GetMethod(nameof(SettingsController.GetPaymentDisplaySettings));
        var updateAction = typeof(SettingsController).GetMethod(nameof(SettingsController.UpdatePaymentDisplaySettings));

        Assert.Equal(SystemPermissions.PaymentsRead, Assert.Single(getAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(SystemPermissions.UsersManage, Assert.Single(updateAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
    }

    [Fact]
    public async Task GetPaymentDisplaySettings_ReturnsServiceValue()
    {
        var service = new FakeService { Current = new PaymentDisplaySettingsDto(false) };
        var controller = new SettingsController(service);

        var result = await controller.GetPaymentDisplaySettings(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(service.Current, ok.Value);
    }

    [Fact]
    public async Task UpdatePaymentDisplaySettings_PassesActorAndReturnsUpdatedValue()
    {
        var actorUserId = Guid.NewGuid();
        var service = new FakeService();
        var controller = new SettingsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())], "Test"))
                }
            }
        };
        var request = new UpdatePaymentDisplaySettingsRequest(true);

        var result = await controller.UpdatePaymentDisplaySettings(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<PaymentDisplaySettingsDto>(ok.Value);
        Assert.True(dto.ShowAllGarageOperationsByDefault);
        Assert.Same(request, service.ReceivedRequest);
        Assert.Equal(actorUserId, service.ReceivedActorUserId);
    }

    private sealed class FakeService : IApplicationSettingsService
    {
        public PaymentDisplaySettingsDto Current { get; set; } = new(false);
        public UpdatePaymentDisplaySettingsRequest? ReceivedRequest { get; private set; }
        public Guid? ReceivedActorUserId { get; private set; }

        public Task<PaymentDisplaySettingsDto> GetPaymentDisplaySettingsAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task<PaymentDisplaySettingsDto> UpdatePaymentDisplaySettingsAsync(UpdatePaymentDisplaySettingsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            ReceivedRequest = request;
            ReceivedActorUserId = actorUserId;
            Current = new PaymentDisplaySettingsDto(request.ShowAllGarageOperationsByDefault);
            return Task.FromResult(Current);
        }
    }
}
