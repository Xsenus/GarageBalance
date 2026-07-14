import { useCallback, useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Minus, Pencil, Plus, RefreshCw, RotateCcw, Save, Trash2, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { FundDto, FundOperationDto, FundsClient } from '../../services/fundsApi'
import type { ChangePreview } from '../../shared/changePreview'
import { appendChangePreview, formatChangeMoney, formatChangeText } from '../../shared/changePreview'
import { LoadingSkeleton, TableLoadingState } from '../../shared/AsyncState'
import { FormField } from '../../shared/FormField'
import { formatDateTime, formatMoney } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { createEmptyPage, createFallbackPage } from '../../shared/pagination'
import { TablePagination } from '../../shared/TablePagination'
type FundPrototypeRow = {
  id: string
  name: string
  amount: number | null
  actions?: false
}

type FundOperationKind = 'withdraw' | 'deposit'

type FundOperationDraft = {
  kind: FundOperationKind
  fundId: string
  fundName: string
  amount: string
  reason: string
}

type FundOperationStatusDraft = {
  action: 'cancel' | 'restore'
  operation: FundOperationDto
  reason: string
}

type FundOperationEditDraft = {
  operation: FundOperationDto
  amount: string
  reason: string
}

type FundOperationEditConfirmation = {
  operation: FundOperationDto
  amount: number
  reason: string
  changes: ChangePreview[]
}

type FundOperationReverseDraft = {
  operation: FundOperationDto
  reason: string
}

function mapFundDtoToPrototypeRow(fund: FundDto): FundPrototypeRow {
  return {
    id: fund.id,
    name: fund.name,
    amount: fund.balance,
    actions: fund.allowOperations ? undefined : false,
  }
}

export function FundsPrototypePanel({ auth, fundsClient }: { auth: AuthResponse; fundsClient: FundsClient }) {
  const [rows, setRows] = useState<FundPrototypeRow[]>([])
  const [operationPage, setOperationPage] = useState(() => createEmptyPage<FundOperationDto>(25))
  const [availableToDistribute, setAvailableToDistribute] = useState<number | null>(null)
  const [operation, setOperation] = useState<FundOperationDraft | null>(null)
  const [operationEdit, setOperationEdit] = useState<FundOperationEditDraft | null>(null)
  const [operationEditConfirmation, setOperationEditConfirmation] = useState<FundOperationEditConfirmation | null>(null)
  const [operationReverse, setOperationReverse] = useState<FundOperationReverseDraft | null>(null)
  const [statusAction, setStatusAction] = useState<FundOperationStatusDraft | null>(null)
  const [operationError, setOperationError] = useState<string | null>(null)
  const [operationMessage, setOperationMessage] = useState<string | null>(null)
  const [fundsLoading, setFundsLoading] = useState(true)
  const [operationsLoading, setOperationsLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [savingOperation, setSavingOperation] = useState(false)
  const [savingStatusAction, setSavingStatusAction] = useState(false)
  useRestoreFocusOnClose(Boolean(operation))
  useRestoreFocusOnClose(Boolean(operationEdit))
  useRestoreFocusOnClose(Boolean(operationEditConfirmation))
  useRestoreFocusOnClose(Boolean(operationReverse))
  useRestoreFocusOnClose(Boolean(statusAction))
  const operationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(operation))
  const operationDialogRef = useFocusTrap<HTMLElement>(Boolean(operation))
  const operationEditCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(operationEdit) && !operationEditConfirmation)
  const operationEditDialogRef = useFocusTrap<HTMLElement>(Boolean(operationEdit) && !operationEditConfirmation)
  const operationEditConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(operationEditConfirmation))
  const operationEditConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(operationEditConfirmation))
  const operationReverseReasonRef = useFocusOnOpen<HTMLTextAreaElement>(Boolean(operationReverse))
  const operationReverseDialogRef = useFocusTrap<HTMLElement>(Boolean(operationReverse))
  const statusReasonRef = useFocusOnOpen<HTMLTextAreaElement>(statusAction?.action === 'cancel')
  const statusCancelRef = useFocusOnOpen<HTMLButtonElement>(statusAction?.action === 'restore')
  const statusDialogRef = useFocusTrap<HTMLElement>(Boolean(statusAction))

  useEscapeKey(Boolean(operation), () => closeFundOperation())
  useEscapeKey(Boolean(operationEdit) && !operationEditConfirmation && !savingOperation, () => closeFundOperationEdit())
  useEscapeKey(Boolean(operationEditConfirmation) && !savingOperation, () => setOperationEditConfirmation(null))
  useEscapeKey(Boolean(operationReverse) && !savingOperation, () => closeFundOperationReverse())
  useEscapeKey(Boolean(statusAction) && !savingStatusAction, () => closeFundStatusAction())

  const getOperationsPage = useCallback(async (pageNumber: number, limit: number) => {
    const offset = (pageNumber - 1) * limit
    if (fundsClient.getOperationsPage) {
      return fundsClient.getOperationsPage(auth.accessToken, { offset, limit, includeCanceled: true })
    }

    const operations = await fundsClient.getOperations(auth.accessToken, { limit: 100, includeCanceled: true })
    return createFallbackPage(operations, offset, limit)
  }, [auth.accessToken, fundsClient])

  useEffect(() => {
    let cancelled = false

    async function loadFunds() {
      setLoadError(null)
      setFundsLoading(true)
      try {
        const funds = await fundsClient.getFunds(auth.accessToken)
        if (!cancelled) {
          setRows(funds.map(mapFundDtoToPrototypeRow))
          setAvailableToDistribute(funds.length > 0 ? funds[0].availableToDistribute : null)
        }
      } catch (error: unknown) {
        if (!cancelled) {
          setLoadError(error instanceof Error ? error.message : 'Не удалось загрузить фонды.')
        }
      } finally {
        if (!cancelled) {
          setFundsLoading(false)
        }
      }
    }

    async function loadOperations() {
      setOperationsLoading(true)
      try {
        const operations = await getOperationsPage(1, 25)
        if (!cancelled) {
          setOperationPage(operations)
        }
      } catch (error: unknown) {
        if (!cancelled) {
          setLoadError(error instanceof Error ? error.message : 'Не удалось загрузить операции фондов.')
        }
      } finally {
        if (!cancelled) {
          setOperationsLoading(false)
        }
      }
    }

    void loadFunds()
    void loadOperations()

    return () => {
      cancelled = true
    }
  }, [auth.accessToken, fundsClient, getOperationsPage])

  async function changeOperationsPage(pageNumber: number, limit = operationPage.limit) {
    setOperationsLoading(true)
    setLoadError(null)
    try {
      setOperationPage(await getOperationsPage(pageNumber, limit))
    } catch (error: unknown) {
      setLoadError(error instanceof Error ? error.message : 'Не удалось загрузить операции фондов.')
    } finally {
      setOperationsLoading(false)
    }
  }

  async function refreshFundsPanel() {
    const currentPageNumber = Math.floor(operationPage.offset / operationPage.limit) + 1
    const [funds, operations] = await Promise.all([
      fundsClient.getFunds(auth.accessToken),
      getOperationsPage(currentPageNumber, operationPage.limit),
    ])
    setRows(funds.map(mapFundDtoToPrototypeRow))
    setOperationPage(operations)
    setAvailableToDistribute(funds.length > 0 ? funds[0].availableToDistribute : null)
  }

  function openFundOperation(kind: FundOperationKind, fund: FundPrototypeRow) {
    setOperation({ kind, fundId: fund.id, fundName: fund.name, amount: '', reason: '' })
    setOperationError(null)
    setOperationMessage(null)
  }

  function closeFundOperation() {
    setOperation(null)
    setOperationError(null)
  }

  function openFundOperationEdit(fundOperation: FundOperationDto) {
    setOperationEdit({
      operation: fundOperation,
      amount: String(fundOperation.amount).replace('.', ','),
      reason: fundOperation.reason,
    })
    setOperationError(null)
    setOperationMessage(null)
  }

  function closeFundOperationEdit() {
    setOperationEdit(null)
    setOperationEditConfirmation(null)
    setOperationError(null)
  }

  function getFundOperationEditChanges(operation: FundOperationDto, amount: number, reason: string) {
    const changes: ChangePreview[] = []
    appendChangePreview(changes, 'Сумма', formatChangeMoney(operation.amount), formatChangeMoney(amount))
    appendChangePreview(changes, 'Основание', formatChangeText(operation.reason), formatChangeText(reason))
    return changes
  }

  function openFundOperationReverse(fundOperation: FundOperationDto) {
    setOperationReverse({ operation: fundOperation, reason: '' })
    setOperationError(null)
    setOperationMessage(null)
  }

  function closeFundOperationReverse() {
    setOperationReverse(null)
    setOperationError(null)
  }

  function closeFundStatusAction() {
    setStatusAction(null)
    setOperationError(null)
  }

  function openFundStatusAction(action: 'cancel' | 'restore', fundOperation: FundOperationDto) {
    setStatusAction({ action, operation: fundOperation, reason: '' })
    setOperationError(null)
    setOperationMessage(null)
  }

  function parseFundOperationAmount(value: string) {
    const normalized = value.trim().replace(',', '.')
    if (!normalized) {
      return null
    }

    const amount = Number(normalized)
    return Number.isFinite(amount) && amount > 0 ? amount : null
  }

  function getReverseFundOperationKind(kind: FundOperationKind): FundOperationKind {
    return kind === 'deposit' ? 'withdraw' : 'deposit'
  }

  async function submitFundOperation(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!operation) {
      return
    }

    const amount = parseFundOperationAmount(operation.amount)
    const reason = operation.reason.trim()
    if (amount === null) {
      setOperationError('Укажите сумму больше нуля.')
      return
    }

    if (!reason) {
      setOperationError('Укажите причину операции.')
      return
    }

    if (operation.kind === 'deposit' && availableToDistribute !== null && amount > availableToDistribute) {
      setOperationError(`Сумма пополнения не может превышать доступную к распределению сумму ${formatMoney(availableToDistribute)} руб.`)
      return
    }

    setSavingOperation(true)
    try {
      const savedOperation = await fundsClient.createOperation(auth.accessToken, operation.fundId, {
        operationKind: operation.kind,
        amount,
        reason,
      })
      await refreshFundsPanel()
      setOperationMessage(`${operation.kind === 'deposit' ? 'Пополнение' : 'Изъятие'} по фонду "${savedOperation.fundName}" сохранено и записано в историю изменений.`)
      closeFundOperation()
    } catch (error: unknown) {
      setOperationError(error instanceof Error ? error.message : 'Не удалось выполнить операцию фонда.')
    } finally {
      setSavingOperation(false)
    }
  }

  async function submitFundStatusAction(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!statusAction) {
      return
    }

    if (statusAction.action === 'cancel' && !statusAction.reason.trim()) {
      setOperationError('Укажите причину отмены операции фонда.')
      return
    }

    setSavingStatusAction(true)
    try {
      if (statusAction.action === 'cancel') {
        await fundsClient.cancelOperation(auth.accessToken, statusAction.operation.id, { reason: statusAction.reason.trim() })
      } else {
        await fundsClient.restoreOperation(auth.accessToken, statusAction.operation.id)
      }
      await refreshFundsPanel()
      setOperationMessage(`${statusAction.action === 'cancel' ? 'Операция отменена' : 'Операция восстановлена'} и записана в историю изменений.`)
      closeFundStatusAction()
    } catch (error: unknown) {
      setOperationError(error instanceof Error ? error.message : 'Не удалось изменить статус операции фонда.')
    } finally {
      setSavingStatusAction(false)
    }
  }

  async function submitFundOperationEdit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!operationEdit) {
      return
    }

    const amount = parseFundOperationAmount(operationEdit.amount)
    const reason = operationEdit.reason.trim()
    if (amount === null) {
      setOperationError('Укажите сумму больше нуля.')
      return
    }

    if (!reason) {
      setOperationError('Укажите основание изменения операции фонда.')
      return
    }

    const changes = getFundOperationEditChanges(operationEdit.operation, amount, reason)
    if (changes.length === 0) {
      closeFundOperationEdit()
      return
    }

    setOperationEditConfirmation({ operation: operationEdit.operation, amount, reason, changes })
  }

  async function confirmFundOperationEdit() {
    if (!operationEditConfirmation) {
      return
    }

    setSavingOperation(true)
    try {
      const savedOperation = await fundsClient.updateOperation(auth.accessToken, operationEditConfirmation.operation.id, {
        amount: operationEditConfirmation.amount,
        reason: operationEditConfirmation.reason,
      })
      await refreshFundsPanel()
      setOperationMessage(`Операция фонда "${savedOperation.fundName}" изменена и записана в историю изменений.`)
      setOperationEditConfirmation(null)
      closeFundOperationEdit()
    } catch (error: unknown) {
      setOperationError(error instanceof Error ? error.message : 'Не удалось изменить операцию фонда.')
    } finally {
      setSavingOperation(false)
    }
  }

  async function submitFundOperationReverse(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!operationReverse) {
      return
    }

    const reason = operationReverse.reason.trim()
    if (!reason) {
      setOperationError('Укажите причину обратной операции фонда.')
      return
    }

    const reverseKind = getReverseFundOperationKind(operationReverse.operation.operationKind)
    setSavingOperation(true)
    try {
      const savedOperation = await fundsClient.createOperation(auth.accessToken, operationReverse.operation.fundId, {
        operationKind: reverseKind,
        amount: operationReverse.operation.amount,
        reason,
      })
      await refreshFundsPanel()
      setOperationMessage(`Обратная операция фонда "${savedOperation.fundName}" создана и записана в историю изменений.`)
      closeFundOperationReverse()
    } catch (error: unknown) {
      setOperationError(error instanceof Error ? error.message : 'Не удалось создать обратную операцию фонда.')
    } finally {
      setSavingOperation(false)
    }
  }

  const operationAmount = operation ? parseFundOperationAmount(operation.amount) : null
  const operationEditAmount = operationEdit ? parseFundOperationAmount(operationEdit.amount) : null
  const operationActionLabel = operation?.kind === 'deposit' ? 'Пополнить фонд' : 'Изъять из фонда'
  const statusActionTitle = statusAction?.action === 'cancel' ? 'Отменить операцию фонда?' : 'Вернуть операцию фонда?'
  const statusActionLabel = statusAction?.operation.operationKind === 'deposit' ? 'Пополнение' : 'Изъятие'
  const operationReverseKind = operationReverse ? getReverseFundOperationKind(operationReverse.operation.operationKind) : null
  const operationReverseLabel = operationReverseKind === 'deposit' ? 'Пополнение' : 'Изъятие'

  return (
    <section className="funds-page" aria-label="Управление фондами">
      <div className="funds-heading">
        <h1>Управление фондами</h1>
      </div>

      <div className="funds-sheet">
        {loadError ? <p className="form-error" role="alert">{loadError}</p> : null}
        {fundsLoading ? (
          <TableLoadingState label="Загружаем фонды" />
        ) : (
          <table className="funds-table" aria-label="Фонды и собранные суммы">
          <thead>
            <tr>
              <th scope="col">Фонд</th>
              <th scope="col">Собранная сумма</th>
              <th className="funds-table-action-column" scope="col">Изъятие</th>
              <th className="funds-table-action-column" scope="col">Пополнение</th>
            </tr>
          </thead>
          <tbody>
            {rows.length > 0 ? rows.map((row) => (
              <tr key={row.id}>
                <td>{row.name}</td>
                <td>{row.amount === null ? '—' : `${formatMoney(row.amount)} руб.`}</td>
                <td className="funds-table-action-column">
                  {row.actions === false ? null : (
                    <button className="funds-action-button funds-action-button--withdraw" type="button" aria-label={`Изъять из фонда ${row.name}`} title={`Изъять из фонда ${row.name}`} data-tooltip="Изъять" onClick={() => openFundOperation('withdraw', row)}>
                      <Minus size={16} aria-hidden="true" />
                    </button>
                  )}
                </td>
                <td className="funds-table-action-column">
                  {row.actions === false ? null : (
                    <button className="funds-action-button funds-action-button--deposit" type="button" aria-label={`Пополнить фонд ${row.name}`} title={`Пополнить фонд ${row.name}`} data-tooltip="Пополнить" onClick={() => openFundOperation('deposit', row)}>
                      <Plus size={16} aria-hidden="true" />
                    </button>
                  )}
                </td>
              </tr>
            )) : (
              <tr>
                <td colSpan={4}>Фонды пока не настроены.</td>
              </tr>
            )}
          </tbody>
          </table>
        )}
      </div>

      <div className="funds-distribution" aria-label="Сумма к распределению">
        {fundsLoading ? (
          <LoadingSkeleton className="loading-skeleton--compact funds-distribution-skeleton" label="Загружаем сумму к распределению" rows={1} columns={2} />
        ) : (
          <>
            <span>Сумма к распределению</span>
            <strong>{availableToDistribute === null || availableToDistribute === 0 ? '—' : `${formatMoney(availableToDistribute)} руб.`}</strong>
          </>
        )}
      </div>

      {operationMessage ? <p className="form-success" role="status">{operationMessage}</p> : null}

      <div className="funds-sheet funds-operations-sheet">
        {operationsLoading ? (
          <TableLoadingState label="Загружаем операции фондов" />
        ) : (
          <>
            <table className="funds-table funds-operations-table" aria-label="Операции фондов">
          <thead>
            <tr>
              <th scope="col">Дата</th>
              <th scope="col">Фонд</th>
              <th scope="col">Операция</th>
              <th scope="col">Сумма</th>
              <th scope="col">Остаток</th>
              <th scope="col">Статус</th>
              <th scope="col">Действие</th>
            </tr>
          </thead>
          <tbody>
            {operationPage.items.length > 0 ? operationPage.items.map((fundOperation) => (
              <tr key={fundOperation.id}>
                <td>{formatDateTime(fundOperation.createdAtUtc)}</td>
                <td>{fundOperation.fundName}</td>
                <td>{fundOperation.operationKind === 'deposit' ? 'Пополнение' : 'Изъятие'}</td>
                <td>{formatMoney(fundOperation.amount)} руб.</td>
                <td>{formatMoney(fundOperation.balanceAfter)} руб.</td>
                <td>
                  <span className={fundOperation.isCanceled ? 'dictionary-status-pill dictionary-status-pill-archived' : 'dictionary-status-pill dictionary-status-pill-active'}>
                    {fundOperation.isCanceled ? 'Отменена' : 'Активна'}
                  </span>
                </td>
                <td>
                  <div className="funds-operation-actions">
                    {fundOperation.isCanceled ? (
                      <button className="funds-action-button" type="button" aria-label={`Вернуть операцию фонда ${fundOperation.fundName}`} title={`Вернуть операцию фонда ${fundOperation.fundName}`} data-tooltip="Вернуть" onClick={() => openFundStatusAction('restore', fundOperation)}>
                        <RotateCcw size={16} aria-hidden="true" />
                      </button>
                    ) : (
                      <>
                        <button className="funds-action-button" type="button" aria-label={`Изменить операцию фонда ${fundOperation.fundName}`} title={`Изменить операцию фонда ${fundOperation.fundName}`} data-tooltip="Изменить" onClick={() => openFundOperationEdit(fundOperation)}>
                          <Pencil size={16} aria-hidden="true" />
                        </button>
                        <button className="funds-action-button" type="button" aria-label={`Создать обратную операцию фонда ${fundOperation.fundName}`} title={`Создать обратную операцию фонда ${fundOperation.fundName}`} data-tooltip="Обратная" onClick={() => openFundOperationReverse(fundOperation)}>
                          <RefreshCw size={16} aria-hidden="true" />
                        </button>
                        <button className="funds-action-button funds-action-button--withdraw" type="button" aria-label={`Отменить операцию фонда ${fundOperation.fundName}`} title={`Отменить операцию фонда ${fundOperation.fundName}`} data-tooltip="Отменить" onClick={() => openFundStatusAction('cancel', fundOperation)}>
                          <Trash2 size={16} aria-hidden="true" />
                        </button>
                      </>
                    )}
                  </div>
                </td>
              </tr>
            )) : (
              <tr>
                <td colSpan={7}>Операций фондов пока нет.</td>
              </tr>
            )}
          </tbody>
            </table>
            <TablePagination
              ariaLabel="Пагинация операций фондов"
              totalCount={operationPage.totalCount}
              offset={operationPage.offset}
              limit={operationPage.limit}
              visibleCount={operationPage.items.length}
              disabled={operationsLoading}
              pageSizeLabel="Количество операций фондов"
              onPageChange={(pageNumber) => void changeOperationsPage(pageNumber)}
              onPageSizeChange={(limit) => void changeOperationsPage(1, limit)}
            />
          </>
        )}
      </div>

      {operation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeFundOperation}>
          <section ref={operationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="fund-operation-title" aria-describedby="fund-operation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Операция фонда</p>
                <h3 id="fund-operation-title">{operationActionLabel}</h3>
                <p>{operation.fundName}</p>
              </div>
              <button className="icon-button" type="button" onClick={closeFundOperation} aria-label="Закрыть операцию фонда">
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fund-operation-description">Проверьте сумму и укажите причину. Операция будет сохранена и записана в историю изменений.</p>
            <form className="dictionary-modal-form" onSubmit={submitFundOperation}>
              <FormField label="Сумма">
                <input
                  aria-label="Сумма операции фонда"
                  inputMode="decimal"
                  value={operation.amount}
                  onChange={(event) => {
                    setOperation({ ...operation, amount: event.target.value })
                    if (operationError) {
                      setOperationError(null)
                    }
                  }}
                  placeholder="0,00"
                />
              </FormField>
              {operation.kind === 'deposit' && availableToDistribute !== null ? (
                <p className="form-hint">Доступно к пополнению: {formatMoney(availableToDistribute)} руб.</p>
              ) : null}
              <FormField label="Причина">
                <textarea
                  aria-label="Причина операции фонда"
                  rows={3}
                  maxLength={1000}
                  value={operation.reason}
                  onChange={(event) => {
                    setOperation({ ...operation, reason: event.target.value })
                    if (operationError) {
                      setOperationError(null)
                    }
                  }}
                  placeholder="Например: распределение средств по решению правления"
                />
              </FormField>
              <dl className="fund-operation-preview">
                <div>
                  <dt>Действие</dt>
                  <dd>{operation.kind === 'deposit' ? 'Пополнение' : 'Изъятие'}</dd>
                </div>
                <div>
                  <dt>Сумма</dt>
                  <dd>{operationAmount === null ? '—' : `${formatMoney(operationAmount)} руб.`}</dd>
                </div>
              </dl>
              {operationError ? <p className="form-error" role="alert">{operationError}</p> : null}
              <div className="detail-dialog-actions">
                <button ref={operationCancelRef} className="ghost-button" type="button" onClick={closeFundOperation} disabled={savingOperation}>Отмена</button>
                <button className="secondary-button create-action-button" type="submit" disabled={savingOperation}>
                  <Save size={16} />
                  <span>{savingOperation ? 'Сохраняем...' : 'Подтвердить операцию'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
      {operationEdit ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!savingOperation && !operationEditConfirmation) {
            closeFundOperationEdit()
          }
        }}>
          <section ref={operationEditDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="fund-operation-edit-title" aria-describedby="fund-operation-edit-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Изменение операции</p>
                <h3 id="fund-operation-edit-title">Изменить операцию фонда?</h3>
                <p>{operationEdit.operation.fundName} · {operationEdit.operation.operationKind === 'deposit' ? 'Пополнение' : 'Изъятие'}</p>
              </div>
              <button className="icon-button" type="button" onClick={closeFundOperationEdit} aria-label="Закрыть изменение операции фонда" disabled={savingOperation}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fund-operation-edit-description">Проверьте сумму и основание. Изменение пересчитает остаток фонда и попадет в историю изменений.</p>
            <form className="dictionary-modal-form" onSubmit={submitFundOperationEdit}>
              <FormField label="Сумма">
                <input
                  aria-label="Новая сумма операции фонда"
                  inputMode="decimal"
                  value={operationEdit.amount}
                  onChange={(event) => {
                    setOperationEdit({ ...operationEdit, amount: event.target.value })
                    if (operationError) {
                      setOperationError(null)
                    }
                  }}
                  disabled={savingOperation}
                />
              </FormField>
              <FormField label="Основание">
                <textarea
                  aria-label="Новое основание операции фонда"
                  rows={3}
                  maxLength={1000}
                  value={operationEdit.reason}
                  onChange={(event) => {
                    setOperationEdit({ ...operationEdit, reason: event.target.value })
                    if (operationError) {
                      setOperationError(null)
                    }
                  }}
                  disabled={savingOperation}
                />
              </FormField>
              <dl className="fund-operation-preview">
                <div>
                  <dt>Было</dt>
                  <dd>{formatMoney(operationEdit.operation.amount)} руб.</dd>
                </div>
                <div>
                  <dt>Стало</dt>
                  <dd>{operationEditAmount === null ? '—' : `${formatMoney(operationEditAmount)} руб.`}</dd>
                </div>
              </dl>
              {operationError ? <p className="form-error" role="alert">{operationError}</p> : null}
              <div className="detail-dialog-actions">
                <button ref={operationEditCancelRef} className="ghost-button" type="button" onClick={closeFundOperationEdit} disabled={savingOperation}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={savingOperation}>
                  <Save size={16} aria-hidden="true" />
                  <span>{savingOperation ? 'Сохраняем...' : 'Сохранить изменения'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
      {operationEditConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!savingOperation) {
            setOperationEditConfirmation(null)
          }
        }}>
          <section ref={operationEditConfirmationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="fund-operation-edit-confirmation-title" aria-describedby="fund-operation-edit-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Проверка изменения</p>
                <h3 id="fund-operation-edit-confirmation-title">Подтвердить изменение операции фонда?</h3>
                <p>{operationEditConfirmation.operation.fundName} · {operationEditConfirmation.operation.operationKind === 'deposit' ? 'Пополнение' : 'Изъятие'}</p>
              </div>
              <button className="icon-button" type="button" onClick={() => setOperationEditConfirmation(null)} aria-label="Закрыть подтверждение операции фонда" disabled={savingOperation}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fund-operation-edit-confirmation-description">Проверьте изменения перед сохранением. После подтверждения backend пересчитает остаток фонда и запишет корректировку в историю изменений.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля операции фонда">
              {operationEditConfirmation.changes.map((change) => (
                <li key={change.field}>
                  <span className="dictionary-change-field">{change.field}</span>
                  <span className="dictionary-change-values">
                    <span className="dictionary-change-value">{change.before}</span>
                    <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                    <span className="dictionary-change-value dictionary-change-value-after">{change.after}</span>
                  </span>
                </li>
              ))}
            </ul>
            {operationError ? <p className="form-error" role="alert">{operationError}</p> : null}
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={operationEditConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setOperationEditConfirmation(null)} disabled={savingOperation}>Отмена</button>
              <button className="secondary-button" type="button" onClick={confirmFundOperationEdit} disabled={savingOperation}>
                <Save size={16} aria-hidden="true" />
                <span>{savingOperation ? 'Сохраняем...' : 'Сохранить'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
      {operationReverse ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!savingOperation) {
            closeFundOperationReverse()
          }
        }}>
          <section ref={operationReverseDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="fund-operation-reverse-title" aria-describedby="fund-operation-reverse-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Обратная операция</p>
                <h3 id="fund-operation-reverse-title">Создать обратную операцию фонда?</h3>
                <p>{operationReverse.operation.fundName} · {operationReverse.operation.operationKind === 'deposit' ? 'Пополнение' : 'Изъятие'} {'->'} {operationReverseLabel}</p>
              </div>
              <button className="icon-button" type="button" onClick={closeFundOperationReverse} aria-label="Закрыть обратную операцию фонда" disabled={savingOperation}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fund-operation-reverse-description">Исходная операция останется в истории, а система создаст новую противоположную операцию на ту же сумму и запишет причину в историю изменений.</p>
            <form className="dictionary-modal-form" onSubmit={submitFundOperationReverse}>
              <FormField label="Причина">
                <textarea
                  ref={operationReverseReasonRef}
                  aria-label="Причина обратной операции фонда"
                  rows={3}
                  maxLength={1000}
                  value={operationReverse.reason}
                  onChange={(event) => {
                    setOperationReverse({ ...operationReverse, reason: event.target.value })
                    if (operationError) {
                      setOperationError(null)
                    }
                  }}
                  placeholder="Например: сторнирование ошибочной операции"
                  disabled={savingOperation}
                />
              </FormField>
              <dl className="fund-operation-preview">
                <div>
                  <dt>Будет создано</dt>
                  <dd>{operationReverseLabel}</dd>
                </div>
                <div>
                  <dt>Сумма</dt>
                  <dd>{formatMoney(operationReverse.operation.amount)} руб.</dd>
                </div>
              </dl>
              {operationError ? <p className="form-error" role="alert">{operationError}</p> : null}
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeFundOperationReverse} disabled={savingOperation}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={savingOperation}>
                  <RefreshCw size={16} aria-hidden="true" />
                  <span>{savingOperation ? 'Сохраняем...' : 'Создать обратную операцию'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
      {statusAction ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!savingStatusAction) {
            closeFundStatusAction()
          }
        }}>
          <section ref={statusDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="fund-status-title" aria-describedby="fund-status-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">{statusAction.action === 'cancel' ? 'Отмена операции' : 'Восстановление операции'}</p>
                <h3 id="fund-status-title">{statusActionTitle}</h3>
                <p>{statusAction.operation.fundName} · {statusActionLabel} · {formatMoney(statusAction.operation.amount)} руб.</p>
              </div>
              <button className="icon-button" type="button" onClick={closeFundStatusAction} aria-label="Закрыть подтверждение операции фонда" disabled={savingStatusAction}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fund-status-description">
              {statusAction.action === 'cancel'
                ? 'Укажите причину отмены. Операция будет скрыта из активных расчетов, а действие попадет в историю изменений.'
                : 'Операция снова попадет в активные расчеты фонда, а действие будет записано в историю изменений.'}
            </p>
            <form className="dictionary-modal-form" onSubmit={submitFundStatusAction}>
              {statusAction.action === 'cancel' ? (
                <FormField label="Причина отмены">
                  <textarea
                    ref={statusReasonRef}
                    aria-label="Причина отмены операции фонда"
                    rows={3}
                    maxLength={1000}
                    value={statusAction.reason}
                    onChange={(event) => {
                      setStatusAction({ ...statusAction, reason: event.target.value })
                      if (operationError) {
                        setOperationError(null)
                      }
                    }}
                    placeholder="Например: ошибочное распределение средств"
                    disabled={savingStatusAction}
                  />
                </FormField>
              ) : null}
              {operationError ? <p className="form-error" role="alert">{operationError}</p> : null}
              <div className="detail-dialog-actions">
                <button ref={statusCancelRef} className="ghost-button" type="button" onClick={closeFundStatusAction} disabled={savingStatusAction}>Отмена</button>
                <button className={statusAction.action === 'cancel' ? 'secondary-button danger-button' : 'secondary-button'} type="submit" disabled={savingStatusAction}>
                  {statusAction.action === 'cancel' ? <Trash2 size={16} aria-hidden="true" /> : <RotateCcw size={16} aria-hidden="true" />}
                  <span>{savingStatusAction ? 'Сохраняем...' : statusAction.action === 'cancel' ? 'Отменить операцию' : 'Вернуть операцию'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
    </section>
  )
}
