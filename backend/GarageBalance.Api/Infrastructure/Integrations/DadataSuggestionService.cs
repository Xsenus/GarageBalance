using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using GarageBalance.Api.Application.Integrations;

namespace GarageBalance.Api.Infrastructure.Integrations;

public sealed class DadataSuggestionService(
    HttpClient httpClient,
    IConfiguration configuration,
    IIntegrationSecretSettingsService integrationSecretSettingsService,
    ILogger<DadataSuggestionService> logger) : IDadataSuggestionService
{
    private const string DefaultBaseUrl = "https://suggestions.dadata.ru/suggestions/api/4_1/rs/suggest/";

    public Task<DadataSuggestionResult<IReadOnlyList<DadataPartySuggestionDto>>> SuggestPartiesAsync(string query, int count, CancellationToken cancellationToken) =>
        SendAsync(
            "party",
            query,
            count,
            suggestion => new DadataPartySuggestionDto(
                suggestion.Value,
                suggestion.UnrestrictedValue,
                suggestion.Data?.Inn,
                suggestion.Data?.Kpp,
                suggestion.Data?.Ogrn,
                suggestion.Data?.Address?.UnrestrictedValue ?? suggestion.Data?.Address?.Value),
            cancellationToken);

    public Task<DadataSuggestionResult<IReadOnlyList<DadataAddressSuggestionDto>>> SuggestAddressesAsync(string query, int count, CancellationToken cancellationToken) =>
        SendAsync(
            "address",
            query,
            count,
            suggestion => new DadataAddressSuggestionDto(
                suggestion.Value,
                suggestion.UnrestrictedValue,
                suggestion.Data?.FiasId,
                suggestion.Data?.PostalCode),
            cancellationToken);

    private async Task<DadataSuggestionResult<IReadOnlyList<T>>> SendAsync<T>(
        string resource,
        string query,
        int count,
        Func<DadataSuggestion, T> map,
        CancellationToken cancellationToken)
    {
        var apiKey = await ResolveApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return DadataSuggestionResult<IReadOnlyList<T>>.Failure(
                "dadata_not_configured",
                "Подсказки DaData не настроены. Администратору нужно сохранить API-ключ интеграции.");
        }

        var baseUrl = configuration["Dadata:BaseUrl"]?.TrimEnd('/') ?? DefaultBaseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/{resource}")
        {
            Content = JsonContent.Create(new DadataRequest(query.Trim(), count))
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", apiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("DaData suggestion request failed with status {StatusCode} for resource {Resource}.", (int)response.StatusCode, resource);
                return DadataSuggestionResult<IReadOnlyList<T>>.Failure(
                    "dadata_unavailable",
                    "DaData временно не отвечает. Можно продолжить ввод вручную.");
            }

            var payload = await response.Content.ReadFromJsonAsync<DadataResponse>(cancellationToken: cancellationToken);
            var suggestions = payload?.Suggestions?.Select(map).ToList() ?? [];
            return DadataSuggestionResult<IReadOnlyList<T>>.Success(suggestions);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "DaData suggestion request failed for resource {Resource}.", resource);
            return DadataSuggestionResult<IReadOnlyList<T>>.Failure(
                "dadata_unavailable",
                "DaData временно недоступна. Можно продолжить ввод вручную.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "DaData suggestion request timed out for resource {Resource}.", resource);
            return DadataSuggestionResult<IReadOnlyList<T>>.Failure(
                "dadata_unavailable",
                "DaData не ответила вовремя. Можно продолжить ввод вручную.");
        }
    }

    private async Task<string?> ResolveApiKeyAsync(CancellationToken cancellationToken)
    {
        var configuredApiKey = configuration["Dadata:ApiKey"];
        if (!string.IsNullOrWhiteSpace(configuredApiKey))
        {
            return configuredApiKey.Trim();
        }

        var protectedSetting = await integrationSecretSettingsService.GetSecretAsync(
            IntegrationSecretCatalog.DadataProvider,
            IntegrationSecretCatalog.DadataApiKey,
            cancellationToken);
        return protectedSetting.Succeeded ? protectedSetting.Value : null;
    }

    private sealed record DadataRequest([property: JsonPropertyName("query")] string Query, [property: JsonPropertyName("count")] int Count);
    private sealed record DadataResponse([property: JsonPropertyName("suggestions")] IReadOnlyList<DadataSuggestion>? Suggestions);
    private sealed record DadataSuggestion(
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("unrestricted_value")] string? UnrestrictedValue,
        [property: JsonPropertyName("data")] DadataData? Data);
    private sealed record DadataData(
        [property: JsonPropertyName("inn")] string? Inn,
        [property: JsonPropertyName("kpp")] string? Kpp,
        [property: JsonPropertyName("ogrn")] string? Ogrn,
        [property: JsonPropertyName("fias_id")] string? FiasId,
        [property: JsonPropertyName("postal_code")] string? PostalCode,
        [property: JsonPropertyName("address")] DadataNestedAddress? Address);
    private sealed record DadataNestedAddress(
        [property: JsonPropertyName("value")] string? Value,
        [property: JsonPropertyName("unrestricted_value")] string? UnrestrictedValue);
}
