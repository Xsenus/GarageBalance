import { createContext, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import type { ApplicationSettingsClient } from '../services/settingsApi'

type ActionCommentSettingsContextValue = readonly [
  required: boolean,
  loading: boolean,
  error: string | null,
  saveRequired: (required: boolean) => Promise<void>,
]

const ActionCommentSettingsContext = createContext<ActionCommentSettingsContextValue>([true, false, null, async () => undefined])

export function ActionCommentSettingsProvider({ accessToken, client, children }: {
  accessToken: string
  client: ApplicationSettingsClient
  children: ReactNode
}) {
  const [state, setState] = useState({
    required: true,
    version: '',
    loading: true,
    error: null as string | null,
  })

  useEffect(() => {
    const controller = new AbortController()
    client.getActionCommentSettings(accessToken, controller.signal)
      .then((settings) => {
        if (!controller.signal.aborted) {
          setState({ ...settings, loading: false, error: null })
        }
      })
      .catch((caught: unknown) => {
        if (!controller.signal.aborted) {
          setState((current) => ({
            ...current,
            loading: false,
            error: caught instanceof Error ? caught.message : 'Настройка комментариев недоступна.',
          }))
        }
      })
    return () => controller.abort()
  }, [accessToken, client])

  async function saveRequired(nextRequired: boolean) {
    setState((current) => ({ ...current, loading: true, error: null }))
    try {
      const settings = await client.updateActionCommentSettings(accessToken, { required: nextRequired, version: state.version })
      setState({ ...settings, loading: false, error: null })
    } catch (caught) {
      setState((current) => ({
        ...current,
        loading: false,
        error: caught instanceof Error ? caught.message : 'Настройка комментариев недоступна.',
      }))
      throw caught
    }
  }

  return <ActionCommentSettingsContext.Provider value={[state.required, state.loading, state.error, saveRequired]}>{children}</ActionCommentSettingsContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useActionCommentSettings() {
  return useContext(ActionCommentSettingsContext)
}
