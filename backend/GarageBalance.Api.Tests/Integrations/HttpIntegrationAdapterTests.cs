using System.Net;
using System.Text;
using System.Text.Json;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Infrastructure.Integrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class HttpIntegrationAdapterTests
{
    [Fact]
    public async Task OneCFreshAdapter_SendsBearerTokenAndMapsSuccessfulResponse()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.Accepted,
            """{"statusMessage":"Принято.","externalRunId":"run-42"}"""));
        var adapter = CreateOneCFreshAdapter(handler, enabled: true, "https://bridge.example.test/one-c/sync");

        var result = await adapter.StartAsync(
            new OneCFreshSyncAdapterRequest("private-token", "Месячный обмен", DateTimeOffset.Parse("2026-08-04T10:00:00Z")),
            CancellationToken.None);

        Assert.Equal("started", result.Status);
        Assert.Equal("run-42", result.ExternalRunId);
        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("private-token", handler.Request.Headers.Authorization.Parameter);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("Месячный обмен", body.RootElement.GetProperty("comment").GetString());
    }

    [Fact]
    public async Task OneCFreshAdapter_MapsConflictWithoutLeakingToken()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.Conflict,
            """{"statusMessage":"Обмен уже идет.","errorCode":"already_running"}"""));
        var adapter = CreateOneCFreshAdapter(handler, enabled: true, "https://bridge.example.test/one-c/sync");

        var result = await adapter.StartAsync(
            new OneCFreshSyncAdapterRequest("private-token", null, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal("conflict", result.Status);
        Assert.Equal("already_running", result.ErrorCode);
        Assert.DoesNotContain("private-token", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "https://bridge.example.test/sync", "adapter_disabled")]
    [InlineData(true, "http://public.example.test/sync", "adapter_not_configured")]
    public async Task OneCFreshAdapter_DoesNotSendWhenDisabledOrEndpointIsInsecure(
        bool enabled,
        string endpoint,
        string expectedStatus)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        var adapter = CreateOneCFreshAdapter(handler, enabled, endpoint);

        var result = await adapter.StartAsync(
            new OneCFreshSyncAdapterRequest("private-token", null, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(expectedStatus, adapter.Availability.Status);
        Assert.Equal("pending_adapter", result.Status);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task ReceiptAdapter_LoadsProtectedSettingsAndMapsSuccessfulResponse()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"statusMessage":"Напечатано.","deviceResponseCode":"0","externalReceiptId":"receipt-7"}"""));
        var secrets = new FakeSecretSettingsService(new Dictionary<string, string>
        {
            [IntegrationSecretCatalog.ReceiptPrintingDeviceConnection] = "private-device",
            [IntegrationSecretCatalog.ReceiptPrintingReceiptTemplate] = "private-template"
        });
        var adapter = CreateReceiptAdapter(handler, secrets, enabled: true, "http://localhost:9040/print");

        var result = await adapter.ProcessAsync(CreateReceiptRequest(), CancellationToken.None);

        Assert.Equal("printed", result.Status);
        Assert.Equal("0", result.DeviceResponseCode);
        Assert.Equal("receipt-7", result.ExternalReceiptId);
        Assert.Contains("private-device", handler.Body, StringComparison.Ordinal);
        Assert.Contains("private-template", handler.Body, StringComparison.Ordinal);
        Assert.Contains("DOC-42", handler.Body, StringComparison.Ordinal);
        Assert.Equal(1, secrets.BatchReadCount);
        Assert.Equal(0, secrets.SingleReadCount);
    }

    [Fact]
    public async Task ReceiptAdapter_DoesNotSendWithoutBothProtectedSettings()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        var secrets = new FakeSecretSettingsService(new Dictionary<string, string>
        {
            [IntegrationSecretCatalog.ReceiptPrintingDeviceConnection] = "private-device"
        });
        var adapter = CreateReceiptAdapter(handler, secrets, enabled: true, "https://bridge.example.test/print");

        var result = await adapter.ProcessAsync(CreateReceiptRequest(), CancellationToken.None);

        Assert.Equal("not_configured", result.Status);
        Assert.Equal("receipt_printing_not_configured", result.DeviceResponseCode);
        Assert.Null(handler.Request);
        Assert.Equal(1, secrets.BatchReadCount);
        Assert.Equal(0, secrets.SingleReadCount);
    }

    [Fact]
    public async Task ReceiptAdapter_MapsBridgeFailureWithoutReturningProtectedSettings()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.ServiceUnavailable,
            """{"statusMessage":"Касса занята.","deviceResponseCode":"busy"}"""));
        var secrets = new FakeSecretSettingsService(new Dictionary<string, string>
        {
            [IntegrationSecretCatalog.ReceiptPrintingDeviceConnection] = "private-device",
            [IntegrationSecretCatalog.ReceiptPrintingReceiptTemplate] = "private-template"
        });
        var adapter = CreateReceiptAdapter(handler, secrets, enabled: true, "https://bridge.example.test/print");

        var result = await adapter.ProcessAsync(CreateReceiptRequest(), CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Equal("busy", result.DeviceResponseCode);
        Assert.DoesNotContain("private-device", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-template", result.ToString(), StringComparison.Ordinal);
    }

    private static OneCFreshHttpSyncAdapter CreateOneCFreshAdapter(
        HttpMessageHandler handler,
        bool enabled,
        string endpoint) =>
        new(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) },
            Options.Create(new OneCFreshHttpSyncAdapterOptions { Enabled = enabled, Endpoint = endpoint, TimeoutSeconds = 2 }),
            NullLogger<OneCFreshHttpSyncAdapter>.Instance);

    private static ReceiptPrintingHttpAdapter CreateReceiptAdapter(
        HttpMessageHandler handler,
        IIntegrationSecretSettingsService secrets,
        bool enabled,
        string endpoint) =>
        new(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) },
            Options.Create(new ReceiptPrintingHttpAdapterOptions { Enabled = enabled, Endpoint = endpoint, TimeoutSeconds = 2 }),
            secrets,
            NullLogger<ReceiptPrintingHttpAdapter>.Instance);

    private static ReceiptPrintingAdapterRequest CreateReceiptRequest() =>
        new(
            ReceiptPrintingActions.Print,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "DOC-42",
            125.50m,
            new DateOnly(2026, 8, 4),
            new DateOnly(2026, 8, 1),
            "7",
            "Иванов И.И.",
            "Электроэнергия",
            null,
            false,
            null);

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }

    private sealed class FakeSecretSettingsService(IReadOnlyDictionary<string, string> settings) : IIntegrationSecretSettingsService
    {
        public int BatchReadCount { get; private set; }
        public int SingleReadCount { get; private set; }

        public Task<IntegrationSecretSettingResult<IntegrationSecretSettingDto>> UpsertSecretAsync(
            UpsertIntegrationSecretRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IntegrationSecretSettingResult<string>> GetSecretAsync(
            string provider,
            string settingKey,
            CancellationToken cancellationToken)
        {
            SingleReadCount++;
            return Task.FromResult(settings.TryGetValue(settingKey, out var value)
                ? IntegrationSecretSettingResult<string>.Success(value)
                : IntegrationSecretSettingResult<string>.Failure("not_found", "Not found."));
        }

        public Task<IntegrationSecretSettingResult<IReadOnlyDictionary<string, string>>> GetSecretsAsync(
            string provider,
            IReadOnlyCollection<string> settingKeys,
            CancellationToken cancellationToken)
        {
            BatchReadCount++;
            var values = settingKeys
                .Where(settings.ContainsKey)
                .ToDictionary(settingKey => settingKey, settingKey => settings[settingKey], StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(values.Count == settingKeys.Count
                ? IntegrationSecretSettingResult<IReadOnlyDictionary<string, string>>.Success(values)
                : IntegrationSecretSettingResult<IReadOnlyDictionary<string, string>>.Failure("not_found", "Not found."));
        }

        public Task<IReadOnlyList<IntegrationSecretSettingDto>> GetSettingsAsync(
            string? provider,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IntegrationSecretSettingDto>>([]);
    }
}
