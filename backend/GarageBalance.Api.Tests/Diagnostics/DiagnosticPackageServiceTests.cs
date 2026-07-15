using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Diagnostics;
using GarageBalance.Api.Domain.Audit;

namespace GarageBalance.Api.Tests.Diagnostics;

public sealed class DiagnosticPackageServiceTests
{
    [Fact]
    public async Task CreatePackage_AuditsSuccessfulExportWithoutIncludingContent()
    {
        var store = new FakeStore(new DiagnosticPackage("diagnostics.zip", [1, 2, 3]));
        var audit = new CaptureAuditWriter();
        var unitOfWork = new CaptureUnitOfWork();
        var service = new DiagnosticPackageService(store, audit, unitOfWork);

        var result = await service.CreatePackageAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), CancellationToken.None);

        Assert.NotNull(result);
        var request = Assert.Single(audit.Requests);
        Assert.Equal("diagnostics.package_created", request.Action);
        Assert.Equal("diagnostics.zip", request.EntityDisplayName);
        Assert.DoesNotContain("1, 2, 3", request.Summary, StringComparison.Ordinal);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task CreatePackage_DoesNotAuditWhenLoggingIsDisabled()
    {
        var audit = new CaptureAuditWriter();
        var unitOfWork = new CaptureUnitOfWork();
        var service = new DiagnosticPackageService(new FakeStore(null), audit, unitOfWork);

        Assert.Null(await service.CreatePackageAsync(null, CancellationToken.None));
        Assert.Empty(audit.Requests);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    private sealed class FakeStore(DiagnosticPackage? package) : IDiagnosticLogStore
    {
        public DiagnosticLogStatusDto GetStatus() => new(package is not null, 14, 7, 20, 0, 0, null, null);
        public Task<DiagnosticPackage?> CreatePackageAsync(CancellationToken cancellationToken) => Task.FromResult(package);
    }

    private sealed class CaptureAuditWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Requests { get; } = [];
        public AuditEvent? Add(AuditEventWriteRequest request)
        {
            Requests.Add(request);
            return null;
        }
    }

    private sealed class CaptureUnitOfWork : IApplicationUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
