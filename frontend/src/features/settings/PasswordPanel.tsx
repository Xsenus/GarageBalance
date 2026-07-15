import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { DatabaseBackup, Eye, KeyRound, PlugZap, RefreshCw, ShieldCheck, SlidersHorizontal, X } from 'lucide-react'
import type { AuthClient, AuthResponse, CurrentUserDto } from '../../services/authApi'
import type { IntegrationClient, OneCFreshIntegrationStatusDto, OneCFreshSyncDto, OneCFreshSyncPreviewDto, ReceiptPrintingIntegrationStatusDto } from '../../services/integrationsApi'
import type { ApplicationSettingsClient, DatabaseBackupStatusDto } from '../../services/settingsApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { LoadingSkeleton } from '../../shared/AsyncState'
import { formatSensitiveChange } from '../../shared/changePreview'
import { FormField } from '../../shared/FormField'
import { FormError, FormValidationSummary } from '../../shared/formFeedback'
import { formatDateTime } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { getPasswordChangeValidationErrors } from '../../shared/validation'

export function PasswordPanel({ auth, authClient, integrationClient, settingsClient, onUserChanged }: { auth: AuthResponse; authClient: AuthClient; integrationClient: IntegrationClient; settingsClient: ApplicationSettingsClient; onUserChanged: (user: CurrentUserDto) => void }) {
  const integrationSettingsVisible = import.meta.env.VITE_SHOW_INTEGRATION_SETTINGS === 'true'
  const dadataSettingsVisible = hasPermission(auth, permissions.usersManage)
  const integrationTabVisible = integrationSettingsVisible || dadataSettingsVisible
  const [activeSettingsTab, setActiveSettingsTab] = useState<'security' | 'display' | 'backups' | 'integrations'>(() => (
    integrationSettingsVisible && (hasPermission(auth, permissions.importRun) || hasPermission(auth, permissions.paymentsWrite))
      ? 'integrations'
      : 'security'
  ))
  const [form, setForm] = useState({ currentPassword: '', newPassword: '', repeatPassword: '' })
  const [message, setMessage] = useState<string | null>(null)
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
  const backupTriggerRef = useRef<HTMLButtonElement | null>(null)
  const canViewIntegrationStatus = integrationSettingsVisible && hasPermission(auth, permissions.importRun)
  const canViewReceiptPrintingStatus = integrationSettingsVisible && hasPermission(auth, permissions.paymentsWrite)
  const canManageIntegrationSettings = integrationSettingsVisible && hasPermission(auth, permissions.usersManage)
  const canManageDadataSettings = dadataSettingsVisible
  const canManageApplicationSettings = hasPermission(auth, permissions.usersManage)
  useRestoreFocusOnClose(Boolean(pendingPasswordChange))
  const confirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingPasswordChange))
  const confirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingPasswordChange))
  const oneCFreshSyncCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(oneCFreshSyncConfirmation))
  const oneCFreshSyncDialogRef = useFocusTrap<HTMLElement>(Boolean(oneCFreshSyncConfirmation))
  useRestoreFocusOnClose(Boolean(backupConfirmation))
  const backupConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(backupConfirmation))
  const backupConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(backupConfirmation))
  useEscapeKey(Boolean(pendingPasswordChange) && !saving, () => setPendingPasswordChange(null))
  useEscapeKey(Boolean(oneCFreshSyncConfirmation) && !oneCFreshSyncSaving, () => closeOneCFreshSyncConfirmation())
  useEscapeKey(Boolean(backupConfirmation) && !backupCreating, () => setBackupConfirmation(null))

  useEffect(() => {
    if (!canManageApplicationSettings || activeSettingsTab !== 'display') {
      return
    }

    let ignore = false
    setPaymentDisplaySettingsLoading(true)
    setPaymentDisplaySettingsError(null)
    settingsClient.getPaymentDisplaySettings(auth.accessToken)
      .then((settings) => {
        if (!ignore) {
          setShowAllGarageOperationsByDefault(settings.showAllGarageOperationsByDefault)
        }
      })
      .catch((caught: unknown) => {
        if (!ignore) {
          setPaymentDisplaySettingsError(caught instanceof Error ? caught.message : 'Не удалось загрузить настройки платежей.')
        }
      })
      .finally(() => {
        if (!ignore) {
          setPaymentDisplaySettingsLoading(false)
        }
      })

    return () => {
      ignore = true
    }
  }, [activeSettingsTab, auth.accessToken, canManageApplicationSettings, settingsClient])

  useEffect(() => {
    if (!canManageApplicationSettings || activeSettingsTab !== 'backups') {
      return
    }

    let ignore = false
    setBackupLoading(true)
    setBackupError(null)
    settingsClient.getDatabaseBackups(auth.accessToken)
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
    }
  }, [activeSettingsTab, auth.accessToken, backupReloadToken, canManageApplicationSettings, settingsClient])

  async function createDatabaseBackup() {
    if (!backupConfirmation) {
      return
    }

    const reason = backupConfirmation.reason.trim()
    if (reason.length < 3) {
      setBackupConfirmation({ ...backupConfirmation, error: 'Укажите причину длиной не менее 3 символов.' })
      return
    }

    setBackupCreating(true)
    setBackupError(null)
    setBackupMessage(null)
    try {
      const created = await settingsClient.createDatabaseBackup(auth.accessToken, { reason })
      setBackupMessage(`Резервная копия ${created.fileName} создана и проверена.`)
      setBackupConfirmation(null)
      try {
        const status = await settingsClient.getDatabaseBackups(auth.accessToken)
        setBackupStatus(status)
      } catch (caught) {
        setBackupError(caught instanceof Error
          ? `Копия создана, но список не обновился: ${caught.message}`
          : 'Копия создана, но список резервных копий не обновился.')
      }
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
      const settings = await settingsClient.updatePaymentDisplaySettings(auth.accessToken, { showAllGarageOperationsByDefault })
      setShowAllGarageOperationsByDefault(settings.showAllGarageOperationsByDefault)
      setPaymentDisplaySettingsMessage('Настройка отображения платежей сохранена.')
    } catch (caught) {
      setPaymentDisplaySettingsError(caught instanceof Error ? caught.message : 'Не удалось сохранить настройку отображения платежей.')
    } finally {
      setPaymentDisplaySettingsSaving(false)
    }
  }

  useEffect(() => {
    if (!canViewIntegrationStatus) {
      return
    }

    let ignore = false
    async function loadOneCFreshStatus() {
      await Promise.resolve()
      if (ignore) {
        return
      }

      setIntegrationLoading(true)
      setIntegrationError(null)
      try {
        const status = await integrationClient.getOneCFreshStatus(auth.accessToken)
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
    }
  }, [auth.accessToken, canViewIntegrationStatus, integrationClient])

  useEffect(() => {
    if (!canViewReceiptPrintingStatus) {
      return
    }

    let ignore = false
    async function loadReceiptPrintingStatus() {
      await Promise.resolve()
      if (ignore) {
        return
      }

      setReceiptPrintingLoading(true)
      setReceiptPrintingError(null)
      try {
        const status = await integrationClient.getReceiptPrintingStatus(auth.accessToken)
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
    }
  }, [auth.accessToken, canViewReceiptPrintingStatus, integrationClient])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setMessage(null)

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
    try {
      const user = await authClient.changeOwnPassword(auth.accessToken, {
        currentPassword: pendingPasswordChange.currentPassword,
        newPassword: pendingPasswordChange.newPassword,
      })
      onUserChanged(user)
      setPendingPasswordChange(null)
      setForm({ currentPassword: '', newPassword: '', repeatPassword: '' })
      setMessage('Пароль изменен. Используйте новый пароль при следующем входе.')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось изменить пароль.')
      setPendingPasswordChange(null)
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
    const trigger = oneCFreshSyncTriggerRef.current
    setOneCFreshSyncConfirmation(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      oneCFreshSyncTriggerRef.current = null
    }, 0)
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
          status: 'prepared',
          statusMessage: 'Токен 1C Fresh сохранен в защищенном хранилище. Запуск синхронизации будет доступен после подключения адаптера 1C Fresh.',
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
              ? 'Защищенные настройки печати сохранены. Печать, отмена и повторная печать станут доступны после подключения адаптера фискального оборудования.'
              : 'Для будущей печати нужно сохранить защищенные настройки ReceiptPrinting:DeviceConnection и ReceiptPrinting:ReceiptTemplate.',
            lastProtectedSettingUpdatedAtUtc: setting.updatedAtUtc,
          }
        })
      }
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
                <SlidersHorizontal size={17} aria-hidden="true" />
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
          <p className="form-hint" id="own-password-policy-hint">Минимум 8 символов: заглавная буква, строчная буква и цифра.</p>
          <FormValidationSummary title="Проверьте смену пароля" items={validationErrors} />
          {error ? <FormError>{error}</FormError> : null}
          {message ? <div className="form-success" role="status" aria-live="polite">{message}</div> : null}
          <button className="secondary-button" type="submit" disabled={saving || Boolean(pendingPasswordChange)}>
            <ShieldCheck size={16} />
            <span>{saving ? 'Сохраняем...' : 'Изменить пароль'}</span>
          </button>
        </form>
      </section>
      ) : null}
      {canManageApplicationSettings && activeSettingsTab === 'display' ? (
      <section className="password-panel settings-card settings-card--display" aria-label="Настройки отображения платежей">
        <div className="settings-card-intro">
          <p className="eyebrow">Отображение</p>
          <h2>Платежи при открытии раздела</h2>
          <p>По умолчанию раздел ожидает поиск гаража. При включении общей ведомости поступления и выплаты показываются сразу, но загружаются постранично.</p>
        </div>
        <div className="dictionary-form settings-card-form settings-display-form">
          <label className="contractors-switch-row settings-display-switch">
            <span>
              <strong>Показывать общую ведомость платежей</strong>
              <small>Поиск по гаражу продолжит работать в любом режиме.</small>
            </span>
            <span className="contractors-switch-control">
              <input
                type="checkbox"
                aria-label="Показывать общую ведомость платежей при открытии"
                checked={showAllGarageOperationsByDefault}
                disabled={paymentDisplaySettingsLoading || paymentDisplaySettingsSaving}
                onChange={(event) => {
                  setShowAllGarageOperationsByDefault(event.target.checked)
                  setPaymentDisplaySettingsMessage(null)
                }}
              />
            </span>
          </label>
          {paymentDisplaySettingsLoading ? <LoadingSkeleton className="loading-skeleton--compact" label="Загружаем настройку отображения платежей" rows={2} columns={2} /> : null}
          {paymentDisplaySettingsError ? <FormError>{paymentDisplaySettingsError}</FormError> : null}
          {paymentDisplaySettingsMessage ? <div className="form-success" role="status" aria-live="polite">{paymentDisplaySettingsMessage}</div> : null}
          <button className="secondary-button" type="button" disabled={paymentDisplaySettingsLoading || paymentDisplaySettingsSaving} onClick={() => void savePaymentDisplaySettings()}>
            <SlidersHorizontal size={16} aria-hidden="true" />
            <span>{paymentDisplaySettingsSaving ? 'Сохраняем...' : 'Сохранить отображение'}</span>
          </button>
        </div>
      </section>
      ) : null}
      {canManageApplicationSettings && activeSettingsTab === 'backups' ? (
      <section className="password-panel settings-card settings-card--backups" aria-label="Резервное копирование базы данных">
        <div className="settings-card-intro">
          <p className="eyebrow">Резервные копии</p>
          <h2>Защита данных PostgreSQL</h2>
          <p>Автоматические копии сохраняются вне контейнера. При обновлении контейнеры заменяются, но база, ключи шифрования и файлы backup остаются в постоянных хранилищах.</p>
        </div>
        {backupLoading ? <LoadingSkeleton className="loading-skeleton--compact" label="Загружаем состояние резервного копирования" rows={3} columns={4} /> : null}
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
        {backupStatus && !backupLoading ? (
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
            <p className="form-hint">Каталог внутри контейнера: {backupStatus.directory}. Фактическая папка компьютера задается параметром BACKUP_HOST_PATH в файле .env.</p>
            {backupStatus.lastError ? <FormError>{backupStatus.lastError}</FormError> : null}
            <button
              ref={backupTriggerRef}
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
            <div className="table-shell settings-backup-table-shell">
              <table aria-label="Последние резервные копии">
                <thead>
                  <tr>
                    <th>Дата</th>
                    <th>Тип</th>
                    <th>Файл</th>
                    <th>Размер</th>
                  </tr>
                </thead>
                <tbody>
                  {backupStatus.backups.map((backup) => (
                    <tr key={backup.fileName}>
                      <td>{formatDateTime(backup.createdAtUtc)}</td>
                      <td>{formatBackupKind(backup.kind)}</td>
                      <td>{backup.fileName}</td>
                      <td>{formatFileSize(backup.sizeBytes)}</td>
                    </tr>
                  ))}
                  {backupStatus.backups.length === 0 ? (
                    <tr>
                      <td colSpan={4}><p className="empty-state" role="status">Резервные копии еще не создавались.</p></td>
                    </tr>
                  ) : null}
                </tbody>
              </table>
            </div>
          </>
        ) : null}
      </section>
      ) : null}
      {integrationTabVisible && activeSettingsTab === 'integrations' ? (
      <>
      {canViewIntegrationStatus ? (
        <section className="password-panel" aria-label="Интеграция 1C Fresh">
          <div>
            <p className="eyebrow">Интеграции</p>
            <h2>1C Fresh</h2>
            <p>Статус подготовки будущей синхронизации показывается без раскрытия токенов и других защищенных настроек.</p>
          </div>
          {integrationError ? <FormError>{integrationError}</FormError> : null}
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
            <p className="empty-state" role="status" aria-live="polite">{oneCFreshStatus.statusMessage}</p>
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
            <button className="secondary-button" type="button" onClick={(event) => openOneCFreshSyncConfirmation(event.currentTarget, 'start')} disabled={integrationLoading || oneCFreshSyncSaving || !oneCFreshStatus.isConfigured || Boolean(oneCFreshPreview && !oneCFreshPreview.canApply)}>
              <RefreshCw size={16} aria-hidden="true" />
              <span>{oneCFreshSyncSaving ? 'Запускаем...' : 'Запустить синхронизацию'}</span>
            </button>
          ) : null}
          {oneCFreshStatus && oneCFreshSyncResult?.canRetry ? (
            <button className="ghost-button" type="button" onClick={(event) => openOneCFreshSyncConfirmation(event.currentTarget, 'retry')} disabled={integrationLoading || oneCFreshSyncSaving || !oneCFreshStatus.isConfigured}>
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
            <p>Статус подготовки печати показывается без раскрытия параметров фискального оборудования и шаблонов.</p>
          </div>
          {receiptPrintingError ? <FormError>{receiptPrintingError}</FormError> : null}
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
              <p className="empty-state" role="status" aria-live="polite">{receiptPrintingStatus.statusMessage}</p>
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
      {backupConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => !backupCreating && setBackupConfirmation(null)}>
          <section ref={backupConfirmationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="database-backup-confirmation-title" aria-describedby="database-backup-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="dialog-heading">
              <div>
                <p className="eyebrow">Резервные копии</p>
                <h3 id="database-backup-confirmation-title">Создать резервную копию базы?</h3>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть создание резервной копии" onClick={() => setBackupConfirmation(null)} disabled={backupCreating}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p className="confirmation-text" id="database-backup-confirmation-description">Система создаст PostgreSQL backup в отдельной папке, проверит его через pg_restore и запишет действие в историю изменений.</p>
            <FormField label="Причина создания копии">
              <textarea
                aria-label="Причина создания резервной копии"
                rows={3}
                value={backupConfirmation.reason}
                onChange={(event) => setBackupConfirmation({ reason: event.target.value, error: null })}
                placeholder="Например: перед обновлением программы"
                disabled={backupCreating}
              />
            </FormField>
            {backupConfirmation.error ? <FormError>{backupConfirmation.error}</FormError> : null}
            <div className="dialog-actions">
              <button ref={backupConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setBackupConfirmation(null)} disabled={backupCreating}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void createDatabaseBackup()} disabled={backupCreating}>
                <DatabaseBackup size={16} aria-hidden="true" />
                <span>{backupCreating ? 'Создаем и проверяем...' : 'Создать копию'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
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
            <p className="confirmation-text" id="password-change-confirmation-description">После подтверждения пароль будет изменен, а действие появится в истории изменений как смена учетных данных без раскрытия самого пароля.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля настройки">
              <li>
                <span className="dictionary-change-field">Пароль</span>
                <span className="dictionary-change-values">
                  <span className="dictionary-change-value">Без изменения</span>
                  <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                  <span className="dictionary-change-value dictionary-change-value-after">{formatSensitiveChange(pendingPasswordChange.newPassword)}</span>
                </span>
              </li>
            </ul>
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

function formatBackupKind(kind: string) {
  if (kind === 'manual') return 'Ручная'
  if (kind === 'automatic') return 'Автоматическая'
  if (kind === 'pre_update') return 'Перед обновлением'
  return kind
}

function formatFileSize(sizeBytes: number) {
  if (sizeBytes < 1024) return `${sizeBytes} Б`
  if (sizeBytes < 1024 * 1024) return `${(sizeBytes / 1024).toFixed(1)} КБ`
  return `${(sizeBytes / (1024 * 1024)).toFixed(1)} МБ`
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
    ? 'Повтор будет записан в историю изменений отдельным событием. До подключения адаптера система зарегистрирует запрос и не будет передавать данные во внешнюю 1C Fresh.'
    : 'Запуск будет записан в историю изменений. До подключения адаптера система зарегистрирует запрос и не будет передавать данные во внешнюю 1C Fresh.'
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
  return direction === 'pending_decision' ? 'Ожидает решения по направлению обмена' : direction
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
