using GarageBalance.Api.Application.Reports;

namespace GarageBalance.Api.Tests.Reports;

public sealed class ReportExportBuilderCancellationTests
{
    [Fact]
    public void XlsxBuilderHonorsCancellationBeforeAllocatingWorkbook()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            XlsxWorkbookBuilder.Build(
                [new XlsxSheet("Отчёт", ["Колонка"], [[XlsxCell.Text("Значение")]])],
                cancellation.Token));
    }

    [Fact]
    public void PdfBuilderHonorsCancellationBeforeAllocatingDocument()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            PdfReportDocumentBuilder.Build("Отчёт", ["Строка"], cancellation.Token));
    }
}
