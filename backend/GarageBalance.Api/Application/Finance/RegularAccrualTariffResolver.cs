using GarageBalance.Api.Application.Common;

namespace GarageBalance.Api.Application.Finance;

public static class RegularAccrualTariffResolver
{
    // The individual schedule overrides only its own dates. Outside those dates
    // the general schedule, including changes and gaps, remains authoritative.
    public static IReadOnlyList<RegularAccrualSegmentDefinition> ApplyIndividualSegments(
        DateOnly accountingMonth,
        IReadOnlyList<RegularAccrualSegmentDefinition> generalSegments,
        IReadOnlyList<RegularAccrualSegmentDefinition> individualSegments)
    {
        if (individualSegments.Count == 0) return generalSegments;
        var from = MonthPeriod.Normalize(accountingMonth);
        var to = from.AddMonths(1).AddDays(-1);
        var individual = ClipAndValidate(individualSegments, from, to);
        if (individual.Count == 0) return generalSegments;
        var general = ClipAndValidate(generalSegments, from, to);
        var boundaries = new SortedSet<int> { from.DayNumber, to.DayNumber + 1 };
        foreach (var segment in general.Concat(individual))
        {
            boundaries.Add(segment.EffectiveFrom.DayNumber);
            boundaries.Add(segment.EffectiveTo.DayNumber + 1);
        }
        var points = boundaries.ToArray();
        var result = new List<RegularAccrualSegmentDefinition>();
        RegularAccrualSegmentDefinition? previousSource = null;
        for (var index = 0; index < points.Length - 1; index++)
        {
            var start = DateOnly.FromDayNumber(points[index]);
            var end = DateOnly.FromDayNumber(points[index + 1] - 1);
            var source = individual.FirstOrDefault(segment => segment.EffectiveFrom <= start && segment.EffectiveTo >= start)
                ?? general.FirstOrDefault(segment => segment.EffectiveFrom <= start && segment.EffectiveTo >= start);
            if (result.Count > 0 && ReferenceEquals(previousSource, source))
            {
                result[^1] = result[^1] with { EffectiveTo = end };
            }
            else
            {
                result.Add(source is null
                    ? new(start, end, null, 0m, general.FirstOrDefault()?.UnitName ?? individual[0].UnitName, [])
                    : source with { EffectiveFrom = start, EffectiveTo = end });
            }
            previousSource = source;
        }
        return result;
    }

    private static List<RegularAccrualSegmentDefinition> ClipAndValidate(
        IReadOnlyList<RegularAccrualSegmentDefinition> segments, DateOnly from, DateOnly to)
    {
        if (segments.Any(segment => segment.EffectiveFrom > segment.EffectiveTo))
            throw new ArgumentException("Дата окончания тарифного периода раньше даты начала.", nameof(segments));
        var clipped = segments.Where(segment => segment.EffectiveFrom <= to && segment.EffectiveTo >= from)
            .Select(segment => segment with
            {
                EffectiveFrom = segment.EffectiveFrom < from ? from : segment.EffectiveFrom,
                EffectiveTo = segment.EffectiveTo > to ? to : segment.EffectiveTo,
            }).OrderBy(segment => segment.EffectiveFrom).ToList();
        if (clipped.Zip(clipped.Skip(1), (left, right) => left.EffectiveTo >= right.EffectiveFrom).Any(overlap => overlap))
            throw new ArgumentException("Тарифные периоды пересекаются.", nameof(segments));
        return clipped;
    }
}
