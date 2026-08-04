using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GarageBalance.Api.Application.Diagnostics;
using GarageBalance.Api.Application.Integrations;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Infrastructure.Integrations;

public sealed class OneCFreshHttpSyncAdapterOptions
{
    public const string SectionName = "Integrations:OneCFreshAdapter";

    public bool Enabled { get; init; }

    public string Endpoint { get; init; } = string.Empty;

    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 30;
}

public sealed class OneCFreshHttpSyncAdapter(
    HttpClient httpClient,
    IOptions<OneCFreshHttpSyncAdapterOptions> options,
    ILogger<OneCFreshHttpSyncAdapter> logger) : IOneCFreshSyncAdapter
{
    private readonly OneCFreshHttpSyncAdapterOptions _options = options.Value;

    public IntegrationAdapterAvailability Availability => ResolveAvailability(_options);

    public async Task<OneCFreshSyncAdapterResult> StartAsync(
        OneCFreshSyncAdapterRequest request,
        CancellationToken cancellationToken)
    {
        var availability = Availability;
        if (!availability.IsAvailable)
        {
            return OneCFreshSyncAdapterResult.Pending(availability.Message);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = JsonContent.Create(new OneCFreshBridgeRequest(request.Comment, request.RequestedAtUtc, request.IsRetry))
        };
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.RefreshToken);

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var payload = await ReadPayloadAsync(response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return OneCFreshSyncAdapterResult.Conflict(
                    payload?.StatusMessage ?? "1C Fresh уже обрабатывает синхронизацию.",
                    payload?.ErrorCode ?? "one_c_fresh_conflict");
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("1C Fresh bridge rejected synchronization with status {StatusCode}.", (int)response.StatusCode);
                return OneCFreshSyncAdapterResult.Failed(
                    "failed",
                    payload?.StatusMessage ?? "Шлюз 1C Fresh временно не принял синхронизацию.",
                    payload?.ErrorCode ?? "one_c_fresh_unavailable");
            }

            return OneCFreshSyncAdapterResult.Started(
                payload?.StatusMessage ?? "Синхронизация передана в 1C Fresh.",
                payload?.ExternalRunId);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                "1C Fresh bridge request failed. ExceptionType={ExceptionType}; Diagnostic={Diagnostic}",
                exception.GetType().Name,
                DiagnosticLogSanitizer.SanitizeException(exception));
            return OneCFreshSyncAdapterResult.Failed(
                "failed",
                "Шлюз 1C Fresh временно недоступен.",
                "one_c_fresh_unavailable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OneCFreshSyncAdapterResult.Failed(
                "failed",
                "Шлюз 1C Fresh не ответил вовремя.",
                "one_c_fresh_timeout");
        }
        catch (JsonException)
        {
            return OneCFreshSyncAdapterResult.Failed(
                "failed",
                "Шлюз 1C Fresh вернул некорректный ответ.",
                "one_c_fresh_invalid_response");
        }
    }

    private static async Task<OneCFreshBridgeResponse?> ReadPayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength == 0)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<OneCFreshBridgeResponse>(cancellationToken: cancellationToken);
    }

    private static IntegrationAdapterAvailability ResolveAvailability(OneCFreshHttpSyncAdapterOptions options)
    {
        if (!options.Enabled)
        {
            return IntegrationAdapterAvailability.Disabled("HTTP-адаптер 1C Fresh выключен администратором.");
        }

        return TryResolveSecureEndpoint(options.Endpoint, out _)
            ? IntegrationAdapterAvailability.Ready("HTTP-адаптер 1C Fresh готов принимать задания синхронизации.")
            : IntegrationAdapterAvailability.Invalid("Для адаптера 1C Fresh нужен абсолютный HTTPS endpoint (HTTP допустим только для localhost).");
    }

    internal static bool TryResolveSecureEndpoint(string endpoint, out Uri? uri)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps ||
               (uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || string.Equals(uri.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed record OneCFreshBridgeRequest(
        [property: JsonPropertyName("comment")] string? Comment,
        [property: JsonPropertyName("requestedAtUtc")] DateTimeOffset RequestedAtUtc,
        [property: JsonPropertyName("isRetry")] bool IsRetry);

    private sealed record OneCFreshBridgeResponse(
        [property: JsonPropertyName("statusMessage")] string? StatusMessage,
        [property: JsonPropertyName("externalRunId")] string? ExternalRunId,
        [property: JsonPropertyName("errorCode")] string? ErrorCode);
}
