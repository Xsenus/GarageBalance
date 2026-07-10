namespace GarageBalance.Api.Application.Import;

public interface IAccessImportReader
{
    Task<AccessImportReaderStatusDto> GetStatusAsync(CancellationToken cancellationToken);
}
