import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { intentPreloadDelayMs, useIntentPreload } from './useIntentPreload'

function IntentProbe({ preload }: { preload: (section: string) => void }) {
  const intent = useIntentPreload(preload)
  return (
    <button
      type="button"
      onPointerEnter={() => intent.schedule('payments')}
      onPointerLeave={intent.cancel}
      onFocus={() => intent.runNow('payments')}
    >
      Платежи
    </button>
  )
}

describe('useIntentPreload', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('preloads only after sustained pointer intent', () => {
    vi.useFakeTimers()
    const preload = vi.fn()
    render(<IntentProbe preload={preload} />)

    fireEvent.pointerEnter(screen.getByRole('button', { name: 'Платежи' }))
    act(() => vi.advanceTimersByTime(intentPreloadDelayMs - 1))
    expect(preload).not.toHaveBeenCalled()
    act(() => vi.advanceTimersByTime(1))
    expect(preload).toHaveBeenCalledOnce()
  })

  it('cancels incidental pointer traversal and pending work on unmount', () => {
    vi.useFakeTimers()
    const preload = vi.fn()
    const view = render(<IntentProbe preload={preload} />)
    const button = screen.getByRole('button', { name: 'Платежи' })

    fireEvent.pointerEnter(button)
    fireEvent.pointerLeave(button)
    act(() => vi.runAllTimers())
    expect(preload).not.toHaveBeenCalled()

    fireEvent.pointerEnter(button)
    view.unmount()
    act(() => vi.runAllTimers())
    expect(preload).not.toHaveBeenCalled()
  })

  it('preloads keyboard focus immediately and cancels a pending hover', () => {
    vi.useFakeTimers()
    const preload = vi.fn()
    render(<IntentProbe preload={preload} />)
    const button = screen.getByRole('button', { name: 'Платежи' })

    fireEvent.pointerEnter(button)
    fireEvent.focus(button)
    expect(preload).toHaveBeenCalledOnce()
    act(() => vi.runAllTimers())
    expect(preload).toHaveBeenCalledOnce()
  })
})
