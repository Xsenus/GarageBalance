import { getLocalDateInputValue } from './formatters'
import { getReportQuickPeriodRange, reportQuickPeriodOptions } from './reportFilters'
import type { ReportQuickPeriodRange } from './reportFilters'

export function ReportPeriodQuickSelect({
  mode,
  valueFrom,
  valueTo,
  onSelect,
  referenceDate = getLocalDateInputValue(),
  className = '',
}: {
  mode: 'month' | 'date'
  valueFrom: string
  valueTo: string
  onSelect: (range: ReportQuickPeriodRange) => void
  referenceDate?: string
  className?: string
}) {
  return (
    <div className={`report-quick-periods ${className}`.trim()} role="group" aria-label="Быстрый выбор периода">
      {reportQuickPeriodOptions.map((option) => {
        const range = getReportQuickPeriodRange(option.key, referenceDate)
        const rangeFrom = mode === 'month' ? range.monthFrom : range.dateFrom
        const rangeTo = mode === 'month' ? range.monthTo : range.dateTo
        const selected = valueFrom === rangeFrom && valueTo === rangeTo

        return (
          <button
            key={option.key}
            className="secondary-button report-quick-periods__button"
            type="button"
            aria-pressed={selected}
            onClick={() => onSelect(range)}
          >
            {option.label}
          </button>
        )
      })}
    </div>
  )
}
