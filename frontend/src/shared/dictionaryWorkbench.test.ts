import { describe, expect, it } from 'vitest'
import { createEmptyOwnerGarageLinkForm, dictionarySectionGroups, dictionarySectionOptions, getDictionarySearchPlaceholder, supportsDictionarySearch } from './dictionaryWorkbench'

describe('dictionary workbench metadata', () => {
  it('keeps dictionary groups in the expected order', () => {
    expect(dictionarySectionGroups).toEqual([
      { key: 'counterparties', label: 'Контрагенты' },
      { key: 'operations', label: 'Операции' },
      { key: 'tariffs', label: 'Тарифы' },
    ])
  })

  it('keeps dictionary sections grouped with their write permission', () => {
    expect(dictionarySectionOptions).toEqual([
      { key: 'owners', label: 'Владельцы', group: 'counterparties', writePermission: 'dictionaries' },
      { key: 'garages', label: 'Гаражи', group: 'counterparties', writePermission: 'dictionaries' },
      { key: 'supplierGroups', label: 'Группы поставщиков и персонала', group: 'counterparties', writePermission: 'dictionaries' },
      { key: 'suppliers', label: 'Поставщики и персонал', group: 'counterparties', writePermission: 'dictionaries' },
      { key: 'incomeTypes', label: 'Виды поступлений', group: 'operations', writePermission: 'dictionaries' },
      { key: 'expenseTypes', label: 'Виды выплат', group: 'operations', writePermission: 'dictionaries' },
      { key: 'tariffs', label: 'Тарифы', group: 'tariffs', writePermission: 'tariffs' },
    ])
  })

  it('creates an empty owner garage link form with numeric defaults', () => {
    expect(createEmptyOwnerGarageLinkForm()).toEqual({
      existingGarageId: '',
      newGarageNumber: '',
      peopleCount: 1,
      floorCount: 1,
      startingBalance: 0,
      initialWaterMeterValue: '',
      initialElectricityMeterValue: '',
      comment: '',
    })
  })

  it('marks only server-searchable dictionary sections as searchable', () => {
    expect(Object.fromEntries(dictionarySectionOptions.map((section) => [section.key, supportsDictionarySearch(section.key)]))).toEqual({
      owners: true,
      garages: true,
      supplierGroups: false,
      suppliers: true,
      incomeTypes: false,
      expenseTypes: false,
      tariffs: true,
    })
  })

  it('returns search placeholders for every dictionary section', () => {
    expect(Object.fromEntries(dictionarySectionOptions.map((section) => [section.key, getDictionarySearchPlaceholder(section.key)]))).toEqual({
      owners: 'ФИО или телефон',
      garages: 'Номер гаража или ФИО владельца',
      supplierGroups: 'Поиск для этого справочника пока не применяется',
      suppliers: 'Название, ИНН или контакт',
      incomeTypes: 'Поиск для этого справочника пока не применяется',
      expenseTypes: 'Поиск для этого справочника пока не применяется',
      tariffs: 'Название или база расчета',
    })
  })
})
