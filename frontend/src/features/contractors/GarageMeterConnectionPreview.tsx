import { useEffect, useState } from 'react'
import type { FinanceClient, MeterConnectionPreviewDto } from '../../services/financeApi'
import type { UpsertGarageMeterStartValueRequest } from '../../services/dictionariesApi'
import { AsyncErrorState, LoadingSkeleton } from '../../shared/AsyncState'
import { TablePagination } from '../../shared/TablePagination'
import { formatMoney, formatMonth } from '../../shared/formatters'

export function GarageMeterConnectionPreview({ accessToken, financeClient, garageId, garageVersion, values, onReady }: {
  accessToken: string; financeClient: FinanceClient; garageId: string; garageVersion: string; values: UpsertGarageMeterStartValueRequest[]; onReady: (ready: boolean) => void
}) {
  const [result, setResult] = useState<MeterConnectionPreviewDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)
  const [offset, setOffset] = useState(0)
  const [limit, setLimit] = useState(10)
  const [loadedKey, setLoadedKey] = useState('')
  const valuesKey = JSON.stringify(values)
  const requestKey = `${accessToken}|${garageId}|${garageVersion}|${valuesKey}|${retry}`
  useEffect(() => {
    const controller = new AbortController()
    onReady(false)
    financeClient.previewMeterConnections!(accessToken, { garageId, garageVersion, values: JSON.parse(valuesKey) as UpsertGarageMeterStartValueRequest[] }, controller.signal)
      .then((preview) => {
        if (controller.signal.aborted) return
        setResult(preview)
        setError(null)
        setLoadedKey(requestKey)
        onReady(true)
      }).catch((caught) => {
        if (controller.signal.aborted) return
        setError(caught instanceof Error ? caught.message : 'Не удалось проверить изменение учёта.')
        setLoadedKey(requestKey)
      })
    return () => controller.abort()
    // onReady only stores readiness in the parent confirmation dialog.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [accessToken, financeClient, garageId, garageVersion, requestKey, valuesKey])

  if (loadedKey !== requestKey) return <LoadingSkeleton label="Проверяем изменение учёта и начислений" rows={2} />
  if (error) return <AsyncErrorState message={error} onRetry={() => setRetry((value) => value + 1)} />
  if (!result?.accrualChanges.length) return <p className="hint" role="status">Начисления не изменятся.</p>
  return <div>
    <div className="meter-connection-table" role="table" aria-label="Изменения начислений услуги">
      <div role="row"><span role="columnheader">Месяц</span><span role="columnheader">Было → станет</span></div>
      {result.accrualChanges.slice(offset, offset + limit).map((row) => <div role="row" key={row.accrualId}>
        <span role="cell">{formatMonth(row.accountingMonth)}</span>
        <span role="cell">{row.oldRequiresMeterReading ? 'Ожидается показание' : formatMoney(row.oldAmount)} → {row.newRequiresMeterReading ? 'Ожидается показание' : formatMoney(row.newAmount)}</span>
      </div>)}
    </div>
    <TablePagination ariaLabel="Пагинация предпросмотра начислений" totalCount={result.accrualChanges.length} offset={offset} limit={limit} visibleCount={result.accrualChanges.slice(offset, offset + limit).length} onPageChange={(value) => setOffset((value - 1) * limit)} onPageSizeChange={(value) => { setLimit(value); setOffset(0) }} />
  </div>
}
