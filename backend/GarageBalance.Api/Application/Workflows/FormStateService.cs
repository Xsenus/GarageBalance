using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Workflows;

namespace GarageBalance.Api.Application.Workflows;

public sealed class FormStateService(
    IFormStateRepository repository,
    IAuditEventWriter auditEventWriter) : IFormStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxPayloadLength = 1_000_000;

    public async Task<FormStateDto?> GetStateAsync(string scope, CancellationToken cancellationToken)
    {
        var normalizedScope = NormalizeScope(scope);
        if (normalizedScope is null)
        {
            return null;
        }

        var state = await repository.GetAsync(normalizedScope, cancellationToken);

        return state is null ? null : ToDto(state);
    }

    public async Task<FormStateResult<FormStateDto>> UpsertStateAsync(
        string scope,
        UpsertFormStateRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var normalizedScope = NormalizeScope(scope);
        if (normalizedScope is null)
        {
            return FormStateResult<FormStateDto>.Failure("form_state_scope_invalid", "Укажите корректный ключ формы.");
        }

        var payloadJson = request.Payload.GetRawText();
        if (payloadJson.Length > MaxPayloadLength)
        {
            return FormStateResult<FormStateDto>.Failure("form_state_payload_too_large", "Состояние формы слишком большое для сохранения.");
        }

        var now = DateTimeOffset.UtcNow;
        var state = await repository.FindForUpdateAsync(normalizedScope, cancellationToken);
        var isCreated = state is null;
        var oldPayload = state?.PayloadJson;
        if (isCreated)
        {
            state = new FormState
            {
                Scope = normalizedScope,
                PayloadJson = payloadJson,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                UpdatedByUserId = actorUserId
            };
            repository.Add(state);
        }
        else
        {
            if (state is null)
            {
                throw new InvalidOperationException("Form state update branch requires an existing state.");
            }

            state.PayloadJson = payloadJson;
            state.UpdatedAtUtc = now;
            state.UpdatedByUserId = actorUserId;
        }

        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            isCreated ? "workflows.form_state_created" : "workflows.form_state_updated",
            "form_state",
            normalizedScope,
            request.Summary ?? $"Сохранено состояние формы {normalizedScope}.",
            Section: "workflows",
            ActionKind: isCreated ? "create" : "update",
            EntityDisplayName: normalizedScope,
            OldValues: oldPayload is null ? null : new Dictionary<string, object?> { ["payload"] = oldPayload },
            NewValues: oldPayload is null ? null : new Dictionary<string, object?> { ["payload"] = payloadJson },
            FieldLabels: new Dictionary<string, string> { ["payload"] = "Состояние формы" }));

        await repository.SaveChangesAsync(cancellationToken);
        return FormStateResult<FormStateDto>.Success(ToDto(state));
    }

    private static string? NormalizeScope(string? scope)
    {
        var normalized = scope?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 120)
        {
            return null;
        }

        return normalized;
    }

    private static FormStateDto ToDto(FormState state)
    {
        using var document = JsonDocument.Parse(state.PayloadJson);
        return new FormStateDto(
            state.Scope,
            document.RootElement.Clone(),
            state.UpdatedAtUtc,
            state.UpdatedByUserId);
    }
}
