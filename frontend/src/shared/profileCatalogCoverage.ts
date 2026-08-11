import type { CatalogCoverageEntry } from './catalogCoverage'

export const profileCatalogEntries: CatalogCoverageEntry[] = [
  { apiRoute: 'supplier-groups', label: 'Группы поставщиков', kind: 'master-data', workspaceSection: 'contractors', workspaceLabel: 'Контрагенты · Поставщики' },
  { apiRoute: 'suppliers', label: 'Поставщики', kind: 'master-data', workspaceSection: 'contractors', workspaceLabel: 'Контрагенты · Поставщики' },
  { apiRoute: 'supplier-contacts', label: 'Контакты поставщиков', kind: 'master-data', workspaceSection: 'contractors', workspaceLabel: 'Контрагенты · Поставщики' },
  { apiRoute: 'staff-departments', label: 'Отделы персонала', kind: 'master-data', workspaceSection: 'contractors', workspaceLabel: 'Контрагенты · Персонал' },
  { apiRoute: 'staff-members', label: 'Сотрудники', kind: 'master-data', workspaceSection: 'contractors', workspaceLabel: 'Контрагенты · Персонал' },
  { apiRoute: 'tariffs', label: 'Тарифы услуг', kind: 'master-data', workspaceSection: 'tariffsAndFees', workspaceLabel: 'Тарифы и сборы' },
  { apiRoute: 'charge-services', label: 'Услуги начислений', kind: 'master-data', workspaceSection: 'tariffsAndFees', workspaceLabel: 'Тарифы и сборы' },
  { apiRoute: 'irregular-payments', label: 'Разовые платежи', kind: 'master-data', workspaceSection: 'tariffsAndFees', workspaceLabel: 'Тарифы и сборы' },
]
