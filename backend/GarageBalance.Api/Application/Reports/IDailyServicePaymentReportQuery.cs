namespace GarageBalance.Api.Application.Reports;

public interface IDailyServicePaymentReportQuery
{
    Task<DailyServicePaymentReportData> GetRowsAsync(DateOnly throughDate, Guid? garageId, int offset, int limit, CancellationToken cancellationToken, bool forExport = false);
}

public sealed record DailyServicePaymentAmounts(decimal Electricity, decimal Water, decimal Trash,
    decimal OutdoorLighting, decimal Membership, decimal Target, decimal Other)
{
    public decimal Total => Electricity + Water + Trash + OutdoorLighting + Membership + Target + Other;
}

public sealed record DailyServicePaymentRow(DateOnly Date, Guid GarageId, string GarageNumber, DailyServicePaymentAmounts Amounts);
public sealed record DailyServicePaymentDayTotal(DateOnly Date, DailyServicePaymentAmounts Amounts);
public sealed record DailyServicePaymentReportData(IReadOnlyList<DailyServicePaymentRow> Rows,
    IReadOnlyList<DailyServicePaymentDayTotal> Days, DailyServicePaymentAmounts MonthTotal,
    int RowCount, bool HasOther, int Offset, int Limit);
