import { useEffect, useRef, useState } from 'react'
import type { FormEvent, MouseEvent, ReactNode } from 'react'
import { FileText, RotateCcw, Save, Search, Trash2, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { CatalogWorkspaceSection } from '../../shared/catalogCoverage'
import { profileCatalogEntries } from '../../shared/profileCatalogCoverage'
import type { WorkspaceOpenContext } from '../../shared/workspaceNavigation'
import { DictionaryApiError } from '../../services/dictionariesApi'
import type { AccountingTypeDto, DictionaryClient, GarageDto, MeasurementUnitDto, OwnerDto, PagedResult, UpsertGarageRequest, UpsertOwnerRequest } from '../../services/dictionariesApi'
import type { FinanceClient, GarageBalanceHistoryDto } from '../../services/financeApi'
import type { DadataAddressSuggestionDto, IntegrationClient } from '../../services/integrationsApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { AsyncErrorState, BackgroundRefreshStatus, EmptyState, StatusMessage, TableLoadingState } from '../../shared/AsyncState'
import type { DictionaryEditorFieldKey, DictionaryRecord, DictionarySectionKey } from '../../shared/dictionaryWorkbench'
import { canWriteDictionarySection, createAccountingTypeFormFromDto, createEmptyAccountingTypeForm, createEmptyGarageForm, createEmptyOwnerForm, createEmptyOwnerGarageLinkForm, createGarageFormFromDto, createOwnerFormFromDto, dictionarySectionGroups, dictionarySectionOptions, getDictionaryEditorFieldMeta, getDictionaryRecordCells, getDictionaryRecordTitle, getDictionarySearchPlaceholder, getDictionarySectionOption, getDictionaryTableHeaders, getOwnerGarageOptions, supportsDictionarySearch } from '../../shared/dictionaryWorkbench'
import type { ChangePreview } from '../../shared/changePreview'
import { appendChangePreview, formatChangeMoney, formatChangeNumber, formatChangeText } from '../../shared/changePreview'
import { ChangePreviewList } from '../../shared/ChangePreviewList'
import { scheduleDebouncedRequest } from '../../shared/debouncedRequest'
import { FormError, FormValidationSummary } from '../../shared/formFeedback'
import { FormField } from '../../shared/FormField'
import { formatDebtAmount, formatDebtLabel, formatMoney, formatMonth, getDebtClassName } from '../../shared/formatters'
import { restoreFocusAfterClose, useDismissOnWindowClick, useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { MoneyInput } from '../../shared/MoneyInput'
import { PhoneInput } from '../../shared/PhoneInput'
import { createEmptyPage, createFallbackPage, getLastPageOffset } from '../../shared/pagination'
import { TablePagination } from '../../shared/TablePagination'
import { ToastViewport } from '../../shared/Toast'
import { useToast } from '../../shared/useToast'
import { createDefaultGarageBalanceHistoryFilters, createFullFinancialReportFilters } from '../../shared/reportFilters'
import { SelectControl } from '../../shared/SelectControl'
import type { OwnerGarageLinkForm } from '../../shared/validation'
import { getAccountingTypeValidationErrors, getGarageValidationErrors, getOwnerGarageLinkValidationErrors, getOwnerValidationErrors } from '../../shared/validation'

function getDictionaryRestoreErrorMessage(caught: unknown) {
  if (caught instanceof DictionaryApiError) {
    if (caught.code === 'garage_number_duplicate') {
      return 'Гараж нельзя восстановить: активный гараж с таким номером уже есть. Проверьте рабочий список и архив.'
    }

    if (caught.code === 'income_type_duplicate') {
      return 'Вид поступления нельзя восстановить: активный вид с таким названием уже есть.'
    }

    if (caught.code === 'expense_type_duplicate') {
      return 'Статью расхода нельзя восстановить: активная статья с таким названием уже есть.'
    }

  }

  return caught instanceof Error ? caught.message : 'Не удалось восстановить запись.'
}

type DictionaryEditorState = { section: DictionarySectionKey; mode: 'create' | 'edit'; item?: DictionaryRecord }

type DictionaryChangePreview = ChangePreview

export function DictionaryPanelV2({ auth, dictionaryClient, financeClient, integrationClient, initialSection, onOpenWorkspaceSection }: { auth: AuthResponse; dictionaryClient: DictionaryClient; financeClient: FinanceClient; integrationClient: IntegrationClient; initialSection: DictionarySectionKey; onOpenWorkspaceSection?: (section: Exclude<CatalogWorkspaceSection, 'dictionaries'>, context?: WorkspaceOpenContext | null) => void }) {
  const [activeSection, setActiveSection] = useState<DictionarySectionKey>(initialSection)
  const [owners, setOwners] = useState<OwnerDto[]>([])
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [measurementUnits, setMeasurementUnits] = useState<MeasurementUnitDto[]>([])
  const [ownerOptions, setOwnerOptions] = useState<OwnerDto[]>([])
  const [garageOptions, setGarageOptions] = useState<GarageDto[]>([])
  const loadedEditorReferences = useRef({ owners: false, garages: false })
  const editorReferencesRequestRef = useRef<{ section: 'owners' | 'garages'; promise: Promise<boolean> } | null>(null)
  const editorReferencesControllerRef = useRef<AbortController | null>(null)
  const pendingEditorOpenRef = useRef<DictionaryEditorState | null>(null)
  const editorOpenSequenceRef = useRef(0)
  const pageRequestSequence = useRef(0)
  const pageRequestControllerRef = useRef<AbortController | null>(null)
  const [editorReferencesLoading, setEditorReferencesLoading] = useState(false)
  const [pages, setPages] = useState<Record<DictionarySectionKey, PagedResult<DictionaryRecord>>>({
    owners: createEmptyPage<DictionaryRecord>(),
    garages: createEmptyPage<DictionaryRecord>(),
    incomeTypes: createEmptyPage<DictionaryRecord>(),
    expenseTypes: createEmptyPage<DictionaryRecord>(),
    measurementUnits: createEmptyPage<DictionaryRecord>(),
  })
  const [search, setSearch] = useState('')
  const [showArchived, setShowArchived] = useState(false)
  const [loading, setLoading] = useState(true)
  const [loadedSectionState, setLoadedSectionState] = useState(() => ({
    accessToken: auth.accessToken,
    client: dictionaryClient,
    sections: { owners: false, garages: false, incomeTypes: false, expenseTypes: false, measurementUnits: false } as Record<DictionarySectionKey, boolean>,
  }))
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const { toast, showToast, dismissToast } = useToast(3200)
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
  const balanceHistoryRequestSequenceRef = useRef(0)
  const balanceHistoryRequestControllerRef = useRef<AbortController | null>(null)
  const [ownerForm, setOwnerForm] = useState<UpsertOwnerRequest>(createEmptyOwnerForm())
  const [ownerGarageLinkForm, setOwnerGarageLinkForm] = useState<OwnerGarageLinkForm>(createEmptyOwnerGarageLinkForm())
  const [ownerAddressSuggestions, setOwnerAddressSuggestions] = useState<DadataAddressSuggestionDto[]>([])
  const [ownerAddressSuggestionsOpen, setOwnerAddressSuggestionsOpen] = useState(false)
  const [ownerAddressSuggestionStatus, setOwnerAddressSuggestionStatus] = useState('')
  const [ownerAddressActiveIndex, setOwnerAddressActiveIndex] = useState(0)
  const ownerAddressInputTouched = useRef(false)
  const ownerAddressRequestSequence = useRef(0)
  const [garageForm, setGarageForm] = useState(createEmptyGarageForm())
  const [accountingTypeForm, setAccountingTypeForm] = useState(createEmptyAccountingTypeForm())
  const [measurementUnitName, setMeasurementUnitName] = useState('')
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
  const activePage = pages[activeSection]
  const activeSectionLoaded = loadedSectionState.accessToken === auth.accessToken
    && loadedSectionState.client === dictionaryClient
    && loadedSectionState.sections[activeSection]
  const activeOption = getDictionarySectionOption(activeSection)
  const canWriteActiveSection = canWriteDictionarySection(activeSection, canWriteDictionaries)
  const supportsSearch = supportsDictionarySearch(activeSection)
  const searchPlaceholder = getDictionarySearchPlaceholder(activeSection)
  const ownerGarageOptions = getOwnerGarageOptions(garageOptions, editor?.section === 'owners' && editor.item ? editor.item as OwnerDto : undefined)
  const mutationDialogOpen = Boolean(editor || archiveTarget || restoreTarget)

  useEscapeKey(Boolean(contextMenu), () => setContextMenu(null))
  useDismissOnWindowClick(Boolean(contextMenu), setContextMenu)
  useEscapeKey(Boolean(editor) && !pendingEditorConfirmation && saving !== 'dictionary-editor', () => closeEditor())
  useEscapeKey(Boolean(pendingEditorConfirmation) && saving !== 'dictionary-editor', () => setPendingEditorConfirmation(null))
  useEscapeKey(Boolean(archiveTarget) && saving !== 'dictionary-archive', () => closeArchiveTarget())
  useEscapeKey(Boolean(restoreTarget) && saving !== 'dictionary-restore', () => closeRestoreTarget())
  useEscapeKey(Boolean(balanceHistoryGarage), () => closeBalanceHistory())

  useEffect(() => {
    const query = ownerForm.address?.trim() ?? ''
    const sequence = ++ownerAddressRequestSequence.current
    if (editor?.section !== 'owners' || !ownerAddressInputTouched.current || query.length < 2) {
      return undefined
    }

    return scheduleDebouncedRequest({
      request: (signal) => integrationClient.suggestAddresses(auth.accessToken, query, undefined, signal),
      onStart: () => setOwnerAddressSuggestionStatus('Ищем адрес...'),
      onSuccess: (suggestions) => {
        if (sequence !== ownerAddressRequestSequence.current) return
        setOwnerAddressSuggestions(suggestions)
        setOwnerAddressActiveIndex(0)
        setOwnerAddressSuggestionsOpen(suggestions.length > 0)
        setOwnerAddressSuggestionStatus(suggestions.length > 0 ? `Найдено вариантов: ${suggestions.length}` : 'Подходящих адресов не найдено. Можно продолжить ввод вручную.')
      },
      onError: () => {
        if (sequence !== ownerAddressRequestSequence.current) return
        setOwnerAddressSuggestions([])
        setOwnerAddressSuggestionsOpen(false)
        setOwnerAddressSuggestionStatus('Подсказки DaData недоступны. Можно продолжить ввод вручную.')
      },
    })
  }, [auth.accessToken, editor?.section, integrationClient, ownerForm.address])

  useEffect(() => {
    loadedEditorReferences.current = { owners: false, garages: false }
    pendingEditorOpenRef.current = null
    editorOpenSequenceRef.current += 1
    return () => {
      editorReferencesControllerRef.current?.abort()
      editorReferencesControllerRef.current = null
      editorReferencesRequestRef.current = null
    }
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => () => {
    balanceHistoryRequestSequenceRef.current += 1
    balanceHistoryRequestControllerRef.current?.abort()
    balanceHistoryRequestControllerRef.current = null
  }, [auth.accessToken, financeClient])

  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      const page = pages[activeSection]
      setError(null)
      loadPage(activeSection, 0, page.limit)
        .catch((caught) => {
          const message = caught instanceof Error ? caught.message : 'Не удалось загрузить таблицу справочника.'
          setError(message)
          showToast(message, 'error')
        })
    }, supportsSearch && search.trim() ? 250 : 0)

    return () => {
      window.clearTimeout(timeoutId)
      pageRequestSequence.current += 1
      pageRequestControllerRef.current?.abort()
    }
    // The loader intentionally captures the current page settings for the active dictionary section.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeSection, auth.accessToken, dictionaryClient, search, showArchived])

  async function loadPage(section: DictionarySectionKey, offset = pages[section].offset, limit = pages[section].limit, background = false) {
    pageRequestControllerRef.current?.abort()
    const controller = new AbortController()
    pageRequestControllerRef.current = controller
    const signal = controller.signal
    const requestSequence = ++pageRequestSequence.current
    const query = supportsDictionarySearch(section) ? search.trim() || undefined : undefined
    if (!background) {
      setLoading(true)
    }
    try {
      let page: PagedResult<DictionaryRecord>
      if (section === 'owners') {
        page = dictionaryClient.getOwnersPage
          ? await dictionaryClient.getOwnersPage(auth.accessToken, query, offset, limit, showArchived, signal)
          : createFallbackPage<DictionaryRecord>(await dictionaryClient.getOwners(auth.accessToken, query, 500, showArchived, signal), offset, limit)
      } else if (section === 'garages') {
        page = dictionaryClient.getGaragesPage
          ? await dictionaryClient.getGaragesPage(auth.accessToken, query, offset, limit, showArchived, undefined, undefined, false, {}, signal)
          : createFallbackPage<DictionaryRecord>(await dictionaryClient.getGarages(auth.accessToken, query, 500, showArchived, signal), offset, limit)
      } else if (section === 'incomeTypes') {
        page = dictionaryClient.getIncomeTypesPage
          ? await dictionaryClient.getIncomeTypesPage(auth.accessToken, query, offset, limit, showArchived, signal)
          : createFallbackPage<DictionaryRecord>(await dictionaryClient.getIncomeTypes(auth.accessToken, query, 500, showArchived, signal), offset, limit)
      } else if (section === 'expenseTypes') {
        page = dictionaryClient.getExpenseTypesPage
          ? await dictionaryClient.getExpenseTypesPage(auth.accessToken, query, offset, limit, showArchived, signal)
          : createFallbackPage<DictionaryRecord>(await dictionaryClient.getExpenseTypes(auth.accessToken, query, 500, showArchived, signal), offset, limit)
      } else {
        page = await dictionaryClient.getMeasurementUnitsPage(auth.accessToken, query, offset, limit, showArchived, signal)
      }

      if (requestSequence !== pageRequestSequence.current) {
        return
      }

      if (offset > 0 && page.items.length === 0 && offset >= page.totalCount) {
        return loadPage(section, getLastPageOffset(page.totalCount, limit), limit, background)
      }

      if (section === 'owners') setOwners(page.items as OwnerDto[])
      else if (section === 'garages') setGarages(page.items as GarageDto[])
      else if (section === 'incomeTypes') setIncomeTypes(page.items as AccountingTypeDto[])
      else if (section === 'expenseTypes') setExpenseTypes(page.items as AccountingTypeDto[])
      else setMeasurementUnits(page.items as MeasurementUnitDto[])

      setPages((current) => ({ ...current, [section]: page }))
      setLoadedSectionState((current) => ({
        accessToken: auth.accessToken,
        client: dictionaryClient,
        sections: {
          ...(current.accessToken === auth.accessToken && current.client === dictionaryClient
            ? current.sections
            : { owners: false, garages: false, incomeTypes: false, expenseTypes: false, measurementUnits: false }),
          [section]: true,
        },
      }))
    } catch (caught) {
      if (requestSequence !== pageRequestSequence.current || signal?.aborted) {
        return
      }

      throw caught
    } finally {
      if (requestSequence === pageRequestSequence.current && !background) {
        setLoading(false)
      }
    }
  }

  function reportPageLoadError(caught: unknown) {
    const message = caught instanceof Error ? caught.message : 'Не удалось загрузить таблицу справочника.'
    setError(message)
    showToast(message, 'error')
  }

  function reportBackgroundPageLoadError(caught: unknown) {
    const message = caught instanceof Error ? caught.message : 'Не удалось обновить таблицу справочника.'
    setError(message)
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
    setError(null)
    setArchiveReason('')
    setArchiveReasonError(null)
    setArchiveTarget({ section, item })
  }

  function closeArchiveTarget() {
    setError(null)
    setArchiveTarget(null)
    setArchiveReason('')
    setArchiveReasonError(null)
  }

  async function openBalanceHistory(garage: GarageDto) {
    const fallbackFilters = createDefaultGarageBalanceHistoryFilters()
    const request = beginBalanceHistoryRequest()
    setContextMenu(null)
    setBalanceHistoryGarage(garage)
    setBalanceHistoryFilters(fallbackFilters)
    setBalanceHistoryLoading(true)
    setBalanceHistoryError(null)

    try {
      const period = await financeClient.getFinancialReportPeriod(auth.accessToken, { garageId: garage.id }, request.controller.signal)
      if (!isCurrentBalanceHistoryRequest(request)) {
        return
      }
      const filters = createFullFinancialReportFilters(period)
      setBalanceHistoryFilters(filters)
      await loadBalanceHistory(garage.id, filters, request)
    } catch (error) {
      if (!isCurrentBalanceHistoryRequest(request)) {
        return
      }
      setBalanceHistoryError(error instanceof Error ? error.message : 'Не удалось определить полный период истории баланса.')
      setBalanceHistoryLoading(false)
      finishBalanceHistoryRequest(request)
    }
  }

  function openRestoreTarget(section: DictionarySectionKey, item: DictionaryRecord) {
    setError(null)
    setRestoreTarget({ section, item })
  }

  function closeRestoreTarget() {
    setError(null)
    setRestoreTarget(null)
  }

  async function retryActivePage() {
    setError(null)
    const pendingEditor = pendingEditorOpenRef.current
    if (pendingEditor && pendingEditor.section === activeSection) {
      await openEditor(pendingEditor.section, pendingEditor.mode, pendingEditor.item)
      return
    }
    try {
      await loadPage(activeSection, activePage.offset, activePage.limit)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось загрузить справочник.')
    }
  }

  function closeBalanceHistory() {
    balanceHistoryRequestSequenceRef.current += 1
    balanceHistoryRequestControllerRef.current?.abort()
    balanceHistoryRequestControllerRef.current = null
    setBalanceHistoryGarage(null)
    setBalanceHistory(null)
    setBalanceHistoryError(null)
    restoreFocusAfterClose(balanceHistoryTriggerRef)
  }

  function beginBalanceHistoryRequest() {
    balanceHistoryRequestControllerRef.current?.abort()
    const controller = new AbortController()
    const sequence = ++balanceHistoryRequestSequenceRef.current
    balanceHistoryRequestControllerRef.current = controller
    return { controller, sequence }
  }

  function isCurrentBalanceHistoryRequest(request: { controller: AbortController; sequence: number }) {
    return !request.controller.signal.aborted
      && request.sequence === balanceHistoryRequestSequenceRef.current
      && balanceHistoryRequestControllerRef.current === request.controller
  }

  function finishBalanceHistoryRequest(request: { controller: AbortController; sequence: number }) {
    if (balanceHistoryRequestControllerRef.current === request.controller) {
      balanceHistoryRequestControllerRef.current = null
    }
  }

  async function loadBalanceHistory(
    garageId = balanceHistoryGarage?.id,
    filters = balanceHistoryFilters,
    existingRequest?: { controller: AbortController; sequence: number },
  ) {
    if (!garageId) {
      return
    }

    const request = existingRequest ?? beginBalanceHistoryRequest()
    setBalanceHistoryLoading(true)
    setBalanceHistoryError(null)
    try {
      const history = await financeClient.getGarageBalanceHistory(auth.accessToken, garageId, filters, request.controller.signal)
      if (!isCurrentBalanceHistoryRequest(request)) {
        return
      }
      setBalanceHistory(history)
    } catch (caught) {
      if (!isCurrentBalanceHistoryRequest(request)) {
        return
      }
      const message = caught instanceof Error ? caught.message : 'Не удалось загрузить историю баланса гаража.'
      setBalanceHistory(null)
      setBalanceHistoryError(message)
      showToast(message, 'error')
    } finally {
      if (isCurrentBalanceHistoryRequest(request)) {
        setBalanceHistoryLoading(false)
        finishBalanceHistoryRequest(request)
      }
    }
  }

  function ensureEditorReferences(section: DictionarySectionKey): Promise<boolean> {
    if (section !== 'owners' && section !== 'garages') {
      return Promise.resolve(true)
    }
    if (loadedEditorReferences.current[section]) {
      return Promise.resolve(true)
    }

    const existingRequest = editorReferencesRequestRef.current
    if (existingRequest?.section === section) {
      return existingRequest.promise
    }

    editorReferencesControllerRef.current?.abort()
    const controller = new AbortController()
    editorReferencesControllerRef.current = controller
    setEditorReferencesLoading(true)

    const promise = (async () => {
      try {
        if (section === 'owners') {
          const loadedGarages = await dictionaryClient.getGarages(auth.accessToken, undefined, 500, false, controller.signal)
          if (controller.signal.aborted) {
            return false
          }
          setGarageOptions(loadedGarages)
        } else {
          const loadedOwners = await dictionaryClient.getOwners(auth.accessToken, undefined, 500, false, controller.signal)
          if (controller.signal.aborted) {
            return false
          }
          setOwnerOptions(loadedOwners)
        }
        loadedEditorReferences.current[section] = true
        return true
      } catch (caught) {
        if (!controller.signal.aborted) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить справочные значения для формы.')
        }
        return false
      } finally {
        if (editorReferencesControllerRef.current === controller) {
          editorReferencesControllerRef.current = null
          editorReferencesRequestRef.current = null
          setEditorReferencesLoading(false)
        }
      }
    })()
    editorReferencesRequestRef.current = { section, promise }
    return promise
  }

  function cancelEditorReferenceRequest() {
    const controller = editorReferencesControllerRef.current
    editorReferencesControllerRef.current = null
    editorReferencesRequestRef.current = null
    controller?.abort()
    setEditorReferencesLoading(false)
  }

  async function openEditor(section: DictionarySectionKey, mode: 'create' | 'edit', item?: DictionaryRecord) {
    const pendingEditor = { section, mode, item }
    pendingEditorOpenRef.current = pendingEditor
    const openSequence = ++editorOpenSequenceRef.current
    setValidationErrors([])
    setError(null)
    setContextMenu(null)
    resetOwnerAddressSuggestions()
    if (!await ensureEditorReferences(section) || openSequence !== editorOpenSequenceRef.current) {
      return
    }
    pendingEditorOpenRef.current = null
    if (mode === 'edit' && item) {
      if (section === 'owners') {
        const owner = item as OwnerDto
        setOwnerForm(createOwnerFormFromDto(owner))
        setOwnerGarageLinkForm({ ...createEmptyOwnerGarageLinkForm(), existingGarageId: garageOptions.find((garage) => garage.ownerId === owner.id)?.id ?? '' })
      } else if (section === 'garages') {
        const garage = item as GarageDto
        setGarageForm(createGarageFormFromDto(garage))
      } else if (section === 'incomeTypes' || section === 'expenseTypes') {
        const type = item as AccountingTypeDto
        setAccountingTypeForm(createAccountingTypeFormFromDto(type))
      } else {
        setMeasurementUnitName((item as MeasurementUnitDto).name)
      }
    } else {
      setOwnerForm(createEmptyOwnerForm())
      setOwnerGarageLinkForm(createEmptyOwnerGarageLinkForm())
      setGarageForm(createEmptyGarageForm())
      setAccountingTypeForm(createEmptyAccountingTypeForm())
      setMeasurementUnitName('')
    }

    setEditor({ section, mode, item })
  }

  function closeEditor() {
    pendingEditorOpenRef.current = null
    editorOpenSequenceRef.current += 1
    resetOwnerAddressSuggestions()
    setError(null)
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

    if (!canWriteDictionaries) {
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
      refreshAfterMutation(currentEditor.section)
      showToast(currentEditor.mode === 'create' ? 'Запись добавлена.' : 'Изменения сохранены.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить запись.'
      setError(message)
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

    if (currentEditor.section === 'incomeTypes') {
      return getAccountingTypeValidationErrors(accountingTypeForm, 'вида поступления')
    }

    if (currentEditor.section === 'expenseTypes') {
      return getAccountingTypeValidationErrors(accountingTypeForm, 'вида выплаты')
    }

    if (!measurementUnitName.trim()) return ['Укажите обозначение единицы измерения.']
    if (measurementUnitName.trim().length > 40) return ['Обозначение единицы измерения должно содержать не более 40 символов.']
    return []
  }

  function createGarageRequestFromForm(): UpsertGarageRequest {
    return {
      number: garageForm.number,
      peopleCount: garageForm.peopleCount,
      floorCount: garageForm.floorCount,
      ownerId: garageForm.ownerId || null,
      startingBalance: garageForm.startingBalance,
      startingOverdueDebt: garageForm.startingOverdueDebt,
      initialWaterMeterValue: garageForm.initialWaterMeterValue === '' ? null : Number(garageForm.initialWaterMeterValue),
      initialElectricityMeterValue: garageForm.initialElectricityMeterValue === '' ? null : Number(garageForm.initialElectricityMeterValue),
      comment: garageForm.comment.trim() || undefined,
    }
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
        const garage = currentEditor.item as GarageDto
        await dictionaryClient.updateGarage(auth.accessToken, garage.id, { ...request, version: garage.version })
      } else {
        await dictionaryClient.createGarage(auth.accessToken, request)
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
      const request = { name: measurementUnitName.trim() }
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateMeasurementUnit(auth.accessToken, (currentEditor.item as MeasurementUnitDto).id, request)
      } else {
        await dictionaryClient.createMeasurementUnit(auth.accessToken, request)
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
        startingOverdueDebt: existingGarage.startingOverdueDebt,
        initialWaterMeterValue: existingGarage.initialWaterMeterValue,
        initialElectricityMeterValue: existingGarage.initialElectricityMeterValue,
        comment: existingGarage.comment ?? undefined,
        version: existingGarage.version,
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

  function refreshAfterMutation(section: DictionarySectionKey, visibleCountDelta = 0) {
    if (section === 'owners' || section === 'garages') {
      loadedEditorReferences.current = { owners: false, garages: false }
    }
    const page = pages[section]
    void loadPage(
      section,
      Math.min(page.offset, getLastPageOffset(page.totalCount + visibleCountDelta, page.limit)),
      page.limit,
      true,
    ).catch(reportBackgroundPageLoadError)
  }

  async function confirmArchive() {
    if (!archiveTarget) {
      return
    }

    if (!canWriteDictionaries) {
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
      } else if (archiveTarget.section === 'incomeTypes') {
        await dictionaryClient.archiveIncomeType(auth.accessToken, (archiveTarget.item as AccountingTypeDto).id, reason)
      } else if (archiveTarget.section === 'expenseTypes') {
        await dictionaryClient.archiveExpenseType(auth.accessToken, (archiveTarget.item as AccountingTypeDto).id, reason)
      } else {
        await dictionaryClient.archiveMeasurementUnit(auth.accessToken, (archiveTarget.item as MeasurementUnitDto).id, reason)
      }

      const section = archiveTarget.section
      closeArchiveTarget()
      refreshAfterMutation(section, showArchived ? 0 : -1)
      showToast('Запись удалена из рабочего списка.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось удалить запись.'
      setError(message)
    } finally {
      setSaving(null)
    }
  }

  async function confirmRestore() {
    if (!restoreTarget) {
      return
    }

    if (!canWriteDictionaries) {
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
      } else if (restoreTarget.section === 'incomeTypes') {
        await dictionaryClient.restoreIncomeType(auth.accessToken, (restoreTarget.item as AccountingTypeDto).id)
      } else if (restoreTarget.section === 'expenseTypes') {
        await dictionaryClient.restoreExpenseType(auth.accessToken, (restoreTarget.item as AccountingTypeDto).id)
      } else {
        await dictionaryClient.restoreMeasurementUnit(auth.accessToken, (restoreTarget.item as MeasurementUnitDto).id)
      }

      const section = restoreTarget.section
      closeRestoreTarget()
      refreshAfterMutation(section)
      showToast('Запись восстановлена и снова доступна в рабочих списках.')
    } catch (caught) {
      const message = getDictionaryRestoreErrorMessage(caught)
      setError(message)
    } finally {
      setSaving(null)
    }
  }

  function changePageSize(value: number) {
    setError(null)
    void loadPage(activeSection, 0, value).catch(reportPageLoadError)
  }

  function getRows(): DictionaryRecord[] {
    if (activeSection === 'owners') return owners
    if (activeSection === 'garages') return garages
    if (activeSection === 'incomeTypes') return incomeTypes
    if (activeSection === 'expenseTypes') return expenseTypes
    return measurementUnits
  }

  function renderHeaders() {
    const headers = [...getDictionaryTableHeaders(activeSection), 'Статус', 'Действия']
    return headers.map((header, index) => <th className={index === headers.length - 1 ? 'dictionary-actions-column table-actions-column' : undefined} key={header}>{header}</th>)
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
        <button className="ghost-button dictionary-row-action" type="button" aria-label="Вернуть" title="Вернуть" disabled={loading || !canWriteActiveSection} onClick={() => openRestoreTarget(activeSection, item)}>
          <RotateCcw size={15} aria-hidden="true" />
        </button>
      )
    }

    return (
      <button className="ghost-button dictionary-row-action danger-icon-button" type="button" aria-label="Удалить" title="Удалить" disabled={loading || !canWriteActiveSection} onClick={() => openArchiveTarget(activeSection, item)}>
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

    if (section === 'incomeTypes' || section === 'expenseTypes') {
      const type = item as AccountingTypeDto
      addDictionaryChange(changes, 'Название', formatChangeText(type.name), formatChangeText(accountingTypeForm.name))
      addDictionaryChange(changes, 'Код', formatChangeText(type.code), formatChangeText(accountingTypeForm.code))
      return changes
    }

    addDictionaryChange(changes, 'Обозначение', formatChangeText((item as MeasurementUnitDto).name), formatChangeText(measurementUnitName))
    return changes
  }

  function renderEditorFields(section: DictionarySectionKey) {
    const fieldMeta = getDictionaryEditorFieldMeta
    const dictionaryField = (key: DictionaryEditorFieldKey, children: ReactNode, options?: { className?: string; help?: string }) => {
      const meta = fieldMeta(key)
      return <FormField className={options?.className} label={meta.label} help={options?.help ?? meta.hint}>{children}</FormField>
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
            {dictionaryField('ownerPhone', <PhoneInput aria-label={fieldMeta('ownerPhone').ariaLabel} value={ownerForm.phone ?? ''} onValueChange={(phone) => setOwnerForm({ ...ownerForm, phone })} />)}
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
                {ownerAddressSuggestionStatus ? <small className={`suggestion-status${ownerAddressSuggestionStatus === 'Адрес выбран из DaData.' ? ' suggestion-status--visually-hidden' : ''}`} role="status" aria-live="polite">{ownerAddressSuggestionStatus}</small> : null}
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
              {dictionaryField('ownerNewGarageStartingBalance', <MoneyInput aria-label={fieldMeta('ownerNewGarageStartingBalance').ariaLabel} value={ownerGarageLinkForm.startingBalance} onValueChange={(startingBalance) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, startingBalance })} />, { help: 'Долг на начало учета укажите положительным числом, переплату — отрицательным.' })}
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
          {dictionaryField('garageStartingBalance', <MoneyInput aria-label={fieldMeta('garageStartingBalance').ariaLabel} value={garageForm.startingBalance} onValueChange={(startingBalance) => setGarageForm({ ...garageForm, startingBalance })} />, { help: 'Долг на начало учета укажите положительным числом, переплату — отрицательным.' })}
          {dictionaryField('garageStartingOverdueDebt', <MoneyInput aria-label={fieldMeta('garageStartingOverdueDebt').ariaLabel} value={garageForm.startingOverdueDebt} onValueChange={(startingOverdueDebt) => setGarageForm({ ...garageForm, startingOverdueDebt })} />)}
          <div className="inline-fields">
            {dictionaryField('garageInitialWaterMeterValue', <input aria-label={fieldMeta('garageInitialWaterMeterValue').ariaLabel} type="number" min="0" step="0.001" value={garageForm.initialWaterMeterValue} onChange={(event) => setGarageForm({ ...garageForm, initialWaterMeterValue: event.target.value })} />, { help: 'Последнее показание счетчика воды на момент начала учета. Оставьте поле пустым, если показаний нет.' })}
            {dictionaryField('garageInitialElectricityMeterValue', <input aria-label={fieldMeta('garageInitialElectricityMeterValue').ariaLabel} type="number" min="0" step="0.001" value={garageForm.initialElectricityMeterValue} onChange={(event) => setGarageForm({ ...garageForm, initialElectricityMeterValue: event.target.value })} />, { help: 'Последнее показание счетчика электроэнергии на момент начала учета. Оставьте поле пустым, если показаний нет.' })}
          </div>
          {dictionaryField('garageComment', <textarea aria-label={fieldMeta('garageComment').ariaLabel} placeholder={fieldMeta('garageComment').placeholder} value={garageForm.comment} onChange={(event) => setGarageForm({ ...garageForm, comment: event.target.value })} />)}
        </>
      )
    }
    if (section === 'incomeTypes' || section === 'expenseTypes') {
      return (
        <>
          {dictionaryField('accountingTypeName', <input aria-label={fieldMeta('accountingTypeName').ariaLabel} placeholder={fieldMeta('accountingTypeName').placeholder} value={accountingTypeForm.name} onChange={(event) => setAccountingTypeForm({ ...accountingTypeForm, name: event.target.value })} required />)}
          {dictionaryField('accountingTypeCode', <input aria-label={fieldMeta('accountingTypeCode').ariaLabel} placeholder={fieldMeta('accountingTypeCode').placeholder} value={accountingTypeForm.code} onChange={(event) => setAccountingTypeForm({ ...accountingTypeForm, code: event.target.value })} maxLength={80} autoCapitalize="none" spellCheck={false} />, { help: 'Код хранится строчными латинскими буквами. Системные коды зарезервированы.' })}
        </>
      )
    }
    return dictionaryField('measurementUnitName', <input aria-label={fieldMeta('measurementUnitName').ariaLabel} placeholder={fieldMeta('measurementUnitName').placeholder} value={measurementUnitName} onChange={(event) => setMeasurementUnitName(event.target.value)} maxLength={40} required />)
  }

  const rows = getRows()

  return (
    <section className="dictionary-panel dictionary-panel-v2" aria-label="Справочники">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Справочники</p>
          <h2>{activeOption.label}</h2>
        </div>
        {activeSectionLoaded ? <span>{activePage.totalCount} записей</span> : null}
      </div>

      {error && !mutationDialogOpen ? (
        <AsyncErrorState message={error} onRetry={() => void retryActivePage()} retrying={loading || editorReferencesLoading} />
      ) : null}
      {!canWriteDictionaries ? <p className="form-hint">Режим просмотра: для добавления, изменения и удаления справочников нужно право dictionaries.write.</p> : null}
      <div className="dictionary-workbench">
        <nav className="dictionary-subnav" aria-label="Подгруппы справочников">
          {dictionarySectionGroups.map((group) => (
            <div className="dictionary-subnav-group" key={group.key}>
              <span>{group.label}</span>
              {dictionarySectionOptions.filter((section) => section.group === group.key).map((section) => (
                <button className={section.key === activeSection ? 'is-active' : undefined} type="button" aria-label={`Подгруппа: ${section.label}`} aria-current={section.key === activeSection ? 'page' : undefined} onClick={() => {
                  setSearch('')
                  if (section.key !== activeSection) {
                    pendingEditorOpenRef.current = null
                    editorOpenSequenceRef.current += 1
                    cancelEditorReferenceRequest()
                    setLoading(true)
                    if (section.key !== 'owners' && section.key !== 'garages') {
                      setEditorReferencesLoading(false)
                    }
                  }
                  setActiveSection(section.key)
                }} key={section.key}>
                  {section.label}
                </button>
              ))}
            </div>
          ))}
          <div className="dictionary-subnav-group dictionary-subnav-group--related">
            <span>Профильные справочники</span>
            {profileCatalogEntries.map((entry) => (
              <button
                type="button"
                className="secondary-button"
                aria-label={`${entry.label}: открыть ${entry.workspaceLabel}`}
                disabled={!onOpenWorkspaceSection}
                onClick={() => onOpenWorkspaceSection?.(
                  entry.workspaceSection as Exclude<CatalogWorkspaceSection, 'dictionaries'>,
                  entry.workspaceSection === 'contractors'
                    ? { contractorTarget: { section: entry.apiRoute.startsWith('staff-') ? 'staff' : 'suppliers' } }
                    : null,
                )}
                key={entry.apiRoute}
              >
                <strong>{entry.label}</strong>
                <small>{entry.workspaceLabel}</small>
              </button>
            ))}
          </div>
        </nav>

        <div className="dictionary-table-shell">
          <div className="dictionary-toolbar">
            <input aria-label={`Поиск: ${activeOption.label}`} placeholder={searchPlaceholder} value={search} onChange={(event) => setSearch(event.target.value)} disabled={!supportsSearch} />
            <label className="dictionary-archive-toggle">
              <input aria-label="Показывать архивные" type="checkbox" checked={showArchived} onChange={(event) => setShowArchived(event.target.checked)} />
              <span>Показывать архивные</span>
            </label>
            <button className="secondary-button create-action-button" type="button" aria-busy={editorReferencesLoading} disabled={loading || !canWriteActiveSection || editorReferencesLoading} onClick={() => void openEditor(activeSection, 'create')}>
              <FileText size={16} aria-hidden="true" />
              <span>Добавить</span>
            </button>
          </div>

          <div className="dictionary-table-scroll">
            <table className="dictionary-data-table" aria-label={`Таблица: ${activeOption.label}`} aria-busy={loading}>
              <thead>
                <tr>{renderHeaders()}</tr>
              </thead>
              <tbody>
                {activeSectionLoaded ? rows.map((item) => (
                  <tr className={isArchivedRecord(item) ? 'dictionary-data-row-archived' : undefined} tabIndex={0} onContextMenu={loading ? undefined : (event) => openContextMenu(event, activeSection, item)} onDoubleClick={() => {
                    if (!loading && !editorReferencesLoading && !isArchivedRecord(item)) {
                      void openEditor(activeSection, 'edit', item)
                    }
                  }} key={`${activeSection}-${getDictionaryRecordTitle(activeSection, item)}-${'id' in item ? item.id : ''}`}>
                    {renderCells(item)}
                    <td>
                      <span className={isArchivedRecord(item) ? 'dictionary-status-pill dictionary-status-pill-archived' : 'dictionary-status-pill'}>
                        {isArchivedRecord(item) ? 'Архив' : 'Активна'}
                      </span>
                    </td>
                    <td className="dictionary-actions-column table-actions-column"><span className="dictionary-row-actions">{renderRowAction(item)}</span></td>
                  </tr>
                )) : null}
              </tbody>
            </table>
            {loading && !activeSectionLoaded ? <TableLoadingState label={`Загружаем справочник: ${activeOption.label}`} /> : null}
            {loading && activeSectionLoaded ? <BackgroundRefreshStatus label={`Обновляем справочник: ${activeOption.label}`} /> : null}
            {activeSectionLoaded && !loading && rows.length === 0 ? <EmptyState>В этом справочнике пока нет записей</EmptyState> : null}
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
              setError(null)
              void loadPage(activeSection, (page - 1) * activePage.limit, activePage.limit).catch(reportPageLoadError)
            }}
            onPageSizeChange={changePageSize}
          />
        </div>
      </div>

      {contextMenu && !loading ? (
        <div className="context-menu" style={{ left: contextMenu.x, top: contextMenu.y }} role="menu" aria-label="Операции со справочником" onClick={(event) => event.stopPropagation()}>
          <div className="context-menu-group" role="group">
            <button type="button" role="menuitem" onClick={() => void openEditor(contextMenu.section, 'create')}>
              <FileText size={15} aria-hidden="true" />
              <span>Добавить</span>
            </button>
          </div>
          <div className="context-menu-separator" role="separator" />
          <div className="context-menu-group" role="group">
            {isArchivedRecord(contextMenu.item) ? (
              <button type="button" role="menuitem" disabled={!canWriteActiveSection} onClick={() => {
                openRestoreTarget(contextMenu.section, contextMenu.item)
                setContextMenu(null)
              }}>
                <RotateCcw size={15} />
                <span>Вернуть</span>
              </button>
            ) : (
              <>
                <button type="button" role="menuitem" disabled={!canWriteActiveSection} onClick={() => void openEditor(contextMenu.section, 'edit', contextMenu.item)}>
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
                  <LocalizedDatePicker ariaLabel="Начало периода истории баланса" mode="month" value={balanceHistoryFilters.monthFrom} onChange={(monthFrom) => setBalanceHistoryFilters((value) => ({ ...value, monthFrom }))} required />
              </label>
              <label>
                Период по
                  <LocalizedDatePicker ariaLabel="Конец периода истории баланса" mode="month" value={balanceHistoryFilters.monthTo} onChange={(monthTo) => setBalanceHistoryFilters((value) => ({ ...value, monthTo }))} required />
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
                  {balanceHistory.rows.length === 0 ? <StatusMessage>По выбранному периоду строк нет</StatusMessage> : null}
                </div>
              </>
            ) : null}
          </section>
        </div>
      ) : null}

      {editor ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={saving === 'dictionary-editor' ? undefined : closeEditor}>
          <section ref={editorDialogRef} className={`detail-dialog dictionary-editor-dialog${editor.section === 'owners' ? ' dictionary-editor-dialog--owners' : ''}`} role="dialog" aria-modal="true" aria-labelledby="dictionary-editor-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">{editor.mode === 'create' ? 'Добавление' : 'Изменение'}</p>
                <h3 id="dictionary-editor-title">{dictionarySectionOptions.find((item) => item.key === editor.section)?.label ?? activeOption.label}</h3>
              </div>
              <button ref={editorCloseRef} className="icon-button" type="button" aria-label="Закрыть окно справочника" onClick={closeEditor} disabled={saving === 'dictionary-editor'}>
                <X size={18} />
              </button>
            </div>
            <form className="dictionary-modal-form" onSubmit={saveEditor}>
              <fieldset disabled={saving === 'dictionary-editor'}>
                {renderEditorFields(editor.section)}
              </fieldset>
              <FormValidationSummary title="Проверьте запись" items={validationErrors} />
              {error ? <FormError>{error}</FormError> : null}
              <div className="detail-dialog-actions">
                <button className="secondary-button" type="submit" disabled={saving === 'dictionary-editor'}>
                  <Save size={16} />
                  <span>{saving === 'dictionary-editor' ? 'Сохраняем...' : 'Сохранить'}</span>
                </button>
                <button className="ghost-button" type="button" onClick={closeEditor} disabled={saving === 'dictionary-editor'}>Отмена</button>
              </div>
            </form>
          </section>
        </div>
      ) : null}

      {pendingEditorConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={saving === 'dictionary-editor' ? undefined : () => setPendingEditorConfirmation(null)}>
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
            <ChangePreviewList ariaLabel="Изменяемые поля" changes={pendingEditorConfirmation.changes} />
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
        <div className="modal-backdrop" role="presentation" onMouseDown={saving === 'dictionary-archive' ? undefined : () => closeArchiveTarget()}>
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
            {error ? <FormError>{error}</FormError> : null}
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
        <div className="modal-backdrop" role="presentation" onMouseDown={saving === 'dictionary-restore' ? undefined : closeRestoreTarget}>
          <section ref={restoreDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="dictionary-restore-title" aria-describedby="dictionary-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление</p>
                <h3 id="dictionary-restore-title">Вернуть запись из архива?</h3>
                <p>{getDictionaryRecordTitle(restoreTarget.section, restoreTarget.item)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить восстановление" onClick={closeRestoreTarget} disabled={saving === 'dictionary-restore'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="dictionary-restore-description">Запись снова появится в рабочих списках. Действие будет записано в историю изменений.</p>
            {error ? <FormError>{error}</FormError> : null}
            <div className="detail-dialog-actions">
              <button ref={restoreCancelRef} className="ghost-button" type="button" onClick={closeRestoreTarget} disabled={saving === 'dictionary-restore'}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmRestore()} disabled={saving === 'dictionary-restore'}>
                <RotateCcw size={16} />
                <span>{saving === 'dictionary-restore' ? 'Возвращаем...' : 'Вернуть запись'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      <ToastViewport toast={toast} onDismiss={dismissToast} />
    </section>
  )
}
