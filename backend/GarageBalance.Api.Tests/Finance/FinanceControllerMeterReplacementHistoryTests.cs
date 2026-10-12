using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Finance;

public sealed partial class FinanceControllerTests
{
    [Fact]
    public async Task MeterHistory_PassesBoundedPageAndKeepsDtoShape()
    {
        var service = new FakeFinanceService { MeterDevices = [CreateMeterDevice()] };
        var controller = CreateController(service);
        var garage = Guid.NewGuid();
        var response = await controller.GetMeterDeviceHistory(garage, "water", 25, 25, default);
        var page = Assert.IsType<FinancePagedResult<MeterDeviceDto>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal((garage, "water", 25, 25), service.LastDeviceHistoryRequest);
        Assert.Single(page.Items);
        Assert.Equal((1, 25, 25), (page.TotalCount, page.Offset, page.Limit));
        var method = typeof(FinanceController).GetMethod(nameof(FinanceController.GetMeterDeviceHistory))!;
        Assert.Equal("meter-devices/history", Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>()).Template);
        Assert.Contains(typeof(FinanceController).GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>(), item => item.Policy == SystemPermissions.PaymentsRead);
    }

    [Fact]
    public async Task CancelReplacement_PassesActorVersionsAndRequiresWritePolicy()
    {
        var actor = Guid.NewGuid();
        var service = new FakeFinanceService { CancelReplacementResult = FinanceResult<MeterDeviceReplacementCancellationDto>.Success(new(CreateMeterDevice(), CreateMeterReading())) };
        var request = new CancelMeterDeviceReplacementRequest(Guid.NewGuid(), Guid.NewGuid(), new(2026, 6, 1), "Ошибка", 12.125m);
        var controller = CreateController(service, actor, new(2026, 6, 20));
        var device = Guid.NewGuid();
        var response = await controller.CancelMeterDeviceReplacement(device, request, default);
        Assert.IsType<OkObjectResult>(response.Result);
        Assert.Equal(device, service.LastCanceledDeviceId);
        Assert.Same(request, service.LastCancelReplacementRequest);
        Assert.Equal(actor, service.LastActorUserId);
        var method = typeof(FinanceController).GetMethod(nameof(FinanceController.CancelMeterDeviceReplacement))!;
        Assert.Equal("meter-devices/{deviceId:guid}/cancel-replacement", Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>()).Template);
        Assert.Contains(method.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>(), item => item.Policy == SystemPermissions.PaymentsWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelReplacement_HistoricalPermissionIsRequiredForAnotherMonth(bool allowed)
    {
        var service = new FakeFinanceService { CancelReplacementResult = FinanceResult<MeterDeviceReplacementCancellationDto>.Success(new(CreateMeterDevice(), CreateMeterReading())) };
        var controller = CreateController(service, businessDate: new(2026, 7, 20), permissions: allowed ? [SystemPermissions.HistoricalMeterReadingsCorrect] : []);
        var response = await controller.CancelMeterDeviceReplacement(Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), new(2026, 6, 1), "Причина"), default);
        if (allowed) Assert.IsType<OkObjectResult>(response.Result);
        else
        {
            Assert.IsType<ForbidResult>(response.Result);
            Assert.Null(service.LastCancelReplacementRequest);
        }
    }

    [Theory]
    [InlineData("meter_device_not_found", 404)]
    [InlineData("meter_reading_not_found", 404)]
    [InlineData("meter_device_conflict", 409)]
    [InlineData("meter_reading_conflict", 409)]
    [InlineData("meter_device_already_canceled", 409)]
    [InlineData("meter_device_later_replacement", 409)]
    [InlineData("meter_device_later_readings", 409)]
    [InlineData("meter_reading_accrual_paid", 409)]
    [InlineData("meter_device_cancel_reason_required", 400)]
    public async Task CancelReplacement_MapsValidationMissingAndConflict(string code, int status)
    {
        var service = new FakeFinanceService { CancelReplacementResult = FinanceResult<MeterDeviceReplacementCancellationDto>.Failure(code, "Операция отклонена") };
        var controller = CreateController(service, businessDate: new(2026, 6, 20));
        var response = await controller.CancelMeterDeviceReplacement(Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), new(2026, 6, 1), "Причина"), default);
        var result = Assert.IsAssignableFrom<ObjectResult>(response.Result);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(code, Assert.IsType<ProblemDetails>(result.Value).Title);
    }

    [Fact]
    public void CancelReplacement_DtoUsesConfigurableLimitedReason()
    {
        var parameter = typeof(CancelMeterDeviceReplacementRequest).GetConstructors().Single().GetParameters().Single(item => item.Name == "Reason");
        Assert.Contains(parameter.GetCustomAttributes(false), item => item is ActionCommentAttribute);
        Assert.Equal(500, Assert.Single(parameter.GetCustomAttributes(false).OfType<MaxLengthAttribute>()).Length);
        Assert.DoesNotContain(parameter.GetCustomAttributes(false), item => item is RequiredAttribute);
    }
}
