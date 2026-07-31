export type CatalogWorkspaceSection = 'dictionaries' | 'contractors' | 'tariffsAndFees'

export type CatalogCoverageEntry = {
  apiRoute: string
  label: string
  kind: 'master-data' | 'operation-register'
  workspaceSection: CatalogWorkspaceSection
  workspaceLabel: string
  dictionarySection?: string
}

/**
 * Authoritative map for every resource family exposed by DictionariesController.
 * Profile-owned catalogs stay in their business workspace instead of duplicating
 * editing forms in the generic dictionary table.
 */
export const catalogCoverageEntries: CatalogCoverageEntry[] = [
  { apiRoute: 'owners', label: 'Владельцы', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'owners' },
  { apiRoute: 'garages', label: 'Гаражи', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'garages' },
  { apiRoute: 'supplier-groups', label: 'Группы поставщиков и персонала', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'supplierGroups' },
  { apiRoute: 'suppliers', label: 'Поставщики', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'suppliers' },
  { apiRoute: 'supplier-contacts', label: 'Контакты поставщиков', kind: 'master-data', workspaceSection: 'contractors', workspaceLabel: 'Контрагенты · Поставщики' },
  { apiRoute: 'staff-departments', label: 'Отделы персонала', kind: 'master-data', workspaceSection: 'contractors', workspaceLabel: 'Контрагенты · Персонал' },
  { apiRoute: 'staff-members', label: 'Сотрудники', kind: 'master-data', workspaceSection: 'contractors', workspaceLabel: 'Контрагенты · Персонал' },
  { apiRoute: 'income-types', label: 'Виды поступлений', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'incomeTypes' },
  { apiRoute: 'expense-types', label: 'Статьи расходов', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'expenseTypes' },
  { apiRoute: 'tariffs', label: 'Тарифы', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'tariffs' },
  { apiRoute: 'charge-services', label: 'Услуги начислений', kind: 'master-data', workspaceSection: 'tariffsAndFees', workspaceLabel: 'Тарифы и сборы' },
  { apiRoute: 'irregular-payments', label: 'Разовые платежи', kind: 'master-data', workspaceSection: 'tariffsAndFees', workspaceLabel: 'Тарифы и сборы' },
  { apiRoute: 'fee-campaigns', label: 'Объявленные сборы', kind: 'operation-register', workspaceSection: 'tariffsAndFees', workspaceLabel: 'Тарифы и сборы' },
]

export const profileCatalogEntries = catalogCoverageEntries.filter(
  (entry) => entry.kind === 'master-data' && entry.workspaceSection !== 'dictionaries',
)
