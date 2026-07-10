using GarageBalance.Api.Application.Import;

namespace GarageBalance.Api.Infrastructure.Import;

public sealed class DisabledAccessImportReader : IAccessImportReader
{
    public Task<AccessImportReaderStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new AccessImportReaderStatusDto(
            "disabled",
            "Reader Access",
            false,
            "not_configured",
            "Фактическое чтение Access не подключено. Для переноса нужен ACE-драйвер, Microsoft Access или предварительная конвертация старой базы.",
            ["ACE OLE DB driver", "Microsoft Access runtime или согласованный конвертер Access"],
            DateTimeOffset.UtcNow));
    }
}
