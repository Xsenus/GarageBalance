import { useEffect, useState } from 'react'
import type { DictionaryClient } from '../../services/dictionariesApi'
import { AsyncErrorState, LoadingSkeleton } from '../../shared/AsyncState'
import { FormField } from '../../shared/FormField'
import { getGarageMeterStartFieldKey, toGarageMeterStartRows } from './garageMeterStartValues'
import type { GarageMeterStartRow } from './garageMeterStartValues'

type LoadState = { key: string; status: 'ready' | 'error' }

export function GarageMeterStartFields({ accessToken, dictionaryClient, garageId, rows, errors, disabled, onLoaded, onChange, onFieldEdited }: {
  accessToken: string
  dictionaryClient: DictionaryClient
  garageId: string
  rows: GarageMeterStartRow[] | undefined
  errors: Record<string, string | undefined>
  disabled: boolean
  onLoaded: (rows: GarageMeterStartRow[]) => void
  onChange: (meterKind: string, value: string) => void
  onFieldEdited: (fieldKey: string) => void
}) {
  const [retry, setRetry] = useState(0)
  const [loadState, setLoadState] = useState<LoadState | null>(null)
  const requestKey = `${accessToken}|${garageId}|${retry}`

  useEffect(() => {
    const controller = new AbortController()
    dictionaryClient.getGarageMeterStartValues?.(accessToken, garageId, controller.signal)
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
  }, [accessToken, dictionaryClient, garageId, requestKey])

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
        return (
          <FormField
            key={row.meterKind}
            label={`Старт. зн. сч.: ${row.label}`}
            help={row.hasReadings
              ? 'По счётчику уже есть показания: при изменении расход и неоплаченные начисления будут пересчитаны.'
              : row.unitName ? `Показание счётчика на начало учёта, ${row.unitName}.` : 'Показание счётчика на начало учёта.'}
          >
            <input
              aria-label={`Стартовое значение счётчика: ${row.label}`}
              aria-invalid={Boolean(errors[fieldKey])}
              data-garage-field={fieldKey}
              disabled={disabled}
              value={row.value}
              onChange={(event) => {
                onFieldEdited(fieldKey)
                onChange(row.meterKind, event.target.value)
              }}
            />
          </FormField>
        )
      })}
    </div>
  )
}
