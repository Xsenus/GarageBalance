import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ToastViewport } from './Toast'
import { useToast } from './useToast'

function ToastHarness() {
  const { toast, showToast, dismissToast } = useToast(3200)
  return (
    <>
      <button type="button" onClick={() => showToast('Операция сохранена.')}>Показать</button>
      <ToastViewport toast={toast} onDismiss={dismissToast} />
    </>
  )
}

describe('ToastViewport', () => {
  afterEach(() => vi.useRealTimers())

  it('announces and closes a success notification', async () => {
    const user = userEvent.setup()
    render(<ToastHarness />)

    await user.click(screen.getByRole('button', { name: 'Показать' }))
    expect(screen.getByRole('status')).toHaveTextContent('ГотовоОперация сохранена.')
    await user.click(screen.getByRole('button', { name: 'Закрыть уведомление' }))
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('automatically dismisses a notification', async () => {
    vi.useFakeTimers()
    render(<ToastHarness />)
    act(() => screen.getByRole('button', { name: 'Показать' }).click())
    expect(screen.getByRole('status')).toBeInTheDocument()

    await act(async () => vi.advanceTimersByTime(3200))
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('uses an assertive alert for an error', () => {
    render(<ToastViewport toast={{ id: 1, text: 'Не удалось сохранить.', kind: 'error' }} onDismiss={() => undefined} />)
    expect(screen.getByRole('alert')).toHaveAttribute('aria-live', 'assertive')
  })
})
