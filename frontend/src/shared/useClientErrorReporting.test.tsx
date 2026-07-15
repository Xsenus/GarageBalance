import { render, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useClientErrorReporting } from './useClientErrorReporting'

function Reporter({ accessToken }: { accessToken: string | null }) {
  useClientErrorReporting(accessToken)
  return null
}

describe('useClientErrorReporting', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('reports window and promise errors only during an authenticated session', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }))
    vi.stubGlobal('fetch', fetchMock)
    const removeListener = vi.spyOn(window, 'removeEventListener')
    const view = render(<Reporter accessToken="token" />)

    window.dispatchEvent(new ErrorEvent('error', { error: new TypeError('render failed'), message: 'render failed' }))
    const rejection = new Event('unhandledrejection') as PromiseRejectionEvent
    Object.defineProperty(rejection, 'reason', { value: new Error('async failed') })
    window.dispatchEvent(rejection)

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    expect(fetchMock.mock.calls.map((call) => JSON.parse(String(call[1]?.body)).message)).toEqual([
      'Ошибка интерфейса; подробности определяются по коду и стеку вызовов.',
      'Ошибка интерфейса; подробности определяются по коду и стеку вызовов.',
    ])

    view.unmount()
    expect(removeListener).toHaveBeenCalledWith('error', expect.any(Function))
    expect(removeListener).toHaveBeenCalledWith('unhandledrejection', expect.any(Function))
  })

  it('does not register reporting without an access token', () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const addListener = vi.spyOn(window, 'addEventListener')
    render(<Reporter accessToken={null} />)

    expect(fetchMock).not.toHaveBeenCalled()
    expect(addListener).not.toHaveBeenCalledWith('error', expect.any(Function))
    expect(addListener).not.toHaveBeenCalledWith('unhandledrejection', expect.any(Function))
  })
})
