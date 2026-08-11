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
const dictionaryCatalogEntries: CatalogCoverageEntry[] = [
  { apiRoute: 'owners', label: 'Владельцы', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'owners' },
  { apiRoute: 'garages', label: 'Гаражи', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'garages' },
  { apiRoute: 'supplier-groups', label: 'Группы поставщиков и персонала', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'supplierGroups' },
  { apiRoute: 'suppliers', label: 'Поставщики', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'suppliers' },
  { apiRoute: 'income-types', label: 'Виды поступлений', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'incomeTypes' },
  { apiRoute: 'expense-types', label: 'Статьи расходов', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'expenseTypes' },
  { apiRoute: 'measurement-units', label: 'Единицы измерения', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'measurementUnits' },
  { apiRoute: 'tariffs', label: 'Тарифы', kind: 'master-data', workspaceSection: 'dictionaries', workspaceLabel: 'Справочники', dictionarySection: 'tariffs' },
]

export { profileCatalogEntries } from './profileCatalogCoverage'
import { profileCatalogEntries } from './profileCatalogCoverage'

export const catalogCoverageEntries: CatalogCoverageEntry[] = [
  ...dictionaryCatalogEntries,
  ...profileCatalogEntries,
  { apiRoute: 'fee-campaigns', label: 'Объявленные сборы', kind: 'operation-register', workspaceSection: 'tariffsAndFees', workspaceLabel: 'Тарифы и сборы' },
]
