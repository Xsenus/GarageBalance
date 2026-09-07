using System.Reflection;
using System.Security.Claims;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class ExpenseBatchesControllerTests
{
    private static readonly DateOnly Month = new(2026, 8, 1);
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task Actions_ReturnDtosAndForwardActorAndCancellationToken()
    {
        var preview = new PreviewStub();
        var payment = new PaymentStub();
        var controller = Controller(preview, payment, Actor.ToString());
        using var cancellation = new CancellationTokenSource();
        var previewResponse = await controller.Preview(new(Month, Month), cancellation.Token);
        Assert.Same(preview.Result.Value, Assert.IsType<OkObjectResult>(previewResponse.Result).Value);
        var response = await controller.Pay(Request(), cancellation.Token);
        Assert.Same(payment.Result.Value, Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(Actor, payment.Actor);
        Assert.Equal(cancellation.Token, preview.Token);
        Assert.Equal(cancellation.Token, payment.Token);
        Assert.Equal("api/finance/expense-batches", typeof(ExpenseBatchesController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("preview", typeof(ExpenseBatchesController).GetMethod(nameof(ExpenseBatchesController.Preview))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Null(typeof(ExpenseBatchesController).GetMethod(nameof(ExpenseBatchesController.Pay))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Theory]
    [InlineData("expense_batch_request_conflict", 409)]
    [InlineData("expense_batch_preview_changed", 409)]
    [InlineData("expense_batch_concurrency", 409)]
    [InlineData("expense_batch_data_changed", 409)]
    [InlineData("expense_batch_not_payable", 400)]
    [InlineData("expense_batch_period_invalid", 400)]
    public async Task Actions_MapFailuresToStableProblemCodes(string code, int status)
    {
        var preview = new PreviewStub { Result = FinanceResult<ExpenseBatchPreviewDto>.Failure(code, "Проверка ошибки") };
        var payment = new PaymentStub { Result = FinanceResult<ExpenseBatchPaymentResult>.Failure(code, "Проверка ошибки") };
        var controller = Controller(preview, payment, Actor.ToString());
        var previewResponse = await controller.Preview(new(Month, Month), CancellationToken.None);
        var paymentResponse = await controller.Pay(Request(), CancellationToken.None);
        foreach (var response in new[] { previewResponse.Result, paymentResponse.Result })
        {
            var objectResult = Assert.IsType<ObjectResult>(response);
            Assert.Equal(status, objectResult.StatusCode);
            var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
            Assert.Equal(code, problem.Extensions[ApiProblemDetails.CodeExtensionKey]);
            Assert.Equal("Проверка ошибки", problem.Detail);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Pay_RejectsMissingActorWithoutCallingPaymentService(string? actor)
    {
        var payment = new PaymentStub();
        var response = await Controller(new PreviewStub(), payment, actor).Pay(Request(), CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(response.Result);
        Assert.Equal(0, payment.Calls);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task BothActions_RequireReadAndWritePermissions(bool read, bool write, bool allowed)
    {
        var policies = typeof(ExpenseBatchesController).GetCustomAttributes<AuthorizeAttribute>().Select(item => item.Policy).ToArray();
        Assert.Equal(new[] { SystemPermissions.PaymentsRead, SystemPermissions.PaymentsWrite }, policies);
        var claims = new List<Claim>();
        if (read) claims.Add(new("permission", SystemPermissions.PaymentsRead));
        if (write) claims.Add(new("permission", SystemPermissions.PaymentsWrite));
        var context = new AuthorizationHandlerContext(policies.Select(policy => new PermissionRequirement(policy!)).ToArray(),
            new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), null);
        await new PermissionAuthorizationHandler().HandleAsync(context);
        Assert.Equal(allowed, context.HasSucceeded);
    }

    [Fact]
    public async Task Actions_PropagateCancellation()
    {
        var controller = Controller(new PreviewStub(), new PaymentStub(), Actor.ToString());
        var token = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.Preview(new(Month, Month), token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.Pay(Request(), token));
    }

    private static ExpenseBatchesController Controller(PreviewStub preview, PaymentStub payment, string? actor) => new(preview, payment)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(actor is null ? [] : [new Claim(ClaimTypes.NameIdentifier, actor)], "Test"))
            }
        }
    };
    private static ExpenseBatchPaymentRequest Request() => new(Guid.NewGuid(), Month, Month, new string('a', 64), false, "Проверка");

    private sealed class PreviewStub : IExpenseBatchPreviewService
    {
        public FinanceResult<ExpenseBatchPreviewDto> Result { get; init; } = FinanceResult<ExpenseBatchPreviewDto>.Success(
            new(Month, Month, [], 0, 0, 0, 0, [], [], false, new string('a', 64)));
        public CancellationToken Token { get; private set; }
        public Task<FinanceResult<ExpenseBatchPreviewDto>> PreviewAsync(ExpenseBatchPreviewRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Token = token; return Task.FromResult(Result);
        }
    }
    private sealed class PaymentStub : IExpenseBatchPaymentService
    {
        public FinanceResult<ExpenseBatchPaymentResult> Result { get; init; } = FinanceResult<ExpenseBatchPaymentResult>.Success(new(Guid.NewGuid(), [Guid.NewGuid()]));
        public Guid Actor { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Task<FinanceResult<ExpenseBatchPaymentResult>> PayAsync(ExpenseBatchPaymentRequest request, Guid actor, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++; Actor = actor; Token = token; return Task.FromResult(Result);
        }
    }
}
