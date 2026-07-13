import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Eye, RefreshCw, ShieldCheck, X } from 'lucide-react'
import type { AuthClient, AuthResponse, CurrentUserDto } from '../../services/authApi'
import type { IntegrationClient, OneCFreshIntegrationStatusDto, OneCFreshSyncDto, OneCFreshSyncPreviewDto, ReceiptPrintingIntegrationStatusDto } from '../../services/integrationsApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { formatSensitiveChange } from '../../shared/changePreview'
import { FormField } from '../../shared/FormField'
import { FormError, FormValidationSummary } from '../../shared/formFeedback'
import { formatDateTime } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { getPasswordChangeValidationErrors } from '../../shared/validation'
export function PasswordPanel({ auth, authClient, integrationClient, onUserChanged }: { auth: AuthResponse; authClient: AuthClient; integrationClient: IntegrationClient; onUserChanged: (user: CurrentUserDto) => void }) {
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
  const canViewIntegrationStatus = hasPermission(auth, permissions.importRun)
  const canViewReceiptPrintingStatus = hasPermission(auth, permissions.paymentsWrite)
  const canManageIntegrationSettings = hasPermission(auth, permissions.usersManage)
  useRestoreFocusOnClose(Boolean(pendingPasswordChange))
  const confirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingPasswordChange))
  const confirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingPasswordChange))
  const oneCFreshSyncCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(oneCFreshSyncConfirmation))
  const oneCFreshSyncDialogRef = useFocusTrap<HTMLElement>(Boolean(oneCFreshSyncConfirmation))
  useEscapeKey(Boolean(pendingPasswordChange) && !saving, () => setPendingPasswordChange(null))
  useEscapeKey(Boolean(oneCFreshSyncConfirmation) && !oneCFreshSyncSaving, () => closeOneCFreshSyncConfirmation())

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
      <section className="password-panel" aria-label="Безопасность аккаунта">
        <div>
          <p className="eyebrow">Безопасность</p>
          <h2>Смена пароля</h2>
          <p>Пользователь может обновить свой пароль без участия администратора. Текущий пароль нужен для подтверждения действия.</p>
        </div>
        <form className="dictionary-form" onSubmit={handleSubmit}>
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
      {canViewIntegrationStatus ? (
        <section className="password-panel" aria-label="Интеграция 1C Fresh">
          <div>
            <p className="eyebrow">Интеграции</p>
            <h2>1C Fresh</h2>
            <p>Статус подготовки будущей синхронизации показывается без раскрытия токенов и других защищенных настроек.</p>
          </div>
          {integrationError ? <FormError>{integrationError}</FormError> : null}
          {integrationLoading ? <p className="empty-state" role="status" aria-live="polite">Загрузка статуса 1C Fresh...</p> : null}
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
          {receiptPrintingLoading ? <p className="empty-state" role="status" aria-live="polite">Загрузка статуса печати...</p> : null}
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
      {canManageIntegrationSettings ? (
        <section className="password-panel" aria-label="Подсказки DaData">
          <div>
            <p className="eyebrow">Интеграции</p>
            <h2>DaData</h2>
            <p>Ключ используется для подсказок организаций по ИНН и адресов в карточке поставщика.</p>
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
      {oneCFreshSyncConfirmation ? (
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
