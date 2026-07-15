import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent, MouseEvent, ReactNode } from 'react'
import { CircleHelp, FileText, RotateCcw, Save, Search, Trash2, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import { DictionaryApiError } from '../../services/dictionariesApi'
import type { AccountingTypeDto, DictionaryClient, GarageDto, OwnerDto, PagedResult, SupplierGroupDto, SupplierDto, TariffDto, UpsertGarageRequest, UpsertOwnerRequest, UpsertSupplierRequest, UpsertTariffRequest } from '../../services/dictionariesApi'
import type { FinanceClient, GarageBalanceHistoryDto } from '../../services/financeApi'
import type { DadataAddressSuggestionDto, IntegrationClient } from '../../services/integrationsApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { TableLoadingState } from '../../shared/AsyncState'
import type { DictionaryEditorFieldKey, DictionaryRecord, DictionarySectionKey } from '../../shared/dictionaryWorkbench'
import { canWriteDictionarySection, createAccountingTypeFormFromDto, createEmptyAccountingTypeForm, createEmptyGarageForm, createEmptyOwnerForm, createEmptyOwnerGarageLinkForm, createEmptySupplierForm, createEmptyTariffForm, createGarageFormFromDto, createOwnerFormFromDto, createSupplierFormFromDto, dictionarySectionGroups, dictionarySectionOptions, getDictionaryEditorFieldMeta, getDictionaryRecordCells, getDictionaryRecordTitle, getDictionarySearchPlaceholder, getDictionarySectionOption, getDictionaryTableHeaders, getOwnerGarageOptions, getTariffCalculationBaseOptions, supportsDictionarySearch, usesElectricityTariffTiers } from '../../shared/dictionaryWorkbench'
import type { ChangePreview } from '../../shared/changePreview'
import { appendChangePreview, formatChangeDate, formatChangeMoney, formatChangeNumber, formatChangeText } from '../../shared/changePreview'
import { DictionaryList } from '../../shared/DictionaryList'
import { FormError, FormValidationSummary } from '../../shared/formFeedback'
import { FormField } from '../../shared/FormField'
import { formatDateOnly, formatDebtAmount, formatDebtLabel, formatMoney, formatMonth, formatNullableNumber, formatTariffRateSummary, getDebtClassName } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { createEmptyPage, createFallbackPage } from '../../shared/pagination'
import { TablePagination } from '../../shared/TablePagination'
import { createDefaultGarageBalanceHistoryFilters } from '../../shared/reportFilters'
import { SelectControl } from '../../shared/SelectControl'
import type { OwnerGarageLinkForm } from '../../shared/validation'
import { createTariffFormFromDto, getAccountingTypeValidationErrors, getGarageValidationErrors, getOwnerGarageLinkValidationErrors, getOwnerValidationErrors, getSupplierGroupValidationErrors, getSupplierValidationErrors, getTariffValidationErrors, parseOptionalNumberInput, updateTariffCalculationBase, withoutElectricityTierFields } from '../../shared/validation'

const dictionaryScreenRequestLimit = 100

function getDictionaryRestoreErrorMessage(section: DictionarySectionKey, caught: unknown) {
  if (caught instanceof DictionaryApiError) {
    if (caught.code === 'garage_number_duplicate') {
      return 'Гараж нельзя восстановить: активный гараж с таким номером уже есть. Проверьте рабочий список и архив.'
    }

    if (caught.code === 'supplier_group_duplicate') {
      return 'Группу поставщиков нельзя восстановить: активная группа с таким названием уже есть.'
    }

    if (caught.code === 'supplier_group_not_found' && section === 'suppliers') {
      return 'Поставщика нельзя восстановить: сначала верните его группу поставщиков.'
    }

    if (caught.code === 'income_type_duplicate') {
      return 'Вид поступления нельзя восстановить: активный вид с таким названием уже есть.'
    }

    if (caught.code === 'expense_type_duplicate') {
      return 'Вид выплаты нельзя восстановить: активный вид с таким названием уже есть.'
    }

    if (caught.code === 'tariff_duplicate') {
      return 'Тариф нельзя восстановить: активный тариф с таким названием и датой начала уже есть.'
    }
  }

  return caught instanceof Error ? caught.message : 'Не удалось восстановить запись.'
}

type DictionaryEditorState = { section: DictionarySectionKey; mode: 'create' | 'edit'; item?: DictionaryRecord }

type DictionaryChangePreview = ChangePreview

function FieldHelpLabel({ id, label, help }: { id: string; label: string; help: string }) {
  return (
    <span className="field-label-with-help">
      <span>{label}</span>
      <span className="field-help" tabIndex={0} aria-label={`Справка: ${label}`} aria-describedby={id}>
        <CircleHelp size={15} aria-hidden="true" />
        <span className="field-help__tooltip" id={id} role="tooltip">{help}</span>
      </span>
    </span>
  )
}

export function DictionaryPanelV2({ auth, dictionaryClient, financeClient, integrationClient, initialSection }: { auth: AuthResponse; dictionaryClient: DictionaryClient; financeClient: FinanceClient; integrationClient: IntegrationClient; initialSection: DictionarySectionKey }) {
  const [activeSection, setActiveSection] = useState<DictionarySectionKey>(initialSection)
  const [owners, setOwners] = useState<OwnerDto[]>([])
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [groups, setGroups] = useState<SupplierGroupDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [tariffs, setTariffs] = useState<TariffDto[]>([])
  const [ownerOptions, setOwnerOptions] = useState<OwnerDto[]>([])
  const [garageOptions, setGarageOptions] = useState<GarageDto[]>([])
  const [groupOptions, setGroupOptions] = useState<SupplierGroupDto[]>([])
  const [pages, setPages] = useState<Record<DictionarySectionKey, PagedResult<DictionaryRecord>>>({
    owners: createEmptyPage<DictionaryRecord>(),
    garages: createEmptyPage<DictionaryRecord>(),
    supplierGroups: createEmptyPage<DictionaryRecord>(),
    suppliers: createEmptyPage<DictionaryRecord>(),
    incomeTypes: createEmptyPage<DictionaryRecord>(),
    expenseTypes: createEmptyPage<DictionaryRecord>(),
    tariffs: createEmptyPage<DictionaryRecord>(),
  })
  const [search, setSearch] = useState('')
  const [showArchived, setShowArchived] = useState(false)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<{ id: number; text: string; kind: 'success' | 'error' } | null>(null)
  const [contextMenu, setContextMenu] = useState<{ section: DictionarySectionKey; item: DictionaryRecord; x: number; y: number } | null>(null)
  const [editor, setEditor] = useState<DictionaryEditorState | null>(null)
  const [pendingEditorConfirmation, setPendingEditorConfirmation] = useState<{ editor: DictionaryEditorState; changes: DictionaryChangePreview[] } | null>(null)
  const [archiveTarget, setArchiveTarget] = useState<{ section: DictionarySectionKey; item: DictionaryRecord } | null>(null)
  const [archiveReason, setArchiveReason] = useState('')
  const [archiveReasonError, setArchiveReasonError] = useState<string | null>(null)
  const [restoreTarget, setRestoreTarget] = useState<{ section: DictionarySectionKey; item: DictionaryRecord } | null>(null)
  const [balanceHistoryGarage, setBalanceHistoryGarage] = useState<GarageDto | null>(null)
  const [balanceHistory, setBalanceHistory] = useState<GarageBalanceHistoryDto | null>(null)
  const [balanceHistoryFilters, setBalanceHistoryFilters] = useState(() => createDefaultGarageBalanceHistoryFilters())
  const [balanceHistoryLoading, setBalanceHistoryLoading] = useState(false)
  const [balanceHistoryError, setBalanceHistoryError] = useState<string | null>(null)
  const balanceHistoryTriggerRef = useRef<HTMLElement | null>(null)
  const [ownerForm, setOwnerForm] = useState<UpsertOwnerRequest>(createEmptyOwnerForm())
  const [ownerGarageLinkForm, setOwnerGarageLinkForm] = useState<OwnerGarageLinkForm>(createEmptyOwnerGarageLinkForm())
  const [ownerAddressSuggestions, setOwnerAddressSuggestions] = useState<DadataAddressSuggestionDto[]>([])
  const [ownerAddressSuggestionsOpen, setOwnerAddressSuggestionsOpen] = useState(false)
  const [ownerAddressSuggestionStatus, setOwnerAddressSuggestionStatus] = useState('')
  const [ownerAddressActiveIndex, setOwnerAddressActiveIndex] = useState(0)
  const ownerAddressRequestSequence = useRef(0)
  const ownerAddressInputTouched = useRef(false)
  const [garageForm, setGarageForm] = useState(createEmptyGarageForm())
  const [supplierGroupName, setSupplierGroupName] = useState('')
  const [supplierForm, setSupplierForm] = useState(createEmptySupplierForm())
  const [accountingTypeForm, setAccountingTypeForm] = useState(createEmptyAccountingTypeForm())
  const [tariffForm, setTariffForm] = useState<UpsertTariffRequest>(createEmptyTariffForm())
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  useRestoreFocusOnClose(Boolean(editor))
  const editorCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(editor))
  const editorDialogRef = useFocusTrap<HTMLElement>(Boolean(editor))
  useRestoreFocusOnClose(Boolean(pendingEditorConfirmation))
  const editorConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingEditorConfirmation))
  const editorConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingEditorConfirmation))
  useRestoreFocusOnClose(Boolean(archiveTarget))
  const archiveCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(archiveTarget))
  const archiveDialogRef = useFocusTrap<HTMLElement>(Boolean(archiveTarget))
  useRestoreFocusOnClose(Boolean(restoreTarget))
  const restoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(restoreTarget))
  const restoreDialogRef = useFocusTrap<HTMLElement>(Boolean(restoreTarget))
  useRestoreFocusOnClose(Boolean(balanceHistoryGarage))
  const balanceHistoryCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(balanceHistoryGarage))
  const balanceHistoryDialogRef = useFocusTrap<HTMLElement>(Boolean(balanceHistoryGarage))
  const canWriteDictionaries = hasPermission(auth, permissions.dictionariesWrite)
  const canManageTariffs = hasPermission(auth, permissions.tariffsManage)
  const activePage = pages[activeSection]
  const activeOption = getDictionarySectionOption(activeSection)
  const canWriteActiveSection = canWriteDictionarySection(activeSection, canWriteDictionaries, canManageTariffs)
  const supportsSearch = supportsDictionarySearch(activeSection)
  const searchPlaceholder = getDictionarySearchPlaceholder(activeSection)
  const ownerGarageOptions = getOwnerGarageOptions(garageOptions, editor?.section === 'owners' && editor.item ? editor.item as OwnerDto : undefined)

  useEscapeKey(Boolean(contextMenu), () => setContextMenu(null))
  useEscapeKey(Boolean(editor) && !pendingEditorConfirmation, () => closeEditor())
  useEscapeKey(Boolean(pendingEditorConfirmation), () => setPendingEditorConfirmation(null))
  useEscapeKey(Boolean(archiveTarget), () => closeArchiveTarget())
  useEscapeKey(Boolean(restoreTarget), () => setRestoreTarget(null))
  useEscapeKey(Boolean(balanceHistoryGarage), () => closeBalanceHistory())

  useEffect(() => {
    if (!toast) {
      return undefined
    }

    const timeoutId = window.setTimeout(() => setToast(null), 3200)
    return () => window.clearTimeout(timeoutId)
  }, [toast])

  useEffect(() => {
    const query = ownerForm.address?.trim() ?? ''
    const sequence = ++ownerAddressRequestSequence.current
    if (editor?.section !== 'owners' || !ownerAddressInputTouched.current || query.length < 2) {
      return undefined
    }

    const timeoutId = window.setTimeout(() => {
      setOwnerAddressSuggestionStatus('Ищем адрес...')
      void integrationClient.suggestAddresses(auth.accessToken, query).then((suggestions) => {
        if (sequence !== ownerAddressRequestSequence.current) {
          return
        }

        setOwnerAddressSuggestions(suggestions)
        setOwnerAddressActiveIndex(0)
        setOwnerAddressSuggestionsOpen(suggestions.length > 0)
        setOwnerAddressSuggestionStatus(suggestions.length > 0 ? `Найдено вариантов: ${suggestions.length}` : 'Подходящих адресов не найдено. Можно продолжить ввод вручную.')
      }).catch(() => {
        if (sequence !== ownerAddressRequestSequence.current) {
          return
        }

        setOwnerAddressSuggestions([])
        setOwnerAddressSuggestionsOpen(false)
        setOwnerAddressSuggestionStatus('Подсказки DaData недоступны. Можно продолжить ввод вручную.')
      })
    }, 350)

    return () => window.clearTimeout(timeoutId)
  }, [auth.accessToken, editor?.section, integrationClient, ownerForm.address])

  useEffect(() => {
    function closeMenu() {
      setContextMenu(null)
    }

    window.addEventListener('click', closeMenu)
    return () => window.removeEventListener('click', closeMenu)
  }, [])

  useEffect(() => {
    let ignore = false
    async function loadReferences() {
      try {
        const [loadedOwners, loadedGarages, loadedGroups] = await Promise.all([
          dictionaryClient.getOwners(auth.accessToken, undefined, 500),
          dictionaryClient.getGarages(auth.accessToken, undefined, 500),
          dictionaryClient.getSupplierGroups(auth.accessToken, undefined, 500),
        ])
        if (!ignore) {
          setOwnerOptions(loadedOwners)
          setGarageOptions(loadedGarages)
          setGroupOptions(loadedGroups)
        }
      } catch {
        if (!ignore) {
          setError('Не удалось загрузить справочные значения для форм.')
        }
      }
    }

    void loadReferences()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    let ignore = false
    const timeoutId = window.setTimeout(() => {
      const page = pages[activeSection]
      setLoading(true)
      setError(null)
      loadPage(activeSection, 0, page.limit)
        .catch((caught) => {
          if (!ignore) {
            const message = caught instanceof Error ? caught.message : 'Не удалось загрузить таблицу справочника.'
            setError(message)
            showToast(message, 'error')
          }
        })
        .finally(() => {
          if (!ignore) {
            setLoading(false)
          }
        })
    }, supportsSearch && search.trim() ? 250 : 0)

    return () => {
      ignore = true
      window.clearTimeout(timeoutId)
    }
    // The loader intentionally captures the current page settings for the active dictionary section.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeSection, auth.accessToken, dictionaryClient, search, showArchived])

  async function loadPage(section: DictionarySectionKey, offset = pages[section].offset, limit = pages[section].limit) {
    const query = supportsDictionarySearch(section) ? search.trim() || undefined : undefined
    let page: PagedResult<DictionaryRecord>
    if (section === 'owners') {
      page = dictionaryClient.getOwnersPage
        ? await dictionaryClient.getOwnersPage(auth.accessToken, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getOwners(auth.accessToken, query, 500, showArchived), offset, limit)
      setOwners(page.items as OwnerDto[])
    } else if (section === 'garages') {
      page = dictionaryClient.getGaragesPage
        ? await dictionaryClient.getGaragesPage(auth.accessToken, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getGarages(auth.accessToken, query, 500, showArchived), offset, limit)
      setGarages(page.items as GarageDto[])
    } else if (section === 'supplierGroups') {
      page = dictionaryClient.getSupplierGroupsPage
        ? await dictionaryClient.getSupplierGroupsPage(auth.accessToken, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getSupplierGroups(auth.accessToken, query, 500, showArchived), offset, limit)
      setGroups(page.items as SupplierGroupDto[])
    } else if (section === 'suppliers') {
      page = dictionaryClient.getSuppliersPage
        ? await dictionaryClient.getSuppliersPage(auth.accessToken, undefined, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getSuppliers(auth.accessToken, undefined, query, 500, showArchived), offset, limit)
      setSuppliers(page.items as SupplierDto[])
    } else if (section === 'incomeTypes') {
      page = dictionaryClient.getIncomeTypesPage
        ? await dictionaryClient.getIncomeTypesPage(auth.accessToken, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getIncomeTypes(auth.accessToken, query, 500, showArchived), offset, limit)
      setIncomeTypes(page.items as AccountingTypeDto[])
    } else if (section === 'expenseTypes') {
      page = dictionaryClient.getExpenseTypesPage
        ? await dictionaryClient.getExpenseTypesPage(auth.accessToken, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getExpenseTypes(auth.accessToken, query, 500, showArchived), offset, limit)
      setExpenseTypes(page.items as AccountingTypeDto[])
    } else {
      page = dictionaryClient.getTariffsPage
        ? await dictionaryClient.getTariffsPage(auth.accessToken, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getTariffs(auth.accessToken, query, 500, showArchived), offset, limit)
      setTariffs(page.items as TariffDto[])
    }

    setPages((current) => ({ ...current, [section]: page }))
  }

  function showToast(text: string, kind: 'success' | 'error' = 'success') {
    setToast({ id: Date.now(), text, kind })
  }

  function openContextMenu(event: MouseEvent, section: DictionarySectionKey, item: DictionaryRecord) {
    event.preventDefault()
    if (section === 'garages') {
      balanceHistoryTriggerRef.current = event.currentTarget as HTMLElement
    } else {
      balanceHistoryTriggerRef.current = null
    }
    setContextMenu({ section, item, x: event.clientX, y: event.clientY })
  }

  function openArchiveTarget(section: DictionarySectionKey, item: DictionaryRecord) {
    setArchiveReason('')
    setArchiveReasonError(null)
    setArchiveTarget({ section, item })
  }

  function closeArchiveTarget() {
    setArchiveTarget(null)
    setArchiveReason('')
    setArchiveReasonError(null)
  }

  async function openBalanceHistory(garage: GarageDto) {
    const filters = createDefaultGarageBalanceHistoryFilters()
    setContextMenu(null)
    setBalanceHistoryGarage(garage)
    setBalanceHistoryFilters(filters)
    await loadBalanceHistory(garage.id, filters)
  }

  function closeBalanceHistory() {
    const trigger = balanceHistoryTriggerRef.current
    setBalanceHistoryGarage(null)
    setBalanceHistory(null)
    setBalanceHistoryError(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      balanceHistoryTriggerRef.current = null
    }, 0)
  }

  async function loadBalanceHistory(garageId = balanceHistoryGarage?.id, filters = balanceHistoryFilters) {
    if (!garageId) {
      return
    }

    setBalanceHistoryLoading(true)
    setBalanceHistoryError(null)
    try {
      const history = await financeClient.getGarageBalanceHistory(auth.accessToken, garageId, filters)
      setBalanceHistory(history)
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось загрузить историю баланса гаража.'
      setBalanceHistory(null)
      setBalanceHistoryError(message)
      showToast(message, 'error')
    } finally {
      setBalanceHistoryLoading(false)
    }
  }

  function openEditor(section: DictionarySectionKey, mode: 'create' | 'edit', item?: DictionaryRecord) {
    setValidationErrors([])
    setError(null)
    setContextMenu(null)
    resetOwnerAddressSuggestions()
    if (mode === 'edit' && item) {
      if (section === 'owners') {
        const owner = item as OwnerDto
        setOwnerForm(createOwnerFormFromDto(owner))
        setOwnerGarageLinkForm({ ...createEmptyOwnerGarageLinkForm(), existingGarageId: garageOptions.find((garage) => garage.ownerId === owner.id)?.id ?? '' })
      } else if (section === 'garages') {
        const garage = item as GarageDto
        setGarageForm(createGarageFormFromDto(garage))
      } else if (section === 'supplierGroups') {
        setSupplierGroupName((item as SupplierGroupDto).name)
      } else if (section === 'suppliers') {
        const supplier = item as SupplierDto
        setSupplierForm(createSupplierFormFromDto(supplier))
      } else if (section === 'incomeTypes' || section === 'expenseTypes') {
        const type = item as AccountingTypeDto
        setAccountingTypeForm(createAccountingTypeFormFromDto(type))
      } else {
        const tariff = item as TariffDto
        setTariffForm(createTariffFormFromDto(tariff))
      }
    } else {
      setOwnerForm(createEmptyOwnerForm())
      setOwnerGarageLinkForm(createEmptyOwnerGarageLinkForm())
      setGarageForm(createEmptyGarageForm())
      setSupplierGroupName('')
      setSupplierForm(createEmptySupplierForm(groupOptions[0]?.id ?? ''))
      setAccountingTypeForm(createEmptyAccountingTypeForm())
      setTariffForm(createEmptyTariffForm())
    }

    setEditor({ section, mode, item })
  }

  function closeEditor() {
    resetOwnerAddressSuggestions()
    setPendingEditorConfirmation(null)
    setEditor(null)
    setValidationErrors([])
  }

  function resetOwnerAddressSuggestions() {
    ownerAddressRequestSequence.current += 1
    ownerAddressInputTouched.current = false
    setOwnerAddressSuggestions([])
    setOwnerAddressSuggestionsOpen(false)
    setOwnerAddressSuggestionStatus('')
    setOwnerAddressActiveIndex(0)
  }

  function selectOwnerAddressSuggestion(suggestion: DadataAddressSuggestionDto) {
    ownerAddressInputTouched.current = false
    ownerAddressRequestSequence.current += 1
    setOwnerForm((current) => ({ ...current, address: suggestion.unrestrictedValue || suggestion.value }))
    setOwnerAddressSuggestionsOpen(false)
    setOwnerAddressSuggestionStatus('Адрес выбран из DaData.')
  }

  async function saveEditor(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!editor) {
      return
    }

    if (editor.section === 'tariffs' && !canManageTariffs) {
      setError('Для изменения тарифов нужно право tariffs.manage.')
      return
    }

    if (editor.section !== 'tariffs' && !canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const validation = getEditorValidationErrors(editor)
    if (validation.length > 0) {
      setValidationErrors(validation)
      return
    }

    if (editor.mode === 'edit' && editor.item) {
      const changes = getDictionaryEditorChanges(editor.section, editor.item)
      if (changes.length === 0) {
        closeEditor()
        showToast('Изменений нет.')
        return
      }

      setPendingEditorConfirmation({ editor, changes })
      return
    }

    await saveConfirmedEditor(editor)
  }

  async function saveConfirmedEditor(currentEditor: DictionaryEditorState) {
    setSaving('dictionary-editor')
    setError(null)
    try {
      const saved = await saveEditorRequest(currentEditor)
      if (!saved) {
        return
      }

      closeEditor()
      await refreshAfterMutation(currentEditor.section)
      showToast(currentEditor.mode === 'create' ? 'Запись добавлена.' : 'Изменения сохранены.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить запись.'
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  async function confirmEditorChanges() {
    if (!pendingEditorConfirmation) {
      return
    }

    const currentEditor = pendingEditorConfirmation.editor
    setPendingEditorConfirmation(null)
    await saveConfirmedEditor(currentEditor)
  }

  function getEditorValidationErrors(currentEditor: DictionaryEditorState) {
    if (currentEditor.section === 'owners') {
      return [...getOwnerValidationErrors(ownerForm), ...getOwnerGarageLinkValidationErrors(ownerGarageLinkForm)]
    }

    if (currentEditor.section === 'garages') {
      return getGarageValidationErrors(createGarageRequestFromForm())
    }

    if (currentEditor.section === 'supplierGroups') {
      return getSupplierGroupValidationErrors({ name: supplierGroupName })
    }

    if (currentEditor.section === 'suppliers') {
      return getSupplierValidationErrors(createSupplierRequestFromForm())
    }

    if (currentEditor.section === 'incomeTypes') {
      return getAccountingTypeValidationErrors(accountingTypeForm, 'вида поступления')
    }

    if (currentEditor.section === 'expenseTypes') {
      return getAccountingTypeValidationErrors(accountingTypeForm, 'вида выплаты')
    }

    return getTariffValidationErrors(tariffForm)
  }

  function createGarageRequestFromForm(): UpsertGarageRequest {
    return {
      number: garageForm.number,
      peopleCount: garageForm.peopleCount,
      floorCount: garageForm.floorCount,
      ownerId: garageForm.ownerId || null,
      startingBalance: garageForm.startingBalance,
      initialWaterMeterValue: garageForm.initialWaterMeterValue === '' ? null : Number(garageForm.initialWaterMeterValue),
      initialElectricityMeterValue: garageForm.initialElectricityMeterValue === '' ? null : Number(garageForm.initialElectricityMeterValue),
      comment: garageForm.comment.trim() || undefined,
    }
  }

  function createSupplierRequestFromForm(): UpsertSupplierRequest {
    return { ...supplierForm, groupId: supplierForm.groupId || groupOptions[0]?.id || '' }
  }

  async function saveEditorRequest(currentEditor: DictionaryEditorState) {
    const errors = getEditorValidationErrors(currentEditor)
    if (errors.length > 0) {
      setValidationErrors(errors)
      return false
    }

    if (currentEditor.section === 'owners') {
      let savedOwner: OwnerDto
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        savedOwner = await dictionaryClient.updateOwner(auth.accessToken, (currentEditor.item as OwnerDto).id, ownerForm)
      } else {
        savedOwner = await dictionaryClient.createOwner(auth.accessToken, ownerForm)
      }
      await saveOwnerGarageLinks(savedOwner.id)
    } else if (currentEditor.section === 'garages') {
      const request = createGarageRequestFromForm()
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateGarage(auth.accessToken, (currentEditor.item as GarageDto).id, request)
      } else {
        await dictionaryClient.createGarage(auth.accessToken, request)
      }
    } else if (currentEditor.section === 'supplierGroups') {
      const request = { name: supplierGroupName }
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateSupplierGroup(auth.accessToken, (currentEditor.item as SupplierGroupDto).id, request)
      } else {
        await dictionaryClient.createSupplierGroup(auth.accessToken, request)
      }
    } else if (currentEditor.section === 'suppliers') {
      const request = createSupplierRequestFromForm()
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateSupplier(auth.accessToken, (currentEditor.item as SupplierDto).id, request)
      } else {
        await dictionaryClient.createSupplier(auth.accessToken, request)
      }
    } else if (currentEditor.section === 'incomeTypes') {
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateIncomeType(auth.accessToken, (currentEditor.item as AccountingTypeDto).id, accountingTypeForm)
      } else {
        await dictionaryClient.createIncomeType(auth.accessToken, accountingTypeForm)
      }
    } else if (currentEditor.section === 'expenseTypes') {
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateExpenseType(auth.accessToken, (currentEditor.item as AccountingTypeDto).id, accountingTypeForm)
      } else {
        await dictionaryClient.createExpenseType(auth.accessToken, accountingTypeForm)
      }
    } else {
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateTariff(auth.accessToken, (currentEditor.item as TariffDto).id, tariffForm)
      } else {
        await dictionaryClient.createTariff(auth.accessToken, tariffForm)
      }
    }

    return true
  }

  async function saveOwnerGarageLinks(ownerId: string) {
    if (ownerGarageLinkForm.existingGarageId) {
      const existingGarage = garageOptions.find((garage) => garage.id === ownerGarageLinkForm.existingGarageId)
      if (!existingGarage) {
        throw new Error('Выбранный гараж не найден в справочнике.')
      }

      await dictionaryClient.updateGarage(auth.accessToken, existingGarage.id, {
        number: existingGarage.number,
        peopleCount: existingGarage.peopleCount,
        floorCount: existingGarage.floorCount,
        ownerId,
        startingBalance: existingGarage.startingBalance,
        initialWaterMeterValue: existingGarage.initialWaterMeterValue,
        initialElectricityMeterValue: existingGarage.initialElectricityMeterValue,
        comment: existingGarage.comment ?? undefined,
      })
    }

    if (ownerGarageLinkForm.newGarageNumber.trim()) {
      await dictionaryClient.createGarage(auth.accessToken, {
        number: ownerGarageLinkForm.newGarageNumber,
        peopleCount: ownerGarageLinkForm.peopleCount,
        floorCount: ownerGarageLinkForm.floorCount,
        ownerId,
        startingBalance: ownerGarageLinkForm.startingBalance,
        initialWaterMeterValue: ownerGarageLinkForm.initialWaterMeterValue === '' ? null : Number(ownerGarageLinkForm.initialWaterMeterValue),
        initialElectricityMeterValue: ownerGarageLinkForm.initialElectricityMeterValue === '' ? null : Number(ownerGarageLinkForm.initialElectricityMeterValue),
        comment: ownerGarageLinkForm.comment.trim() || undefined,
      })
    }
  }

  async function refreshAfterMutation(section: DictionarySectionKey) {
    const page = pages[section]
    await loadPage(section, Math.min(page.offset, Math.max(0, page.totalCount - 1)), page.limit)
    if (section === 'owners') {
      setOwnerOptions(await dictionaryClient.getOwners(auth.accessToken, undefined, 500))
      setGarageOptions(await dictionaryClient.getGarages(auth.accessToken, undefined, 500))
    }
    if (section === 'garages') {
      setOwnerOptions(await dictionaryClient.getOwners(auth.accessToken, undefined, 500))
      setGarageOptions(await dictionaryClient.getGarages(auth.accessToken, undefined, 500))
    }
    if (section === 'supplierGroups') {
      setGroupOptions(await dictionaryClient.getSupplierGroups(auth.accessToken, undefined, 500))
    }
  }

  async function confirmArchive() {
    if (!archiveTarget) {
      return
    }

    if (archiveTarget.section === 'tariffs' && !canManageTariffs) {
      setError('Для удаления тарифов нужно право tariffs.manage.')
      return
    }

    if (archiveTarget.section !== 'tariffs' && !canWriteDictionaries) {
      setError('Для удаления справочников нужно право dictionaries.write.')
      return
    }

    const reason = archiveReason.trim()
    if (!reason) {
      setArchiveReasonError('Укажите причину удаления записи.')
      return
    }

    setSaving('dictionary-archive')
    setError(null)
    setArchiveReasonError(null)
    try {
      if (archiveTarget.section === 'owners') {
        await dictionaryClient.archiveOwner(auth.accessToken, (archiveTarget.item as OwnerDto).id, reason)
      } else if (archiveTarget.section === 'garages') {
        await dictionaryClient.archiveGarage(auth.accessToken, (archiveTarget.item as GarageDto).id, reason)
      } else if (archiveTarget.section === 'supplierGroups') {
        await dictionaryClient.archiveSupplierGroup(auth.accessToken, (archiveTarget.item as SupplierGroupDto).id, reason)
      } else if (archiveTarget.section === 'suppliers') {
        await dictionaryClient.archiveSupplier(auth.accessToken, (archiveTarget.item as SupplierDto).id, reason)
      } else if (archiveTarget.section === 'incomeTypes') {
        await dictionaryClient.archiveIncomeType(auth.accessToken, (archiveTarget.item as AccountingTypeDto).id, reason)
      } else if (archiveTarget.section === 'expenseTypes') {
        await dictionaryClient.archiveExpenseType(auth.accessToken, (archiveTarget.item as AccountingTypeDto).id, reason)
      } else {
        await dictionaryClient.archiveTariff(auth.accessToken, (archiveTarget.item as TariffDto).id, reason)
      }

      const section = archiveTarget.section
      closeArchiveTarget()
      await refreshAfterMutation(section)
      showToast('Запись удалена из рабочего списка.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось удалить запись.'
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  async function confirmRestore() {
    if (!restoreTarget) {
      return
    }

    if (restoreTarget.section === 'tariffs' && !canManageTariffs) {
      setError('Для восстановления тарифов нужно право tariffs.manage.')
      return
    }

    if (restoreTarget.section !== 'tariffs' && !canWriteDictionaries) {
      setError('Для восстановления справочников нужно право dictionaries.write.')
      return
    }

    setSaving('dictionary-restore')
    setError(null)
    try {
      if (restoreTarget.section === 'owners') {
        await dictionaryClient.restoreOwner(auth.accessToken, (restoreTarget.item as OwnerDto).id)
      } else if (restoreTarget.section === 'garages') {
        await dictionaryClient.restoreGarage(auth.accessToken, (restoreTarget.item as GarageDto).id)
      } else if (restoreTarget.section === 'supplierGroups') {
        await dictionaryClient.restoreSupplierGroup(auth.accessToken, (restoreTarget.item as SupplierGroupDto).id)
      } else if (restoreTarget.section === 'suppliers') {
        await dictionaryClient.restoreSupplier(auth.accessToken, (restoreTarget.item as SupplierDto).id)
      } else if (restoreTarget.section === 'incomeTypes') {
        await dictionaryClient.restoreIncomeType(auth.accessToken, (restoreTarget.item as AccountingTypeDto).id)
      } else if (restoreTarget.section === 'expenseTypes') {
        await dictionaryClient.restoreExpenseType(auth.accessToken, (restoreTarget.item as AccountingTypeDto).id)
      } else {
        await dictionaryClient.restoreTariff(auth.accessToken, (restoreTarget.item as TariffDto).id)
      }

      const section = restoreTarget.section
      setRestoreTarget(null)
      await refreshAfterMutation(section)
      showToast('Запись восстановлена и снова доступна в рабочих списках.')
    } catch (caught) {
      const message = getDictionaryRestoreErrorMessage(restoreTarget.section, caught)
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  function changePageSize(value: number) {
    setPages((current) => ({ ...current, [activeSection]: { ...current[activeSection], offset: 0, limit: value } }))
    setLoading(true)
    void loadPage(activeSection, 0, value).finally(() => setLoading(false))
  }

  function getRows(): DictionaryRecord[] {
    if (activeSection === 'owners') return owners
    if (activeSection === 'garages') return garages
    if (activeSection === 'supplierGroups') return groups
    if (activeSection === 'suppliers') return suppliers
    if (activeSection === 'incomeTypes') return incomeTypes
    if (activeSection === 'expenseTypes') return expenseTypes
    return tariffs
  }

  function renderHeaders() {
    const headers = [...getDictionaryTableHeaders(activeSection), 'Статус', 'Действия']
    return headers.map((header, index) => <th className={index === headers.length - 1 ? 'dictionary-actions-column' : undefined} key={header}>{header}</th>)
  }

  function renderCells(item: DictionaryRecord) {
    return getDictionaryRecordCells(activeSection, item).map((value, index) => <td key={index}>{value}</td>)
  }

  function isArchivedRecord(item: DictionaryRecord) {
    return item.isArchived
  }

  function renderRowAction(item: DictionaryRecord) {
    if (isArchivedRecord(item)) {
      return (
        <button className="ghost-button dictionary-row-action" type="button" aria-label="Вернуть" title="Вернуть" disabled={!canWriteActiveSection} onClick={() => setRestoreTarget({ section: activeSection, item })}>
          <RotateCcw size={15} aria-hidden="true" />
        </button>
      )
    }

    return (
      <button className="ghost-button dictionary-row-action dictionary-row-action-danger" type="button" aria-label="Удалить" title="Удалить" disabled={!canWriteActiveSection} onClick={() => openArchiveTarget(activeSection, item)}>
        <Trash2 size={15} aria-hidden="true" />
      </button>
    )
  }

  function addDictionaryChange(changes: DictionaryChangePreview[], field: string, before: string, after: string) {
    appendChangePreview(changes, field, before, after)
  }

  function formatOwnerLabel(ownerId: string | null | undefined) {
    if (!ownerId) {
      return 'Без владельца'
    }

    return ownerOptions.find((owner) => owner.id === ownerId)?.fullName ?? `ID ${ownerId}`
  }

  function formatGarageLabel(garageId: string | null | undefined) {
    if (!garageId) {
      return 'Не выбран'
    }

    const garage = garageOptions.find((item) => item.id === garageId)
    return garage ? `Гараж ${garage.number}` : `ID ${garageId}`
  }

  function formatSupplierGroupLabel(groupId: string | null | undefined) {
    if (!groupId) {
      return 'Без группы'
    }

    return groupOptions.find((group) => group.id === groupId)?.name ?? `ID ${groupId}`
  }

  function formatCalculationBaseLabel(value: string) {
    return getTariffCalculationBaseOptions().find((option) => option.value === value)?.label ?? value
  }

  function getDictionaryEditorChanges(section: DictionarySectionKey, item: DictionaryRecord): DictionaryChangePreview[] {
    const changes: DictionaryChangePreview[] = []

    if (section === 'owners') {
      const owner = item as OwnerDto
      const currentGarageId = garageOptions.find((garage) => garage.ownerId === owner.id)?.id ?? ''

      addDictionaryChange(changes, 'Фамилия', formatChangeText(owner.lastName), formatChangeText(ownerForm.lastName))
      addDictionaryChange(changes, 'Имя', formatChangeText(owner.firstName), formatChangeText(ownerForm.firstName))
      addDictionaryChange(changes, 'Отчество', formatChangeText(owner.middleName), formatChangeText(ownerForm.middleName))
      addDictionaryChange(changes, 'Телефон', formatChangeText(owner.phone), formatChangeText(ownerForm.phone))
      addDictionaryChange(changes, 'Адрес', formatChangeText(owner.address), formatChangeText(ownerForm.address))
      addDictionaryChange(changes, 'Заметки по счетчикам', formatChangeText(owner.meterNotes), formatChangeText(ownerForm.meterNotes))
      addDictionaryChange(changes, 'Привязанный гараж', formatGarageLabel(currentGarageId), formatGarageLabel(ownerGarageLinkForm.existingGarageId))

      if (ownerGarageLinkForm.newGarageNumber.trim()) {
        addDictionaryChange(changes, 'Новый гараж', 'пусто', formatChangeText(ownerGarageLinkForm.newGarageNumber))
      }

      return changes
    }

    if (section === 'garages') {
      const garage = item as GarageDto
      const request = createGarageRequestFromForm()

      addDictionaryChange(changes, 'Номер', formatChangeText(garage.number), formatChangeText(request.number))
      addDictionaryChange(changes, 'Количество людей', formatChangeNumber(garage.peopleCount), formatChangeNumber(request.peopleCount))
      addDictionaryChange(changes, 'Количество этажей', formatChangeNumber(garage.floorCount), formatChangeNumber(request.floorCount))
      addDictionaryChange(changes, 'Владелец', formatOwnerLabel(garage.ownerId), formatOwnerLabel(request.ownerId))
      addDictionaryChange(changes, 'Стартовый баланс', formatChangeMoney(garage.startingBalance), formatChangeMoney(request.startingBalance))
      addDictionaryChange(changes, 'Стартовый счетчик воды', formatChangeNumber(garage.initialWaterMeterValue), formatChangeNumber(request.initialWaterMeterValue))
      addDictionaryChange(changes, 'Стартовый счетчик электроэнергии', formatChangeNumber(garage.initialElectricityMeterValue), formatChangeNumber(request.initialElectricityMeterValue))
      addDictionaryChange(changes, 'Комментарий', formatChangeText(garage.comment), formatChangeText(request.comment))
      return changes
    }

    if (section === 'supplierGroups') {
      addDictionaryChange(changes, 'Название', formatChangeText((item as SupplierGroupDto).name), formatChangeText(supplierGroupName))
      return changes
    }

    if (section === 'suppliers') {
      const supplier = item as SupplierDto
      const request = createSupplierRequestFromForm()

      addDictionaryChange(changes, 'Наименование', formatChangeText(supplier.name), formatChangeText(request.name))
      addDictionaryChange(changes, 'Группа', formatSupplierGroupLabel(supplier.groupId), formatSupplierGroupLabel(request.groupId))
      addDictionaryChange(changes, 'ИНН', formatChangeText(supplier.inn), formatChangeText(request.inn))
      addDictionaryChange(changes, 'Юридический адрес', formatChangeText(supplier.legalAddress), formatChangeText(request.legalAddress))
      addDictionaryChange(changes, 'Контактное лицо', formatChangeText(supplier.contactPerson), formatChangeText(request.contactPerson))
      addDictionaryChange(changes, 'Телефон', formatChangeText(supplier.phone), formatChangeText(request.phone))
      addDictionaryChange(changes, 'Почта', formatChangeText(supplier.email), formatChangeText(request.email))
      addDictionaryChange(changes, 'Стартовый баланс', formatChangeMoney(supplier.startingBalance), formatChangeMoney(request.startingBalance))
      addDictionaryChange(changes, 'Комментарий', formatChangeText(supplier.comment), formatChangeText(request.comment))
      return changes
    }

    if (section === 'incomeTypes' || section === 'expenseTypes') {
      const type = item as AccountingTypeDto
      addDictionaryChange(changes, 'Название', formatChangeText(type.name), formatChangeText(accountingTypeForm.name))
      addDictionaryChange(changes, 'Код', formatChangeText(type.code), formatChangeText(accountingTypeForm.code))
      return changes
    }

    const tariff = item as TariffDto
    addDictionaryChange(changes, 'Название', formatChangeText(tariff.name), formatChangeText(tariffForm.name))
    addDictionaryChange(changes, 'База расчета', formatCalculationBaseLabel(tariff.calculationBase), formatCalculationBaseLabel(tariffForm.calculationBase))
    addDictionaryChange(changes, 'Ставка', formatChangeNumber(tariff.rate), formatChangeNumber(tariffForm.rate))
    addDictionaryChange(changes, 'Дата начала', formatChangeDate(tariff.effectiveFrom), formatChangeDate(tariffForm.effectiveFrom))
    addDictionaryChange(changes, 'Первый порог электроэнергии', formatChangeNumber(tariff.electricityFirstThreshold), formatChangeNumber(tariffForm.electricityFirstThreshold))
    addDictionaryChange(changes, 'Второй порог электроэнергии', formatChangeNumber(tariff.electricitySecondThreshold), formatChangeNumber(tariffForm.electricitySecondThreshold))
    addDictionaryChange(changes, 'Первая ставка электроэнергии', formatChangeNumber(tariff.electricityFirstRate), formatChangeNumber(tariffForm.electricityFirstRate))
    addDictionaryChange(changes, 'Вторая ставка электроэнергии', formatChangeNumber(tariff.electricitySecondRate), formatChangeNumber(tariffForm.electricitySecondRate))
    addDictionaryChange(changes, 'Третья ставка электроэнергии', formatChangeNumber(tariff.electricityThirdRate), formatChangeNumber(tariffForm.electricityThirdRate))
    addDictionaryChange(changes, 'Комментарий', formatChangeText(tariff.comment), formatChangeText(tariffForm.comment))
    return changes
  }

  function renderEditorFields(section: DictionarySectionKey) {
    const fieldMeta = getDictionaryEditorFieldMeta
    const dictionaryField = (key: DictionaryEditorFieldKey, children: ReactNode, options?: { className?: string; help?: string }) => {
      const meta = fieldMeta(key)
      const label = options?.help ? <FieldHelpLabel id={`${key}-help`} label={meta.label} help={options.help} /> : meta.label
      return <FormField className={options?.className} label={label} hint={options?.help ? undefined : meta.hint}>{children}</FormField>
    }

    if (section === 'owners') {
      return (
        <>
          <div className="owner-name-grid">
            {dictionaryField('ownerLastName', <input aria-label={fieldMeta('ownerLastName').ariaLabel} placeholder={fieldMeta('ownerLastName').placeholder} value={ownerForm.lastName} onChange={(event) => setOwnerForm({ ...ownerForm, lastName: event.target.value })} required />)}
            {dictionaryField('ownerFirstName', <input aria-label={fieldMeta('ownerFirstName').ariaLabel} placeholder={fieldMeta('ownerFirstName').placeholder} value={ownerForm.firstName} onChange={(event) => setOwnerForm({ ...ownerForm, firstName: event.target.value })} required />)}
            {dictionaryField('ownerMiddleName', <input aria-label={fieldMeta('ownerMiddleName').ariaLabel} placeholder={fieldMeta('ownerMiddleName').placeholder} value={ownerForm.middleName ?? ''} onChange={(event) => setOwnerForm({ ...ownerForm, middleName: event.target.value })} />, { className: 'owner-name-grid__middle-name' })}
          </div>
          <div className="owner-contact-grid">
            {dictionaryField('ownerPhone', <input aria-label={fieldMeta('ownerPhone').ariaLabel} placeholder={fieldMeta('ownerPhone').placeholder} value={ownerForm.phone ?? ''} onChange={(event) => setOwnerForm({ ...ownerForm, phone: event.target.value })} />)}
            {dictionaryField('ownerAddress', (
              <>
                <div className="suggestion-combobox">
                  <input
                    aria-label={fieldMeta('ownerAddress').ariaLabel}
                    placeholder={fieldMeta('ownerAddress').placeholder}
                    role="combobox"
                    aria-autocomplete="list"
                    aria-expanded={ownerAddressSuggestionsOpen}
                    aria-controls="owner-address-suggestions"
                    aria-activedescendant={ownerAddressSuggestionsOpen && ownerAddressSuggestions.length > 0 ? `owner-address-suggestion-${ownerAddressActiveIndex}` : undefined}
                    autoComplete="off"
                    value={ownerForm.address ?? ''}
                    onFocus={() => setOwnerAddressSuggestionsOpen(ownerAddressSuggestions.length > 0)}
                    onBlur={() => setOwnerAddressSuggestionsOpen(false)}
                    onKeyDown={(event) => {
                      if (event.key === 'Escape') {
                        setOwnerAddressSuggestionsOpen(false)
                        return
                      }

                      if (ownerAddressSuggestions.length === 0 || !['ArrowDown', 'ArrowUp', 'Enter'].includes(event.key)) {
                        return
                      }

                      if (event.key === 'Enter' && ownerAddressSuggestionsOpen) {
                        event.preventDefault()
                        selectOwnerAddressSuggestion(ownerAddressSuggestions[ownerAddressActiveIndex])
                        return
                      }

                      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                        event.preventDefault()
                        setOwnerAddressSuggestionsOpen(true)
                        setOwnerAddressActiveIndex((current) => event.key === 'ArrowDown'
                          ? (current + 1) % ownerAddressSuggestions.length
                          : (current - 1 + ownerAddressSuggestions.length) % ownerAddressSuggestions.length)
                      }
                    }}
                    onChange={(event) => {
                      const value = event.target.value
                      ownerAddressInputTouched.current = true
                      setOwnerForm({ ...ownerForm, address: value })
                      if (value.trim().length < 2) {
                        setOwnerAddressSuggestions([])
                        setOwnerAddressSuggestionsOpen(false)
                        setOwnerAddressSuggestionStatus('')
                        setOwnerAddressActiveIndex(0)
                      }
                    }}
                  />
                  {ownerAddressSuggestionsOpen ? (
                    <div className="suggestion-options" id="owner-address-suggestions" role="listbox" aria-label="Адреса владельца DaData">
                      {ownerAddressSuggestions.map((suggestion, index) => (
                        <button className="ghost-button suggestion-option" type="button" role="option" id={`owner-address-suggestion-${index}`} aria-selected={index === ownerAddressActiveIndex} key={`${suggestion.fiasId ?? ''}-${suggestion.value}`} onMouseDown={(event) => event.preventDefault()} onMouseEnter={() => setOwnerAddressActiveIndex(index)} onClick={() => selectOwnerAddressSuggestion(suggestion)}>
                          <strong>{suggestion.value}</strong>
                          {suggestion.postalCode ? <span>Индекс {suggestion.postalCode}</span> : null}
                        </button>
                      ))}
                    </div>
                  ) : null}
                </div>
                {ownerAddressSuggestionStatus ? <small className="suggestion-status" role="status" aria-live="polite">{ownerAddressSuggestionStatus}</small> : null}
              </>
            ), { className: 'owner-address-field' })}
          </div>
          {dictionaryField('ownerMeterNotes', <textarea aria-label={fieldMeta('ownerMeterNotes').ariaLabel} placeholder={fieldMeta('ownerMeterNotes').placeholder} value={ownerForm.meterNotes ?? ''} onChange={(event) => setOwnerForm({ ...ownerForm, meterNotes: event.target.value })} />)}
          <div className="dictionary-form-section">
            <h4>Гараж владельца</h4>
            {dictionaryField('ownerExistingGarage', (
              <SelectControl
                aria-label={fieldMeta('ownerExistingGarage').ariaLabel}
                value={ownerGarageLinkForm.existingGarageId}
                options={[
                  { value: '', label: 'Не привязывать существующий гараж' },
                  ...ownerGarageOptions.map((garage) => ({ value: garage.id, label: garage.ownerName ? `Гараж ${garage.number} - ${garage.ownerName}` : `Гараж ${garage.number}` })),
                ]}
                onChange={(value) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, existingGarageId: value })}
              />
            ))}
            <div className="inline-fields">
              {dictionaryField('ownerNewGarageNumber', <input aria-label={fieldMeta('ownerNewGarageNumber').ariaLabel} placeholder={fieldMeta('ownerNewGarageNumber').placeholder} value={ownerGarageLinkForm.newGarageNumber} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, newGarageNumber: event.target.value })} />)}
              {dictionaryField('ownerNewGaragePeopleCount', <input aria-label={fieldMeta('ownerNewGaragePeopleCount').ariaLabel} type="number" min="0" value={ownerGarageLinkForm.peopleCount} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, peopleCount: Number(event.target.value) })} />)}
              {dictionaryField('ownerNewGarageFloorCount', <input aria-label={fieldMeta('ownerNewGarageFloorCount').ariaLabel} type="number" min="0" value={ownerGarageLinkForm.floorCount} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, floorCount: Number(event.target.value) })} />)}
            </div>
            <div className="inline-fields">
              {dictionaryField('ownerNewGarageStartingBalance', <input aria-label={fieldMeta('ownerNewGarageStartingBalance').ariaLabel} type="number" step="0.01" value={ownerGarageLinkForm.startingBalance} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, startingBalance: Number(event.target.value) })} />, { help: 'Долг на начало учета укажите положительным числом, переплату — отрицательным.' })}
              {dictionaryField('ownerNewGarageInitialWaterMeterValue', <input aria-label={fieldMeta('ownerNewGarageInitialWaterMeterValue').ariaLabel} type="number" min="0" step="0.001" value={ownerGarageLinkForm.initialWaterMeterValue} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, initialWaterMeterValue: event.target.value })} />, { help: 'Последнее показание счетчика воды на момент начала учета. Оставьте поле пустым, если показаний нет.' })}
              {dictionaryField('ownerNewGarageInitialElectricityMeterValue', <input aria-label={fieldMeta('ownerNewGarageInitialElectricityMeterValue').ariaLabel} type="number" min="0" step="0.001" value={ownerGarageLinkForm.initialElectricityMeterValue} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, initialElectricityMeterValue: event.target.value })} />, { help: 'Последнее показание счетчика электроэнергии на момент начала учета. Оставьте поле пустым, если показаний нет.' })}
            </div>
            {dictionaryField('ownerNewGarageComment', <textarea aria-label={fieldMeta('ownerNewGarageComment').ariaLabel} placeholder={fieldMeta('ownerNewGarageComment').placeholder} value={ownerGarageLinkForm.comment} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, comment: event.target.value })} />)}
          </div>
        </>
      )
    }
    if (section === 'garages') {
      return (
        <>
          {dictionaryField('garageNumber', <input aria-label={fieldMeta('garageNumber').ariaLabel} placeholder={fieldMeta('garageNumber').placeholder} value={garageForm.number} onChange={(event) => setGarageForm({ ...garageForm, number: event.target.value })} required />)}
          <div className="inline-fields">
            {dictionaryField('garagePeopleCount', <input aria-label={fieldMeta('garagePeopleCount').ariaLabel} type="number" min="0" value={garageForm.peopleCount} onChange={(event) => setGarageForm({ ...garageForm, peopleCount: Number(event.target.value) })} />)}
            {dictionaryField('garageFloorCount', <input aria-label={fieldMeta('garageFloorCount').ariaLabel} type="number" min="0" value={garageForm.floorCount} onChange={(event) => setGarageForm({ ...garageForm, floorCount: Number(event.target.value) })} />)}
          </div>
          {dictionaryField('garageOwner', (
            <SelectControl
              aria-label={fieldMeta('garageOwner').ariaLabel}
              value={garageForm.ownerId}
              options={[{ value: '', label: 'Без владельца' }, ...ownerOptions.map((owner) => ({ value: owner.id, label: owner.fullName }))]}
              onChange={(value) => setGarageForm({ ...garageForm, ownerId: value })}
            />
          ))}
          {dictionaryField('garageStartingBalance', <input aria-label={fieldMeta('garageStartingBalance').ariaLabel} type="number" step="0.01" value={garageForm.startingBalance} onChange={(event) => setGarageForm({ ...garageForm, startingBalance: Number(event.target.value) })} />)}
          <div className="inline-fields">
            {dictionaryField('garageInitialWaterMeterValue', <input aria-label={fieldMeta('garageInitialWaterMeterValue').ariaLabel} type="number" min="0" step="0.001" value={garageForm.initialWaterMeterValue} onChange={(event) => setGarageForm({ ...garageForm, initialWaterMeterValue: event.target.value })} />)}
            {dictionaryField('garageInitialElectricityMeterValue', <input aria-label={fieldMeta('garageInitialElectricityMeterValue').ariaLabel} type="number" min="0" step="0.001" value={garageForm.initialElectricityMeterValue} onChange={(event) => setGarageForm({ ...garageForm, initialElectricityMeterValue: event.target.value })} />)}
          </div>
          {dictionaryField('garageComment', <textarea aria-label={fieldMeta('garageComment').ariaLabel} placeholder={fieldMeta('garageComment').placeholder} value={garageForm.comment} onChange={(event) => setGarageForm({ ...garageForm, comment: event.target.value })} />)}
        </>
      )
    }
    if (section === 'supplierGroups') {
      return dictionaryField('supplierGroupName', <input aria-label={fieldMeta('supplierGroupName').ariaLabel} placeholder={fieldMeta('supplierGroupName').placeholder} value={supplierGroupName} onChange={(event) => setSupplierGroupName(event.target.value)} required />)
    }
    if (section === 'suppliers') {
      return (
        <>
          {dictionaryField('supplierName', <input aria-label={fieldMeta('supplierName').ariaLabel} placeholder={fieldMeta('supplierName').placeholder} value={supplierForm.name} onChange={(event) => setSupplierForm({ ...supplierForm, name: event.target.value })} required />)}
          {dictionaryField('supplierGroup', (
            <SelectControl
              aria-label={fieldMeta('supplierGroup').ariaLabel}
              value={supplierForm.groupId}
              options={groupOptions.length > 0 ? groupOptions.map((group) => ({ value: group.id, label: group.name })) : [{ value: '', label: 'Группы пока не добавлены' }]}
              disabled={groupOptions.length === 0}
              onChange={(value) => setSupplierForm({ ...supplierForm, groupId: value })}
            />
          ))}
          {dictionaryField('supplierInn', <input aria-label={fieldMeta('supplierInn').ariaLabel} placeholder={fieldMeta('supplierInn').placeholder} value={supplierForm.inn} onChange={(event) => setSupplierForm({ ...supplierForm, inn: event.target.value })} />)}
          {dictionaryField('supplierLegalAddress', <input aria-label={fieldMeta('supplierLegalAddress').ariaLabel} placeholder={fieldMeta('supplierLegalAddress').placeholder} value={supplierForm.legalAddress} onChange={(event) => setSupplierForm({ ...supplierForm, legalAddress: event.target.value })} />)}
          {dictionaryField('supplierContactPerson', <input aria-label={fieldMeta('supplierContactPerson').ariaLabel} placeholder={fieldMeta('supplierContactPerson').placeholder} value={supplierForm.contactPerson} onChange={(event) => setSupplierForm({ ...supplierForm, contactPerson: event.target.value })} />)}
          {dictionaryField('supplierPhone', <input aria-label={fieldMeta('supplierPhone').ariaLabel} placeholder={fieldMeta('supplierPhone').placeholder} value={supplierForm.phone} onChange={(event) => setSupplierForm({ ...supplierForm, phone: event.target.value })} />)}
          {dictionaryField('supplierEmail', <input aria-label={fieldMeta('supplierEmail').ariaLabel} placeholder={fieldMeta('supplierEmail').placeholder} value={supplierForm.email} onChange={(event) => setSupplierForm({ ...supplierForm, email: event.target.value })} />)}
          {dictionaryField('supplierStartingBalance', <input aria-label={fieldMeta('supplierStartingBalance').ariaLabel} type="number" step="0.01" value={supplierForm.startingBalance} onChange={(event) => setSupplierForm({ ...supplierForm, startingBalance: Number(event.target.value) })} />)}
          {dictionaryField('supplierComment', <textarea aria-label={fieldMeta('supplierComment').ariaLabel} placeholder={fieldMeta('supplierComment').placeholder} value={supplierForm.comment} onChange={(event) => setSupplierForm({ ...supplierForm, comment: event.target.value })} />)}
        </>
      )
    }
    if (section === 'incomeTypes' || section === 'expenseTypes') {
      return (
        <>
          {dictionaryField('accountingTypeName', <input aria-label={fieldMeta('accountingTypeName').ariaLabel} placeholder={fieldMeta('accountingTypeName').placeholder} value={accountingTypeForm.name} onChange={(event) => setAccountingTypeForm({ ...accountingTypeForm, name: event.target.value })} required />)}
          {dictionaryField('accountingTypeCode', <input aria-label={fieldMeta('accountingTypeCode').ariaLabel} placeholder={fieldMeta('accountingTypeCode').placeholder} value={accountingTypeForm.code} onChange={(event) => setAccountingTypeForm({ ...accountingTypeForm, code: event.target.value })} />)}
        </>
      )
    }
    return (
      <>
        {dictionaryField('tariffName', <input aria-label={fieldMeta('tariffName').ariaLabel} placeholder={fieldMeta('tariffName').placeholder} value={tariffForm.name} onChange={(event) => setTariffForm({ ...tariffForm, name: event.target.value })} required />)}
        {dictionaryField('tariffCalculationBase', (
          <SelectControl aria-label={fieldMeta('tariffCalculationBase').ariaLabel} value={tariffForm.calculationBase} options={getTariffCalculationBaseOptions()} onChange={(value) => setTariffForm(updateTariffCalculationBase(tariffForm, value))} />
        ))}
        <div className="inline-fields">
          {dictionaryField('tariffRate', <input aria-label={fieldMeta('tariffRate').ariaLabel} type="number" min="0.0001" step="0.0001" value={tariffForm.rate} onChange={(event) => setTariffForm({ ...tariffForm, rate: Number(event.target.value) })} />)}
          {dictionaryField('tariffEffectiveFrom', <LocalizedDatePicker ariaLabel={fieldMeta('tariffEffectiveFrom').ariaLabel} mode="date" value={tariffForm.effectiveFrom} onChange={(value) => setTariffForm({ ...tariffForm, effectiveFrom: value })} />)}
        </div>
        {usesElectricityTariffTiers(tariffForm.calculationBase) ? (
          <div className="inline-fields tariff-tier-fields">
            {dictionaryField('tariffElectricityFirstThreshold', <input aria-label={fieldMeta('tariffElectricityFirstThreshold').ariaLabel} placeholder={fieldMeta('tariffElectricityFirstThreshold').placeholder} type="number" min="0.0001" step="0.0001" value={tariffForm.electricityFirstThreshold ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityFirstThreshold: parseOptionalNumberInput(event.target.value) })} />)}
            {dictionaryField('tariffElectricitySecondThreshold', <input aria-label={fieldMeta('tariffElectricitySecondThreshold').ariaLabel} placeholder={fieldMeta('tariffElectricitySecondThreshold').placeholder} type="number" min="0.0001" step="0.0001" value={tariffForm.electricitySecondThreshold ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricitySecondThreshold: parseOptionalNumberInput(event.target.value) })} />)}
            {dictionaryField('tariffElectricityFirstRate', <input aria-label={fieldMeta('tariffElectricityFirstRate').ariaLabel} placeholder={fieldMeta('tariffElectricityFirstRate').placeholder} type="number" min="0.0001" step="0.0001" value={tariffForm.electricityFirstRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityFirstRate: parseOptionalNumberInput(event.target.value) })} />)}
            {dictionaryField('tariffElectricitySecondRate', <input aria-label={fieldMeta('tariffElectricitySecondRate').ariaLabel} placeholder={fieldMeta('tariffElectricitySecondRate').placeholder} type="number" min="0.0001" step="0.0001" value={tariffForm.electricitySecondRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricitySecondRate: parseOptionalNumberInput(event.target.value) })} />)}
            {dictionaryField('tariffElectricityThirdRate', <input aria-label={fieldMeta('tariffElectricityThirdRate').ariaLabel} placeholder={fieldMeta('tariffElectricityThirdRate').placeholder} type="number" min="0.0001" step="0.0001" value={tariffForm.electricityThirdRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityThirdRate: parseOptionalNumberInput(event.target.value) })} />)}
          </div>
        ) : null}
        {dictionaryField('tariffComment', <textarea aria-label={fieldMeta('tariffComment').ariaLabel} placeholder={fieldMeta('tariffComment').placeholder} value={tariffForm.comment ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, comment: event.target.value })} />)}
      </>
    )
  }

  const rows = getRows()

  return (
    <section className="dictionary-panel dictionary-panel-v2" aria-label="Справочники">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Справочники</p>
          <h2>{activeOption.label}</h2>
        </div>
        {!loading ? <span>{activePage.totalCount} записей</span> : null}
      </div>

      {error ? <FormError>{error}</FormError> : null}
      {!canWriteDictionaries ? <p className="form-hint">Режим просмотра: для добавления, изменения и удаления справочников нужно право dictionaries.write.</p> : null}
      {!canManageTariffs ? <p className="form-hint">Режим просмотра тарифов: для изменения тарифов нужно право tariffs.manage.</p> : null}

      <div className="dictionary-workbench">
        <nav className="dictionary-subnav" aria-label="Подгруппы справочников">
          {dictionarySectionGroups.map((group) => (
            <div className="dictionary-subnav-group" key={group.key}>
              <span>{group.label}</span>
              {dictionarySectionOptions.filter((section) => section.group === group.key).map((section) => (
                <button className={section.key === activeSection ? 'is-active' : undefined} type="button" aria-label={`Подгруппа: ${section.label}`} aria-current={section.key === activeSection ? 'page' : undefined} onClick={() => {
                  setSearch('')
                  setActiveSection(section.key)
                }} key={section.key}>
                  {section.label}
                </button>
              ))}
            </div>
          ))}
        </nav>

        <div className="dictionary-table-shell">
          <div className="dictionary-toolbar">
            <input aria-label={`Поиск: ${activeOption.label}`} placeholder={searchPlaceholder} value={search} onChange={(event) => setSearch(event.target.value)} disabled={!supportsSearch} />
            <label className="dictionary-archive-toggle">
              <input aria-label="Показывать архивные" type="checkbox" checked={showArchived} onChange={(event) => setShowArchived(event.target.checked)} />
              <span>Показывать архивные</span>
            </label>
            <button className="secondary-button create-action-button" type="button" disabled={!canWriteActiveSection} onClick={() => openEditor(activeSection, 'create')}>
              <FileText size={16} aria-hidden="true" />
              <span>Добавить</span>
            </button>
          </div>

          <div className="dictionary-table-scroll">
            <table className="dictionary-data-table" aria-label={`Таблица: ${activeOption.label}`}>
              <thead>
                <tr>{renderHeaders()}</tr>
              </thead>
              <tbody>
                {!loading ? rows.map((item) => (
                  <tr className={isArchivedRecord(item) ? 'dictionary-data-row-archived' : undefined} tabIndex={0} onContextMenu={(event) => openContextMenu(event, activeSection, item)} onDoubleClick={() => {
                    if (!isArchivedRecord(item)) {
                      openEditor(activeSection, 'edit', item)
                    }
                  }} key={`${activeSection}-${getDictionaryRecordTitle(activeSection, item)}-${'id' in item ? item.id : ''}`}>
                    {renderCells(item)}
                    <td>
                      <span className={isArchivedRecord(item) ? 'dictionary-status-pill dictionary-status-pill-archived' : 'dictionary-status-pill'}>
                        {isArchivedRecord(item) ? 'Архив' : 'Активна'}
                      </span>
                    </td>
                    <td className="dictionary-actions-column"><span className="dictionary-row-actions">{renderRowAction(item)}</span></td>
                  </tr>
                )) : null}
              </tbody>
            </table>
            {loading ? <TableLoadingState label={`Загружаем справочник: ${activeOption.label}`} /> : null}
            {!loading && rows.length === 0 ? <p className="empty-state" role="status" aria-live="polite">В этом справочнике пока нет записей</p> : null}
          </div>

          <TablePagination
            ariaLabel="Пагинация справочника"
            totalCount={activePage.totalCount}
            offset={activePage.offset}
            limit={activePage.limit}
            visibleCount={rows.length}
            disabled={loading}
            pageSizeLabel="Количество строк справочника"
            onPageChange={(page) => {
              setLoading(true)
              void loadPage(activeSection, (page - 1) * activePage.limit, activePage.limit).finally(() => setLoading(false))
            }}
            onPageSizeChange={changePageSize}
          />
        </div>
      </div>

      {contextMenu ? (
        <div className="context-menu" style={{ left: contextMenu.x, top: contextMenu.y }} role="menu" aria-label="Операции со справочником" onClick={(event) => event.stopPropagation()}>
          <div className="context-menu-group" role="group">
            <button type="button" role="menuitem" onClick={() => openEditor(contextMenu.section, 'create')}>
              <FileText size={15} aria-hidden="true" />
              <span>Добавить</span>
            </button>
          </div>
          <div className="context-menu-separator" role="separator" />
          <div className="context-menu-group" role="group">
            {isArchivedRecord(contextMenu.item) ? (
              <button type="button" role="menuitem" disabled={!canWriteActiveSection} onClick={() => {
                setRestoreTarget({ section: contextMenu.section, item: contextMenu.item })
                setContextMenu(null)
              }}>
                <RotateCcw size={15} />
                <span>Вернуть</span>
              </button>
            ) : (
              <>
                <button type="button" role="menuitem" disabled={!canWriteActiveSection} onClick={() => openEditor(contextMenu.section, 'edit', contextMenu.item)}>
                  <Save size={15} />
                  <span>Изменить</span>
                </button>
                <button className="context-menu-danger" type="button" role="menuitem" disabled={!canWriteActiveSection} onClick={() => {
                  openArchiveTarget(contextMenu.section, contextMenu.item)
                  setContextMenu(null)
                }}>
                  <Trash2 size={15} />
                  <span>Удалить</span>
                </button>
              </>
            )}
          </div>
          {contextMenu.section === 'garages' ? (
            <>
              <div className="context-menu-separator" role="separator" />
              <div className="context-menu-group" role="group">
                <button type="button" role="menuitem" onClick={() => void openBalanceHistory(contextMenu.item as GarageDto)}>
                  <FileText size={15} />
                  <span>История баланса</span>
                </button>
              </div>
            </>
          ) : null}
        </div>
      ) : null}

      {balanceHistoryGarage ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeBalanceHistory}>
          <section ref={balanceHistoryDialogRef} className="detail-dialog garage-balance-dialog" role="dialog" aria-modal="true" aria-labelledby="garage-balance-title" aria-describedby="garage-balance-owner" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">История баланса</p>
                <h3 id="garage-balance-title">Гараж {balanceHistoryGarage.number}</h3>
                <p id="garage-balance-owner">{balanceHistoryGarage.ownerName ?? 'Владелец не указан'}</p>
              </div>
              <button ref={balanceHistoryCloseRef} className="icon-button" type="button" aria-label="Закрыть историю баланса" onClick={closeBalanceHistory}>
                <X size={18} />
              </button>
            </div>
            <form className="balance-history-filters" onSubmit={(event) => {
              event.preventDefault()
              void loadBalanceHistory()
            }}>
              <label>
                Период с
                <input aria-label="Начало периода истории баланса" type="month" value={balanceHistoryFilters.monthFrom} onChange={(event) => setBalanceHistoryFilters((value) => ({ ...value, monthFrom: event.target.value }))} required />
              </label>
              <label>
                Период по
                <input aria-label="Конец периода истории баланса" type="month" value={balanceHistoryFilters.monthTo} onChange={(event) => setBalanceHistoryFilters((value) => ({ ...value, monthTo: event.target.value }))} required />
              </label>
              <button className="secondary-button" type="submit" disabled={balanceHistoryLoading}>
                <Search size={16} />
                <span>{balanceHistoryLoading ? 'Загружаем...' : 'Показать'}</span>
              </button>
            </form>
            {balanceHistoryError ? <FormError>{balanceHistoryError}</FormError> : null}
            {balanceHistory ? (
              <>
                <div className="balance-history-summary" aria-label="Итоги истории баланса">
                  <div>
                    <span>Старт</span>
                    <strong>{formatMoney(balanceHistory.startingBalance)}</strong>
                  </div>
                  <div>
                    <span>Начислено</span>
                    <strong>{formatMoney(balanceHistory.accrualTotal)}</strong>
                  </div>
                  <div>
                    <span>Поступило</span>
                    <strong>{formatMoney(balanceHistory.incomeTotal)}</strong>
                  </div>
                  <div>
                    <span>{formatDebtLabel(balanceHistory.debt)}</span>
                    <strong className={getDebtClassName(balanceHistory.debt)}>{formatDebtAmount(balanceHistory.debt)}</strong>
                  </div>
                </div>
                <div className="dictionary-table-scroll garage-balance-table-scroll">
                  <table className="dictionary-data-table" aria-label="История баланса гаража">
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
                      {balanceHistory.rows.map((row) => (
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
                  {balanceHistory.rows.length === 0 ? <p className="empty-state" role="status" aria-live="polite">По выбранному периоду строк нет</p> : null}
                </div>
              </>
            ) : null}
          </section>
        </div>
      ) : null}

      {editor ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeEditor}>
          <section ref={editorDialogRef} className={`detail-dialog dictionary-editor-dialog${editor.section === 'owners' ? ' dictionary-editor-dialog--owners' : ''}`} role="dialog" aria-modal="true" aria-labelledby="dictionary-editor-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">{editor.mode === 'create' ? 'Добавление' : 'Изменение'}</p>
                <h3 id="dictionary-editor-title">{dictionarySectionOptions.find((item) => item.key === editor.section)?.label ?? activeOption.label}</h3>
              </div>
              <button ref={editorCloseRef} className="icon-button" type="button" aria-label="Закрыть окно справочника" onClick={closeEditor}>
                <X size={18} />
              </button>
            </div>
            <form className="dictionary-modal-form" onSubmit={saveEditor}>
              {renderEditorFields(editor.section)}
              <FormValidationSummary title="Проверьте запись" items={validationErrors} />
              <div className="detail-dialog-actions">
                <button className="secondary-button" type="submit" disabled={saving === 'dictionary-editor'}>
                  <Save size={16} />
                  <span>{saving === 'dictionary-editor' ? 'Сохраняем...' : 'Сохранить'}</span>
                </button>
                <button className="ghost-button" type="button" onClick={closeEditor}>Отмена</button>
              </div>
            </form>
          </section>
        </div>
      ) : null}

      {pendingEditorConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setPendingEditorConfirmation(null)}>
          <section ref={editorConfirmationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="dictionary-edit-confirmation-title" aria-describedby="dictionary-edit-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Изменение</p>
                <h3 id="dictionary-edit-confirmation-title">Подтвердите изменения</h3>
                <p>{getDictionaryRecordTitle(pendingEditorConfirmation.editor.section, pendingEditorConfirmation.editor.item as DictionaryRecord)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить подтверждение изменений" onClick={() => setPendingEditorConfirmation(null)} disabled={saving === 'dictionary-editor'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="dictionary-edit-confirmation-description">Проверьте, что именно изменится. После подтверждения действие будет записано в историю изменений.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля">
              {pendingEditorConfirmation.changes.map((change) => (
                <li key={`${change.field}-${change.before}-${change.after}`}>
                  <span className="dictionary-change-field">{change.field}</span>
                  <span className="dictionary-change-values">
                    <span className="dictionary-change-value">{change.before}</span>
                    <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                    <span className="dictionary-change-value dictionary-change-value-after">{change.after}</span>
                  </span>
                </li>
              ))}
            </ul>
            <div className="detail-dialog-actions">
              <button ref={editorConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setPendingEditorConfirmation(null)} disabled={saving === 'dictionary-editor'}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmEditorChanges()} disabled={saving === 'dictionary-editor'}>
                <Save size={16} />
                <span>{saving === 'dictionary-editor' ? 'Сохраняем...' : 'Сохранить изменения'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {archiveTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => closeArchiveTarget()}>
          <section ref={archiveDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="dictionary-archive-title" aria-describedby="dictionary-archive-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="dictionary-archive-title">Подтвердите удаление</h3>
                <p>{getDictionaryRecordTitle(archiveTarget.section, archiveTarget.item)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить удаление" onClick={() => closeArchiveTarget()} disabled={saving === 'dictionary-archive'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="dictionary-archive-description">Запись будет скрыта из рабочих таблиц, но останется в истории изменений и связанной финансовой истории.</p>
            <label className="field-label" htmlFor="dictionary-archive-reason">Причина удаления</label>
            <textarea
              id="dictionary-archive-reason"
              aria-label="Причина удаления"
              aria-invalid={Boolean(archiveReasonError)}
              aria-describedby={archiveReasonError ? 'dictionary-archive-reason-error' : undefined}
              maxLength={1000}
              value={archiveReason}
              onChange={(event) => {
                setArchiveReason(event.target.value)
                if (archiveReasonError && event.target.value.trim()) {
                  setArchiveReasonError(null)
                }
              }}
              placeholder="Например: дубль, ошибочная карточка, услуга больше не используется"
              disabled={saving === 'dictionary-archive'}
              required
            />
            {archiveReasonError ? <p className="form-error" id="dictionary-archive-reason-error">{archiveReasonError}</p> : null}
            <div className="detail-dialog-actions">
              <button ref={archiveCancelRef} className="ghost-button" type="button" onClick={() => closeArchiveTarget()} disabled={saving === 'dictionary-archive'}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={() => void confirmArchive()} disabled={saving === 'dictionary-archive' || !archiveReason.trim()}>
                <Trash2 size={16} />
                <span>{saving === 'dictionary-archive' ? 'Удаляем...' : 'Удалить запись'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {restoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setRestoreTarget(null)}>
          <section ref={restoreDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="dictionary-restore-title" aria-describedby="dictionary-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление</p>
                <h3 id="dictionary-restore-title">Вернуть запись из архива?</h3>
                <p>{getDictionaryRecordTitle(restoreTarget.section, restoreTarget.item)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить восстановление" onClick={() => setRestoreTarget(null)} disabled={saving === 'dictionary-restore'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="dictionary-restore-description">Запись снова появится в рабочих списках. Действие будет записано в историю изменений.</p>
            <div className="detail-dialog-actions">
              <button ref={restoreCancelRef} className="ghost-button" type="button" onClick={() => setRestoreTarget(null)} disabled={saving === 'dictionary-restore'}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmRestore()} disabled={saving === 'dictionary-restore'}>
                <RotateCcw size={16} />
                <span>{saving === 'dictionary-restore' ? 'Возвращаем...' : 'Вернуть запись'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {toast ? <div className={`toast-message toast-message--${toast.kind}`} role="status" aria-live="polite">{toast.text}</div> : null}
    </section>
  )
}
export function DictionaryPanel({ auth, dictionaryClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient }) {
  const [owners, setOwners] = useState<OwnerDto[]>([])
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [groups, setGroups] = useState<SupplierGroupDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [tariffs, setTariffs] = useState<TariffDto[]>([])
  const [ownerForm, setOwnerForm] = useState({ lastName: '', firstName: '', phone: '' })
  const [garageForm, setGarageForm] = useState({ number: '', peopleCount: 1, floorCount: 1, ownerId: '', startingBalance: 0, initialWaterMeterValue: '', initialElectricityMeterValue: '', comment: '' })
  const [garageSearch, setGarageSearch] = useState('')
  const [garageSearchStatus, setGarageSearchStatus] = useState<string | null>(null)
  const garageSearchInitialized = useRef(false)
  const [selectedGarage, setSelectedGarage] = useState<GarageDto | null>(null)
  const [supplierGroupName, setSupplierGroupName] = useState('')
  const [supplierForm, setSupplierForm] = useState({ name: '', groupId: '', inn: '', startingBalance: 0 })
  const [supplierSearch, setSupplierSearch] = useState('')
  const [supplierSearchStatus, setSupplierSearchStatus] = useState<string | null>(null)
  const supplierSearchInitialized = useRef(false)
  const [incomeTypeForm, setIncomeTypeForm] = useState({ name: '', code: '' })
  const [expenseTypeForm, setExpenseTypeForm] = useState({ name: '', code: '' })
  const [tariffForm, setTariffForm] = useState<UpsertTariffRequest>({ name: '', calculationBase: 'fixed', rate: 1, effectiveFrom: '2026-07-01', comment: '' })
  const [editingTariffId, setEditingTariffId] = useState<string | null>(null)
  const [editingTariffBaseline, setEditingTariffBaseline] = useState<typeof tariffForm | null>(null)
  const [tariffDraftConfirmation, setTariffDraftConfirmation] = useState<{
    title: string
    description: string
    confirmLabel: string
    action: () => void
  } | null>(null)
  const [ownerValidationErrors, setOwnerValidationErrors] = useState<string[]>([])
  const [garageValidationErrors, setGarageValidationErrors] = useState<string[]>([])
  const [supplierGroupValidationErrors, setSupplierGroupValidationErrors] = useState<string[]>([])
  const [supplierValidationErrors, setSupplierValidationErrors] = useState<string[]>([])
  const [incomeTypeValidationErrors, setIncomeTypeValidationErrors] = useState<string[]>([])
  const [expenseTypeValidationErrors, setExpenseTypeValidationErrors] = useState<string[]>([])
  const [tariffValidationErrors, setTariffValidationErrors] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  useRestoreFocusOnClose(Boolean(selectedGarage))
  useRestoreFocusOnClose(Boolean(tariffDraftConfirmation))
  const selectedGarageCloseButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(selectedGarage))
  const selectedGarageDialogRef = useFocusTrap<HTMLElement>(Boolean(selectedGarage))
  const tariffDraftConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(tariffDraftConfirmation))
  const tariffDraftConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(tariffDraftConfirmation))

  useEscapeKey(Boolean(selectedGarage), () => setSelectedGarage(null))
  useEscapeKey(Boolean(tariffDraftConfirmation), () => setTariffDraftConfirmation(null))
  const canWriteDictionaries = hasPermission(auth, permissions.dictionariesWrite)
  const canManageTariffs = hasPermission(auth, permissions.tariffsManage)

  const defaultGroupId = useMemo(() => supplierForm.groupId || groups[0]?.id || '', [groups, supplierForm.groupId])

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [loadedOwners, loadedGarages, loadedGroups, loadedSuppliers, loadedIncomeTypes, loadedExpenseTypes, loadedTariffs] = await Promise.all([
          dictionaryClient.getOwners(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getGarages(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getSupplierGroups(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIncomeTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getExpenseTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getTariffs(auth.accessToken, undefined, dictionaryScreenRequestLimit),
        ])
        if (!ignore) {
          setOwners(loadedOwners)
          setGarages(loadedGarages)
          setGroups(loadedGroups)
          setSuppliers(loadedSuppliers)
          setIncomeTypes(loadedIncomeTypes)
          setExpenseTypes(loadedExpenseTypes)
          setTariffs(loadedTariffs)
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить справочники.')
        }
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }
    }

    void load()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    const query = garageSearch.trim()
    if (!garageSearchInitialized.current) {
      garageSearchInitialized.current = true
      return
    }

    let ignore = false
    const timeoutId = window.setTimeout(() => {
      setError(null)
      dictionaryClient
        .getGarages(auth.accessToken, query || undefined, dictionaryScreenRequestLimit)
        .then((result) => {
          if (!ignore) {
            setGarages(result)
            setGarageSearchStatus(query ? `Найдено гаражей: ${result.length}` : 'Показаны все гаражи')
          }
        })
        .catch((caught) => {
          if (!ignore) {
            setError(caught instanceof Error ? caught.message : 'Не удалось выполнить поиск гаражей.')
          }
        })
    }, 350)

    return () => {
      ignore = true
      window.clearTimeout(timeoutId)
    }
  }, [auth.accessToken, dictionaryClient, garageSearch])

  useEffect(() => {
    const query = supplierSearch.trim()
    if (!supplierSearchInitialized.current) {
      supplierSearchInitialized.current = true
      return
    }

    let ignore = false
    const timeoutId = window.setTimeout(() => {
      setError(null)
      dictionaryClient
        .getSuppliers(auth.accessToken, undefined, query || undefined, dictionaryScreenRequestLimit)
        .then((result) => {
          if (!ignore) {
            setSuppliers(result)
            setSupplierSearchStatus(query ? `Найдено поставщиков: ${result.length}` : 'Показаны все поставщики')
          }
        })
        .catch((caught) => {
          if (!ignore) {
            setError(caught instanceof Error ? caught.message : 'Не удалось выполнить поиск поставщиков.')
          }
        })
    }, 350)

    return () => {
      ignore = true
      window.clearTimeout(timeoutId)
    }
  }, [auth.accessToken, dictionaryClient, supplierSearch])

  async function saveOwner(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const errors = getOwnerValidationErrors(ownerForm)
    if (errors.length > 0) {
      setError(null)
      setOwnerValidationErrors(errors)
      return
    }

    setOwnerValidationErrors([])
    await runSaving('owner', async () => {
      const owner = await dictionaryClient.createOwner(auth.accessToken, ownerForm)
      setOwners((items) => [owner, ...items])
      setOwnerForm({ lastName: '', firstName: '', phone: '' })
    })
  }

  async function saveGarage(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const request: UpsertGarageRequest = {
      number: garageForm.number,
      peopleCount: garageForm.peopleCount,
      floorCount: garageForm.floorCount,
      ownerId: garageForm.ownerId || null,
      startingBalance: garageForm.startingBalance,
      initialWaterMeterValue: garageForm.initialWaterMeterValue === '' ? null : Number(garageForm.initialWaterMeterValue),
      initialElectricityMeterValue: garageForm.initialElectricityMeterValue === '' ? null : Number(garageForm.initialElectricityMeterValue),
      comment: garageForm.comment.trim() || undefined,
    }
    const errors = getGarageValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setGarageValidationErrors(errors)
      return
    }

    setGarageValidationErrors([])
    await runSaving('garage', async () => {
      const garage = await dictionaryClient.createGarage(auth.accessToken, request)
      setGarages((items) => [garage, ...items])
      setGarageForm({ number: '', peopleCount: 1, floorCount: 1, ownerId: '', startingBalance: 0, initialWaterMeterValue: '', initialElectricityMeterValue: '', comment: '' })
    })
  }

  async function searchGarages() {
    setSaving('garage-search')
    setError(null)
    setGarageSearchStatus(null)
    try {
      const result = await dictionaryClient.getGarages(auth.accessToken, garageSearch, dictionaryScreenRequestLimit)
      setGarages(result)
      const query = garageSearch.trim()
      setGarageSearchStatus(query ? `Найдено гаражей: ${result.length}` : 'Показаны все гаражи')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось выполнить поиск гаражей.')
    } finally {
      setSaving(null)
    }
  }

  async function saveSupplierGroup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const errors = getSupplierGroupValidationErrors({ name: supplierGroupName })
    if (errors.length > 0) {
      setError(null)
      setSupplierGroupValidationErrors(errors)
      return
    }

    setSupplierGroupValidationErrors([])
    await runSaving('group', async () => {
      const group = await dictionaryClient.createSupplierGroup(auth.accessToken, { name: supplierGroupName })
      setGroups((items) => [...items, group])
      setSupplierGroupName('')
      setSupplierForm((value) => ({ ...value, groupId: group.id }))
    })
  }

  async function saveSupplier(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const request: UpsertSupplierRequest = {
      name: supplierForm.name,
      groupId: defaultGroupId,
      inn: supplierForm.inn,
      startingBalance: supplierForm.startingBalance,
    }
    const errors = getSupplierValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setSupplierValidationErrors(errors)
      return
    }

    setSupplierValidationErrors([])
    await runSaving('supplier', async () => {
      const supplier = await dictionaryClient.createSupplier(auth.accessToken, request)
      setSuppliers((items) => [supplier, ...items])
      setSupplierForm({ name: '', groupId: defaultGroupId, inn: '', startingBalance: 0 })
    })
  }

  async function searchSuppliers() {
    setSaving('supplier-search')
    setError(null)
    setSupplierSearchStatus(null)
    try {
      const result = await dictionaryClient.getSuppliers(auth.accessToken, undefined, supplierSearch, dictionaryScreenRequestLimit)
      setSuppliers(result)
      const query = supplierSearch.trim()
      setSupplierSearchStatus(query ? `Найдено поставщиков: ${result.length}` : 'Показаны все поставщики')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось выполнить поиск поставщиков.')
    } finally {
      setSaving(null)
    }
  }

  async function saveIncomeType(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const errors = getAccountingTypeValidationErrors(incomeTypeForm, 'вида поступления')
    if (errors.length > 0) {
      setError(null)
      setIncomeTypeValidationErrors(errors)
      return
    }

    setIncomeTypeValidationErrors([])
    await runSaving('income-type', async () => {
      const incomeType = await dictionaryClient.createIncomeType(auth.accessToken, incomeTypeForm)
      setIncomeTypes((items) => [incomeType, ...items])
      setIncomeTypeForm({ name: '', code: '' })
    })
  }

  async function saveExpenseType(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const errors = getAccountingTypeValidationErrors(expenseTypeForm, 'вида выплаты')
    if (errors.length > 0) {
      setError(null)
      setExpenseTypeValidationErrors(errors)
      return
    }

    setExpenseTypeValidationErrors([])
    await runSaving('expense-type', async () => {
      const expenseType = await dictionaryClient.createExpenseType(auth.accessToken, expenseTypeForm)
      setExpenseTypes((items) => [expenseType, ...items])
      setExpenseTypeForm({ name: '', code: '' })
    })
  }

  async function saveTariff(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canManageTariffs) {
      setError('Для изменения тарифов нужно право tariffs.manage.')
      return
    }

    const errors = getTariffValidationErrors(tariffForm)
    if (errors.length > 0) {
      setError(null)
      setTariffValidationErrors(errors)
      return
    }

    setTariffValidationErrors([])
    await runSaving('tariff', async () => {
      if (editingTariffId) {
        const tariff = await dictionaryClient.updateTariff(auth.accessToken, editingTariffId, tariffForm)
        setTariffs((items) => items.map((item) => (item.id === tariff.id ? tariff : item)))
        setEditingTariffId(null)
        setEditingTariffBaseline(null)
      } else {
        const tariff = await dictionaryClient.createTariff(auth.accessToken, tariffForm)
        setTariffs((items) => [tariff, ...items])
      }

      setTariffForm((value) => withoutElectricityTierFields({ ...value, name: '', rate: 1, comment: '' }))
    })
  }

  function applyEditTariff(tariff: TariffDto) {
    const nextForm = createTariffFormFromDto(tariff)

    setEditingTariffId(tariff.id)
    setTariffValidationErrors([])
    setTariffForm(nextForm)
    setEditingTariffBaseline(nextForm)
  }

  function editTariff(tariff: TariffDto) {
    if (editingTariffId === tariff.id) {
      return
    }

    if (editingTariffId && hasUnsavedTariffChanges()) {
      setTariffDraftConfirmation({
        title: 'Перейти к другому тарифу?',
        description: 'Несохраненные изменения текущего тарифа будут потеряны.',
        confirmLabel: 'Перейти без сохранения',
        action: () => applyEditTariff(tariff),
      })
      return
    }

    applyEditTariff(tariff)
  }

  function hasUnsavedTariffChanges() {
    return Boolean(
      editingTariffBaseline
      && (
        tariffForm.name !== editingTariffBaseline.name
        || tariffForm.calculationBase !== editingTariffBaseline.calculationBase
        || tariffForm.rate !== editingTariffBaseline.rate
        || tariffForm.effectiveFrom !== editingTariffBaseline.effectiveFrom
        || tariffForm.comment !== editingTariffBaseline.comment
        || tariffForm.electricityFirstThreshold !== editingTariffBaseline.electricityFirstThreshold
        || tariffForm.electricitySecondThreshold !== editingTariffBaseline.electricitySecondThreshold
        || tariffForm.electricityFirstRate !== editingTariffBaseline.electricityFirstRate
        || tariffForm.electricitySecondRate !== editingTariffBaseline.electricitySecondRate
        || tariffForm.electricityThirdRate !== editingTariffBaseline.electricityThirdRate
      ),
    )
  }

  function resetTariffForm(options?: { skipConfirmation?: boolean }) {
    if (editingTariffId && !options?.skipConfirmation && hasUnsavedTariffChanges()) {
      setTariffDraftConfirmation({
        title: 'Отменить редактирование тарифа?',
        description: 'Несохраненные изменения текущего тарифа будут потеряны.',
        confirmLabel: 'Отменить без сохранения',
        action: () => resetTariffForm({ skipConfirmation: true }),
      })
      return
    }

    setTariffDraftConfirmation(null)
    setEditingTariffId(null)
    setEditingTariffBaseline(null)
    setTariffValidationErrors([])
    setTariffForm((value) => withoutElectricityTierFields({ ...value, name: '', rate: 1, comment: '' }))
  }

  function confirmTariffDraftAction() {
    if (!tariffDraftConfirmation) {
      return
    }

    const action = tariffDraftConfirmation.action
    setTariffDraftConfirmation(null)
    action()
  }

  async function archiveDictionaryItem(scope: string, reason: string, action: (reason: string) => Promise<void>) {
    if (scope === 'tariff' && !canManageTariffs) {
      setError('Для архивирования тарифов нужно право tariffs.manage.')
      return
    }

    if (scope !== 'tariff' && !canWriteDictionaries) {
      setError('Для архивирования справочников нужно право dictionaries.write.')
      return
    }

    await runSaving(`archive-${scope}`, () => action(reason))
  }

  async function runSaving(scope: string, action: () => Promise<void>) {
    setSaving(scope)
    setError(null)
    try {
      await action()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить запись.')
    } finally {
      setSaving(null)
    }
  }

  return (
    <section className="dictionary-panel" aria-label="Справочники">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Справочники</p>
          <h2>База для импорта, начислений и отчетов</h2>
        </div>
        {!loading ? <span>{owners.length + garages.length + suppliers.length} записей</span> : null}
      </div>

      {error ? <FormError>{error}</FormError> : null}
      {!canWriteDictionaries ? <p className="form-hint">Режим просмотра: для добавления и архивирования справочников нужно право dictionaries.write.</p> : null}
      {!canManageTariffs ? <p className="form-hint">Режим просмотра тарифов: для добавления и архивирования тарифов нужно право tariffs.manage.</p> : null}

      <div className="dictionary-grid">
        <form className="dictionary-form" onSubmit={saveOwner}>
          <h3>Владельцы</h3>
          <input aria-label="Фамилия владельца" placeholder="Фамилия" value={ownerForm.lastName} onChange={(event) => setOwnerForm({ ...ownerForm, lastName: event.target.value })} required />
          <input aria-label="Имя владельца" placeholder="Имя" value={ownerForm.firstName} onChange={(event) => setOwnerForm({ ...ownerForm, firstName: event.target.value })} required />
          <input aria-label="Телефон владельца" placeholder="Телефон" value={ownerForm.phone} onChange={(event) => setOwnerForm({ ...ownerForm, phone: event.target.value })} />
          <FormValidationSummary title="Проверьте владельца" items={ownerValidationErrors} />
          <button className="secondary-button create-action-button" type="submit" disabled={!canWriteDictionaries || saving === 'owner'}>
            <FileText size={16} aria-hidden="true" />
            <span>Добавить</span>
          </button>
          <DictionaryList
            items={owners.map((owner) => ({
              id: owner.id,
              title: owner.fullName,
              meta: owner.phone ?? 'телефон не указан',
              archiveLabel: canWriteDictionaries ? `Архивировать владельца ${owner.fullName}` : undefined,
              onArchive: canWriteDictionaries ? (reason) => archiveDictionaryItem('owner', reason, async (archiveReason) => {
                await dictionaryClient.archiveOwner(auth.accessToken, owner.id, archiveReason)
                setOwners((items) => items.filter((item) => item.id !== owner.id))
              }) : undefined,
            }))}
            emptyText="Владельцев пока нет"
          />
        </form>

        <form className="dictionary-form" onSubmit={saveGarage}>
          <h3>Гаражи</h3>
          <div className="compact-form">
            <input
              aria-label="Поиск гаража или владельца"
              placeholder="Номер или ФИО владельца"
              value={garageSearch}
              onChange={(event) => setGarageSearch(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault()
                  void searchGarages()
                }
              }}
            />
            <button className="icon-button" type="button" aria-label="Найти гараж" disabled={saving === 'garage-search'} onClick={() => void searchGarages()}>
              <Search size={17} />
            </button>
          </div>
          {garageSearchStatus ? <p className="form-hint" role="status" aria-live="polite">{garageSearchStatus}</p> : null}
          <input aria-label="Номер гаража" placeholder="Номер" value={garageForm.number} onChange={(event) => setGarageForm({ ...garageForm, number: event.target.value })} required />
          <div className="inline-fields">
            <input aria-label="Количество людей" type="number" min="0" value={garageForm.peopleCount} onChange={(event) => setGarageForm({ ...garageForm, peopleCount: Number(event.target.value) })} />
            <input aria-label="Количество этажей" type="number" min="0" value={garageForm.floorCount} onChange={(event) => setGarageForm({ ...garageForm, floorCount: Number(event.target.value) })} />
          </div>
          <input aria-label="Стартовый баланс гаража" type="number" step="0.01" value={garageForm.startingBalance} onChange={(event) => setGarageForm({ ...garageForm, startingBalance: Number(event.target.value) })} />
          <div className="inline-fields">
            <input aria-label="Стартовый счетчик воды" type="number" min="0" step="0.001" value={garageForm.initialWaterMeterValue} onChange={(event) => setGarageForm({ ...garageForm, initialWaterMeterValue: event.target.value })} />
            <input aria-label="Стартовый счетчик электричества" type="number" min="0" step="0.001" value={garageForm.initialElectricityMeterValue} onChange={(event) => setGarageForm({ ...garageForm, initialElectricityMeterValue: event.target.value })} />
          </div>
          <textarea aria-label="Комментарий по гаражу" placeholder="Комментарий по счетчикам, особенностям начислений или импорта" value={garageForm.comment} onChange={(event) => setGarageForm({ ...garageForm, comment: event.target.value })} />
          <SelectControl aria-label="Владелец гаража" value={garageForm.ownerId} options={[{ value: '', label: 'Без владельца' }, ...owners.map((owner) => ({ value: owner.id, label: owner.fullName }))]} onChange={(value) => setGarageForm({ ...garageForm, ownerId: value })} />
          <FormValidationSummary title="Проверьте гараж" items={garageValidationErrors} />
          <button className="secondary-button create-action-button" type="submit" disabled={!canWriteDictionaries || saving === 'garage'}>
            <FileText size={16} aria-hidden="true" />
            <span>Добавить</span>
          </button>
          <DictionaryList
            items={garages.map((garage) => ({
              id: garage.id,
              title: `Гараж ${garage.number}`,
              meta: `${garage.ownerName ?? 'владелец не указан'} · старт ${formatMoney(garage.startingBalance)}`,
              openLabel: `Открыть карточку гаража ${garage.number}`,
              onOpen: () => setSelectedGarage(garage),
              archiveLabel: canWriteDictionaries ? `Архивировать гараж ${garage.number}` : undefined,
              onArchive: canWriteDictionaries ? (reason) => archiveDictionaryItem('garage', reason, async (archiveReason) => {
                await dictionaryClient.archiveGarage(auth.accessToken, garage.id, archiveReason)
                setGarages((items) => items.filter((item) => item.id !== garage.id))
              }) : undefined,
            }))}
            emptyText="Гаражей пока нет"
          />
        </form>

        <div className="dictionary-form">
          <h3>Поставщики</h3>
          <div className="compact-form">
            <input
              aria-label="Поиск поставщика"
              placeholder="Название, ИНН или контакт"
              value={supplierSearch}
              onChange={(event) => setSupplierSearch(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault()
                  void searchSuppliers()
                }
              }}
            />
            <button className="icon-button" type="button" aria-label="Найти поставщика" disabled={saving === 'supplier-search'} onClick={() => void searchSuppliers()}>
              <Search size={17} />
            </button>
          </div>
          {supplierSearchStatus ? <p className="form-hint" role="status" aria-live="polite">{supplierSearchStatus}</p> : null}
          <form className="compact-form" onSubmit={saveSupplierGroup}>
            <input aria-label="Группа поставщиков" placeholder="Группа" value={supplierGroupName} onChange={(event) => setSupplierGroupName(event.target.value)} required />
            <button className="icon-button" type="submit" aria-label="Добавить группу" disabled={!canWriteDictionaries || saving === 'group'}>
              <FileText size={17} aria-hidden="true" />
            </button>
          </form>
          <FormValidationSummary title="Проверьте группу поставщиков" items={supplierGroupValidationErrors} />
          <form className="compact-stack" onSubmit={saveSupplier}>
            <input aria-label="Название поставщика" placeholder="Название" value={supplierForm.name} onChange={(event) => setSupplierForm({ ...supplierForm, name: event.target.value })} required />
            <SelectControl aria-label="Группа для поставщика" value={defaultGroupId} options={groups.length > 0 ? groups.map((group) => ({ value: group.id, label: group.name })) : [{ value: '', label: 'Группы пока не добавлены' }]} disabled={groups.length === 0} onChange={(value) => setSupplierForm({ ...supplierForm, groupId: value })} />
            <input aria-label="ИНН поставщика" placeholder="ИНН" value={supplierForm.inn} onChange={(event) => setSupplierForm({ ...supplierForm, inn: event.target.value })} />
            <input aria-label="Стартовый баланс поставщика" type="number" step="0.01" value={supplierForm.startingBalance} onChange={(event) => setSupplierForm({ ...supplierForm, startingBalance: Number(event.target.value) })} />
            <FormValidationSummary title="Проверьте поставщика" items={supplierValidationErrors} />
            <button className="secondary-button create-action-button" type="submit" disabled={!canWriteDictionaries || !defaultGroupId || saving === 'supplier'}>
              <FileText size={16} aria-hidden="true" />
              <span>Добавить</span>
            </button>
          </form>
          <DictionaryList
            items={suppliers.map((supplier) => ({
              id: supplier.id,
              title: supplier.name,
              meta: `${supplier.groupName}${supplier.inn ? `, ИНН ${supplier.inn}` : ''} · старт ${formatMoney(supplier.startingBalance)}`,
              archiveLabel: canWriteDictionaries ? `Архивировать поставщика ${supplier.name}` : undefined,
              onArchive: canWriteDictionaries ? (reason) => archiveDictionaryItem('supplier', reason, async (archiveReason) => {
                await dictionaryClient.archiveSupplier(auth.accessToken, supplier.id, archiveReason)
                setSuppliers((items) => items.filter((item) => item.id !== supplier.id))
              }) : undefined,
            }))}
            emptyText="Поставщиков пока нет"
          />
        </div>
      </div>

      <div className="finance-settings-grid" aria-label="Финансовые настройки">
        <form className="dictionary-form" onSubmit={saveIncomeType}>
          <h3>Виды поступлений</h3>
          <input aria-label="Название вида поступления" placeholder="Членский взнос" value={incomeTypeForm.name} onChange={(event) => setIncomeTypeForm({ ...incomeTypeForm, name: event.target.value })} required />
          <input aria-label="Код вида поступления" placeholder="Код" value={incomeTypeForm.code} onChange={(event) => setIncomeTypeForm({ ...incomeTypeForm, code: event.target.value })} />
          <FormValidationSummary title="Проверьте вид поступления" items={incomeTypeValidationErrors} />
          <button className="secondary-button create-action-button" type="submit" disabled={!canWriteDictionaries || saving === 'income-type'}>
            <FileText size={16} aria-hidden="true" />
            <span>Добавить</span>
          </button>
          <DictionaryList
            items={incomeTypes.map((item) => ({
              id: item.id,
              title: item.name,
              meta: item.code ?? 'код не указан',
              archiveLabel: canWriteDictionaries ? `Архивировать вид поступления ${item.name}` : undefined,
              onArchive: canWriteDictionaries ? (reason) => archiveDictionaryItem('income-type', reason, async (archiveReason) => {
                await dictionaryClient.archiveIncomeType(auth.accessToken, item.id, archiveReason)
                setIncomeTypes((items) => items.filter((incomeType) => incomeType.id !== item.id))
              }) : undefined,
            }))}
            emptyText="Видов поступлений пока нет"
          />
        </form>

        <form className="dictionary-form" onSubmit={saveExpenseType}>
          <h3>Виды выплат</h3>
          <input aria-label="Название вида выплаты" placeholder="Электроэнергия" value={expenseTypeForm.name} onChange={(event) => setExpenseTypeForm({ ...expenseTypeForm, name: event.target.value })} required />
          <input aria-label="Код вида выплаты" placeholder="Код" value={expenseTypeForm.code} onChange={(event) => setExpenseTypeForm({ ...expenseTypeForm, code: event.target.value })} />
          <FormValidationSummary title="Проверьте вид выплаты" items={expenseTypeValidationErrors} />
          <button className="secondary-button create-action-button" type="submit" disabled={!canWriteDictionaries || saving === 'expense-type'}>
            <FileText size={16} aria-hidden="true" />
            <span>Добавить</span>
          </button>
          <DictionaryList
            items={expenseTypes.map((item) => ({
              id: item.id,
              title: item.name,
              meta: item.code ?? 'код не указан',
              archiveLabel: canWriteDictionaries ? `Архивировать вид выплаты ${item.name}` : undefined,
              onArchive: canWriteDictionaries ? (reason) => archiveDictionaryItem('expense-type', reason, async (archiveReason) => {
                await dictionaryClient.archiveExpenseType(auth.accessToken, item.id, archiveReason)
                setExpenseTypes((items) => items.filter((expenseType) => expenseType.id !== item.id))
              }) : undefined,
            }))}
            emptyText="Видов выплат пока нет"
          />
        </form>

        <form className="dictionary-form" onSubmit={saveTariff}>
          <h3>{editingTariffId ? 'Изменение тарифа' : 'Тарифы'}</h3>
          <input aria-label="Название тарифа" placeholder="Вода" value={tariffForm.name} onChange={(event) => setTariffForm({ ...tariffForm, name: event.target.value })} required />
          <SelectControl aria-label="База расчета тарифа" value={tariffForm.calculationBase} options={getTariffCalculationBaseOptions()} onChange={(value) => setTariffForm(updateTariffCalculationBase(tariffForm, value))} />
          <div className="inline-fields">
            <input aria-label="Ставка тарифа" type="number" min="0.0001" step="0.0001" value={tariffForm.rate} onChange={(event) => setTariffForm({ ...tariffForm, rate: Number(event.target.value) })} />
            <LocalizedDatePicker ariaLabel="Дата начала тарифа" mode="date" value={tariffForm.effectiveFrom} onChange={(value) => setTariffForm({ ...tariffForm, effectiveFrom: value })} />
          </div>
          {usesElectricityTariffTiers(tariffForm.calculationBase) ? (
            <div className="inline-fields tariff-tier-fields">
              <input aria-label="Первый порог электроэнергии" placeholder="Порог 1, кВт" type="number" min="0.0001" step="0.0001" value={tariffForm.electricityFirstThreshold ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityFirstThreshold: parseOptionalNumberInput(event.target.value) })} />
              <input aria-label="Второй порог электроэнергии" placeholder="Порог 2, кВт" type="number" min="0.0001" step="0.0001" value={tariffForm.electricitySecondThreshold ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricitySecondThreshold: parseOptionalNumberInput(event.target.value) })} />
              <input aria-label="Первая ставка электроэнергии" placeholder="Ставка 1" type="number" min="0.0001" step="0.0001" value={tariffForm.electricityFirstRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityFirstRate: parseOptionalNumberInput(event.target.value) })} />
              <input aria-label="Вторая ставка электроэнергии" placeholder="Ставка 2" type="number" min="0.0001" step="0.0001" value={tariffForm.electricitySecondRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricitySecondRate: parseOptionalNumberInput(event.target.value) })} />
              <input aria-label="Третья ставка электроэнергии" placeholder="Ставка 3" type="number" min="0.0001" step="0.0001" value={tariffForm.electricityThirdRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityThirdRate: parseOptionalNumberInput(event.target.value) })} />
            </div>
          ) : null}
          <textarea aria-label="Комментарий тарифа" placeholder="Комментарий" value={tariffForm.comment ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, comment: event.target.value })} />
          {editingTariffId && hasUnsavedTariffChanges() ? <p className="form-hint" role="status" aria-live="polite">Есть несохраненные изменения тарифа.</p> : null}
          <FormValidationSummary title="Проверьте тариф" items={tariffValidationErrors} />
          <div className="inline-actions">
            <button className={editingTariffId ? 'secondary-button' : 'secondary-button create-action-button'} type="submit" disabled={!canManageTariffs || saving === 'tariff'}>
              {editingTariffId ? <Save size={16} /> : <FileText size={16} aria-hidden="true" />}
              <span>{editingTariffId ? 'Сохранить' : 'Добавить'}</span>
            </button>
            {editingTariffId ? (
              <button className="ghost-button" type="button" onClick={() => resetTariffForm()}>
                Отменить
              </button>
            ) : null}
          </div>
          <DictionaryList
            items={tariffs.map((item) => ({
              id: item.id,
              title: item.name,
              meta: `${formatTariffRateSummary(item)} с ${formatDateOnly(item.effectiveFrom)}${item.comment ? ` · ${item.comment}` : ''}`,
              isActive: editingTariffId === item.id,
              activeLabel: 'Редактируется',
              openLabel: canManageTariffs ? `Изменить тариф ${item.name}` : undefined,
              onOpen: canManageTariffs ? () => editTariff(item) : undefined,
              archiveLabel: canManageTariffs ? `Архивировать тариф ${item.name}` : undefined,
              onArchive: canManageTariffs ? (reason) => archiveDictionaryItem('tariff', reason, async (archiveReason) => {
                await dictionaryClient.archiveTariff(auth.accessToken, item.id, archiveReason)
                setTariffs((items) => items.filter((tariff) => tariff.id !== item.id))
                if (editingTariffId === item.id) {
                  resetTariffForm({ skipConfirmation: true })
                }
              }) : undefined,
            }))}
            emptyText="Тарифов пока нет"
          />
        </form>
      </div>
      {tariffDraftConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setTariffDraftConfirmation(null)}>
          <section ref={tariffDraftConfirmationDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="tariff-draft-confirmation-title" aria-describedby="tariff-draft-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Черновик тарифа</p>
                <h3 id="tariff-draft-confirmation-title">{tariffDraftConfirmation.title}</h3>
                <p>{editingTariffBaseline?.name || 'Тариф'}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Остаться в редактировании тарифа" onClick={() => setTariffDraftConfirmation(null)}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="tariff-draft-confirmation-description">{tariffDraftConfirmation.description}</p>
            <div className="detail-dialog-actions">
              <button ref={tariffDraftConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setTariffDraftConfirmation(null)}>
                Остаться
              </button>
              <button className="secondary-button danger-button" type="button" onClick={confirmTariffDraftAction}>
                <X size={16} />
                <span>{tariffDraftConfirmation.confirmLabel}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
      {selectedGarage ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setSelectedGarage(null)}>
          <section ref={selectedGarageDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="garage-card-title" aria-describedby="garage-card-owner" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Карточка гаража</p>
                <h3 id="garage-card-title">Гараж {selectedGarage.number}</h3>
                <p id="garage-card-owner">{selectedGarage.ownerName ?? 'Владелец не указан'}</p>
              </div>
              <button ref={selectedGarageCloseButtonRef} className="icon-button" type="button" aria-label="Закрыть карточку гаража" onClick={() => setSelectedGarage(null)}>
                <X size={18} />
              </button>
            </div>
            <dl className="detail-grid">
              <div>
                <dt>Владелец</dt>
                <dd>{selectedGarage.ownerName ?? 'Не указан'}</dd>
              </div>
              <div>
                <dt>Людей</dt>
                <dd>{selectedGarage.peopleCount}</dd>
              </div>
              <div>
                <dt>Этажей</dt>
                <dd>{selectedGarage.floorCount}</dd>
              </div>
              <div>
                <dt>Стартовый баланс</dt>
                <dd>{formatMoney(selectedGarage.startingBalance)}</dd>
              </div>
              <div>
                <dt>Старт воды</dt>
                <dd>{formatNullableNumber(selectedGarage.initialWaterMeterValue)}</dd>
              </div>
              <div>
                <dt>Старт электричества</dt>
                <dd>{formatNullableNumber(selectedGarage.initialElectricityMeterValue)}</dd>
              </div>
              <div>
                <dt>Комментарий</dt>
                <dd>{selectedGarage.comment || 'Нет комментария'}</dd>
              </div>
            </dl>
          </section>
        </div>
      ) : null}
    </section>
  )
}
