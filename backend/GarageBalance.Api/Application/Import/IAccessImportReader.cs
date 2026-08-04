namespace GarageBalance.Api.Application.Import;

public interface IAccessImportReader
{
    Task<AccessImportReaderStatusDto> GetStatusAsync(CancellationToken cancellationToken);

    Task<AccessImportReaderInspectionDto> InspectAsync(
        ReadOnlyMemory<byte> content,
        string fileExtension,
        CancellationToken cancellationToken) =>
        Task.FromResult(AccessImportReaderInspectionDto.Unavailable(
            "not_supported",
            "Reader Access не поддерживает проверку структуры файла."));
}
