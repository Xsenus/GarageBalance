import { useEffect, useId, useState } from 'react'
import type { FormEvent } from 'react'
import { DatabaseZap, FileText, RotateCcw, Save, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { AccessImportCreatedRecordDto, AccessImportQuarantineItemDto, AccessImportReaderStatusDto, AccessImportRunDto, AccessImportRunLogEntryDto, ImportClient } from '../../services/importApi'
import { LoadingSkeleton, TableLoadingState } from '../../shared/AsyncState'
import { buildImportReportFileName, downloadBlob } from '../../shared/fileExports'
import { FormField } from '../../shared/FormField'
import { FormError } from '../../shared/formFeedback'
import { formatDateTime, formatImportCheckStatus, formatImportCreatedRecordRollbackStatus, formatImportLogLevel, formatImportReaderStatus, formatImportRunCheckSummary, formatImportRunStatus } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { createClientPage } from '../../shared/pagination'
import { TablePagination } from '../../shared/TablePagination'

const importQuarantineScreenRequestLimit = 50
const importCreatedRecordsScreenRequestLimit = 100

type ImportTab = 'checks' | 'log' | 'created' | 'history' | 'quarantine'
export function ImportPanel({ auth, importClient }: { auth: AuthResponse; importClient: ImportClient }) {
  const fileInputId = useId()
  const [runs, setRuns] = useState<AccessImportRunDto[]>([])
  const [readerStatus, setReaderStatus] = useState<AccessImportReaderStatusDto | null>(null)
  const [quarantineItems, setQuarantineItems] = useState<AccessImportQuarantineItemDto[]>([])
  const [runLogEntries, setRunLogEntries] = useState<AccessImportRunLogEntryDto[]>([])
  const [createdRecords, setCreatedRecords] = useState<AccessImportCreatedRecordDto[]>([])
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [currentRun, setCurrentRun] = useState<AccessImportRunDto | null>(null)
  const [activeImportTab, setActiveImportTab] = useState<ImportTab>('checks')
  const [logPageNumber, setLogPageNumber] = useState(1)
  const [logPageSize, setLogPageSize] = useState(10)
  const [createdPageNumber, setCreatedPageNumber] = useState(1)
  const [createdPageSize, setCreatedPageSize] = useState(10)
  const [historyPageNumber, setHistoryPageNumber] = useState(1)
  const [historyPageSize, setHistoryPageSize] = useState(10)
  const [quarantinePageNumber, setQuarantinePageNumber] = useState(1)
  const [quarantinePageSize, setQuarantinePageSize] = useState(10)
  const [loading, setLoading] = useState(true)
  const [loadingLog, setLoadingLog] = useState(false)
  const [loadingCreatedRecords, setLoadingCreatedRecords] = useState(false)
  const [saving, setSaving] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [resolvingQuarantineId, setResolvingQuarantineId] = useState<string | null>(null)
  const [applyingRunId, setApplyingRunId] = useState<string | null>(null)
  const [applyTarget, setApplyTarget] = useState<AccessImportRunDto | null>(null)
  const [applyReason, setApplyReason] = useState('')
  const [applyBackupConfirmed, setApplyBackupConfirmed] = useState(false)
  const [applyError, setApplyError] = useState<string | null>(null)
  const [cancelingApplyRunId, setCancelingApplyRunId] = useState<string | null>(null)
  const [applyCancelTarget, setApplyCancelTarget] = useState<AccessImportRunDto | null>(null)
  const [applyCancelReason, setApplyCancelReason] = useState('')
  const [applyCancelError, setApplyCancelError] = useState<string | null>(null)
  const [rollbackingRunId, setRollbackingRunId] = useState<string | null>(null)
  const [rollbackTarget, setRollbackTarget] = useState<AccessImportRunDto | null>(null)
  const [rollbackReason, setRollbackReason] = useState('')
  const [rollbackError, setRollbackError] = useState<string | null>(null)
  const [quarantineResolveTarget, setQuarantineResolveTarget] = useState<AccessImportQuarantineItemDto | null>(null)
  const [quarantineResolveComment, setQuarantineResolveComment] = useState('')
  const [quarantineResolveError, setQuarantineResolveError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [exportMessage, setExportMessage] = useState<string | null>(null)
  const filePickerActionLabel = 'Выбрать файл Access .accdb или .mdb'
  const dryRunActionLabel = selectedFile ? `Проверить файл Access ${selectedFile.name}` : 'Проверить файл Access'
  const reportDownloadActionLabel = currentRun ? `Скачать JSON-отчет dry-run ${currentRun.originalFileName}` : 'Скачать JSON-отчет dry-run'
  const applyActionLabel = currentRun ? `Запросить фактический импорт ${currentRun.originalFileName}` : 'Запросить фактический импорт'
  const applyCancelActionLabel = currentRun ? `Отменить заявку на импорт ${currentRun.originalFileName}` : 'Отменить заявку на импорт'
  const rollbackActionLabel = currentRun ? `Запросить rollback импорта ${currentRun.originalFileName}` : 'Запросить rollback импорта'
  const applyDisabled = !currentRun || (currentRun.status !== 'completed' && currentRun.status !== 'import_request_cancelled') || applyingRunId !== null || cancelingApplyRunId !== null || rollbackingRunId !== null
  const applyCancelDisabled = !currentRun || currentRun.status !== 'import_requested' || cancelingApplyRunId !== null
  const rollbackDisabled = !currentRun || currentRun.status === 'rollback_requested' || currentRun.status === 'import_requested' || rollbackingRunId !== null || cancelingApplyRunId !== null
  useRestoreFocusOnClose(Boolean(quarantineResolveTarget))
  useRestoreFocusOnClose(Boolean(applyTarget))
  useRestoreFocusOnClose(Boolean(applyCancelTarget))
  useRestoreFocusOnClose(Boolean(rollbackTarget))
  const quarantineResolveCommentRef = useFocusOnOpen<HTMLTextAreaElement>(Boolean(quarantineResolveTarget))
  const quarantineResolveDialogRef = useFocusTrap<HTMLElement>(Boolean(quarantineResolveTarget))
  const applyReasonRef = useFocusOnOpen<HTMLTextAreaElement>(Boolean(applyTarget))
  const applyDialogRef = useFocusTrap<HTMLElement>(Boolean(applyTarget))
  const applyCancelReasonRef = useFocusOnOpen<HTMLTextAreaElement>(Boolean(applyCancelTarget))
  const applyCancelDialogRef = useFocusTrap<HTMLElement>(Boolean(applyCancelTarget))
  const rollbackReasonRef = useFocusOnOpen<HTMLTextAreaElement>(Boolean(rollbackTarget))
  const rollbackDialogRef = useFocusTrap<HTMLElement>(Boolean(rollbackTarget))
  const logPage = createClientPage(runLogEntries, logPageNumber, logPageSize)
  const createdPage = createClientPage(createdRecords, createdPageNumber, createdPageSize)
  const historyPage = createClientPage(runs, historyPageNumber, historyPageSize)
  const quarantinePage = createClientPage(quarantineItems, quarantinePageNumber, quarantinePageSize)
  const importTabs: Array<{ key: ImportTab; label: string; meta: string }> = [
    { key: 'checks', label: 'Проверки', meta: currentRun ? formatImportRunCheckSummary(currentRun) : 'ожидают запуска' },
    { key: 'log', label: 'Лог', meta: loadingLog ? 'загрузка' : `${runLogEntries.length} строк` },
    { key: 'created', label: 'Создано', meta: loadingCreatedRecords ? 'загрузка' : `${createdRecords.length} записей` },
    { key: 'history', label: 'История', meta: `${runs.length} запусков` },
    { key: 'quarantine', label: 'Карантин', meta: `${quarantineItems.length} открыто` },
  ]

  useEscapeKey(Boolean(quarantineResolveTarget) && resolvingQuarantineId === null, () => closeQuarantineResolveDialog())
  useEscapeKey(Boolean(applyTarget) && applyingRunId === null, () => closeApplyDialog())
  useEscapeKey(Boolean(applyCancelTarget) && cancelingApplyRunId === null, () => closeApplyCancelDialog())
  useEscapeKey(Boolean(rollbackTarget) && rollbackingRunId === null, () => closeRollbackDialog())

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [loadedReaderStatus, loadedRuns, loadedQuarantineItems] = await Promise.all([
          importClient.getAccessReaderStatus(auth.accessToken),
          importClient.getAccessRuns(auth.accessToken),
          importClient.getOpenQuarantineItems(auth.accessToken, undefined, importQuarantineScreenRequestLimit),
        ])
        if (!ignore) {
          setReaderStatus(loadedReaderStatus)
          setRuns(loadedRuns)
          setQuarantineItems(loadedQuarantineItems)
          setCurrentRun(loadedRuns[0] ?? null)
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить историю импорта.')
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
  }, [auth.accessToken, importClient])

  useEffect(() => {
    let ignore = false

    async function loadRunLog() {
      if (!currentRun) {
        setRunLogEntries([])
        return
      }

      setLogPageNumber(1)
      setLoadingLog(true)
      try {
        const entries = await importClient.getAccessRunLog(auth.accessToken, currentRun.id)
        if (!ignore) {
          setRunLogEntries(entries)
        }
      } catch (caught) {
        if (!ignore) {
          setRunLogEntries([])
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить лог импорта.')
        }
      } finally {
        if (!ignore) {
          setLoadingLog(false)
        }
      }
    }

    void loadRunLog()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, currentRun, importClient])

  useEffect(() => {
    let ignore = false

    async function loadCreatedRecords() {
      if (!currentRun) {
        setCreatedRecords([])
        return
      }

      setCreatedPageNumber(1)
      setLoadingCreatedRecords(true)
      try {
        const records = await importClient.getAccessCreatedRecords(auth.accessToken, currentRun.id, importCreatedRecordsScreenRequestLimit)
        if (!ignore) {
          setCreatedRecords(records)
        }
      } catch (caught) {
        if (!ignore) {
          setCreatedRecords([])
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить созданные импортом записи.')
        }
      } finally {
        if (!ignore) {
          setLoadingCreatedRecords(false)
        }
      }
    }

    void loadCreatedRecords()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, currentRun, importClient])

  async function runDryRun(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    if (!selectedFile) {
      setError('Выберите файл Access для проверки.')
      return
    }

    setSaving(true)
    setError(null)
    try {
      const run = await importClient.dryRunAccess(auth.accessToken, selectedFile)
      setCurrentRun(run)
      setRuns((items) => [run, ...items.filter((item) => item.id !== run.id)])
      setQuarantineItems(await importClient.getOpenQuarantineItems(auth.accessToken, undefined, importQuarantineScreenRequestLimit))
      setSelectedFile(null)
      setExportMessage(null)
      form.reset()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось выполнить dry-run импорта.')
    } finally {
      setSaving(false)
    }
  }

  async function downloadCurrentReport() {
    if (!currentRun) {
      return
    }

    setExporting(true)
    setError(null)
    setExportMessage(null)
    try {
      const blob = await importClient.downloadAccessRunReport(auth.accessToken, currentRun.id)
      downloadBlob(blob, buildImportReportFileName(currentRun))
      setExportMessage('Отчет dry-run импорта готов.')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось скачать отчет dry-run импорта.')
    } finally {
      setExporting(false)
    }
  }

  function openApplyDialog(run: AccessImportRunDto) {
    setApplyTarget(run)
    setApplyReason('')
    setApplyBackupConfirmed(false)
    setApplyError(null)
    setError(null)
    setExportMessage(null)
  }

  function closeApplyDialog() {
    setApplyTarget(null)
    setApplyReason('')
    setApplyBackupConfirmed(false)
    setApplyError(null)
  }

  async function submitApplyRequest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!applyTarget) {
      return
    }

    const reason = applyReason.trim()
    if (!reason) {
      setApplyError('Укажите причину фактического импорта.')
      return
    }

    if (!applyBackupConfirmed) {
      setApplyError('Подтвердите, что backup PostgreSQL создан перед импортом.')
      return
    }

    setApplyingRunId(applyTarget.id)
    setError(null)
    setExportMessage(null)
    try {
      const updatedRun = await importClient.requestAccessImportApply(auth.accessToken, applyTarget.id, reason, applyBackupConfirmed)
      setCurrentRun(updatedRun)
      setRuns((items) => items.map((item) => item.id === updatedRun.id ? updatedRun : item))
      setRunLogEntries(await importClient.getAccessRunLog(auth.accessToken, updatedRun.id))
      setExportMessage('Фактический импорт запрошен. Данные не переносились до подключения reader Access.')
      closeApplyDialog()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось запросить фактический импорт.')
    } finally {
      setApplyingRunId(null)
    }
  }

  function openApplyCancelDialog(run: AccessImportRunDto) {
    setApplyCancelTarget(run)
    setApplyCancelReason('')
    setApplyCancelError(null)
    setError(null)
    setExportMessage(null)
  }

  function closeApplyCancelDialog() {
    setApplyCancelTarget(null)
    setApplyCancelReason('')
    setApplyCancelError(null)
  }

  async function submitApplyCancelRequest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!applyCancelTarget) {
      return
    }

    const reason = applyCancelReason.trim()
    if (!reason) {
      setApplyCancelError('Укажите причину отмены заявки на импорт.')
      return
    }

    setCancelingApplyRunId(applyCancelTarget.id)
    setError(null)
    setExportMessage(null)
    try {
      const updatedRun = await importClient.cancelAccessImportApplyRequest(auth.accessToken, applyCancelTarget.id, reason)
      setCurrentRun(updatedRun)
      setRuns((items) => items.map((item) => item.id === updatedRun.id ? updatedRun : item))
      setRunLogEntries(await importClient.getAccessRunLog(auth.accessToken, updatedRun.id))
      setExportMessage('Заявка на фактический импорт отменена. Данные не переносились.')
      closeApplyCancelDialog()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось отменить заявку на импорт.')
    } finally {
      setCancelingApplyRunId(null)
    }
  }

  function openRollbackDialog(run: AccessImportRunDto) {
    setRollbackTarget(run)
    setRollbackReason('')
    setRollbackError(null)
    setError(null)
    setExportMessage(null)
  }

  function closeRollbackDialog() {
    setRollbackTarget(null)
    setRollbackReason('')
    setRollbackError(null)
  }

  async function submitRollbackRequest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!rollbackTarget) {
      return
    }

    const reason = rollbackReason.trim()
    if (!reason) {
      setRollbackError('Укажите причину rollback импорта.')
      return
    }

    setRollbackingRunId(rollbackTarget.id)
    setError(null)
    setExportMessage(null)
    try {
      const updatedRun = await importClient.requestAccessImportRollback(auth.accessToken, rollbackTarget.id, reason)
      setCurrentRun(updatedRun)
      setRuns((items) => items.map((item) => item.id === updatedRun.id ? updatedRun : item))
      setRunLogEntries(await importClient.getAccessRunLog(auth.accessToken, updatedRun.id))
      setExportMessage('Rollback импорта запрошен. Фактический откат данных не выполнялся для dry-run запуска.')
      closeRollbackDialog()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось запросить rollback импорта.')
    } finally {
      setRollbackingRunId(null)
    }
  }

  function openQuarantineResolveDialog(item: AccessImportQuarantineItemDto) {
    setQuarantineResolveTarget(item)
    setQuarantineResolveComment('')
    setQuarantineResolveError(null)
    setError(null)
    setExportMessage(null)
  }

  function closeQuarantineResolveDialog() {
    setQuarantineResolveTarget(null)
    setQuarantineResolveComment('')
    setQuarantineResolveError(null)
  }

  async function resolveQuarantineItem(item: AccessImportQuarantineItemDto, resolutionComment: string) {
    setResolvingQuarantineId(item.id)
    setError(null)
    setExportMessage(null)
    try {
      await importClient.resolveQuarantineItem(auth.accessToken, item.id, resolutionComment)
      setQuarantineItems((items) => items.filter((candidate) => candidate.id !== item.id))
      setExportMessage('Строка карантина закрыта.')
      closeQuarantineResolveDialog()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось закрыть строку карантина импорта.')
    } finally {
      setResolvingQuarantineId(null)
    }
  }

  async function submitQuarantineResolve(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!quarantineResolveTarget) {
      return
    }

    const comment = quarantineResolveComment.trim()
    if (!comment) {
      setQuarantineResolveError('Укажите комментарий к закрытию строки карантина.')
      return
    }

    await resolveQuarantineItem(quarantineResolveTarget, comment)
  }

  return (
    <section className="dictionary-panel" aria-label="Импорт Access">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Импорт</p>
          <h2>Проверка старой базы Access перед переносом</h2>
        </div>
        <span>{loading ? 'Загрузка...' : `${runs.length} запусков`}</span>
      </div>

      {error ? <FormError>{error}</FormError> : null}
      {exportMessage ? <div className="form-note" role="status" aria-live="polite">{exportMessage}</div> : null}

      <div className="import-workbench">
        <form className="dictionary-form" onSubmit={runDryRun}>
          <h3>Dry-run Access</h3>
          <div className="file-picker">
            <span className="form-field-label">Файл Access</span>
            <input id={fileInputId} aria-label="Файл Access" type="file" accept=".accdb,.mdb" onChange={(event) => setSelectedFile(event.target.files?.[0] ?? null)} />
            <label className="file-picker-button" htmlFor={fileInputId} title={filePickerActionLabel} data-tooltip={filePickerActionLabel}>
              <FileText size={16} aria-hidden="true" />
              <span>Выбрать .accdb или .mdb</span>
            </label>
            <span className="file-picker-name" role="status" aria-live="polite">{selectedFile ? selectedFile.name : 'Файл не выбран'}</span>
          </div>
          <button className="secondary-button" type="submit" aria-label={dryRunActionLabel} title={dryRunActionLabel} data-tooltip={dryRunActionLabel} disabled={saving || !selectedFile}>
            <DatabaseZap size={16} aria-hidden="true" />
            <span>Проверить файл</span>
          </button>
        </form>

        <div className="import-workbench-overview">
          <div className="dictionary-form import-reader-card">
            <h3>Reader Access</h3>
            {readerStatus ? (
              <>
                <p className={readerStatus.isAvailable ? 'status-active' : 'warning-text'} role="status" aria-live="polite">{formatImportReaderStatus(readerStatus.status)}</p>
                <p className="empty-state">{readerStatus.statusMessage}</p>
                {readerStatus.requiredComponents.length > 0 ? (
                  <div className="import-reader-requirements" aria-label="Требования reader Access">
                    {readerStatus.requiredComponents.map((component) => (
                      <div key={component}>
                        <span>Компонент</span>
                        <strong>{component}</strong>
                      </div>
                    ))}
                  </div>
                ) : null}
              </>
            ) : <LoadingSkeleton className="loading-skeleton--compact" label="Загружаем статус reader Access" rows={3} columns={2} />}
          </div>

          <section className="dictionary-form import-report-card" aria-labelledby="import-report-title">
            <h3 id="import-report-title">Отчет проверки</h3>
            {currentRun ? (
              <>
                <div className="import-report-group" aria-label="Проверенный файл и результат">
                  <h4>Файл и результат</h4>
                  <p className="import-report-file" role="status" aria-live="polite">
                    <strong>{currentRun.originalFileName}</strong>
                    <span>{formatImportRunCheckSummary(currentRun)}</span>
                  </p>
                  <p className="import-report-summary">{currentRun.summary}</p>
                </div>
                <div className="import-report-metrics" aria-label="Итоги dry-run импорта">
                  <div>
                    <span>Статус</span>
                    <strong>{formatImportRunStatus(currentRun.status)}</strong>
                  </div>
                  <div>
                    <span>Успешно</span>
                    <strong className="status-active">{currentRun.passedChecks}</strong>
                  </div>
                  <div>
                    <span>Предупреждения</span>
                    <strong className="warning-text">{currentRun.warningCount}</strong>
                  </div>
                  <div>
                    <span>Ошибки</span>
                    <strong className={currentRun.errorCount > 0 ? 'status-disabled' : 'status-active'}>{currentRun.errorCount}</strong>
                  </div>
                </div>
              </>
            ) : <p className="empty-state" role="status" aria-live="polite">Выберите запуск dry-run</p>}
            <div className="import-report-group import-report-actions" aria-label="Действия с отчетом проверки">
              <h4>Действия</h4>
              <div>
                <button className="secondary-button" type="button" aria-label={reportDownloadActionLabel} title={reportDownloadActionLabel} data-tooltip={reportDownloadActionLabel} disabled={!currentRun || exporting} onClick={downloadCurrentReport}>
                  <FileText size={16} aria-hidden="true" />
                  <span>Скачать отчет JSON</span>
                </button>
                <button className="secondary-button" type="button" aria-label={applyActionLabel} title={applyActionLabel} data-tooltip={applyActionLabel} disabled={applyDisabled} onClick={() => currentRun ? openApplyDialog(currentRun) : undefined}>
                  <DatabaseZap size={16} aria-hidden="true" />
                  <span>Запросить импорт</span>
                </button>
                <button className="secondary-button" type="button" aria-label={applyCancelActionLabel} title={applyCancelActionLabel} data-tooltip={applyCancelActionLabel} disabled={applyCancelDisabled} onClick={() => currentRun ? openApplyCancelDialog(currentRun) : undefined}>
                  <RotateCcw size={16} aria-hidden="true" />
                  <span>Отменить заявку</span>
                </button>
                <button className="secondary-button" type="button" aria-label={rollbackActionLabel} title={rollbackActionLabel} data-tooltip={rollbackActionLabel} disabled={rollbackDisabled} onClick={() => currentRun ? openRollbackDialog(currentRun) : undefined}>
                  <RotateCcw size={16} aria-hidden="true" />
                  <span>Запросить rollback</span>
                </button>
              </div>
            </div>
          </section>
        </div>
      </div>

      <div className="import-tabs" role="tablist" aria-label="Разделы импорта Access">
        {importTabs.map((tab) => (
          <button
            key={tab.key}
            type="button"
            role="tab"
            aria-selected={activeImportTab === tab.key}
            className={activeImportTab === tab.key ? 'is-active' : undefined}
            onClick={() => setActiveImportTab(tab.key)}
          >
            <span>{tab.label}</span>
            <small>{tab.meta}</small>
          </button>
        ))}
      </div>

      <div className="import-tab-panel" role="tabpanel" aria-label={importTabs.find((tab) => tab.key === activeImportTab)?.label}>
        {activeImportTab === 'checks' ? (
        <div className="operation-list import-table import-table--checks" role="table" aria-label="Проверки импорта">
          <div className="operation-row header" role="row">
            <span role="columnheader">Проверка</span>
            <span role="columnheader">Статус</span>
            <span role="columnheader">Итог</span>
          </div>
          {!currentRun ? <p className="empty-state" role="status" aria-live="polite">Проверок пока нет</p> : null}
          {currentRun?.checks.map((check) => (
            <div className="operation-row" role="row" key={check.code}>
              <span role="cell">
                <strong>{check.title}</strong>
                <small>{check.message}</small>
              </span>
              <span role="cell" className={check.status === 'passed' ? 'status-active' : check.status === 'warning' ? 'warning-text' : 'status-disabled'}>
                {formatImportCheckStatus(check.status)}
              </span>
              <span role="cell">{currentRun.originalFileName}</span>
            </div>
          ))}
        </div>
        ) : null}

        {activeImportTab === 'log' ? (
        <>
          <div className="operation-list import-table import-table--log" role="table" aria-label="Лог запуска Access">
            <div className="operation-row header" role="row">
              <span role="columnheader">Шаг</span>
              <span role="columnheader">Уровень</span>
              <span role="columnheader">Сообщение</span>
            </div>
            {loadingLog ? <TableLoadingState label="Загружаем лог импорта" /> : null}
            {!loadingLog && runLogEntries.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Лог выбранного запуска пока пуст</p> : null}
            {logPage.items.map((entry) => (
              <div className="operation-row" role="row" key={entry.id}>
                <span role="cell">
                  <strong>{entry.stepCode}</strong>
                  <small>{formatDateTime(entry.createdAtUtc)}</small>
                </span>
                <span role="cell" className={entry.level === 'info' ? 'status-active' : entry.level === 'warning' ? 'warning-text' : 'status-disabled'}>
                  {formatImportLogLevel(entry.level)}
                </span>
                <span role="cell">{entry.message}</span>
              </div>
            ))}
          </div>
          <TablePagination ariaLabel="Пагинация лога импорта" totalCount={logPage.totalCount} offset={logPage.offset} limit={logPage.limit} visibleCount={logPage.items.length} disabled={loadingLog} pageSizeLabel="Количество строк лога импорта" onPageChange={setLogPageNumber} onPageSizeChange={(limit) => { setLogPageSize(limit); setLogPageNumber(1) }} />
        </>
        ) : null}

        {activeImportTab === 'history' ? (
        <>
          <div className="operation-list import-table import-table--history" role="table" aria-label="История импорта Access">
            <div className="operation-row header" role="row">
              <span role="columnheader">Файл</span>
              <span role="columnheader">Статус</span>
              <span role="columnheader">Проверки</span>
            </div>
            {runs.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Истории импорта пока нет</p> : null}
            {historyPage.items.map((run) => (
              <button className="operation-row" role="row" type="button" key={run.id} onClick={() => setCurrentRun(run)}>
                <span role="cell">
                  <strong>{run.originalFileName}</strong>
                  <small>{run.summary}</small>
                </span>
                <span role="cell" className={run.status === 'completed' ? 'status-active' : run.status === 'rollback_requested' || run.status === 'import_requested' || run.status === 'import_request_cancelled' ? 'warning-text' : 'status-disabled'}>
                  {formatImportRunStatus(run.status)}
                </span>
                <span role="cell">{formatImportRunCheckSummary(run)}</span>
              </button>
            ))}
          </div>
          <TablePagination ariaLabel="Пагинация истории импорта" totalCount={historyPage.totalCount} offset={historyPage.offset} limit={historyPage.limit} visibleCount={historyPage.items.length} pageSizeLabel="Количество запусков импорта" onPageChange={setHistoryPageNumber} onPageSizeChange={(limit) => { setHistoryPageSize(limit); setHistoryPageNumber(1) }} />
        </>
        ) : null}

        {activeImportTab === 'created' ? (
        <>
          <div className="operation-list import-table import-table--created" role="table" aria-label="Созданные импортом записи Access">
            <div className="operation-row header" role="row">
              <span role="columnheader">Созданная запись</span>
              <span role="columnheader">Источник</span>
              <span role="columnheader">Rollback</span>
            </div>
            {loadingCreatedRecords ? <TableLoadingState label="Загружаем созданные импортом записи" /> : null}
            {!loadingCreatedRecords && createdRecords.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Созданные записи появятся после фактического переноса Access</p> : null}
            {createdPage.items.map((record) => (
              <div className="operation-row" role="row" key={record.id}>
                <span role="cell">
                  <strong>{record.targetDisplayName ?? record.targetEntityId}</strong>
                  <small>{record.targetEntityType} · {formatDateTime(record.createdAtUtc)}</small>
                </span>
                <span role="cell">
                  <strong>{record.sourceEntityType}{record.sourceExternalId ? ` #${record.sourceExternalId}` : ''}</strong>
                  <small>{record.sourceSystem} · {record.sourceRowHash.slice(0, 12)}</small>
                </span>
                <span role="cell" className={record.rollbackStatus === 'created' ? 'warning-text' : record.rollbackStatus === 'rolled_back' ? 'status-disabled' : 'status-active'}>
                  {formatImportCreatedRecordRollbackStatus(record.rollbackStatus)}
                </span>
              </div>
            ))}
          </div>
          <TablePagination ariaLabel="Пагинация созданных импортом записей" totalCount={createdPage.totalCount} offset={createdPage.offset} limit={createdPage.limit} visibleCount={createdPage.items.length} disabled={loadingCreatedRecords} pageSizeLabel="Количество созданных импортом записей" onPageChange={setCreatedPageNumber} onPageSizeChange={(limit) => { setCreatedPageSize(limit); setCreatedPageNumber(1) }} />
        </>
        ) : null}

        {activeImportTab === 'quarantine' ? (
        <>
          <div className="operation-list import-table import-table--quarantine" role="table" aria-label="Карантин импорта Access">
            <div className="operation-row header" role="row">
              <span role="columnheader">Строка</span>
              <span role="columnheader">Причина</span>
              <span role="columnheader">Действие</span>
            </div>
            {quarantineItems.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Открытых строк карантина нет</p> : null}
            {quarantinePage.items.map((item) => (
              <div className="operation-row" role="row" key={item.id}>
                <span role="cell">
                  <strong>{item.entityType}{item.externalId ? ` #${item.externalId}` : ''}</strong>
                  <small>{item.sourceSystem} · {item.rowHash.slice(0, 12)}</small>
                </span>
                <span role="cell" className={item.severity === 'warning' ? 'warning-text' : 'status-disabled'}>
                  <strong>{item.reasonCode}</strong>
                  <small>{item.reasonMessage}</small>
                </span>
                <span role="cell">
                  <button className="secondary-button" type="button" title={`Закрыть строку карантина ${item.entityType}${item.externalId ? ` #${item.externalId}` : ''}`} data-tooltip="Закрыть" disabled={resolvingQuarantineId === item.id} onClick={() => openQuarantineResolveDialog(item)}>
                    <Save size={16} />
                    <span>Закрыть</span>
                  </button>
                </span>
              </div>
            ))}
          </div>
          <TablePagination ariaLabel="Пагинация карантина импорта" totalCount={quarantinePage.totalCount} offset={quarantinePage.offset} limit={quarantinePage.limit} visibleCount={quarantinePage.items.length} pageSizeLabel="Количество строк карантина импорта" onPageChange={setQuarantinePageNumber} onPageSizeChange={(limit) => { setQuarantinePageSize(limit); setQuarantinePageNumber(1) }} />
        </>
        ) : null}
      </div>
      {applyTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (applyingRunId === null) {
            closeApplyDialog()
          }
        }}>
          <section ref={applyDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="import-apply-title" aria-describedby="import-apply-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Фактический импорт</p>
                <h3 id="import-apply-title">Запросить фактический импорт?</h3>
                <p>{applyTarget.originalFileName} · {formatImportRunStatus(applyTarget.status)}</p>
              </div>
              <button className="icon-button" type="button" onClick={closeApplyDialog} aria-label="Закрыть подтверждение фактического импорта" title="Закрыть подтверждение фактического импорта" data-tooltip="Закрыть" disabled={applyingRunId !== null}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p className="confirmation-text" id="import-apply-description">Заявка будет записана в историю импорта. До подключения reader Access система не переносит строки в рабочую базу, поэтому перед реальным переносом нужен свежий backup PostgreSQL.</p>
            <form className="dictionary-modal-form" onSubmit={submitApplyRequest}>
              <FormField label="Причина импорта">
                <textarea
                  ref={applyReasonRef}
                  aria-label="Причина фактического импорта"
                  aria-invalid={Boolean(applyError)}
                  aria-describedby={applyError ? 'import-apply-error' : undefined}
                  rows={3}
                  maxLength={1000}
                  value={applyReason}
                  onChange={(event) => {
                    setApplyReason(event.target.value)
                    if (applyError && event.target.value.trim() && applyBackupConfirmed) {
                      setApplyError(null)
                    }
                  }}
                  placeholder="Например: dry-run проверен, backup PostgreSQL создан"
                  disabled={applyingRunId !== null}
                />
              </FormField>
              <label className="checkbox-label">
                <input
                  type="checkbox"
                  aria-label="Backup PostgreSQL создан перед фактическим импортом"
                  checked={applyBackupConfirmed}
                  onChange={(event) => {
                    setApplyBackupConfirmed(event.target.checked)
                    if (applyError && event.target.checked && applyReason.trim()) {
                      setApplyError(null)
                    }
                  }}
                  disabled={applyingRunId !== null}
                />
                <span>Backup PostgreSQL создан перед фактическим импортом</span>
              </label>
              <dl className="fund-operation-preview">
                <div>
                  <dt>Запуск</dt>
                  <dd>{applyTarget.originalFileName}</dd>
                </div>
                <div>
                  <dt>Проверки</dt>
                  <dd>{formatImportRunCheckSummary(applyTarget)}</dd>
                </div>
              </dl>
              {applyError ? <p className="form-error" id="import-apply-error" role="alert">{applyError}</p> : null}
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeApplyDialog} disabled={applyingRunId !== null}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={applyingRunId !== null}>
                  <DatabaseZap size={16} aria-hidden="true" />
                  <span>{applyingRunId === applyTarget.id ? 'Запрашиваем...' : 'Запросить импорт'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
      {applyCancelTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (cancelingApplyRunId === null) {
            closeApplyCancelDialog()
          }
        }}>
          <section ref={applyCancelDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="import-apply-cancel-title" aria-describedby="import-apply-cancel-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Отмена заявки</p>
                <h3 id="import-apply-cancel-title">Отменить заявку на импорт?</h3>
                <p>{applyCancelTarget.originalFileName} · {formatImportRunStatus(applyCancelTarget.status)}</p>
              </div>
              <button className="icon-button" type="button" onClick={closeApplyCancelDialog} aria-label="Закрыть подтверждение отмены заявки на импорт" title="Закрыть подтверждение отмены заявки на импорт" data-tooltip="Закрыть" disabled={cancelingApplyRunId !== null}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p className="confirmation-text" id="import-apply-cancel-description">Отмена заявки будет записана в историю импорта. Это не rollback данных: строки Access еще не переносились в рабочую базу.</p>
            <form className="dictionary-modal-form" onSubmit={submitApplyCancelRequest}>
              <FormField label="Причина отмены заявки">
                <textarea
                  ref={applyCancelReasonRef}
                  aria-label="Причина отмены заявки на импорт"
                  aria-invalid={Boolean(applyCancelError)}
                  aria-describedby={applyCancelError ? 'import-apply-cancel-error' : undefined}
                  rows={3}
                  maxLength={1000}
                  value={applyCancelReason}
                  onChange={(event) => {
                    setApplyCancelReason(event.target.value)
                    if (applyCancelError && event.target.value.trim()) {
                      setApplyCancelError(null)
                    }
                  }}
                  placeholder="Например: нужно перепроверить backup"
                  disabled={cancelingApplyRunId !== null}
                />
              </FormField>
              <dl className="fund-operation-preview">
                <div>
                  <dt>Запуск</dt>
                  <dd>{applyCancelTarget.originalFileName}</dd>
                </div>
                <div>
                  <dt>Проверки</dt>
                  <dd>{formatImportRunCheckSummary(applyCancelTarget)}</dd>
                </div>
              </dl>
              {applyCancelError ? <p className="form-error" id="import-apply-cancel-error" role="alert">{applyCancelError}</p> : null}
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeApplyCancelDialog} disabled={cancelingApplyRunId !== null}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={cancelingApplyRunId !== null}>
                  <RotateCcw size={16} aria-hidden="true" />
                  <span>{cancelingApplyRunId === applyCancelTarget.id ? 'Отменяем...' : 'Отменить заявку'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
      {rollbackTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (rollbackingRunId === null) {
            closeRollbackDialog()
          }
        }}>
          <section ref={rollbackDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="import-rollback-title" aria-describedby="import-rollback-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Rollback импорта</p>
                <h3 id="import-rollback-title">Запросить rollback импорта?</h3>
                <p>{rollbackTarget.originalFileName} · {formatImportRunStatus(rollbackTarget.status)}</p>
              </div>
              <button className="icon-button" type="button" onClick={closeRollbackDialog} aria-label="Закрыть подтверждение rollback импорта" title="Закрыть подтверждение rollback импорта" data-tooltip="Закрыть" disabled={rollbackingRunId !== null}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p className="confirmation-text" id="import-rollback-description">Rollback будет записан в историю импорта с причиной. Для dry-run запуска фактический откат данных не выполняется, потому что данные еще не переносились в рабочую базу.</p>
            <form className="dictionary-modal-form" onSubmit={submitRollbackRequest}>
              <FormField label="Причина rollback">
                <textarea
                  ref={rollbackReasonRef}
                  aria-label="Причина rollback импорта"
                  aria-invalid={Boolean(rollbackError)}
                  aria-describedby={rollbackError ? 'import-rollback-error' : undefined}
                  rows={3}
                  maxLength={1000}
                  value={rollbackReason}
                  onChange={(event) => {
                    setRollbackReason(event.target.value)
                    if (rollbackError && event.target.value.trim()) {
                      setRollbackError(null)
                    }
                  }}
                  placeholder="Например: выбран неверный файл старой базы"
                  disabled={rollbackingRunId !== null}
                />
              </FormField>
              <dl className="fund-operation-preview">
                <div>
                  <dt>Запуск</dt>
                  <dd>{rollbackTarget.originalFileName}</dd>
                </div>
                <div>
                  <dt>Проверки</dt>
                  <dd>{formatImportRunCheckSummary(rollbackTarget)}</dd>
                </div>
              </dl>
              {rollbackError ? <p className="form-error" id="import-rollback-error" role="alert">{rollbackError}</p> : null}
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeRollbackDialog} disabled={rollbackingRunId !== null}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={rollbackingRunId !== null}>
                  <RotateCcw size={16} aria-hidden="true" />
                  <span>{rollbackingRunId === rollbackTarget.id ? 'Запрашиваем...' : 'Запросить rollback'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
      {quarantineResolveTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (resolvingQuarantineId === null) {
            closeQuarantineResolveDialog()
          }
        }}>
          <section ref={quarantineResolveDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="import-quarantine-resolve-title" aria-describedby="import-quarantine-resolve-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Карантин импорта</p>
                <h3 id="import-quarantine-resolve-title">Закрыть строку карантина?</h3>
                <p>{quarantineResolveTarget.entityType}{quarantineResolveTarget.externalId ? ` #${quarantineResolveTarget.externalId}` : ''} · {quarantineResolveTarget.reasonCode}</p>
              </div>
              <button className="icon-button" type="button" onClick={closeQuarantineResolveDialog} aria-label="Закрыть подтверждение карантина" title="Закрыть подтверждение карантина" data-tooltip="Закрыть" disabled={resolvingQuarantineId !== null}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p className="confirmation-text" id="import-quarantine-resolve-description">Строка исчезнет из списка открытого карантина, а комментарий будет записан в историю импорта. Убедитесь, что причина разобрана и перенос можно продолжать безопасно.</p>
            <form className="dictionary-modal-form" onSubmit={submitQuarantineResolve}>
              <FormField label="Комментарий">
                <textarea
                  ref={quarantineResolveCommentRef}
                  aria-label="Комментарий к закрытию строки карантина"
                  aria-invalid={Boolean(quarantineResolveError)}
                  aria-describedby={quarantineResolveError ? 'import-quarantine-resolve-error' : undefined}
                  rows={3}
                  maxLength={1000}
                  value={quarantineResolveComment}
                  onChange={(event) => {
                    setQuarantineResolveComment(event.target.value)
                    if (quarantineResolveError && event.target.value.trim()) {
                      setQuarantineResolveError(null)
                    }
                  }}
                  placeholder="Например: владелец найден и сопоставлен вручную"
                  disabled={resolvingQuarantineId !== null}
                />
              </FormField>
              <dl className="fund-operation-preview">
                <div>
                  <dt>Строка</dt>
                  <dd>{quarantineResolveTarget.sourceSystem} · {quarantineResolveTarget.rowHash.slice(0, 12)}</dd>
                </div>
                <div>
                  <dt>Причина</dt>
                  <dd>{quarantineResolveTarget.reasonMessage}</dd>
                </div>
              </dl>
              {quarantineResolveError ? <p className="form-error" id="import-quarantine-resolve-error" role="alert">{quarantineResolveError}</p> : null}
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeQuarantineResolveDialog} disabled={resolvingQuarantineId !== null}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={resolvingQuarantineId !== null}>
                  <Save size={16} aria-hidden="true" />
                  <span>{resolvingQuarantineId === quarantineResolveTarget.id ? 'Закрываем...' : 'Закрыть строку'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
    </section>
  )
}
