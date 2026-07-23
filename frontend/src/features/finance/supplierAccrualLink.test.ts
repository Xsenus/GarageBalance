// @vitest-environment node
import { describe, expect, it } from 'vitest'
import type { AccountingTypeDto, SupplierDto } from '../../services/dictionariesApi'
import { getFirstLinkedSupplier, getSupplierAccrualExpenseType } from './supplierAccrualLink'

const electricity: AccountingTypeDto = {
  id: 'expense-electricity',
  name: 'Электроэнергия',
  code: 'electricity',
  isSystem: true,
  isArchived: false,
}

function createSupplier(overrides: Partial<SupplierDto> = {}): SupplierDto {
  return {
    id: 'supplier',
    name: 'Поставщик',
    groupId: 'group',
    groupName: 'Коммунальные услуги',
    inn: null,
    legalAddress: null,
    contactPerson: null,
    phone: null,
    email: null,
    startingBalance: 0,
    debt: 0,
    chargeServiceSettingId: 'service-electricity',
    chargeServiceSettingName: 'Электроэнергия',
    chargeServiceExpenseTypeId: electricity.id,
    comment: null,
    isArchived: false,
    ...overrides,
  }
}

describe('supplier accrual service link', () => {
  it('returns only the active expense type explicitly linked through the supplier service', () => {
    expect(getSupplierAccrualExpenseType(createSupplier(), [electricity])).toEqual(electricity)
    expect(getSupplierAccrualExpenseType(createSupplier({ chargeServiceExpenseTypeId: null }), [electricity])).toBeUndefined()
    expect(getSupplierAccrualExpenseType(createSupplier(), [{ ...electricity, isArchived: true }])).toBeUndefined()
    expect(getSupplierAccrualExpenseType(createSupplier({ chargeServiceExpenseTypeId: 'missing' }), [electricity])).toBeUndefined()
    expect(getSupplierAccrualExpenseType(undefined, [electricity])).toBeUndefined()
  })

  it('chooses the first supplier with a complete active service link', () => {
    const unlinked = createSupplier({ id: 'unlinked', chargeServiceExpenseTypeId: null })
    const linked = createSupplier({ id: 'linked' })
    expect(getFirstLinkedSupplier([unlinked, linked], [electricity])).toEqual(linked)
    expect(getFirstLinkedSupplier([unlinked], [electricity])).toBeUndefined()
  })
})
