import { useEffect, useState } from 'react'
import type { FundOptionDto, FundsClient } from '../../services/fundsApi'

export function useExpenseFundOptions(client: Pick<FundsClient, 'getFundOptions'>, accessToken: string, enabled: boolean) {
  const [revision, setRevision] = useState(0)
  const [state, setState] = useState<{ options: FundOptionDto[]; loading: boolean; error: string | null }>({ options: [], loading: true, error: null })
  useEffect(() => {
    if (!enabled) return
    const controller = new AbortController()
    async function load() {
      await Promise.resolve()
      if (controller.signal.aborted) return
      setState((previous) => ({ ...previous, loading: true, error: null }))
      try {
        const options = await client.getFundOptions(accessToken, controller.signal)
        if (!controller.signal.aborted) setState({ options: options.filter((fund) => fund.allowOperations).sort((a, b) => a.name.localeCompare(b.name, 'ru-RU')), loading: false, error: null })
      } catch (error) {
        if (!controller.signal.aborted) setState((previous) => ({ ...previous, loading: false, error: error instanceof Error ? error.message : 'Не удалось загрузить фонды расходования.' }))
      }
    }
    void load()
    return () => controller.abort()
  }, [accessToken, client, enabled, revision])
  return { ...state, reload: () => setRevision((value) => value + 1) }
}
