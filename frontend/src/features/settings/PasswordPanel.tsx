import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { ArrowDownCircle, ArrowUpCircle, Banknote, CalendarClock, DatabaseBackup, Eye, FileWarning, KeyRound, Landmark, PlugZap, RefreshCw, ShieldCheck, X } from 'lucide-react'
import type { AuthClient, AuthResponse } from '../../services/authApi'
import type { IntegrationClient, OneCFreshIntegrationStatusDto, OneCFreshSyncDto, OneCFreshSyncPreviewDto, ReceiptPrintingIntegrationStatusDto } from '../../services/integrationsApi'
import type { ApplicationSettingsClient, BusinessDateChangePreviewDto, BusinessDateSettingsDto, CashBankBalanceSettingsDto, DatabaseBackupFileDto, DatabaseBackupStatusDto, DiagnosticLogStatusDto, SalaryAccrualSettingsDto } from '../../services/settingsApi'
import { hasPermission, isAdministrator, permissions } from '../../shared/accessControl'
import { AsyncErrorState, BackgroundRefreshStatus, EmptyState, LoadingSkeleton, StatusMessage } from '../../shared/AsyncState'
import { ChangePreviewList } from '../../shared/ChangePreviewList'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { MoneyTextInput } from '../../shared/MoneyInput'
import { parseMoneyInput } from '../../shared/moneyInputFormatting'
import { formatSensitiveChange } from '../../shared/changePreview'
import { FormField } from '../../shared/FormField'
import { FormError, FormValidationSummary } from '../../shared/formFeedback'
import { formatDateOnly, formatDateTime, formatMoney, formatOperationTime, getLocalDateInputValue } from '../../shared/formatters'
import { downloadBlob } from '../../shared/fileExports'
import { restoreFocusAfterClose, useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { ToastViewport } from '../../shared/Toast'
import { useToast } from '../../shared/useToast'
import { getPasswordChangeValidationErrors } from '../../shared/validation'
import { useActionCommentSettings } from '../../shared/ActionCommentSettings'

function SettingsDisplaySwitch({ title, label, checked, disabled, onChange }: {
  title: string
  label: string
  checked: boolean
  disabled?: boolean
  onChange: (checked: boolean) => void
}) {
  return (
    <label className="contractors-switch-row settings-display-switch">
      <strong>{title}</strong>
      <span className="contractors-switch-control">
        <input type="checkbox" aria-label={label} checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} />
      </span>
    </label>
  )
}

const tariffColumnSwitches = [
  { key: 'periodicity', title: 'Периодичность', label: 'Колонка «Периодичность»' },
  { key: 'accrualMonth', title: 'Месяц начисления', label: 'Колонка «Месяц начисления»' },
  { key: 'fundName', title: 'Название фонда', label: 'Показывать фонд под наименованием услуги' },
] as const

export function PasswordPanel({ auth, authClient, integrationClient, settingsClient, onSessionRevoked }: { auth: AuthResponse; authClient: AuthClient; integrationClient: IntegrationClient; settingsClient: ApplicationSettingsClient; onSessionRevoked: () => void }) {
  const [actionCommentsRequired, actionCommentSettingsLoading, actionCommentSettingsError, saveActionCommentsRequired] = useActionCommentSettings()
  const integrationSettingsVisible = import.meta.env.VITE_SHOW_INTEGRATION_SETTINGS === 'true'
  const dadataSettingsVisible = hasPermission(auth, permissions.usersManage)
  const integrationTabVisible = integrationSettingsVisible || dadataSettingsVisible
  const [activeSettingsTab, setActiveSettingsTab] = useState<'security' | 'business-date' | 'cash-bank' | 'display' | 'backups' | 'diagnostics' | 'integrations'>(() => (
    integrationSettingsVisible && (hasPermission(auth, permissions.importRun) || hasPermission(auth, permissions.paymentsWrite))
      ? 'integrations'
      : 'security'
  ))
  const [form, setForm] = useState({ currentPassword: '', newPassword: '', repeatPassword: '' })
  const [error, setError] = useState<string | null>(null)
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  const [pendingPasswordChange, setPendingPasswordChange] = useState<{ currentPassword: string; newPassword: string } | null>(null)
  const [saving, setSaving] = useState(false)
  const [oneCFreshStatus, setOneCFreshStatus] = useState<OneCFreshIntegrationStatusDto | null>(null)
  const [integrationLoading, setIntegrationLoading] = useState(false)
  const [integrationError, setIntegrationError] = useState<string | null>(null)
  const [oneCFreshSyncConfirmation, setOneCFreshSyncConfirmation] = useState<{ mode: 'preview' | 'start' | 'retry'; comment: string; error: string | null } | null>(null)
  const [oneCFreshSyncSaving, setOneCFreshSyncSaving] = useState(false)
  const [oneCFreshSyncMessage, setOneCFreshSyncMessage] = useState<string | null>(null)
  const [oneCFreshSyncResult, setOneCFreshSyncResult] = useState<OneCFreshSyncDto | null>(null)
  const [oneCFreshPreview, setOneCFreshPreview] = useState<OneCFreshSyncPreviewDto | null>(null)
  const oneCFreshSyncTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [receiptPrintingStatus, setReceiptPrintingStatus] = useState<ReceiptPrintingIntegrationStatusDto | null>(null)
  const [receiptPrintingLoading, setReceiptPrintingLoading] = useState(false)
  const [receiptPrintingError, setReceiptPrintingError] = useState<string | null>(null)
  const [oneCFreshToken, setOneCFreshToken] = useState('')
  const [receiptDeviceConnection, setReceiptDeviceConnection] = useState('')
  const [receiptTemplate, setReceiptTemplate] = useState('')
  const [dadataApiKey, setDadataApiKey] = useState('')
  const [protectedSettingSaving, setProtectedSettingSaving] = useState<string | null>(null)
  const [protectedSettingMessage, setProtectedSettingMessage] = useState<string | null>(null)
  const [protectedSettingError, setProtectedSettingError] = useState<string | null>(null)
  const [showAllGarageOperationsByDefault, setShowAllGarageOperationsByDefault] = useState(false)
  const [tariffTableColumns, setTariffTableColumns] = useState({ periodicity: false, accrualMonth: false, fundName: false })
  const [paymentDisplaySettingsVersion, setPaymentDisplaySettingsVersion] = useState<string | null>(null)
  const [tariffTableDisplaySettingsVersion, setTariffTableDisplaySettingsVersion] = useState<string | null>(null)
  const [paymentDisplaySettingsLoading, setPaymentDisplaySettingsLoading] = useState(false)
  const [paymentDisplaySettingsSaving, setPaymentDisplaySettingsSaving] = useState(false)
  const [paymentDisplaySettingsMessage, setPaymentDisplaySettingsMessage] = useState<string | null>(null)
  const [paymentDisplaySettingsError, setPaymentDisplaySettingsError] = useState<string | null>(null)
  const [backupStatus, setBackupStatus] = useState<DatabaseBackupStatusDto | null>(null)
  const [backupLoading, setBackupLoading] = useState(false)
  const [backupCreating, setBackupCreating] = useState(false)
  const [backupError, setBackupError] = useState<string | null>(null)
  const [backupMessage, setBackupMessage] = useState<string | null>(null)
  const [backupReloadToken, setBackupReloadToken] = useState(0)
  const [backupConfirmation, setBackupConfirmation] = useState<{ reason: string; error: string | null } | null>(null)
  const [backupDeleteConfirmation, setBackupDeleteConfirmation] = useState<{ backup: DatabaseBackupFileDto; reason: string; error: string | null } | null>(null)
  const [backupDownloadingFileName, setBackupDownloadingFileName] = useState<string | null>(null)
  const [backupDeleting, setBackupDeleting] = useState(false)
  const [diagnosticStatus, setDiagnosticStatus] = useState<DiagnosticLogStatusDto | null>(null)
  const [diagnosticLoading, setDiagnosticLoading] = useState(false)
  const [diagnosticExporting, setDiagnosticExporting] = useState(false)
  const [diagnosticError, setDiagnosticError] = useState<string | null>(null)
  const [diagnosticMessage, setDiagnosticMessage] = useState<string | null>(null)
  const [diagnosticReloadToken, setDiagnosticReloadToken] = useState(0)
  const [businessDateSettings, setBusinessDateSettings] = useState<BusinessDateSettingsDto | null>(null)
  const [businessDateDraft, setBusinessDateDraft] = useState('')
  const [businessDateLoading, setBusinessDateLoading] = useState(false)
  const [businessDateSaving, setBusinessDateSaving] = useState(false)
  const [businessDateError, setBusinessDateError] = useState<string | null>(null)
  const [businessDateMessage, setBusinessDateMessage] = useState<string | null>(null)
  const [businessDateConfirmation, setBusinessDateConfirmation] = useState<BusinessDateChangePreviewDto | null>(null)
  const [salaryAccrualSettings, setSalaryAccrualSettings] = useState<SalaryAccrualSettingsDto | null>(null)
  const [salaryAccrualDayDraft, setSalaryAccrualDayDraft] = useState('1')
  const [salaryAccrualSaving, setSalaryAccrualSaving] = useState(false)
  const [salaryAccrualMessage, setSalaryAccrualMessage] = useState<string | null>(null)
  const [cashBankSettings, setCashBankSettings] = useState<CashBankBalanceSettingsDto | null>(null)
  const [cashBankLoading, setCashBankLoading] = useState(false)
  const [cashBankSaving, setCashBankSaving] = useState(false)
  const [cashBankError, setCashBankError] = useState<string | null>(null)
  const { toast, showToast, dismissToast } = useToast()
  const [settingsReloadRevision, setSettingsReloadRevision] = useState(0)
  const [balanceAdjustmentDraft, setBalanceAdjustmentDraft] = useState<{
    account: 'cash' | 'bank'
    direction: 'increase' | 'decrease'
    operationDate: string
    amount: string
    reason: string
  } | null>(null)
  const canViewIntegrationStatus = integrationSettingsVisible && hasPermission(auth, permissions.importRun)
  const canViewReceiptPrintingStatus = integrationSettingsVisible && hasPermission(auth, permissions.paymentsWrite)
  const canManageIntegrationSettings = integrationSettingsVisible && hasPermission(auth, permissions.usersManage)
  const canManageDadataSettings = dadataSettingsVisible
  const canManageApplicationSettings = hasPermission(auth, permissions.usersManage)
  const canManageBusinessDate = isAdministrator(auth)
  useRestoreFocusOnClose(Boolean(pendingPasswordChange))
  const confirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingPasswordChange))
  const confirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingPasswordChange))
  const oneCFreshSyncCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(oneCFreshSyncConfirmation))
  const oneCFreshSyncDialogRef = useFocusTrap<HTMLElement>(Boolean(oneCFreshSyncConfirmation))
  useRestoreFocusOnClose(Boolean(balanceAdjustmentDraft))
  const balanceAdjustmentCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(balanceAdjustmentDraft))
  const balanceAdjustmentDialogRef = useFocusTrap<HTMLElement>(Boolean(balanceAdjustmentDraft))
  useEscapeKey(Boolean(pendingPasswordChange) && !saving, () => setPendingPasswordChange(null))
  useEscapeKey(Boolean(oneCFreshSyncConfirmation) && !oneCFreshSyncSaving, () => closeOneCFreshSyncConfirmation())
  useEscapeKey(Boolean(businessDateConfirmation) && !businessDateSaving, () => setBusinessDateConfirmation(null))
  useEscapeKey(Boolean(balanceAdjustmentDraft) && !cashBankSaving, () => closeBalanceAdjustment())

  useEffect(() => {
    if (!canManageBusinessDate || activeSettingsTab !== 'business-date') return
    let ignore = false
    const controller = new AbortController()
    setBusinessDateLoading(true)
    setBusinessDateError(null)
    Promise.all([
      settingsClient.getBusinessDateSettings(auth.accessToken, controller.signal),
      settingsClient.getSalaryAccrualSettings(auth.accessToken, controller.signal),
    ])
      .then(([settings, salarySettings]) => {
        if (ignore) return
        setBusinessDateSettings(settings)
        setBusinessDateDraft(settings.overrideDate ?? settings.systemDate)
        setSalaryAccrualSettings(salarySettings)
        setSalaryAccrualDayDraft(String(salarySettings.accrualDay))
      })
      .catch((caught: unknown) => {
        if (!ignore) setBusinessDateError(caught instanceof Error ? caught.message : 'Не удалось загрузить рабочую дату.')
      })
      .finally(() => {
        if (!ignore) setBusinessDateLoading(false)
      })
    return () => {
      ignore = true
      controller.abort()
    }
  }, [activeSettingsTab, auth.accessToken, canManageBusinessDate, settingsClient, settingsReloadRevision])

  useEffect(() => {
    if (!canManageBusinessDate || activeSettingsTab !== 'cash-bank') return
    let ignore = false
    const controller = new AbortController()
    setCashBankLoading(true)
    setCashBankError(null)
    settingsClient.getCashBankBalances(auth.accessToken, controller.signal)
      .then((settings) => {
        if (ignore) return
        setCashBankSettings(settings)
      })
      .catch((caught: unknown) => {
        if (!ignore) setCashBankError(caught instanceof Error ? caught.message : 'Не удалось загрузить остатки кассы и банковского счёта.')
      })
      .finally(() => {
        if (!ignore) setCashBankLoading(false)
      })
    return () => {
      ignore = true
      controller.abort()
    }
  }, [activeSettingsTab, auth.accessToken, canManageBusinessDate, settingsClient, settingsReloadRevision])

  useEffect(() => {
    if (!canManageApplicationSettings || activeSettingsTab !== 'display') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    setPaymentDisplaySettingsLoading(true)
    setPaymentDisplaySettingsError(null)
    settingsClient.getPaymentDisplaySettings(auth.accessToken, controller.signal)
      .then((settings) => {
        if (!ignore) {
          setShowAllGarageOperationsByDefault(settings.showAllGarageOperationsByDefault)
          setPaymentDisplaySettingsVersion(settings.version)
          setTariffTableColumns({
            periodicity: settings.showPeriodicityColumn,
            accrualMonth: settings.showAccrualMonthColumn,
            fundName: settings.showFundName,
          })
          setTariffTableDisplaySettingsVersion(settings.tariffTableVersion)
        }
      })
      .catch((caught: unknown) => {
        if (!ignore) {
          setPaymentDisplaySettingsError(caught instanceof Error ? caught.message : 'Не удалось загрузить.')
        }
      })
      .finally(() => {
        if (!ignore) {
          setPaymentDisplaySettingsLoading(false)
        }
      })

    return () => {
      ignore = true
      controller.abort()
    }
  }, [activeSettingsTab, auth.accessToken, canManageApplicationSettings, settingsClient, settingsReloadRevision])

  useEffect(() => {
    if (!canManageApplicationSettings || activeSettingsTab !== 'backups') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    setBackupLoading(true)
    setBackupError(null)
    settingsClient.getDatabaseBackups(auth.accessToken, controller.signal)
      .then((status) => {
        if (!ignore) {
          setBackupStatus(status)
        }
      })
      .catch((caught: unknown) => {
        if (!ignore) {
          setBackupError(caught instanceof Error ? caught.message : 'Не удалось загрузить состояние резервного копирования.')
        }
      })
      .finally(() => {
        if (!ignore) {
          setBackupLoading(false)
        }
      })

    return () => {
      ignore = true
      controller.abort()
    }
  }, [activeSettingsTab, auth.accessToken, backupReloadToken, canManageApplicationSettings, settingsClient])

  useEffect(() => {
    if (!canManageApplicationSettings || activeSettingsTab !== 'diagnostics') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    setDiagnosticLoading(true)
    setDiagnosticError(null)
    settingsClient.getDiagnosticLogStatus(auth.accessToken, controller.signal)
      .then((status) => {
        if (!ignore) {
          setDiagnosticStatus(status)
        }
      })
      .catch((caught: unknown) => {
        if (!ignore) {
          setDiagnosticError(caught instanceof Error ? caught.message : 'Не удалось загрузить состояние журнала ошибок.')
        }
      })
      .finally(() => {
        if (!ignore) {
          setDiagnosticLoading(false)
        }
      })

    return () => {
      ignore = true
      controller.abort()
    }
  }, [activeSettingsTab, auth.accessToken, canManageApplicationSettings, diagnosticReloadToken, settingsClient])

  async function exportDiagnosticPackage() {
    setDiagnosticExporting(true)
    setDiagnosticError(null)
    setDiagnosticMessage(null)
    try {
      const packageBlob = await settingsClient.createDiagnosticPackage(auth.accessToken)
      downloadBlob(packageBlob, buildDiagnosticPackageFileName())
      setDiagnosticMessage('Диагностический пакет подготовлен. Перед передачей проверьте, кому отправляется файл.')
      setDiagnosticReloadToken((value) => value + 1)
    } catch (caught) {
      setDiagnosticError(caught instanceof Error ? caught.message : 'Не удалось сформировать диагностический пакет.')
    } finally {
      setDiagnosticExporting(false)
    }
  }

  async function createDatabaseBackup() {
    if (!backupConfirmation) {
      return
    }

    const reason = backupConfirmation.reason.trim()
    if (actionCommentsRequired && reason.length < 3) {
      setBackupConfirmation({ ...backupConfirmation, error: 'Укажите причину длиной не менее 3 символов.' })
      return
    }

    setBackupCreating(true)
    setBackupError(null)
    setBackupMessage(null)
    try {
      const created = await settingsClient.createDatabaseBackup(auth.accessToken, { reason })
      setBackupStatus((current) => current ? {
        ...current,
        isRunning: false,
        lastSuccessfulBackupAtUtc: created.createdAtUtc,
        backups: [created, ...current.backups.filter((backup) => backup.fileName !== created.fileName)],
      } : current)
      setBackupMessage(`Резервная копия ${created.fileName} создана и проверена.`)
      setBackupConfirmation(null)
      setBackupReloadToken((value) => value + 1)
    } catch (caught) {
      setBackupConfirmation((current) => current ? {
        ...current,
        error: caught instanceof Error ? caught.message : 'Не удалось создать резервную копию базы данных.',
      } : current)
    } finally {
      setBackupCreating(false)
    }
  }

  async function savePaymentDisplaySettings() {
    setPaymentDisplaySettingsSaving(true)
    setPaymentDisplaySettingsMessage(null)
    setPaymentDisplaySettingsError(null)
    try {
      const settings = await settingsClient.updatePaymentDisplaySettings(auth.accessToken, {
        showAllGarageOperationsByDefault,
        version: paymentDisplaySettingsVersion ?? '',
        showPeriodicityColumn: tariffTableColumns.periodicity,
        showAccrualMonthColumn: tariffTableColumns.accrualMonth,
        tariffTableVersion: tariffTableDisplaySettingsVersion ?? '',
        showFundName: tariffTableColumns.fundName,
      })
      setShowAllGarageOperationsByDefault(settings.showAllGarageOperationsByDefault)
      setPaymentDisplaySettingsVersion(settings.version)
      setTariffTableDisplaySettingsVersion(settings.tariffTableVersion)
      setPaymentDisplaySettingsMessage('Отображение сохранено.')
    } catch (caught) {
      setPaymentDisplaySettingsError(caught instanceof Error ? caught.message : 'Не удалось сохранить.')
    } finally {
      setPaymentDisplaySettingsSaving(false)
    }
  }

  async function downloadDatabaseBackup(backup: DatabaseBackupFileDto) {
    setBackupDownloadingFileName(backup.fileName)
    setBackupError(null)
    setBackupMessage(null)
    try {
      const blob = await settingsClient.downloadDatabaseBackup(auth.accessToken, backup.fileName)
      downloadBlob(blob, backup.fileName)
      setBackupMessage(`Резервная копия ${backup.fileName} скачана.`)
    } catch (caught) {
      setBackupError(caught instanceof Error ? caught.message : 'Не удалось скачать резервную копию.')
    } finally {
      setBackupDownloadingFileName(null)
    }
  }

  async function deleteDatabaseBackup() {
    if (!backupDeleteConfirmation) return
    const reason = backupDeleteConfirmation.reason.trim()
    if ((actionCommentsRequired && reason.length < 3) || reason.length > 500) {
      setBackupDeleteConfirmation({
        ...backupDeleteConfirmation,
        error: 'Укажите причину длиной от 3 до 500 символов.',
      })
      return
    }

    setBackupDeleting(true)
    setBackupError(null)
    setBackupMessage(null)
    try {
      const deleted = await settingsClient.deleteDatabaseBackup(
        auth.accessToken,
        backupDeleteConfirmation.backup.fileName,
        { reason },
      )
      setBackupStatus((current) => current ? {
        ...current,
        backups: current.backups.filter((backup) => backup.fileName !== deleted.fileName),
      } : current)
      setBackupDeleteConfirmation(null)
      setBackupMessage(`Резервная копия ${deleted.fileName} удалена. Действие записано в историю изменений.`)
    } catch (caught) {
      setBackupDeleteConfirmation((current) => current ? {
        ...current,
        error: caught instanceof Error ? caught.message : 'Не удалось удалить резервную копию.',
      } : current)
    } finally {
      setBackupDeleting(false)
    }
  }

  async function confirmBusinessDateChange() {
    if (!businessDateConfirmation) return
    setBusinessDateSaving(true)
    setBusinessDateError(null)
    setBusinessDateMessage(null)
    try {
      const settings = await settingsClient.updateBusinessDateSettings(auth.accessToken, {
        overrideDate: businessDateConfirmation.overrideDate,
        version: businessDateConfirmation.version,
      })
      setBusinessDateSettings(settings)
      setBusinessDateDraft(settings.overrideDate ?? settings.systemDate)
      setBusinessDateConfirmation(null)
      setBusinessDateMessage(settings.automation?.message ?? (settings.isOverrideActive
        ? `Рабочая дата установлена: ${formatBusinessDate(settings.effectiveDate)}.`
        : 'Восстановлена автоматическая системная дата.'))
    } catch (caught) {
      setBusinessDateError(caught instanceof Error ? caught.message : 'Не удалось изменить рабочую дату.')
    } finally {
      setBusinessDateSaving(false)
    }
  }

  async function previewBusinessDateChange(overrideDate: string | null) {
    setBusinessDateSaving(true)
    setBusinessDateError(null)
    setBusinessDateMessage(null)
    try {
      const preview = await settingsClient.previewBusinessDateChange(auth.accessToken, {
        overrideDate,
        version: businessDateSettings?.version,
      })
      setBusinessDateConfirmation(preview)
    } catch (caught) {
      setBusinessDateError(caught instanceof Error ? caught.message : 'Не удалось проверить рабочую дату.')
    } finally {
      setBusinessDateSaving(false)
    }
  }

  async function saveSalaryAccrualSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const accrualDay = Number(salaryAccrualDayDraft)
    if (!Number.isInteger(accrualDay) || accrualDay < 1 || accrualDay > 28) {
      setBusinessDateError('День начисления зарплаты должен быть от 1 до 28.')
      return
    }

    setSalaryAccrualSaving(true)
    setBusinessDateError(null)
    setSalaryAccrualMessage(null)
    try {
      const settings = await settingsClient.updateSalaryAccrualSettings(auth.accessToken, {
        accrualDay,
        version: salaryAccrualSettings?.version ?? '',
      })
      setSalaryAccrualSettings(settings)
      setSalaryAccrualDayDraft(String(settings.accrualDay))
      setSalaryAccrualMessage(`Зарплата активным сотрудникам будет начисляться автоматически ${settings.accrualDay}-го числа каждого месяца.`)
    } catch (caught) {
      setBusinessDateError(caught instanceof Error ? caught.message : 'Не удалось сохранить день начисления зарплаты.')
    } finally {
      setSalaryAccrualSaving(false)
    }
  }

  async function saveBalanceAdjustment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!balanceAdjustmentDraft) return
    const amount = parseMoneyDraft(balanceAdjustmentDraft.amount)
    const reason = balanceAdjustmentDraft.reason.trim()
    if (amount === null || amount <= 0) {
      setCashBankError('Сумма операции должна быть больше нуля.')
      return
    }
    if (!balanceAdjustmentDraft.operationDate) {
      setCashBankError('Укажите дату операции.')
      return
    }
    if (actionCommentsRequired && reason.length < 3) {
      setCashBankError('Укажите причину операции длиной не менее 3 символов.')
      return
    }

    setCashBankSaving(true)
    setCashBankError(null)
    try {
      const completedOperation = balanceAdjustmentDraft
      const settings = await settingsClient.createCashBankBalanceAdjustment(auth.accessToken, {
        ...balanceAdjustmentDraft,
        amount,
        reason,
      })
      setCashBankSettings(settings)
      setBalanceAdjustmentDraft(null)
      const accountName = completedOperation.account === 'cash' ? 'кассы' : 'банковского счёта'
      const operationName = completedOperation.direction === 'increase' ? 'Пополнение' : 'Списание'
      showToast('Операция проведена и записана в историю изменений.', 'success', `${operationName} ${accountName} выполнено`)
    } catch (caught) {
      setCashBankError(caught instanceof Error ? caught.message : 'Не удалось провести операцию.')
    } finally {
      setCashBankSaving(false)
    }
  }

  function openBalanceAdjustment(
    account: 'cash' | 'bank',
    direction: 'increase' | 'decrease',
  ) {
    setCashBankError(null)
    setBalanceAdjustmentDraft({
      account,
      direction,
      operationDate: getLocalDateInputValue(),
      amount: '',
      reason: '',
    })
  }

  function closeBalanceAdjustment() {
    if (cashBankSaving) return
    setBalanceAdjustmentDraft(null)
    setCashBankError(null)
  }

  useEffect(() => {
    if (!canViewIntegrationStatus || activeSettingsTab !== 'integrations') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    async function loadOneCFreshStatus() {
      await Promise.resolve()
      if (ignore) {
        return
      }

      setIntegrationLoading(true)
      setIntegrationError(null)
      try {
        const status = await integrationClient.getOneCFreshStatus(auth.accessToken, controller.signal)
        if (!ignore) {
          setOneCFreshStatus(status)
        }
      } catch (caught: unknown) {
        if (!ignore) {
          setOneCFreshStatus(null)
          setIntegrationError(caught instanceof Error ? caught.message : 'Не удалось загрузить статус 1C Fresh.')
        }
      } finally {
        if (!ignore) {
          setIntegrationLoading(false)
        }
      }
    }

    void loadOneCFreshStatus()

    return () => {
      ignore = true
      controller.abort()
    }
  }, [activeSettingsTab, auth.accessToken, canViewIntegrationStatus, integrationClient, settingsReloadRevision])

  useEffect(() => {
    if (!canViewReceiptPrintingStatus || activeSettingsTab !== 'integrations') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    async function loadReceiptPrintingStatus() {
      await Promise.resolve()
      if (ignore) {
        return
      }

      setReceiptPrintingLoading(true)
      setReceiptPrintingError(null)
      try {
        const status = await integrationClient.getReceiptPrintingStatus(auth.accessToken, controller.signal)
        if (!ignore) {
          setReceiptPrintingStatus(status)
        }
      } catch (caught: unknown) {
        if (!ignore) {
          setReceiptPrintingStatus(null)
          setReceiptPrintingError(caught instanceof Error ? caught.message : 'Не удалось загрузить статус печати чеков и квитанций.')
        }
      } finally {
        if (!ignore) {
          setReceiptPrintingLoading(false)
        }
      }
    }

    void loadReceiptPrintingStatus()

    return () => {
      ignore = true
      controller.abort()
    }
  }, [activeSettingsTab, auth.accessToken, canViewReceiptPrintingStatus, integrationClient, settingsReloadRevision])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)

    const errors = getPasswordChangeValidationErrors(form.currentPassword, form.newPassword, form.repeatPassword)
    if (errors.length > 0) {
      setValidationErrors(errors)
      return
    }

    setValidationErrors([])
    setPendingPasswordChange({
      currentPassword: form.currentPassword,
      newPassword: form.newPassword,
    })
  }

  async function confirmPasswordChange() {
    if (!pendingPasswordChange) {
      return
    }

    setSaving(true)
    setError(null)
    try {
      await authClient.changeOwnPassword(auth.accessToken, {
        currentPassword: pendingPasswordChange.currentPassword,
        newPassword: pendingPasswordChange.newPassword,
      })
      setPendingPasswordChange(null)
      setForm({ currentPassword: '', newPassword: '', repeatPassword: '' })
      onSessionRevoked()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось изменить пароль.')
    } finally {
      setSaving(false)
    }
  }

  function openOneCFreshSyncConfirmation(trigger: HTMLButtonElement, mode: 'preview' | 'start' | 'retry' = 'start') {
    oneCFreshSyncTriggerRef.current = trigger
    setIntegrationError(null)
    if (mode === 'preview' || mode === 'start') {
      setOneCFreshSyncMessage(null)
      setOneCFreshSyncResult(null)
      setOneCFreshPreview(null)
    }
    setOneCFreshSyncConfirmation({ mode, comment: '', error: null })
  }

  function closeOneCFreshSyncConfirmation() {
    setOneCFreshSyncConfirmation(null)
    restoreFocusAfterClose(oneCFreshSyncTriggerRef)
  }

  async function confirmOneCFreshSync() {
    if (!oneCFreshSyncConfirmation) {
      return
    }

    setOneCFreshSyncSaving(true)
    setOneCFreshSyncConfirmation((state) => state ? { ...state, error: null } : state)
    try {
      const request = {
        comment: oneCFreshSyncConfirmation.comment.trim() || undefined,
      }
      if (oneCFreshSyncConfirmation.mode === 'preview') {
        const preview = await integrationClient.previewOneCFreshSync(auth.accessToken, request)
        closeOneCFreshSyncConfirmation()
        setOneCFreshSyncMessage(preview.statusMessage)
        setOneCFreshPreview(preview)
        setOneCFreshSyncResult(null)
        return
      }

      const result = oneCFreshSyncConfirmation.mode === 'retry'
        ? await integrationClient.retryOneCFreshSync(auth.accessToken, request)
        : await integrationClient.startOneCFreshSync(auth.accessToken, request)
      closeOneCFreshSyncConfirmation()
      setOneCFreshSyncMessage(result.statusMessage)
      setOneCFreshSyncResult(result)
    } catch (caught) {
      setOneCFreshSyncConfirmation((state) => state ? { ...state, error: caught instanceof Error ? caught.message : 'Не удалось отправить запрос синхронизации 1C Fresh.' } : state)
    } finally {
      setOneCFreshSyncSaving(false)
    }
  }

  async function saveProtectedSetting(provider: string, settingKey: string, plaintextValue: string, clearValue: () => void) {
    const value = plaintextValue.trim()
    setProtectedSettingMessage(null)
    setProtectedSettingError(null)
    if (!value) {
      setProtectedSettingError('Введите защищенное значение перед сохранением.')
      return
    }

    const savingKey = `${provider}:${settingKey}`
    setProtectedSettingSaving(savingKey)
    try {
      const setting = await integrationClient.updateProtectedSetting(auth.accessToken, provider, settingKey, value)
      clearValue()
      setProtectedSettingMessage(`Защищенная настройка ${setting.provider}:${setting.settingKey} сохранена. Значение повторно не отображается.`)
      if (setting.provider === 'OneCFresh') {
        setOneCFreshStatus((state) => state ? {
          ...state,
          isConfigured: true,
          status: state.canSynchronize ? 'ready' : 'prepared',
          statusMessage: 'Токен сохранен. Проверяем готовность адаптера 1C Fresh...',
          configuredSettings: Array.from(new Set([...state.configuredSettings, setting.settingKey])),
          lastProtectedSettingUpdatedAtUtc: setting.updatedAtUtc,
        } : state)
      } else if (setting.provider === 'ReceiptPrinting') {
        setReceiptPrintingStatus((state) => {
          if (!state) return state
          const configuredSettings = Array.from(new Set([...state.configuredSettings, setting.settingKey]))
          const isConfigured = state.requiredSettings.every((key) => configuredSettings.includes(key))
          return {
            ...state,
            configuredSettings,
            isConfigured,
            status: isConfigured ? 'prepared' : 'not_configured',
            statusMessage: isConfigured
              ? 'Защищенные настройки печати сохранены. Проверяем готовность адаптера...'
              : 'Для печати нужно сохранить защищенные настройки ReceiptPrinting:DeviceConnection и ReceiptPrinting:ReceiptTemplate.',
            lastProtectedSettingUpdatedAtUtc: setting.updatedAtUtc,
          }
        })
      }
      setSettingsReloadRevision((revision) => revision + 1)
    } catch (caught) {
      setProtectedSettingError(caught instanceof Error ? caught.message : 'Не удалось сохранить защищенную настройку.')
    } finally {
      setProtectedSettingSaving(null)
    }
  }

  return (
    <>
      <section className="settings-layout" aria-label="Настройки">
        <aside className="settings-section-nav">
          <div>
            <p className="eyebrow">Настройки</p>
            <h1>Настройки</h1>
            <p>Выберите раздел для управления параметрами системы и своей учетной записи.</p>
          </div>
          <div className="settings-tab-list" role="tablist" aria-label="Разделы настроек" aria-orientation="vertical">
            <button
              id="settings-security-tab"
              className={activeSettingsTab === 'security' ? 'settings-tab is-active' : 'settings-tab'}
              type="button"
              role="tab"
              aria-controls="settings-security-panel"
              aria-selected={activeSettingsTab === 'security'}
              onClick={() => setActiveSettingsTab('security')}
            >
              <KeyRound size={17} aria-hidden="true" />
              <span>Безопасность</span>
            </button>
            {canManageBusinessDate ? (
              <button
                id="settings-business-date-tab"
                className={activeSettingsTab === 'business-date' ? 'settings-tab is-active' : 'settings-tab'}
                type="button"
                role="tab"
                aria-controls="settings-business-date-panel"
                aria-selected={activeSettingsTab === 'business-date'}
                onClick={() => setActiveSettingsTab('business-date')}
              >
                <CalendarClock size={17} aria-hidden="true" />
                <span>Рабочая дата</span>
              </button>
            ) : null}
            {canManageBusinessDate ? (
              <button
                id="settings-cash-bank-tab"
                className={activeSettingsTab === 'cash-bank' ? 'settings-tab is-active' : 'settings-tab'}
                type="button"
                role="tab"
                aria-controls="settings-cash-bank-panel"
                aria-selected={activeSettingsTab === 'cash-bank'}
                onClick={() => setActiveSettingsTab('cash-bank')}
              >
                <Landmark size={17} aria-hidden="true" />
                <span>Касса и счёт</span>
              </button>
            ) : null}
            {canManageApplicationSettings ? (
              <button
                id="settings-display-tab"
                className={activeSettingsTab === 'display' ? 'settings-tab is-active' : 'settings-tab'}
                type="button"
                role="tab"
                aria-controls="settings-display-panel"
                aria-selected={activeSettingsTab === 'display'}
                onClick={() => setActiveSettingsTab('display')}
              >
                <Eye size={17} aria-hidden="true" />
                <span>Отображение</span>
              </button>
            ) : null}
            {canManageApplicationSettings ? (
              <button
                id="settings-backups-tab"
                className={activeSettingsTab === 'backups' ? 'settings-tab is-active' : 'settings-tab'}
                type="button"
                role="tab"
                aria-controls="settings-backups-panel"
                aria-selected={activeSettingsTab === 'backups'}
                onClick={() => setActiveSettingsTab('backups')}
              >
                <DatabaseBackup size={17} aria-hidden="true" />
                <span>Резервные копии</span>
              </button>
            ) : null}
            {canManageApplicationSettings ? (
              <button
                id="settings-diagnostics-tab"
                className={activeSettingsTab === 'diagnostics' ? 'settings-tab is-active' : 'settings-tab'}
                type="button"
                role="tab"
                aria-controls="settings-diagnostics-panel"
                aria-selected={activeSettingsTab === 'diagnostics'}
                onClick={() => setActiveSettingsTab('diagnostics')}
              >
                <FileWarning size={17} aria-hidden="true" />
                <span>Диагностика</span>
              </button>
            ) : null}
            {integrationTabVisible ? (
              <button
                id="settings-integrations-tab"
                className={activeSettingsTab === 'integrations' ? 'settings-tab is-active' : 'settings-tab'}
                type="button"
                role="tab"
                aria-controls="settings-integrations-panel"
                aria-selected={activeSettingsTab === 'integrations'}
                onClick={() => setActiveSettingsTab('integrations')}
              >
                <PlugZap size={17} aria-hidden="true" />
                <span>Интеграции</span>
              </button>
            ) : null}
          </div>
        </aside>
        <div
          className="settings-section-content"
          id={`settings-${activeSettingsTab}-panel`}
          role="tabpanel"
          aria-labelledby={`settings-${activeSettingsTab}-tab`}
        >
      {activeSettingsTab === 'security' ? (
      <section className="password-panel settings-card settings-card--security" aria-label="Безопасность аккаунта">
        <div className="settings-card-intro">
          <p className="eyebrow">Безопасность</p>
          <h2>Смена пароля</h2>
          <p>Пользователь может обновить свой пароль без участия администратора. Текущий пароль нужен для подтверждения действия.</p>
        </div>
        <form className="dictionary-form settings-card-form" onSubmit={handleSubmit}>
          <label>
            Текущий пароль
            <input aria-label="Текущий пароль" type="password" value={form.currentPassword} onChange={(event) => setForm({ ...form, currentPassword: event.target.value })} minLength={8} required />
          </label>
          <div className="inline-fields">
            <label>
              Новый пароль
              <input aria-label="Новый пароль" aria-describedby="own-password-policy-hint" type="password" value={form.newPassword} onChange={(event) => setForm({ ...form, newPassword: event.target.value })} minLength={8} required />
            </label>
            <label>
              Повтор нового пароля
              <input aria-label="Повтор нового пароля" aria-describedby="own-password-policy-hint" type="password" value={form.repeatPassword} onChange={(event) => setForm({ ...form, repeatPassword: event.target.value })} minLength={8} required />
            </label>
          </div>
          <p className="form-hint" id="own-password-policy-hint">Минимум 8 символов.</p>
          <FormValidationSummary title="Проверьте смену пароля" items={validationErrors} />
          {error && !pendingPasswordChange ? <FormError>{error}</FormError> : null}
          <button className="secondary-button" type="submit" disabled={saving || Boolean(pendingPasswordChange)}>
            <ShieldCheck size={16} />
            <span>{saving ? 'Сохраняем...' : 'Изменить пароль'}</span>
          </button>
        </form>
      </section>
      ) : null}
      {canManageBusinessDate && activeSettingsTab === 'business-date' ? (
      <section className="password-panel settings-card settings-card--business-date" aria-label="Эмулятор рабочей даты">
        <div className="settings-card-intro">
          <p className="eyebrow">Администрирование</p>
          <h2>Эмулятор рабочей даты</h2>
          <p>Позволяет безопасно проверить начисления, перенос долга в просроченный и попадание гаражей в список должников без изменения дат документов и технических журналов.</p>
        </div>
        <div className="dictionary-form settings-card-form">
          {businessDateLoading && !businessDateSettings ? <LoadingSkeleton className="loading-skeleton--compact" label="Загружаем рабочую дату" rows={2} columns={3} /> : null}
          {businessDateLoading && businessDateSettings ? <BackgroundRefreshStatus label="Обновляем рабочую дату" /> : null}
          {businessDateSettings ? (
            <>
              <div className="summary-strip" aria-label="Состояние рабочей даты">
                <div><span>Системная дата</span><strong>{formatBusinessDate(businessDateSettings.systemDate)}</strong></div>
                <div><span>Рабочая дата</span><strong className={businessDateSettings.isOverrideActive ? 'warning-text' : 'status-active'}>{formatBusinessDate(businessDateSettings.effectiveDate)}</strong></div>
                <div><span>Режим</span><strong>{businessDateSettings.isOverrideActive ? 'Тестовая дата' : 'Автоматически'}</strong></div>
              </div>
              {businessDateSettings.isOverrideActive ? (
                <div className="form-warning" role="status">Расчёты сейчас выполняются на тестовую дату. Верните системную дату после проверки.</div>
              ) : null}
              <FormField label="Новая рабочая дата">
                <LocalizedDatePicker ariaLabel="Новая рабочая дата" mode="date" value={businessDateDraft} disabled={businessDateSaving} onChange={(value) => { setBusinessDateDraft(value); setBusinessDateMessage(null) }} required />
              </FormField>
              <p className="form-hint">Изменение применяется после предварительного просмотра и подтверждения.</p>
              <div className="dialog-actions dialog-actions--start">
                <button className="secondary-button" type="button" disabled={!businessDateDraft || businessDateSaving} onClick={() => void previewBusinessDateChange(businessDateDraft)}>
                  <CalendarClock size={16} aria-hidden="true" />
                  <span>{businessDateSaving ? 'Проверяем влияние...' : 'Проверить и установить дату'}</span>
                </button>
                <button className="ghost-button" type="button" disabled={!businessDateSettings.isOverrideActive || businessDateSaving} onClick={() => void previewBusinessDateChange(null)}>Проверить возврат системной даты</button>
              </div>
              <form className="dictionary-form settings-card-form" aria-label="Настройка автоматического начисления зарплаты" onSubmit={(event) => void saveSalaryAccrualSettings(event)}>
                <FormField label="День начисления зарплаты">
                  <input
                    aria-label="День начисления зарплаты"
                    type="number"
                    min="1"
                    max="28"
                    step="1"
                    value={salaryAccrualDayDraft}
                    disabled={salaryAccrualSaving}
                    onChange={(event) => {
                      setSalaryAccrualDayDraft(event.target.value)
                      setSalaryAccrualMessage(null)
                    }}
                    required
                  />
                </FormField>
                <p className="form-hint">В выбранный день оклад автоматически попадает в ведомость всем активным сотрудникам. Дни 1–28 работают одинаково во всех месяцах.</p>
                <div className="dialog-actions dialog-actions--start">
                  <button className="secondary-button" type="submit" disabled={salaryAccrualSaving || salaryAccrualSettings?.accrualDay === Number(salaryAccrualDayDraft)}>
                    <CalendarClock size={16} aria-hidden="true" />
                    <span>{salaryAccrualSaving ? 'Сохраняем...' : 'Сохранить день начисления'}</span>
                  </button>
                </div>
                {salaryAccrualMessage ? <div className="form-success" role="status" aria-live="polite">{salaryAccrualMessage}</div> : null}
              </form>
            </>
          ) : null}
          {businessDateError && !businessDateSettings ? (
            <AsyncErrorState message={businessDateError} onRetry={() => setSettingsReloadRevision((value) => value + 1)} retrying={businessDateLoading} />
          ) : businessDateError ? <FormError>{businessDateError}</FormError> : null}
          {businessDateMessage ? <div className="form-success" role="status" aria-live="polite">{businessDateMessage}</div> : null}
        </div>
      </section>
      ) : null}
      {canManageBusinessDate && activeSettingsTab === 'cash-bank' ? (
      <section className="password-panel settings-card settings-card--cash-bank" aria-label="Остатки кассы и банковского счёта">
        <div className="settings-card-intro">
          <p className="eyebrow">Финансовые настройки</p>
          <h2>Касса и банковский счёт</h2>
          <p>Текущие остатки изменяются отдельными операциями пополнения и списания. Каждая операция сохраняет дату, время и причину в истории.</p>
        </div>
        <div className="dictionary-form settings-card-form cash-bank-settings">
          {cashBankLoading && !cashBankSettings ? <LoadingSkeleton className="loading-skeleton--compact" label="Загружаем остатки кассы и банковского счёта" rows={3} columns={4} /> : null}
          {cashBankLoading && cashBankSettings ? <BackgroundRefreshStatus label="Обновляем остатки кассы и банковского счёта" /> : null}
          {cashBankSettings ? (
            <>
              <div className="summary-strip cash-bank-summary" aria-label="Текущие остатки">
                <div>
                  <span>Касса сейчас</span>
                  <strong>{formatMoney(cashBankSettings.cashCurrentBalance)} ₽</strong>
                </div>
                <div>
                  <span>Счёт сейчас</span>
                  <strong>{formatMoney(cashBankSettings.bankCurrentBalance)} ₽</strong>
                </div>
              </div>

              <div className="cash-bank-action-groups" aria-label="Операции с остатками">
                {(['cash', 'bank'] as const).map((account) => (
                  <div className="cash-bank-action-card" key={account}>
                    <div>
                      {account === 'cash' ? <Banknote size={20} aria-hidden="true" /> : <Landmark size={20} aria-hidden="true" />}
                      <strong>{account === 'cash' ? 'Касса' : 'Банковский счёт'}</strong>
                    </div>
                    <div className="dialog-actions dialog-actions--start">
                      <button className="secondary-button create-action-button" type="button" disabled={cashBankSaving} onClick={() => openBalanceAdjustment(account, 'increase')}>
                        <ArrowUpCircle size={17} aria-hidden="true" />
                        <span>Пополнить</span>
                      </button>
                      <button className="ghost-button create-action-button" type="button" disabled={cashBankSaving} onClick={() => openBalanceAdjustment(account, 'decrease')}>
                        <ArrowDownCircle size={17} aria-hidden="true" />
                        <span>Списать</span>
                      </button>
                    </div>
                  </div>
                ))}
              </div>

              <div className="table-shell cash-bank-history-shell">
                <table className="cash-bank-history-table" aria-label="Последние операции с кассой и банковским счётом">
                  <thead>
                    <tr>
                      <th>Дата</th>
                      <th>Счёт</th>
                      <th>Операция</th>
                      <th>Сумма</th>
                      <th>Причина</th>
                    </tr>
                  </thead>
                  <tbody>
                    {cashBankSettings.recentOperations.map((operation) => (
                      <tr key={operation.id}>
                        <td><time dateTime={operation.createdAtUtc}>{formatDateOnly(operation.operationDate)}, {formatOperationTime(operation.createdAtUtc)}</time></td>
                        <td>{operation.account === 'cash' ? 'Касса' : 'Банковский счёт'}</td>
                        <td>{formatCashBankOperation(operation.operationKind, operation.direction)}</td>
                        <td className={operation.direction === 'increase' ? 'money-overpayment' : 'money-accrual'}>
                          {operation.direction === 'increase' ? '+' : '−'}{formatMoney(operation.amount)} ₽
                        </td>
                        <td>{operation.reason}</td>
                      </tr>
                    ))}
                    {cashBankSettings.recentOperations.length === 0 ? (
                      <tr><td colSpan={5}><p className="empty-state" role="status">Операций пока нет.</p></td></tr>
                    ) : null}
                  </tbody>
                </table>
              </div>
            </>
          ) : null}
          {cashBankError && !cashBankSettings ? (
            <AsyncErrorState message={cashBankError} onRetry={() => setSettingsReloadRevision((value) => value + 1)} retrying={cashBankLoading} />
          ) : cashBankError && !balanceAdjustmentDraft ? <FormError>{cashBankError}</FormError> : null}
        </div>
      </section>
      ) : null}
      {balanceAdjustmentDraft ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeBalanceAdjustment}>
          <section ref={balanceAdjustmentDialogRef} className="detail-dialog cash-bank-adjustment-dialog" role="dialog" aria-modal="true" aria-labelledby="cash-bank-adjustment-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">{balanceAdjustmentDraft.account === 'cash' ? 'Касса' : 'Банковский счёт'}</p>
                <h3 id="cash-bank-adjustment-title">{balanceAdjustmentDraft.direction === 'increase' ? 'Пополнение' : 'Списание'} {balanceAdjustmentDraft.account === 'cash' ? 'кассы' : 'банковского счёта'}</h3>
              </div>
              <button ref={balanceAdjustmentCloseRef} className="icon-button" type="button" aria-label="Закрыть окно операции" disabled={cashBankSaving} onClick={closeBalanceAdjustment}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <form className="cash-bank-adjustment-form" aria-label={balanceAdjustmentDraft.direction === 'increase' ? 'Пополнение остатка' : 'Списание остатка'} onSubmit={(event) => void saveBalanceAdjustment(event)}>
              <div className="dictionary-form-grid cash-bank-adjustment-grid">
                <FormField label="Дата операции">
                  <LocalizedDatePicker
                    ariaLabel="Дата операции"
                    mode="date"
                    value={balanceAdjustmentDraft.operationDate}
                    disabled={cashBankSaving}
                    onChange={(value) => setBalanceAdjustmentDraft({ ...balanceAdjustmentDraft, operationDate: value })}
                    required
                  />
                </FormField>
                <FormField label="Сумма, ₽">
                  <MoneyTextInput
                    aria-label="Сумма операции"
                    value={balanceAdjustmentDraft.amount}
                    disabled={cashBankSaving}
                    onValueChange={(amount) => setBalanceAdjustmentDraft({ ...balanceAdjustmentDraft, amount })}
                    required
                  />
                </FormField>
                <FormField label="Причина">
                  <textarea
                    aria-label="Причина операции"
                    value={balanceAdjustmentDraft.reason}
                    disabled={cashBankSaving}
                    maxLength={1000}
                    onChange={(event) => setBalanceAdjustmentDraft({ ...balanceAdjustmentDraft, reason: event.target.value })}
                    required={actionCommentsRequired}
                  />
                </FormField>
              </div>
              {cashBankError ? <FormError>{cashBankError}</FormError> : null}
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" disabled={cashBankSaving} onClick={closeBalanceAdjustment}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={cashBankSaving}>
                  {balanceAdjustmentDraft.direction === 'increase' ? <ArrowUpCircle size={17} aria-hidden="true" /> : <ArrowDownCircle size={17} aria-hidden="true" />}
                  <span>{cashBankSaving ? 'Проводим...' : 'Провести операцию'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
      {canManageApplicationSettings && activeSettingsTab === 'display' ? (
      <section className="password-panel settings-card settings-card--display" aria-label="Отображение таблиц">
        <div className="settings-card-intro">
          <h2>Интерфейс и действия</h2>
        </div>
        <div className="dictionary-form settings-card-form settings-display-form">
          <SettingsDisplaySwitch
            title="Требовать комментарий к действиям"
            label="Требовать комментарий к действиям"
            checked={actionCommentsRequired}
            disabled={actionCommentSettingsLoading}
            onChange={(checked) => {
              setPaymentDisplaySettingsError(null)
              void saveActionCommentsRequired(checked)
                .catch((caught: unknown) => setPaymentDisplaySettingsError(caught instanceof Error ? caught.message : 'Не удалось сохранить настройку.'))
            }}
          />
          <p className="form-hint">Причины необязательны, но история действий сохраняется.</p>
          {actionCommentSettingsError ? <FormError>{actionCommentSettingsError}</FormError> : null}
          <SettingsDisplaySwitch
            title="Показывать общую ведомость платежей"
            label="Показывать общую ведомость платежей при открытии"
            checked={showAllGarageOperationsByDefault}
            disabled={paymentDisplaySettingsLoading || paymentDisplaySettingsSaving}
            onChange={(checked) => { setShowAllGarageOperationsByDefault(checked); setPaymentDisplaySettingsMessage(null) }}
          />
          {tariffColumnSwitches.map((item) => (
            <SettingsDisplaySwitch
              key={item.key}
              title={item.title}
              label={item.label}
              checked={tariffTableColumns[item.key]}
              disabled={paymentDisplaySettingsLoading || paymentDisplaySettingsSaving}
              onChange={(checked) => { setTariffTableColumns({ ...tariffTableColumns, [item.key]: checked }); setPaymentDisplaySettingsMessage(null) }}
            />
          ))}
          {paymentDisplaySettingsLoading ? <LoadingSkeleton label="Загружаем настройки" /> : null}
          {paymentDisplaySettingsError && !paymentDisplaySettingsVersion ? (
            <AsyncErrorState message={paymentDisplaySettingsError} onRetry={() => setSettingsReloadRevision((value) => value + 1)} retrying={paymentDisplaySettingsLoading} />
          ) : paymentDisplaySettingsError ? <FormError>{paymentDisplaySettingsError}</FormError> : null}
          {paymentDisplaySettingsMessage ? <div className="form-success" role="status" aria-live="polite">{paymentDisplaySettingsMessage}</div> : null}
          <button className="secondary-button" type="button" disabled={paymentDisplaySettingsLoading || paymentDisplaySettingsSaving} onClick={() => void savePaymentDisplaySettings()}>
            {paymentDisplaySettingsSaving ? 'Сохраняем...' : 'Сохранить отображение'}
          </button>
        </div>
      </section>
      ) : null}
      {canManageApplicationSettings && activeSettingsTab === 'backups' ? (
      <section className="password-panel settings-card settings-card--backups" aria-label="Резервное копирование базы данных">
        <div className="settings-card-intro">
          <p className="eyebrow">Резервные копии</p>
          <h2>Защита данных PostgreSQL</h2>
          <p>Резервные копии работают как при обычном запуске, так и в Docker. Файлы сохраняются в постоянной папке компьютера и не зависят от обновления приложения.</p>
        </div>
        <div className="settings-card-body">
        {backupLoading && !backupStatus ? <LoadingSkeleton className="loading-skeleton--compact" label="Загружаем состояние резервного копирования" rows={3} columns={4} /> : null}
        {backupLoading && backupStatus ? <BackgroundRefreshStatus label="Обновляем состояние резервного копирования" /> : null}
        {backupError ? (
          <div className="settings-backup-error">
            <FormError>{backupError}</FormError>
            <button className="ghost-button" type="button" disabled={backupLoading} onClick={() => setBackupReloadToken((value) => value + 1)}>
              <RefreshCw size={16} aria-hidden="true" />
              <span>Повторить загрузку</span>
            </button>
          </div>
        ) : null}
        {backupMessage ? <div className="form-success" role="status" aria-live="polite">{backupMessage}</div> : null}
        {backupStatus ? (
          <>
            <div className="summary-strip" aria-label="Состояние резервного копирования">
              <div>
                <span>Резервное копирование</span>
                <strong className={backupStatus.enabled ? 'status-active' : 'status-disabled'}>{backupStatus.enabled ? 'Включено' : 'Отключено'}</strong>
              </div>
              <div>
                <span>Автоматически</span>
                <strong>{backupStatus.automaticEnabled ? `каждые ${backupStatus.intervalHours} ч.` : 'отключено'}</strong>
              </div>
              <div>
                <span>Хранится копий</span>
                <strong>до {backupStatus.retentionCount}</strong>
              </div>
              <div>
                <span>Последняя успешная</span>
                <strong>{backupStatus.lastSuccessfulBackupAtUtc ? formatDateTime(backupStatus.lastSuccessfulBackupAtUtc) : 'еще не создавалась'}</strong>
              </div>
            </div>
            <p className="form-hint">Папка хранения: {backupStatus.directory}. При обычном запуске система выбирает постоянный локальный каталог автоматически; путь можно переопределить параметром DatabaseBackup__Directory. В Docker используется BACKUP_HOST_PATH.</p>
            {backupStatus.lastError ? <FormError>{backupStatus.lastError}</FormError> : null}
            <button
              className="secondary-button create-action-button"
              type="button"
              disabled={!backupStatus.enabled || backupStatus.isRunning || backupCreating}
              onClick={() => {
                setBackupMessage(null)
                setBackupConfirmation({ reason: '', error: null })
              }}
            >
              <DatabaseBackup size={17} aria-hidden="true" />
              <span>{backupStatus.isRunning ? 'Копия создается...' : 'Создать резервную копию'}</span>
            </button>
            <div className="dictionary-table-scroll settings-backup-table-shell" aria-busy={backupDeleting || backupDownloadingFileName !== null}>
              <table className="dictionary-data-table settings-backup-table" aria-label="Резервные копии базы данных">
                <thead>
                  <tr>
                    <th>Дата</th>
                    <th>Тип</th>
                    <th>Файл</th>
                    <th>Размер</th>
                    <th className="table-actions-column">Действия</th>
                  </tr>
                </thead>
                <tbody>
                  {backupStatus.backups.map((backup) => (
                    <tr key={backup.fileName}>
                      <td className="settings-backup-date">{formatDateTime(backup.createdAtUtc)}</td>
                      <td className="settings-backup-kind"><span className="dictionary-status-pill dictionary-status-pill-archived">{formatBackupKind(backup.kind)}</span></td>
                      <td className="settings-backup-file" title={backup.fileName}>{backup.fileName}</td>
                      <td className="settings-backup-size">{formatFileSize(backup.sizeBytes)}</td>
                      <td className="table-actions-column">
                        <div className="dictionary-row-actions">
                          <button
                            className="icon-button dictionary-row-action"
                            type="button"
                            aria-label={`Скачать резервную копию ${backup.fileName}`}
                            title="Скачать резервную копию"
                            disabled={backupDeleting || backupDownloadingFileName !== null}
                            onClick={() => void downloadDatabaseBackup(backup)}
                          >
                            <ArrowDownCircle size={16} aria-hidden="true" />
                          </button>
                          <button
                            className="icon-button dictionary-row-action danger-icon-button"
                            type="button"
                            aria-label={`Удалить резервную копию ${backup.fileName}`}
                            title="Удалить резервную копию"
                            disabled={backupDeleting || backupDownloadingFileName !== null}
                            onClick={() => {
                              setBackupMessage(null)
                              setBackupDeleteConfirmation({ backup, reason: '', error: null })
                            }}
                          >
                            <X size={16} aria-hidden="true" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                  {backupStatus.backups.length === 0 ? (
                    <tr>
                      <td colSpan={5}><EmptyState>Резервные копии еще не создавались.</EmptyState></td>
                    </tr>
                  ) : null}
                </tbody>
              </table>
            </div>
          </>
        ) : null}
        </div>
      </section>
      ) : null}
      {canManageApplicationSettings && activeSettingsTab === 'diagnostics' ? (
      <section className="password-panel settings-card settings-card--diagnostics" aria-label="Диагностика ошибок приложения">
        <div className="settings-card-intro">
          <p className="eyebrow">Диагностика</p>
          <h2>Журнал ошибок</h2>
          <p>Система сохраняет технические ошибки сервера и браузера с кодом для поиска. Пароли, токены, телефоны и адреса электронной почты маскируются; база данных и резервные копии в пакет не входят.</p>
        </div>
        <div className="settings-card-body">
        {diagnosticLoading && !diagnosticStatus ? <LoadingSkeleton className="loading-skeleton--compact" label="Загружаем состояние журнала ошибок" rows={3} columns={4} /> : null}
        {diagnosticLoading && diagnosticStatus ? <BackgroundRefreshStatus label="Обновляем состояние журнала ошибок" /> : null}
        {diagnosticError ? (
          <div className="settings-backup-error">
            <FormError>{diagnosticError}</FormError>
            <button className="ghost-button" type="button" disabled={diagnosticLoading} onClick={() => setDiagnosticReloadToken((value) => value + 1)}>
              <RefreshCw size={16} aria-hidden="true" />
              <span>Повторить загрузку</span>
            </button>
          </div>
        ) : null}
        {diagnosticMessage ? <div className="form-success" role="status" aria-live="polite">{diagnosticMessage}</div> : null}
        {diagnosticStatus ? (
          <>
            <div className="summary-strip" aria-label="Состояние журнала ошибок">
              <div>
                <span>Журнал ошибок</span>
                <strong className={diagnosticStatus.enabled ? 'status-active' : 'status-disabled'}>{diagnosticStatus.enabled ? 'Включен' : 'Отключен'}</strong>
              </div>
              <div>
                <span>Срок хранения</span>
                <strong>{diagnosticStatus.retentionDays} дн.</strong>
              </div>
              <div>
                <span>Файлы</span>
                <strong>{diagnosticStatus.fileCount} / {formatFileSize(diagnosticStatus.totalSizeBytes)}</strong>
              </div>
              <div>
                <span>Последняя ошибка</span>
                <strong>{diagnosticStatus.lastEntryAtUtc ? formatDateTime(diagnosticStatus.lastEntryAtUtc) : 'не зарегистрирована'}</strong>
              </div>
            </div>
            <p className="form-hint">В пакет попадут журналы максимум за {diagnosticStatus.packageDays} дн. общим объемом до {diagnosticStatus.packageMaxSizeMb} МБ. Выгрузка доступна только администратору и фиксируется в истории изменений.</p>
            {diagnosticStatus.lastWriteError ? <FormError>{diagnosticStatus.lastWriteError}</FormError> : null}
            <button className="secondary-button" type="button" disabled={!diagnosticStatus.enabled || diagnosticExporting} onClick={() => void exportDiagnosticPackage()}>
              <ArrowDownCircle size={17} aria-hidden="true" />
              <span>{diagnosticExporting ? 'Формируем пакет...' : 'Скачать диагностический пакет'}</span>
            </button>
          </>
        ) : null}
        </div>
      </section>
      ) : null}
      {integrationTabVisible && activeSettingsTab === 'integrations' ? (
      <>
      {canViewIntegrationStatus ? (
        <section className="password-panel" aria-label="Интеграция 1C Fresh">
          <div>
            <p className="eyebrow">Интеграции</p>
            <h2>1C Fresh</h2>
            <p>Статус рабочего подключения показывается без раскрытия токенов и других защищенных настроек.</p>
          </div>
          {integrationError ? <AsyncErrorState message={integrationError} onRetry={() => setSettingsReloadRevision((value) => value + 1)} retrying={integrationLoading} /> : null}
          {integrationLoading ? <LoadingSkeleton className="loading-skeleton--compact" label="Загружаем статус 1C Fresh" rows={3} columns={4} /> : null}
          {oneCFreshStatus ? (
            <div className="summary-strip" aria-label="Статус интеграции 1C Fresh">
              <div>
                <span>Состояние</span>
                <strong className={oneCFreshStatus.isConfigured ? 'status-active' : 'status-disabled'}>{oneCFreshStatus.isConfigured ? 'Подготовлено' : 'Не настроено'}</strong>
              </div>
              <div>
                <span>Синхронизация</span>
                <strong className={oneCFreshStatus.canSynchronize ? 'status-active' : 'warning-text'}>{oneCFreshStatus.canSynchronize ? 'Доступна' : 'Ожидает адаптер'}</strong>
              </div>
              <div>
                <span>Защищенные настройки</span>
                <strong>{oneCFreshStatus.configuredSettings.length} / {oneCFreshStatus.requiredSettings.length}</strong>
              </div>
          <div>
            <span>Обновлено</span>
            <strong>{oneCFreshStatus.lastProtectedSettingUpdatedAtUtc ? formatDateTime(oneCFreshStatus.lastProtectedSettingUpdatedAtUtc) : 'нет данных'}</strong>
          </div>
        </div>
          ) : null}
          {oneCFreshStatus ? (
            <EmptyState>{oneCFreshStatus.statusMessage}</EmptyState>
          ) : null}
          {canManageIntegrationSettings ? (
            <form className="dictionary-form" aria-label="Защищенная настройка 1C Fresh" onSubmit={(event) => {
              event.preventDefault()
              void saveProtectedSetting('OneCFresh', 'RefreshToken', oneCFreshToken, () => setOneCFreshToken(''))
            }}>
              <FormField label="Refresh token 1C Fresh">
                <input aria-label="Новый refresh token 1C Fresh" type="password" autoComplete="new-password" value={oneCFreshToken} onChange={(event) => setOneCFreshToken(event.target.value)} />
              </FormField>
              <p className="form-hint">Сохраненное значение нельзя просмотреть: можно только заменить новым.</p>
              <button className="secondary-button" type="submit" disabled={protectedSettingSaving !== null}>
                <ShieldCheck size={16} />
                <span>{protectedSettingSaving === 'OneCFresh:RefreshToken' ? 'Сохраняем...' : 'Сохранить токен'}</span>
              </button>
            </form>
          ) : null}
          {oneCFreshSyncMessage ? <div className="form-success" role="status" aria-live="polite">{oneCFreshSyncMessage}</div> : null}
          {oneCFreshPreview ? (
            <dl className="fund-operation-preview" aria-label="Предпросмотр синхронизации 1C Fresh">
              <div>
                <dt>Режим</dt>
                <dd>{formatOneCFreshPreviewMode(oneCFreshPreview.mode)}</dd>
              </div>
              <div>
                <dt>Направление</dt>
                <dd>{formatOneCFreshPreviewDirection(oneCFreshPreview.direction)}</dd>
              </div>
              <div>
                <dt>Период и фильтры</dt>
                <dd>{oneCFreshPreview.periodSummary}</dd>
              </div>
              <div>
                <dt>Снимок</dt>
                <dd>{oneCFreshPreview.snapshotHash.slice(0, 12)}</dd>
              </div>
              <div>
                <dt>Можно отправлять</dt>
                <dd>{oneCFreshPreview.canApply ? 'Да' : 'Нет, нужен реальный контур и подтверждение состава обмена'}</dd>
              </div>
              {oneCFreshPreview.counts.map((count) => (
                <div key={`${count.objectType}-${count.operation}`}>
                  <dt>{formatOneCFreshObjectType(count.objectType)}</dt>
                  <dd>{formatOneCFreshOperation(count.operation)}: {count.count}</dd>
                </div>
              ))}
              {oneCFreshPreview.warnings.map((warning) => (
                <div key={warning.code}>
                  <dt>Предупреждение</dt>
                  <dd>{warning.message}</dd>
                </div>
              ))}
            </dl>
          ) : null}
          {oneCFreshSyncResult ? <p className={oneCFreshSyncResult.hasConflict ? 'form-note warning-text' : 'form-note'} role="status" aria-live="polite">{getOneCFreshSyncRecoveryMessage(oneCFreshSyncResult)}</p> : null}
          {oneCFreshStatus ? (
            <button className="secondary-button" type="button" onClick={(event) => openOneCFreshSyncConfirmation(event.currentTarget, 'preview')} disabled={integrationLoading || oneCFreshSyncSaving || !oneCFreshStatus.isConfigured}>
              <Eye size={16} aria-hidden="true" />
              <span>{oneCFreshSyncSaving ? 'Готовим...' : 'Подготовить предпросмотр'}</span>
            </button>
          ) : null}
          {oneCFreshStatus ? (
            <button className="secondary-button" type="button" onClick={(event) => openOneCFreshSyncConfirmation(event.currentTarget, 'start')} disabled={integrationLoading || oneCFreshSyncSaving || !oneCFreshStatus.canSynchronize || Boolean(oneCFreshPreview && !oneCFreshPreview.canApply)}>
              <RefreshCw size={16} aria-hidden="true" />
              <span>{oneCFreshSyncSaving ? 'Запускаем...' : 'Запустить синхронизацию'}</span>
            </button>
          ) : null}
          {oneCFreshStatus && oneCFreshSyncResult?.canRetry ? (
            <button className="ghost-button" type="button" onClick={(event) => openOneCFreshSyncConfirmation(event.currentTarget, 'retry')} disabled={integrationLoading || oneCFreshSyncSaving || !oneCFreshStatus.canSynchronize}>
              <RefreshCw size={16} aria-hidden="true" />
              <span>Повторить запрос</span>
            </button>
          ) : null}
        </section>
      ) : null}
      {canViewReceiptPrintingStatus ? (
        <section className="password-panel" aria-label="Печать чеков и квитанций">
          <div>
            <p className="eyebrow">Интеграции</p>
            <h2>Чеки и квитанции</h2>
            <p>Статус рабочего подключения печати показывается без раскрытия параметров фискального оборудования и шаблонов.</p>
          </div>
          {receiptPrintingError ? <AsyncErrorState message={receiptPrintingError} onRetry={() => setSettingsReloadRevision((value) => value + 1)} retrying={receiptPrintingLoading} /> : null}
          {receiptPrintingLoading ? <LoadingSkeleton className="loading-skeleton--compact" label="Загружаем статус печати" rows={3} columns={4} /> : null}
          {receiptPrintingStatus ? (
            <div className="summary-strip" aria-label="Статус печати чеков и квитанций">
              <div>
                <span>Состояние</span>
                <strong className={receiptPrintingStatus.isConfigured ? 'status-active' : 'status-disabled'}>{receiptPrintingStatus.isConfigured ? 'Подготовлено' : 'Не настроено'}</strong>
              </div>
              <div>
                <span>Печать</span>
                <strong className={receiptPrintingStatus.canPrint ? 'status-active' : 'warning-text'}>{receiptPrintingStatus.canPrint ? 'Доступна' : 'Ожидает адаптер'}</strong>
              </div>
              <div>
                <span>Защищенные настройки</span>
                <strong>{receiptPrintingStatus.configuredSettings.length} / {receiptPrintingStatus.requiredSettings.length}</strong>
              </div>
              <div>
                <span>Обновлено</span>
                <strong>{receiptPrintingStatus.lastProtectedSettingUpdatedAtUtc ? formatDateTime(receiptPrintingStatus.lastProtectedSettingUpdatedAtUtc) : 'нет данных'}</strong>
              </div>
            </div>
          ) : null}
          {receiptPrintingStatus ? (
            <>
              <StatusMessage>{receiptPrintingStatus.statusMessage}</StatusMessage>
              <p className="form-hint">Будущие действия: {receiptPrintingStatus.plannedActions.join(', ')}.</p>
            </>
          ) : null}
          {canManageIntegrationSettings ? (
            <form className="dictionary-form" aria-label="Защищенные настройки печати" onSubmit={(event) => event.preventDefault()}>
              <FormField label="Подключение к устройству">
                <input aria-label="Новое подключение к устройству печати" type="password" autoComplete="new-password" value={receiptDeviceConnection} onChange={(event) => setReceiptDeviceConnection(event.target.value)} />
              </FormField>
              <button className="secondary-button" type="button" disabled={protectedSettingSaving !== null} onClick={() => void saveProtectedSetting('ReceiptPrinting', 'DeviceConnection', receiptDeviceConnection, () => setReceiptDeviceConnection(''))}>
                <ShieldCheck size={16} />
                <span>{protectedSettingSaving === 'ReceiptPrinting:DeviceConnection' ? 'Сохраняем...' : 'Сохранить подключение'}</span>
              </button>
              <FormField label="Шаблон квитанции">
                <textarea aria-label="Новый защищенный шаблон квитанции" rows={3} value={receiptTemplate} onChange={(event) => setReceiptTemplate(event.target.value)} />
              </FormField>
              <button className="secondary-button" type="button" disabled={protectedSettingSaving !== null} onClick={() => void saveProtectedSetting('ReceiptPrinting', 'ReceiptTemplate', receiptTemplate, () => setReceiptTemplate(''))}>
                <ShieldCheck size={16} />
                <span>{protectedSettingSaving === 'ReceiptPrinting:ReceiptTemplate' ? 'Сохраняем...' : 'Сохранить шаблон'}</span>
              </button>
              <p className="form-hint">Сохраненные значения не возвращаются из API и после записи очищаются из формы.</p>
            </form>
          ) : null}
        </section>
      ) : null}
      {canManageDadataSettings ? (
        <section className="password-panel" aria-label="Подсказки DaData">
          <div>
            <p className="eyebrow">Интеграции</p>
            <h2>DaData</h2>
            <p>Ключ используется для подсказок организаций по ИНН и адресов в карточках гаражей и поставщиков.</p>
          </div>
          <form className="dictionary-form" aria-label="Защищенная настройка DaData" onSubmit={(event) => {
            event.preventDefault()
            void saveProtectedSetting('DaData', 'ApiKey', dadataApiKey, () => setDadataApiKey(''))
          }}>
            <FormField label="API-ключ DaData">
              <input aria-label="Новый API-ключ DaData" type="password" autoComplete="new-password" value={dadataApiKey} onChange={(event) => setDadataApiKey(event.target.value)} />
            </FormField>
            <p className="form-hint">Сохраненный ключ нельзя просмотреть: администратор может только заменить его новым.</p>
            <button className="secondary-button" type="submit" disabled={protectedSettingSaving !== null}>
              <ShieldCheck size={16} />
              <span>{protectedSettingSaving === 'DaData:ApiKey' ? 'Сохраняем...' : 'Сохранить API-ключ'}</span>
            </button>
          </form>
        </section>
      ) : null}
      {protectedSettingError ? <FormError>{protectedSettingError}</FormError> : null}
      {protectedSettingMessage ? <div className="form-success" role="status" aria-live="polite">{protectedSettingMessage}</div> : null}
      </>
      ) : null}
        </div>
      </section>
      {businessDateConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => !businessDateSaving && setBusinessDateConfirmation(null)}>
          <section className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="business-date-confirmation-title" aria-describedby="business-date-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="dialog-heading">
              <div>
                <p className="eyebrow">Рабочая дата</p>
                <h3 id="business-date-confirmation-title">{businessDateConfirmation.overrideDate ? 'Включить тестовую дату?' : 'Вернуть системную дату?'}</h3>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение рабочей даты" onClick={() => setBusinessDateConfirmation(null)} disabled={businessDateSaving}><X size={18} aria-hidden="true" /></button>
            </div>
            <p className="confirmation-text" id="business-date-confirmation-description">
              Рабочая дата: {formatBusinessDate(businessDateConfirmation.currentEffectiveDate)} → {formatBusinessDate(businessDateConfirmation.proposedEffectiveDate)}.
            </p>
            <p className="form-hint">
              Месяц {formatBusinessDate(businessDateConfirmation.automation.accountingMonth)}: гаражей — {businessDateConfirmation.automation.activeGarageCount}, услуг — {businessDateConfirmation.automation.dueRegularServiceCount}, сборов — {businessDateConfirmation.automation.activeFeeCampaignCount}.
            </p>
            <p className="form-hint">До {businessDateConfirmation.automation.maximumGarageChecks} проверок без дублирования начислений.</p>
            {businessDateConfirmation.automation.warnings.length > 0 ? (
              <div className="form-warning" role="alert">
                {businessDateConfirmation.automation.warnings.map((warning) => <p key={warning}>{warning}</p>)}
              </div>
            ) : null}
            {businessDateError ? <FormError>{businessDateError}</FormError> : null}
            <div className="dialog-actions">
              <button className="ghost-button" type="button" onClick={() => setBusinessDateConfirmation(null)} disabled={businessDateSaving}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmBusinessDateChange()} disabled={businessDateSaving}>
                <CalendarClock size={16} aria-hidden="true" />
                <span>{businessDateSaving ? 'Применяем и рассчитываем...' : 'Подтвердить'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
      {backupConfirmation ? (
        <BackupReasonDialog
          reason={backupConfirmation.reason}
          error={backupConfirmation.error}
          busy={backupCreating}
          onReasonChange={(reason) => setBackupConfirmation({ reason, error: null })}
          onCancel={() => setBackupConfirmation(null)}
          onSubmit={() => void createDatabaseBackup()}
        />
      ) : null}
      {backupDeleteConfirmation ? (
        <BackupReasonDialog
          fileName={backupDeleteConfirmation.backup.fileName}
          reason={backupDeleteConfirmation.reason}
          error={backupDeleteConfirmation.error}
          busy={backupDeleting}
          onReasonChange={(reason) => setBackupDeleteConfirmation({ ...backupDeleteConfirmation, reason, error: null })}
          onCancel={() => setBackupDeleteConfirmation(null)}
          onSubmit={() => void deleteDatabaseBackup()}
        />
      ) : null}
      <ToastViewport toast={toast} onDismiss={dismissToast} />
      {pendingPasswordChange ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => !saving && setPendingPasswordChange(null)}>
          <section ref={confirmationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="password-change-confirmation-title" aria-describedby="password-change-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="dialog-heading">
              <div>
                <p className="eyebrow">Настройки</p>
                <h3 id="password-change-confirmation-title">Подтвердить смену пароля?</h3>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить подтверждение смены пароля" onClick={() => setPendingPasswordChange(null)} disabled={saving}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="password-change-confirmation-description">После подтверждения пароль будет изменен, текущая сессия завершится, а действие появится в истории изменений без раскрытия самого пароля.</p>
            <ChangePreviewList ariaLabel="Изменяемые поля настройки" changes={[{
              field: 'Пароль',
              before: 'Без изменения',
              after: formatSensitiveChange(pendingPasswordChange.newPassword),
            }]} />
            {error ? <FormError>{error}</FormError> : null}
            <div className="dialog-actions">
              <button ref={confirmationCancelRef} className="ghost-button" type="button" onClick={() => setPendingPasswordChange(null)} disabled={saving}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmPasswordChange()} disabled={saving}>
                <ShieldCheck size={16} />
                <span>{saving ? 'Сохраняем...' : 'Подтвердить смену пароля'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
      {integrationSettingsVisible && oneCFreshSyncConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!oneCFreshSyncSaving) {
            closeOneCFreshSyncConfirmation()
          }
        }}>
          <section ref={oneCFreshSyncDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="one-c-fresh-sync-confirmation-title" aria-describedby="one-c-fresh-sync-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="dialog-heading">
              <div>
                <p className="eyebrow">Интеграции</p>
                <h3 id="one-c-fresh-sync-confirmation-title">{getOneCFreshSyncConfirmationTitle(oneCFreshSyncConfirmation.mode)}</h3>
              </div>
              <button className="icon-button" type="button" aria-label={getOneCFreshSyncCancelLabel(oneCFreshSyncConfirmation.mode)} onClick={closeOneCFreshSyncConfirmation} disabled={oneCFreshSyncSaving}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="one-c-fresh-sync-confirmation-description">{getOneCFreshSyncConfirmationDescription(oneCFreshSyncConfirmation.mode)}</p>
            <FormField label="Комментарий">
              <textarea aria-label={getOneCFreshSyncCommentLabel(oneCFreshSyncConfirmation.mode)} rows={4} value={oneCFreshSyncConfirmation.comment} onChange={(event) => setOneCFreshSyncConfirmation((state) => state ? { ...state, comment: event.target.value, error: null } : state)} disabled={oneCFreshSyncSaving} />
            </FormField>
            {oneCFreshSyncConfirmation.error ? <FormError>{oneCFreshSyncConfirmation.error}</FormError> : null}
            <div className="dialog-actions">
              <button ref={oneCFreshSyncCancelRef} className="ghost-button" type="button" onClick={closeOneCFreshSyncConfirmation} disabled={oneCFreshSyncSaving}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmOneCFreshSync()} disabled={oneCFreshSyncSaving}>
                <RefreshCw size={16} />
                  <span>{oneCFreshSyncSaving ? 'Отправляем...' : getOneCFreshSyncConfirmLabel(oneCFreshSyncConfirmation.mode)}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </>
  )
}

function getOneCFreshSyncConfirmationTitle(mode: 'preview' | 'start' | 'retry') {
  if (mode === 'preview') {
    return 'Подготовить предпросмотр синхронизации 1C Fresh?'
  }

  return mode === 'retry'
    ? 'Повторить запрос синхронизации 1C Fresh?'
    : 'Запустить синхронизацию 1C Fresh?'
}

function BackupReasonDialog({ fileName, reason, error, busy, onReasonChange, onCancel, onSubmit }: {
  fileName?: string
  reason: string
  error: string | null
  busy: boolean
  onReasonChange: (reason: string) => void
  onCancel: () => void
  onSubmit: () => void
}) {
  const deleting = Boolean(fileName)
  useRestoreFocusOnClose(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useEscapeKey(!busy, onCancel)

  const action = deleting ? 'удаления' : 'создания'
  const titleId = `database-backup-${deleting ? 'delete-' : ''}title`
  const descriptionId = `database-backup-${deleting ? 'delete-' : ''}description`

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={() => !busy && onCancel()}>
      <section ref={dialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} aria-describedby={descriptionId} onMouseDown={(event) => event.stopPropagation()}>
        <div className="dialog-heading">
          <div>
            <p className="eyebrow">{deleting ? 'Удаление резервной копии' : 'Резервные копии'}</p>
            <h3 id={titleId}>{deleting ? 'Удалить выбранную копию?' : 'Создать резервную копию базы?'}</h3>
          </div>
          <button className="icon-button" type="button" aria-label={`Закрыть ${action} резервной копии`} onClick={onCancel} disabled={busy}>
            <X size={18} aria-hidden="true" />
          </button>
        </div>
        <p className="confirmation-text" id={descriptionId}>
          {deleting
            ? <>Файл <strong>{fileName}</strong> будет удален без возможности восстановления. Остальные копии не изменятся.</>
            : 'Система создаст PostgreSQL backup в отдельной папке, проверит его через pg_restore и запишет действие в историю изменений.'}
        </p>
        <FormField label={`Причина ${action}`}>
          <textarea
            aria-label={`Причина ${action} резервной копии`}
            rows={3}
            value={reason}
            onChange={(event) => onReasonChange(event.target.value)}
            placeholder={deleting ? 'Например: копия больше не нужна после успешного обновления' : 'Например: перед обновлением программы'}
            disabled={busy}
          />
        </FormField>
        {error ? <FormError>{error}</FormError> : null}
        <div className="dialog-actions">
          <button ref={cancelRef} className="ghost-button" type="button" onClick={onCancel} disabled={busy}>Отмена</button>
          <button className={deleting ? 'danger-button' : 'secondary-button'} type="button" onClick={onSubmit} disabled={busy}>
            {deleting ? <X size={16} aria-hidden="true" /> : <DatabaseBackup size={16} aria-hidden="true" />}
            <span>{busy ? (deleting ? 'Удаляем...' : 'Создаем и проверяем...') : (deleting ? 'Удалить копию' : 'Создать копию')}</span>
          </button>
        </div>
      </section>
    </div>
  )
}

function formatBackupKind(kind: string) {
  if (kind === 'manual') return 'Ручная'
  if (kind === 'automatic') return 'Автоматическая'
  if (kind === 'pre_update') return 'Перед обновлением'
  return kind
}

function formatBusinessDate(value: string) {
  const [year, month, day] = value.split('-')
  return year && month && day ? `${day}.${month}.${year}` : value
}

function parseMoneyDraft(value: string): number | null {
  const parsed = parseMoneyInput(value)
  return Number.isFinite(parsed) ? parsed : null
}

function formatCashBankOperation(
  operationKind: 'opening_balance' | 'adjustment',
  direction: 'increase' | 'decrease',
) {
  if (operationKind === 'opening_balance') {
    return direction === 'increase' ? 'Стартовый остаток' : 'Исправление старта'
  }
  return direction === 'increase' ? 'Пополнение' : 'Списание'
}

function formatFileSize(sizeBytes: number) {
  if (sizeBytes < 1024) return `${sizeBytes} Б`
  if (sizeBytes < 1024 * 1024) return `${(sizeBytes / 1024).toFixed(1)} КБ`
  return `${(sizeBytes / (1024 * 1024)).toFixed(1)} МБ`
}

function buildDiagnosticPackageFileName(now = new Date()) {
  const stamp = now.toISOString().replace(/[-:]/g, '').replace(/\.\d{3}Z$/, 'Z')
  return `garagebalance-diagnostics-${stamp}.zip`
}

function getOneCFreshSyncCancelLabel(mode: 'preview' | 'start' | 'retry') {
  if (mode === 'preview') {
    return 'Отменить предпросмотр синхронизации 1C Fresh'
  }

  return mode === 'retry'
    ? 'Отменить повтор синхронизации 1C Fresh'
    : 'Отменить запуск синхронизации 1C Fresh'
}

function getOneCFreshSyncConfirmationDescription(mode: 'preview' | 'start' | 'retry') {
  if (mode === 'preview') {
    return 'Предпросмотр будет записан в историю изменений и не отправит данные во внешнюю 1C Fresh. Он нужен, чтобы увидеть безопасный снимок будущего обмена перед запуском.'
  }

  return mode === 'retry'
    ? 'Повтор будет записан в историю изменений отдельным событием. Если HTTP-адаптер готов, задание будет передано в настроенный шлюз 1C Fresh.'
    : 'Запуск будет записан в историю изменений. Если HTTP-адаптер готов, задание будет передано в настроенный шлюз 1C Fresh.'
}

function getOneCFreshSyncCommentLabel(mode: 'preview' | 'start' | 'retry') {
  if (mode === 'preview') {
    return 'Комментарий к предпросмотру синхронизации 1C Fresh'
  }

  return mode === 'retry'
    ? 'Комментарий к повтору синхронизации 1C Fresh'
    : 'Комментарий к запуску синхронизации 1C Fresh'
}

function getOneCFreshSyncConfirmLabel(mode: 'preview' | 'start' | 'retry') {
  if (mode === 'preview') {
    return 'Подготовить'
  }

  return mode === 'retry' ? 'Повторить' : 'Запустить'
}

function formatOneCFreshPreviewMode(mode: string) {
  return mode === 'preview' ? 'Предпросмотр' : mode
}

function formatOneCFreshPreviewDirection(direction: string) {
  if (direction === 'pending_decision') return 'Ожидает решения по направлению обмена'
  if (direction === 'configured_bridge') return 'Настроенный шлюз 1C Fresh'
  return direction
}

function formatOneCFreshObjectType(objectType: string) {
  const labels: Record<string, string> = {
    accrual: 'Начисления',
    counterparty: 'Контрагенты',
    payment: 'Платежи',
  }

  return labels[objectType] ?? objectType
}

function formatOneCFreshOperation(operation: string) {
  const labels: Record<string, string> = {
    export: 'к выгрузке',
    match: 'к сопоставлению',
  }

  return labels[operation] ?? operation
}

function getOneCFreshSyncRecoveryMessage(result: OneCFreshSyncDto) {
  if (result.hasConflict) {
    return 'Обнаружен конфликт синхронизации. Перед повтором проверьте журнал обмена и выберите решение по конфликтным строкам.'
  }

  if (result.canRetry) {
    return result.isRetry
      ? 'Повтор записан отдельным событием истории. Если адаптер снова вернет ошибку, можно создать новый повтор с комментарием.'
      : 'Повтор доступен: новый запрос будет записан отдельным событием истории без раскрытия токена 1C Fresh.'
  }

  if (result.recoveryAction === 'watch_status') {
    return 'Запуск передан адаптеру. Следите за статусом обмена и журналом интеграции.'
  }

  return 'Запрос синхронизации обработан. Дополнительные действия сейчас не требуются.'
}
