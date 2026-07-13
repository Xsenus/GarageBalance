using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class SuggestionsControllerTests
{
    [Fact]
    public async Task SuggestParties_ReturnsSuggestionsAndPassesLimit()
    {
        var service = new FakeDadataSuggestionService();
        var controller = new SuggestionsController(service);

        var result = await controller.SuggestParties("5400", 6, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<DadataPartySuggestionDto>>(ok.Value));
        Assert.Equal(("5400", 6), service.LastPartyRequest);
    }

    [Theory]
    [InlineData("", 8, "suggestion_query_invalid")]
    [InlineData("1", 8, "suggestion_query_invalid")]
    [InlineData("valid", 0, "suggestion_count_invalid")]
    [InlineData("valid", 11, "suggestion_count_invalid")]
    public async Task SuggestParties_ValidatesQueryAndCount(string query, int count, string expectedCode)
    {
        var controller = new SuggestionsController(new FakeDadataSuggestionService());

        var result = await controller.SuggestParties(query, count, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(expectedCode, Assert.IsType<ProblemDetails>(badRequest.Value).Title);
    }

    [Fact]
    public async Task SuggestAddresses_ReturnsServiceUnavailableWhenDadataIsNotConfigured()
    {
        var service = new FakeDadataSuggestionService
        {
            AddressResult = DadataSuggestionResult<IReadOnlyList<DadataAddressSuggestionDto>>.Failure("dadata_not_configured", "Настройте ключ.")
        };
        var controller = new SuggestionsController(service);

        var result = await controller.SuggestAddresses("Ленина", 8, CancellationToken.None);

        var unavailable = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("dadata_not_configured", Assert.IsType<ProblemDetails>(unavailable.Value).Title);
    }

    [Fact]
    public async Task SuggestAddresses_ReturnsBadGatewayForProviderFailure()
    {
        var service = new FakeDadataSuggestionService
        {
            AddressResult = DadataSuggestionResult<IReadOnlyList<DadataAddressSuggestionDto>>.Failure("dadata_unavailable", "Введите вручную.")
        };
        var controller = new SuggestionsController(service);

        var result = await controller.SuggestAddresses("Ленина", 8, CancellationToken.None);

        var badGateway = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, badGateway.StatusCode);
    }

    private sealed class FakeDadataSuggestionService : IDadataSuggestionService
    {
        public (string Query, int Count)? LastPartyRequest { get; private set; }
        public DadataSuggestionResult<IReadOnlyList<DadataAddressSuggestionDto>> AddressResult { get; init; } =
            DadataSuggestionResult<IReadOnlyList<DadataAddressSuggestionDto>>.Success([new("Ленина, 1", "630000, Ленина, 1", "fias", "630000")]);

        public Task<DadataSuggestionResult<IReadOnlyList<DadataPartySuggestionDto>>> SuggestPartiesAsync(string query, int count, CancellationToken cancellationToken)
        {
            LastPartyRequest = (query, count);
            return Task.FromResult(DadataSuggestionResult<IReadOnlyList<DadataPartySuggestionDto>>.Success([new("ООО Ромашка", "ООО Ромашка", "5400", null, null, "Новосибирск")]));
        }

        public Task<DadataSuggestionResult<IReadOnlyList<DadataAddressSuggestionDto>>> SuggestAddressesAsync(string query, int count, CancellationToken cancellationToken) =>
            Task.FromResult(AddressResult);
    }
}
