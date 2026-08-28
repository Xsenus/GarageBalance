import type { GarageDto } from '../../services/dictionariesApi'
import { formatFinanceGarageLabel } from '../../shared/financeWorkbench'

export function formatFinanceReference(
  referenceId: string | null | undefined,
  fallbackName: string | null | undefined,
  references: Array<{ id: string; name: string }>,
) {
  if (!referenceId) {
    return 'пусто'
  }

  return fallbackName || references.find((item) => item.id === referenceId)?.name || referenceId
}

export function formatFinanceGarageReference(
  garageId: string | null | undefined,
  fallbackGarageNumber: string | null | undefined,
  garages: GarageDto[],
) {
  if (!garageId) {
    return 'пусто'
  }

  return formatFinanceGarageLabel(fallbackGarageNumber || garages.find((item) => item.id === garageId)?.number || garageId)
}
