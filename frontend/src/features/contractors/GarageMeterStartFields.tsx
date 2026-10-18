import { useEffect, useState } from 'react'
import type { GarageMeterStartValueDto } from '../../services/dictionariesApi'
import { AsyncErrorState, EmptyState, LoadingSkeleton } from '../../shared/AsyncState'
import { FormField } from '../../shared/FormField'
import { getGarageMeterBaselineMonth, getGarageMeterConnectionStatus, getGarageMeterStartFieldKey, requiresGarageMeterConnectionReason, toGarageMeterStartRows } from './garageMeterStartValues'
import type { GarageMeterStartRow } from './garageMeterStartValues'

type LoadState = { key: string; status: 'ready' | 'error' }

export function GarageMeterStartFields({ loadKey, load, rows, errors, disabled, accountingStartMonth, canManageConnections = true, onLoaded, onChange, onReasonChange, onFieldEdited }: {
  loadKey: string
  load: (signal: AbortSignal) => Promise<GarageMeterStartValueDto[]>
  rows: GarageMeterStartRow[] | undefined
  errors: Record<string, string | undefined>
  disabled: boolean
  accountingStartMonth?: string
  canManageConnections?: boolean
  onLoaded: (rows: GarageMeterStartRow[]) => void
  onChange: (meterKind: string, value: string) => void
  onReasonChange: (meterKind: string, value: string) => void
  onFieldEdited: (fieldKey: string) => void
}) {
  const [retry, setRetry] = useState(0)
  const [loadState, setLoadState] = useState<LoadState | null>(null)
  const requestKey = `${loadKey}|${retry}`

  useEffect(() => {
    const controller = new AbortController()
    load(controller.signal)
      .then((values) => {
        if (controller.signal.aborted) return
        onLoaded(toGarageMeterStartRows(values))
        setLoadState({ key: requestKey, status: 'ready' })
      })
      .catch(() => {
        if (!controller.signal.aborted) setLoadState({ key: requestKey, status: 'error' })
      })
    return () => controller.abort()
    // onLoaded only stores the rows into the parent form; it must not restart the request.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  if (loadState?.key !== requestKey) {
    return <LoadingSkeleton className="contractors-garage-meter-start" label="Загружаем стартовые значения счётчиков" rows={2} />
  }

  if (loadState.status === 'error') {
    return <AsyncErrorState className="contractors-garage-meter-start" message="Не удалось загрузить стартовые значения счётчиков." onRetry={() => setRetry((value) => value + 1)} />
  }

  return (
    <div className="contractors-garage-meter-start" role="group" aria-label="Стартовые значения счётчиков">
      {(rows ?? []).map((row) => {
        const fieldKey = getGarageMeterStartFieldKey(row.meterKind)
        const status = getGarageMeterConnectionStatus(row, accountingStartMonth)
        const baselineMonth = getGarageMeterBaselineMonth(accountingStartMonth)
        return (
          <div className="contractors-garage-meter-service" key={row.meterKind} role="group" aria-label={`Учёт услуги: ${row.label}`}>
          <FormField
            label={`Старт. зн. сч.: ${row.label}`}
            help={row.hasReadings
              ? 'По счётчику уже есть показания: при изменении расход и неоплаченные начисления будут пересчитаны.'
              : `Показание счётчика на конец ${baselineMonth ?? 'месяца перед началом учёта'} — месяца перед «Учитывать с»${row.unitName ? `, ${row.unitName}` : ''}. Расход первого месяца считается от этого значения.`}
          >
            <input
              aria-label={`Стартовое значение счётчика: ${row.label}`}
              aria-invalid={Boolean(errors[fieldKey])}
              data-garage-field={fieldKey}
              disabled={disabled || !canManageConnections && Boolean(row.initialConnectedOn || row.requiresConnectionDate)}
              value={row.value}
              onChange={(event) => {
                onFieldEdited(fieldKey)
                onChange(row.meterKind, event.target.value)
              }}
            />
          </FormField>
          {status ? <span className="hint contractors-garage-meter-status" role="status" aria-live="polite">{status}</span> : null}
          {row.requiresTariff ? <span className="form-error" role="status">Нет тарифа на весь первый месяц подключения: начисление не рассчитано.</span> : null}
          {requiresGarageMeterConnectionReason(row) ? (
            <FormField label="Причина изменения" help="Причина сохранится в истории исправления стартового показания.">
              <input aria-label={`Причина изменения учёта: ${row.label}`} data-garage-field={`meter:${row.meterKind}:reason`}
                aria-invalid={Boolean(errors[`meter:${row.meterKind}:reason`])} disabled={disabled || !canManageConnections} maxLength={1000} required value={row.reason}
                onChange={(event) => { onFieldEdited(`meter:${row.meterKind}:reason`); onReasonChange(row.meterKind, event.target.value) }} />
            </FormField>
          ) : null}
          </div>
        )
      })}
      {rows?.length === 0 ? <EmptyState>Нет применимых услуг со счётчиком.</EmptyState> : null}
    </div>
  )
}
