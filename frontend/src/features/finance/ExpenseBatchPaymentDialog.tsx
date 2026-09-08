import { useState } from 'react'
import { LoaderCircle, WalletCards, X } from 'lucide-react'
import { expenseBatchesApi } from '../../services/expenseBatchesApi'
import { useActionCommentSettings } from '../../shared/ActionCommentSettings'
import { EmptyState, LoadingSkeleton } from '../../shared/AsyncState'
import { FormField } from '../../shared/FormField'
import { FormError } from '../../shared/formFeedback'
import { useEscapeKey, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { formatMoney, formatMonth, getLocalDateInputValue } from '../../shared/formatters'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { TablePagination } from '../../shared/TablePagination'
import { useExpenseBatchPayment } from './useExpenseBatchPayment'

export default function ExpenseBatchPaymentDialog({ accessToken, open, accountingMonth, canPay, onClose, onPaid, client = expenseBatchesApi }: {
  accessToken: string
  open: boolean
  accountingMonth: string
  canPay: boolean
  onClose: () => void
  onPaid: () => void
  client?: typeof expenseBatchesApi
}) {
  const [operationDate, setOperationDate] = useState(getLocalDateInputValue)
  const [comment, setComment] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [offset, setOffset] = useState(0)
  const [limit, setLimit] = useState(10)
  const [required, settingsLoading, settingsError] = useActionCommentSettings()
  const flow = useExpenseBatchPayment({ accessToken, open, accountingMonth, operationDate, canPay, client })
  useRestoreFocusOnClose(open)
  const dialogRef = useFocusTrap<HTMLElement>(open)
  function close() {
    if (flow.saving) return
    if (!flow.uncertain) setConfirmed(false)
    onClose()
  }
  useEscapeKey(open && !flow.saving, close)
  const locked = flow.saving || flow.uncertain
  const rows = flow.preview?.items ?? []
  const safeOffset = Math.min(offset, Math.max(0, (Math.ceil(rows.length / limit) - 1) * limit))
  const visibleRows = rows.slice(safeOffset, safeOffset + limit)

  if (!open) return null
  return (
    <div className="modal-backdrop" role="presentation">
      <section ref={dialogRef} className="detail-dialog payments-prototype-calculation-dialog" role="dialog" aria-modal="true" aria-labelledby="expense-batch-title">
        <div className="detail-dialog-header">
          <h3 id="expense-batch-title">Оплатить все</h3>
          <button type="button" className="icon-button" aria-label="Закрыть оплату всех выплат" disabled={flow.saving} onClick={close}><X size={18} /></button>
        </div>
        {!canPay ? <FormError>Нет разрешения на проведение выплат.</FormError> : (
          <form className="dictionary-modal-form" noValidate onSubmit={async (event) => {
            event.preventDefault()
            if (settingsLoading || settingsError) return
            const paid = await flow.pay(comment, required, confirmed)
            if (paid) onPaid()
          }}>
            <p>Погашение задолженности за {formatMonth(flow.preview?.accountingMonth ?? accountingMonth)}. Поставщикам — из банка, сотрудникам — из кассы по месяцам задолженности.</p>
            <FormField label="Дата выплаты">
              <LocalizedDatePicker ariaLabel="Дата общей выплаты" mode="date" value={operationDate} required disabled={locked || !!flow.result} onChange={(value) => { setOperationDate(value); setConfirmed(false); setOffset(0) }} />
            </FormField>
            {flow.loading ? <LoadingSkeleton label="Рассчитываем все выплаты" rows={5} columns={4} /> : null}
            {flow.error ? <FormError>{flow.error}</FormError> : null}
            {settingsError ? <FormError>{settingsError}</FormError> : null}
            {flow.uncertain ? <FormError>Ответ о проведении не получен. Повторите проверку результата: сохранённый запрос не создаст выплаты повторно. Поля заблокированы до получения результата.</FormError> : null}
            {flow.result ? <p role="status" aria-live="polite">Выплаты проведены: {flow.result.operationIds.length}. Форма выплат обновляется.</p> : null}
            {flow.preview && !flow.result ? <>
              {rows.length === 0 ? <EmptyState>Задолженности для оплаты нет.</EmptyState> : <>
                <div className="dictionary-table-scroll">
                  <table className="dictionary-data-table" aria-label="Предварительный список выплат">
                    <thead><tr><th>Получатель</th><th>Услуга</th><th>Месяц</th><th>Источник</th><th>Сумма</th></tr></thead>
                    <tbody>{visibleRows.map(({ payment, recipientName, expenseTypeName }) => <tr key={`${payment.recipientKind}/${payment.recipientId}/${payment.expenseTypeId}/${payment.accountingMonth}`}>
                      <td>{recipientName}</td><td>{expenseTypeName}</td><td>{formatMonth(payment.accountingMonth)}</td><td>{payment.paymentSource === 'bank' ? 'Банк' : 'Касса'}</td><td>{formatMoney(payment.amount)}</td>
                    </tr>)}</tbody>
                  </table>
                </div>
                <TablePagination ariaLabel="Страницы предварительных выплат" totalCount={rows.length} offset={safeOffset} limit={limit} visibleCount={visibleRows.length} onPageChange={(page) => setOffset((page - 1) * limit)} onPageSizeChange={(value) => { setLimit(value); setOffset(0) }} />
                <div className="full-payment-fields" aria-label="Итоги общей выплаты">
                  <p>Из банка: <strong>{formatMoney(flow.preview.bankAmount)}</strong> · доступно {formatMoney(flow.preview.availableBankAmount)}</p>
                  <p>Из кассы: <strong>{formatMoney(flow.preview.cashAmount)}</strong> · доступно {formatMoney(flow.preview.availableCashAmount)}</p>
                  {flow.preview.funds.map((fund) => <p key={fund.fundId}>{fund.name}: {formatMoney(fund.amount)} · остаток после выплаты {formatMoney(fund.availableAmount - fund.amount)}</p>)}
                </div>
              </>}
              {flow.preview.issues.map((issue, index) => <FormError key={index}>{issue}</FormError>)}
              {flow.preview.requiresNegativeFundConfirmation ? <label className="checkbox-field"><input type="checkbox" checked={confirmed} disabled={locked} onChange={(event) => setConfirmed(event.target.checked)} />Подтверждаю выплату сверх остатка фонда</label> : null}
              <FormField label="Комментарий" help={required ? 'Обязателен для проведения выплат. От 3 до 1000 символов.' : 'Необязательно. Если указан — от 3 до 1000 символов.'}>
                <textarea aria-label="Комментарий к общей выплате" value={comment} maxLength={1000} disabled={locked} onChange={(event) => setComment(event.target.value)} />
              </FormField>
            </> : null}
            <div className="detail-dialog-actions">
              <button type="button" className="ghost-button" disabled={flow.saving} onClick={close}>{flow.result ? 'Закрыть' : 'Отмена'}</button>
              {!flow.result ? <button type="submit" className="secondary-button" disabled={flow.saving || flow.loading || settingsLoading || !!settingsError || (!flow.uncertain && !flow.preview?.canSubmit)} aria-busy={flow.saving}>
                {flow.saving ? <LoaderCircle size={17} className="financial-report-button__spinner" aria-hidden="true" /> : <WalletCards size={17} aria-hidden="true" />}
                <span>{flow.saving ? 'Проводим выплаты…' : flow.uncertain ? 'Проверить результат' : 'Подтвердить выплаты'}</span>
              </button> : null}
            </div>
            {flow.saving ? <span className="sr-only" role="status" aria-live="polite">Проводим все выплаты</span> : null}
          </form>
        )}
      </section>
    </div>
  )
}
