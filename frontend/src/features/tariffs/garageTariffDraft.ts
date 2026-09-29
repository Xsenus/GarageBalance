import type { ElectricityTariffTierDto } from '../../services/dictionariesApi'
import type { GarageTariffAssignment, GarageTariffTerms } from '../../services/garageTariffAssignmentsApi'

export type GarageTariffDraft = {
  from: string; to: string; rate: string; comment: string; reason: string; tiered: boolean
  tiers: Array<{ id?: string; name: string; upper: string; rate: string }>
}
export function createGarageTariffDraft(date: string, rate: number, tiers: ElectricityTariffTierDto[], row?: GarageTariffAssignment): GarageTariffDraft {
  const source = row?.tiers ?? tiers
  return { from: row?.effectiveFrom ?? date, to: row?.effectiveTo ?? '', rate: String(row?.rate ?? rate),
    comment: row?.comment ?? '', reason: '', tiered: source.length >= 2,
    tiers: source.length >= 2 ? source.map((tier) => ({ id: row ? tier.id : undefined, name: tier.name,
      upper: tier.upperBound === null ? '' : String(tier.upperBound), rate: String(tier.rate) }))
      : [{ name: 'Ступень 1', upper: '', rate: String(rate) }, { name: 'Ступень 2', upper: '', rate: String(rate) }] }
}
function positiveDecimal(value: string, precision: number): number | null {
  const normalized = value.replace(/\s/g, '').replace(',', '.')
  if (!new RegExp(`^\\d+(?:\\.\\d{1,${precision}})?$`).test(normalized)) return null
  const number = Number(normalized)
  return number > 0 && number <= 999999999 ? number : null
}
function validDate(value: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const [year, month, day] = value.split('-').map(Number)
  const parsed = new Date(Date.UTC(year, month - 1, day))
  return year >= 2000 && year <= 9998 && parsed.getUTCFullYear() === year && parsed.getUTCMonth() === month - 1 && parsed.getUTCDate() === day
}
export function validateGarageTariffReason(reason: string, required: boolean): string | null {
  return (required && !reason.trim()) || reason.length > 1000 ? 'Укажите причину изменения не длиннее 1000 символов.' : null
}
export function validateGarageTariffDraft(draft: GarageTariffDraft, meter: boolean, requiredReason: boolean): { terms: GarageTariffTerms; error?: never } | { error: string; terms?: never } {
  if (!validDate(draft.from) || (draft.to && (!validDate(draft.to) || draft.to < draft.from))) return { error: 'Проверьте даты периода: окончание не может быть раньше начала.' }
  const rate = positiveDecimal(draft.rate, 4)
  if (rate === null) return { error: 'Укажите положительную ставку с точностью до 4 знаков после запятой.' }
  if (draft.comment.length > 2000) return { error: 'Комментарий не должен превышать 2000 символов.' }
  const reasonError = validateGarageTariffReason(draft.reason, requiredReason)
  if (reasonError) return { error: reasonError }
  const tiers: NonNullable<GarageTariffTerms['tiers']> = []
  if (draft.tiered) {
    if (!meter || draft.tiers.length < 2 || draft.tiers.length > 20) return { error: 'Для тарифа по счётчику укажите от 2 до 20 ступеней.' }
    let previous = 0
    const ids = new Set<string>()
    for (const [index, tier] of draft.tiers.entries()) {
      const upper = index === draft.tiers.length - 1 ? undefined : positiveDecimal(tier.upper, 3)
      const tierRate = positiveDecimal(tier.rate, 4)
      if (!tier.name.trim() || tier.name.trim().length > 120 || tierRate === null || upper === null || (upper !== undefined && upper <= previous)
        || (index === draft.tiers.length - 1 && tier.upper.trim()) || (tier.id && ids.has(tier.id))) return { error: 'Проверьте названия, ставки и возрастающие границы ступеней. Последняя ступень — без границы.' }
      if (tier.id) ids.add(tier.id)
      tiers.push({ id: tier.id, name: tier.name.trim(), upperBound: upper, rate: tierRate })
      previous = upper ?? previous
    }
  }
  return { terms: { effectiveFrom: draft.from, effectiveTo: draft.to || null, rate, tiers: tiers.length ? tiers : null,
    comment: draft.comment.trim() || null, reason: draft.reason.trim() || null } }
}
export function appendGarageTariffTier(draft: GarageTariffDraft): GarageTariffDraft {
  if (draft.tiers.length >= 20) return draft
  return { ...draft, tiers: [...draft.tiers.slice(0, -1), { name: `Ступень ${draft.tiers.length + 1}`, upper: '', rate: draft.tiers.at(-1)?.rate ?? draft.rate }, draft.tiers.at(-1)!] }
}
export function removeGarageTariffTier(draft: GarageTariffDraft, index: number): GarageTariffDraft {
  if (draft.tiers.length <= 2 || index < 0 || index >= draft.tiers.length) return draft
  const tiers = draft.tiers.filter((_, itemIndex) => itemIndex !== index)
  return { ...draft, tiers: tiers.map((tier, itemIndex) => itemIndex === tiers.length - 1 ? { ...tier, upper: '' } : tier) }
}
