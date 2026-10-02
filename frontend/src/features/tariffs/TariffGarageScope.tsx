import { useEffect, useRef, useState } from 'react'
import type { DictionaryClient, GarageDto } from '../../services/dictionariesApi'
import { AsyncErrorState, EmptyState, TableLoadingState } from '../../shared/AsyncState'
import { FieldHelp, FormField } from '../../shared/FormField'
import './garageTariffAssignments.css'

export function TariffGarageScope({ accessToken, dictionaryClient, restricted, garageIds, disabled, onRestrictedChange, onSelectionChange }: {
  accessToken?: string
  dictionaryClient?: DictionaryClient
  restricted: boolean
  garageIds: string[]
  disabled: boolean
  onRestrictedChange: (value: boolean) => void
  onSelectionChange: (ids: string[]) => void
}) {
  const [search, setSearch] = useState('')
  const [offset, setOffset] = useState(0)
  const limit = 48
  const [page, setPage] = useState<{ items: GarageDto[]; totalCount: number } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [completedRequestKey, setCompletedRequestKey] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)
  const [selectingAll, setSelectingAll] = useState(false)
  const [selectedReferences, setSelectedReferences] = useState<{ token?: string; items: GarageDto[] }>({ items: [] })
  const selectionRequest = useRef<AbortController | null>(null)
  useEffect(() => () => { selectionRequest.current?.abort() }, [accessToken, dictionaryClient])
  useEffect(() => {
    if (!restricted || disabled) {
      selectionRequest.current?.abort()
    }
  }, [restricted, disabled])
  const requestKey = JSON.stringify([accessToken, search, offset, limit, retry])
  const loading = restricted && (pending || completedRequestKey !== requestKey)
  useEffect(() => {
    if (!restricted) return
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      setPending(true)
      setError(null)
      if (!accessToken || !dictionaryClient?.getGaragesPage) {
        setError('Не удалось загрузить гаражи. Закройте и снова откройте карточку тарифа.')
        setPending(false)
        setCompletedRequestKey(requestKey)
        return
      }
      dictionaryClient.getGaragesPage(accessToken, search.trim() || undefined, offset, limit, false, 'number', 'asc', false, {}, controller.signal)
        .then((result) => { if (!controller.signal.aborted) setPage((previous) => ({ ...result, items: offset === 0 ? result.items : [...(previous?.items ?? []), ...result.items.filter((garage) => !previous?.items.some((item) => item.id === garage.id))] })) })
        .catch(() => { if (!controller.signal.aborted) setError('Не удалось загрузить гаражи. Повторите попытку.') })
        .finally(() => { if (!controller.signal.aborted) { setPending(false); setCompletedRequestKey(requestKey) } })
    }, 250)
    return () => { controller.abort(); window.clearTimeout(timer) }
  }, [accessToken, dictionaryClient, restricted, search, offset, limit, retry, requestKey])

  // A persisted selection can lie beyond the first page (for example garage 165).
  // Resolve its labels in bounded pages, then include it at the start of the same
  // visible page instead of forcing the user to scroll to find selected garages.
  useEffect(() => {
    if (!restricted || search.trim() || loading || error || !page || !accessToken || !dictionaryClient?.getGaragesPage) return
    const known = new Set([...page.items, ...(selectedReferences.token === accessToken ? selectedReferences.items : [])].map((item) => item.id))
    const missing = new Set(garageIds.filter((id) => !known.has(id)))
    if (!missing.size) return
    const controller = new AbortController()
    async function resolveSelection() {
      let nextOffset = page!.items.length
      const found: GarageDto[] = []
      try {
        while (missing.size && !controller.signal.aborted) {
          const result = await dictionaryClient!.getGaragesPage!(accessToken!, undefined, nextOffset, 100, false, 'number', 'asc', false, {}, controller.signal)
          if (controller.signal.aborted) return
          for (const garage of result.items) if (missing.delete(garage.id)) found.push(garage)
          nextOffset += result.items.length
          if (nextOffset >= result.totalCount || result.items.length === 0) break
        }
        if (found.length && !controller.signal.aborted) setSelectedReferences((previous) => ({ token: accessToken,
          items: [...(previous.token === accessToken ? previous.items : []), ...found] }))
      } catch {
        if (!controller.signal.aborted) setError('Не удалось загрузить выбранные гаражи. Повторите попытку.')
      }
    }
    void resolveSelection()
    return () => controller.abort()
  }, [accessToken, dictionaryClient, restricted, search, loading, error, page, garageIds, selectedReferences])

  async function selectAll() {
    if (disabled || selectionRequest.current || !accessToken || !dictionaryClient?.getGaragesPage) return
    const controller = new AbortController()
    selectionRequest.current = controller
    setSelectingAll(true)
    setError(null)
    try {
      const ids = new Set<string>()
      let nextOffset = 0
      while (true) {
        const result = await dictionaryClient.getGaragesPage(accessToken, undefined, nextOffset, 100, false, 'number', 'asc', false, {}, controller.signal)
        if (controller.signal.aborted) return
        for (const garage of result.items) ids.add(garage.id)
        nextOffset += result.items.length
        if (nextOffset >= result.totalCount) break
        if (result.items.length === 0) throw new Error('Incomplete garage page')
      }
      onSelectionChange([...ids])
    } catch {
      if (!controller.signal.aborted) setError('Не удалось выбрать все гаражи. Повторите попытку; прежний выбор сохранён.')
    } finally {
      if (selectionRequest.current === controller) {
        selectionRequest.current = null
        setSelectingAll(false)
      }
    }
  }

  const visibleReferences = !search.trim() && selectedReferences.token === accessToken
    ? selectedReferences.items.filter((garage) => garageIds.includes(garage.id) && !page?.items.some((item) => item.id === garage.id)) : []
  const orderedGarages = [...(page?.items ?? []), ...visibleReferences].sort((left, right) =>
    Number(garageIds.includes(right.id)) - Number(garageIds.includes(left.id))
    || left.number.localeCompare(right.number, 'ru', { numeric: true })).slice(0, page?.items.length ?? 0)

  return <div className="tariff-garage-scope">
    <div className="form-field">
      <span className="field-label-with-help"><span>Область действия тарифа</span><FieldHelp label="Область действия тарифа">Без ограничения тариф действует для всех гаражей. При включённом ограничении начисления по этому тарифу создаются только для выбранных гаражей. Уже созданные начисления не удаляются автоматически.</FieldHelp></span>
      <label className="contractors-service-regular-toggle">
        <input type="checkbox" checked={restricted} disabled={disabled || selectingAll} onChange={(event) => onRestrictedChange(event.target.checked)} />
        Только для выбранных гаражей
      </label>
    </div>
    {!restricted ? <p role="status">Тариф действует для всех гаражей.</p> : <>
      <div className="tariff-garage-scope-toolbar">
        <FormField label="Поиск гаража"><input value={search} disabled={disabled} onChange={(event) => { setSearch(event.target.value); setOffset(0); setPage(null) }} /></FormField>
        <span role="status" aria-live="polite">{selectingAll ? 'Выбираем все гаражи…' : `Выбрано: ${garageIds.length}`}</span>
        <button className="ghost-button" type="button" disabled={disabled || selectingAll || !accessToken || !dictionaryClient?.getGaragesPage} onClick={() => { void selectAll() }}>Выделить всё</button>
        <button className="ghost-button" type="button" disabled={disabled || garageIds.length === 0 && !selectingAll} onClick={() => {
          selectionRequest.current?.abort()
          selectionRequest.current = null
          setSelectingAll(false)
          onSelectionChange([])
        }}>Снять всё</button>
      </div>
      <div className="tariff-garage-scope-grid-scroll" role="region" aria-label="Список гаражей тарифа" tabIndex={0} onScroll={(event) => {
        const element = event.currentTarget
        if (!loading && !error && page && page.items.length < page.totalCount && element.scrollHeight - element.scrollTop - element.clientHeight < 100) setOffset(offset + limit)
      }}>
      {page?.items.length ? <div className="tariff-garage-scope-grid">
            {orderedGarages.map((garage) => <label className={`tariff-garage-scope-choice${garageIds.includes(garage.id) ? ' is-selected' : ''}`} key={garage.id}><input type="checkbox" aria-label={`Выбрать гараж ${garage.number}`} checked={garageIds.includes(garage.id)}
              disabled={disabled || selectingAll} onChange={(event) => onSelectionChange(event.target.checked
                ? [...garageIds, garage.id] : garageIds.filter((id) => id !== garage.id))} /><span>Гараж {garage.number}</span></label>)}
          </div> : null}
      {loading ? <TableLoadingState label="Загрузка гаражей тарифа" rows={offset === 0 ? 4 : 1} columns={4} />
        : error ? <AsyncErrorState message={error} onRetry={() => setRetry((value) => value + 1)} />
          : !page?.items.length ? <EmptyState>Гаражи не найдены</EmptyState> : null}
      </div>
      {!loading && page?.items.length ? <div className="tariff-garage-scope-progress"><span role="status">Загружено: {page.items.length} из {page.totalCount}</span>{page.items.length < page.totalCount ? <button type="button" className="ghost-button" disabled={disabled || Boolean(error)} onClick={() => setOffset(offset + limit)}>Показать ещё гаражи</button> : null}</div> : null}
    </>}
  </div>
}
