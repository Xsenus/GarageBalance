using System.Net;
using System.Text;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class DadataSuggestionServiceTests
{
    [Fact]
    public async Task SuggestPartiesAsync_UsesProtectedApiKeyAndMapsOrganization()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"suggestions":[{"value":"ООО Ромашка","unrestricted_value":"ООО Ромашка","data":{"inn":"5400000000","kpp":"540001001","ogrn":"1000000000000","address":{"value":"г Новосибирск","unrestricted_value":"630000, г Новосибирск"}}}]}""", Encoding.UTF8, "application/json")
            };
        });
        var service = CreateService(handler, new FakeSecretSettingsService("protected-dadata-key"));

        var result = await service.SuggestPartiesAsync("5400", 5, CancellationToken.None);

        Assert.True(result.Succeeded);
        var suggestion = Assert.Single(result.Value!);
        Assert.Equal("5400000000", suggestion.Inn);
        Assert.Equal("630000, г Новосибирск", suggestion.LegalAddress);
        Assert.Equal("Token", capturedRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("protected-dadata-key", capturedRequest.Headers.Authorization.Parameter);
        Assert.Equal("https://suggestions.dadata.ru/suggestions/api/4_1/rs/suggest/party", capturedRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task SuggestAddressesAsync_MapsAddressAndAllowsConfiguredKey()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"suggestions":[{"value":"г Новосибирск, ул Ленина, д 1","unrestricted_value":"630000, г Новосибирск, ул Ленина, д 1","data":{"fias_id":"fias-1","postal_code":"630000"}}]}""", Encoding.UTF8, "application/json")
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dadata:ApiKey"] = "configuration-key"
        }).Build();
        var service = CreateService(handler, new FakeSecretSettingsService(null), configuration);

        var result = await service.SuggestAddressesAsync("Ленина 1", 8, CancellationToken.None);

        Assert.True(result.Succeeded);
        var suggestion = Assert.Single(result.Value!);
        Assert.Equal("fias-1", suggestion.FiasId);
        Assert.Equal("630000", suggestion.PostalCode);
    }

    [Fact]
    public async Task SuggestPartiesAsync_ReturnsSafeFailureWhenKeyIsMissing()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("HTTP should not be called"));
        var service = CreateService(handler, new FakeSecretSettingsService(null));

        var result = await service.SuggestPartiesAsync("5400", 8, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("dadata_not_configured", result.ErrorCode);
        Assert.Contains("Администратору", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuggestAddressesAsync_ReturnsManualInputMessageWhenProviderFails()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = CreateService(handler, new FakeSecretSettingsService("key"));

        var result = await service.SuggestAddressesAsync("Ленина", 8, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("dadata_unavailable", result.ErrorCode);
        Assert.Contains("вручную", result.ErrorMessage, StringComparison.Ordinal);
    }

    private static DadataSuggestionService CreateService(HttpMessageHandler handler, IIntegrationSecretSettingsService secrets, IConfiguration? configuration = null) =>
        new(new HttpClient(handler), configuration ?? new ConfigurationBuilder().Build(), secrets, NullLogger<DadataSuggestionService>.Instance);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class FakeSecretSettingsService(string? value) : IIntegrationSecretSettingsService
    {
        public Task<IntegrationSecretSettingResult<string>> GetSecretAsync(string provider, string settingKey, CancellationToken cancellationToken) =>
            Task.FromResult(value is null
                ? IntegrationSecretSettingResult<string>.Failure("not_found", "Not found")
                : IntegrationSecretSettingResult<string>.Success(value));

        public Task<IReadOnlyList<IntegrationSecretSettingDto>> GetSettingsAsync(string? provider, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IntegrationSecretSettingDto>>([]);

        public Task<IntegrationSecretSettingResult<IntegrationSecretSettingDto>> UpsertSecretAsync(UpsertIntegrationSecretRequest request, Guid? actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
