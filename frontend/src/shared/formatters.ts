import type { TariffDto } from '../services/dictionariesApi'
import type { MissingMeterReadingDto, PaymentAllocationDto } from '../services/financeApi'
import type { AccessImportCheckDto, AccessImportCreatedRecordDto, AccessImportReaderStatusDto, AccessImportRunDto, AccessImportRunLogEntryDto } from '../services/importApi'

export function formatMoney(value: number): string {
  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2, minimumFractionDigits: 2 }).format(value)
}

export function formatTariffRateSummary(tariff: TariffDto): string {
  const hasElectricityTiers = tariff.electricityFirstThreshold !== null
    && tariff.electricitySecondThreshold !== null
    && tariff.electricityFirstRate !== null
    && tariff.electricitySecondRate !== null
    && tariff.electricityThirdRate !== null

  if (!hasElectricityTiers) {
    return formatMoney(tariff.rate)
  }

  return `до ${formatMoney(tariff.electricityFirstThreshold!)} кВт: ${formatMoney(tariff.electricityFirstRate!)}, до ${formatMoney(tariff.electricitySecondThreshold!)} кВт: ${formatMoney(tariff.electricitySecondRate!)}, выше: ${formatMoney(tariff.electricityThirdRate!)}`
}

export function formatDebtLabel(value: number): string {
  return value < 0 ? 'Переплата' : 'Задолженность'
}

export function formatDebtAmount(value: number): string {
  return formatMoney(Math.abs(value))
}

export function getDebtClassName(value: number): string {
  return value < 0 ? 'money-overpayment' : 'money-accrual'
}

export function formatPaymentAllocations(allocations: PaymentAllocationDto[]): string {
  const visible = allocations.slice(0, 3).map((allocation) => {
    const label = allocation.accountingMonth ? formatMonth(allocation.accountingMonth) : allocation.label
    return `${label} ${formatMoney(allocation.paidAmount)}`
  })
  const hiddenCount = allocations.length - visible.length
  return hiddenCount > 0 ? `${visible.join(', ')} и еще ${hiddenCount}` : visible.join(', ')
}

export function formatMonthInputValue(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  return `${year}-${month}`
}

export function getLocalDateInputValue(date = new Date()): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

export function formatDateOnly(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (!match) {
    return value
  }

  return `${match[3]}.${match[2]}.${match[1]}`
}

export function formatMonth(value: string): string {
  const match = /^(\d{4})-(\d{2})(?:-\d{2})?$/.exec(value)
  if (!match) {
    return value
  }

  return `${match[2]}.${match[1]}`
}

export function formatAccrualSource(source: string): string {
  if (source === 'manual') {
    return 'Ручное'
  }

  if (source === 'regular') {
    return 'Авто'
  }

  return source
}

export function formatMissingMeterReadings(items: MissingMeterReadingDto[]): string {
  const visibleItems = items.slice(0, 6)
  const suffix = items.length > visibleItems.length ? ` и еще ${items.length - visibleItems.length}` : ''
  return `${visibleItems.map((item) => `Гараж ${item.garageNumber} - ${item.meterKind === 'water' ? 'Вода' : 'Электричество'}`).join(', ')}${suffix}`
}

export function formatImportRunStatus(status: AccessImportRunDto['status']): string {
  if (status === 'completed') {
    return 'Завершен'
  }

  if (status === 'rollback_requested') {
    return 'Rollback запрошен'
  }

  if (status === 'import_requested') {
    return 'Импорт запрошен'
  }

  if (status === 'import_request_cancelled') {
    return 'Заявка отменена'
  }

  return 'Заблокирован'
}

export function formatImportCheckStatus(status: AccessImportCheckDto['status']): string {
  if (status === 'passed') {
    return 'Пройдено'
  }

  if (status === 'warning') {
    return 'Предупреждение'
  }

  return 'Ошибка'
}

export function formatImportLogLevel(level: AccessImportRunLogEntryDto['level']): string {
  if (level === 'warning') {
    return 'Предупреждение'
  }

  if (level === 'error') {
    return 'Ошибка'
  }

  return 'Инфо'
}

export function formatImportCreatedRecordRollbackStatus(status: AccessImportCreatedRecordDto['rollbackStatus']): string {
  if (status === 'rollback_requested') {
    return 'Rollback запрошен'
  }

  if (status === 'rolled_back') {
    return 'Откат выполнен'
  }

  if (status === 'rollback_failed') {
    return 'Ошибка отката'
  }

  return 'Ожидает rollback'
}

export function formatImportReaderStatus(status: AccessImportReaderStatusDto['status']): string {
  if (status === 'ready') {
    return 'Готов'
  }

  if (status === 'unavailable') {
    return 'Недоступен'
  }

  if (status === 'error') {
    return 'Ошибка'
  }

  return 'Не настроен'
}

export function formatImportRunCheckSummary(run: AccessImportRunDto): string {
  return `${run.passedChecks}/${run.totalChecks} · ${formatCount(run.warningCount, 'предупреждение', 'предупреждения', 'предупреждений')} · ${formatCount(run.errorCount, 'ошибка', 'ошибки', 'ошибок')}`
}

export function formatCount(value: number, one: string, few: string, many: string): string {
  const absoluteValue = Math.abs(value)
  const lastTwoDigits = absoluteValue % 100
  const lastDigit = absoluteValue % 10
  const form = lastTwoDigits >= 11 && lastTwoDigits <= 14 ? many : lastDigit === 1 ? one : lastDigit >= 2 && lastDigit <= 4 ? few : many
  return `${value} ${form}`
}

export function formatNullableNumber(value: number | null): string {
  if (value === null) {
    return 'Не указан'
  }

  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 3 }).format(value)
}

export function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))
}

export function getCurrentMonthInputValue(dateValue = getLocalDateInputValue()): string {
  return dateValue.slice(0, 7)
}

export function getPreviousMonthInputValue(monthValue: string): string {
  const [yearText, monthText] = monthValue.split('-')
  const date = new Date(Number(yearText), Number(monthText) - 2, 1)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`
}

export function formatOperationTime(value: string | null | undefined): string {
  if (!value) {
    return ''
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return ''
  }

  return date.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
}

export function formatReleaseDate(value: string): string {
  return new Intl.DateTimeFormat('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(new Date(value))
}
