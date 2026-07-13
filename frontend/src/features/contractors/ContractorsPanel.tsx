import { useEffect, useMemo, useRef, useState } from 'react'
import type { CSSProperties, FormEvent, MouseEvent, RefObject } from 'react'
import { FileText, Pencil, RotateCcw, Save, Search, Trash2, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { DictionaryClient, GarageDto, OwnerDto, StaffDepartmentDto, StaffMemberDto, SupplierContactDto, SupplierDto, SupplierGroupDto, UpsertGarageRequest, UpsertOwnerRequest, UpsertStaffMemberRequest, UpsertSupplierContactRequest, UpsertSupplierRequest } from '../../services/dictionariesApi'
import type { FinanceClient, GarageBalanceHistoryDto } from '../../services/financeApi'
import type { FormStateClient } from '../../services/formStatesApi'
import type { DadataAddressSuggestionDto, DadataPartySuggestionDto, IntegrationClient } from '../../services/integrationsApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { FormError } from '../../shared/formFeedback'
import { FormField } from '../../shared/FormField'
import { formatDateOnly, formatDebtAmount, formatDebtLabel, formatMoney, formatMonth, getDebtClassName } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { Pagination } from '../../shared/PageNavigator'
import { createClientPage, createFallbackPage, getPageVisibleRange, pageSizeOptions } from '../../shared/pagination'
import { TablePagination } from '../../shared/TablePagination'
import { createDefaultGarageBalanceHistoryFilters } from '../../shared/reportFilters'
import { formatPrototypeChangeValue } from '../../shared/prototypeEditing'
import type { AuditPanelPreset, ContractorOpenTarget } from '../../shared/workspaceNavigation'

const contractorsFormStateScope = 'contractors-prototype'

function normalizeContractorTargetText(value?: string | null) {
  return (value ?? '').trim().toLocaleLowerCase('ru-RU')
}

function extractGarageNumberFromTarget(target: ContractorOpenTarget) {
  if (target.garageNumber?.trim()) {
    return target.garageNumber.trim()
  }

  return target.displayName?.match(/\d+/)?.[0] ?? null
}

function findGarageForOpenTarget(garages: ContractorGarageRow[], target: ContractorOpenTarget) {
  const garageNumber = extractGarageNumberFromTarget(target)
  const displayName = normalizeContractorTargetText(target.displayName)

  return garages.find((garage) => target.entityId && garage.id === target.entityId)
    ?? garages.find((garage) => garageNumber && garage.number === garageNumber)
    ?? garages.find((garage) => displayName.length > 0 && normalizeContractorTargetText(garage.owner).includes(displayName))
    ?? null
}

function findSupplierForOpenTarget(suppliers: ContractorSupplierRow[], target: ContractorOpenTarget) {
  const displayName = normalizeContractorTargetText(target.displayName)

  return suppliers.find((supplier) => target.entityId && supplier.id === target.entityId)
    ?? suppliers.find((supplier) => displayName.length > 0 && normalizeContractorTargetText(supplier.name) === displayName)
    ?? suppliers.find((supplier) => displayName.length > 0 && normalizeContractorTargetText(supplier.name).includes(displayName))
    ?? null
}

function findStaffForOpenTarget(staff: ContractorStaffRow[], target: ContractorOpenTarget) {
  const displayName = normalizeContractorTargetText(target.displayName)

  return staff.find((employee) => target.entityId && employee.id === target.entityId)
    ?? staff.find((employee) => displayName.length > 0 && normalizeContractorTargetText(employee.fullName) === displayName)
    ?? staff.find((employee) => displayName.length > 0 && normalizeContractorTargetText(employee.fullName).includes(displayName))
    ?? null
}

type ContractorSection = 'garages' | 'suppliers' | 'staff'
type ContractorSortDirection = 'asc' | 'desc'
type ContractorSortableSection = ContractorSection
type ContractorDebtorFilterSection = 'garages' | 'suppliers'

type ContractorGarageRow = {
  id: string
  ownerId?: string | null
  number: string
  peopleCount: string
  floorCount: string
  owner: string
  phone: string
  address: string
  startingBalance?: string
  balance: string
  overdueDebt: string
  initialWater: string
  initialElectricity: string
  meters: string
  comment: string
  isDeleted: boolean
}

type ContractorSupplierRow = {
  id: string
  name: string
  service: string
  inn: string
  legalAddress: string
  contactPerson: string
  phone: string
  email: string
  contacts: ContractorSupplierContact[]
  startingBalance: string
  debt: string
  comment: string
  isDeleted: boolean
}

type ContractorSupplierContact = {
  id: string
  fullName: string
  position: string
  phone: string
  email: string
  status: 'Работает' | 'Не работает'
  comment: string
  isDeleted: boolean
  deleteReason?: string
}

type ContractorStaffRow = {
  id: string
  fullName: string
  department: string
  rate: string
  isDeleted: boolean
}

type ContractorFinancialReportTarget =
  | { type: 'supplier'; row: ContractorSupplierRow }
  | { type: 'employee'; row: ContractorStaffRow }

type ContractorFinancialReportRow = {
  id: string
  accountingMonth: string
  date: string
  documentNumber: string
  description: string
  accrualAmount: number
  paymentAmount: number
  balanceAfter: number
}

type ContractorFinancialReport = {
  accrualTotal: number
  paymentTotal: number
  balance: number
  rows: ContractorFinancialReportRow[]
}

type ContractorDepartmentRow = {
  id: string
  name: string
  isDeleted?: boolean
}

type ContractorModal =
  | { type: 'garage'; item?: ContractorGarageRow }
  | { type: 'supplier'; item?: ContractorSupplierRow }
  | { type: 'service' }
  | { type: 'employee'; item?: ContractorStaffRow }
  | { type: 'department'; item?: ContractorDepartmentRow }

type ContractorRestoreTarget =
  | { type: 'garage'; item: ContractorGarageRow }
  | { type: 'supplier'; item: ContractorSupplierRow }
  | { type: 'employee'; item: ContractorStaffRow }
  | { type: 'department'; item: ContractorDepartmentRow }

const contractorSectionLabels: Record<ContractorSection, string> = {
  garages: 'Гаражи',
  suppliers: 'Поставщики',
  staff: 'Персонал',
}

type ContractorGarageColumnKey = 'number' | 'peopleCount' | 'floorCount' | 'owner' | 'phone' | 'overdueDebt' | 'actions'
type ContractorGarageServerSortKey = Exclude<ContractorGarageColumnKey, 'actions'>
type ContractorSupplierSortKey = 'name' | 'service' | 'contactPerson' | 'phone' | 'email' | 'debt'
type ContractorSupplierServerSortKey = ContractorSupplierSortKey
type ContractorStaffSortKey = 'fullName' | 'department' | 'rate'
type ContractorSupplierColumnKey = ContractorSupplierSortKey | 'actions'
type ContractorStaffColumnKey = ContractorStaffSortKey | 'actions'
type ContractorSortKey = Exclude<ContractorGarageColumnKey, 'actions'> | ContractorSupplierSortKey | ContractorStaffSortKey
type ContractorSortState = {
  section: ContractorSortableSection
  key: ContractorSortKey
  direction: ContractorSortDirection
}
type ContractorColumnDefinition<TKey extends string> = { key: TKey; label: string; defaultWidth: number; minWidth: number }

const contractorGarageColumnStorageKey = 'garagebalance.contractors.garageColumnWidths'
const contractorSupplierColumnStorageKey = 'garagebalance.contractors.supplierColumnWidths'
const contractorStaffColumnStorageKey = 'garagebalance.contractors.staffColumnWidths'
const contractorsDictionaryListLimit = 500
const contractorsDefaultPageSize = 25

type ContractorPageState = {
  totalCount: number
  offset: number
  limit: number
}

const createContractorPageState = (): ContractorPageState => ({ totalCount: 0, offset: 0, limit: contractorsDefaultPageSize })
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const contractorGarageColumnDefinitions: Array<ContractorColumnDefinition<ContractorGarageColumnKey>> = [
  { key: 'number', label: 'Номер', defaultWidth: 96, minWidth: 72 },
  { key: 'peopleCount', label: 'Количество человек', defaultWidth: 170, minWidth: 132 },
  { key: 'floorCount', label: 'Количество этажей', defaultWidth: 170, minWidth: 132 },
  { key: 'owner', label: 'Владелец', defaultWidth: 260, minWidth: 160 },
  { key: 'phone', label: 'Телефон', defaultWidth: 220, minWidth: 150 },
  { key: 'overdueDebt', label: 'Просроченная задолженность', defaultWidth: 220, minWidth: 170 },
  { key: 'actions', label: 'Действия', defaultWidth: 132, minWidth: 112 },
]

const contractorSupplierColumnDefinitions: Array<ContractorColumnDefinition<ContractorSupplierColumnKey>> = [
  { key: 'name', label: 'Поставщик', defaultWidth: 180, minWidth: 140 },
  { key: 'service', label: 'Услуга', defaultWidth: 190, minWidth: 140 },
  { key: 'contactPerson', label: 'Контактное лицо', defaultWidth: 190, minWidth: 150 },
  { key: 'phone', label: 'Телефон', defaultWidth: 160, minWidth: 130 },
  { key: 'email', label: 'Почта', defaultWidth: 180, minWidth: 140 },
  { key: 'debt', label: 'Задолженность', defaultWidth: 150, minWidth: 130 },
  { key: 'actions', label: 'Действия', defaultWidth: 132, minWidth: 112 },
]

const contractorStaffColumnDefinitions: Array<ContractorColumnDefinition<ContractorStaffColumnKey>> = [
  { key: 'fullName', label: 'ФИО', defaultWidth: 260, minWidth: 180 },
  { key: 'department', label: 'Отдел', defaultWidth: 220, minWidth: 160 },
  { key: 'rate', label: 'Ставка', defaultWidth: 150, minWidth: 120 },
  { key: 'actions', label: 'Действия', defaultWidth: 132, minWidth: 112 },
]

function getDefaultContractorColumnWidths<TKey extends string>(definitions: Array<ContractorColumnDefinition<TKey>>) {
  return definitions.reduce<Record<TKey, number>>((widths, column) => {
    widths[column.key] = column.defaultWidth
    return widths
  }, {} as Record<TKey, number>)
}

function getSupplierServiceOptions(services: string[]) {
  return Array.from(new Set(services.map((service) => service.trim()).filter(Boolean))).sort((left, right) => left.localeCompare(right, 'ru'))
}

function getSupplierPrimaryContact(supplier: ContractorSupplierRow) {
  return supplier.contacts.find((contact) => !contact.isDeleted && contact.status === 'Работает') ?? supplier.contacts.find((contact) => !contact.isDeleted) ?? null
}

function normalizeSupplierPrototype(supplier: ContractorSupplierRow): ContractorSupplierRow {
  const primaryContact = getSupplierPrimaryContact(supplier)

  return {
    ...supplier,
    contactPerson: primaryContact?.fullName ?? supplier.contactPerson,
    phone: primaryContact?.phone ?? supplier.phone,
    email: primaryContact?.email ?? supplier.email,
  }
}

function isBackendDictionaryId(id: string) {
  return guidPattern.test(id)
}

function formatPrototypeMoney(value: number | null | undefined) {
  if (!value) {
    return ''
  }

  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(value)
}

function parsePrototypeMoney(value: string) {
  const normalized = value.replace(/\s/g, '').replace(',', '.').replace(/[^\d.-]/g, '')
  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? parsed : 0
}

function comparePrototypeText(left: string, right: string) {
  return left.localeCompare(right, 'ru', { numeric: true, sensitivity: 'base' })
}

function applyContractorSortDirection(value: number, direction: ContractorSortDirection) {
  return direction === 'asc' ? value : -value
}

function isContractorMoneyDebt(value: string) {
  return parsePrototypeMoney(value) > 0
}

function compareContractorGarages(left: ContractorGarageRow, right: ContractorGarageRow, key: Exclude<ContractorGarageColumnKey, 'actions'>) {
  if (key === 'peopleCount' || key === 'floorCount' || key === 'overdueDebt') {
    return parsePrototypeMoney(left[key]) - parsePrototypeMoney(right[key])
  }

  return comparePrototypeText(left[key], right[key])
}

function compareContractorSuppliers(left: ContractorSupplierRow, right: ContractorSupplierRow, key: ContractorSupplierSortKey) {
  if (key === 'debt') {
    return parsePrototypeMoney(left.debt) - parsePrototypeMoney(right.debt)
  }

  if (key === 'contactPerson' || key === 'phone' || key === 'email') {
    const leftContact = getSupplierPrimaryContact(left)
    const rightContact = getSupplierPrimaryContact(right)
    const leftValue = key === 'contactPerson' ? leftContact?.fullName ?? left.contactPerson : key === 'phone' ? leftContact?.phone ?? left.phone : leftContact?.email ?? left.email
    const rightValue = key === 'contactPerson' ? rightContact?.fullName ?? right.contactPerson : key === 'phone' ? rightContact?.phone ?? right.phone : rightContact?.email ?? right.email
    return comparePrototypeText(leftValue, rightValue)
  }

  return comparePrototypeText(left[key], right[key])
}

function isGarageServerSortKey(key: ContractorSortKey): key is ContractorGarageServerSortKey {
  return key === 'number' || key === 'peopleCount' || key === 'floorCount' || key === 'owner' || key === 'phone' || key === 'overdueDebt'
}

function isSupplierServerSortKey(key: ContractorSortKey): key is ContractorSupplierServerSortKey {
  return key === 'name' || key === 'service' || key === 'contactPerson' || key === 'phone' || key === 'email' || key === 'debt'
}

function compareContractorStaff(left: ContractorStaffRow, right: ContractorStaffRow, key: ContractorStaffSortKey) {
  if (key === 'rate') {
    return parsePrototypeMoney(left.rate) - parsePrototypeMoney(right.rate)
  }

  return comparePrototypeText(left[key], right[key])
}

function compareContractorReportEntries(
  left: Omit<ContractorFinancialReportRow, 'balanceAfter'>,
  right: Omit<ContractorFinancialReportRow, 'balanceAfter'>,
) {
  const monthComparison = left.accountingMonth.localeCompare(right.accountingMonth)
  if (monthComparison !== 0) {
    return monthComparison
  }

  const dateComparison = left.date.localeCompare(right.date)
  if (dateComparison !== 0) {
    return dateComparison
  }

  return left.description.localeCompare(right.description)
}

function buildContractorFinancialReport(entries: Array<Omit<ContractorFinancialReportRow, 'balanceAfter'>>): ContractorFinancialReport {
  let balance = 0
  let accrualTotal = 0
  let paymentTotal = 0
  const rows = [...entries].sort(compareContractorReportEntries).map((entry) => {
    accrualTotal += entry.accrualAmount
    paymentTotal += entry.paymentAmount
    balance += entry.accrualAmount - entry.paymentAmount

    return {
      ...entry,
      balanceAfter: balance,
    }
  })

  return {
    accrualTotal,
    paymentTotal,
    balance,
    rows,
  }
}

function getContractorReportMonthStarts(monthFrom: string, monthTo: string) {
  const [fromYear, fromMonth] = monthFrom.split('-').map(Number)
  const [toYear, toMonth] = monthTo.split('-').map(Number)
  if (!fromYear || !fromMonth || !toYear || !toMonth) {
    return []
  }

  const months: string[] = []
  const cursor = new Date(fromYear, fromMonth - 1, 1)
  const last = new Date(toYear, toMonth - 1, 1)
  while (cursor <= last) {
    months.push(`${cursor.getFullYear()}-${String(cursor.getMonth() + 1).padStart(2, '0')}-01`)
    cursor.setMonth(cursor.getMonth() + 1)
  }

  return months
}

function createStaffFinancialReportEntries(row: ContractorStaffRow, monthFrom: string, monthTo: string) {
  const rate = parsePrototypeMoney(row.rate)
  if (rate <= 0) {
    return []
  }

  return getContractorReportMonthStarts(monthFrom, monthTo).map((month) => ({
    id: `staff-accrual-${row.id}-${month}`,
    accountingMonth: month,
    date: month,
    documentNumber: '—',
    description: 'Начисление зарплаты',
    accrualAmount: rate,
    paymentAmount: 0,
  }))
}

function formatPrototypeNumber(value: number | null | undefined) {
  if (value === null || value === undefined) {
    return ''
  }

  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 4 }).format(value)
}

function parsePrototypeInteger(value: string, fallback = 0) {
  const parsed = Number.parseInt(value.trim(), 10)
  return Number.isFinite(parsed) ? parsed : fallback
}

function parsePrototypeNullableNumber(value: string) {
  const normalized = value.replace(/\s/g, '').replace(',', '.')
  if (!normalized) {
    return null
  }

  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? parsed : null
}

function normalizeOwnerName(value: string) {
  return value.trim().replace(/\s+/g, ' ')
}

function splitOwnerName(value: string) {
  const [lastName = '', firstName = '', ...middleNameParts] = normalizeOwnerName(value).split(' ')
  return {
    lastName,
    firstName: firstName || 'Без имени',
    middleName: middleNameParts.join(' '),
  }
}

function createOwnerRequestFromGarage(row: ContractorGarageRow): UpsertOwnerRequest {
  const parsedName = splitOwnerName(row.owner)

  return {
    lastName: parsedName.lastName || 'Без фамилии',
    firstName: parsedName.firstName,
    middleName: parsedName.middleName,
    phone: row.phone.trim(),
    address: row.address.trim(),
    meterNotes: row.meters.trim(),
  }
}

function createGarageRowFromDto(garage: GarageDto, owners: OwnerDto[]): ContractorGarageRow {
  const owner = garage.ownerId ? owners.find((item) => item.id === garage.ownerId) : null
  const balance = garage.balance ?? garage.startingBalance ?? 0
  const overdueDebt = garage.overdueDebt ?? Math.max(balance, 0)

  return {
    id: garage.id,
    ownerId: garage.ownerId,
    number: garage.number,
    peopleCount: String(garage.peopleCount),
    floorCount: String(garage.floorCount),
    owner: garage.ownerName ?? owner?.fullName ?? '',
    phone: owner?.phone ?? '',
    address: owner?.address ?? '',
    startingBalance: formatPrototypeMoney(garage.startingBalance),
    balance: formatPrototypeMoney(balance),
    overdueDebt: overdueDebt > 0 ? `${formatPrototypeMoney(overdueDebt)} руб.` : '',
    initialWater: formatPrototypeNumber(garage.initialWaterMeterValue),
    initialElectricity: formatPrototypeNumber(garage.initialElectricityMeterValue),
    meters: owner?.meterNotes ?? '',
    comment: garage.comment ?? '',
    isDeleted: garage.isArchived,
  }
}

function createGarageRequestFromRow(row: ContractorGarageRow, ownerId: string | null): UpsertGarageRequest {
  return {
    number: row.number.trim(),
    peopleCount: parsePrototypeInteger(row.peopleCount, 0),
    floorCount: parsePrototypeInteger(row.floorCount, 0),
    ownerId,
    startingBalance: parsePrototypeMoney(row.startingBalance ?? row.balance),
    initialWaterMeterValue: parsePrototypeNullableNumber(row.initialWater),
    initialElectricityMeterValue: parsePrototypeNullableNumber(row.initialElectricity),
    comment: row.comment.trim(),
  }
}

async function resolveGarageOwner(
  dictionaryClient: DictionaryClient,
  accessToken: string,
  owners: OwnerDto[],
  row: ContractorGarageRow,
) {
  const ownerName = normalizeOwnerName(row.owner)
  if (!ownerName) {
    return null
  }

  const existing = owners.find((owner) => owner.id === row.ownerId)
    ?? owners.find((owner) => normalizeOwnerName(owner.fullName).localeCompare(ownerName, 'ru', { sensitivity: 'accent' }) === 0)
  const request = createOwnerRequestFromGarage(row)

  if (existing) {
    const shouldUpdate = existing.phone !== (request.phone || null)
      || existing.address !== (request.address || null)
      || existing.meterNotes !== (request.meterNotes || null)
      || normalizeOwnerName(existing.fullName) !== ownerName
    if (!shouldUpdate) {
      return existing
    }

    return dictionaryClient.updateOwner(accessToken, existing.id, request)
  }

  return dictionaryClient.createOwner(accessToken, request)
}

function createSupplierContactFromDto(contact: SupplierContactDto): ContractorSupplierContact {
  return {
    id: contact.id,
    fullName: contact.fullName,
    position: contact.position ?? '',
    phone: contact.phone ?? '',
    email: contact.email ?? '',
    status: contact.status === 'Не работает' ? 'Не работает' : 'Работает',
    comment: contact.comment ?? '',
    isDeleted: contact.isArchived,
  }
}

function createSupplierRowFromDto(supplier: SupplierDto, contacts: SupplierContactDto[]): ContractorSupplierRow {
  const supplierContacts = contacts.filter((contact) => contact.supplierId === supplier.id).map(createSupplierContactFromDto)

  return normalizeSupplierPrototype({
    id: supplier.id,
    name: supplier.name,
    service: supplier.groupName,
    inn: supplier.inn ?? '',
    legalAddress: supplier.legalAddress ?? '',
    contactPerson: supplier.contactPerson ?? '',
    phone: supplier.phone ?? '',
    email: supplier.email ?? '',
    contacts: supplierContacts,
    startingBalance: formatPrototypeMoney(supplier.startingBalance),
    debt: formatPrototypeMoney(supplier.debt),
    comment: supplier.comment ?? '',
    isDeleted: supplier.isArchived,
  })
}

function createStaffDepartmentRowFromDto(department: StaffDepartmentDto): ContractorDepartmentRow {
  return {
    id: department.id,
    name: department.name,
    isDeleted: department.isArchived,
  }
}

function createStaffRowFromDto(member: StaffMemberDto): ContractorStaffRow {
  return {
    id: member.id,
    fullName: member.fullName,
    department: member.departmentName,
    rate: formatPrototypeMoney(member.rate),
    isDeleted: member.isArchived,
  }
}

async function resolveSupplierGroup(
  dictionaryClient: DictionaryClient,
  accessToken: string,
  groups: SupplierGroupDto[],
  serviceName: string,
) {
  const normalizedName = serviceName.trim() || 'Прочее'
  const existing = groups.find((group) => !group.isArchived && group.name.localeCompare(normalizedName, 'ru', { sensitivity: 'accent' }) === 0)
  if (existing) {
    return existing
  }

  const created = await dictionaryClient.createSupplierGroup(accessToken, { name: normalizedName })
  groups.push(created)
  return created
}

function createSupplierRequestFromRow(row: ContractorSupplierRow, groupId: string): UpsertSupplierRequest {
  const normalized = normalizeSupplierPrototype(row)
  return {
    name: normalized.name.trim(),
    groupId,
    inn: normalized.inn.trim(),
    legalAddress: normalized.legalAddress.trim(),
    contactPerson: normalized.contactPerson.trim(),
    phone: normalized.phone.trim(),
    email: normalized.email.trim(),
    startingBalance: parsePrototypeMoney(normalized.startingBalance),
    comment: normalized.comment.trim(),
  }
}

function createSupplierContactRequestFromRow(supplierId: string, contact: ContractorSupplierContact): UpsertSupplierContactRequest {
  return {
    supplierId,
    fullName: contact.fullName.trim(),
    position: contact.position.trim(),
    phone: contact.phone.trim(),
    email: contact.email.trim(),
    status: contact.status,
    comment: contact.comment.trim(),
  }
}

function createStaffMemberRequestFromRow(row: ContractorStaffRow, departmentId: string): UpsertStaffMemberRequest {
  return {
    fullName: row.fullName.trim(),
    departmentId,
    rate: parsePrototypeMoney(row.rate),
  }
}

function createEmptySupplierContact(): ContractorSupplierContact {
  return {
    id: `supplier-contact-${Date.now()}`,
    fullName: '',
    position: '',
    phone: '',
    email: '',
    status: 'Работает',
    comment: '',
    isDeleted: false,
  }
}

function formatSupplierContactSummary(contacts: ContractorSupplierContact[]) {
  if (contacts.length === 0) {
    return ''
  }

  return contacts
    .map((contact, index) => {
      const state = contact.isDeleted ? 'удален' : contact.status
      return `${index + 1}. ${contact.fullName || 'Без ФИО'} / ${contact.position || 'Без должности'} / ${contact.phone || 'Без телефона'} / ${contact.email || 'Без почты'} / ${state} / ${contact.comment || 'Без комментария'}`
    })
    .join('; ')
}

function loadContractorColumnWidths<TKey extends string>(storageKey: string, definitions: Array<ContractorColumnDefinition<TKey>>) {
  const defaults = getDefaultContractorColumnWidths(definitions)

  try {
    const rawValue = window.localStorage.getItem(storageKey)
    if (!rawValue) {
      return defaults
    }

    const parsed = JSON.parse(rawValue) as Partial<Record<TKey, number>>
    return definitions.reduce<Record<TKey, number>>((widths, column) => {
      const value = parsed[column.key]
      widths[column.key] = typeof value === 'number' && Number.isFinite(value) ? Math.max(column.minWidth, value) : defaults[column.key]
      return widths
    }, {} as Record<TKey, number>)
  } catch {
    return defaults
  }
}

function saveContractorColumnWidths<TKey extends string>(storageKey: string, widths: Record<TKey, number>) {
  try {
    window.localStorage.setItem(storageKey, JSON.stringify(widths))
  } catch {
    // Column widths are a UI preference; the table must work if localStorage is unavailable.
  }
}

function loadGarageColumnWidths() {
  return loadContractorColumnWidths(contractorGarageColumnStorageKey, contractorGarageColumnDefinitions)
}

function startContractorColumnResize<TKey extends string>(
  definitions: Array<ContractorColumnDefinition<TKey>>,
  widths: Record<TKey, number>,
  setWidths: (updater: (currentWidths: Record<TKey, number>) => Record<TKey, number>) => void,
  columnKey: TKey,
  event: MouseEvent<HTMLButtonElement>,
) {
  event.preventDefault()
  event.stopPropagation()
  const column = definitions.find((item) => item.key === columnKey)
  if (!column) {
    return
  }

  const startX = event.clientX
  const startWidth = widths[columnKey]
  const handleMouseMove = (moveEvent: globalThis.MouseEvent) => {
    const nextWidth = Math.max(column.minWidth, startWidth + moveEvent.clientX - startX)
    setWidths((currentWidths) => ({ ...currentWidths, [columnKey]: nextWidth }))
  }
  const handleMouseUp = () => {
    document.removeEventListener('mousemove', handleMouseMove)
    document.removeEventListener('mouseup', handleMouseUp)
  }

  document.addEventListener('mousemove', handleMouseMove)
  document.addEventListener('mouseup', handleMouseUp)
}

function getContractorRestoreTitle(target: ContractorRestoreTarget) {
  if (target.type === 'garage') {
    return `Гараж ${target.item.number || 'без номера'}`
  }

  if (target.type === 'supplier') {
    return target.item.name || 'Поставщик без названия'
  }

  if (target.type === 'department') {
    return `Отдел ${target.item.name || 'без названия'}`
  }

  return target.item.fullName || 'Сотрудник без имени'
}

type ContractorsPrototypeSavedState = {
  garages: ContractorGarageRow[]
  suppliers: ContractorSupplierRow[]
  staff: ContractorStaffRow[]
  departments: ContractorDepartmentRow[]
  supplierServices: string[]
}

export function ContractorsPrototypePanel({ auth, dictionaryClient, financeClient, formStateClient, integrationClient, initialTarget = null, onOpenAudit }: { auth: AuthResponse; dictionaryClient: DictionaryClient; financeClient: FinanceClient; formStateClient: FormStateClient; integrationClient: IntegrationClient; initialTarget?: ContractorOpenTarget | null; onOpenAudit: (preset: AuditPanelPreset) => void }) {
  const [activeSection, setActiveSection] = useState<ContractorSection>('garages')
  const [debtorFilters, setDebtorFilters] = useState<Record<ContractorDebtorFilterSection, boolean>>({ garages: false, suppliers: false })
  const [contractorSort, setContractorSort] = useState<ContractorSortState>({ section: 'garages', key: 'number', direction: 'asc' })
  const [garages, setGarages] = useState<ContractorGarageRow[]>([])
  const [garagePage, setGaragePage] = useState<ContractorPageState>(createContractorPageState)
  const [owners, setOwners] = useState<OwnerDto[]>([])
  const [suppliers, setSuppliers] = useState<ContractorSupplierRow[]>([])
  const [supplierPage, setSupplierPage] = useState<ContractorPageState>(createContractorPageState)
  const [supplierContacts, setSupplierContacts] = useState<SupplierContactDto[]>([])
  const [contractorPageLoading, setContractorPageLoading] = useState<Record<ContractorSection, boolean>>({ garages: true, suppliers: true, staff: true })
  const [staff, setStaff] = useState<ContractorStaffRow[]>([])
  const [staffPage, setStaffPage] = useState<ContractorPageState>(createContractorPageState)
  const [departments, setDepartments] = useState<ContractorDepartmentRow[]>([])
  const [departmentPageNumber, setDepartmentPageNumber] = useState(1)
  const [departmentPageSize, setDepartmentPageSize] = useState(10)
  const [supplierGroups, setSupplierGroups] = useState<SupplierGroupDto[]>([])
  const [supplierServices, setSupplierServices] = useState<string[]>([])
  const [formStateLoaded, setFormStateLoaded] = useState(false)
  const [formStateError, setFormStateError] = useState<string | null>(null)
  const [modal, setModal] = useState<ContractorModal | null>(null)
  const [restoreTarget, setRestoreTarget] = useState<ContractorRestoreTarget | null>(null)
  const [garageColumnWidths, setGarageColumnWidths] = useState(loadGarageColumnWidths)
  const [supplierColumnWidths, setSupplierColumnWidths] = useState(() => loadContractorColumnWidths(contractorSupplierColumnStorageKey, contractorSupplierColumnDefinitions))
  const [staffColumnWidths, setStaffColumnWidths] = useState(() => loadContractorColumnWidths(contractorStaffColumnStorageKey, contractorStaffColumnDefinitions))
  const [garageContextMenu, setGarageContextMenu] = useState<{ row: ContractorGarageRow; x: number; y: number } | null>(null)
  const [garageDeleteTarget, setGarageDeleteTarget] = useState<ContractorGarageRow | null>(null)
  const [garageDeleteReason, setGarageDeleteReason] = useState('')
  const [garageFinancialReportTarget, setGarageFinancialReportTarget] = useState<ContractorGarageRow | null>(null)
  const [garageFinancialReport, setGarageFinancialReport] = useState<GarageBalanceHistoryDto | null>(null)
  const [garageFinancialReportFilters, setGarageFinancialReportFilters] = useState(() => createDefaultGarageBalanceHistoryFilters())
  const [garageFinancialReportLoading, setGarageFinancialReportLoading] = useState(false)
  const [garageFinancialReportError, setGarageFinancialReportError] = useState<string | null>(null)
  const [contractorFinancialReportTarget, setContractorFinancialReportTarget] = useState<ContractorFinancialReportTarget | null>(null)
  const [contractorFinancialReport, setContractorFinancialReport] = useState<ContractorFinancialReport | null>(null)
  const [contractorFinancialReportFilters, setContractorFinancialReportFilters] = useState(() => createDefaultGarageBalanceHistoryFilters())
  const [contractorFinancialReportLoading, setContractorFinancialReportLoading] = useState(false)
  const [contractorFinancialReportError, setContractorFinancialReportError] = useState<string | null>(null)
  const [supplierContextMenu, setSupplierContextMenu] = useState<{ row: ContractorSupplierRow; x: number; y: number } | null>(null)
  const [supplierDeleteTarget, setSupplierDeleteTarget] = useState<ContractorSupplierRow | null>(null)
  const [supplierDeleteReason, setSupplierDeleteReason] = useState('')
  const [employeeContextMenu, setEmployeeContextMenu] = useState<{ row: ContractorStaffRow; x: number; y: number } | null>(null)
  const [employeeDeleteTarget, setEmployeeDeleteTarget] = useState<ContractorStaffRow | null>(null)
  const [employeeDeleteReason, setEmployeeDeleteReason] = useState('')
  const [departmentContextMenu, setDepartmentContextMenu] = useState<{ row: ContractorDepartmentRow; x: number; y: number } | null>(null)
  const [departmentDeleteTarget, setDepartmentDeleteTarget] = useState<ContractorDepartmentRow | null>(null)
  const [departmentDeleteReason, setDepartmentDeleteReason] = useState('')
  const openedInitialTargetRef = useRef<string | null>(null)
  useRestoreFocusOnClose(Boolean(restoreTarget))
  useRestoreFocusOnClose(Boolean(garageDeleteTarget))
  useRestoreFocusOnClose(Boolean(garageFinancialReportTarget))
  useRestoreFocusOnClose(Boolean(contractorFinancialReportTarget))
  useRestoreFocusOnClose(Boolean(supplierDeleteTarget))
  useRestoreFocusOnClose(Boolean(employeeDeleteTarget))
  useRestoreFocusOnClose(Boolean(departmentDeleteTarget))
  const restoreDialogRef = useFocusTrap<HTMLElement>(Boolean(restoreTarget))
  const restoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(restoreTarget))
  const garageDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(garageDeleteTarget))
  const garageDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(garageDeleteTarget))
  const garageFinancialReportDialogRef = useFocusTrap<HTMLElement>(Boolean(garageFinancialReportTarget))
  const garageFinancialReportCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(garageFinancialReportTarget))
  const contractorFinancialReportDialogRef = useFocusTrap<HTMLElement>(Boolean(contractorFinancialReportTarget))
  const contractorFinancialReportCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(contractorFinancialReportTarget))
  const supplierDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(supplierDeleteTarget))
  const supplierDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(supplierDeleteTarget))
  const employeeDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(employeeDeleteTarget))
  const employeeDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(employeeDeleteTarget))
  const departmentDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(departmentDeleteTarget))
  const departmentDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(departmentDeleteTarget))
  useEscapeKey(Boolean(restoreTarget), () => setRestoreTarget(null))
  useEscapeKey(Boolean(garageContextMenu), () => setGarageContextMenu(null))
  useEscapeKey(Boolean(garageDeleteTarget), () => closeGarageDeleteDialog())
  useEscapeKey(Boolean(garageFinancialReportTarget), () => closeGarageFinancialReport())
  useEscapeKey(Boolean(contractorFinancialReportTarget), () => closeContractorFinancialReport())
  useEscapeKey(Boolean(supplierContextMenu), () => setSupplierContextMenu(null))
  useEscapeKey(Boolean(supplierDeleteTarget), () => closeSupplierDeleteDialog())
  useEscapeKey(Boolean(employeeContextMenu), () => setEmployeeContextMenu(null))
  useEscapeKey(Boolean(employeeDeleteTarget), () => closeEmployeeDeleteDialog())
  useEscapeKey(Boolean(departmentContextMenu), () => setDepartmentContextMenu(null))
  useEscapeKey(Boolean(departmentDeleteTarget), () => closeDepartmentDeleteDialog())

  useEffect(() => {
    let cancelled = false
    formStateClient
      .getState<ContractorsPrototypeSavedState>(auth.accessToken, contractorsFormStateScope)
      .catch((error: unknown) => {
        if (!cancelled) {
          setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить сохраненное состояние контрагентов.')
        }
      })
      .finally(() => {
        if (!cancelled) {
          setFormStateLoaded(true)
        }
      })

    return () => {
      cancelled = true
    }
  }, [auth.accessToken, formStateClient])

  useEffect(() => {
    let cancelled = false

    async function loadContractorsFromDictionaries() {
      try {
        const [ownerRows, garageRows, groups, supplierRows, supplierContactRows, departmentRows, staffRows] = await Promise.all([
          dictionaryClient.getOwners(auth.accessToken, undefined, contractorsDictionaryListLimit, true),
          dictionaryClient.getGaragesPage
            ? dictionaryClient.getGaragesPage(auth.accessToken, undefined, 0, contractorsDefaultPageSize, true)
            : dictionaryClient.getGarages(auth.accessToken, undefined, contractorsDictionaryListLimit, true).then((items) => createFallbackPage(items, 0, contractorsDefaultPageSize)),
          dictionaryClient.getSupplierGroups(auth.accessToken, undefined, contractorsDictionaryListLimit, true),
          dictionaryClient.getSuppliersPage
            ? dictionaryClient.getSuppliersPage(auth.accessToken, undefined, undefined, 0, contractorsDefaultPageSize, true)
            : dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, contractorsDictionaryListLimit, true).then((items) => createFallbackPage(items, 0, contractorsDefaultPageSize)),
          dictionaryClient.getSupplierContacts(auth.accessToken, undefined, undefined, contractorsDictionaryListLimit, true),
          dictionaryClient.getStaffDepartments(auth.accessToken, contractorsDictionaryListLimit, true),
          dictionaryClient.getStaffMembersPage
            ? dictionaryClient.getStaffMembersPage(auth.accessToken, undefined, undefined, 0, contractorsDefaultPageSize, true)
            : dictionaryClient.getStaffMembers(auth.accessToken, undefined, undefined, contractorsDictionaryListLimit, true).then((items) => createFallbackPage(items, 0, contractorsDefaultPageSize)),
        ])

        if (cancelled) {
          return
        }

        const nextSuppliers = supplierRows.items.map((supplier) => createSupplierRowFromDto(supplier, supplierContactRows))

        setOwners(ownerRows)
        setGarages(garageRows.items.map((garage) => createGarageRowFromDto(garage, ownerRows)))
        setGaragePage({ totalCount: garageRows.totalCount, offset: garageRows.offset, limit: garageRows.limit })
        setSupplierGroups(groups)
        setSuppliers(nextSuppliers)
        setSupplierPage({ totalCount: supplierRows.totalCount, offset: supplierRows.offset, limit: supplierRows.limit })
        setSupplierContacts(supplierContactRows)
        setSupplierServices(getSupplierServiceOptions([...groups.map((group) => group.name), ...nextSuppliers.map((supplier) => supplier.service)]))
        setDepartments(departmentRows.map(createStaffDepartmentRowFromDto))
        setStaff(staffRows.items.map(createStaffRowFromDto))
        setStaffPage({ totalCount: staffRows.totalCount, offset: staffRows.offset, limit: staffRows.limit })
      } catch (error) {
        if (!cancelled) {
          setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить контрагентов из справочников.')
        }
      } finally {
        if (!cancelled) {
          setContractorPageLoading({ garages: false, suppliers: false, staff: false })
        }
      }
    }

    void loadContractorsFromDictionaries()

    return () => {
      cancelled = true
    }
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    if (!initialTarget) {
      openedInitialTargetRef.current = null
      return
    }

    const targetKey = `${initialTarget.section}:${initialTarget.entityId ?? ''}:${initialTarget.garageNumber ?? ''}:${initialTarget.displayName ?? ''}`
    if (openedInitialTargetRef.current === targetKey) {
      return
    }

    let nextSection: ContractorSection
    let nextModal: ContractorModal
    let closeContextMenu: () => void

    if (initialTarget.section === 'garages') {
      const targetGarage = findGarageForOpenTarget(garages, initialTarget)
      if (!targetGarage) {
        return
      }

      nextSection = 'garages'
      nextModal = { type: 'garage', item: targetGarage }
      closeContextMenu = () => setGarageContextMenu(null)
    } else if (initialTarget.section === 'suppliers') {
      const targetSupplier = findSupplierForOpenTarget(suppliers, initialTarget)
      if (!targetSupplier) {
        return
      }

      nextSection = 'suppliers'
      nextModal = { type: 'supplier', item: targetSupplier }
      closeContextMenu = () => setSupplierContextMenu(null)
    } else {
      const targetEmployee = findStaffForOpenTarget(staff, initialTarget)
      if (!targetEmployee) {
        return
      }

      nextSection = 'staff'
      nextModal = { type: 'employee', item: targetEmployee }
      closeContextMenu = () => setEmployeeContextMenu(null)
    }

    openedInitialTargetRef.current = targetKey
    const handle = window.setTimeout(() => {
      setActiveSection(nextSection)
      closeContextMenu()
      setModal(nextModal)
    }, 0)

    return () => window.clearTimeout(handle)
  }, [garages, initialTarget, staff, suppliers])

  useEffect(() => {
    if (!formStateLoaded) {
      return
    }

    const handle = window.setTimeout(() => {
      void formStateClient
        .saveState<ContractorsPrototypeSavedState>(auth.accessToken, contractorsFormStateScope, {
          payload: { garages, suppliers, staff, departments, supplierServices },
          summary: 'Сохранено состояние раздела контрагентов.'
        })
        .catch((error: unknown) => setFormStateError(error instanceof Error ? error.message : 'Не удалось сохранить состояние контрагентов.'))
    }, 400)

    return () => window.clearTimeout(handle)
  }, [auth.accessToken, departments, formStateClient, formStateLoaded, garages, staff, supplierServices, suppliers])

  useEffect(() => {
    saveContractorColumnWidths(contractorGarageColumnStorageKey, garageColumnWidths)
  }, [garageColumnWidths])

  useEffect(() => {
    saveContractorColumnWidths(contractorSupplierColumnStorageKey, supplierColumnWidths)
  }, [supplierColumnWidths])

  useEffect(() => {
    saveContractorColumnWidths(contractorStaffColumnStorageKey, staffColumnWidths)
  }, [staffColumnWidths])

  const garageTableStyle = useMemo(() => {
    return contractorGarageColumnDefinitions.reduce<CSSProperties>((style, column) => {
      return { ...style, [`--garage-col-${column.key}`]: `${garageColumnWidths[column.key]}px` }
    }, {})
  }, [garageColumnWidths])

  const supplierTableStyle = useMemo(() => {
    return contractorSupplierColumnDefinitions.reduce<CSSProperties>((style, column) => {
      return { ...style, [`--supplier-col-${column.key}`]: `${supplierColumnWidths[column.key]}px` }
    }, {})
  }, [supplierColumnWidths])

  const staffTableStyle = useMemo(() => {
    return contractorStaffColumnDefinitions.reduce<CSSProperties>((style, column) => {
      return { ...style, [`--staff-col-${column.key}`]: `${staffColumnWidths[column.key]}px` }
    }, {})
  }, [staffColumnWidths])
  const canReadContractorHistory = hasPermission(auth, permissions.auditRead)

  async function loadGaragePage(
    offset = garagePage.offset,
    limit = garagePage.limit,
    sort: ContractorSortState = contractorSort.section === 'garages' && isGarageServerSortKey(contractorSort.key)
      ? contractorSort
      : { section: 'garages', key: 'number', direction: 'asc' },
  ) {
    setContractorPageLoading((current) => ({ ...current, garages: true }))
    setGarageContextMenu(null)
    try {
      const page = dictionaryClient.getGaragesPage
        ? await dictionaryClient.getGaragesPage(auth.accessToken, undefined, offset, limit, true, sort.key, sort.direction)
        : createFallbackPage(await dictionaryClient.getGarages(auth.accessToken, undefined, contractorsDictionaryListLimit, true), offset, limit)
      setGarages(page.items.map((garage) => createGarageRowFromDto(garage, owners)))
      setGaragePage({ totalCount: page.totalCount, offset: page.offset, limit: page.limit })
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить страницу гаражей.')
    } finally {
      setContractorPageLoading((current) => ({ ...current, garages: false }))
    }
  }

  async function loadSupplierPage(
    offset = supplierPage.offset,
    limit = supplierPage.limit,
    sort: ContractorSortState = contractorSort.section === 'suppliers' && isSupplierServerSortKey(contractorSort.key)
      ? contractorSort
      : { section: 'suppliers', key: 'service', direction: 'asc' },
  ) {
    setContractorPageLoading((current) => ({ ...current, suppliers: true }))
    setSupplierContextMenu(null)
    try {
      const page = dictionaryClient.getSuppliersPage
        ? await dictionaryClient.getSuppliersPage(auth.accessToken, undefined, undefined, offset, limit, true, sort.key, sort.direction)
        : createFallbackPage(await dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, contractorsDictionaryListLimit, true), offset, limit)
      const nextSuppliers = page.items.map((supplier) => createSupplierRowFromDto(supplier, supplierContacts))
      setSuppliers(nextSuppliers)
      setSupplierPage({ totalCount: page.totalCount, offset: page.offset, limit: page.limit })
      setSupplierServices((currentServices) => getSupplierServiceOptions([...currentServices, ...nextSuppliers.map((supplier) => supplier.service)]))
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить страницу поставщиков.')
    } finally {
      setContractorPageLoading((current) => ({ ...current, suppliers: false }))
    }
  }

  async function loadStaffPage(
    offset = staffPage.offset,
    limit = staffPage.limit,
    sort: ContractorSortState = contractorSort.section === 'staff'
      ? contractorSort
      : { section: 'staff', key: 'fullName', direction: 'asc' },
  ) {
    setContractorPageLoading((current) => ({ ...current, staff: true }))
    setEmployeeContextMenu(null)
    try {
      const page = dictionaryClient.getStaffMembersPage
        ? await dictionaryClient.getStaffMembersPage(auth.accessToken, undefined, undefined, offset, limit, true, sort.key, sort.direction)
        : createFallbackPage(await dictionaryClient.getStaffMembers(auth.accessToken, undefined, undefined, contractorsDictionaryListLimit, true), offset, limit)
      setStaff(page.items.map(createStaffRowFromDto))
      setStaffPage({ totalCount: page.totalCount, offset: page.offset, limit: page.limit })
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить страницу персонала.')
    } finally {
      setContractorPageLoading((current) => ({ ...current, staff: false }))
    }
  }

  const resizeGarageColumn = (columnKey: ContractorGarageColumnKey, event: MouseEvent<HTMLButtonElement>) => {
    startContractorColumnResize(contractorGarageColumnDefinitions, garageColumnWidths, setGarageColumnWidths, columnKey, event)
  }

  const resizeSupplierColumn = (columnKey: ContractorSupplierColumnKey, event: MouseEvent<HTMLButtonElement>) => {
    startContractorColumnResize(contractorSupplierColumnDefinitions, supplierColumnWidths, setSupplierColumnWidths, columnKey, event)
  }

  const resizeStaffColumn = (columnKey: ContractorStaffColumnKey, event: MouseEvent<HTMLButtonElement>) => {
    startContractorColumnResize(contractorStaffColumnDefinitions, staffColumnWidths, setStaffColumnWidths, columnKey, event)
  }

  const saveGarage = async (garage: ContractorGarageRow) => {
    const currentGarage = garages.find((item) => item.id === garage.id)

    try {
      const savedOwner = await resolveGarageOwner(dictionaryClient, auth.accessToken, owners, garage)
      if (savedOwner) {
        setOwners((currentOwners) => {
          if (currentOwners.some((owner) => owner.id === savedOwner.id)) {
            return currentOwners.map((owner) => (owner.id === savedOwner.id ? savedOwner : owner))
          }

          return [...currentOwners, savedOwner]
        })
      }

      const request = createGarageRequestFromRow(garage, savedOwner?.id ?? null)
      const savedGarage = isBackendDictionaryId(garage.id)
        ? await dictionaryClient.updateGarage(auth.accessToken, garage.id, request)
        : await dictionaryClient.createGarage(auth.accessToken, request)
      const nextGarage = createGarageRowFromDto(savedGarage, savedOwner ? [...owners.filter((owner) => owner.id !== savedOwner.id), savedOwner] : owners)

      setGarages((currentGarages) => {
        if (currentGarage) {
          return currentGarages.map((item) => (item.id === garage.id ? nextGarage : item))
        }

        return [...currentGarages.slice(0, Math.max(0, garagePage.limit - 1)), nextGarage]
      })
      if (!currentGarage) {
        setGaragePage((currentPage) => ({ ...currentPage, totalCount: currentPage.totalCount + 1 }))
      }
      return
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось сохранить гараж.')
      return
    }
  }

  const deleteGarage = async (garage: ContractorGarageRow, reason = 'Гараж удален из таблицы контрагентов.') => {
    try {
      if (isBackendDictionaryId(garage.id)) {
        await dictionaryClient.archiveGarage(auth.accessToken, garage.id, reason)
      }
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось удалить гараж.')
      return
    }

    setGarages((currentGarages) => currentGarages.map((item) => (item.id === garage.id ? { ...item, isDeleted: true } : item)))
  }

  function openGarageContextMenu(event: MouseEvent<HTMLDivElement>, row: ContractorGarageRow) {
    event.preventDefault()
    setGarageContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  function openGarageEditor(row: ContractorGarageRow) {
    setGarageContextMenu(null)
    setModal({ type: 'garage', item: row })
  }

  function openGarageDeleteDialog(row: ContractorGarageRow) {
    setGarageContextMenu(null)
    setGarageDeleteTarget(row)
    setGarageDeleteReason('')
  }

  function closeGarageDeleteDialog() {
    setGarageDeleteTarget(null)
    setGarageDeleteReason('')
  }

  function confirmGarageDeleteFromTable() {
    if (!garageDeleteTarget || !garageDeleteReason.trim()) {
      return
    }

    void deleteGarage(garageDeleteTarget, garageDeleteReason.trim())
    closeGarageDeleteDialog()
  }

  function restoreGarage(row: ContractorGarageRow) {
    setGarageContextMenu(null)
    setRestoreTarget({ type: 'garage', item: row })
  }

  async function loadGarageFinancialReport(row = garageFinancialReportTarget, filters = garageFinancialReportFilters) {
    if (!row) {
      return
    }

    if (!isBackendDictionaryId(row.id)) {
      setGarageFinancialReport(null)
      setGarageFinancialReportError('Финансовый отчет доступен для гаража, сохраненного в справочнике.')
      return
    }

    setGarageFinancialReportLoading(true)
    setGarageFinancialReportError(null)

    try {
      const report = await financeClient.getGarageBalanceHistory(auth.accessToken, row.id, filters)
      setGarageFinancialReport(report)
    } catch (error) {
      setGarageFinancialReportError(error instanceof Error ? error.message : 'Не удалось загрузить финансовый отчет гаража.')
      setGarageFinancialReport(null)
    } finally {
      setGarageFinancialReportLoading(false)
    }
  }

  function openGarageFinancialReport(row: ContractorGarageRow) {
    setGarageContextMenu(null)
    const filters = createDefaultGarageBalanceHistoryFilters()
    setGarageFinancialReportTarget(row)
    setGarageFinancialReportFilters(filters)
    setGarageFinancialReport(null)
    setGarageFinancialReportError(null)
    void loadGarageFinancialReport(row, filters)
  }

  function closeGarageFinancialReport() {
    setGarageFinancialReportTarget(null)
    setGarageFinancialReport(null)
    setGarageFinancialReportError(null)
    setGarageFinancialReportLoading(false)
  }

  async function loadContractorFinancialReport(target = contractorFinancialReportTarget, filters = contractorFinancialReportFilters) {
    if (!target) {
      return
    }

    if (!isBackendDictionaryId(target.row.id)) {
      setContractorFinancialReport(null)
      setContractorFinancialReportError('Финансовый отчет доступен для записи, сохраненной в справочнике.')
      return
    }

    setContractorFinancialReportLoading(true)
    setContractorFinancialReportError(null)

    try {
      const operationsPage = await financeClient.getOperationsPage(auth.accessToken, {
        monthFrom: filters.monthFrom,
        monthTo: filters.monthTo,
        operationKind: 'expense',
        supplierId: target.type === 'supplier' ? target.row.id : undefined,
        staffMemberId: target.type === 'employee' ? target.row.id : undefined,
        limit: 500,
      })
      const operationEntries = operationsPage.items
        .filter((operation) => !operation.isCanceled)
        .map((operation) => ({
          id: `operation-${operation.id}`,
          accountingMonth: operation.accountingMonth,
          date: operation.operationDate,
          documentNumber: operation.documentNumber ?? '—',
          description: target.type === 'supplier'
            ? operation.expenseTypeName ?? operation.comment ?? 'Выплата поставщику'
            : operation.expenseTypeName ?? operation.comment ?? 'Выплата сотруднику',
          accrualAmount: 0,
          paymentAmount: operation.amount,
        }))

      if (target.type === 'supplier') {
        const accrualsPage = await financeClient.getSupplierAccrualsPage(auth.accessToken, {
          monthFrom: filters.monthFrom,
          monthTo: filters.monthTo,
          supplierId: target.row.id,
          limit: 500,
        })
        const accrualEntries = accrualsPage.items
          .filter((accrual) => !accrual.isCanceled)
          .map((accrual) => ({
            id: `supplier-accrual-${accrual.id}`,
            accountingMonth: accrual.accountingMonth,
            date: accrual.accountingMonth,
            documentNumber: accrual.documentNumber ?? '—',
            description: accrual.expenseTypeName,
            accrualAmount: accrual.amount,
            paymentAmount: 0,
          }))
        setContractorFinancialReport(buildContractorFinancialReport([...accrualEntries, ...operationEntries]))
      } else {
        const staffAccrualEntries = createStaffFinancialReportEntries(target.row, filters.monthFrom, filters.monthTo)
        setContractorFinancialReport(buildContractorFinancialReport([...staffAccrualEntries, ...operationEntries]))
      }
    } catch (error) {
      setContractorFinancialReportError(error instanceof Error ? error.message : 'Не удалось загрузить финансовый отчет контрагента.')
      setContractorFinancialReport(null)
    } finally {
      setContractorFinancialReportLoading(false)
    }
  }

  function openContractorFinancialReport(target: ContractorFinancialReportTarget) {
    setSupplierContextMenu(null)
    setEmployeeContextMenu(null)
    setModal(null)
    const filters = createDefaultGarageBalanceHistoryFilters()
    setContractorFinancialReportTarget(target)
    setContractorFinancialReportFilters(filters)
    setContractorFinancialReport(null)
    setContractorFinancialReportError(null)
    void loadContractorFinancialReport(target, filters)
  }

  function closeContractorFinancialReport() {
    setContractorFinancialReportTarget(null)
    setContractorFinancialReport(null)
    setContractorFinancialReportError(null)
    setContractorFinancialReportLoading(false)
  }

  function openContractorHistoryInAudit(target = contractorFinancialReportTarget) {
    if (!target || !isBackendDictionaryId(target.row.id)) {
      return
    }

    closeContractorFinancialReport()
    onOpenAudit({
      section: 'dictionary',
      entityType: target.type === 'supplier' ? 'supplier' : 'staff_member',
      relatedCounterparty: target.row.id,
    })
  }

  const saveSupplier = async (supplier: ContractorSupplierRow) => {
    const normalizedSupplier = normalizeSupplierPrototype(supplier)
    const currentSupplier = suppliers.find((item) => item.id === normalizedSupplier.id)

    if (normalizedSupplier.service.trim()) {
      setSupplierServices((currentServices) => getSupplierServiceOptions([...currentServices, normalizedSupplier.service]))
    }

    try {
      const groups = [...supplierGroups]
      const group = await resolveSupplierGroup(dictionaryClient, auth.accessToken, groups, normalizedSupplier.service)
      const request = createSupplierRequestFromRow(normalizedSupplier, group.id)
      const savedSupplier = isBackendDictionaryId(normalizedSupplier.id)
        ? await dictionaryClient.updateSupplier(auth.accessToken, normalizedSupplier.id, request)
        : await dictionaryClient.createSupplier(auth.accessToken, request)
      const savedContacts: SupplierContactDto[] = []

      for (const contact of normalizedSupplier.contacts) {
        if (contact.isDeleted) {
          if (isBackendDictionaryId(contact.id)) {
            await dictionaryClient.archiveSupplierContact(auth.accessToken, contact.id, contact.deleteReason?.trim() || 'Контакт удален из карточки поставщика.')
          }

          savedContacts.push({
            id: contact.id,
            supplierId: savedSupplier.id,
            supplierName: savedSupplier.name,
            fullName: contact.fullName,
            position: contact.position || null,
            phone: contact.phone || null,
            email: contact.email || null,
            status: contact.status,
            comment: contact.comment || null,
            isArchived: true,
          })
          continue
        }

        if (!contact.fullName.trim()) {
          continue
        }

        const contactRequest = createSupplierContactRequestFromRow(savedSupplier.id, contact)
        const savedContact = isBackendDictionaryId(contact.id)
          ? await dictionaryClient.updateSupplierContact(auth.accessToken, contact.id, contactRequest)
          : await dictionaryClient.createSupplierContact(auth.accessToken, contactRequest)
        savedContacts.push(savedContact)
      }

      const nextSupplier = createSupplierRowFromDto(savedSupplier, savedContacts)
      setSupplierGroups(groups)
      setSuppliers((currentSuppliers) => {
        if (currentSupplier) {
          return currentSuppliers.map((item) => (item.id === normalizedSupplier.id ? nextSupplier : item))
        }

        return [...currentSuppliers.slice(0, Math.max(0, supplierPage.limit - 1)), nextSupplier]
      })
      if (!currentSupplier) {
        setSupplierPage((currentPage) => ({ ...currentPage, totalCount: currentPage.totalCount + 1 }))
      }
      return
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось сохранить поставщика.')
    }

    if (currentSupplier) {
      setSuppliers((currentSuppliers) => currentSuppliers.map((item) => (item.id === normalizedSupplier.id ? normalizedSupplier : item)))
      return
    }

    setSuppliers((currentSuppliers) => [...currentSuppliers, normalizedSupplier])
  }

  const deleteSupplier = async (supplier: ContractorSupplierRow, reason = 'Поставщик удален из таблицы контрагентов.') => {
    try {
      if (isBackendDictionaryId(supplier.id)) {
        await dictionaryClient.archiveSupplier(auth.accessToken, supplier.id, reason)
      }
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось удалить поставщика.')
      return
    }

    setSuppliers((currentSuppliers) => currentSuppliers.map((item) => (item.id === supplier.id ? { ...item, isDeleted: true } : item)))
  }

  function openSupplierContextMenu(event: MouseEvent<HTMLDivElement>, row: ContractorSupplierRow) {
    event.preventDefault()
    setSupplierContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  function openSupplierEditor(row: ContractorSupplierRow) {
    setSupplierContextMenu(null)
    setModal({ type: 'supplier', item: row })
  }

  function openSupplierDeleteDialog(row: ContractorSupplierRow) {
    setSupplierContextMenu(null)
    setSupplierDeleteTarget(row)
    setSupplierDeleteReason('')
  }

  function closeSupplierDeleteDialog() {
    setSupplierDeleteTarget(null)
    setSupplierDeleteReason('')
  }

  function confirmSupplierDeleteFromTable() {
    if (!supplierDeleteTarget || !supplierDeleteReason.trim()) {
      return
    }

    void deleteSupplier(supplierDeleteTarget, supplierDeleteReason.trim())
    closeSupplierDeleteDialog()
  }

  function restoreSupplier(row: ContractorSupplierRow) {
    setSupplierContextMenu(null)
    setRestoreTarget({ type: 'supplier', item: row })
  }

  function openSupplierFinancialReport(row: ContractorSupplierRow) {
    openContractorFinancialReport({ type: 'supplier', row })
  }

  const saveEmployee = async (employee: ContractorStaffRow) => {
    const currentEmployee = staff.find((item) => item.id === employee.id)

    try {
      const department = departments.find((item) => item.name === employee.department)
      let departmentId = department?.id
      if (!departmentId || !isBackendDictionaryId(departmentId)) {
        const savedDepartment = await dictionaryClient.createStaffDepartment(auth.accessToken, { name: employee.department.trim() || 'Без отдела' })
        departmentId = savedDepartment.id
        setDepartments((currentDepartments) => {
          const withoutLocal = department ? currentDepartments.filter((item) => item.id !== department.id) : currentDepartments
          return [...withoutLocal, createStaffDepartmentRowFromDto(savedDepartment)]
        })
      }

      const request = createStaffMemberRequestFromRow(employee, departmentId)
      const savedEmployee = isBackendDictionaryId(employee.id)
        ? await dictionaryClient.updateStaffMember(auth.accessToken, employee.id, request)
        : await dictionaryClient.createStaffMember(auth.accessToken, request)
      const nextEmployee = createStaffRowFromDto(savedEmployee)

      setStaff((currentStaff) => {
        if (currentEmployee) {
          return currentStaff.map((item) => (item.id === employee.id ? nextEmployee : item))
        }

        return [...currentStaff.slice(0, Math.max(0, staffPage.limit - 1)), nextEmployee]
      })
      if (!currentEmployee) {
        setStaffPage((currentPage) => ({ ...currentPage, totalCount: currentPage.totalCount + 1 }))
      }
      return
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось сохранить сотрудника.')
    }

    if (currentEmployee) {
      setStaff((currentStaff) => currentStaff.map((item) => (item.id === employee.id ? employee : item)))
      return
    }

    setStaff((currentStaff) => [...currentStaff, employee])
  }

  const deleteEmployee = async (employee: ContractorStaffRow, reason = 'Сотрудник удален из таблицы контрагентов.') => {
    try {
      if (isBackendDictionaryId(employee.id)) {
        await dictionaryClient.archiveStaffMember(auth.accessToken, employee.id, reason)
      }
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось удалить сотрудника.')
      return
    }

    setStaff((currentStaff) => currentStaff.map((item) => (item.id === employee.id ? { ...item, isDeleted: true } : item)))
  }

  const deleteDepartment = async (department: ContractorDepartmentRow, reason = 'Отдел удален из таблицы контрагентов.') => {
    try {
      if (isBackendDictionaryId(department.id)) {
        await dictionaryClient.archiveStaffDepartment(auth.accessToken, department.id, reason)
      }
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось удалить отдел.')
      return
    }

    setDepartments((currentDepartments) => currentDepartments.map((item) => (item.id === department.id ? { ...item, isDeleted: true } : item)))
  }

  function openEmployeeContextMenu(event: MouseEvent<HTMLDivElement>, row: ContractorStaffRow) {
    event.preventDefault()
    setEmployeeContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  function openEmployeeEditor(row: ContractorStaffRow) {
    setEmployeeContextMenu(null)
    setModal({ type: 'employee', item: row })
  }

  function openEmployeeDeleteDialog(row: ContractorStaffRow) {
    setEmployeeContextMenu(null)
    setEmployeeDeleteTarget(row)
    setEmployeeDeleteReason('')
  }

  function closeEmployeeDeleteDialog() {
    setEmployeeDeleteTarget(null)
    setEmployeeDeleteReason('')
  }

  function confirmEmployeeDeleteFromTable() {
    if (!employeeDeleteTarget || !employeeDeleteReason.trim()) {
      return
    }

    void deleteEmployee(employeeDeleteTarget, employeeDeleteReason.trim())
    closeEmployeeDeleteDialog()
  }

  function openDepartmentDeleteDialog(row: ContractorDepartmentRow) {
    setDepartmentContextMenu(null)
    setDepartmentDeleteTarget(row)
    setDepartmentDeleteReason('')
  }

  function closeDepartmentDeleteDialog() {
    setDepartmentDeleteTarget(null)
    setDepartmentDeleteReason('')
  }

  function confirmDepartmentDeleteFromTable() {
    if (!departmentDeleteTarget || !departmentDeleteReason.trim()) {
      return
    }

    void deleteDepartment(departmentDeleteTarget, departmentDeleteReason.trim())
    closeDepartmentDeleteDialog()
  }

  function restoreEmployee(row: ContractorStaffRow) {
    setEmployeeContextMenu(null)
    setRestoreTarget({ type: 'employee', item: row })
  }

  function restoreDepartment(row: ContractorDepartmentRow) {
    setDepartmentContextMenu(null)
    setRestoreTarget({ type: 'department', item: row })
  }

  function openDepartmentContextMenu(event: MouseEvent<HTMLDivElement>, row: ContractorDepartmentRow) {
    event.preventDefault()
    setDepartmentContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  function openDepartmentEditor(row: ContractorDepartmentRow) {
    setDepartmentContextMenu(null)
    setModal({ type: 'department', item: row })
  }

  function openEmployeeFinancialReport(row: ContractorStaffRow) {
    openContractorFinancialReport({ type: 'employee', row })
  }

  const confirmRestore = async () => {
    if (!restoreTarget) {
      return
    }

    try {
      if (restoreTarget.type === 'garage') {
        if (isBackendDictionaryId(restoreTarget.item.id)) {
          const restoredGarage = await dictionaryClient.restoreGarage(auth.accessToken, restoreTarget.item.id)
          const nextGarage = createGarageRowFromDto(restoredGarage, owners)
          setGarages((currentGarages) => currentGarages.map((item) => (item.id === restoreTarget.item.id ? nextGarage : item)))
        } else {
          setGarages((currentGarages) => currentGarages.map((item) => (item.id === restoreTarget.item.id ? { ...item, isDeleted: false } : item)))
        }
      } else if (restoreTarget.type === 'supplier') {
        if (isBackendDictionaryId(restoreTarget.item.id)) {
          const restoredSupplier = await dictionaryClient.restoreSupplier(auth.accessToken, restoreTarget.item.id)
          const restoredContacts = await dictionaryClient.getSupplierContacts(auth.accessToken, restoredSupplier.id, undefined, contractorsDictionaryListLimit, true)
          const nextSupplier = createSupplierRowFromDto(restoredSupplier, restoredContacts)
          setSuppliers((currentSuppliers) => currentSuppliers.map((item) => (item.id === restoreTarget.item.id ? nextSupplier : item)))
        } else {
          setSuppliers((currentSuppliers) => currentSuppliers.map((item) => (item.id === restoreTarget.item.id ? { ...item, isDeleted: false } : item)))
        }
      } else if (restoreTarget.type === 'department') {
        if (isBackendDictionaryId(restoreTarget.item.id)) {
          const restoredDepartment = await dictionaryClient.restoreStaffDepartment(auth.accessToken, restoreTarget.item.id)
          setDepartments((currentDepartments) => currentDepartments.map((item) => (item.id === restoreTarget.item.id ? createStaffDepartmentRowFromDto(restoredDepartment) : item)))
        } else {
          setDepartments((currentDepartments) => currentDepartments.map((item) => (item.id === restoreTarget.item.id ? { ...item, isDeleted: false } : item)))
        }
      } else if (isBackendDictionaryId(restoreTarget.item.id)) {
        const restoredEmployee = await dictionaryClient.restoreStaffMember(auth.accessToken, restoreTarget.item.id)
        setStaff((currentStaff) => currentStaff.map((item) => (item.id === restoreTarget.item.id ? createStaffRowFromDto(restoredEmployee) : item)))
      } else {
        setStaff((currentStaff) => currentStaff.map((item) => (item.id === restoreTarget.item.id ? { ...item, isDeleted: false } : item)))
      }
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось восстановить запись.')
      return
    }

    setRestoreTarget(null)
  }

  const saveDepartment = async (department: ContractorDepartmentRow) => {
    const currentDepartment = departments.find((item) => item.id === department.id)
    const normalizedName = department.name.trim() || 'Новый отдел'

    try {
      const savedDepartment = currentDepartment && isBackendDictionaryId(department.id)
        ? await dictionaryClient.updateStaffDepartment(auth.accessToken, department.id, { name: normalizedName })
        : await dictionaryClient.createStaffDepartment(auth.accessToken, { name: normalizedName })
      const nextDepartment = createStaffDepartmentRowFromDto(savedDepartment)
      setDepartments((currentDepartments) => currentDepartment
        ? currentDepartments.map((item) => (item.id === department.id ? nextDepartment : item))
        : [...currentDepartments, nextDepartment])
      if (currentDepartment && currentDepartment.name !== nextDepartment.name) {
        setStaff((currentStaff) => currentStaff.map((employee) => employee.department === currentDepartment.name
          ? { ...employee, department: nextDepartment.name }
          : employee))
      }
      return
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось сохранить отдел.')
    }

    if (!currentDepartment) {
      const nextDepartment = { id: `department-${Date.now()}`, name: normalizedName }
      setDepartments((currentDepartments) => [...currentDepartments, nextDepartment])
    }
  }

  const saveService = (serviceName: string) => {
    setSupplierServices((currentServices) => getSupplierServiceOptions([...currentServices, serviceName]))
  }

  const changeContractorSort = (section: ContractorSortableSection, key: ContractorSortKey) => {
    const nextSort: ContractorSortState = contractorSort.section === section && contractorSort.key === key
      ? { ...contractorSort, direction: contractorSort.direction === 'asc' ? 'desc' : 'asc' }
      : { section, key, direction: 'asc' }
    setContractorSort(nextSort)
    if (section === 'garages' && isGarageServerSortKey(key)) {
      void loadGaragePage(0, garagePage.limit, nextSort)
    } else if (section === 'staff') {
      void loadStaffPage(0, staffPage.limit, nextSort)
    } else if (section === 'suppliers' && isSupplierServerSortKey(key)) {
      void loadSupplierPage(0, supplierPage.limit, nextSort)
    }
  }

  const renderContractorSortHeader = (section: ContractorSortableSection, key: ContractorSortKey, label: string) => {
    const isActiveSort = contractorSort.section === section && contractorSort.key === key
    const indicator = isActiveSort ? (contractorSort.direction === 'asc' ? '↑' : '↓') : ''

    return (
      <button
        className="ghost-button contractors-sort-button"
        type="button"
        title={`Сортировать: ${label}`}
        aria-pressed={isActiveSort}
        onClick={() => changeContractorSort(section, key)}
      >
        <span>{label}</span>
        <span className="contractors-sort-indicator" aria-hidden="true">{indicator}</span>
      </button>
    )
  }

  const showGarageDebtorsOnly = debtorFilters.garages
  const showSupplierDebtorsOnly = debtorFilters.suppliers
  const showDebtorsOnly = activeSection === 'suppliers' ? showSupplierDebtorsOnly : activeSection === 'garages' ? showGarageDebtorsOnly : false
  const toggleDebtorsFilter = (section: ContractorDebtorFilterSection) => {
    setDebtorFilters((currentFilters) => ({ ...currentFilters, [section]: !currentFilters[section] }))
  }

  const filteredGarages = showGarageDebtorsOnly
    ? garages.filter((garage) => !garage.isDeleted && isContractorMoneyDebt(garage.overdueDebt))
    : garages
  const filteredSuppliers = showSupplierDebtorsOnly
    ? suppliers.filter((supplier) => !supplier.isDeleted && isContractorMoneyDebt(supplier.debt))
    : suppliers

  const visibleGarages = useMemo(() => {
    const rows = [...filteredGarages]
    if (contractorSort.section !== 'garages') {
      return rows
    }

    const sortKey = contractorSort.key as Exclude<ContractorGarageColumnKey, 'actions'>
    return rows.sort((left, right) => applyContractorSortDirection(compareContractorGarages(left, right, sortKey), contractorSort.direction))
  }, [filteredGarages, contractorSort])

  const visibleSuppliers = useMemo(() => {
    const rows = [...filteredSuppliers]
    if (contractorSort.section !== 'suppliers') {
      return rows
    }

    const sortKey = contractorSort.key as ContractorSupplierSortKey
    return rows.sort((left, right) => applyContractorSortDirection(compareContractorSuppliers(left, right, sortKey), contractorSort.direction))
  }, [filteredSuppliers, contractorSort])

  const visibleStaff = useMemo(() => {
    const rows = [...staff]
    if (contractorSort.section !== 'staff') {
      return rows
    }

    const sortKey = contractorSort.key as ContractorStaffSortKey
    return rows.sort((left, right) => applyContractorSortDirection(compareContractorStaff(left, right, sortKey), contractorSort.direction))
  }, [staff, contractorSort])
  const departmentPage = createClientPage(departments, departmentPageNumber, departmentPageSize)
  const garageVisibleRange = getPageVisibleRange({ ...garagePage, items: garages })
  const supplierVisibleRange = getPageVisibleRange({ ...supplierPage, items: suppliers })
  const staffVisibleRange = getPageVisibleRange({ ...staffPage, items: staff })
  const garageCurrentPage = Math.floor(garagePage.offset / garagePage.limit) + 1
  const supplierCurrentPage = Math.floor(supplierPage.offset / supplierPage.limit) + 1
  const staffCurrentPage = Math.floor(staffPage.offset / staffPage.limit) + 1
  const garageTotalPages = Math.max(1, Math.ceil(garagePage.totalCount / garagePage.limit))
  const supplierTotalPages = Math.max(1, Math.ceil(supplierPage.totalCount / supplierPage.limit))
  const staffTotalPages = Math.max(1, Math.ceil(staffPage.totalCount / staffPage.limit))
  const debtorsButtonLabel = activeSection === 'suppliers'
    ? showDebtorsOnly ? 'Показать всех поставщиков' : 'Показать должников'
    : showDebtorsOnly ? 'Показать все гаражи' : 'Показать должников'
  const contractorFinancialReportTitle = contractorFinancialReportTarget?.type === 'supplier'
    ? contractorFinancialReportTarget.row.name || 'Поставщик без названия'
    : contractorFinancialReportTarget?.row.fullName || 'Сотрудник без ФИО'
  const contractorFinancialReportDescription = contractorFinancialReportTarget?.type === 'supplier'
    ? contractorFinancialReportTarget.row.service || contractorFinancialReportTarget.row.contactPerson || 'Услуга не указана'
    : contractorFinancialReportTarget?.row.department || 'Отдел не указан'
  const contractorFinancialReportDialogTitleId = 'contractor-financial-report-title'
  const contractorFinancialReportDialogDescriptionId = 'contractor-financial-report-description'

  return (
    <section className="contractors-page contractors-page--directory" aria-label="Контрагенты">
      <div className="contractors-heading">
        <div>
          <h1>Контрагенты</h1>
        </div>
        <div className="contractors-actions">
          {activeSection === 'garages' ? (
            <>
              <button className="secondary-button" type="button" onClick={() => toggleDebtorsFilter('garages')}>{debtorsButtonLabel}</button>
              <button className="secondary-button" type="button" onClick={() => setModal({ type: 'garage' })}>Добавить гараж</button>
            </>
          ) : null}
          {activeSection === 'suppliers' ? (
            <>
              <button className="secondary-button" type="button" onClick={() => toggleDebtorsFilter('suppliers')}>{debtorsButtonLabel}</button>
              <button className="secondary-button" type="button" onClick={() => setModal({ type: 'supplier' })}>Добавить поставщика</button>
              <button className="secondary-button" type="button" onClick={() => setModal({ type: 'service' })}>Добавить услугу</button>
            </>
          ) : null}
          {activeSection === 'staff' ? (
            <>
              <button className="secondary-button" type="button" onClick={() => setModal({ type: 'department' })}>Добавить отдел</button>
              <button className="secondary-button" type="button" onClick={() => setModal({ type: 'employee' })}>Добавить сотрудника</button>
            </>
          ) : null}
        </div>
      </div>
      {formStateError ? <FormError>{formStateError}</FormError> : null}

      <div className="contractors-prototype-tabs" role="tablist" aria-label="Разделы контрагентов">
        {Object.entries(contractorSectionLabels).map(([section, label]) => (
          <button type="button" role="tab" aria-selected={activeSection === section} className={activeSection === section ? 'is-active' : ''} onClick={() => setActiveSection(section as ContractorSection)} key={section}>
            {label}
          </button>
        ))}
      </div>

      {activeSection === 'garages' ? (
        <section className="contractors-directory-card" aria-label="Гаражи">
          <div className="contractors-directory-table contractors-directory-table--garages" role="table" aria-label="Гаражи" style={garageTableStyle}>
            <div className="contractors-directory-row contractors-directory-row--header" role="row">
              {contractorGarageColumnDefinitions.map((column) => (
                <span className="contractors-directory-header-cell" role="columnheader" key={column.key}>
                  {column.key === 'actions' ? <span>{column.label}</span> : renderContractorSortHeader('garages', column.key, column.label)}
                  {column.key !== 'actions' ? (
                    <button
                      className="icon-button contractors-column-resizer"
                      type="button"
                      aria-label={`Изменить ширину столбца ${column.label}`}
                      onMouseDown={(event) => resizeGarageColumn(column.key, event)}
                    />
                  ) : null}
                </span>
              ))}
            </div>
            {visibleGarages.map((row) => (
              <div className={row.isDeleted ? 'contractors-directory-row contractors-directory-row--deleted' : 'contractors-directory-row'} role="row" key={row.id} onContextMenu={(event) => openGarageContextMenu(event, row)}>
                <span role="cell" className="contractors-directory-cell--center">{row.number}</span>
                <span role="cell" className="contractors-directory-cell--center">{row.peopleCount}</span>
                <span role="cell" className="contractors-directory-cell--center">{row.floorCount}</span>
                <span role="cell">{row.owner}</span>
                <span role="cell">{row.phone}</span>
                <span role="cell" className={row.overdueDebt ? 'contractors-directory-cell--center money-expense' : 'contractors-directory-cell--center'}>
                  {row.isDeleted ? 'Удален' : row.overdueDebt || 'Нет'}
                </span>
                <span role="cell" className="contractors-row-actions">
                  {row.isDeleted ? (
                    <button className="icon-button" type="button" aria-label={`Восстановить гараж ${row.number}`} title="Восстановить" onClick={() => restoreGarage(row)}>
                      <RotateCcw size={16} />
                    </button>
                  ) : (
                    <>
                      <button className="icon-button" type="button" aria-label={`Изменить гараж ${row.number}`} title="Изменить" onClick={() => openGarageEditor(row)}>
                        <Pencil size={16} />
                      </button>
                      <button className="icon-button" type="button" aria-label={`Открыть финансовый отчет гаража ${row.number}`} title="Финансовый отчет" onClick={() => openGarageFinancialReport(row)}>
                        <FileText size={16} />
                      </button>
                      <button className="icon-button contractors-delete-button" type="button" aria-label={`Удалить гараж ${row.number}`} title="Удалить" onClick={() => openGarageDeleteDialog(row)}>
                        <Trash2 size={16} />
                      </button>
                    </>
                  )}
                </span>
              </div>
            ))}
            {visibleGarages.length === 0 ? (
              <div className="contractors-directory-row contractors-directory-row--empty" role="row">
                <span className="contractors-directory-empty-cell" role="cell">{contractorPageLoading.garages ? 'Загрузка гаражей...' : showGarageDebtorsOnly ? 'Гаражей с задолженностью не найдено.' : 'Гаражи пока не настроены.'}</span>
              </div>
            ) : null}
          </div>
          <div className="dictionary-pagination" role="navigation" aria-label="Пагинация гаражей">
            <span role="status" aria-live="polite">
              {showGarageDebtorsOnly
                ? `Должников на странице: ${visibleGarages.length}. Записи ${garageVisibleRange.from}-${garageVisibleRange.to} из ${garagePage.totalCount}`
                : `Показано ${garageVisibleRange.from}-${garageVisibleRange.to} из ${garagePage.totalCount}`}
            </span>
            <label>
              Строк
              <select aria-label="Количество строк гаражей" value={garagePage.limit} disabled={contractorPageLoading.garages} onChange={(event) => void loadGaragePage(0, Number(event.target.value))}>
                {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <Pagination currentPage={garageCurrentPage} totalPages={garageTotalPages} disabled={contractorPageLoading.garages} showQuickJump onPageChange={(page) => void loadGaragePage((page - 1) * garagePage.limit)} />
          </div>
        </section>
      ) : null}

      {activeSection === 'suppliers' ? (
        <section className="contractors-directory-card" aria-label="Поставщики">
          <div className="contractors-directory-table contractors-directory-table--suppliers" role="table" aria-label="Поставщики" style={supplierTableStyle}>
            <div className="contractors-directory-row contractors-directory-row--header" role="row">
              {contractorSupplierColumnDefinitions.map((column) => (
                <span className="contractors-directory-header-cell" role="columnheader" key={column.key}>
                  {column.key === 'actions' ? <span>{column.label}</span> : renderContractorSortHeader('suppliers', column.key, column.label)}
                  {column.key !== 'actions' ? (
                    <button
                      className="icon-button contractors-column-resizer"
                      type="button"
                      aria-label={`Изменить ширину столбца ${column.label}`}
                      onMouseDown={(event) => resizeSupplierColumn(column.key, event)}
                    />
                  ) : null}
                </span>
              ))}
            </div>
            {visibleSuppliers.map((row) => {
              const primaryContact = getSupplierPrimaryContact(row)
              return (
                <div className={row.isDeleted ? 'contractors-directory-row contractors-directory-row--deleted' : 'contractors-directory-row'} role="row" key={row.id} onContextMenu={(event) => openSupplierContextMenu(event, row)}>
                  <span role="cell">{row.name}</span>
                  <span role="cell">{row.service}</span>
                  <span role="cell">{primaryContact?.fullName ?? row.contactPerson}</span>
                  <span role="cell">{primaryContact?.phone ?? row.phone}</span>
                  <span role="cell">{primaryContact?.email ?? row.email}</span>
                  <span role="cell" className={row.debt ? 'contractors-directory-cell--center money-expense' : 'contractors-directory-cell--center'}>
                    {row.isDeleted ? 'Удален' : row.debt || 'Нет'}
                  </span>
                  <span role="cell" className="contractors-row-actions">
                    {row.isDeleted ? (
                      <button className="icon-button" type="button" aria-label={`Восстановить поставщика ${row.name}`} title="Восстановить" onClick={() => restoreSupplier(row)}>
                        <RotateCcw size={16} />
                      </button>
                    ) : (
                      <>
                        <button className="icon-button" type="button" aria-label={`Изменить поставщика ${row.name}`} title="Изменить" onClick={() => openSupplierEditor(row)}>
                          <Pencil size={16} />
                        </button>
                        <button className="icon-button" type="button" aria-label={`Открыть финансовый отчет поставщика ${row.name}`} title="Финансовый отчет" onClick={() => openSupplierFinancialReport(row)}>
                          <FileText size={16} />
                        </button>
                        <button className="icon-button contractors-delete-button" type="button" aria-label={`Удалить поставщика ${row.name}`} title="Удалить" onClick={() => openSupplierDeleteDialog(row)}>
                          <Trash2 size={16} />
                        </button>
                      </>
                    )}
                  </span>
                </div>
              )
            })}
            {visibleSuppliers.length === 0 ? (
              <div className="contractors-directory-row contractors-directory-row--empty" role="row">
                <span className="contractors-directory-empty-cell" role="cell">{contractorPageLoading.suppliers ? 'Загрузка поставщиков...' : showSupplierDebtorsOnly ? 'Поставщиков с задолженностью не найдено.' : 'Поставщики пока не настроены.'}</span>
              </div>
            ) : null}
          </div>
          <div className="dictionary-pagination" role="navigation" aria-label="Пагинация поставщиков">
            <span role="status" aria-live="polite">
              {showSupplierDebtorsOnly
                ? `Должников на странице: ${visibleSuppliers.length}. Записи ${supplierVisibleRange.from}-${supplierVisibleRange.to} из ${supplierPage.totalCount}`
                : `Показано ${supplierVisibleRange.from}-${supplierVisibleRange.to} из ${supplierPage.totalCount}`}
            </span>
            <label>
              Строк
              <select aria-label="Количество строк поставщиков" value={supplierPage.limit} disabled={contractorPageLoading.suppliers} onChange={(event) => void loadSupplierPage(0, Number(event.target.value))}>
                {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <Pagination currentPage={supplierCurrentPage} totalPages={supplierTotalPages} disabled={contractorPageLoading.suppliers} showQuickJump onPageChange={(page) => void loadSupplierPage((page - 1) * supplierPage.limit)} />
          </div>
        </section>
      ) : null}

      {activeSection === 'staff' ? (
        <>
          <section className="contractors-directory-card" aria-label="Отделы персонала">
            <div className="contractors-directory-card-header">
              <h2>Отделы</h2>
            </div>
            <div className="contractors-directory-table contractors-directory-table--departments" role="table" aria-label="Отделы персонала">
              <div className="contractors-directory-row contractors-directory-row--header" role="row">
                <span className="contractors-directory-header-cell" role="columnheader">Отдел</span>
                <span className="contractors-directory-header-cell" role="columnheader">Статус</span>
                <span className="contractors-directory-header-cell" role="columnheader">Действия</span>
              </div>
              {departmentPage.items.map((department) => (
                <div className={department.isDeleted ? 'contractors-directory-row contractors-directory-row--deleted' : 'contractors-directory-row'} role="row" key={department.id} onContextMenu={(event) => openDepartmentContextMenu(event, department)}>
                  <span role="cell">{department.name}</span>
                  <span role="cell" className="contractors-directory-cell--center">{department.isDeleted ? 'Удален' : 'Активен'}</span>
                  <span role="cell" className="contractors-row-actions">
                    {department.isDeleted ? (
                      <button className="icon-button" type="button" aria-label={`Восстановить отдел ${department.name}`} title="Восстановить" onClick={() => restoreDepartment(department)}>
                        <RotateCcw size={16} />
                      </button>
                    ) : (
                      <>
                        <button className="icon-button" type="button" aria-label={`Изменить отдел ${department.name}`} title="Изменить" onClick={() => openDepartmentEditor(department)}>
                          <Pencil size={16} />
                        </button>
                        <button className="icon-button contractors-delete-button" type="button" aria-label={`Удалить отдел ${department.name}`} title="Удалить" onClick={() => openDepartmentDeleteDialog(department)}>
                          <Trash2 size={16} />
                        </button>
                      </>
                    )}
                  </span>
                </div>
              ))}
              {departments.length === 0 ? (
                <div className="contractors-directory-row contractors-directory-row--empty" role="row">
                  <span className="contractors-directory-empty-cell" role="cell">Отделы пока не настроены.</span>
                </div>
              ) : null}
            </div>
            <TablePagination
              ariaLabel="Пагинация отделов"
              totalCount={departmentPage.totalCount}
              offset={departmentPage.offset}
              limit={departmentPage.limit}
              visibleCount={departmentPage.items.length}
              pageSizeLabel="Количество строк отделов"
              onPageChange={setDepartmentPageNumber}
              onPageSizeChange={(limit) => {
                setDepartmentPageNumber(1)
                setDepartmentPageSize(limit)
              }}
            />
          </section>

          <section className="contractors-directory-card" aria-label="Персонал">
            <div className="contractors-directory-table contractors-directory-table--staff" role="table" aria-label="Персонал" style={staffTableStyle}>
              <div className="contractors-directory-row contractors-directory-row--header" role="row">
                {contractorStaffColumnDefinitions.map((column) => (
                  <span className="contractors-directory-header-cell" role="columnheader" key={column.key}>
                    {column.key === 'actions' ? <span>{column.label}</span> : renderContractorSortHeader('staff', column.key, column.label)}
                    {column.key !== 'actions' ? (
                      <button
                        className="icon-button contractors-column-resizer"
                        type="button"
                        aria-label={`Изменить ширину столбца ${column.label}`}
                        onMouseDown={(event) => resizeStaffColumn(column.key, event)}
                      />
                    ) : null}
                  </span>
                ))}
              </div>
              {visibleStaff.map((row) => (
                <div className={row.isDeleted ? 'contractors-directory-row contractors-directory-row--deleted' : 'contractors-directory-row'} role="row" key={row.id} onContextMenu={(event) => openEmployeeContextMenu(event, row)}>
                  <span role="cell">{row.fullName}</span>
                  <span role="cell">{row.department}</span>
                  <span role="cell">{row.isDeleted ? 'Удален' : row.rate}</span>
                  <span role="cell" className="contractors-row-actions">
                    {row.isDeleted ? (
                      <button className="icon-button" type="button" aria-label={`Восстановить сотрудника ${row.fullName}`} title="Восстановить" onClick={() => restoreEmployee(row)}>
                        <RotateCcw size={16} />
                      </button>
                    ) : (
                      <>
                        <button className="icon-button" type="button" aria-label={`Изменить сотрудника ${row.fullName}`} title="Изменить" onClick={() => openEmployeeEditor(row)}>
                          <Pencil size={16} />
                        </button>
                        <button className="icon-button" type="button" aria-label={`Открыть финансовый отчет сотрудника ${row.fullName}`} title="Финансовый отчет" onClick={() => openEmployeeFinancialReport(row)}>
                          <FileText size={16} />
                        </button>
                        <button className="icon-button contractors-delete-button" type="button" aria-label={`Удалить сотрудника ${row.fullName}`} title="Удалить" onClick={() => openEmployeeDeleteDialog(row)}>
                          <Trash2 size={16} />
                        </button>
                      </>
                    )}
                  </span>
                </div>
              ))}
              {visibleStaff.length === 0 ? (
                <div className="contractors-directory-row contractors-directory-row--empty" role="row">
                  <span className="contractors-directory-empty-cell" role="cell">{contractorPageLoading.staff ? 'Загрузка персонала...' : 'Сотрудники пока не настроены.'}</span>
                </div>
              ) : null}
            </div>
            <div className="dictionary-pagination" role="navigation" aria-label="Пагинация персонала">
              <span role="status" aria-live="polite">Показано {staffVisibleRange.from}-{staffVisibleRange.to} из {staffPage.totalCount}</span>
              <label>
                Строк
                <select aria-label="Количество строк персонала" value={staffPage.limit} disabled={contractorPageLoading.staff} onChange={(event) => void loadStaffPage(0, Number(event.target.value))}>
                  {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
                </select>
              </label>
              <Pagination currentPage={staffCurrentPage} totalPages={staffTotalPages} disabled={contractorPageLoading.staff} showQuickJump onPageChange={(page) => void loadStaffPage((page - 1) * staffPage.limit)} />
            </div>
          </section>
        </>
      ) : null}

      {garageContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setGarageContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия гаража ${garageContextMenu.row.number}`}
            style={{ left: garageContextMenu.x, top: garageContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {garageContextMenu.row.isDeleted ? (
              <button type="button" role="menuitem" onClick={() => restoreGarage(garageContextMenu.row)}>
                <RotateCcw size={16} />
                <span>Восстановить</span>
              </button>
            ) : (
              <>
                <button type="button" role="menuitem" onClick={() => openGarageEditor(garageContextMenu.row)}>
                  <Pencil size={16} />
                  <span>Изменить</span>
                </button>
                <button type="button" role="menuitem" onClick={() => openGarageFinancialReport(garageContextMenu.row)}>
                  <FileText size={16} />
                  <span>Открыть финансовый отчет</span>
                </button>
                <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openGarageDeleteDialog(garageContextMenu.row)}>
                  <Trash2 size={16} />
                  <span>Удалить</span>
                </button>
              </>
            )}
          </div>
        </div>
      ) : null}

      {supplierContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setSupplierContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия поставщика ${supplierContextMenu.row.name}`}
            style={{ left: supplierContextMenu.x, top: supplierContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {supplierContextMenu.row.isDeleted ? (
              <button type="button" role="menuitem" onClick={() => restoreSupplier(supplierContextMenu.row)}>
                <RotateCcw size={16} />
                <span>Восстановить</span>
              </button>
            ) : (
              <>
                <button type="button" role="menuitem" onClick={() => openSupplierEditor(supplierContextMenu.row)}>
                  <Pencil size={16} />
                  <span>Изменить</span>
                </button>
                <button type="button" role="menuitem" onClick={() => openSupplierFinancialReport(supplierContextMenu.row)}>
                  <FileText size={16} />
                  <span>Открыть финансовый отчет</span>
                </button>
                <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openSupplierDeleteDialog(supplierContextMenu.row)}>
                  <Trash2 size={16} />
                  <span>Удалить</span>
                </button>
              </>
            )}
          </div>
        </div>
      ) : null}

      {employeeContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setEmployeeContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия сотрудника ${employeeContextMenu.row.fullName}`}
            style={{ left: employeeContextMenu.x, top: employeeContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {employeeContextMenu.row.isDeleted ? (
              <button type="button" role="menuitem" onClick={() => restoreEmployee(employeeContextMenu.row)}>
                <RotateCcw size={16} />
                <span>Восстановить</span>
              </button>
            ) : (
              <>
                <button type="button" role="menuitem" onClick={() => openEmployeeEditor(employeeContextMenu.row)}>
                  <Pencil size={16} />
                  <span>Изменить</span>
                </button>
                <button type="button" role="menuitem" onClick={() => openEmployeeFinancialReport(employeeContextMenu.row)}>
                  <FileText size={16} />
                  <span>Открыть финансовый отчет</span>
                </button>
                <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openEmployeeDeleteDialog(employeeContextMenu.row)}>
                  <Trash2 size={16} />
                  <span>Удалить</span>
                </button>
              </>
            )}
          </div>
        </div>
      ) : null}

      {departmentContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setDepartmentContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия отдела ${departmentContextMenu.row.name}`}
            style={{ left: departmentContextMenu.x, top: departmentContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {departmentContextMenu.row.isDeleted ? (
              <button type="button" role="menuitem" onClick={() => restoreDepartment(departmentContextMenu.row)}>
                <RotateCcw size={16} />
                <span>Восстановить</span>
              </button>
            ) : (
              <>
                <button type="button" role="menuitem" onClick={() => openDepartmentEditor(departmentContextMenu.row)}>
                  <Pencil size={16} />
                  <span>Изменить</span>
                </button>
                <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openDepartmentDeleteDialog(departmentContextMenu.row)}>
                  <Trash2 size={16} />
                  <span>Удалить</span>
                </button>
              </>
            )}
          </div>
        </div>
      ) : null}

      {modal?.type === 'garage' ? <GaragePrototypeDialog item={modal.item} onClose={() => setModal(null)} onSave={saveGarage} onOpenFinancialReport={openGarageFinancialReport} /> : null}
      {modal?.type === 'supplier' ? <SupplierPrototypeDialog accessToken={auth.accessToken} integrationClient={integrationClient} item={modal.item} services={supplierServices} onClose={() => setModal(null)} onOpenFinancialReport={openSupplierFinancialReport} onSave={saveSupplier} /> : null}
      {modal?.type === 'service' ? <ContractorServicePrototypeDialog onClose={() => setModal(null)} onSave={saveService} /> : null}
      {modal?.type === 'employee' ? <EmployeePrototypeDialog departments={departments} item={modal.item} onClose={() => setModal(null)} onOpenFinancialReport={openEmployeeFinancialReport} onSave={saveEmployee} /> : null}
      {modal?.type === 'department' ? <DepartmentPrototypeDialog item={modal.item} onClose={() => setModal(null)} onSave={saveDepartment} /> : null}

      {garageFinancialReportTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeGarageFinancialReport}>
          <section ref={garageFinancialReportDialogRef} className="detail-dialog garage-balance-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-garage-report-title" aria-describedby="contractor-garage-report-owner" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Финансовый отчет</p>
                <h3 id="contractor-garage-report-title">Гараж {garageFinancialReportTarget.number || 'без номера'}</h3>
                <p id="contractor-garage-report-owner">{garageFinancialReportTarget.owner || 'Владелец не указан'}</p>
              </div>
              <button ref={garageFinancialReportCloseRef} className="icon-button" type="button" aria-label="Закрыть финансовый отчет гаража" onClick={closeGarageFinancialReport}>
                <X size={18} />
              </button>
            </div>
            <form className="balance-history-filters" onSubmit={(event) => {
              event.preventDefault()
              void loadGarageFinancialReport()
            }}>
              <label>
                Период с
                <input aria-label="Начало периода финансового отчета гаража" type="month" value={garageFinancialReportFilters.monthFrom} onChange={(event) => setGarageFinancialReportFilters((value) => ({ ...value, monthFrom: event.target.value }))} required />
              </label>
              <label>
                Период по
                <input aria-label="Конец периода финансового отчета гаража" type="month" value={garageFinancialReportFilters.monthTo} onChange={(event) => setGarageFinancialReportFilters((value) => ({ ...value, monthTo: event.target.value }))} required />
              </label>
              <button className="secondary-button" type="submit" disabled={garageFinancialReportLoading}>
                <Search size={16} />
                <span>{garageFinancialReportLoading ? 'Загружаем...' : 'Показать'}</span>
              </button>
            </form>
            {garageFinancialReportError ? <FormError>{garageFinancialReportError}</FormError> : null}
            {garageFinancialReport ? (
              <>
                <div className="balance-history-summary" aria-label="Итоги финансового отчета гаража">
                  <div>
                    <span>Старт</span>
                    <strong>{formatMoney(garageFinancialReport.startingBalance)}</strong>
                  </div>
                  <div>
                    <span>Начислено</span>
                    <strong>{formatMoney(garageFinancialReport.accrualTotal)}</strong>
                  </div>
                  <div>
                    <span>Поступило</span>
                    <strong>{formatMoney(garageFinancialReport.incomeTotal)}</strong>
                  </div>
                  <div>
                    <span>{formatDebtLabel(garageFinancialReport.debt)}</span>
                    <strong className={getDebtClassName(garageFinancialReport.debt)}>{formatDebtAmount(garageFinancialReport.debt)}</strong>
                  </div>
                </div>
                <div className="dictionary-table-scroll garage-balance-table-scroll">
                  <table className="dictionary-data-table" aria-label="Финансовый отчет гаража">
                    <thead>
                      <tr>
                        <th>Месяц</th>
                        <th>Долг на начало</th>
                        <th>Начислено</th>
                        <th>Поступило</th>
                        <th>Долг на конец</th>
                      </tr>
                    </thead>
                    <tbody>
                      {garageFinancialReport.rows.map((row) => (
                        <tr key={row.accountingMonth}>
                          <td>{formatMonth(row.accountingMonth)}</td>
                          <td className={getDebtClassName(row.openingDebt)}>{formatDebtAmount(row.openingDebt)}</td>
                          <td className="money-accrual">{formatMoney(row.accrualAmount)}</td>
                          <td className="money-income">{formatMoney(row.incomeAmount)}</td>
                          <td className={getDebtClassName(row.closingDebt)}>{formatDebtAmount(row.closingDebt)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {garageFinancialReport.rows.length === 0 ? <p className="empty-state" role="status" aria-live="polite">По выбранному периоду строк нет</p> : null}
                </div>
              </>
            ) : null}
          </section>
        </div>
      ) : null}

      {contractorFinancialReportTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeContractorFinancialReport}>
          <section ref={contractorFinancialReportDialogRef} className="detail-dialog garage-balance-dialog" role="dialog" aria-modal="true" aria-labelledby={contractorFinancialReportDialogTitleId} aria-describedby={contractorFinancialReportDialogDescriptionId} onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Финансовый отчет</p>
                <h3 id={contractorFinancialReportDialogTitleId}>{contractorFinancialReportTitle}</h3>
                <p id={contractorFinancialReportDialogDescriptionId}>{contractorFinancialReportDescription}</p>
              </div>
              <button ref={contractorFinancialReportCloseRef} className="icon-button" type="button" aria-label="Закрыть финансовый отчет контрагента" onClick={closeContractorFinancialReport}>
                <X size={18} />
              </button>
            </div>
            <form className="balance-history-filters" onSubmit={(event) => {
              event.preventDefault()
              void loadContractorFinancialReport()
            }}>
              <label>
                Период с
                <input aria-label="Начало периода финансового отчета контрагента" type="month" value={contractorFinancialReportFilters.monthFrom} onChange={(event) => setContractorFinancialReportFilters((value) => ({ ...value, monthFrom: event.target.value }))} required />
              </label>
              <label>
                Период по
                <input aria-label="Конец периода финансового отчета контрагента" type="month" value={contractorFinancialReportFilters.monthTo} onChange={(event) => setContractorFinancialReportFilters((value) => ({ ...value, monthTo: event.target.value }))} required />
              </label>
              <button className="secondary-button" type="submit" disabled={contractorFinancialReportLoading}>
                <Search size={16} />
                <span>{contractorFinancialReportLoading ? 'Загружаем...' : 'Показать'}</span>
              </button>
            </form>
            {contractorFinancialReportError ? <FormError>{contractorFinancialReportError}</FormError> : null}
            {contractorFinancialReport ? (
              <>
                <div className="balance-history-summary" aria-label="Итоги финансового отчета контрагента">
                  <div>
                    <span>Начислено</span>
                    <strong>{formatMoney(contractorFinancialReport.accrualTotal)}</strong>
                  </div>
                  <div>
                    <span>Оплачено</span>
                    <strong>{formatMoney(contractorFinancialReport.paymentTotal)}</strong>
                  </div>
                  <div>
                    <span>{formatDebtLabel(contractorFinancialReport.balance)}</span>
                    <strong className={getDebtClassName(contractorFinancialReport.balance)}>{formatDebtAmount(contractorFinancialReport.balance)}</strong>
                  </div>
                  <div>
                    <span>Строк</span>
                    <strong>{contractorFinancialReport.rows.length}</strong>
                  </div>
                </div>
                <div className="dictionary-table-scroll garage-balance-table-scroll">
                  <table className="dictionary-data-table" aria-label={contractorFinancialReportTarget.type === 'supplier' ? 'Финансовый отчет поставщика' : 'Финансовый отчет сотрудника'}>
                    <thead>
                      <tr>
                        <th>Месяц</th>
                        <th>Дата</th>
                        <th>Документ</th>
                        <th>Операция</th>
                        <th>Начислено</th>
                        <th>Оплачено</th>
                        <th>Остаток</th>
                      </tr>
                    </thead>
                    <tbody>
                      {contractorFinancialReport.rows.map((row) => (
                        <tr key={row.id}>
                          <td>{formatMonth(row.accountingMonth)}</td>
                          <td>{formatDateOnly(row.date)}</td>
                          <td>{row.documentNumber}</td>
                          <td>{row.description}</td>
                          <td className="money-accrual">{row.accrualAmount > 0 ? formatMoney(row.accrualAmount) : '—'}</td>
                          <td className="money-expense">{row.paymentAmount > 0 ? formatMoney(row.paymentAmount) : '—'}</td>
                          <td className={getDebtClassName(row.balanceAfter)}>{formatDebtAmount(row.balanceAfter)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {contractorFinancialReport.rows.length === 0 ? <p className="empty-state" role="status" aria-live="polite">По выбранному периоду строк нет</p> : null}
                </div>
                <section className="contractor-history-section" aria-label="Переход к истории изменений контрагента">
                  <h4>История изменений</h4>
                  {!canReadContractorHistory ? (
                    <p className="empty-state" role="status" aria-live="polite">История изменений доступна пользователям с правом просмотра audit-событий.</p>
                  ) : (
                    <div className="inline-action-row">
                      <p>Откройте общий журнал с фильтром по этому контрагенту.</p>
                      <button className="secondary-button" type="button" onClick={() => openContractorHistoryInAudit()}>
                        <FileText size={16} />
                        <span>Открыть в истории изменений</span>
                      </button>
                    </div>
                  )}
                </section>
              </>
            ) : null}
          </section>
        </div>
      ) : null}

      {restoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setRestoreTarget(null)}>
          <section ref={restoreDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-restore-title" aria-describedby="contractor-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление</p>
                <h3 id="contractor-restore-title">Вернуть запись?</h3>
                <p>{getContractorRestoreTitle(restoreTarget)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение восстановления контрагента" onClick={() => setRestoreTarget(null)}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="contractor-restore-description">Запись снова появится как активная в рабочем списке. Действие записывается в историю изменений.</p>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={restoreCancelRef} className="ghost-button" type="button" onClick={() => setRestoreTarget(null)}>Отмена</button>
              <button className="secondary-button" type="button" onClick={confirmRestore}>
                <RotateCcw size={16} />
                <span>Вернуть запись</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {garageDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeGarageDeleteDialog}>
          <section ref={garageDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="garage-table-delete-title" aria-describedby="garage-table-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="garage-table-delete-title">Удалить гараж?</h3>
                <p>{`Гараж ${garageDeleteTarget.number || 'без номера'}`}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления гаража" onClick={closeGarageDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="garage-table-delete-description">Гараж будет скрыт из рабочего списка, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
            <label className="field-label" htmlFor="garage-table-delete-reason">Причина удаления</label>
            <textarea
              id="garage-table-delete-reason"
              aria-label="Причина удаления гаража"
              maxLength={1000}
              value={garageDeleteReason}
              onChange={(event) => setGarageDeleteReason(event.target.value)}
              placeholder="Например: дубликат карточки"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={garageDeleteCancelRef} className="ghost-button" type="button" onClick={closeGarageDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmGarageDeleteFromTable} disabled={!garageDeleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {supplierDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeSupplierDeleteDialog}>
          <section ref={supplierDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="supplier-table-delete-title" aria-describedby="supplier-table-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="supplier-table-delete-title">Удалить поставщика?</h3>
                <p>{supplierDeleteTarget.name || 'Поставщик без названия'}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления поставщика" onClick={closeSupplierDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="supplier-table-delete-description">Поставщик будет скрыт из рабочего списка, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
            <label className="field-label" htmlFor="supplier-table-delete-reason">Причина удаления</label>
            <textarea
              id="supplier-table-delete-reason"
              aria-label="Причина удаления поставщика"
              maxLength={1000}
              value={supplierDeleteReason}
              onChange={(event) => setSupplierDeleteReason(event.target.value)}
              placeholder="Например: договор больше не действует"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={supplierDeleteCancelRef} className="ghost-button" type="button" onClick={closeSupplierDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmSupplierDeleteFromTable} disabled={!supplierDeleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {employeeDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeEmployeeDeleteDialog}>
          <section ref={employeeDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="employee-table-delete-title" aria-describedby="employee-table-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="employee-table-delete-title">Удалить сотрудника?</h3>
                <p>{employeeDeleteTarget.fullName || 'Сотрудник без имени'}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления сотрудника" onClick={closeEmployeeDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="employee-table-delete-description">Сотрудник будет скрыт из рабочего списка персонала, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
            <label className="field-label" htmlFor="employee-table-delete-reason">Причина удаления</label>
            <textarea
              id="employee-table-delete-reason"
              aria-label="Причина удаления сотрудника"
              maxLength={1000}
              value={employeeDeleteReason}
              onChange={(event) => setEmployeeDeleteReason(event.target.value)}
              placeholder="Например: сотрудник больше не работает"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={employeeDeleteCancelRef} className="ghost-button" type="button" onClick={closeEmployeeDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmEmployeeDeleteFromTable} disabled={!employeeDeleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {departmentDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeDepartmentDeleteDialog}>
          <section ref={departmentDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="department-table-delete-title" aria-describedby="department-table-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="department-table-delete-title">Удалить отдел?</h3>
                <p>{departmentDeleteTarget.name || 'Отдел без названия'}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления отдела" onClick={closeDepartmentDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="department-table-delete-description">Отдел будет скрыт из рабочего списка персонала, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
            <label className="field-label" htmlFor="department-table-delete-reason">Причина удаления</label>
            <textarea
              id="department-table-delete-reason"
              aria-label="Причина удаления отдела"
              maxLength={1000}
              value={departmentDeleteReason}
              onChange={(event) => setDepartmentDeleteReason(event.target.value)}
              placeholder="Например: отдел больше не используется"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={departmentDeleteCancelRef} className="ghost-button" type="button" onClick={closeDepartmentDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmDepartmentDeleteFromTable} disabled={!departmentDeleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </section>
  )
}

function createEmptyGaragePrototype(): ContractorGarageRow {
  return {
    id: `garage-${Date.now()}`,
    ownerId: null,
    number: '',
    peopleCount: '',
    floorCount: '',
    owner: '',
    phone: '',
    address: '',
    startingBalance: '',
    balance: '',
    overdueDebt: '',
    initialWater: '',
    initialElectricity: '',
    meters: '',
    comment: '',
    isDeleted: false,
  }
}

function createEmptySupplierPrototype(): ContractorSupplierRow {
  return {
    id: `supplier-${Date.now()}`,
    name: '',
    service: '',
    inn: '',
    legalAddress: '',
    contactPerson: '',
    phone: '',
    email: '',
    contacts: [],
    startingBalance: '',
    debt: '',
    comment: '',
    isDeleted: false,
  }
}

function createEmptyEmployeePrototype(department: string): ContractorStaffRow {
  return {
    id: `employee-${Date.now()}`,
    fullName: '',
    department,
    rate: '',
    isDeleted: false,
  }
}

type PrototypeChangeEntry = {
  fieldLabel: string
  previousValue: string
  nextValue: string
}

function createPrototypeChangeEntry(fieldLabel: string, previousValue: string, nextValue: string): PrototypeChangeEntry | null {
  if (previousValue.trim() === nextValue.trim()) {
    return null
  }

  return { fieldLabel, previousValue, nextValue }
}

function compactPrototypeChanges(changes: Array<PrototypeChangeEntry | null>) {
  return changes.filter((change): change is PrototypeChangeEntry => Boolean(change))
}

function getGaragePrototypeChanges(previous: ContractorGarageRow, next: ContractorGarageRow) {
  return compactPrototypeChanges([
    createPrototypeChangeEntry('Номер', previous.number, next.number),
    createPrototypeChangeEntry('Количество человек', previous.peopleCount, next.peopleCount),
    createPrototypeChangeEntry('Этажи', previous.floorCount, next.floorCount),
    createPrototypeChangeEntry('Стартовое значение счетчика воды', previous.initialWater, next.initialWater),
    createPrototypeChangeEntry('Стартовое значение счетчика электричества', previous.initialElectricity, next.initialElectricity),
    createPrototypeChangeEntry('Владелец', previous.owner, next.owner),
    createPrototypeChangeEntry('Телефон', previous.phone, next.phone),
    createPrototypeChangeEntry('Адрес', previous.address, next.address),
    createPrototypeChangeEntry('Счётчики', previous.meters, next.meters),
    createPrototypeChangeEntry('Комментарий', previous.comment, next.comment),
  ])
}

function getSupplierPrototypeChanges(previous: ContractorSupplierRow, next: ContractorSupplierRow) {
  return compactPrototypeChanges([
    createPrototypeChangeEntry('Наименование', previous.name, next.name),
    createPrototypeChangeEntry('Услуга', previous.service, next.service),
    createPrototypeChangeEntry('ИНН', previous.inn, next.inn),
    createPrototypeChangeEntry('Задолженность', previous.debt, next.debt),
    createPrototypeChangeEntry('Юридический адрес', previous.legalAddress, next.legalAddress),
    createPrototypeChangeEntry('Контакты', formatSupplierContactSummary(previous.contacts), formatSupplierContactSummary(next.contacts)),
    createPrototypeChangeEntry('Комментарий', previous.comment, next.comment),
  ])
}

function getEmployeePrototypeChanges(previous: ContractorStaffRow, next: ContractorStaffRow) {
  return compactPrototypeChanges([
    createPrototypeChangeEntry('ФИО', previous.fullName, next.fullName),
    createPrototypeChangeEntry('Отдел', previous.department, next.department),
    createPrototypeChangeEntry('Ставка', previous.rate, next.rate),
  ])
}

function PrototypeChangeConfirmationDialog({
  changes,
  objectName,
  onCancel,
  onConfirm,
  title,
}: {
  changes: PrototypeChangeEntry[]
  objectName: string
  onCancel: () => void
  onConfirm: () => void
  title: string
}) {
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  useEscapeKey(true, onCancel)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onCancel}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="prototype-change-title" aria-describedby="prototype-change-description" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <p className="eyebrow">Изменение</p>
            <h3 id="prototype-change-title">{title}</h3>
            <p>{objectName}</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть подтверждение изменений" onClick={onCancel}>
            <X size={18} />
          </button>
        </div>
        <p className="confirmation-text" id="prototype-change-description">Проверьте, что именно изменится. Действие записывается в историю изменений.</p>
        <dl className="dictionary-change-list">
          {changes.map((change) => (
            <div key={change.fieldLabel}>
              <dt>{change.fieldLabel}</dt>
              <dd>{formatPrototypeChangeValue(change.previousValue)} {'->'} {formatPrototypeChangeValue(change.nextValue)}</dd>
            </div>
          ))}
        </dl>
        <div className="detail-dialog-actions contractors-dialog-actions">
          <button ref={cancelRef} className="ghost-button" type="button" onClick={onCancel}>Отмена</button>
          <button className="secondary-button" type="button" onClick={onConfirm}>
            <Save size={16} />
            <span>Сохранить</span>
          </button>
        </div>
      </section>
    </div>
  )
}

function SupplierContactDeleteConfirmationDialog({
  contact,
  reason,
  cancelRef,
  dialogRef,
  onReasonChange,
  onCancel,
  onConfirm,
}: {
  contact: ContractorSupplierContact
  reason: string
  cancelRef: RefObject<HTMLButtonElement | null>
  dialogRef: RefObject<HTMLElement | null>
  onReasonChange: (reason: string) => void
  onCancel: () => void
  onConfirm: () => void
}) {
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onCancel}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="supplier-contact-delete-title" aria-describedby="supplier-contact-delete-description" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <p className="eyebrow">Удаление</p>
            <h3 id="supplier-contact-delete-title">Удалить контакт?</h3>
            <p>{contact.fullName || 'Контакт без ФИО'}</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления контакта" onClick={onCancel}>
            <X size={18} />
          </button>
        </div>
        <p className="confirmation-text" id="supplier-contact-delete-description">Контакт будет скрыт в карточке поставщика, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
        <label className="field-label" htmlFor="supplier-contact-delete-reason">Причина удаления</label>
        <textarea
          id="supplier-contact-delete-reason"
          aria-label="Причина удаления контакта"
          maxLength={1000}
          value={reason}
          onChange={(event) => onReasonChange(event.target.value)}
          placeholder="Например: контакт больше не работает у поставщика"
          required
        />
        <div className="detail-dialog-actions contractors-dialog-actions">
          <button className="secondary-button danger-button" type="button" onClick={onConfirm} disabled={!reason.trim()}>
            <Trash2 size={16} />
            <span>Удалить</span>
          </button>
          <button ref={cancelRef} className="ghost-button" type="button" onClick={onCancel}>Отмена</button>
        </div>
      </section>
    </div>
  )
}

function SupplierContactRestoreConfirmationDialog({
  contact,
  cancelRef,
  dialogRef,
  onCancel,
  onConfirm,
}: {
  contact: ContractorSupplierContact
  cancelRef: RefObject<HTMLButtonElement | null>
  dialogRef: RefObject<HTMLElement | null>
  onCancel: () => void
  onConfirm: () => void
}) {
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onCancel}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="supplier-contact-restore-title" aria-describedby="supplier-contact-restore-description" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <p className="eyebrow">Восстановление</p>
            <h3 id="supplier-contact-restore-title">Восстановить контакт?</h3>
            <p>{contact.fullName || 'Контакт без ФИО'}</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть подтверждение восстановления контакта" onClick={onCancel}>
            <X size={18} />
          </button>
        </div>
        <p className="confirmation-text" id="supplier-contact-restore-description">Контакт снова станет активным. Если поставщик был скрыт, он тоже будет восстановлен после сохранения карточки.</p>
        <div className="detail-dialog-actions contractors-dialog-actions">
          <button className="secondary-button" type="button" onClick={onConfirm}>
            <RotateCcw size={16} />
            <span>Восстановить</span>
          </button>
          <button ref={cancelRef} className="ghost-button" type="button" onClick={onCancel}>Отмена</button>
        </div>
      </section>
    </div>
  )
}

function GaragePrototypeDialog({ item, onClose, onOpenFinancialReport, onSave }: { item?: ContractorGarageRow; onClose: () => void; onOpenFinancialReport: (item: ContractorGarageRow) => void; onSave: (item: ContractorGarageRow) => void }) {
  const [form, setForm] = useState<ContractorGarageRow>(item ?? createEmptyGaragePrototype())
  const [saveChanges, setSaveChanges] = useState<PrototypeChangeEntry[]>([])
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(saveChanges.length === 0)
  useEscapeKey(saveChanges.length === 0, onClose)

  function saveAndClose() {
    onSave(form)
    setSaveChanges([])
    onClose()
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!item) {
      saveAndClose()
      return
    }

    const changes = getGaragePrototypeChanges(item, form)
    if (changes.length === 0) {
      onClose()
      return
    }

    setSaveChanges(changes)
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-dialog--wide contractors-dialog--garage" role="dialog" aria-modal="true" aria-labelledby="garage-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="garage-dialog-title">{item ? `Гараж ${item.number}` : 'Новый гараж'}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму гаража" onClick={onClose}><X size={18} /></button>
          </div>
          <form className="dictionary-modal-form contractors-modal-form" onSubmit={handleSubmit}>
            <div className="contractors-garage-form-columns">
              <div className="contractors-garage-form-column" role="group" aria-label="Основные сведения о гараже">
                <FormField label="Номер"><input aria-label="Номер гаража" value={form.number} onChange={(event) => setForm({ ...form, number: event.target.value })} /></FormField>
                <FormField label="Количество человек"><input aria-label="Количество человек" value={form.peopleCount} onChange={(event) => setForm({ ...form, peopleCount: event.target.value })} /></FormField>
                <FormField label="Этажи"><input aria-label="Этажи гаража" value={form.floorCount} onChange={(event) => setForm({ ...form, floorCount: event.target.value })} /></FormField>
              </div>
              <div className="contractors-garage-form-column contractors-garage-form-column--financial" role="group" aria-label="Финансовые показатели гаража">
                <FormField label="Баланс"><input aria-label="Баланс гаража" value={form.balance || '0'} readOnly /></FormField>
                <FormField label="Просроченная задолженность"><input aria-label="Просроченная задолженность гаража" value={form.overdueDebt || 'Нет'} readOnly /></FormField>
                <FormField label="Старт. зн. сч. за воду"><input aria-label="Стартовое значение счетчика воды" value={form.initialWater} onChange={(event) => setForm({ ...form, initialWater: event.target.value })} /></FormField>
                <FormField label="Старт. зн. сч. за эл-во"><input aria-label="Стартовое значение счетчика электричества" value={form.initialElectricity} onChange={(event) => setForm({ ...form, initialElectricity: event.target.value })} /></FormField>
              </div>
            </div>
            <FormField label="Владелец"><input aria-label="Владелец гаража" value={form.owner} onChange={(event) => setForm({ ...form, owner: event.target.value })} /></FormField>
            <FormField label="Телефон"><input aria-label="Телефон владельца гаража" value={form.phone} onChange={(event) => setForm({ ...form, phone: event.target.value })} /></FormField>
            <FormField label="Адрес"><input aria-label="Адрес гаража" value={form.address} onChange={(event) => setForm({ ...form, address: event.target.value })} /></FormField>
            <FormField label="Счётчики"><textarea aria-label="Счетчики гаража" maxLength={1000} value={form.meters} onChange={(event) => setForm({ ...form, meters: event.target.value })} /></FormField>
            <FormField label="Комментарий"><textarea aria-label="Комментарий гаража" value={form.comment} onChange={(event) => setForm({ ...form, comment: event.target.value })} /></FormField>
            <div className="detail-dialog-actions contractors-dialog-actions contractors-garage-actions">
              <button className="secondary-button contractors-report-button" type="button" onClick={() => onOpenFinancialReport(form)}>
                <FileText size={16} />
                <span>Открыть фин. отчет</span>
              </button>
              <button className="secondary-button" type="submit"><Save size={17} /><span>Сохранить</span></button>
              <button className="ghost-button" type="button" onClick={onClose}>Отмена</button>
            </div>
          </form>
        </section>
      </div>

      {item && saveChanges.length > 0 ? (
        <PrototypeChangeConfirmationDialog changes={saveChanges} objectName={`Гараж ${item.number || 'без номера'}`} onCancel={() => setSaveChanges([])} onConfirm={saveAndClose} title="Подтвердить изменения гаража" />
      ) : null}
    </>
  )
}

function getDepartmentPrototypeChanges(previous: ContractorDepartmentRow, next: ContractorDepartmentRow) {
  return compactPrototypeChanges([
    createPrototypeChangeEntry('Наименование', previous.name, next.name),
  ])
}

function SupplierPrototypeDialog({ accessToken, integrationClient, item, services, onClose, onOpenFinancialReport, onSave }: { accessToken: string; integrationClient: IntegrationClient; item?: ContractorSupplierRow; services: string[]; onClose: () => void; onOpenFinancialReport: (item: ContractorSupplierRow) => void; onSave: (item: ContractorSupplierRow) => void }) {
  const [form, setForm] = useState<ContractorSupplierRow>(item ?? { ...createEmptySupplierPrototype(), service: services[0] ?? '' })
  const [saveChanges, setSaveChanges] = useState<PrototypeChangeEntry[]>([])
  const [partySuggestions, setPartySuggestions] = useState<DadataPartySuggestionDto[]>([])
  const [addressSuggestions, setAddressSuggestions] = useState<DadataAddressSuggestionDto[]>([])
  const [partySuggestionsOpen, setPartySuggestionsOpen] = useState(false)
  const [addressSuggestionsOpen, setAddressSuggestionsOpen] = useState(false)
  const [partySuggestionStatus, setPartySuggestionStatus] = useState('')
  const [addressSuggestionStatus, setAddressSuggestionStatus] = useState('')
  const partyRequestSequence = useRef(0)
  const addressRequestSequence = useRef(0)
  const partyInputTouched = useRef(false)
  const addressInputTouched = useRef(false)
  const [contactContextMenu, setContactContextMenu] = useState<{ contact: ContractorSupplierContact; x: number; y: number } | null>(null)
  const [contactDeleteTarget, setContactDeleteTarget] = useState<ContractorSupplierContact | null>(null)
  const [contactDeleteReason, setContactDeleteReason] = useState('')
  const [contactRestoreTarget, setContactRestoreTarget] = useState<ContractorSupplierContact | null>(null)
  useRestoreFocusOnClose(true)
  useRestoreFocusOnClose(Boolean(contactDeleteTarget))
  useRestoreFocusOnClose(Boolean(contactRestoreTarget))
  const dialogRef = useFocusTrap<HTMLElement>(saveChanges.length === 0 && !contactDeleteTarget && !contactRestoreTarget)
  const contactDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(contactDeleteTarget))
  const contactDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(contactDeleteTarget))
  const contactRestoreDialogRef = useFocusTrap<HTMLElement>(Boolean(contactRestoreTarget))
  const contactRestoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(contactRestoreTarget))
  useEscapeKey(saveChanges.length === 0 && !contactDeleteTarget && !contactRestoreTarget, onClose)
  useEscapeKey(Boolean(contactContextMenu), () => setContactContextMenu(null))
  useEscapeKey(Boolean(contactDeleteTarget), () => closeContactDeleteDialog())
  useEscapeKey(Boolean(contactRestoreTarget), () => closeContactRestoreDialog())

  useEffect(() => {
    const query = form.inn.trim()
    const sequence = ++partyRequestSequence.current
    if (!partyInputTouched.current || query.length < 2) {
      return
    }

    const timer = window.setTimeout(() => {
      setPartySuggestionStatus('Ищем организацию...')
      void integrationClient.suggestParties(accessToken, query).then((suggestions) => {
        if (sequence !== partyRequestSequence.current) return
        setPartySuggestions(suggestions)
        setPartySuggestionsOpen(suggestions.length > 0)
        setPartySuggestionStatus(suggestions.length > 0 ? `Найдено вариантов: ${suggestions.length}` : 'Подходящих организаций не найдено. Можно продолжить ввод вручную.')
      }).catch(() => {
        if (sequence !== partyRequestSequence.current) return
        setPartySuggestions([])
        setPartySuggestionsOpen(false)
        setPartySuggestionStatus('Подсказки DaData недоступны. Можно продолжить ввод вручную.')
      })
    }, 350)

    return () => window.clearTimeout(timer)
  }, [accessToken, form.inn, integrationClient])

  useEffect(() => {
    const query = form.legalAddress.trim()
    const sequence = ++addressRequestSequence.current
    if (!addressInputTouched.current || query.length < 2) {
      return
    }

    const timer = window.setTimeout(() => {
      setAddressSuggestionStatus('Ищем адрес...')
      void integrationClient.suggestAddresses(accessToken, query).then((suggestions) => {
        if (sequence !== addressRequestSequence.current) return
        setAddressSuggestions(suggestions)
        setAddressSuggestionsOpen(suggestions.length > 0)
        setAddressSuggestionStatus(suggestions.length > 0 ? `Найдено вариантов: ${suggestions.length}` : 'Подходящих адресов не найдено. Можно продолжить ввод вручную.')
      }).catch(() => {
        if (sequence !== addressRequestSequence.current) return
        setAddressSuggestions([])
        setAddressSuggestionsOpen(false)
        setAddressSuggestionStatus('Подсказки DaData недоступны. Можно продолжить ввод вручную.')
      })
    }, 350)

    return () => window.clearTimeout(timer)
  }, [accessToken, form.legalAddress, integrationClient])

  function saveAndClose() {
    onSave(normalizeSupplierPrototype(form))
    setSaveChanges([])
    onClose()
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!item) {
      saveAndClose()
      return
    }

    const changes = getSupplierPrototypeChanges(item, form)
    if (changes.length === 0) {
      onClose()
      return
    }

    setSaveChanges(changes)
  }

  function addContact() {
    setForm((currentForm) => ({ ...currentForm, contacts: [...currentForm.contacts, createEmptySupplierContact()] }))
  }

  function updateContact(contactId: string, patch: Partial<ContractorSupplierContact>) {
    setForm((currentForm) => ({
      ...currentForm,
      contacts: currentForm.contacts.map((contact) => (contact.id === contactId ? { ...contact, ...patch } : contact)),
    }))
  }

  function openContactContextMenu(event: MouseEvent<HTMLDivElement>, contact: ContractorSupplierContact) {
    event.preventDefault()
    setContactContextMenu({ contact, x: event.clientX, y: event.clientY })
  }

  function requestDeleteContact(contact: ContractorSupplierContact) {
    setContactContextMenu(null)
    setContactDeleteTarget(contact)
    setContactDeleteReason(contact.deleteReason ?? '')
  }

  function closeContactDeleteDialog() {
    setContactDeleteTarget(null)
    setContactDeleteReason('')
  }

  function requestRestoreContact(contact: ContractorSupplierContact) {
    setContactContextMenu(null)
    setContactRestoreTarget(contact)
  }

  function closeContactRestoreDialog() {
    setContactRestoreTarget(null)
  }

  function confirmContactDelete() {
    if (!contactDeleteTarget || !contactDeleteReason.trim()) {
      return
    }

    updateContact(contactDeleteTarget.id, { isDeleted: true, status: 'Не работает', deleteReason: contactDeleteReason.trim() })
    closeContactDeleteDialog()
  }

  function confirmContactRestore() {
    if (!contactRestoreTarget) {
      return
    }

    setForm((currentForm) => ({
      ...currentForm,
      isDeleted: false,
      contacts: currentForm.contacts.map((itemContact) => (itemContact.id === contactRestoreTarget.id ? { ...itemContact, isDeleted: false, status: 'Работает', deleteReason: undefined } : itemContact)),
    }))
    closeContactRestoreDialog()
  }

  const availableServices = getSupplierServiceOptions([...services, form.service])

  function selectPartySuggestion(suggestion: DadataPartySuggestionDto) {
    partyInputTouched.current = false
    addressInputTouched.current = false
    setForm((currentForm) => ({
      ...currentForm,
      name: suggestion.value || currentForm.name,
      inn: suggestion.inn || currentForm.inn,
      legalAddress: suggestion.legalAddress || currentForm.legalAddress,
    }))
    setPartySuggestionsOpen(false)
    setPartySuggestionStatus('Организация выбрана из DaData.')
  }

  function selectAddressSuggestion(suggestion: DadataAddressSuggestionDto) {
    addressInputTouched.current = false
    setForm((currentForm) => ({ ...currentForm, legalAddress: suggestion.unrestrictedValue || suggestion.value }))
    setAddressSuggestionsOpen(false)
    setAddressSuggestionStatus('Адрес выбран из DaData.')
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-dialog--wide contractors-dialog--supplier" role="dialog" aria-modal="true" aria-labelledby="supplier-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="supplier-dialog-title">{item ? form.name : 'Новый поставщик'}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму поставщика" onClick={onClose}><X size={18} /></button>
          </div>
          <form className="dictionary-modal-form contractors-modal-form" onSubmit={handleSubmit}>
            <FormField label="Наименование"><input aria-label="Наименование поставщика" value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} /></FormField>
            <FormField label="Услуга">
              <select aria-label="Услуга поставщика" value={form.service} onChange={(event) => setForm({ ...form, service: event.target.value })}>
                <option value="">Выберите услугу</option>
                {availableServices.map((service) => (
                  <option value={service} key={service}>{service}</option>
                ))}
              </select>
            </FormField>
            <div className="contractors-modal-grid contractors-supplier-lookup-grid">
              <FormField label="ИНН">
                <div className="suggestion-combobox">
                  <input
                    aria-label="ИНН поставщика"
                    role="combobox"
                    aria-autocomplete="list"
                    aria-expanded={partySuggestionsOpen}
                    aria-controls="supplier-party-suggestions"
                    autoComplete="off"
                    value={form.inn}
                    onFocus={() => setPartySuggestionsOpen(partySuggestions.length > 0)}
                    onBlur={() => setPartySuggestionsOpen(false)}
                    onChange={(event) => {
                      const value = event.target.value
                      partyInputTouched.current = true
                      setForm({ ...form, inn: value })
                      if (value.trim().length < 2) {
                        setPartySuggestions([])
                        setPartySuggestionsOpen(false)
                        setPartySuggestionStatus('')
                      }
                    }}
                  />
                  {partySuggestionsOpen ? (
                    <div className="suggestion-options" id="supplier-party-suggestions" role="listbox" aria-label="Организации DaData">
                      {partySuggestions.map((suggestion) => (
                        <button className="ghost-button suggestion-option" type="button" role="option" aria-selected="false" key={`${suggestion.inn ?? ''}-${suggestion.kpp ?? ''}-${suggestion.value}`} onMouseDown={(event) => event.preventDefault()} onClick={() => selectPartySuggestion(suggestion)}>
                          <strong>{suggestion.value}</strong>
                          <span>{[suggestion.inn ? `ИНН ${suggestion.inn}` : null, suggestion.legalAddress].filter(Boolean).join(' · ')}</span>
                        </button>
                      ))}
                    </div>
                  ) : null}
                </div>
                {partySuggestionStatus ? <small className="suggestion-status" role="status" aria-live="polite">{partySuggestionStatus}</small> : null}
              </FormField>
              <FormField label="Задолженность"><input aria-label="Задолженность поставщика" value={form.debt || 'Нет'} readOnly /></FormField>
            </div>
            <FormField label="Юр. адрес">
              <div className="suggestion-combobox">
                <input
                  aria-label="Юридический адрес поставщика"
                  role="combobox"
                  aria-autocomplete="list"
                  aria-expanded={addressSuggestionsOpen}
                  aria-controls="supplier-address-suggestions"
                  autoComplete="off"
                  value={form.legalAddress}
                  onFocus={() => setAddressSuggestionsOpen(addressSuggestions.length > 0)}
                  onBlur={() => setAddressSuggestionsOpen(false)}
                  onChange={(event) => {
                    const value = event.target.value
                    addressInputTouched.current = true
                    setForm({ ...form, legalAddress: value })
                    if (value.trim().length < 2) {
                      setAddressSuggestions([])
                      setAddressSuggestionsOpen(false)
                      setAddressSuggestionStatus('')
                    }
                  }}
                />
                {addressSuggestionsOpen ? (
                  <div className="suggestion-options" id="supplier-address-suggestions" role="listbox" aria-label="Адреса DaData">
                    {addressSuggestions.map((suggestion) => (
                      <button className="ghost-button suggestion-option" type="button" role="option" aria-selected="false" key={`${suggestion.fiasId ?? ''}-${suggestion.value}`} onMouseDown={(event) => event.preventDefault()} onClick={() => selectAddressSuggestion(suggestion)}>
                        <strong>{suggestion.value}</strong>
                        {suggestion.postalCode ? <span>Индекс {suggestion.postalCode}</span> : null}
                      </button>
                    ))}
                  </div>
                ) : null}
              </div>
              {addressSuggestionStatus ? <small className="suggestion-status" role="status" aria-live="polite">{addressSuggestionStatus}</small> : null}
            </FormField>
            <div className="contractors-contacts-toolbar">
              <span>Контакты</span>
              <button className="secondary-button" type="button" onClick={addContact}>Добавить контакт</button>
            </div>
            <div className="contractors-contacts-preview contractors-contacts-preview--editable" role="table" aria-label="Контакты поставщика">
              <div className="contractors-contacts-row contractors-contacts-row--header contractors-contacts-row--editable" role="row">
                <span role="columnheader">№</span>
                <span role="columnheader">ФИО</span>
                <span role="columnheader">Должность</span>
                <span role="columnheader">Телефон</span>
                <span role="columnheader">Почта</span>
                <span role="columnheader">Статус</span>
                <span role="columnheader">Комментарий</span>
              </div>
              {form.contacts.length === 0 ? (
                <div className="contractors-contacts-row contractors-contacts-row--editable contractors-contacts-row--empty" role="row">
                  <span role="cell">Контакты пока не добавлены</span>
                </div>
              ) : form.contacts.map((contact, index) => (
                <div className={contact.isDeleted ? 'contractors-contacts-row contractors-contacts-row--editable contractors-contacts-row--deleted' : 'contractors-contacts-row contractors-contacts-row--editable'} role="row" key={contact.id} onContextMenu={(event) => openContactContextMenu(event, contact)}>
                  <span role="cell">{index + 1}</span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: ФИО`} value={contact.fullName} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { fullName: event.target.value })} /></span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: должность`} value={contact.position} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { position: event.target.value })} /></span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: телефон`} value={contact.phone} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { phone: event.target.value })} /></span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: почта`} value={contact.email} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { email: event.target.value })} /></span>
                  <span role="cell">
                    <select aria-label={`Контакт ${index + 1}: статус`} value={contact.status} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { status: event.target.value as ContractorSupplierContact['status'] })}>
                      <option>Работает</option>
                      <option>Не работает</option>
                    </select>
                  </span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: комментарий`} value={contact.comment} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { comment: event.target.value })} /></span>
                </div>
              ))}
            </div>
            <div className="contractors-modal-grid">
              <FormField label="Телефон"><input aria-label="Телефон поставщика" type="tel" value={form.phone} onChange={(event) => setForm({ ...form, phone: event.target.value })} /></FormField>
              <FormField label="Почта"><input aria-label="Почта поставщика" type="email" value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} /></FormField>
            </div>
            <FormField label="Комментарий"><textarea aria-label="Комментарий поставщика" value={form.comment} onChange={(event) => setForm({ ...form, comment: event.target.value })} /></FormField>
            <div className="detail-dialog-actions contractors-dialog-actions contractors-garage-actions">
              <button className="secondary-button contractors-report-button" type="button" onClick={() => onOpenFinancialReport(form)}>
                <FileText size={16} />
                <span>Открыть фин. отчет</span>
              </button>
              <button className="secondary-button" type="submit"><Save size={17} /><span>Сохранить</span></button>
              <button className="ghost-button" type="button" onClick={onClose}>Отмена</button>
            </div>
          </form>
        </section>
      </div>

      {contactContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setContactContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия контакта ${contactContextMenu.contact.fullName || 'без ФИО'}`}
            style={{ left: contactContextMenu.x, top: contactContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {contactContextMenu.contact.isDeleted ? (
              <>
                <p className="context-menu-hint">При восстановлении контакта будет восстановлен и поставщик.</p>
                <button type="button" role="menuitem" onClick={() => requestRestoreContact(contactContextMenu.contact)}>
                  <RotateCcw size={16} />
                  <span>Восстановить контакт</span>
                </button>
              </>
            ) : (
              <button className="context-menu-danger" type="button" role="menuitem" onClick={() => requestDeleteContact(contactContextMenu.contact)}>
                <Trash2 size={16} />
                <span>Удалить контакт</span>
              </button>
            )}
          </div>
        </div>
      ) : null}

      {item && saveChanges.length > 0 ? (
        <PrototypeChangeConfirmationDialog changes={saveChanges} objectName={item.name || 'Поставщик'} onCancel={() => setSaveChanges([])} onConfirm={saveAndClose} title="Подтвердить изменения поставщика" />
      ) : null}
      {contactDeleteTarget ? (
        <SupplierContactDeleteConfirmationDialog
          contact={contactDeleteTarget}
          reason={contactDeleteReason}
          cancelRef={contactDeleteCancelRef}
          dialogRef={contactDeleteDialogRef}
          onReasonChange={setContactDeleteReason}
          onCancel={closeContactDeleteDialog}
          onConfirm={confirmContactDelete}
        />
      ) : null}
      {contactRestoreTarget ? (
        <SupplierContactRestoreConfirmationDialog
          contact={contactRestoreTarget}
          cancelRef={contactRestoreCancelRef}
          dialogRef={contactRestoreDialogRef}
          onCancel={closeContactRestoreDialog}
          onConfirm={confirmContactRestore}
        />
      ) : null}
    </>
  )
}

function ContractorServicePrototypeDialog({ onClose, onSave }: { onClose: () => void; onSave: (serviceName: string) => void }) {
  const [serviceName, setServiceName] = useState('')
  const [isRegular, setIsRegular] = useState(true)
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useEscapeKey(true, onClose)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-directory-service-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <h3 id="contractor-directory-service-title">Добавить услугу</h3>
          <button className="icon-button" type="button" aria-label="Закрыть форму услуги" onClick={onClose}><X size={18} /></button>
        </div>
        <form className="dictionary-modal-form contractors-modal-form" onSubmit={(event) => {
          event.preventDefault()
          onSave(serviceName || 'Новая услуга')
          onClose()
        }}>
          <FormField label="Наименование услуги"><input aria-label="Наименование услуги контрагента" value={serviceName} onChange={(event) => setServiceName(event.target.value)} /></FormField>
          <label className="contractors-check-row"><input type="checkbox" aria-label="Регулярные платежи услуги" checked={isRegular} onChange={(event) => setIsRegular(event.target.checked)} /><span>Регулярные платежи</span></label>
          <FormField label="Периодичность"><input aria-label="Периодичность услуги" defaultValue="12" /></FormField>
          <FormField label="Учитывать платеж с"><input aria-label="Учитывать платеж услуги с" defaultValue="Январь" /></FormField>
          <FormField label="Оплатить до"><input aria-label="Оплатить услугу до" defaultValue="Июль" /></FormField>
          <FormField label="Перенос долга в просроченный"><input aria-label="Перенос долга услуги в просроченный" defaultValue="30" /></FormField>
          <label className="contractors-check-row"><input type="checkbox" aria-label="По счетчику услуги" defaultChecked /><span>По счетчику</span></label>
          <label className="contractors-check-row"><input type="checkbox" aria-label="Пороговая тарификация услуги" defaultChecked /><span>Пороговая тарификация</span></label>
          <FormField label="Единица измерения"><input aria-label="Единица измерения услуги" /></FormField>
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit"><Save size={17} /><span>Сохранить</span></button>
            <button className="secondary-button" type="button" onClick={onClose}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function EmployeePrototypeDialog({ departments, item, onClose, onOpenFinancialReport, onSave }: { departments: ContractorDepartmentRow[]; item?: ContractorStaffRow; onClose: () => void; onOpenFinancialReport: (item: ContractorStaffRow) => void; onSave: (item: ContractorStaffRow) => void }) {
  const activeDepartments = departments.filter((department) => !department.isDeleted)
  const [form, setForm] = useState<ContractorStaffRow>(item ?? createEmptyEmployeePrototype(activeDepartments[0]?.name ?? departments[0]?.name ?? ''))
  const selectableDepartments = departments.filter((department) => !department.isDeleted || department.name === form.department)
  const [saveChanges, setSaveChanges] = useState<PrototypeChangeEntry[]>([])
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(saveChanges.length === 0)
  useEscapeKey(saveChanges.length === 0, onClose)

  function saveAndClose() {
    onSave(form)
    setSaveChanges([])
    onClose()
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!item) {
      saveAndClose()
      return
    }

    const changes = getEmployeePrototypeChanges(item, form)
    if (changes.length === 0) {
      onClose()
      return
    }

    setSaveChanges(changes)
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="employee-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="employee-dialog-title">{item ? form.fullName : 'Новый сотрудник'}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму сотрудника" onClick={onClose}><X size={18} /></button>
          </div>
          <form className="dictionary-modal-form contractors-modal-form" onSubmit={handleSubmit}>
            <FormField label="ФИО"><input aria-label="ФИО сотрудника" value={form.fullName} onChange={(event) => setForm({ ...form, fullName: event.target.value })} /></FormField>
            <FormField label="Отдел"><select aria-label="Отдел сотрудника" value={form.department} onChange={(event) => setForm({ ...form, department: event.target.value })}>{selectableDepartments.map((department) => <option value={department.name} key={department.id}>{department.name}</option>)}</select></FormField>
            <FormField label="Ставка"><div className="contractors-inline-field"><input aria-label="Ставка сотрудника" value={form.rate} onChange={(event) => setForm({ ...form, rate: event.target.value })} /><span>руб.</span></div></FormField>
            <div className="detail-dialog-actions contractors-dialog-actions contractors-garage-actions">
              <button className="secondary-button contractors-report-button" type="button" onClick={() => onOpenFinancialReport(form)}>
                <FileText size={16} />
                <span>Открыть фин. отчет</span>
              </button>
              <button className="secondary-button" type="submit"><Save size={17} /><span>Сохранить</span></button>
              <button className="ghost-button" type="button" onClick={onClose}>Отмена</button>
            </div>
          </form>
        </section>
      </div>

      {item && saveChanges.length > 0 ? (
        <PrototypeChangeConfirmationDialog changes={saveChanges} objectName={item.fullName || 'Сотрудник'} onCancel={() => setSaveChanges([])} onConfirm={saveAndClose} title="Подтвердить изменения сотрудника" />
      ) : null}

    </>
  )
}

function DepartmentPrototypeDialog({ item, onClose, onSave }: { item?: ContractorDepartmentRow; onClose: () => void; onSave: (department: ContractorDepartmentRow) => void }) {
  const [form, setForm] = useState<ContractorDepartmentRow>(() => item ?? { id: `department-${Date.now()}`, name: '', isDeleted: false })
  const [saveChanges, setSaveChanges] = useState<PrototypeChangeEntry[]>([])
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(saveChanges.length === 0)
  useEscapeKey(saveChanges.length === 0, onClose)

  function saveAndClose() {
    onSave(form)
    setSaveChanges([])
    onClose()
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!item) {
      saveAndClose()
      return
    }

    const changes = getDepartmentPrototypeChanges(item, form)
    if (changes.length === 0) {
      onClose()
      return
    }

    setSaveChanges(changes)
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="department-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="department-dialog-title">{item ? form.name : 'Новый отдел'}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму отдела" onClick={onClose}><X size={18} /></button>
          </div>
          <form className="dictionary-modal-form contractors-modal-form" onSubmit={handleSubmit}>
            <FormField label="Наименование"><input aria-label="Наименование отдела" maxLength={200} required value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} /></FormField>
            <div className="detail-dialog-actions">
              <button className="secondary-button" type="submit"><Save size={17} /><span>{item ? 'Сохранить' : 'Ок'}</span></button>
              <button className="ghost-button" type="button" onClick={onClose}>Отмена</button>
            </div>
          </form>
        </section>
      </div>

      {item && saveChanges.length > 0 ? (
        <PrototypeChangeConfirmationDialog changes={saveChanges} objectName={item.name || 'Отдел'} onCancel={() => setSaveChanges([])} onConfirm={saveAndClose} title="Подтвердить изменения отдела" />
      ) : null}
    </>
  )
}
