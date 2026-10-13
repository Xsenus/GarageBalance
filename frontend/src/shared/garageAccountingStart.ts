import type { UpsertGarageRequest } from '../services/dictionariesApi'

export const garageAccountingStartHelp = 'Первый месяц начислений и показаний гаража. Если месяц не выбран, используется рабочий месяц регистрации на сервере. Годовой взнос начисляется полностью даже при начале учёта в декабре. Дата подключения каждой услуги учитывается отдельно. Исправление пересчитывает неоплаченные начисления; оплаченная история сохраняется.'

export function validateGarageAccountingMonth(value: string | undefined) {
  if (value === undefined) return undefined // Compatibility with clients without this field.
  if (!/^(19\d{2}|[2-9]\d{3})-(0[1-9]|1[0-2])$/.test(value)) return 'Выберите месяц начала учёта не раньше января 1900 года.'
  return undefined
}

export function buildGarageAccountingStartRequest(value: string | undefined, original: string | undefined, reason?: string): Pick<UpsertGarageRequest, 'accountingStartMonth' | 'accountingStartReason'> {
  if (!value || value === original) return {}
  return { accountingStartMonth: `${value}-01`, ...(reason?.trim() ? { accountingStartReason: reason.trim() } : {}) }
}
