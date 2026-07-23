import type { AccountingTypeDto, SupplierDto } from '../../services/dictionariesApi'

export function getSupplierAccrualExpenseType(
  supplier: SupplierDto | undefined,
  expenseTypes: AccountingTypeDto[],
): AccountingTypeDto | undefined {
  const expenseTypeId = supplier?.chargeServiceExpenseTypeId
  if (!expenseTypeId) {
    return undefined
  }

  return expenseTypes.find((expenseType) => expenseType.id === expenseTypeId && !expenseType.isArchived)
}

export function getFirstLinkedSupplier(
  suppliers: SupplierDto[],
  expenseTypes: AccountingTypeDto[],
): SupplierDto | undefined {
  return suppliers.find((supplier) => getSupplierAccrualExpenseType(supplier, expenseTypes) !== undefined)
}
