using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Finance;

public sealed class FinancialJournalControllerTests
{
    [Fact]
    public async Task GetPage_PassesEveryFilterToQueryAndReturnsPage()
    {
        var query = new FakeQuery();
        var controller = new FinancialJournalController(query);
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);

        var response = await controller.GetPage(
            from,
            to,
            "accrual",
            "гараж 103",
            "active",
            "ПКО",
            25,
            50,
            CancellationToken.None);

        var page = Assert.IsType<FinancePagedResult<FinancialJournalEntryDto>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Empty(page.Items);
        Assert.Equal(new FinancialJournalRequest(from, to, "accrual", "гараж 103", "active", "ПКО", 25, 50), query.Request);
    }

    [Theory]
    [InlineData("unknown", null)]
    [InlineData(null, "deleted")]
    public async Task GetPage_RejectsUnsupportedEntityTypeOrStatus(string? entityType, string? status)
    {
        var query = new FakeQuery();
        var controller = new FinancialJournalController(query);

        var response = await controller.GetPage(
            null, null, entityType, null, status, null, 0, 25, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Null(query.Request);
    }

    [Fact]
    public async Task GetPage_RejectsReversedPeriod()
    {
        var query = new FakeQuery();
        var controller = new FinancialJournalController(query);

        var response = await controller.GetPage(
            new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1), null, null, null, null, 0, 25, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Null(query.Request);
    }

    private sealed class FakeQuery : IFinancialJournalQuery
    {
        public FinancialJournalRequest? Request { get; private set; }

        public Task<FinancePagedResult<FinancialJournalEntryDto>> GetPageAsync(
            FinancialJournalRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new FinancePagedResult<FinancialJournalEntryDto>([], 0, request.Offset ?? 0, request.Limit ?? 25));
        }
    }
}
