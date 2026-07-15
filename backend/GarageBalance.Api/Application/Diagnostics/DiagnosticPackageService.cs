using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;

namespace GarageBalance.Api.Application.Diagnostics;

public interface IDiagnosticPackageService
{
    DiagnosticLogStatusDto GetStatus();
    Task<DiagnosticPackage?> CreatePackageAsync(Guid? actorUserId, CancellationToken cancellationToken);
}

public sealed class DiagnosticPackageService(
    IDiagnosticLogStore logStore,
    IAuditEventWriter auditEventWriter,
    IApplicationUnitOfWork unitOfWork) : IDiagnosticPackageService
{
    public DiagnosticLogStatusDto GetStatus() => logStore.GetStatus();

    public async Task<DiagnosticPackage?> CreatePackageAsync(Guid? actorUserId, CancellationToken cancellationToken)
    {
        var package = await logStore.CreatePackageAsync(cancellationToken);
        if (package is null)
        {
            return null;
        }

        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "diagnostics.package_created",
            "diagnostics",
            package.FileName,
            Summary: "Администратор сформировал диагностический пакет ошибок.",
            Section: "settings",
            ActionKind: "export",
            EntityDisplayName: package.FileName,
            Metadata: new Dictionary<string, object?>
            {
                ["sizeBytes"] = package.Content.LongLength
            }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return package;
    }
}
