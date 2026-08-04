using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GarageBalance.Api.Application.Diagnostics;
using GarageBalance.Api.Application.Integrations;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Infrastructure.Integrations;

public sealed class ReceiptPrintingHttpAdapterOptions
{
    public const string SectionName = "Integrations:ReceiptPrintingAdapter";

    public bool Enabled { get; init; }

    public string Endpoint { get; init; } = string.Empty;

    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 30;
}

public sealed class ReceiptPrintingHttpAdapter(
    HttpClient httpClient,
    IOptions<ReceiptPrintingHttpAdapterOptions> options,
    IIntegrationSecretSettingsService secretSettingsService,
    ILogger<ReceiptPrintingHttpAdapter> logger) : IReceiptPrintingAdapter
{
    private readonly ReceiptPrintingHttpAdapterOptions _options = options.Value;

    public IntegrationAdapterAvailability Availability => ResolveAvailability(_options);

    public async Task<ReceiptPrintingAdapterResult> ProcessAsync(
        ReceiptPrintingAdapterRequest request,
        CancellationToken cancellationToken)
    {
        var availability = Availability;
        if (!availability.IsAvailable)
        {
            return ReceiptPrintingAdapterResult.Pending(availability.Message);
        }

        var deviceConnection = await secretSettingsService.GetSecretAsync(
            IntegrationSecretCatalog.ReceiptPrintingProvider,
            IntegrationSecretCatalog.ReceiptPrintingDeviceConnection,
            cancellationToken);
        var receiptTemplate = await secretSettingsService.GetSecretAsync(
            IntegrationSecretCatalog.ReceiptPrintingProvider,
            IntegrationSecretCatalog.ReceiptPrintingReceiptTemplate,
            cancellationToken);
        if (!deviceConnection.Succeeded || !receiptTemplate.Succeeded)
        {
            return ReceiptPrintingAdapterResult.Failed(
                "not_configured",
                "Для печати нужны защищенные настройки подключения к устройству и шаблона.",
                "receipt_printing_not_configured");
        }

        var payload = new ReceiptPrintingBridgeRequest(
            deviceConnection.Value!,
            receiptTemplate.Value!,
            request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = JsonContent.Create(payload)
        };

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responsePayload = await ReadPayloadAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Receipt printing bridge rejected action {Action} with status {StatusCode}.",
                    request.Action,
                    (int)response.StatusCode);
                return ReceiptPrintingAdapterResult.Failed(
                    "failed",
                    responsePayload?.StatusMessage ?? "Шлюз печати временно не принял задание.",
                    responsePayload?.DeviceResponseCode ?? "receipt_printing_unavailable");
            }

            return ReceiptPrintingAdapterResult.Printed(
                responsePayload?.StatusMessage ?? "Задание успешно выполнено шлюзом печати.",
                responsePayload?.DeviceResponseCode,
                responsePayload?.ExternalReceiptId);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                "Receipt printing bridge request failed for action {Action}. ExceptionType={ExceptionType}; Diagnostic={Diagnostic}",
                request.Action,
                exception.GetType().Name,
                DiagnosticLogSanitizer.SanitizeException(exception));
            return ReceiptPrintingAdapterResult.Failed(
                "failed",
                "Шлюз печати временно недоступен.",
                "receipt_printing_unavailable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ReceiptPrintingAdapterResult.Failed(
                "failed",
                "Шлюз печати не ответил вовремя.",
                "receipt_printing_timeout");
        }
        catch (JsonException)
        {
            return ReceiptPrintingAdapterResult.Failed(
                "failed",
                "Шлюз печати вернул некорректный ответ.",
                "receipt_printing_invalid_response");
        }
    }

    private static async Task<ReceiptPrintingBridgeResponse?> ReadPayloadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength == 0)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ReceiptPrintingBridgeResponse>(cancellationToken: cancellationToken);
    }

    private static IntegrationAdapterAvailability ResolveAvailability(ReceiptPrintingHttpAdapterOptions options)
    {
        if (!options.Enabled)
        {
            return IntegrationAdapterAvailability.Disabled("HTTP-адаптер печати выключен администратором.");
        }

        return OneCFreshHttpSyncAdapter.TryResolveSecureEndpoint(options.Endpoint, out _)
            ? IntegrationAdapterAvailability.Ready("HTTP-адаптер печати готов принимать задания.")
            : IntegrationAdapterAvailability.Invalid("Для адаптера печати нужен абсолютный HTTPS endpoint (HTTP допустим только для localhost).");
    }

    private sealed record ReceiptPrintingBridgeRequest(
        [property: JsonPropertyName("deviceConnection")] string DeviceConnection,
        [property: JsonPropertyName("receiptTemplate")] string ReceiptTemplate,
        [property: JsonPropertyName("job")] ReceiptPrintingAdapterRequest Job);

    private sealed record ReceiptPrintingBridgeResponse(
        [property: JsonPropertyName("statusMessage")] string? StatusMessage,
        [property: JsonPropertyName("deviceResponseCode")] string? DeviceResponseCode,
        [property: JsonPropertyName("externalReceiptId")] string? ExternalReceiptId);
}
