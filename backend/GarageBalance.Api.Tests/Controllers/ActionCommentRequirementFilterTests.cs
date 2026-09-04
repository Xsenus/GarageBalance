using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class ActionCommentRequirementFilterTests
{
    [Fact]
    public async Task OptionalSettingFlowsToBusinessServicesAndAllowsBlankComment()
    {
        var filter = new ActionCommentRequirementFilter(new FakeSettingsService(false));
        var context = CreateContext(new CommentRequest(string.Empty));
        var observedRequired = true;

        await filter.OnActionExecutionAsync(context, () =>
        {
            observedRequired = ActionCommentRequirementContext.IsRequired;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });

        Assert.False(observedRequired);
        Assert.Null(context.Result);
        Assert.True(ActionCommentRequirementContext.IsRequired);
    }

    [Fact]
    public async Task RequiredSettingRejectsBlankAnnotatedCommentBeforeAction()
    {
        var filter = new ActionCommentRequirementFilter(new FakeSettingsService(true));
        var context = CreateContext(new CommentRequest("   "));
        var executed = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(context.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal("action_comment_required", problem.Title);
        Assert.False(executed);
    }

    private static ActionExecutingContext CreateContext(CommentRequest request)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            [],
            new Dictionary<string, object?> { ["request"] = request },
            new object());
    }

    private sealed record CommentRequest([ActionComment] string? Reason);

    private sealed class FakeSettingsService(bool required) : IApplicationSettingsService
    {
        public Task<ActionCommentSettingsDto> GetActionCommentSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ActionCommentSettingsDto(required));

        public Task<ActionCommentSettingsDto> UpdateActionCommentSettingsAsync(UpdateActionCommentSettingsRequest request, Guid? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PayoutMutationSettingsDto> GetPayoutMutationSettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PayoutMutationSettingsDto> UpdatePayoutMutationSettingsAsync(UpdatePayoutMutationSettingsRequest request, Guid? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HistoricalMeterReadingCorrectionSettingsDto> GetHistoricalMeterReadingCorrectionSettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HistoricalMeterReadingCorrectionSettingsDto> UpdateHistoricalMeterReadingCorrectionSettingsAsync(UpdateHistoricalMeterReadingCorrectionSettingsRequest request, Guid? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymentDisplaySettingsDto> GetPaymentDisplaySettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymentDisplaySettingsDto> UpdatePaymentDisplaySettingsAsync(UpdatePaymentDisplaySettingsRequest request, Guid? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TariffTableDisplaySettingsDto> GetTariffTableDisplaySettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TariffTableDisplaySettingsDto> UpdateTariffTableDisplaySettingsAsync(UpdateTariffTableDisplaySettingsRequest request, Guid? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TariffPanelsLayoutDto> GetTariffPanelsLayoutAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TariffPanelsLayoutDto> UpdateTariffPanelsLayoutAsync(UpdateTariffPanelsLayoutRequest request, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SalaryAccrualSettingsDto> GetSalaryAccrualSettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SalaryAccrualSettingsDto> UpdateSalaryAccrualSettingsAsync(UpdateSalaryAccrualSettingsRequest request, Guid? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BusinessDateSettingsDto> GetBusinessDateSettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BusinessDateChangePreviewDto> PreviewBusinessDateChangeAsync(PreviewBusinessDateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BusinessDateSettingsDto> UpdateBusinessDateSettingsAsync(UpdateBusinessDateRequest request, Guid? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
