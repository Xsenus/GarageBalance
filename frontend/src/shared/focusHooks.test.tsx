import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from './focusHooks'

function EscapeProbe({ enabled, onEscape }: { enabled: boolean; onEscape: () => void }) {
  useEscapeKey(enabled, onEscape)
  return <button type="button">Probe</button>
}

function FocusOnOpenProbe({ enabled }: { enabled: boolean }) {
  const ref = useFocusOnOpen<HTMLButtonElement>(enabled)
  return <button ref={ref} type="button">Target</button>
}

function FocusTrapProbe({ enabled }: { enabled: boolean }) {
  const ref = useFocusTrap<HTMLDivElement>(enabled)
  return (
    <div ref={ref}>
      <button type="button">First</button>
      <button type="button">Last</button>
    </div>
  )
}

function RestoreFocusProbe({ open }: { open: boolean }) {
  useRestoreFocusOnClose(open)
  return open ? <button type="button">Dialog action</button> : null
}

describe('focus shared hooks', () => {
  it('calls escape handler only while enabled', async () => {
    const user = userEvent.setup()
    const calls: string[] = []
    const { rerender } = render(<EscapeProbe enabled={false} onEscape={() => calls.push('escape')} />)

    await user.keyboard('{Escape}')
    expect(calls).toEqual([])

    rerender(<EscapeProbe enabled={true} onEscape={() => calls.push('escape')} />)
    await user.keyboard('{Escape}')

    expect(calls).toEqual(['escape'])
  })

  it('focuses target when opened', () => {
    render(<FocusOnOpenProbe enabled={true} />)

    expect(screen.getByRole('button', { name: 'Target' })).toHaveFocus()
  })

  it('traps tab navigation inside the active container', async () => {
    const user = userEvent.setup()
    render(<FocusTrapProbe enabled={true} />)

    screen.getByRole('button', { name: 'Last' }).focus()
    await user.keyboard('{Tab}')
    expect(screen.getByRole('button', { name: 'First' })).toHaveFocus()

    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(screen.getByRole('button', { name: 'Last' })).toHaveFocus()
  })

  it('restores focus to opener when the active surface closes', () => {
    const { rerender } = render(
      <>
        <button type="button">Open dialog</button>
        <RestoreFocusProbe open={false} />
      </>,
    )

    screen.getByRole('button', { name: 'Open dialog' }).focus()

    rerender(
      <>
        <button type="button">Open dialog</button>
        <RestoreFocusProbe open={true} />
      </>,
    )

    screen.getByRole('button', { name: 'Dialog action' }).focus()

    rerender(
      <>
        <button type="button">Open dialog</button>
        <RestoreFocusProbe open={false} />
      </>,
    )

    expect(screen.getByRole('button', { name: 'Open dialog' })).toHaveFocus()
  })
})
