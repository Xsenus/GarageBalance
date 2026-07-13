namespace GarageBalance.Api.Application.Integrations;

public sealed record DadataPartySuggestionDto(
    string Value,
    string? UnrestrictedValue,
    string? Inn,
    string? Kpp,
    string? Ogrn,
    string? LegalAddress);

public sealed record DadataAddressSuggestionDto(
    string Value,
    string? UnrestrictedValue,
    string? FiasId,
    string? PostalCode);

public sealed record DadataSuggestionResult<T>(bool Succeeded, T? Value, string? ErrorCode, string? ErrorMessage)
{
    public static DadataSuggestionResult<T> Success(T value) => new(true, value, null, null);
    public static DadataSuggestionResult<T> Failure(string errorCode, string errorMessage) => new(false, default, errorCode, errorMessage);
}

public interface IDadataSuggestionService
{
    Task<DadataSuggestionResult<IReadOnlyList<DadataPartySuggestionDto>>> SuggestPartiesAsync(string query, int count, CancellationToken cancellationToken);
    Task<DadataSuggestionResult<IReadOnlyList<DadataAddressSuggestionDto>>> SuggestAddressesAsync(string query, int count, CancellationToken cancellationToken);
}
